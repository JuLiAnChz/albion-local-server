// Offline decoder: pcap of Albion client traffic -> transcript.jsonl in the format that
// analysis/tools/capdump.py reads (type, t, flow, dir, bytes, params, op_code/event_code, ...).
// Usage: dotnet run --project tools/AlbionLocal.CapDecode -- <in.pcap> [out.transcript.jsonl]
// Only the classic pcap format (µs or ns timestamps) with Ethernet/IPv4/UDP is read.
// Payloads encrypted with the session key (DH/AES) cannot be read and are marked "encrypted".
using System.Buffers.Binary;
using System.Collections;
using System.Text;
using System.Text.Json;
using AlbionLocal.Photon.Enet;
using AlbionLocal.Photon.Serialization;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: CapDecode <in.pcap> [out.transcript.jsonl]");
    return 1;
}
var input = args[0];
var output = args.Length > 1 ? args[1] : Path.ChangeExtension(input, null) + ".transcript.jsonl";
var decoder = new Decoder();
using (var outFile = new StreamWriter(output, false, new UTF8Encoding(false)))
{
    foreach (var (t, src, dst, payload) in Pcap.UdpPackets(input))
        foreach (var rec in decoder.Datagram(t, src, dst, payload))
            outFile.WriteLine(JsonSerializer.Serialize(rec));
}
Console.WriteLine($"{decoder.Records} records, {decoder.Flows.Count} flows -> {output}");
foreach (var f in decoder.Flows) Console.WriteLine($"  {f}");
return 0;

/// <summary>Classic libpcap reader (Ethernet, optional 802.1Q, IPv4, UDP; no IP reassembly).</summary>
static class Pcap
{
    public static IEnumerable<(double Time, string Src, string Dst, byte[] Payload)> UdpPackets(string path)
    {
        var data = File.ReadAllBytes(path);
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var nanos = magic switch
        {
            0xA1B2C3D4 => false,
            0xA1B23C4D => true,
            _ => throw new InvalidDataException($"not a little-endian classic pcap (magic {magic:x8}); pcapng is not supported"),
        };
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20)) != 1)
            throw new InvalidDataException("link type is not Ethernet");
        var pos = 24;
        while (pos + 16 <= data.Length)
        {
            var sec = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            var frac = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 8));
            pos += 16;
            if (pos + len > data.Length) yield break;
            var frame = data.AsSpan(pos, len);
            pos += len;
            var t = sec + frac / (nanos ? 1e9 : 1e6);
            if (Udp(frame) is { } u) yield return (t, u.Src, u.Dst, u.Payload);
        }
    }

    private static (string Src, string Dst, byte[] Payload)? Udp(ReadOnlySpan<byte> f)
    {
        if (f.Length < 14) return null;
        var eth = 14;
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(f[12..]);
        if (etherType == 0x8100 && f.Length >= 18)
        {
            etherType = BinaryPrimitives.ReadUInt16BigEndian(f[16..]);
            eth = 18;
        }
        if (etherType != 0x0800 || f.Length < eth + 20) return null;
        var ip = f[eth..];
        var ihl = (ip[0] & 0x0F) * 4;
        if (ip[9] != 17 || (BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x3FFF) != 0) return null; // UDP, unfragmented
        var total = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
        if (ip.Length < Math.Min(total, ip.Length) || ip.Length < ihl + 8) return null;
        var udp = ip[ihl..];
        var src = $"{ip[12]}.{ip[13]}.{ip[14]}.{ip[15]}:{BinaryPrimitives.ReadUInt16BigEndian(udp)}";
        var dst = $"{ip[16]}.{ip[17]}.{ip[18]}.{ip[19]}:{BinaryPrimitives.ReadUInt16BigEndian(udp[2..])}";
        var udpLen = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(udp[4..]), udp.Length);
        return (src, dst, udp[8..udpLen].ToArray());
    }
}

