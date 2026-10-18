using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// One fishing zone kind of the server-only fishing.xml &lt;fishingzone&gt; (analysis/work/stageG/fishing/SPEC.md §7.1): the
/// charges, the prefab of NewFishingZoneObject key 4, the priority of the zone pick, the bite delay, the charge regrowth and
/// the loot. Capacities and prefabs are captured (15 / 5 / 1, FishingNodeFish/Swarm/Chest); everything else is an
/// [assume] tunable of content/fishing.json (the official values are server-only). <see cref="Loot"/>: "BASE" = the
/// cluster's base list (§7.2), "TREASURE" = T{tier}_FISHING_ZONE_TREASURE, "DEFAULT" = T{tier}_FISHING_ZONE_DEFAULT_{water}_LOOT,
/// anything else a loot.xml list name. <see cref="MaxCharges"/> 0 = unlimited (the default zones without an object).
/// </summary>
public sealed record FishingZoneSettings
{
    public int MaxCharges { get; init; } = 15;
    public string Prefab { get; init; } = "";
    public int Priority { get; init; }

    /// <summary>The bite delay, uniform in [min, max] seconds from the float's splash, ÷ (1 + the rod's fishingspeed) (SPEC §6 [assume]).</summary>
    public float BiteMin { get; init; } = 2;
    public float BiteMax { get; init; } = 25;

    /// <summary>One charge back per uniform(min, max) seconds (÷ Rates:Regrowth); at 0 charges first <see cref="DelayAtZero"/> (SPEC §6 [assume]).</summary>
    public float ChargeMin { get; init; } = 120;
    public float ChargeMax { get; init; } = 300;
    public float DelayAtZero { get; init; } = 90;

    /// <summary>fishing.xml &lt;charge chancetohit&gt;: the chance that a bite comes at all (else FishingCancel 13, caught nothing); 1 [assume].</summary>
    public float ChanceToHit { get; init; } = 1;

    public string Loot { get; init; } = "BASE";
}

/// <summary>
/// The fishing numbers neither the client data nor the captures settle (analysis/work/stageG/fishing/SPEC.md §4-§7):
/// content/fishing.json, as content/combat.json (ARCHITECTURE.md §5.2). A key the file leaves out keeps the built-in value
/// (<see cref="Default"/>, the shipped file's); a missing file or one that does not parse gives the built-in values with a
/// log line, a bad entry is logged and skipped. The measured defaults: the strike window 0.8 s (+ 0.1 s slack), the
/// float's flight 0.44 + 0.30 × power s, the client's minigame safety 3 s (gamedata &lt;Fishing safety&gt;, read from the game
/// data when it has it) with 0.5 s tolerance, the captured minigame setting for every name. Immutable.
/// </summary>
public sealed record FishingSettings
{
    public static FishingSettings Default { get; } = new();

    /// <summary>The minigame parameters in FishingMiniGame key order 3-15 (SPEC §5.1; fishing.xml attribute names, as the client's k.p reads them).</summary>
    public static readonly string[] MiniGameKeys =
    [
        "ProgressBarAcceleration", "ProgressBarDeceleation", "ProgressBarNegativeModiferAboveZero", "ProgressBarSpeedMultiplier",
        "ProgressBarStartPosition", "ProgressbarMaxSpeed", "ProgressbarMinSpeed", "TensionBarFishAcceleration", "TensionBarFishMaxSpeed",
        "TensionBarLetGoSpeed", "TensionBarPlayerMaxSpeed", "TensionBarStartSpeed", "TensionBarPlayerAcceleration",
    ];

    /// <summary>The one captured setting (002835, keys 3-15; key 14 absent = 0), shipped for all nine names (SPEC §5.3 [assume]).</summary>
    public static readonly float[] CapturedMiniGame = [2, 1, 4, 0.3f, 1.1f, 4.5f, -1, 1, 6, 2.5f, 10, 0, 7];

    /// <summary>fishing.xml &lt;fishingsettings catchTimeout&gt;: seconds from the bite to the miss (0.8, misses cancelled at 0.80-0.82 s).</summary>
    public float CatchTimeout { get; init; } = 0.8f;

