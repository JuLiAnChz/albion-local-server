using System.Numerics;
using AlbionLocal.Server;
using AlbionLocal.Server.Bots;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;
using Field = AlbionLocal.SelfTest.BotTest.Field;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Stage I bots, phase 2 (analysis/work/stageI/bots/SPEC.md §6): gatherer bots on Thetford's T1 trees (0000) and Birchcopse's nodes (1207)
/// with <see cref="FakePeer"/> humans on a <see cref="ManualClock"/>. A player's harvest is seen by the others (59/61, the catch-up 59 of a
/// newcomer); a bot's show sends the same to the humans who know the node, its charges unchanged unless Bots:ConsumeNodes; its node choice
/// gives way to humans and other bots; its tier band; it answers a Red player's hit in a yellow zone (no crime, a Blue one may not hit it),
/// gives up after the engage time (also where the pair stays Hostile), turns to a harder hitter, dismounts before its first swing, runs off
/// when low, walks on to an exit after a fight on its way out; its defeat is a knockdown, one Leave 10 s later and the slot's respawn.
/// </summary>
public static class BotGatherTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("bots gather: a player's harvest reaches another player (catch-up 59 with its start after the NewCharacter, then 61, a new 59)", PlayerHarvestSeen),
        ("bots gather: a gatherer's show: 59 with its tool and the node's time, 61 with the node's charges unchanged and no 46; ConsumeNodes takes them (46)", Show),
        ("bots gather: node choice gives way to a human within 6 m and to another gatherer's node", NodeChoice),
        ("bots gather: Birchcopse gatherers: the tool of their tier, nodes of one kind within tier - 2 .. tier; a T8 one on a map without T8 nodes takes the nearest tier", Tiers),
        ("bots gather: a Red player's hit in a yellow zone: 60, the bot answers within the reaction time, no crime, a Blue player refused -501; 21 s later it gathers again", FightBack),
        ("bots gather: a slot gatherer knocked down: KnockedDown, a second hit changes nothing, one Leave after 10 s, the slot respawns with its persona and name", Defeat),
        ("bots gather: below FleeHealth it runs off: no swing for 20 s, 20 m or more away", Flee),
        ("bots gather: a riding gatherer hit: the mount bar takes it, it dismounts before its first Attack", Mounted),
        ("bots gather: under Bots:PvpRules Official (Hostile both ways) it still gives up 21 s after the last hit; its hits give it no PvP credit", EngageOfficial),
        ("bots gather: it turns to an attacker that dealt 1.5 x its target's damage in the last 5 s", Retarget),
        ("bots gather: hit on its way out, it walks on to an exit after the fight and is gone at its gate", LeaveAttacked),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static List<Dictionary<byte, object?>> Of(FakePeer peer, EventCodes code, int from = 0) => BotTest.Of(peer, code, from);

    /// <summary>Thetford's three T1 trees near (44.5, 3.5) (HarvestTest's), as its template places them.</summary>
    private static List<HarvestableSpawn> Trees() =>
        [.. Client.Clusters!.Get("0000").Harvestables.OrderBy(h => Vector2.Distance(h.Position, new Vector2(44.5f, 3.5f))).Take(3)];

    /// <summary>The pve preset with bots and no population; fast thinking.</summary>
    private static BotOptions Quick => new() { Enabled = true, ThinkSeconds = (0.3, 0.3), CrimesAgainstBots = false };

    private static readonly ClusterPvp Yellow = new(PvpRule.PvpAllowed, PlayersAreLethal: false, WarningType: "yellow");

    /// <summary>A GM gatherer of <paramref name="tier"/> on the walkable cell nearest to <paramref name="near"/>.</summary>
    private static Player Gatherer(Field f, Vector2 near, int tier)
    {
        lock (f.World.Sync)
        {
            var nav = f.World.Bots.NavOf(f.Map) ?? throw new InvalidOperationException("the walk grid is not built (Field.Nav first)");
            return f.World.Bots.Spawn(f.Map, BotRole.Gatherer, tier, nav.Grid.Nearest(near, 40) ?? throw new InvalidOperationException($"nothing walkable within 40 m of {near}"), 0)!;
        }
    }

    /// <summary>Runs until <paramref name="done"/> (at most <paramref name="seconds"/>).</summary>
    private static void Until(Field f, double seconds, Func<bool> done)
    {
        for (var i = 0; i < seconds * 20 && !done(); i++) f.Run(0.05);
    }

    private static void Hit(Field f, Player attacker, Player target, double amount)
    {
        lock (f.World.Sync) Pvp.Damage(attacker, target, amount, EffectType.Physical, HitOrigin.Direct, -1, f.World.GameNow);
    }

    private static void PlayerHarvestSeen()
    {
        using var f = new Field(Quick, BotTest.NoPopulation);
        f.Nav();
        var trees = Trees();
        var (session, _, a) = f.Join("Hanna", trees[0].Position - new Vector2(2, 0));
        var tree = f.Map.Objects.Values.OfType<Harvestable>().Single(h => h.Key == trees[0].Key);
        var start = GameTime.Now;
        lock (f.World.Sync) session.OnRequest(Request(new HarvestStartRequest { Start = start, HarvestableId = tree.Id, ToolObjectId = -1 }, 78), encrypted: false);
        var (_, peer, _) = f.Join("Bert", tree.Position - new Vector2(2, 2));
        var joined = Since(peer, 0);
        var spawn = joined.SkipWhile(e => !(e.Code == EventCodes.NewCharacter && L(e.P, 0) == a.Id)).ToList();
        var catchUp = joined.FindIndex(e => e.Code == EventCodes.HarvestStart && L(e.P, 0) == a.Id);
        Check(spawn.Count > 1 && spawn.Skip(1).Any(e => e.Code == EventCodes.HarvestStart && L(e.P, 0) == a.Id && L(e.P, 1) == start && L(e.P, 3) == tree.Id && e.P[5] is float)
              && catchUp > joined.FindLastIndex(e => e.Code == EventCodes.NewSimpleHarvestableObjectList),
              "a newcomer: NewCharacter of the harvester, then (once the node lists went out) its HarvestStart with the original start and a float key 5");
        var mark = peer.Events.Count;
        f.Run(3);
        Check(Of(peer, EventCodes.HarvestFinished, mark).Any(p => L(p, 0) == a.Id && L(p, 3) == tree.Id), "the harvester's HarvestFinished reaches the other player");
        var next = f.Map.Objects.Values.OfType<Harvestable>().Single(h => h.Key == trees[1].Key);
        mark = peer.Events.Count;
        lock (f.World.Sync) session.OnRequest(Request(new HarvestStartRequest { Start = GameTime.Now, HarvestableId = next.Id, ToolObjectId = -1 }, 79), encrypted: false);
        Check(Of(peer, EventCodes.HarvestStart, mark).Any(p => L(p, 0) == a.Id && L(p, 3) == next.Id), "its next HarvestStart too");
    }

    private static void Show()
    {
        foreach (var consume in new[] { false, true })
        {
            using var f = new Field(Quick with { ConsumeNodes = consume }, BotTest.NoPopulation);
            var nav = f.Nav();
            var trees = Trees();
            var (_, peer, _) = f.Join("Sina", trees[0].Position - new Vector2(2, 0));
            var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 2);
            var brain = (GathererBrain)bot.Bot!.Brain;
            var mark = peer.Events.Count;
            Until(f, 30, () => Of(peer, EventCodes.HarvestFinished, mark).Any(p => L(p, 0) == bot.Id));
            var start = Of(peer, EventCodes.HarvestStart, mark).FirstOrDefault(p => L(p, 0) == bot.Id);
            var finish = Of(peer, EventCodes.HarvestFinished, mark).FirstOrDefault(p => L(p, 0) == bot.Id);
            var node = start is null ? null : f.Map.Objects.GetValueOrDefault(L(start, 3)) as Harvestable;
            var tool = bot.Inventory.Equipment[0];
            if (!consume)
            {
                Check(brain.Profile is { Tier: 2, Resource: "WOOD", MinTier: 1, MaxTier: 1 } && bot.Bot.Tier == 2 && tool?.Template.Name == "T2_2H_TOOL_AXE",
                      $"a T2 gatherer on Thetford's T1 trees: wood T1, its T2 axe (profile {brain.Profile}, tool {tool?.Template.Name})");
                var seconds = node is null ? -1 : Harvesting.Duration(node.TierData, Harvesting.TimeFactor(node.Type, node.TierData, tool!.Template)!.Value);
                Check(node is { Corpse: null } && brain.Profile.Takes(node) && start![4] is short type && type == node.Type.Id && start[5] is float s && s == seconds
                      && L(start, 1) == L(start, 2) && L(start, 6) == tool!.Id && Convert.ToInt32(start[7]) == tool.Template.Index,
                      $"59 {{0 bot, 3 its node, 4 its type, 5 {seconds} s, 6 the tool, 7 its item id}}");
                Check(finish is not null && Convert.ToInt32(finish[8]) == node!.FullCharges && finish[10] is byte[] { Length: 0 } && L(finish, 1) == L(start!, 1)
                      && node.Charges == node.FullCharges && Of(peer, EventCodes.HarvestableChangeState, mark).Count == 0 && bot.Bot.Fight.CarriedTotal > 0,
                      $"61 {{1 the start, 8 the node's {node?.FullCharges} charges, 10 an empty byte[]}}, the node untouched and no 46; the bot carries {bot.Bot.Fight.CarriedTotal}");
            }
            else
                Check(node is not null && finish is not null && node.Charges == node.FullCharges - Harvesting.ChargesTaken(node.TierData, node.FullCharges)
                      && Of(peer, EventCodes.HarvestableChangeState, mark).Any(p => L(p, 0) == node.Id) && (node.Charges == 0 ? !finish.ContainsKey(8) : Convert.ToInt32(finish[8]) == node.Charges),
                      $"Bots:ConsumeNodes: the charges taken ({node?.Charges}/{node?.FullCharges}), 46 to the human, 61 key 8 the new charges");
        }
    }

    private static void NodeChoice()
    {
        using var f = new Field(Quick with { ThinkSeconds = (60, 60) }, BotTest.NoPopulation);
        f.Nav();
        var trees = Trees();
        var (_, _, watcher) = f.Join("Wera", trees[0].Position + new Vector2(30, 0));
        var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 1);
        var brain = (GathererBrain)bot.Bot!.Brain;
        var best = brain.Candidates(bot, f.Map).First();
        f.Join("Nils", best.Position + new Vector2(3, 0));
        var after = Enumerable.Range(0, 100).Select(_ => brain.Candidates(bot, f.Map).FirstOrDefault()).ToList();
        Check(after.All(n => n is not null && n != best && Vector2.Distance(n.Position, best.Position) > 0), $"a human 3 m from {best}: it is never chosen in 100 choices");
        var other = Gatherer(f, trees[0].Position + new Vector2(6, 2), 1);
        var otherBrain = (GathererBrain)other.Bot!.Brain;
        lock (f.World.Sync) otherBrain.Think(other, f.World.Clock.Now);
        Check(otherBrain.Node is { } taken && !brain.Candidates(bot, f.Map).Contains(taken), $"another gatherer's node ({otherBrain.Node}) is not a candidate");
    }

    private static void Tiers()
    {
        using var f = new Field(Quick with { ThinkSeconds = (60, 60) }, BotSettings.Default with
        {
            Override = new Dictionary<string, IReadOnlyDictionary<BotRole, int>> { ["1207"] = new Dictionary<BotRole, int> { [BotRole.Gatherer] = 0 } },
        }, cluster: "1207");
        var nav = f.Nav();
        f.Join("Tara", nav.Exits[0].Arrival);
        var nodes = f.World.Bots.NodesOf(f.Map);
        var present = nodes.Select(n => n.Tier).Distinct().Order().ToList();
        var bad = new List<string>();
        foreach (var tier in new[] { 3, 4, 5 })
        {
            var bot = Gatherer(f, nav.Exits[0].Arrival, tier);
            var brain = (GathererBrain)bot.Bot!.Brain;
            var p = brain.Profile;
            var tool = bot.Inventory.Equipment[0]!.Template;
            var chosen = brain.Candidates(bot, f.Map).Take(10).ToList();
            if (p.Tier != bot.Bot.Tier || tool.Tier != p.Tier || !tool.CanHarvest.Contains(p.Resource, StringComparer.OrdinalIgnoreCase) || p.MinTier < p.Tier - 2 || p.MaxTier > p.Tier
                || chosen.Count == 0 || !chosen.All(n => p.Takes(n)))
                bad.Add($"T{tier}: {p}, tool {tool.Name}, {chosen.Count} candidates");
        }
        GatherProfile? top;
        lock (f.World.Sync) top = f.World.Bots.ProfileFor(f.Map, 8);
        Check(bad.Count == 0 && nodes.Count > 100 && present.Count > 1 && top is { } t && t.Tier == present.Where(x => x >= 6).DefaultIfEmpty(present.Max()).Min() && t.MaxTier == t.Tier,
              $"1207's {nodes.Count} nodes of tiers {string.Join(",", present)}: each gatherer's tool of its tier and its nodes in its band (bad: {string.Join("; ", bad)}); T8 → {top}");
    }

    private static void FightBack()
    {
        using var f = new Field(Quick, BotTest.NoPopulation, Yellow);
        var nav = f.Nav();
        var trees = Trees();
        var (blueSession, bluePeer, blue) = f.Join("Bea", trees[0].Position - new Vector2(2, 2));
        var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 1);
        Until(f, 30, () => bot.Bot!.Harvest is not null);
        var harvest = bot.Bot!.Harvest;
        // The Red player comes up to the harvesting bot, on walkable ground (the trees by the start are fenced in).
        var (_, peer, red) = f.Join("Rolf", nav.Grid.RandomNear(new Random(1), nav.Grid.Nearest(bot.PositionAt(f.World.Bots.Time), 6)!.Value, 2, 4)!.Value, Pvp.Red);
        var responses = bluePeer.Responses.Count;
        lock (f.World.Sync) Send(blueSession, new AttackStartRequest { wf = f.World.GameNow, co = bot.Id });
        Check(bot.Flag == Pvp.Blue && Pvp.Of(blue, bot) == Relation.Friendly && bluePeer.Responses.Skip(responses).Any(r => r.ReturnCode == -501),
              "a Blue gatherer in a yellow zone: a Blue player's AttackStart on it refused -501");
        Check(Pvp.Of(red, bot) == Relation.Hostile && Pvp.Of(bot, red) == Relation.Neutral, "a Red player may attack it; it may not attack the Red player unprovoked");
        var mark = peer.Events.Count;
        Hit(f, red, bot, 5);
        var cancel = Of(peer, EventCodes.HarvestCancel, mark).FirstOrDefault(p => L(p, 0) == bot.Id);
        Check(harvest is not null && cancel is not null && L(cancel, 1) == harvest.Start && Pvp.Of(bot, red) == Relation.Hostile,
              "the hit: HarvestCancel {0 bot, 1 its start} to the Red player, and the bot may answer it now");
        var interval = SpellCasts.SwingInterval(bot) / (double)TimeSpan.TicksPerSecond;
        Until(f, f.World.Config.Bots.ReactionMs / 1000.0 + interval + 3, () => Of(peer, EventCodes.Attack, mark).Any(p => L(p, 0) == bot.Id));
        Check(Of(peer, EventCodes.Attack, mark).Any(p => L(p, 0) == bot.Id && L(p, 2) == red.Id) && Of(peer, EventCodes.ReputationUpdate, mark).Count == 0 && !red.PvpState.Implicated(f.World.GameNow),
              $"its Attack 13 {{0 bot, 2 the Red player}} within the reaction time and a swing (plus the walk); no crime: no ReputationUpdate, no OUTLAW (target {bot.Bot.Fight.TargetId}, {bot.Bot.Brain.State})");
        var fought = bluePeer.Events.Count;
        f.Run(22);
        Until(f, 20, () => Of(bluePeer, EventCodes.HarvestStart, fought).Any(p => L(p, 0) == bot.Id));
        Check(!bot.Bot.Fight.Active && Pvp.Of(bot, red) == Relation.Neutral && Of(bluePeer, EventCodes.HarvestStart, fought).Any(p => L(p, 0) == bot.Id),
              "21 s after the last hit the fight is over, the Red player Neutral to it again, and it gathers again (a new 59)");
    }

    private static void Defeat()
    {
        var one = BotSettings.Default with
        {
            Override = new Dictionary<string, IReadOnlyDictionary<BotRole, int>> { ["0000"] = new Dictionary<BotRole, int> { [BotRole.City] = 0, [BotRole.Gatherer] = 1 } },
        };
        using var f = new Field(Quick with { PopulateSeconds = 0.5, MinSpawnCells = 0, RespawnSeconds = (1, 1) }, one, Yellow);
        var nav = f.Nav();
        var (_, _, red) = f.Join("Ragna", nav.Exits[0].Arrival, Pvp.Red);
        Until(f, 5, () => f.Map.Bots.Count == 1);
        var bot = f.Map.Bots.Values.Single();
        var (name, persona) = (bot.Character.Name, bot.Bot!.PersonaId);
        var (_, peer, _) = f.Join("Olga", bot.PositionAt(f.World.Bots.Time) + new Vector2(1, 0));
        var mark = peer.Events.Count;
        Hit(f, red, bot, 1e7);
        Hit(f, red, bot, 1e7);
        var downs = Of(peer, EventCodes.KnockedDown, mark).Where(p => L(p, 0) == bot.Id).ToList();
        Check(downs.Count == 1 && L(downs[0], 2) == red.Id && bot.Bot.Fight.Defeated && bot.Combat.Down is { Kind: KnockdownKind.Pvp },
              $"0 health in a non-lethal zone: one KnockedDown {{0 bot, 2 the attacker}} ({downs.Count}, defeated {bot.Bot.Fight.Defeated}, down {bot.Combat.Down?.Kind}, seen by {bot.Observers.Count}); the second hit changes nothing");
        f.Run(5);
        var early = Of(peer, EventCodes.Leave, mark).Count(p => L(p, 0) == bot.Id);
        f.Run(5.5);
        var leaves = Of(peer, EventCodes.Leave, mark).Count(p => L(p, 0) == bot.Id);
        Check(early == 0 && leaves == 1 && bot.Map is null && !Of(peer, EventCodes.Respawn, mark).Any(p => L(p, 0) == bot.Id),
              $"no Leave in the first 5 s ({early}), exactly one Leave after 10 s ({leaves}), it never got up");
        Until(f, 5, () => f.Map.Bots.Count == 1);
        var next = f.Map.Bots.Values.SingleOrDefault();
        Check(next is not null && next != bot && next.Bot!.PersonaId == persona && next.Character.Name == name && !next.Bot.Fight.Defeated,
              $"the slot respawns after Bots:RespawnSeconds: {persona} as '{next?.Character.Name}' again");
    }

    private static void Flee()
    {
        using var f = new Field(Quick with { FleeChance = 1, FleeHealth = 0.5 }, BotTest.NoPopulation, Yellow);
        var nav = f.Nav();
        var trees = Trees();
        var (_, peer, red) = f.Join("Fred", trees[0].Position - new Vector2(2, 0), Pvp.Red);
        var bot = Gatherer(f, trees[0].Position + new Vector2(4, 0), 1);
        f.Run(1);
        var mark = peer.Events.Count;
        Hit(f, red, bot, bot.Health.Max * 0.6);
        f.Run(19);
        var at = bot.PositionAt(f.World.Bots.Time);
        var from = red.PositionAt(DateTime.UtcNow);
        Check(bot.Bot!.Fight.FleeRolled && !Of(peer, EventCodes.Attack, mark).Any(p => L(p, 0) == bot.Id) && Vector2.Distance(at, from) >= 20 && nav.Grid.Open(at),
              $"at 40 % health it ran off: no Attack for 19 s, {Vector2.Distance(at, from):0.#} m away on open ground");
    }

    private static void Mounted()
    {
        using var f = new Field(Quick with { ThinkSeconds = (60, 60) }, BotSettings.Default with
        {
            GathererWeaponShare = 0, // its tool in hand: a drawn bow's 6 s WINDWALL channel would hold its first Attack back
            Override = new Dictionary<string, IReadOnlyDictionary<BotRole, int>> { ["1207"] = new Dictionary<BotRole, int> { [BotRole.Gatherer] = 0 } },
        }, Yellow, cluster: "1207");
        var nav = f.Nav();
        var at = nav.Exits[0].Arrival;
        var (_, peer, red) = f.Join("Mira", at, Pvp.Red);
        var bot = Gatherer(f, at + new Vector2(3, 0), 5);
        Check(Mounts.ItemOf(bot) is not null, $"a T5 gatherer carries a mount ({string.Join(", ", bot.Inventory.Equipment.Items.Select(i => i.Template.Name))})");
        lock (f.World.Sync) Mounts.Start(bot, remount: false);
        Until(f, 5, () => bot.IsMounted);
        var mark = peer.Events.Count;
        Hit(f, red, bot, 5);
        Until(f, 5, () => Of(peer, EventCodes.Attack, mark).Any(p => L(p, 0) == bot.Id));
        var events = Since(peer, mark);
        var dismount = events.FindIndex(e => e.Code == EventCodes.Mounted && L(e.P, 0) == bot.Id);
        var attack = events.FindIndex(e => e.Code == EventCodes.Attack && L(e.P, 0) == bot.Id);
        var firstHealth = events.FindIndex(e => e.Code == EventCodes.HealthUpdate && L(e.P, 0) == bot.Id);
        Check(bot.IsMounted == false && dismount >= 0 && attack > dismount && (firstHealth < 0 || firstHealth > dismount),
              $"riding: the hit on the mount bar (no HealthUpdate of the bot before the dismount), Mounted (the dismount, at {dismount}) before its first Attack (at {attack})");
    }

    private static void EngageOfficial()
    {
        using var f = new Field(Quick with { PvpRules = BotPvpRules.Official }, BotTest.NoPopulation, Yellow);
        var nav = f.Nav();
        var trees = Trees();
        f.Join("Wanda", trees[0].Position - new Vector2(2, 2)); // the map in use
        var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 1);
        f.Run(1);
        var (_, peer, red) = f.Join("Otto", nav.Grid.RandomNear(new Random(2), bot.PositionAt(f.World.Bots.Time), 2, 3)!.Value, Pvp.Red);
        var mark = peer.Events.Count;
        Hit(f, red, bot, 5);
        Until(f, 5, () => Of(peer, EventCodes.HealthUpdate, mark).Any(p => L(p, 0) == red.Id && L(p, 6) == bot.Id));
        Check(red.PvpState.HitBy.ContainsKey(bot) && !Death.Attackers(red).Any(a => a.Player == bot),
              "its hit on the player is kept (HitBy) but never counts as a PvP attacker (no assist, fame share or goal for a bot)");
        f.Run(22);
        Check(Pvp.Of(bot, red) == Relation.Hostile && !bot.Bot!.Fight.Active && red.Combat.Down is null,
              $"Hostile both ways, yet 22 s after the one hit the fight is over ({bot.Bot!.Brain.State}, down {red.Combat.Down?.Kind})");
    }

    private static void Retarget()
    {
        using var f = new Field(Quick, BotTest.NoPopulation, Yellow);
        var nav = f.Nav();
        var trees = Trees();
        f.Join("Wanda", trees[0].Position - new Vector2(2, 2)); // the map in use
        var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 1);
        f.Run(1);
        var here = bot.PositionAt(f.World.Bots.Time);
        var (_, _, first) = f.Join("Ada", nav.Grid.RandomNear(new Random(3), here, 2, 3)!.Value, Pvp.Red);
        var (_, _, second) = f.Join("Bo", nav.Grid.RandomNear(new Random(4), here, 2, 3)!.Value, Pvp.Red);
        Hit(f, first, bot, 5);
        Hit(f, second, bot, 50);
        f.Run(0.5);
        Check(bot.Bot!.Fight.TargetId == second.Id, $"the first hitter was its target; 10 x its damage turns it to the second ({bot.Bot.Fight.TargetId})");
    }

    private static void LeaveAttacked()
    {
        using var f = new Field(Quick with { GathererNodes = (1, 1) }, BotTest.NoPopulation, Yellow);
        var nav = f.Nav();
        var trees = Trees();
        f.Join("Wanda", trees[0].Position - new Vector2(15, 0)); // the map in use
        var bot = Gatherer(f, trees[0].Position + new Vector2(6, 0), 1);
        Until(f, 60, () => bot.Bot!.Brain.State.StartsWith("leave", StringComparison.Ordinal) && bot.Bot.Motion.Busy);
        f.Run(1);
        var (_, peer, red) = f.Join("Ivo", nav.Grid.RandomNear(new Random(5), bot.PositionAt(f.World.Bots.Time), 2, 3)!.Value, Pvp.Red);
        var mark = peer.Events.Count;
        Hit(f, red, bot, 5);
        f.Run(1);
        Until(f, 25, () => !bot.Bot!.Fight.Active);
        var after = bot.Map is not null && !Of(peer, EventCodes.Leave, mark).Any(p => L(p, 0) == bot.Id);
        var at = bot.PositionAt(f.World.Bots.Time);
        Until(f, 150, () =>
        {
            if (bot.Map is not null) at = bot.PositionAt(f.World.Bots.Time);
            return bot.Map is null;
        });
        var exit = nav.Exits.Min(e => Math.Min(Vector2.Distance(at, e.Gate), Vector2.Distance(at, e.Position)));
        Check(after && bot.Map is null && exit <= CityBrain.ExitReach + 0.5,
              $"the fight over, it is still there ({after}), then walks out: gone {exit:0.#} m from an exit (state {bot.Bot!.Brain.State})");
    }
}
