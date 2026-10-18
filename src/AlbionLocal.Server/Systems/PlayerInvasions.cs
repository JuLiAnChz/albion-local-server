using System.Globalization;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// Stage I (analysis/work/stageI/invasions/SPEC.md): the invasions players start themselves, by .invasion &lt;tier&gt; or by using a ticket
/// item from the bag (content/events.json Player, Tiers; the event itself is a <see cref="WorldEvents.Run"/> with a tier). The rules of
/// SPEC §2.3 in order (<see cref="CanStart"/>): on, a standing player on an open-world map of an allowed cluster type (no city, island,
/// dungeon or expedition), no event on the map, none led by the player, its cooldown over, its daily cap not reached, fewer than
/// Player.MaxRunning player invasions on the server, the tier in MinTier..MaxTier, mobs for it on the map, and for the command its
/// silver. The point is a mob spawn point Player.Distance (15-45 m) from the player. Cooldowns, chosen tiers and daily counts live in
/// memory (SPEC D8: a restart clears them). The time is <see cref="World.Now"/>, the day UTC. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class PlayerInvasions(World world)
{
    private readonly Dictionary<Guid, TimeSpan> _cooldown = [];
    private readonly Dictionary<Guid, int> _chosen = [];
    private readonly Dictionary<Guid, (DateTime Day, int Count)> _daily = [];

    private EventSettings Events => world.EventSettings;

    private PlayerInvasionSettings Settings => world.EventSettings.Player;

    /// <summary>The tier a ticket of the chosen tier starts for <paramref name="p"/>: its .invasion tier, else its zone's tier (1-8, within MinTier..MaxTier).</summary>
    public int Chosen(Player p) =>
        _chosen.TryGetValue(p.Character.Id, out var t) ? t
            : Math.Clamp(Math.Clamp(p.Map?.Template?.Name.Tier ?? 4, 1, 8), Settings.MinTier, Settings.MaxTier);

    /// <summary>.invasion tier &lt;n&gt;: the tier the player's next ticket starts.</summary>
    public void Choose(Player p, int tier) => _chosen[p.Character.Id] = tier;

    /// <summary>The time left before <paramref name="p"/> may start another one (zero: none).</summary>
    public TimeSpan CooldownLeft(Player p) =>
        _cooldown.TryGetValue(p.Character.Id, out var until) && until > world.Now ? until - world.Now : TimeSpan.Zero;

    /// <summary>The player's starts today (UTC).</summary>
    public int Today(Player p) => _daily.TryGetValue(p.Character.Id, out var d) && d.Day == DateTime.UtcNow.Date ? d.Count : 0;

    /// <summary>The running invasion <paramref name="p"/> started; null when there is none.</summary>
    public WorldEvents.Run? Led(Player p) => world.Events.Running.FirstOrDefault(r => r.Starter == p.Character.Id);

    /// <summary>Tickets a start of <paramref name="tier"/> uses with <paramref name="item"/> (ItemCost; a fixed-tier item 1).</summary>
    public int Cost(InvasionItem item, int tier) => item.Tier > 0 ? 1 : Settings.ItemCost[Math.Clamp(tier, 1, 10) - 1];

    /// <summary>The silver (a7m raw) a command start of <paramref name="tier"/> costs (CommandSilver; not × Rates:Silver).</summary>
    public long CommandPrice(int tier) => Settings.CommandSilver[Math.Clamp(tier, 1, 10) - 1] * 10000;

    /// <summary>
    /// Why <paramref name="p"/> may not start a T<paramref name="tier"/> invasion now (by command, or by a ticket with
    /// <paramref name="item"/>; SPEC §2.3 in order); null when it may. The ticket count is the caller's check.
    /// </summary>
    public string? CanStart(Player p, int tier, bool item)
    {
        var s = Settings;
        if (!Events.Enabled || !s.Enabled) return "invasions are off";
        if (!item && !s.Command) return "the command is off here: use an invasion ticket";
        if (p.Map is not { Grid: not null } map || p.Combat.Down is not null) return "you must stand in the world";
        if (!WorldEvents.Fits(map) || !s.Allows(map.Template!.Info.Type)) return "not here: cities, islands and dungeons have no invasions";
        if (world.Events.Running.FirstOrDefault(r => r.Map == map) is { } other) return $"an event already runs on this map (#{other.Id})";
        if (Led(p) is { } led) return $"you already lead an invasion (#{led.Id})";
        if (CooldownLeft(p) is var left && left > TimeSpan.Zero)
            return left.TotalSeconds < 60 ? string.Create(CultureInfo.InvariantCulture, $"cooldown: {Math.Ceiling(left.TotalSeconds):0} s left")
                : string.Create(CultureInfo.InvariantCulture, $"cooldown: {Math.Ceiling(left.TotalMinutes):0} min left");
        if (s.DailyLimit > 0 && Today(p) >= s.DailyLimit) return $"daily limit {s.DailyLimit} reached";
        if (world.Events.Running.Count(r => r.Starter is not null) >= s.MaxRunning) return "too many invasions running";
        if (tier < s.MinTier || tier > s.MaxTier) return $"tier must be {s.MinTier}-{s.MaxTier}";
        try { world.Events.Plan(map, p.PositionAt(DateTime.UtcNow), Events.Tier(tier)); }
        catch (InvalidOperationException) { return $"this map has no mobs for T{tier}"; }
        if (!item && CommandPrice(tier) is > 0 and var price && p.Character.Silver < price)
            return string.Create(CultureInfo.InvariantCulture, $"a T{tier} invasion costs {price / 10000:N0} silver");
        return null;
    }

    /// <summary>
    /// Starts a T<paramref name="tier"/> invasion of <paramref name="type"/> for <paramref name="p"/> (<see cref="CanStart"/> checked again;
    /// throws <see cref="InvalidOperationException"/> with the reason): at a mob spawn point Player.Distance from it, the command's silver
    /// taken, its cooldown (CooldownMinutes, a ticket's ItemCooldownMinutes) and today's count set.
    /// </summary>
    public WorldEvents.Run Start(Player p, int tier, EventType type, bool item)
    {
        if (CanStart(p, tier, item) is { } why) throw new InvalidOperationException(why);
        var map = p.Map!;
        var point = WorldEvents.PointNear(map, p.PositionAt(DateTime.UtcNow), Settings.Distance);
        var price = item ? 0 : CommandPrice(tier);
        var run = world.Events.Start(map, type, point, item ? WorldEvents.Source.Item : WorldEvents.Source.Command, tier, p);
        if (price > 0) Wallet.TrySpend(p, price, $"T{tier} invasion");
        var id = p.Character.Id;
        _cooldown[id] = world.Now + TimeSpan.FromMinutes(item ? Settings.ItemCooldownMinutes : Settings.CooldownMinutes);
        _daily[id] = (DateTime.UtcNow.Date, Today(p) + 1);
        return run;
    }

    /// <summary>.invasion tiers: one line per tier (mob tier, buff, extra waves, chest, loot tier and factor, silver, fame).</summary>
    public IEnumerable<string> DescribeTiers()
    {
        foreach (var t in Events.Tiers)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  T{t.Tier}: T{t.MobTier} mobs{(t.LevelBuff is null ? "" : $" + {t.LevelBuff}")}{(t.ExtraWaves.Count > 0 ? $", +{t.ExtraWaves.Count} wave(s)" : "")}; chest {(t.Chest.Count > 0 ? t.Chest[0] : "of the kind")} T{t.LootTier?.ToString(CultureInfo.InvariantCulture) ?? "zone"}{(t.ItemTier is { } i ? $" (T{i} items)" : "")}{(t.Enchant > 0 ? $" +{t.Enchant} enchant" : "")} x{t.LootFactor:0.##}, {t.Silver:N0} silver, {t.Fame:N0} fame{(Settings.ItemCost[t.Tier - 1] > 1 ? $", {Settings.ItemCost[t.Tier - 1]} tickets" : "")}");
    }
}
