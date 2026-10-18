using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// How far a connection has got (TC SessionStatus), in order: a handler runs once the session has reached
/// its <see cref="HandlesAttribute.Status"/> (a minimum, not an exact match). Login connections go
/// Connected → Authed (Login), game connections Connected → InWorld (Join). Transferring (§6.2, step 13)
/// does not fit this order and has its own rule in the gate (<see cref="AlbionSession.OnRequest"/>): a
/// transferring session runs no handler at all (TC keeps STATUS_TRANSFER apart too).
/// </summary>
public enum SessionStatus : byte
{
    /// <summary>A new connection: status queries, Login, Join.</summary>
    Connected,

    /// <summary>Login connection after Login (character list: select, create, delete).</summary>
    Authed,

    /// <summary>Game connection after Join: the player exists.</summary>
    InWorld,

    /// <summary>
    /// Game connection after a committed cluster change (SPEC stage 3 §4.1), for good: the player has left the world and
    /// the character is saved in the target cluster; the client drops this connection itself and joins on a new one.
    /// Every request is dropped quietly, nothing is sent, the server never disconnects it, and its end saves nothing.
    /// </summary>
    Transferring,
}

/// <summary>
/// Marks a session method as the handler of one operation. It takes the request class the serializer
/// parses that operation into (<see cref="AlbionSerializer.RequestTypes"/>; an operation without one gets
/// AlbionRequest, always null) and returns a response, or null for "no response".
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlesAttribute(OperationCodes code) : Attribute
{
    public OperationCodes Code { get; } = code;

    /// <summary>Status the session must have reached; a request that comes earlier is logged and dropped.</summary>
    public SessionStatus Status { get; set; }

    /// <summary>Frequent operation: the request is logged at trace level instead of info/debug.</summary>
    public bool Quiet { get; set; }
}

/// <summary>
/// The handlers of one session type (TC opcodeTable): its [Handles] methods, found once and compiled to
/// delegates. Building checks the bindings and throws <see cref="InvalidOperationException"/> on a conflict
/// (a handler that does not take its operation's request class, or two handlers for one operation);
/// <see cref="ServerHost"/> builds both tables before it starts, so such a mistake stops the server there
/// (ARCHITECTURE.md principle 6). The table also counts operations that arrive without a handler, those answered
/// from content/opcode-stubs.json apart.
/// </summary>
public sealed class OpcodeTable
{
    /// <summary>A bound handler: <see cref="Invoke"/> calls the method on the session with the parsed request.</summary>
    public sealed record Handler(OperationCodes Code, SessionStatus Status, bool Quiet, string Method,
        Func<AlbionSession, AlbionRequest?, AlbionResponse?> Invoke);

    private static readonly ConcurrentDictionary<Type, OpcodeTable> Tables = new();

    private readonly Dictionary<OperationCodes, Handler> _handlers = new();
    private readonly ConcurrentDictionary<OperationCodes, long> _unhandled = new();
    private readonly ConcurrentDictionary<OperationCodes, long> _stubbed = new();

    /// <summary>The table of a session type, built and checked on first use; one per type for the whole process.</summary>
    public static OpcodeTable For(Type sessionType) => Tables.GetOrAdd(sessionType, static t => new OpcodeTable(t));

    /// <summary>
    /// The startup check (<see cref="ServerHost"/>): builds the tables of <paramref name="sessionTypes"/> and
    /// refuses a [Handles] method on any other class of their assemblies, which would never be bound (its
    /// operation would quietly get the empty default response).
    /// </summary>
    public static void Build(params Type[] sessionTypes)
    {
        foreach (var t in sessionTypes) For(t);
        foreach (var t in sessionTypes.Select(s => s.Assembly).Distinct().SelectMany(a => a.GetTypes()).Except(sessionTypes))
            foreach (var m in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.IsDefined(typeof(HandlesAttribute)))
                    throw new InvalidOperationException(
                        $"{t.Name}.{m.Name} has [Handles] but {t.Name} is not a session with a handler table ({string.Join(", ", sessionTypes.Select(s => s.Name))})");
    }

    private OpcodeTable(Type sessionType)
    {
        foreach (var m in sessionType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetCustomAttribute<HandlesAttribute>() is not { } h) continue;
            var method = $"{sessionType.Name}.{m.Name}";
            if (m.IsStatic)
                throw new InvalidOperationException($"{method} handles {h.Code} but is static; a handler is an instance method of the session");
            var request = AlbionSerializer.RequestTypes.GetValueOrDefault(h.Code) ?? typeof(AlbionRequest);
            if (m.GetParameters() is not [{ ParameterType: var taken }] || taken != request)
                throw new InvalidOperationException(
                    $"{method} handles {h.Code} but takes ({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}); " +
                    $"it must take one {request.Name}, the class op {(short)h.Code} is parsed into");
            if (!typeof(AlbionResponse).IsAssignableFrom(m.ReturnType))
                throw new InvalidOperationException($"{method} handles {h.Code} but returns {m.ReturnType.Name}, not an AlbionResponse");
            if (_handlers.TryGetValue(h.Code, out var other))
                throw new InvalidOperationException($"{h.Code} has two handlers in {sessionType.Name}: {other.Method} and {method}");
            _handlers[h.Code] = new Handler(h.Code, h.Status, h.Quiet, method, Compile(sessionType, m, request));
        }
    }

    /// <summary>(session, request) => ((TSession)session).M((TRequest)request), compiled once instead of a MethodInfo.Invoke per request.</summary>
    private static Func<AlbionSession, AlbionRequest?, AlbionResponse?> Compile(Type sessionType, MethodInfo m, Type requestType)
    {
        var session = Expression.Parameter(typeof(AlbionSession), "session");
        var request = Expression.Parameter(typeof(AlbionRequest), "request");
        var call = Expression.Call(Expression.Convert(session, sessionType), m, Expression.Convert(request, requestType));
        return Expression.Lambda<Func<AlbionSession, AlbionRequest?, AlbionResponse?>>(
            Expression.Convert(call, typeof(AlbionResponse)), session, request).Compile();
    }

    public IReadOnlyCollection<Handler> Handlers => _handlers.Values;

    /// <summary>The handler of <paramref name="code"/>; null when the session type has none.</summary>
    public Handler? Find(OperationCodes code) => _handlers.GetValueOrDefault(code);

    /// <summary>Requests that came without a handler since the server started, per operation (for ".ops unhandled").</summary>
    public IReadOnlyDictionary<OperationCodes, long> Unhandled => _unhandled;

    internal void CountUnhandled(OperationCodes code) => _unhandled.AddOrUpdate(code, 1, static (_, n) => n + 1);

    /// <summary>Requests without a handler answered from content/opcode-stubs.json since the server started, per operation (".ops unhandled" lists them apart).</summary>
    public IReadOnlyDictionary<OperationCodes, long> Stubbed => _stubbed;

    internal void CountStubbed(OperationCodes code) => _stubbed.AddOrUpdate(code, 1, static (_, n) => n + 1);
}