    /// <summary>Seconds a strike may come after <see cref="CatchTimeout"/> by the server's clock (0.1, the round trip; strikes seen up to 0.87 s): the miss tick runs at the sum.</summary>
    public float StrikeSlack { get; init; } = 0.1f;

    /// <summary>The minimum minigame time the client waits before it reports a success (gamedata &lt;Fishing safety="3"/&gt;; the game data's when loaded).</summary>
    public float Safety { get; init; } = 3;

    /// <summary>Seconds a success may come before <see cref="Safety"/> (0.5 [assume]); earlier counts as a fail.</summary>
    public float SafetyTolerance { get; init; } = 0.5f;

    /// <summary>The float's flight: <see cref="FlightBase"/> + <see cref="FlightPerPower"/> × power seconds from the Cast (fit of 16 casts: 0.44 + 0.30 × power).</summary>
    public float FlightBase { get; init; } = 0.44f;
    public float FlightPerPower { get; init; } = 0.30f;

    /// <summary>The farthest cast (3 + 12 × power = 15 m, cuw.ga) plus <see cref="RangeSlack"/> (3) for the server's checks of FishingStart key 1 and FishingCast key 1.</summary>
    public float CastRange { get; init; } = 15;
    public float RangeSlack { get; init; } = 3;

    /// <summary>Seconds after Success, Fail or GroundFail without the client's FishingCancel before the server ends the session itself (5 [assume]).</summary>
    public float EndFallback { get; init; } = 5;

    /// <summary>Seconds a minigame may run without FishingFinish before it counts as a fail (120 [assume]).</summary>
    public float MiniGameTimeout { get; init; } = 120;

    /// <summary>How far a Move's stop point may lie from the fishing spot and keep the session (0.5 m, as a harvest).</summary>
    public float WalkOffTolerance { get; init; } = 0.5f;

    /// <summary>The return code of a refused fishing request (the client only tests ≠ 0 / ≠ -503; 1 [assume]).</summary>
    public short Refusal { get; init; } = 1;

    /// <summary>The rod's durability loss per catch, in items.xml units (1 [assume], as the harvesting tools' NewEquipmentItem in the success burst).</summary>
    public float RodWear { get; init; } = 1;

    /// <summary>A landing point in no zone counts as ocean when the nearest zone within this many metres is an OCEAN_ one (50 [assume]).</summary>
    public float OceanNearby { get; init; } = 50;

    /// <summary>The zone kinds by fishingzone@zone (tiles.xml); a template zone of another name is not spawned.</summary>
    public IReadOnlyDictionary<string, FishingZoneSettings> Zones { get; init; } = DefaultZones();

    /// <summary>The zones without an object (defaultzonefreshwater / defaultzoneocean): unlimited charges, the default lists.</summary>
    public FishingZoneSettings Freshwater { get; init; } = new() { MaxCharges = 0, Loot = "DEFAULT" };
    public FishingZoneSettings Ocean { get; init; } = new() { MaxCharges = 0, Loot = "DEFAULT" };

