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
/// StageE E2 (analysis/work/stageE/pvp/SPEC.md §3.6): the reputation levels and recovery, the safe-zone rules, the saved flag;
/// then <see cref="FakePeer"/> players on a <see cref="ManualClock"/> in a YELLOW map (PvpAllowed, not lethal, 90 s PvP get-up)
/// and a GREEN one (the client data's items, spells and game data, no cluster files): the 15 s flag cast and its interruption, Red
/// against Blue with the attack penalty and OUTLAW, Blue hitting back, the party, the PvP knockdown (item wear, KillRewardedNoFame,
/// the knockdown penalty, the get-up after 90 s with 995), and the faction flags of 0 s.
/// </summary>
public static class OpenPvpTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("pvp E2: reputation levels and recovery, Join/NewCharacter keys, the saved flag and reputation, safe-zone and entry rules", Rules),
        ("pvp E2 yellow: flag cast 1037 (15 s, interrupted by damage), 364, CANNOTDISABLEPVP, Red on Blue (penalty -1, OUTLAW, 310), Blue back, party, knockdown (wear 5 %, 615, -5, 90 s, 995)", Yellow),
        ("pvp E2 green: faction flags at once (CastStart k4 = k1, CastSpell, 364), enemy factions Hostile, enlistment needed", Green),
        ("pvp E2 mounted: a Red hit on a mounted Blue goes to the mount bar, at 0 FORCE_DISMOUNTED_HOSTILE + Mounted 13; an attack ends a flag cast; OUTLAW over a relog", Mounted),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static void Rules()
    {
        var s = Client.GameData!.Reputation;
        Check(s.Levels.Count == 10 && s.LevelOf(0) == Pvp.NeutralLevel && s.LevelOf(520) == 3 && s.LevelOf(452) == 4 && s.LevelOf(1581) == 2
              && s.LevelOf(-1000) == 5 && s.LevelOf(-60000) == 9 && s.Levels[4].AttackPenalty == 1 && s.Levels[0].KillPenalty == 1000,
              "gamedata <Reputation>: the level of 0 / 520 / 452 / 1581 / -1000 / -60000 points (captured 4, 3, 4, 2), NEUTRAL's attack penalty 1");
        Check(Client.GameData.KnockedOutLoss is { MinFactor: 0.05, MaxFactor: 0.05, MinimumRemaining: 0.01 } loss && loss.For("rewardtoken").Max == 0,
              "gamedata AliveToKnockedOut: 5 % durability, 1 % left at least, rewardtoken none");

        var dir = NewDir();
        var characters = Repository(dir);
        var world = new World(new ServerConfig(), characters, Client.Items, Client.GameData, Client.Spells) { Sync = new object() };
        try
        {
            // The daily recovery of a negative level stops at NEUTRAL_NEGATIVE (rate 0).
            var c = StarterKit.Default.NewCharacter("Rex");
            var p = new Player(new WorldSession(new FakePeer(), world), world, 1, c, new Inventory(c.EquipmentContainer, c.InventoryContainer), null);
            var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            (c.Reputation, c.ReputationAt) = (-2500, t0.Ticks);
            Reputation.Recover(p, t0.AddDays(1));
            Check(Math.Abs(c.Reputation - -1500) < 1e-6, $"NEGATIVE01 recovers 1000 a day (got {c.Reputation})");
            Reputation.Recover(p, t0.AddDays(3));
            Check(Math.Abs(c.Reputation - -1000) < 1e-6, $"and stops at NEUTRAL_NEGATIVE (got {c.Reputation})");
            (c.Reputation, c.ReputationAt) = (-11000, t0.Ticks);
            Reputation.Recover(p, t0.AddDays(4));
            Check(Math.Abs(c.Reputation - -8500) < 1e-6, $"NEGATIVE03 500 a day up to -10000 (2 days), then NEGATIVE02's 750 (got {c.Reputation})");

            // Safe-zone and entry rules (SPEC §3.1, §3.4).
            c.Flag = Pvp.Red;
            Flagging.Arrive(p, new ClusterPvp(PvpRule.PvpAllowed));
            Check(p.Flag == Pvp.Red, "Red stays Red in a yellow cluster");
            Flagging.Arrive(p, ClusterPvp.City);
            Check(p.Flag == Pvp.Red, "Red stays Red in a city (captured in royal cities)");
            Flagging.Arrive(p, new ClusterPvp(PvpRule.FactionPvpOnly));
            Check(p.Flag == Pvp.Blue, "Red arrives Blue in a blue cluster (official)");
            c.Flag = 2;
            Check(Flagging.EntryRefusal(p, new ClusterPvp(PvpRule.PvpDisabled, AllowFactionFlaggedEnter: false))?.Code == Flagging.NoFactionPlayersAllowed
                  && Flagging.EntryRefusal(p, ClusterPvp.City) is null, "faction-flagged: refused where allowfactionflaggedenter is false (1655)");
            Check(Flagging.EntryRefusal(p, new ClusterPvp(PvpRule.PvpAllowed, RequiredReputationToEnter: -20000)) is null
                  && Flagging.EntryRefusal(p, new ClusterPvp(PvpRule.PvpAllowed, RequiredReputationToEnter: -1000))?.Code == Flagging.ReputationTooLow,
                  "-8500 points: in at -20000, ReputationTooLow (1652) at -1000");
            var archetypes = Client.Clusters!;
            Check(archetypes.Get("0000").Pvp.AllowFactionFlaggedEnter, "worldsettings: a royal city lets faction-flagged players in");

            // The flag, the faction and the reputation are saved.
            Check(characters.Create(c), "character created");
            (c.Flag, c.Faction, c.Reputation, c.ReputationAt) = (Pvp.Red, 5, -1234.5, t0.Ticks);
            Check(characters.Save(c) && characters.Find(c.Id) is { Flag: Pvp.Red, Faction: 5, Reputation: -1234.5 } back && back.ReputationAt == t0.Ticks,
                  "flag, faction and reputation survive a save");
        }
        finally
        {
            world.Dispose();
            characters.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private sealed record Fighter(WorldSession Session, FakePeer Peer, Player Player);

    /// <summary>A world on a manual clock whose maps (no cluster data) have <paramref name="rules"/>, and its helpers.</summary>
    private sealed class Arena : IDisposable
    {
        private readonly string _dir = NewDir();
        private readonly ManualClock _clock = new();
        public readonly SqliteCharacterRepository Characters;
        public readonly World World;

        public Arena(ClusterPvp rules)
        {
            Characters = Repository(_dir);
            var anchor = GameTime.Now;
            World = new World(new ServerConfig { JoinByCharacterId = true }, Characters, Client.Items, Client.GameData, Client.Spells)
            {
                Sync = new object(), Clock = _clock, GameTicks = () => anchor + _clock.Now.Ticks, PvpFallback = rules,
            };
        }

        public Fighter Join(string name, float x, Action<Character>? setup = null)
        {
            var c = StarterKit.Default.NewCharacter(name);
            (c.Cluster, c.PositionX, c.PositionY) = ("0000", x, 5);
            c.Equipment[0] = new InventoryItem("T4_MAIN_SWORD");
            setup?.Invoke(c);
            Check(Characters.Create(c), $"character {name} created");
            return Rejoin(c.Id);
        }

        public Fighter Rejoin(Guid id)
        {
            var peer = new FakePeer();
            var session = new WorldSession(peer, World);
            peer.Disconnected = session.OnDisconnected;
            session.OnRequest(Request(new JoinRequest { ss = id }, 1), encrypted: false);
            return new Fighter(session, peer, session.Player!);
        }

        public void Run(double seconds)
        {
            for (var i = 0; i < Math.Round(seconds * 20); i++)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(50));
                lock (World.Sync) World.Update();
            }
        }

        public short Flag(Fighter f, byte to)
        {
            var n = f.Peer.Responses.Count;
            f.Session.OnRequest(Request(new ChangeFlaggingPrepareRequest { eo = to }, 5), encrypted: false);
            Check(f.Peer.Responses.Count == n + 1, "one response to ChangeFlaggingPrepare");
            return f.Peer.Responses[^1].ReturnCode;
        }

        public short? Attack(Fighter from, Fighter to)
        {
            var n = from.Peer.Responses.Count;
            Send(from.Session, new AttackStartRequest { wf = World.GameNow, co = to.Player.Id, cp = 0 });
            return from.Peer.Responses.Count > n ? from.Peer.Responses[^1].ReturnCode : null;
        }

        public void Place(Fighter f, float x) { lock (World.Sync) f.Player.Teleport(new Vector2(x, 5), 0, DateTime.UtcNow); }

        public void Dispose()
        {
            World.Dispose();
            Characters.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    private static bool HasSpell(Dictionary<byte, object?> p, short id) => p.TryGetValue(1, out var v) && v is Array ids && ids.Cast<object>().Any(x => Convert.ToInt32(x) == id);

    private static void Yellow()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false, KnockdownSecondsPvp: 90,
            KnockdownBuffAfterGetUp: Knockdown.BuffSpell));
        var world = arena.World;
        var spells = Client.Spells!;
        var a = arena.Join("Ann", 5);
        var b = arena.Join("Bob", 7);
        var c = arena.Join("Cid", 6);
        var d = arena.Join("Dan", 4, ch => ch.Flag = Pvp.Red);
        Check(world.Parties.Invite(a.Player, c.Player.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(c.Player, true, false, Parties.NoReason) == 0,
              "Cid joins Ann's party");
        var joinD = d.Peer.Responses.First(r => r.Parameters.ContainsKey(49));
        Check(Convert.ToInt32(joinD.Parameters[49]) == Pvp.Red && Convert.ToInt32(joinD.Parameters[44]) == Pvp.NeutralLevel, "Dan joins Red (the saved flag, Join key 49), NEUTRAL (44)");

        // 1. Blue cannot hit Blue; the flag cast: CastStart {5 1037, 9 24, k4 − k1 = 15 s} to the others; damage interrupts it.
        Check(arena.Attack(a, b) == WorldSession.AttackInvalidTarget, "Blue A on Blue B: -501");
        Check(arena.Flag(a, 2) == Flagging.Refused && arena.Flag(a, Pvp.Blue) == Flagging.Refused, "a faction without enlistment, or the flag A has: refused");
        var bm = b.Peer.Events.Count;
        var am = a.Peer.Events.Count;
        Check(arena.Flag(a, Pvp.Red) == 0, "op 354 {0 255}: rc 0");
        var red = spells.IdOf("FLAG_RED");
        Check(Since(b.Peer, bm).Where(e => e.Code == EventCodes.CastStart).ToList() is [var cs] && L(cs.P, 0) == a.Player.Id && L(cs.P, 5) == red && L(cs.P, 9) == Flagging.Slot
              && L(cs.P, 7) == a.Player.Id && L(cs.P, 4) - L(cs.P, 1) == 15 * TimeSpan.TicksPerSecond && !Since(a.Peer, am).Any(e => e.Code == EventCodes.CastStart),
              "B sees CastStart {0 A, 5 FLAG_RED, 7 A, 9 24, 15 s}; A's own client casts it itself");
        Check(arena.Flag(a, Pvp.Red) == Flagging.Refused, "a second op 354 while it runs: refused");
        arena.Run(5);
        bm = b.Peer.Events.Count;
        am = a.Peer.Events.Count;
        Check(arena.Attack(d, a) is null, "Red D hits Blue A");
        arena.Run(1);
        Check(a.Player.PvpState.FlagCast is null && Since(b.Peer, bm).Any(e => e.Code == EventCodes.CastCancel && L(e.P, 0) == a.Player.Id)
              && Since(a.Peer, am).Any(e => e.Code == EventCodes.CastCancel && e.P[1] is true), "the hit cancels the flag cast (CastCancel to A and B)");
        arena.Run(15);
        Check(a.Player.Flag == Pvp.Blue && !Since(b.Peer, bm).Any(e => e.Code == EventCodes.ChangeFlaggingFinished), "no 364 after a cancelled cast");
        lock (world.Sync) Combat.EndCombat(a.Player);
        Check(arena.Flag(a, Pvp.Red) == 0, "out of combat: the flag cast again");
        arena.Run(14.9);
        Check(a.Player.Flag == Pvp.Blue, "still Blue before 15 s");
        bm = b.Peer.Events.Count;
        am = a.Peer.Events.Count;
        arena.Run(0.15);
        var done = Since(b.Peer, bm).Where(e => e.Code is EventCodes.CastSpell or EventCodes.ChangeFlaggingFinished).ToList();
        Check(a.Player.Flag == Pvp.Red && done is [var cast, var fin] && cast.Code == EventCodes.CastSpell && L(cast.P, 3) == red && fin.Code == EventCodes.ChangeFlaggingFinished
              && L(fin.P, 0) == a.Player.Id && L(fin.P, 1) == Pvp.Red && Since(a.Peer, am).Any(e => e.Code == EventCodes.ChangeFlaggingFinished),
              $"15 s later: CastSpell FLAG_RED then ChangeFlaggingFinished {{0 A, 1 255}} to B and A ({Show(done)})");
        Check(Pvp.SpellActive(a.Player, "CANNOTDISABLEPVP", world.GameNow) && arena.Flag(a, Pvp.Blue) == Flagging.Refused, "CANNOTDISABLEPVP is on: no Blue for 15 min");
        var e = arena.Join("Eve", 10);
        var newA = Wire(e.Peer.Events.First(x => Code(x) == (int)EventCodes.NewCharacter && L(Wire(x), 0) == a.Player.Id));
        Check(Convert.ToInt32(newA[53]) == Pvp.Red && Convert.ToInt32(newA[39]) == Pvp.NeutralLevel, "a late observer's NewCharacter of A: 53 = 255, 39 NEUTRAL");

        // 2. Red A hits Blue B: the attack penalty of B's level (-1, reason 3, 9 B's name), OUTLAW and 310; no second penalty within 120 s.
        var full = b.Player.Health.Max;
        am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        var hitAt = world.GameNow;
        Check(arena.Attack(a, b) is null, "Red A on Blue B: accepted");
        arena.Run(1);
        Check(b.Player.Health.At(world.GameNow) < full && Since(b.Peer, bm).Any(x => x.Code == EventCodes.HealthUpdate && L(x.P, 0) == b.Player.Id && L(x.P, 6) == a.Player.Id),
              "B is hit (HealthUpdate {6 A})");
        var gotA = Since(a.Peer, am);
        var outlaw = spells.IdOf("OUTLAW");
        Check(gotA.Where(x => x.Code == EventCodes.ReputationUpdate).ToList() is [var rep] && F(rep.P, 2) == -1 && F(rep.P, 3) == -1 && L(rep.P, 6) == Reputation.LostAttack
              && rep.P[9] is "Bob" && a.Player.Character.Reputation == -1, $"A: ReputationUpdate {{2 -1, 3 -1, 6 3, 9 Bob}} ({Show(gotA.Where(x => x.Code == EventCodes.ReputationUpdate))})");
        Check(gotA.Any(x => x.Code == EventCodes.ActiveSpellEffectsUpdate && L(x.P, 0) == a.Player.Id && HasSpell(x.P, outlaw))
              && Since(b.Peer, bm).Where(x => x.Code == EventCodes.ReputationImplicationUpdate).ToList() is [var imp] && L(imp.P, 0) == a.Player.Id
              && Math.Abs(L(imp.P, 1) - (hitAt + 180 * TimeSpan.TicksPerSecond)) < 2 * TimeSpan.TicksPerSecond && a.Player.PvpState.Implicated(world.GameNow),
              "OUTLAW on A and ReputationImplicationUpdate {0 A, 1 now + 180 s} to the observers");
        Check(!Since(b.Peer, bm).Any(x => x.Code == EventCodes.ReputationUpdate), "B gets no reputation line");
        am = a.Peer.Events.Count;
        Check(arena.Attack(a, b) is null, "a second hit");
        arena.Run(1);
        Check(!Since(a.Peer, am).Any(x => x.Code == EventCodes.ReputationUpdate) && a.Player.Character.Reputation == -1, "no second penalty within 120 s");

        // 3. Blue B hits back Red A: no penalty; C (A's party, Blue) cannot hit A.
        bm = b.Peer.Events.Count;
        var aHealth = a.Player.Health.At(world.GameNow);
        Check(arena.Attack(b, a) is null, "Blue B on Red A: accepted");
        arena.Run(1);
        Check(a.Player.Health.At(world.GameNow) < aHealth && !Since(b.Peer, bm).Any(x => x.Code == EventCodes.ReputationUpdate) && b.Player.Character.Reputation == 0,
              "A is hit, B loses nothing");
        Check(arena.Attack(c, a) == WorldSession.AttackInvalidTarget, "party member C on Red A: -501 (Neutral)");

        // 5. B at 0 from A: KnockedDown {2 A}, regeneration max / 90, the sword -5 %, KillRewardedNoFame and -5 to A, no Died; up after 90 s, 995 3 s later.
        var sword = b.Player.Inventory.Equipment[0]!;
        var durability = sword.DurabilityRaw;
        lock (world.Sync)
        {
            var now = Math.Max(world.GameNow, b.Player.Health.Stamp);
            b.Player.Health.Set(1, now);
        }
        arena.Run(1);
        am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        var em = e.Peer.Events.Count;
        Check(arena.Attack(a, b) is null, "the knocking-down swing");
        arena.Run(1);
        var gotB = Since(b.Peer, bm);
        Check(b.Player.Combat.Down is { Kind: KnockdownKind.Pvp } && gotB.FirstOrDefault(x => x.Code == EventCodes.KnockedDown).P is { } kd && L(kd, 2) == a.Player.Id
              && kd[3] is "Ann" && Since(e.Peer, em).Any(x => x.Code == EventCodes.KnockedDown && L(x.P, 0) == b.Player.Id),
              "B is down by A: KnockedDown {2 A, 3 Ann} to B and the observers");
        Check(gotB.Any(x => x.Code == EventCodes.RegenerationPlayerComboChanged && L(x.P, 0) == b.Player.Id && Math.Abs(F(x.P, 5) - full / 90) < 0.01)
              && !gotB.Any(x => x.Code == EventCodes.Died), "the knockdown regeneration is max / 90 (YELLOW), no Died");
        Check(sword.DurabilityRaw == durability - (long)Math.Round(sword.Template.FullDurabilityRaw * 0.05)
              && gotB.Any(x => x.Code == EventCodes.NewEquipmentItem && L(x.P, 0) == sword.Id && L(x.P, 8) == sword.DurabilityRaw),
              $"B's sword loses 5 % of its durability (NewEquipmentItem key 8; {durability} -> {sword.DurabilityRaw})");
        gotA = Since(a.Peer, am);
        Check(gotA.Any(x => x.Code == EventCodes.KillRewardedNoFame && L(x.P, 0) == a.Player.Id)
              && gotA.Where(x => x.Code == EventCodes.ReputationUpdate).ToList() is [var kdRep] && F(kdRep.P, 2) == -5 && L(kdRep.P, 6) == Reputation.LostKnockdown
              && a.Player.Character.Reputation == -6, $"A: KillRewardedNoFame and ReputationUpdate {{2 -5, 6 5}} ({Show(gotA.Where(x => x.Code is EventCodes.ReputationUpdate or EventCodes.KillRewardedNoFame))})");
        Check(arena.Attack(a, b) == WorldSession.AttackInvalidTarget, "no attack on a knocked-down player");
        arena.Run((b.Player.Combat.Down!.At + TimeSpan.FromSeconds(89.8) - world.Now).TotalSeconds);
        Check(b.Player.Combat.Down is not null, "still down before 90 s");
        bm = b.Peer.Events.Count;
        arena.Run(0.3);
        Check(b.Player.Combat.Down is null && Since(b.Peer, bm).FirstOrDefault(x => x.Code == EventCodes.Respawn).P is { } up && F(up, 4) == F(up, 5), "Respawn after 90 s with full health");
        arena.Run(3.1);
        Check(Since(b.Peer, bm).Any(x => x.Code == EventCodes.CastSpell && L(x.P, 0) == b.Player.Id && L(x.P, 3) == spells.IdOf(Knockdown.BuffSpell)), "3 s later CastSpell 995");

        // Saved: A's next Join is Red with its reputation (41 -6, 44 the level).
        var id = a.Player.Character.Id;
        a.Peer.Disconnect();
        var again = arena.Rejoin(id);
        var join = again.Peer.Responses.First(r => r.Parameters.ContainsKey(49));
        Check(Convert.ToInt32(join.Parameters[49]) == Pvp.Red && Convert.ToSingle(join.Parameters[41]) == -6 && Convert.ToInt32(join.Parameters[44]) == 5,
              "A joins again: 49 Red, 41 -6, 44 NEUTRAL_NEGATIVE (5)");
    }

    private static void Mounted()
    {
        Check(!ClusterPvp.Of(System.Xml.Linq.XElement.Parse("<a reputationgainenabled=\"false\"/>")).ReputationGainEnabled && ClusterPvp.Default.ReputationGainEnabled,
              "worldsettings reputationgainenabled (default true)");
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false));
        var world = arena.World;
        var a = arena.Join("Ann", 5, ch => ch.Flag = Pvp.Red);
        var b = arena.Join("Bob", 7, ch => ch.Equipment[Mounts.MountSlot] = new InventoryItem("T5_MOUNT_OX"));
        var c = arena.Join("Cid", 6);
        Send(b.Session, new MountRequest { wf = world.GameNow, v3 = Mounts.ItemOf(b.Player)!.Id, v4 = true });
        arena.Run(3.1);
        Check(b.Player.IsMounted, "Blue B rides its ox");

        // 1. A's hit lands on the mount bar; B stays mounted with its health.
        var (health, bar) = (b.Player.Health.At(world.GameNow), b.Player.MountHealth.At(world.GameNow));
        var bm = b.Peer.Events.Count;
        lock (world.Sync) Pvp.Damage(a.Player, b.Player, 100, EffectType.Physical, HitOrigin.MeleeAutoAttack, -1, world.GameNow);
        Check(b.Player.IsMounted && b.Player.MountHealth.At(world.GameNow) < bar && b.Player.Health.At(world.GameNow) == health
              && Since(b.Peer, bm).Any(x => x.Code == EventCodes.MountHealthUpdate && L(x.P, 6) == a.Player.Id) && !Since(b.Peer, bm).Any(x => x.Code == EventCodes.HealthUpdate)
              && a.Player.Character.Reputation == -1, "the mount bar takes the hit (312 {6 A}, no HealthUpdate), still mounted; the crime costs -1");

        // 2. The bar at 0: the hostile forced dismount.
        bm = b.Peer.Events.Count;
        lock (world.Sync) Pvp.Damage(a.Player, b.Player, 100000, EffectType.Physical, HitOrigin.MeleeAutoAttack, -1, world.GameNow);
        var got = Since(b.Peer, bm);
        Check(!b.Player.IsMounted && got.Any(x => x.Code == EventCodes.CastSpell && L(x.P, 3) == Client.Spells!.IdOf("FORCE_DISMOUNTED_HOSTILE"))
              && got.FirstOrDefault(x => x.Code == EventCodes.Mounted).P is { } m && m[13] is true && b.Player.Health.At(world.GameNow) == health,
              $"at 0: CastSpell FORCE_DISMOUNTED_HOSTILE, Mounted {{13 true}}, the health untouched ({Show(got.Where(x => x.Code is EventCodes.CastSpell or EventCodes.Mounted))})");

        // 3. C's own attack ends its flag cast.
        Check(arena.Flag(c, Pvp.Red) == 0 && c.Player.PvpState.FlagCast is not null, "C starts flagging Red");
        Check(arena.Attack(c, a) is null && c.Player.PvpState.FlagCast is null, "C attacks Red A: the flag cast ends");

        // 4. A's implication comes back from the saved OUTLAW at the next Join.
        Check(a.Player.PvpState.Implicated(world.GameNow) && Pvp.SpellActive(a.Player, "OUTLAW", world.GameNow), "A is implicated (OUTLAW)");
        lock (world.Sync) world.Saves.SaveNow(a.Player.Character);
        var id = a.Player.Character.Id;
        a.Peer.Disconnect();
        var again = arena.Rejoin(id);
        Check(again.Player.PvpState.Implicated(world.GameNow), "A joins again still implicated");
    }

    private static void Green()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.FactionPvpOnly, PlayersAreLethal: false));
        var world = arena.World;
        var a = arena.Join("Ann", 5, ch => ch.Faction = 2);
        var b = arena.Join("Bob", 7, ch => ch.Faction = 5);
        Check(arena.Flag(a, 5) == Flagging.Refused, "green: a faction A is not enlisted to refused");
        var bm = b.Peer.Events.Count;
        var am = a.Peer.Events.Count;
        Check(arena.Flag(a, 2) == 0, "A flags Forest (2)");
        var forest = Client.Spells!.IdOf("FLAG_FOREST");
        var seen = Since(b.Peer, bm).Where(x => x.Code is EventCodes.CastStart or EventCodes.CastSpell or EventCodes.ChangeFlaggingFinished).ToList();
        Check(seen is [var start, var cast, var fin] && start.Code == EventCodes.CastStart && L(start.P, 5) == forest && L(start.P, 4) == L(start.P, 1)
              && cast.Code == EventCodes.CastSpell && fin.Code == EventCodes.ChangeFlaggingFinished && L(fin.P, 1) == 2 && a.Player.Flag == 2
              && Since(a.Peer, am).Any(x => x.Code == EventCodes.ChangeFlaggingFinished),
              $"castingtime 0: CastStart k4 = k1, CastSpell and 364 {{1 2}} at once ({Show(seen)})");
        Check(Pvp.Of(a.Player, b.Player) == Relation.Friendly && arena.Attack(a, b) == WorldSession.AttackInvalidTarget, "Forest A and Blue B: Friendly");
        Check(arena.Flag(b, 5) == 0 && b.Player.Flag == 5 && Pvp.Of(a.Player, b.Player) == Relation.Hostile && Pvp.Of(b.Player, a.Player) == Relation.Hostile,
              "B flags Swamp (5): the enemy factions are Hostile in green");
        Check(Pvp.Of(a.Player.Flag, b.Player.Flag, false, false, ClusterPvp.City) == Relation.Friendly, "the same two in a city: Friendly");
        var hp = b.Player.Health.At(world.GameNow);
        Check(arena.Attack(a, b) is null, "Forest A on Swamp B: accepted");
        arena.Run(1);
        Check(b.Player.Health.At(world.GameNow) < hp && a.Player.Character.Reputation == 0 && !a.Player.PvpState.Implicated(world.GameNow),
              "B is hit; a faction fight is no crime");
        lock (world.Sync) Combat.EndCombat(a.Player);
        Check(arena.Flag(a, Pvp.Blue) == 0 && a.Player.Flag == 2, "A unflags: FLAG_BLUE takes its 15 s");
        arena.Run(15.1);
        Check(a.Player.Flag == Pvp.Blue && Pvp.Of(a.Player, b.Player) == Relation.Neutral && arena.Attack(a, b) == WorldSession.AttackInvalidTarget,
              "15 s later A is Blue: Swamp B is Neutral to it, not attackable");
    }
}
