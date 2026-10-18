using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Content;

/// <summary>
/// Side requests answered without a handler (content/opcode-stubs.json, ARCHITECTURE.md §5.2, §6.2): operations whose
/// client accepts any well-formed answer (season tracker, arena rankings, vanity, gold market, islands) or a fixed
/// refusal (BuyPremium), and the two answers the official server gives that are not a filled response class: none at
/// all ("silent") and a bare success ("ack"); plus "ignore" for the client's telemetry and other requests that nobody
/// answers (no response class, so the unhandled default sends nothing either: only its warning goes). <see cref="Network.AlbionSession"/> sends the stub's answer instead of the
/// plain empty response and without the "unhandled" warning, logs it at debug level and counts it apart
/// (<see cref="Network.OpcodeTable.Stubbed"/>).
/// The file: { "&lt;OperationCodes name&gt;": entry, … }, an entry being one of
/// <list type="bullet">
/// <item>{ "&lt;key or field&gt;": value, … } (<see cref="StubKind.Response"/>): the empty response of the operation's class
/// (<see cref="MessageDefaults.FillEmpty"/>: zeros, "" and empty arrays) with these field values over it. A field is named
/// by its parameter key ("1") or by its field name in the generated class (Messages.g.cs; the confirmed names come from
/// analysis/protocol-names.json); a value is JSON of the field's type: a number (range-checked), true/false, a string,
/// a Guid as a string, [x, y] for a Vector2, an array (nested for jagged arrays). GameTimeStamps are DateTime ticks.
/// "ReturnCode" (a short, default 0) sets the Photon return code, the operation's Rc value the client's handler knows;</item>
/// <item>"silent" (<see cref="StubKind.Silent"/>): no answer, for an operation that has a response class but which the
/// official server never answers (CastStart, RegisterToObject, …): the empty response of the unhandled default would be
/// an answer the client never gets officially;</item>
/// <item>"ack" (<see cref="StubKind.Ack"/>): the bare success {253: op, 255: request id} with return code 0, for an
/// operation without a response class that the official server answers so (GetCharacterStats, …), which the unhandled
/// default leaves unanswered; "ack-no-request-id": the same without key 255 even when the request had one, as official
/// for PartyFinderUnregisterApplicants;</item>
/// <item>"ignore" (<see cref="StubKind.Ignore"/>): no answer, for an operation without a response class that needs none
/// (ExtendedHardwareStats, UseFunction, …; ops-audit SPEC §6): the same silence as the unhandled default, without its
/// warning.</item>
/// </list>
/// Unlike the other content files a mistake here stops the start (<see cref="InvalidDataException"/>, every problem
/// listed): an unknown operation (with the closest name) or kind, one a session handles in code, fields or "silent" for
/// an operation without a response class, "ack" or "ignore" for one with a class, an unknown key or field, a field twice, a value
/// that does not fit its field. A missing file = no stubs, with a warning.
/// </summary>
public sealed class OpcodeStubs
{
    /// <summary>What a stubbed operation gets (the file's entry forms).</summary>
    public enum StubKind
    {
        /// <summary>The empty response of its class with the entry's field values (an object in the file).</summary>
        Response,

        /// <summary>No answer at all ("silent").</summary>
        Silent,

        /// <summary>A bare rc-0 answer without a body ("ack", "ack-no-request-id").</summary>
        Ack,

        /// <summary>No answer to an operation without a response class ("ignore"): telemetry and the like.</summary>
        Ignore,
    }

    /// <summary>
    /// The answer of a stubbed operation: <see cref="Response"/> null = none (<see cref="StubKind.Silent"/>,
    /// <see cref="StubKind.Ignore"/>); else the
    /// response to send, with the request's id when <see cref="EchoRequestId"/> (key 255; the request may have none).
    /// </summary>
    public sealed record StubAnswer(StubKind Kind, AlbionResponse? Response, bool EchoRequestId);

    /// <summary>One operation: its kind, its response class and the converted field values, in file order.</summary>
    private sealed record Stub(StubKind Kind, Type? Response, (FieldInfo Field, object Value)[] Values, bool EchoRequestId);

    private readonly Dictionary<OperationCodes, Stub> _stubs;