/// <summary>ENet/Photon decoding per flow and direction (same framing as AlbionLocal.Photon).</summary>
sealed class Decoder
{
    private static readonly int[] ServerPorts = [5055, 5056, 5057, 5058, 5059, 5060, 5061];
    private readonly Dictionary<string, Side> _sides = new();
    private readonly Dictionary<string, string> _apps = new();

    public int Records { get; private set; }
    public List<string> Flows { get; } = [];

    private sealed class Side
    {
        public readonly HashSet<(byte, int)> SeenReliable = [];
        public readonly Dictionary<(byte, int), (byte[] Data, bool[] Have, int Received)> Fragments = new();
    }

    public IEnumerable<Dictionary<string, object?>> Datagram(double t, string src, string dst, byte[] d)
    {
        var srcPort = int.Parse(src[(src.LastIndexOf(':') + 1)..]);
        var dstPort = int.Parse(dst[(dst.LastIndexOf(':') + 1)..]);
        string flow, dir;
        if (ServerPorts.Contains(dstPort)) (flow, dir) = ($"{src}->{dst}", "c2s");
        else if (ServerPorts.Contains(srcPort)) (flow, dir) = ($"{dst}->{src}", "s2c");
        else yield break;
        if (!Flows.Contains(flow)) Flows.Add(flow);
        if (!_sides.TryGetValue(flow + dir, out var side)) _sides[flow + dir] = side = new Side();
        Dictionary<string, object?> Rec(string type) { Records++; return new() { ["type"] = type, ["t"] = Math.Round(t, 4), ["flow"] = flow, ["dir"] = dir }; }

        if (d.Length < 12) yield break;
        var r = new ByteReader(d);
        r.UInt16BE();
        var flags = r.Byte();
        var count = r.Byte();
        r.Int32BE(); // sent time
        r.Int32BE(); // challenge
        if (flags == 1)
        {
            yield return Rec("enet_encrypted_datagram");
            yield break;
        }
        if (flags == 0xCC) r.Int32BE(); // CRC
        for (var i = 0; i < count && r.Remaining > 0; i++)
        {
            NCommand cmd;
            try { cmd = NCommand.Parse(r); }
            catch { yield break; }
            byte[]? message = null;
            switch (cmd.Type)
            {
                case CommandType.Connect: yield return Rec("connect"); break;
                case CommandType.VerifyConnect: yield return Rec("verify_connect"); break;
                case CommandType.Disconnect: yield return Rec("disconnect"); break;
                case CommandType.Ping: yield return Rec("ping"); break;
                case CommandType.ServerTime: yield return Rec("server_time"); break;
                case CommandType.SendReliable:
                    if (side.SeenReliable.Add((cmd.ChannelId, cmd.ReliableSequenceNumber))) message = cmd.Payload;
                    break;
                case CommandType.SendUnreliable:
                case CommandType.SendUnsequenced:
                case CommandType.SendReliableUnsequenced:
                    message = cmd.Payload;
                    break;
                case CommandType.SendFragment:
                case CommandType.SendFragmentUnsequenced:
                    {
                        var key = (cmd.ChannelId, cmd.StartSequenceNumber);
                        if (!side.Fragments.TryGetValue(key, out var asm))
                        {
                            if (cmd.TotalLength <= 0 || cmd.FragmentCount <= 0) break;
                            asm = (new byte[cmd.TotalLength], new bool[cmd.FragmentCount], 0);
                        }
                        if (cmd.FragmentNumber < asm.Have.Length && !asm.Have[cmd.FragmentNumber]
                            && cmd.FragmentOffset + cmd.Payload.Length <= asm.Data.Length)
                        {
                            asm.Have[cmd.FragmentNumber] = true;
                            cmd.Payload.CopyTo(asm.Data, cmd.FragmentOffset);
                            asm.Received++;
                        }
                        if (asm.Received == asm.Have.Length)
                        {
                            side.Fragments.Remove(key);
                            if (side.SeenReliable.Add((cmd.ChannelId, -cmd.StartSequenceNumber))) message = asm.Data;
                        }
                        else side.Fragments[key] = asm;
                        break;
                    }
            }
            if (message is null) continue;
            var rec = Rec("?");
            rec["ch"] = cmd.ChannelId;
            Message(rec, message, flow);
            yield return rec;
        }
    }

