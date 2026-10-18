using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// StageE E0/E1 (analysis/work/stageE/pvp/SPEC.md §1.2, §2.7): the hostility table and the archetype rules of two clusters, then a
/// duel between two <see cref="FakePeer"/> players in a city map (PvpDisabled, the client data's items, spells and game data, no
/// cluster files) on a <see cref="ManualClock"/>: challenge and its codes, deny, accept, the post and DuelStarted (also for a late
/// observer), no hit before the start, only the opponent attackable, the duel knockdown and its get-up, the area rules.
/// </summary>
public static class DuelTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("pvp: the hostility table (city, green, yellow, red, black; party, factions, Caerleon), archetype rules of 0000 and 0204", Relations),
        ("duel: challenge 183 / 4402 / 4405 / 4406 / 4401, deny 187, accept (post, 185 for both, 300 s), late observer, no hit before the start, the opponent only, knockdown (165, 186 ×2, post Leave, lethal hit, wager), get-up 6 s (88 energy kept, 996), left area 189/190/16, logout 18", Duel),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static void Relations()
    {
        const byte blue = Pvp.Blue, red = Pvp.Red, forest = 2, swamp = 5, caer = Pvp.Caerleon;
        Relation R(byte a, byte b, ClusterPvp rules, bool party = false) => Pvp.Of(a, b, party, guild: false, rules);
        var green = new ClusterPvp(PvpRule.FactionPvpOnly, PlayersAreLethal: false);
        var yellow = new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false);
        var redZone = new ClusterPvp(PvpRule.PvpAllowed);
        var black = new ClusterPvp(PvpRule.PvpForced);
        Check(R(red, blue, ClusterPvp.Unresolved) == Relation.Friendly && !ClusterPvp.Unresolved.AllowDuel, "a cluster not resolved: fail safe, no PvP and no duels");
                Check(R(red, blue, ClusterPvp.City) == Relation.Friendly && R(forest, swamp, ClusterPvp.City) == Relation.Friendly, "a city: everyone Friendly");
        Check(R(forest, swamp, green) == Relation.Hostile && R(forest, forest, green) == Relation.Friendly && R(blue, forest, green) == Relation.Neutral
              && R(forest, blue, green) == Relation.Friendly && R(forest, swamp, green, party: true) == Relation.Neutral
              && R(caer, caer, green) == Relation.Hostile && R(caer, caer, green, party: true) == Relation.Friendly,
              "green: enemy factions Hostile (Neutral in one party), same faction and Blue Friendly, Caerleon pairs Hostile outside a party");
        Check(R(blue, blue, yellow) == Relation.Friendly && R(red, blue, yellow) == Relation.Hostile && R(blue, red, yellow) == Relation.Hostile
              && R(blue, red, yellow, party: true) == Relation.Neutral && R(red, blue, yellow, party: true) == Relation.Friendly && R(red, red, yellow) == Relation.Hostile
              && R(forest, red, yellow) == Relation.Neutral && R(forest, red, redZone) == Relation.Hostile && R(red, swamp, yellow) == Relation.Neutral
              && R(forest, swamp, yellow) == Relation.Hostile && R(blue, forest, yellow) == Relation.Neutral,
              "yellow/red: Red against Blue both ways, factions against Red only where players are lethal, enemy factions Hostile");
        Check(R(blue, blue, black) == Relation.Hostile && R(blue, blue, black, party: true) == Relation.Friendly, "black: everyone but the party Hostile");
        var store = Client.Clusters!;
        var city = store.Get("0000").Pvp;
        var zone = store.Get("0204").Pvp;
        Check(city is { Rule: PvpRule.PvpDisabled, PlayersAreLethal: false, AllowDuel: true } && zone is { Rule: PvpRule.FactionPvpOnly, PlayersAreLethal: false }
              && zone.KnockdownBuffAfterGetUp == Knockdown.BuffSpell,
              $"worldsettings: 0000 a city (got {city}), 0204 green with SPAWN_KNOCKDOWN_INVULNERABILITY after the get-up (got {zone})");
        var duel = Client.GameData!.Duel;
        Check(duel == DuelSettings.Default && Client.GameData.Death is { DuelSeconds: 6, PvpSeconds: 180 }, $"gamedata DuelSettings and the duel/PvP get-up times (got {duel})");
    }

    private sealed record Duelist(WorldSession Session, FakePeer Peer, Player Player);

    private static void Duel()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var anchor = GameTime.Now;
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, GameTicks = () => anchor + clock.Now.Ticks, PvpFallback = ClusterPvp.City,
        };
        try
        {
            Duelist Join(string name, float x, long silver)
            {
                var c = StarterKit.Default.NewCharacter(name);
                (c.Cluster, c.PositionX, c.PositionY, c.Silver) = ("0000", x, 5, silver);
                c.Equipment[0] = new InventoryItem("T4_MAIN_SWORD");
                Check(characters.Create(c), $"character {name} created");
                var peer = new FakePeer();
                var session = new WorldSession(peer, world);
                peer.Disconnected = session.OnDisconnected;
                session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
                return new Duelist(session, peer, session.Player!);
            }
            void Run(double seconds)
            {
                for (var i = 0; i < Math.Round(seconds * 20); i++)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(50));
                    lock (world.Sync) world.Update();
                }
            }
            OperationResponse Call(Duelist d, AlbionRequest req)
            {
                var n = d.Peer.Responses.Count;
                d.Session.OnRequest(Request(req, 7), encrypted: false);
                Check(d.Peer.Responses.Count == n + 1, $"one response to {req.GetType().Name}");
                return d.Peer.Responses[^1];
            }
            short Challenge(Duelist from, Duelist to, long wager) => Call(from, new DuellingChallengePlayerRequest { bqx = to.Player.Id, bqy = wager }).ReturnCode;
            short? Attack(Duelist from, Duelist to)
            {
                var n = from.Peer.Responses.Count;
                Send(from.Session, new AttackStartRequest { wf = world.GameNow, co = to.Player.Id, cp = 0 });
                return from.Peer.Responses.Count > n ? from.Peer.Responses[^1].ReturnCode : null;
            }
            void Place(Duelist d, float x) { lock (world.Sync) d.Player.Teleport(new Vector2(x, 5), 0, DateTime.UtcNow); }
            const long wager = 10000, silver = 50000;

            var a = Join("Ann", 5, silver);
            var b = Join("Bob", 15, silver);
            var c = Join("Cid", 8, 0);
            var d = Join("Dan", 35, silver);
            Check(world.Parties.Invite(a.Player, c.Player.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(c.Player, true, false, Parties.NoReason) == 0,
                  "Cid joins Ann's party");
            Check(a.Player.Observers.Contains(b.Player) && a.Player.Observers.Contains(d.Player) && c.Player.Observers.Contains(d.Player), "all four see each other");
            Check(Pvp.DefenseFactor(a.Player, world.GameNow) == 1 && Math.Abs(SpellEffects.TakenFactor(a.Player, fromPlayer: true, world.GameNow, extra: 1) - 0.5) < 1e-9,
                  "bonusdefensevsplayers 1 halves a player's damage (1 / (1 + Σ), as mobs' hits)");

            // 1. A challenges B at 10 m: rc 0 {0 B, 1 10000}; B gets 183 {0 A, 1 10000}. The codes.
            var bm = b.Peer.Events.Count;
            var r = Call(a, new DuellingChallengePlayerRequest { bqx = b.Player.Id, bqy = wager });
            Check(r.ReturnCode == 0 && L(r.Parameters, 0) == b.Player.Id && L(r.Parameters, 1) == wager, "A: rc 0 {0 B, 1 10000}");
            Check(Since(b.Peer, bm).Where(e => e.Code == EventCodes.DuellingChallengePlayer).ToList() is [var ch] && L(ch.P, 0) == a.Player.Id && L(ch.P, 1) == wager,
                  "B: DuellingChallengePlayer {0 A, 1 10000}");
            Check(Challenge(a, d, 0) == Duels.YouHaveDuelPending, "a second challenge while one is open: 4402");
            Check(Challenge(c, d, 0) == Duels.PlayerOutOfRange && Challenge(c, b, wager) == Duels.NotEnoughSilver && Challenge(d, b, 0) == Duels.PlayerHasDuelPending,
                  "27 m: 4405; a challenger without the wager: 4406; a target already challenged: 4407");
            // 7. B denies: rc 0, A gets DuelDenied {0 A, 1 B}; a new challenge within 10 s is refused, after it sent.
            var am = a.Peer.Events.Count;
            Check(Call(b, new DuellingDenyChallengeRequest()).ReturnCode == 0 && Since(a.Peer, am).Where(e => e.Code == EventCodes.DuelDenied).ToList() is [var den]
                  && L(den.P, 0) == a.Player.Id && L(den.P, 1) == b.Player.Id, "deny: rc 0, A gets DuelDenied {0 A, 1 B}");
            Check(Call(b, new DuellingDenyChallengeRequest()).ReturnCode == Duels.NoActiveDuel && Call(b, new DuellingAcceptChallengeRequest()).ReturnCode == Duels.NoActiveDuel,
                  "deny or accept with nothing open: 4445");
            Check(Challenge(a, b, wager) == Duels.ChallengeFailed, "again within IssueDuelIntervalTimeInSeconds: refused");
            Run(10);
            Check(Challenge(a, b, wager) == 0, "10 s later: sent");

            // 2. B accepts: rc 0; the post, DuelStarted for A {2 true} and B (no key 2) to both and the watchers; k3 = now + 10 s, k4 − k3 = 300 s.
            var marks = new[] { a, b, c, d }.Select(x => x.Peer.Events.Count).ToArray();
            var acceptAt = world.GameNow;
            Check(Call(b, new DuellingAcceptChallengeRequest()).ReturnCode == 0, "accept: rc 0");
            var duel = world.Duels.Of(a.Player)!;
            Check(duel is not null && world.Duels.OpponentOf(b.Player) == a.Player && a.Player.Character.Silver == silver - wager && b.Player.Character.Silver == silver - wager,
                  "the duel is on, both wagers put in");
            foreach (var (x, i) in new[] { a, b, c, d }.Select((x, i) => (x, i)))
            {
                var ev = Since(x.Peer, marks[i]).Where(e => e.Code is EventCodes.NewDuellingPost or EventCodes.DuelStarted).ToList();
                Check(ev is [var post, var sa, var sb] && post.Code == EventCodes.NewDuellingPost && L(post.P, 0) == duel!.PostId
                      && sa.Code == EventCodes.DuelStarted && L(sa.P, 0) == a.Player.Id && L(sa.P, 1) == b.Player.Id && sa.P[2] is true
                      && L(sa.P, 3) == acceptAt + 10 * TimeSpan.TicksPerSecond && L(sa.P, 4) - L(sa.P, 3) == 300 * TimeSpan.TicksPerSecond
                      && L(sb.P, 0) == b.Player.Id && L(sb.P, 1) == a.Player.Id && !sb.P.ContainsKey(2) && L(sb.P, 3) == L(sa.P, 3),
                      $"{x.Player.Character.Name}: NewDuellingPost, DuelStarted A {{2 true}}, DuelStarted B, start now + 10 s, 300 s ({Show(ev)})");
            }
            // A late observer: NewCharacter, then DuelStarted of that duelist; the post once.
            var e = Join("Eve", 10, 0);
            var late = Since(e.Peer, 0).Where(x => x.Code is EventCodes.NewCharacter or EventCodes.NewDuellingPost or EventCodes.DuelStarted).ToList();
            var ia = late.FindIndex(x => x.Code == EventCodes.NewCharacter && L(x.P, 0) == a.Player.Id);
            var ib = late.FindIndex(x => x.Code == EventCodes.NewCharacter && L(x.P, 0) == b.Player.Id);
            Check(ia >= 0 && ib >= 0 && late.Count(x => x.Code == EventCodes.NewDuellingPost) == 1
                  && late.FindIndex(x => x.Code == EventCodes.DuelStarted && L(x.P, 0) == a.Player.Id) > ia
                  && late.FindIndex(x => x.Code == EventCodes.DuelStarted && L(x.P, 0) == b.Player.Id) > ib,
                  $"Eve: NewCharacter then DuelStarted for each duelist, the post once ({Show(late)})");
            var newA = Wire(e.Peer.Events.First(x => Code(x) == (int)EventCodes.NewCharacter && L(Wire(x), 0) == a.Player.Id));
            Check(Convert.ToInt32(newA[39]) == Pvp.NeutralLevel && !newA.ContainsKey(53) && !newA.ContainsKey(56), "NewCharacter: 39 NEUTRAL (4), no flag (Blue) and no faction");

            // 3. Before the start no hit; after it A hits B; C (A's party) and D stay unattackable in the city.
            Place(b, 7);
            var full = b.Player.Health.Max;
            Check(Attack(a, b) == WorldSession.AttackInvalidTarget, "AttackStart on the opponent before the start: -501");
            Run(1);
            Check(b.Player.Health.At(world.GameNow) == full, "no damage before the start");
            Run(9.1);
            Check(Attack(a, b) is null, "after the start: accepted");
            Run(1);
            Check(b.Player.Health.At(world.GameNow) < full, "B is hit");
            Place(c, 6);
            Place(d, 6);
            Check(Attack(a, c) == WorldSession.AttackInvalidTarget && Attack(a, d) == WorldSession.AttackInvalidTarget && Attack(c, b) == WorldSession.AttackInvalidTarget,
                  "in the city the party member, an outsider and another's duelist are not attackable");

            // 4. B at 0: combo (k5 = max / 6), KnockedDown {2 A, 4 B}, DuelEnded B then A {1 11, 2 10000}, the post's Leave, the lethal HealthUpdate.
            var durability = b.Player.Inventory.Equipment[0]!.DurabilityRaw;
            lock (world.Sync)
            {
                var now = Math.Max(world.GameNow, Math.Max(b.Player.Health.Stamp, b.Player.Energy.Stamp));
                b.Player.Health.Set(1, now);
                b.Player.Energy.Set(10, now);
            }
            Run(1);
            bm = b.Peer.Events.Count;
            am = a.Peer.Events.Count;
            Check(Attack(a, b) is null, "the last swing");
            Run(1);
            Check(b.Player.Combat.Down is { Kind: KnockdownKind.Duel } && world.Duels.Of(a.Player) is null, "B is down by the duel, which is over");
            var seen = Since(b.Peer, bm).Where(x => x.Code is EventCodes.RegenerationPlayerComboChanged or EventCodes.KnockedDown or EventCodes.DuelEnded or EventCodes.Leave
                || x.Code == EventCodes.HealthUpdate && L(x.P, 0) == b.Player.Id).ToList();
            var combo = seen.FindIndex(x => x.Code == EventCodes.RegenerationPlayerComboChanged && L(x.P, 0) == b.Player.Id);
            var kd = seen.FindIndex(x => x.Code == EventCodes.KnockedDown);
            Check(combo >= 0 && Math.Abs(F(seen[combo].P, 5) - full / 6) < 0.01 && kd > combo && L(seen[kd].P, 2) == a.Player.Id && L(seen[kd].P, 4) == b.Player.Id
                  && seen[kd].P[3] is "Ann",
                  $"combo with max / 6, then KnockedDown {{2 A, 3 Ann, 4 B}} ({Show(seen)})");
            var rest = seen.Skip(kd + 1).Where(x => x.Code != EventCodes.RegenerationPlayerComboChanged).ToList();
            Check(rest is [var e1, var e2, var leave, var hit] && e1.Code == EventCodes.DuelEnded && L(e1.P, 0) == b.Player.Id && L(e1.P, 1) == 11 && L(e1.P, 2) == wager
                  && e2.Code == EventCodes.DuelEnded && L(e2.P, 0) == a.Player.Id && L(e2.P, 1) == 11 && leave.Code == EventCodes.Leave && L(leave.P, 0) == duel!.PostId
                  && hit.Code == EventCodes.HealthUpdate && L(hit.P, 6) == a.Player.Id && !hit.P.ContainsKey(3),
                  $"DuelEnded B, DuelEnded A (11, 10000), the post's Leave, the lethal HealthUpdate ({Show(rest)})");
            Check(a.Player.Character.Silver == silver + wager && b.Player.Character.Silver == silver - wager && b.Player.Inventory.Equipment[0]!.DurabilityRaw == durability,
                  "the challenger wins the wager; B's sword keeps its durability");
            Check(Since(a.Peer, am).Any(x => x.Code == EventCodes.UpdateMoney) && Since(a.Peer, am).Any(x => x.Code == EventCodes.InCombatStateUpdate && !x.P.ContainsKey(1)),
                  "A: UpdateMoney and its combat end");

            // 5. 6 s later: Respawn {4 max, 9 the energy as it is} and CastSpell 996 on B.
            bm = b.Peer.Events.Count;
            Run((b.Player.Combat.Down!.At + TimeSpan.FromSeconds(5.9) - world.Now).TotalSeconds);
            Check(b.Player.Combat.Down is not null, "still down before 6 s");
            Run(0.15);
            var up = Since(b.Peer, bm);
            Check(up.FirstOrDefault(x => x.Code == EventCodes.Respawn).P is { } respawn && F(respawn, 4) == F(respawn, 5) && F(respawn, 9) >= 10 && F(respawn, 9) < F(respawn, 10)
                  && up.Any(x => x.Code == EventCodes.CastSpell && L(x.P, 0) == b.Player.Id && L(x.P, 1) == b.Player.Id && L(x.P, 3) == Client.Spells!.IdOf("DUEL_SPAWN_KNOCKDOWN")),
                  $"get-up: Respawn with full health and the energy kept, CastSpell DUEL_SPAWN_KNOCKDOWN ({Show(up.Where(x => x.Code is EventCodes.Respawn or EventCodes.CastSpell))})");

            // 6. A second duel: B 26 m from the post gets DuelLeftArea {1 now + 10 s}, back DuelReEnteredArea; 10 s out: 16, the challenger wins.
            Place(b, 7);
            Check(Challenge(a, b, 0) == 0 && Call(b, new DuellingAcceptChallengeRequest()).ReturnCode == 0, "a second duel, no wager");
            Run(10.1);
            var postAt = world.Duels.Of(b.Player)!.Post;
            bm = b.Peer.Events.Count;
            am = a.Peer.Events.Count;
            Place(b, postAt.X + 26);
            var outAt = world.GameNow;
            Run(0.05);
            Check(Since(a.Peer, am).Where(x => x.Code == EventCodes.DuelLeftArea).ToList() is [var left] && L(left.P, 0) == b.Player.Id
                  && L(left.P, 1) == outAt + 50 * TimeSpan.TicksPerMillisecond + 10 * TimeSpan.TicksPerSecond && Since(b.Peer, bm).Any(x => x.Code == EventCodes.DuelLeftArea),
                  "out of the area: DuelLeftArea {0 B, 1 now + 10 s} to both");
            Place(b, postAt.X + 5);
            Run(0.05);
            Check(Since(a.Peer, am).Any(x => x.Code == EventCodes.DuelReEnteredArea && L(x.P, 0) == b.Player.Id), "back: DuelReEnteredArea");
            Place(b, postAt.X + 26);
            Run(9.9);
            Check(world.Duels.Of(b.Player) is not null, "not ended before 10 s out");
            am = a.Peer.Events.Count;
            Run(0.2);
            Check(world.Duels.Of(b.Player) is null && Since(a.Peer, am).Where(x => x.Code == EventCodes.DuelEnded).ToList() is [var l1, var l2]
                  && L(l1.P, 0) == b.Player.Id && L(l1.P, 1) == 16 && L(l2.P, 1) == 16 && !l1.P.ContainsKey(2) && Duels.Winner(duel!, 16) == duel!.Challenger,
                  "10 s out: DuelEnded 16 (challengee left the area), no wager key, the challenger wins");

            // A logout ends the duel: ChallengeeDestroyed (18), the challenger wins the wager.
            Place(b, 7);
            Check(Challenge(a, b, wager) == 0 && Call(b, new DuellingAcceptChallengeRequest()).ReturnCode == 0, "a third duel");
            var before = a.Player.Character.Silver;
            am = a.Peer.Events.Count;
            b.Peer.Disconnect();
            Check(world.Duels.Of(a.Player) is null && Since(a.Peer, am).Where(x => x.Code == EventCodes.DuelEnded).Select(x => L(x.P, 1)).ToList() is [18, 18]
                  && a.Player.Character.Silver == before + 2 * wager, "B logs out: DuelEnded 18 to A, A takes both wagers");

            // Accept checks Challenge's conditions again (a fight since: 4446); a viewer that leaves the map forgets the post; a
            // server stop ends the duel as CanceledBySystem (20), both wagers back.
            Place(d, 9);
            Run(10);
            Check(Challenge(a, d, wager) == 0, "a fourth challenge, A to D");
            lock (world.Sync) Combat.EnterCombat(a.Player, null);
            Check(Call(d, new DuellingAcceptChallengeRequest()).ReturnCode == Duels.AcceptFailed, "the challenger in combat since: 4446");
            lock (world.Sync) Combat.EndCombat(a.Player);
            Check(Call(d, new DuellingAcceptChallengeRequest()).ReturnCode == 0, "out of combat: accepted");
            var fourth = world.Duels.Of(a.Player)!;
            Check(fourth.PostKnown.Contains(e.Player), "Eve knows the post");
            lock (world.Sync) world.Duels.Left(e.Player);
            Check(!fourth.PostKnown.Contains(e.Player), "Eve leaves the map: she forgets it");
            (before, am) = (a.Player.Character.Silver, a.Peer.Events.Count);
            var dSilver = d.Player.Character.Silver;
            lock (world.Sync) world.Duels.CancelAll();
            Check(world.Duels.Of(a.Player) is null && Since(a.Peer, am).Where(x => x.Code == EventCodes.DuelEnded).Select(x => L(x.P, 1)).ToList() is [20, 20]
                  && a.Player.Character.Silver == before + wager && d.Player.Character.Silver == dSilver + wager, "the server stops: DuelEnded 20, both wagers back");
        }
        finally
        {
            world.Dispose();
            characters.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