    private OpcodeStubs(Dictionary<OperationCodes, Stub> stubs) => _stubs = stubs;

    /// <summary>No stubs: every unhandled operation gets the empty response with a warning (sessions built without a host).</summary>
    public static OpcodeStubs None { get; } = new([]);

    /// <summary>The stubbed operations, of every kind.</summary>
    public IReadOnlyCollection<OperationCodes> Codes => _stubs.Keys;

    /// <summary>The kind of stub <paramref name="code"/> has; null = not stubbed.</summary>
    public StubKind? KindOf(OperationCodes code) => _stubs.TryGetValue(code, out var stub) ? stub.Kind : null;

    /// <summary>
    /// The answer for <paramref name="code"/>, a new response object each time (for a filled response the empty one with
    /// the configured values, arrays copied, so a response never shares one with the next); null = not stubbed.
    /// </summary>
    public StubAnswer? Respond(OperationCodes code)
    {
        if (!_stubs.TryGetValue(code, out var stub)) return null;
        switch (stub.Kind)
        {
            case StubKind.Silent or StubKind.Ignore: return new StubAnswer(stub.Kind, null, false);
            case StubKind.Ack: return new StubAnswer(StubKind.Ack, new GenericResponse(), stub.EchoRequestId);
        }
        var resp = ((AlbionResponse)Activator.CreateInstance(stub.Response!)!).FillEmpty();
        foreach (var (field, value) in stub.Values) field.SetValue(resp, value is Array a ? Copy(a) : value);
        return new StubAnswer(StubKind.Response, resp, true);
    }