    private void Message(Dictionary<string, object?> rec, byte[] m, string flow)
    {
        rec["bytes"] = m.Length;
        if (m.Length < 2 || (m[0] != 0xF3 && m[0] != 0xFD))
        {
            rec["type"] = "raw";
            return;
        }
        var type = m[1] & 0x7F;
        if ((m[1] & 0x80) != 0)
        {
            rec["type"] = type switch { 2 => "Operation", 3 => "OperationResponse", 4 => "Event", _ => $"message{type}" };
            rec["encrypted"] = true;
            return;
        }
        var r = new ByteReader(m, 2);
        try
        {
            switch (type)
            {
                case 0:
                    rec["type"] = "Init";
                    if (m.Length >= 41) rec["appid"] = _apps[flow] = Encoding.ASCII.GetString(m, 9, 32).TrimEnd('\0');
                    break;
                case 1:
                    rec["type"] = "InitResponse";
                    break;
                case 2 or 6:
                    {
                        var q = Protocol18.ReadOperationRequest(r);
                        rec["type"] = type == 2 ? "Operation" : "InternalOperationRequest";
                        rec["op_code"] = q.Code;
                        rec["params"] = Table(q.Parameters, request: true);
                        break;
                    }
                case 3 or 7:
                    {
                        var s = Protocol18.ReadOperationResponse(r);
                        rec["type"] = type == 3 ? "OperationResponse" : "InternalOperationResponse";
                        rec["op_code"] = s.Code;
                        rec["rc"] = s.ReturnCode;
                        if (s.DebugMessage is { Length: > 0 } msg) rec["msg"] = msg;
                        rec["params"] = Table(s.Parameters);
                        break;
                    }
                case 4:
                    {
                        var e = Protocol18.ReadEventData(r);
                        rec["type"] = "Event";
                        rec["event_code"] = e.Code;
                        rec["params"] = Table(e.Parameters);
                        break;
                    }
                default:
                    rec["type"] = $"message{type}";
                    break;
            }
        }
        catch (Exception ex)
        {
            rec["type"] = $"undecodable{type}";
            rec["error"] = ex.Message;
        }
    }

    /// <summary>
    /// The parameters as JSON values; a request's secret keys (MessageDump.SecretKeys: the Login and CreateAccount
    /// passwords, device and hardware ids) only as "&lt;redacted len N&gt;".
    /// </summary>
    private static Dictionary<string, object?> Table(Dictionary<byte, object?> p, bool request = false)
    {
        var secrets = request ? AlbionLocal.Server.Protocol.MessageDump.SecretsOf(p) : new HashSet<byte>();
        return p.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(),
            kv => secrets.Contains(kv.Key) ? AlbionLocal.Server.Protocol.MessageDump.Redacted(kv.Value) : Json(kv.Value));
    }

    /// <summary>JSON-friendly values: byte[] as hex (the user's decoder does the same), floats as numbers.</summary>
    private static object? Json(object? v) => v switch
    {
        null => null,
        byte[] b => Convert.ToHexString(b).ToLowerInvariant(),
        string or bool => v,
        float f => float.IsFinite(f) ? (object)f : f.ToString(),
        double d => double.IsFinite(d) ? (object)d : d.ToString(),
        byte or sbyte or short or ushort or int or uint or long or ulong => v,
        Guid g => g.ToString("N"),
        IDictionary dict => dict.Keys.Cast<object>().ToDictionary(k => k?.ToString() ?? "null", k => Json(dict[k])),
        IEnumerable seq => seq.Cast<object?>().Select(Json).ToList(),
        _ => v.ToString(),
    };
}
