using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// The labourer journals (analysis/work/stageH/journals/SPEC.md §A, §E.3; PVE-DESIGN.md step 4): content/journals.json, as
/// content/dungeons.json. Whether journals in the inventory fill, and the journal merchant: standing NPC orders on the
/// marketplace of the listed cities (<see cref="Systems.JournalMerchant"/>), a sell order per _EMPTY journal at
/// <see cref="SellFactor"/> × its labourer price (items.xml craftingrequirements silver: T2 500 … T8 32000) and a buy order per
/// _FULL journal at <see cref="BuyBackFactor"/> × that price. There are no labourers here (islands), and no client window of a
/// city sells journals, so the market window, which the client already has, does it (our choice; the SPEC's buy-back by the
/// labourer loot value is not used: items.xml values the loot of a resource journal at about a tenth of its price). A key the
/// file leaves out keeps the built-in value (<see cref="Default"/>, the shipped file's), a missing file or one that does not
/// parse gives the built-in values with a log line. Immutable.
/// </summary>
public sealed record JournalSettings
{
    public static JournalSettings Default { get; } = new();

    /// <summary>Whether journals in the inventory fill with the fame of their kind (SPEC §A.3).</summary>
    public bool Fill { get; init; } = true;

    /// <summary>The clusters whose marketplace has the merchant's orders (0000 = Thetford); empty = no merchant.</summary>
    public IReadOnlySet<string> MerchantMarkets { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0000" };

    /// <summary>The merchant's price of an _EMPTY journal, × the journal's labourer price (1: the official price).</summary>
    public double SellFactor { get; init; } = 1;

    /// <summary>What the merchant pays for a _FULL journal, × the journal's labourer price (1.5: half the price on top); 0 = it buys none.</summary>
    public double BuyBackFactor { get; init; } = 1.5;

    /// <summary>The amount each merchant order shows (it never runs out; one trade takes at most this many).</summary>
    public int MerchantStock { get; init; } = 999;

    /// <summary>The lowest journal tier the merchant trades.</summary>
    public int MinTier { get; init; } = 2;

    /// <summary>The highest journal tier the merchant trades.</summary>
    public int MaxTier { get; init; } = 8;

    public static JournalSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in journal settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", string.Create(CultureInfo.InvariantCulture,
                $"journals: fill {(s.Fill ? "on" : "off")}, merchant in {(s.MerchantMarkets.Count == 0 ? "no city" : string.Join("/", s.MerchantMarkets))} (sells x{s.SellFactor}, buys back x{s.BuyBackFactor})"));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in journal settings used");
            return Default;
        }
    }

    /// <summary>The file: Fill, MerchantMarkets [cluster ids], SellFactor, BuyBackFactor, MerchantStock, MinTier, MaxTier.</summary>
    public static JournalSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        double Num(JsonElement v, string key, double min, double max) =>
            v.TryGetDouble(out var d) && d >= min && d <= max ? d : throw new InvalidDataException($"{key}: {v} is not {min}..{max}");
        int Int(JsonElement v, string key, int min, int max) =>
            v.TryGetInt32(out var i) && i >= min && i <= max ? i : throw new InvalidDataException($"{key}: {v} is not {min}..{max}");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "Fill":
                    s = s with { Fill = p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.Value.GetBoolean() : throw new InvalidDataException($"{p.Name}: not true or false") };
                    break;
                case "MerchantMarkets":
                    s = s with
                    {
                        MerchantMarkets = p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
                            : throw new InvalidDataException($"{p.Name}: not a list of cluster ids"),
                    };
                    break;
                case "SellFactor": s = s with { SellFactor = Num(p.Value, p.Name, 0.01, 1000) }; break;
                case "BuyBackFactor": s = s with { BuyBackFactor = Num(p.Value, p.Name, 0, 1000) }; break;
                case "MerchantStock": s = s with { MerchantStock = Int(p.Value, p.Name, 1, short.MaxValue) }; break;
                case "MinTier": s = s with { MinTier = Int(p.Value, p.Name, 1, 8) }; break;
                case "MaxTier": s = s with { MaxTier = Int(p.Value, p.Name, 1, 8) }; break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s;
    }
}