    /// <summary>The file at <paramref name="path"/>, checked as described above; the stubbed operations are logged.</summary>
    /// <param name="handlerOf">The handler method bound to an operation in either session (OpcodeTable), null = none;
    /// a stub for a handled operation would never be sent, so it stops the start.</param>
    public static OpcodeStubs Load(string path, Func<OperationCodes, string?> handlerOf)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: no opcode stubs, unhandled operations get the empty response");
            return None;
        }
        var stubs = Parse(File.ReadAllText(path), path, handlerOf);
        Log.Info("content", $"opcode stubs: {stubs._stubs.Count} operation(s): " + string.Join(", ", stubs._stubs.Select(s => s.Value.Kind switch
        {
            StubKind.Silent => $"{s.Key} (silent)",
            StubKind.Ack => $"{s.Key} (ack)",
            StubKind.Ignore => $"{s.Key} (ignore)",
            _ => $"{s.Key}",
        })));
        return stubs;
    }

    /// <summary>The file's text (<paramref name="source"/> names it in the errors); throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static OpcodeStubs Parse(string text, string source, Func<OperationCodes, string?> handlerOf)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{source}: {e.Message}");
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source}: expected an object of operations");
            var errors = new List<string>();
            var stubs = new Dictionary<OperationCodes, Stub>();
            var seen = new HashSet<OperationCodes>();
            foreach (var op in doc.RootElement.EnumerateObject())
            {
                if (Operation(op.Name, errors) is not { } code) continue;
                if (!seen.Add(code))
                {
                    errors.Add($"{op.Name} is listed twice");
                    continue;
                }
                if (handlerOf(code) is { } handler)
                {
                    errors.Add($"{op.Name} is handled by {handler}: the stub would never be sent, remove it");
                    continue;
                }
                var hasClass = AlbionSerializer.ResponseTypes.TryGetValue(code, out var type);
                if (op.Value.ValueKind == JsonValueKind.String)
                {
                    var kind = op.Value.GetString() switch
                    {
                        Silent => StubKind.Silent,
                        Ack or AckNoRequestId => StubKind.Ack,
                        IgnoreKind => StubKind.Ignore,
                        _ => (StubKind?)null,
                    };
                    if (kind is null)
                        errors.Add($"{op.Name}: unknown kind \"{op.Value.GetString()}\" (an object of fields, \"{Silent}\", \"{Ack}\", \"{AckNoRequestId}\" or \"{IgnoreKind}\")");
                    else if (kind == StubKind.Silent && !hasClass)
                        errors.Add($"{op.Name} has no response class: \"{Silent}\" is for one that has, \"{IgnoreKind}\" leaves it unanswered without the warning");
                    else if (kind is StubKind.Ack or StubKind.Ignore && hasClass)
                        errors.Add($"{op.Name} has the response class {type!.Name}: " + (kind == StubKind.Ignore
                            ? $"\"{Silent}\" for no answer, not \"{IgnoreKind}\""
                            : $"{{}} answers with its empty response, not \"{op.Value.GetString()}\""));
                    else stubs[code] = new Stub(kind.Value, null, [], op.Value.GetString() == Ack);
                    continue;
                }
                if (!hasClass)
                {
                    errors.Add($"{op.Name} has no response class, so no fields: \"{Ack}\" for the bare rc-0 answer, \"{IgnoreKind}\" for none");
                    continue;
                }
                if (op.Value.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{op.Name}: expected an object of fields ({{}} = the empty response), \"{Silent}\" or \"{Ack}\"");
                    continue;
                }
                if (Values(op.Name, type!, op.Value, errors) is { } values) stubs[code] = new Stub(StubKind.Response, type, values, true);
            }
            if (errors.Count > 0)
                throw new InvalidDataException($"{source}: {errors.Count} problem(s):\n  " + string.Join("\n  ", errors));
            return new OpcodeStubs(stubs);
        }
    }

    /// <summary>The entry strings of <see cref="StubKind.Silent"/>, <see cref="StubKind.Ack"/> and <see cref="StubKind.Ignore"/>.</summary>
    private const string Silent = "silent", Ack = "ack", AckNoRequestId = "ack-no-request-id", IgnoreKind = "ignore";

    /// <summary>The OperationCodes names (exact spelling; no numbers or "A, B" combinations, which Enum.TryParse would take).</summary>
    private static readonly Dictionary<string, OperationCodes> Operations =
        Enum.GetNames<OperationCodes>().ToDictionary(n => n, n => Enum.Parse<OperationCodes>(n), StringComparer.Ordinal);

    /// <summary>The operation called <paramref name="name"/>; an unknown name is reported with the closest one.</summary>
    private static OperationCodes? Operation(string name, List<string> errors)
    {
        if (Operations.TryGetValue(name, out var code)) return code;
        var closest = Operations.Keys.MinBy(n => ItemStore.Edits(name.ToUpperInvariant(), n.ToUpperInvariant()));
        errors.Add($"unknown operation '{name}' (closest: {closest})");
        return null;
    }

    /// <summary>The Photon return code (<see cref="AlbionResponse.ReturnCode"/>), set by its name "ReturnCode" in any entry.</summary>
    private static readonly FieldInfo ReturnCode = typeof(AlbionResponse).GetField(nameof(AlbionResponse.ReturnCode))!;

    /// <summary>The entry's fields of <paramref name="type"/> with converted values; null when one of them is wrong (reported).</summary>
    private static (FieldInfo, object)[]? Values(string op, Type type, JsonElement entry, List<string> errors)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => (Field: f, Param: f.GetCustomAttribute<ParamAttribute>()))
            .Where(x => x.Param is not null)
            .Select(x => (x.Field, x.Param!.Key))
            .OrderBy(x => x.Key)
            .ToList();
        var values = new List<(FieldInfo, object)>();
        var ok = true;
        foreach (var p in entry.EnumerateObject())
        {
            var field = p.Name == ReturnCode.Name ? ReturnCode
                : byte.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var key)
                ? fields.FirstOrDefault(f => f.Key == key).Field
                : fields.FirstOrDefault(f => f.Field.Name == p.Name).Field;
            if (field is null)
            {
                errors.Add($"{op}: no field '{p.Name}' in {type.Name} (fields: {ReturnCode.Name}, {string.Join(", ", fields.Select(f => $"{f.Key} {f.Field.Name}"))})");
                ok = false;
                continue;
            }
            var where = field == ReturnCode ? $"{op}.{ReturnCode.Name} (short)"
                : $"{op}.{field.Name} (key {fields.First(f => f.Field == field).Key}, {TypeName(field.FieldType)})";
            if (values.Any(v => v.Item1 == field))
            {
                errors.Add($"{where}: set twice");
                ok = false;
                continue;
            }
            try
            {
                values.Add((field, Convert(p.Value, field.FieldType)));
            }
            catch (FormatException e)
            {
                errors.Add($"{where}: {e.Message}");
                ok = false;
            }
        }
        return ok ? [.. values] : null;
    }

    /// <summary>A JSON value as <paramref name="type"/>; throws <see cref="FormatException"/> when it does not fit.</summary>
    private static object Convert(JsonElement e, Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (e.ValueKind == JsonValueKind.Null) throw new FormatException("null is no value; leave the field out for the empty one");
        if (t == typeof(string))
            return e.ValueKind == JsonValueKind.String ? e.GetString()! : throw Expected("a string", e);
        if (t == typeof(bool))
            return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw Expected("true or false", e);
        if (t == typeof(Guid))
            return e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var g) ? g : throw Expected("a Guid string", e);
        if (t == typeof(float) || t == typeof(double))
        {
            if (e.ValueKind != JsonValueKind.Number) throw Expected("a number", e);
            var d = e.GetDouble();
            return t == typeof(double) ? d : float.IsFinite((float)d) ? (float)d : throw Expected("a float", e);
        }
        if (Integers.TryGetValue(t, out var range))
            return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var l) && l >= range.Min && l <= range.Max
                ? System.Convert.ChangeType(l, t, CultureInfo.InvariantCulture)
                : throw Expected($"an integer {range.Min}..{range.Max}", e);
        if (t == typeof(Vector2))
            return e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2
                   && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number
                ? new Vector2(e[0].GetSingle(), e[1].GetSingle()) : throw Expected("[x, y]", e);
        if (t.IsArray)
        {
            if (e.ValueKind != JsonValueKind.Array) throw Expected("an array", e);
            var element = t.GetElementType()!;
            var array = Array.CreateInstance(element, e.GetArrayLength());
            var i = 0;
            foreach (var item in e.EnumerateArray())
            {
                try { array.SetValue(Convert(item, element), i); }
                catch (FormatException inner) { throw new FormatException($"[{i}]: {inner.Message}"); }
                i++;
            }
            return array;
        }
        throw new FormatException($"{TypeName(type)} fields cannot be stubbed");
    }

    private static readonly Dictionary<Type, (long Min, long Max)> Integers = new()
    {
        [typeof(byte)] = (byte.MinValue, byte.MaxValue), [typeof(sbyte)] = (sbyte.MinValue, sbyte.MaxValue),
        [typeof(short)] = (short.MinValue, short.MaxValue), [typeof(ushort)] = (ushort.MinValue, ushort.MaxValue),
        [typeof(int)] = (int.MinValue, int.MaxValue), [typeof(uint)] = (uint.MinValue, uint.MaxValue),
        [typeof(long)] = (long.MinValue, long.MaxValue),
    };

    private static FormatException Expected(string what, JsonElement got) =>
        new($"expected {what}, got {(got.ValueKind == JsonValueKind.String ? $"\"{got.GetString()}\"" : got.GetRawText())}");

    /// <summary>C# spelling of a field type for the errors (int[][], Dictionary`2 as its name).</summary>
    private static string TypeName(Type t) =>
        t.IsArray ? TypeName(t.GetElementType()!) + "[]"
        : Nullable.GetUnderlyingType(t) is { } u ? TypeName(u) + "?"
        : t.Name switch
        {
            "Byte" => "byte", "SByte" => "sbyte", "Int16" => "short", "UInt16" => "ushort", "Int32" => "int", "UInt32" => "uint",
            "Int64" => "long", "Single" => "float", "Double" => "double", "Boolean" => "bool", "String" => "string", "Object" => "object",
            _ => t.Name,
        };

    /// <summary>A copy of <paramref name="a"/> down to the leaves (jagged arrays copied row by row).</summary>
    private static Array Copy(Array a)
    {
        var copy = (Array)a.Clone();
        if (a.GetType().GetElementType()!.IsArray)
            for (var i = 0; i < copy.Length; i++)
                if (copy.GetValue(i) is Array row) copy.SetValue(Copy(row), i);
        return copy;
    }
}
