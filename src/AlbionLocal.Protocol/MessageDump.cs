using System.Reflection;
using System.Text;

namespace AlbionLocal.Server.Protocol;

/// <summary>
/// Readable one-field-per-line text of an Albion message or raw Photon parameters, for logs and tools. The secret keys of
/// a request (<see cref="SecretKeys"/>: passwords, device and hardware ids, the client's address) never appear: they read
/// "&lt;redacted len N&gt;" at any log level.
/// </summary>
public static class MessageDump
{
    /// <summary>
    /// Request keys never shown (analysis/work/accounts, server spec §1): Login (op 5) 5 the client's public IP, 7 the
    /// password (plain text), 9 the device id, 13 the hardware JSON; CreateAccount (op 4) 7 the password, 9 the device id,
    /// 11 the hardware JSON.
    /// </summary>
    public static IReadOnlySet<byte> SecretKeys(OperationCodes code) => code switch
    {
        OperationCodes.Login => LoginSecrets,
        OperationCodes.CreateAccount => CreateAccountSecrets,
        _ => NoSecrets,
    };

    private static readonly HashSet<byte> LoginSecrets = [5, 7, 9, 13], CreateAccountSecrets = [7, 9, 11], NoSecrets = [];

    /// <summary>Class name, then every set [Param] field as "[key] name = value" (null and default values left out).</summary>
    public static string Describe(object msg)
    {
        var secrets = msg is AlbionRequest && msg.GetType().GetCustomAttribute<AlbionOperationAttribute>() is { } op ? SecretKeys(op.Code) : NoSecrets;
        var sb = new StringBuilder(msg.GetType().Name);
        foreach (var f in msg.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.GetCustomAttribute<ParamAttribute>() is not { } p) continue;
            var v = f.GetValue(msg);
            if (v is null || (v is ValueType && v.Equals(Activator.CreateInstance(v.GetType())))) continue;
            sb.Append($"\n    [{p.Key}] {f.Name} = {(secrets.Contains(p.Key) ? Redacted(v) : Format(v))}");
        }
        return sb.ToString();
    }

    /// <summary>Raw request parameters; the secret keys of the operation in key 253 are redacted.</summary>
    public static string Describe(Dictionary<byte, object?> parameters)
    {
        var secrets = SecretsOf(parameters);
        return string.Join("\n", parameters.OrderBy(kv => kv.Key).Select(kv => $"    [{kv.Key}] = {(secrets.Contains(kv.Key) ? Redacted(kv.Value) : Format(kv.Value))}"));
    }

    /// <summary>The secret keys of a raw request (its Albion op code in key 253); none when it has no code.</summary>
    public static IReadOnlySet<byte> SecretsOf(IReadOnlyDictionary<byte, object?> parameters)
    {
        if (parameters.GetValueOrDefault(ReservedKeys.OperationCode) is not { } c) return NoSecrets;
        try { return SecretKeys((OperationCodes)Convert.ToInt16(c)); }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException) { return NoSecrets; }
    }

    /// <summary>What a secret value shows: only its length (characters of a string, entries of an array).</summary>
    public static string Redacted(object? v) => v switch
    {
        null => "null",
        string s => $"<redacted len {s.Length}>",
        Array a => $"<redacted len {a.Length}>",
        _ => "<redacted>",
    };

    /// <summary>A value as the logs show it: strings quoted, byte[] as hex (32 bytes), arrays up to 16 entries.</summary>
    public static string Format(object? v) => v switch
    {
        null => "null",
        string s => $"\"{s}\"",
        byte[] b => $"byte[{b.Length}] {Convert.ToHexString(b.AsSpan(0, Math.Min(b.Length, 32)))}",
        System.Collections.IDictionary d => $"{{{string.Join(", ", d.Keys.Cast<object>().Select(k => $"{k}: {Format(d[k])}"))}}}",
        Array a => $"[{string.Join(", ", a.Cast<object?>().Take(16).Select(Format))}{(a.Length > 16 ? ", …" : "")}]",
        _ => v.ToString() ?? "",
    };
}