    /// <summary>The minigame settings by items.xml fishingminigamesetting (T1, T3_Rare, Boss_Shark, DEFAULT, …): keys 3-15.</summary>
    public IReadOnlyDictionary<string, float[]> MiniGames { get; init; } = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["DEFAULT"] = CapturedMiniGame,
    };

    /// <summary>The setting of <paramref name="name"/> (null or unknown = DEFAULT, else the captured one).</summary>
    public float[] MiniGame(string? name) =>
        name is not null && MiniGames.TryGetValue(name, out var v) ? v : MiniGames.TryGetValue("DEFAULT", out var d) ? d : CapturedMiniGame;

    /// <summary>The built-in zone kinds (SPEC §7.1 table; capacities and prefabs captured, the rest [assume]).</summary>
    public static Dictionary<string, FishingZoneSettings> DefaultZones()
    {
        var spot = new FishingZoneSettings { BiteMin = 6, BiteMax = 7, ChargeMin = 120, ChargeMax = 180 };
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["FRESHWATER_BASE"] = new(),
            ["OCEAN_BASE"] = new(),
            ["FRESHWATER_SPOT_FISH"] = spot with { MaxCharges = 1, Prefab = "FishingNodeFish", Priority = 2 },
            ["FRESHWATER_SPOT_SWARM"] = spot with { MaxCharges = 5, Prefab = "FishingNodeSwarm", Priority = 1 },
            ["FRESHWATER_SPOT_SMALL_SWARM"] = spot with { MaxCharges = 5, Prefab = "FishingNodeSwarm", Priority = 1 },
            ["OCEAN_SPOT_SWARM"] = spot with { MaxCharges = 5, Prefab = "FishingNodeSwarm", Priority = 1 },
            ["OCEAN_SPOT_SMALL_SWARM"] = spot with { MaxCharges = 5, Prefab = "FishingNodeSwarm", Priority = 1 },
            ["FRESHWATER_SPOT_TREASURE"] = spot with { MaxCharges = 1, Prefab = "FishingNodeChest", Priority = 3, Loot = "TREASURE" },
            ["OCEAN_SPOT_TREASURE"] = spot with { MaxCharges = 1, Prefab = "FishingNodeChest", Priority = 3, Loot = "TREASURE" },
            // The boss spot's prefab was never captured (no ocean cluster; SPEC §14.4): no VFX until it is known.
            ["OCEAN_SPOT_BOSS"] = spot with { MaxCharges = 1, Priority = 3, Loot = "FISHING_ZONE_OCEAN_SPOT_BOSS" },
        };
    }

    public static FishingSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in fishing settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", string.Create(CultureInfo.InvariantCulture,
                $"fishing: {s.Zones.Count} zone kinds, {s.MiniGames.Count} minigame settings, strike window {s.CatchTimeout} s"));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in fishing settings used");
            return Default;
        }
    }

    /// <summary>
    /// The file: the numbers by their property name with "Seconds" for the times (CatchTimeoutSeconds, StrikeSlackSeconds,
    /// SafetySeconds, SafetyToleranceSeconds, FlightBaseSeconds, FlightPerPowerSeconds, EndFallbackSeconds,
    /// MiniGameTimeoutSeconds), CastRange, RangeSlack, WalkOffTolerance, Refusal, RodWear, OceanNearby; "Zones": { name:
    /// zone }, "Freshwater" / "Ocean": zone, a zone { MaxCharges, Prefab, Priority, BiteSeconds [min, max], ChargeSeconds [min,
    /// max], DelayAtZeroSeconds, ChanceToHit, Loot } (a left-out key keeps the built-in kind's, else the base's); "MiniGames":
    /// { name: { attribute: value } } over DEFAULT (itself over the captured setting).
    /// </summary>
    public static FishingSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        float Num(JsonElement v, string key, float min = 0) =>
            v.TryGetSingle(out var f) && f >= min ? f : throw new InvalidDataException($"{key}: {v} is not a number of {min} or more");
        var zones = DefaultZones();
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "CatchTimeoutSeconds": s = s with { CatchTimeout = Num(p.Value, p.Name) }; break;
                case "StrikeSlackSeconds": s = s with { StrikeSlack = Num(p.Value, p.Name) }; break;
                case "SafetySeconds": s = s with { Safety = Num(p.Value, p.Name) }; break;
                case "SafetyToleranceSeconds": s = s with { SafetyTolerance = Num(p.Value, p.Name) }; break;
                case "FlightBaseSeconds": s = s with { FlightBase = Num(p.Value, p.Name) }; break;
                case "FlightPerPowerSeconds": s = s with { FlightPerPower = Num(p.Value, p.Name) }; break;
                case "EndFallbackSeconds": s = s with { EndFallback = Num(p.Value, p.Name, 0.1f) }; break;
                case "MiniGameTimeoutSeconds": s = s with { MiniGameTimeout = Num(p.Value, p.Name, 1) }; break;
                case "CastRange": s = s with { CastRange = Num(p.Value, p.Name) }; break;
                case "RangeSlack": s = s with { RangeSlack = Num(p.Value, p.Name) }; break;
                case "WalkOffTolerance": s = s with { WalkOffTolerance = Num(p.Value, p.Name) }; break;
                case "Refusal": s = s with { Refusal = p.Value.TryGetInt16(out var rc) && rc != 0 ? rc : throw new InvalidDataException($"Refusal: {p.Value} is not a non-zero short") }; break;
                case "RodWear": s = s with { RodWear = Num(p.Value, p.Name) }; break;
                case "OceanNearby": s = s with { OceanNearby = Num(p.Value, p.Name) }; break;
                case "Freshwater": s = s with { Freshwater = Zone(p.Value, s.Freshwater, $"{p.Name}") }; break;
                case "Ocean": s = s with { Ocean = Zone(p.Value, s.Ocean, $"{p.Name}") }; break;
                case "Zones":
                    foreach (var z in p.Value.EnumerateObject())
                    {
                        try { zones[z.Name] = Zone(z.Value, zones.GetValueOrDefault(z.Name) ?? new(), $"Zones.{z.Name}"); }
                        catch (InvalidDataException e) { Log.Warn("content", $"{path}: {e.Message}, skipped"); }
                    }
                    break;
                case "MiniGames":
                    var games = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
                    var entries = p.Value.EnumerateObject().ToList();
                    // DEFAULT first: the others fill their gaps from it.
                    foreach (var g in entries.OrderBy(g => g.Name.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                    {
                        var values = (float[])(games.GetValueOrDefault("DEFAULT") ?? CapturedMiniGame).Clone();
                        var bad = false;
                        foreach (var a in g.Value.EnumerateObject())
                        {
                            var i = Array.FindIndex(MiniGameKeys, k => k.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
                            if (i < 0 || !a.Value.TryGetSingle(out var f)) { Log.Warn("content", $"{path}: MiniGames.{g.Name}.{a.Name}: unknown or not a number, setting skipped"); bad = true; break; }
                            values[i] = f;
                        }
                        if (!bad) games[g.Name] = values;
                    }
                    games.TryAdd("DEFAULT", CapturedMiniGame);
                    s = s with { MiniGames = games };
                    break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s with { Zones = zones };
    }

    /// <summary>A zone entry over <paramref name="b"/>.</summary>
    private static FishingZoneSettings Zone(JsonElement v, FishingZoneSettings b, string where)
    {
        if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{where}: not an object");
        (float, float) Range(JsonElement r, string key) =>
            r is { ValueKind: JsonValueKind.Array } && r.GetArrayLength() == 2 && r[0].TryGetSingle(out var lo) && r[1].TryGetSingle(out var hi) && lo >= 0 && hi >= lo
                ? (lo, hi) : throw new InvalidDataException($"{where}.{key}: not [min, max]");
        foreach (var p in v.EnumerateObject())
        {
            switch (p.Name)
            {
                case "MaxCharges": b = b with { MaxCharges = p.Value.TryGetInt32(out var n) && n is >= 0 and <= short.MaxValue ? n : throw new InvalidDataException($"{where}.MaxCharges: {p.Value}") }; break;
                case "Prefab": b = b with { Prefab = p.Value.GetString() ?? "" }; break;
                case "Priority": b = b with { Priority = p.Value.TryGetInt32(out var pr) ? pr : throw new InvalidDataException($"{where}.Priority: {p.Value}") }; break;
                case "BiteSeconds": var (b0, b1) = Range(p.Value, p.Name); b = b with { BiteMin = b0, BiteMax = b1 }; break;
                case "ChargeSeconds": var (c0, c1) = Range(p.Value, p.Name); b = b with { ChargeMin = Math.Max(1, c0), ChargeMax = Math.Max(1, c1) }; break;
                case "DelayAtZeroSeconds": b = b with { DelayAtZero = p.Value.TryGetSingle(out var d) && d >= 0 ? d : throw new InvalidDataException($"{where}.DelayAtZeroSeconds: {p.Value}") }; break;
                case "ChanceToHit": b = b with { ChanceToHit = p.Value.TryGetSingle(out var c) && c is >= 0 and <= 1 ? c : throw new InvalidDataException($"{where}.ChanceToHit: {p.Value}") }; break;
                case "Loot": b = b with { Loot = p.Value.GetString() is { Length: > 0 } l ? l : throw new InvalidDataException($"{where}.Loot: empty") }; break;
                default: throw new InvalidDataException($"{where}: unknown key {p.Name}");
            }
        }
        return b;
    }
}
