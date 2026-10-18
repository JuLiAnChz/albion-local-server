using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Stage I (analysis/work/stageI/invasions/SPEC.md §6): the invasions players start. The tier table and its parser, the item shift
/// and the merchant offers; on FakePeer sessions in 0204 (T4 Undead, SAFEAREA) with a manual clock: no scheduler, .invasion 6 (T6 mobs
/// of the zone's faction, the cooldown), GM tiers T9 (T8 mobs with the level buff, an extra wave) and T2 (heretics), a ticket of the
/// chosen tier (one used; T10 with too few refused, nothing used), and a T10 win (the chest's loot tier and enchant, the silver).
/// </summary>
public static class InvasionTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("invasions on demand: tier table, parser, cluster types, item shift, ticket merchant", Data),
        ("invasions on demand: no scheduler; .invasion 6 and its cooldown; GM T9/T2; a ticket of the chosen tier; a T10 win's chest and silver", Live),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static int Code(EventData e) => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode));

    private static void Data()
    {
        var file = EventSettings.Load(Path.Combine(DataDir(), "..", "content", "events.json"));
        Check(file.Tiers.Count == 10 && file.Tiers.Select(t => t.Tier).SequenceEqual(Enumerable.Range(1, 10)) && file.Tier(9) is { MobTier: 8, LevelBuff: "MOB_RANDOM_DUNGEON_BUFF_02", ExtraWaves.Count: 1 }
              && file.Tier(10) is { Enchant: 2, Silver: 120000, Chest: ["DEFAULT_TREASURE_ELITE", "DEFAULT_TREASURE_VETERAN"] } && file.Tiers.Select(t => (t.MobTier, t.LootTier, t.ItemTier, t.Enchant, t.Silver, t.Fame, t.LootFactor, t.LevelBuff, t.Chest.Count, t.ExtraWaves.Count))
                  .SequenceEqual(EventSettings.DefaultTiers.Select(t => (t.MobTier, t.LootTier, t.ItemTier, t.Enchant, t.Silver, t.Fame, t.LootFactor, t.LevelBuff, t.Chest.Count, t.ExtraWaves.Count)))
              && file.Player.Items.Select(i => (i.Item, i.Kind)).SequenceEqual([("T5_CORRUPTED_NONLETHAL_MAP", "invasion"), ("T6_CORRUPTED_LETHAL_MAP", "champion")])
              && file.ItemFor(Client.Items.Find("T6_CORRUPTED_LETHAL_MAP")!) is { Kind: "champion" } && file.Player.ItemCost[9] == 3,
              "content/events.json: 10 tiers as built in (T9 T8 mobs + BUFF_02 + 1 wave, T10 +2 enchant 120000 silver elite→veteran), the two maps as tickets");
        Check(Throws(() => EventSettings.Parse("""{ "Tiers": [ { "Tier": 3, "MobTier": 9 } ] }""", "test"))
              && EventSettings.Parse("""{ "Tiers": [ { "Tier": 3, "Silver": 5 } ] }""", "test").Tier(3) is { Silver: 5, MobTier: 3, ItemTier: 3 }
              && EventSettings.Parse("""{ "Player": { "Items": [ { "Item": "X", "Kind": "dragon" }, { "Item": "y", "Tier": 4 } ] } }""", "test").Player.Items
                  is [{ Item: "Y", Kind: "invasion", Tier: 4 }],
              "tiers parsing: a row changes only its keys; MobTier 9 refused; a ticket of an unknown kind skipped");
        var p = EventSettings.Default.Player;
        Check(p.Allows("SAFEAREA") && p.Allows("OPENPVP_BLACK_3") && !p.Allows("PLAYERCITY_SAFEAREA_01") && !p.Allows("SAFEAREA_TUTORIAL") && !p.Allows("PLAYERISLAND"),
              "cluster types: open world yes; cities, the tutorial and islands no");
        Check(RandomDungeons.Shift(Client.Items, "T4_MAIN_SWORD", 2, 0) == "T2_MAIN_SWORD" && RandomDungeons.Shift(Client.Items, "T4_MAIN_SWORD@1", null, 2) == "T4_MAIN_SWORD@3"
              && RandomDungeons.Shift(Client.Items, "T8_MAIN_SWORD@4", null, 2) == "T8_MAIN_SWORD@4" && RandomDungeons.Shift(Client.Items, "NOTANITEM", 2, 2) == "NOTANITEM"
              && RandomDungeons.Shift(Client.Items, "T4_MAIN_SWORD@1", 2, 0) == "T2_MAIN_SWORD",
              "the chest's item shift: T4 → T2, @1 + 2 → @3, @4 kept, an unknown name kept, T4@1 → T2 (@0: T2 has no @1)");
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static void Live()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var settings = EventSettings.Default with
        {
            Events =
            [
                new EventType { Name = "invasion", Waves = [new(WaveKind.Standard, (2, 2)), new(WaveKind.Champion, (1, 1), 1.5)], WaveDelaySeconds = 10 },
                new EventType { Name = "champion", Waves = [new(WaveKind.Miniboss, (1, 1), 3)] },
            ],
            Drops = new EventDrops { Won = 0 },
        };
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = Client.Clusters, MapSeed = 11, EventSettings = settings,
        };
        world.MobAi = false;
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }
        Client.Clusters!.Get("0204");
        var hero = EventTest.NewHero(characters, new Vector2(0, 0));
        var (_, peerA) = EventTest.Join(world, hero.Id);
        var c = StarterKit.Default.NewCharacter("I" + Guid.NewGuid().ToString("N")[..10]);
        (c.Cluster, c.PositionX, c.PositionY) = ("0204", 3, 0);
        c.Inventory.Add(new InventoryItem("T5_CORRUPTED_NONLETHAL_MAP", 2));
        Check(characters.Create(c), "character created");
        var (session, peerB) = EventTest.Join(world, c.Id);
        var (me, friend) = (world.Parties.Online(hero.Id)!, world.Parties.Online(c.Id)!);
        string Run(Player who, AccountLevel level, string line)
        {
            var replies = new List<string>();
            var ctx = new Server.Commands.CommandContext(world, who, level, (t, error) => replies.Add((error ? "error: " : "") + t));
            lock (world.Sync) world.Commands.Execute(ctx, line);
            return string.Join("\n", replies);
        }
        bool Told(FakePeer peer, string what) => peer.Events.Any(e => Code(e) == (int)EventCodes.SystemMessage && e.Parameters[0] is string s && s.Contains(what, StringComparison.Ordinal));
        void StopAll()
        {
            lock (world.Sync) foreach (var r in world.Events.Running.ToList()) world.Events.Stop(r);
        }

        // 1. No scheduler (Scheduled off by default): two hours with players out start nothing.
        Tick(0.1);
        Tick(7200);
        Check(world.Events.Running.Count == 0 && world.Events.NextScheduled is null, "Scheduled off: no event in two hours");

        // 2. .invasion 6 by a Player-level account: T6 mobs of an invasion faction, the cooldown after a stop.
        var reply = Run(me, AccountLevel.Player, ".invasion 6");
        var run = world.Events.Running.SingleOrDefault();
        Check(run is { Tier: 6, Source: WorldEvents.Source.Command, Mobs.Count: > 0 } && run.Starter == hero.Id && run.Title.EndsWith("T6", StringComparison.Ordinal)
              && run.Mobs.All(m => m.Template.Tier == 6 && m.Template.Faction == run.Faction) && settings.Player.IsFaction(run.Faction)
              && Vector2.Distance(run.Point, Vector2.Zero) is >= 15 and <= 45 && Told(peerB, "called " + run.Title),
              $".invasion 6: a T6 {run?.Faction} invasion 15-45 m away, the call line to the cluster ({reply})");
        var stop = Run(me, AccountLevel.Player, ".invasion stop");
        var again = Run(me, AccountLevel.Player, ".invasion 6");
        Check(run!.Outcome == WorldEvents.Outcome.Stopped && again.StartsWith("error: cooldown: 10 min", StringComparison.Ordinal),
              $".invasion stop ends it; a second start meets the cooldown ({stop}; {again})");

        // 3. A GM's T9: T8 mobs with MOB_RANDOM_DUNGEON_BUFF_02, fame x1.5, one extra wave; a GM's T2 in an Undead zone: heretics.
        Run(me, AccountLevel.Admin, ".event start invasion 9 here");
        var t9 = world.Events.Running.Single();
        Check(t9 is { Tier: 9, Waves.Count: 3 } && t9.Mobs.All(m => m.Template.Tier == 8 && m.LevelBuff?.Name == "MOB_RANDOM_DUNGEON_BUFF_02" && m.FameFactor == 1.5
              && m.Effects.Spells.Any(s => s.Spell.Name == "MOB_RANDOM_DUNGEON_BUFF_02") && m.Health.Max > m.Template.HitPointsMax * 1.3f),
              $"T9: T8 mobs with the +36 % level buff (health {t9.Mobs.FirstOrDefault()?.Health.Max} of {t9.Mobs.FirstOrDefault()?.Template.HitPointsMax}), x1.5 fame, 3 waves");
        StopAll();
        Run(me, AccountLevel.Admin, ".event start invasion 2 here");
        var t2 = world.Events.Running.Single();
        Check(t2.Faction == "HERETIC" && t2.Mobs.All(m => m.Template.Tier == 2 && m.Template.Faction == "HERETIC"), $"T2 in an Undead zone: T2 heretics (got {t2.Faction})");
        StopAll();

        // 4. A ticket at the friend's chosen T7: one map used, the invasion is T7 and its; T10 needs 3: refused (14960), nothing used.
        Run(friend, AccountLevel.Player, ".invasion tier 7");
        var maps = friend.Inventory.Bag.Items.Single(i => i.Template.Name == "T5_CORRUPTED_NONLETHAL_MAP");
        var responses = peerB.Responses.Count;
        lock (world.Sync) CombatTest.Send(session, new UseConsumableFromInventoryRequest { ah5 = maps.Id, ah6 = 2 });
        Tick(0.6);
        var ticketed = world.Events.Running.SingleOrDefault();
        Check(ticketed is { Tier: 7, Source: WorldEvents.Source.Item } && ticketed.Starter == c.Id && maps.Count == 1 && peerB.Responses.Count == responses + 1
              && peerB.Responses[^1].ReturnCode == 0, $"a ticket used ('use all' of 2): one T7 invasion, one map left (got {ticketed?.Tier}, {maps.Count} left)");
        StopAll();
        Tick(180);
        Run(friend, AccountLevel.Player, ".invasion tier 10");
        lock (world.Sync) CombatTest.Send(session, new UseConsumableFromInventoryRequest { ah5 = maps.Id, ah6 = 1 });
        Check(peerB.Responses[^1].ReturnCode == Consumables.GenericFailed && maps.Count == 1 && world.Events.Running.Count == 0 && Told(peerB, "a T10 invasion needs 3"),
              "T10 with one map: rc 14960, the reason line, nothing used");
        // ... and with three maps in three stacks: started, all three used; the start line gives the time with the extra waves' minutes.
        lock (world.Sync)
            for (var k = 0; k < 2; k++)
                friend.Inventory.Bag.Put(friend.Inventory.Bag.FreeSlot(), new Item(world.ObjectIds.Next(), maps.Template, 1, 1, 0, [], []));
        lock (world.Sync) CombatTest.Send(session, new UseConsumableFromInventoryRequest { ah5 = maps.Id, ah6 = 1 });
        Tick(0.6);
        var stacked = world.Events.Running.SingleOrDefault();
        var minutes = stacked is null ? "?" : $"{(stacked.EndsAt - stacked.StartedAt).TotalMinutes:0.#} min";
        Check(stacked is { Tier: 10 } && !friend.Inventory.Bag.Items.Any(i => i.Template == maps.Template) && stacked.EndsAt - stacked.StartedAt > TimeSpan.FromMinutes(stacked.Type.DurationMinutes)
              && Told(peerB, $"waves, {minutes}"), $"T10 with three single maps: started, all used, announced as {minutes}");
        StopAll();

        // 5. A GM's T10 won by the hero: the chest of loot tier 8 with +2 enchant, loot x RewardFactor x 2, 120000 silver x Rates:Silver.
        Run(me, AccountLevel.Admin, ".event start invasion 10 here");
        var t10 = world.Events.Running.Single();
        var silver = me.Character.Silver;
        for (var i = 0; i < 8 && t10.Outcome == WorldEvents.Outcome.Running; i++)
        {
            lock (world.Sync) foreach (var m in t10.Mobs.ToList()) Combat.Damage(me, m, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
            Tick(0.1);
            Tick(10.1);
        }
        Check(t10 is { Outcome: WorldEvents.Outcome.Won, Wave: 3, Chest: { LootTier: 8, Enchant: 2, ItemTier: null, LootFactor: 4 } } && t10.Chest.ViewerState(me).State == 6
              && me.Character.Silver - silver == (long)Math.Round(120000 * world.Rates.Silver) * 10000,
              $"a T10 win after 4 waves: the chest at loot tier 8 with +2 enchant, x4 loot, 120000 silver (got {t10.Outcome}, wave {t10.Wave + 1}, {t10.Chest})");

        // 6. The ticket merchant in Thetford.
        var offer = JournalMerchant.Orders(world, "0000", MarketOrder.Offer).FirstOrDefault(o => o.ItemName == "T5_CORRUPTED_NONLETHAL_MAP");
        Check(offer is { UnitPrice: 15000 * 10000L, Amount: 999 } && JournalMerchant.Owns(offer) && JournalMerchant.Order(world, offer.Id, "0000") == offer
              && !JournalMerchant.Orders(world, "1000", MarketOrder.Offer).Any(o => o.ItemName == offer.ItemName),
              "Thetford's market sells the invasion map at 15000 (never runs out); Lymhurst's does not");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
