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
/// Stage I PvP goals (analysis/work/stageI/pvp-goals/SPEC.md §4): content/goals.json's Pvp section and the journal's PvP leaves; then
/// <see cref="FakePeer"/> players in Thetford under a red zone's rules (<see cref="World.PvpOverride"/>) with the pve preset (Consensual,
/// no deaths) on a <see cref="ManualClock"/>: a real knockdown (615, then 376 of 50 points, the tracked journal entry, the victim's
/// attackers cleared) and one with Pvp:KnockdownFameFactor; the hook's anti-farming rules, assists, the daily cap, the bounties and the
/// spree on direct <see cref="PvpGoals.OnPvpKill"/> calls; a real duel won after its minimum length.
/// </summary>
public static class PvpGoalTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("pvp goals: goals.json Pvp (20 + 5/tier, cap 600, red 1 / black 1.5), a bad zone key refused, the PvP journal leaves and filters supported", Data),
        ("pvp goals: a red-zone knockdown under the pve preset: 615 then 376 {500000}, KILL_PLAYER_02 600 {1}, HitBy kept while down; its execute one result; KnockdownFameFactor 0.5: UpdateFame, no 163, KILLFAME_01", Knockdown),
        ("pvp goals: the hook's refusals (cooldown, 3 a day, same account but not the unclaimed local one, tier without the mount, crime, duel length and pair), assists 25 / none, cap 60 then the next day, bounties, spree 3", Hook),
        ("pvp goals: a real duel won after 21 s: 15 points to the winner, DUEL_WIN_01 and DUEL completed (362)", Duel),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static readonly ClusterPvp RedZone = new(PvpRule.PvpAllowed, WarningType: "red");

    private static GoalSettings Shipped => GoalSettings.Load(Path.Combine(DataDir(), "..", "content", "goals.json"));

    private sealed record Bot(string PersonaId, int Tier) : IPvpBot
    {
        public string Name => "Bot " + PersonaId;
        public byte Flag => Pvp.Red;
        public long Id => 900000;
    }

    /// <summary>Thetford's map under <see cref="RedZone"/>, the pve preset, the shipped goals (no bounties unless a test sets them).</summary>
    private sealed class Field : IDisposable
    {
        private readonly string _dir = NewDir();
        public readonly ManualClock Clock = new();
        public readonly SqliteCharacterRepository Characters;
        public readonly World World;

        public Field()
        {
            Characters = new SqliteCharacterRepository(Path.Combine(_dir, "albionlocal.db"));
            var store = Client.Clusters!;
            _ = store.Get("0000");
            _ = store.Journal;
            _ = store.Seasons;
            var anchor = GameTime.Now;
            World = new World(ServerConfig.PveDefaults with { JoinByCharacterId = true, Bots = new() }, Characters, Client.Items, Client.GameData, Client.Spells) // no bots: the test counts events
            {
                Sync = new object(), Clock = Clock, Clusters = store, GameTicks = () => anchor + Clock.Now.Ticks, PvpOverride = RedZone,
                Goals = Shipped with { Bounties = new BountyGoals { Enabled = false } },
            };
        }

        /// <summary>
        /// A Red character with only <paramref name="weapon"/> (and <paramref name="mount"/>) at (x, 0) of an account of its own
        /// (<paramref name="account"/>: that one; Guid.Empty: none, as a legacy character), joined.
        /// </summary>
        public (WorldSession Session, FakePeer Peer, Player Player) Join(string name, float x = 0, string weapon = "T6_MAIN_SWORD", Guid? account = null, string? mount = null)
        {
            var c = StarterKit.Default.NewCharacter(name);
            (c.Cluster, c.PositionX, c.PositionY, c.Flag) = ("0000", x, 0, Pvp.Red);
            c.Account = account ?? Characters.CreateAccount(name, null, AccountLevel.Player)!.Id;
            Array.Clear(c.Equipment); // the weapon alone sets the tier
            c.Equipment[0] = new InventoryItem(weapon);
            if (mount is not null) c.Equipment[7] = new InventoryItem(mount);
            Check(Characters.Create(c), $"character {name} created");
            var peer = new FakePeer();
            var session = new WorldSession(peer, World);
            peer.Disconnected = session.OnDisconnected;
            lock (World.Sync) session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
            return (session, peer, session.Player!);
        }

        public void Run(double seconds)
        {
            for (var i = 0; i < Math.Round(seconds * 20); i++)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(50));
                lock (World.Sync) World.Update();
            }
        }

        public void Dispose()
        {
            World.Dispose();
            Characters.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    /// <summary>The season points (a7m) the peer's 376 events since <paramref name="mark"/> gave.</summary>
    private static long Gained(FakePeer peer, int mark) => Since(peer, mark).Where(e => e.Code == EventCodes.PersonalSeasonPointsGained).Sum(e => L(e.P, 0));

    private static void Data()
    {
        var goals = Shipped;
        Check(goals.Pvp is { Enabled: true, KnockdownIsKill: true, KillPoints: 20, KillPointsPerTier: 5, DuelWinPoints: 15, DailyPointsCap: 600, MinVictimTier: 4 }
              && goals.Pvp.ZoneFactor("RED") == 1 && goals.Pvp.ZoneFactor("black") == 1.5 && goals.Pvp.ZoneFactor("yellow") == 0
              && goals.Bounties is { PvpDaily: 1, PvpWeekly: 1 } && goals.Bounties.Pool.Count(b => b.IsPvp) == 3
              && GoalSettings.Parse("{}") is { Pvp: { Enabled: true, KillPoints: 0, KnockdownIsKill: false }, Bounties.PvpDaily: 0 },
              "goals.json Pvp: 20 + 5 per tier, duel 15, cap 600, red 1, black 1.5, yellow 0; 3 PvP bounties, one extra daily and weekly slot; built-in: no points, no PvP slot (official)");
        static bool Refused(string text)
        {
            try { GoalSettings.Parse(text); return false; }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { return true; }
        }
        Check(Refused("""{ "Pvp": { "ZoneFactors": { "purple": 1 } } }""") && Refused("""{ "Bounties": { "Pool": [ { "Id": "A", "Kind": "pvpkill", "Bots": "maybe" } ] } }""")
              && Refused("""{ "Bounties": { "PvpDaily": -1 } }""") && !Refused("""{ "Bounties": { "Daily": 0 } }"""),
              "a zone key that is no pvpwarningtype, a Bots value or a negative PvP slot count refused; weekly bounties only (Daily 0) still load");
        var journal = Client.Clusters!.Journal;
        var duel = journal.Find("JOURNAL_GUILD_PLAYER_DUEL_WIN_01")!;
        var zerg = journal.Find("JOURNAL_GUILD_ZERGKILL_01")!;
        Check(journal.Find("JOURNAL_PVP_OPEN_WORLD_PVP_KILL_PLAYER_02")!.Root.Children is [{ Kind: "killplayer", Goal: 10, Supported: true }]
              && duel.Root.Children is [{ Kind: "finishduel", Supported: true }] && zerg.Root.Children is [{ Supported: true, Filters: [{ Kind: "IfCombatHistoryAttackers", Value: "10" } f] }]
              && journal.Find("JOURNAL_PVP_OPEN_WORLD_PVP_KILLFAME_01")!.Root.Children is [{ Kind: "killplayerfame", Goal: 2_000_000_000, Supported: true }]
              && !AlbionJournal.Holds(null!, f, new JournalEvent("killplayer", 1) { Attackers = 3 }) && AlbionJournal.Holds(null!, f, new JournalEvent("killplayer", 1) { Attackers = 10 }),
              "the journal: KILL_PLAYER_02 killplayer 10, DUEL_WIN_01 finishduel, KILLFAME_01 200000 fame, ZERGKILL_01 IfCombatHistoryAttackers >= 10 (3 no, 10 yes)");
    }

    private static void Knockdown()
    {
        using var field = new Field();
        var world = field.World;
        var (_, ap, a) = field.Join("Ash", 0);
        var (_, bp, b) = field.Join("Bea", 2);
        const string Kills = "JOURNAL_PVP_OPEN_WORLD_PVP_KILL_PLAYER_02";
        a.Character.JournalTracked = [Kills];
        var (am, bm) = (ap.Events.Count, bp.Events.Count);
        lock (world.Sync) Pvp.Damage(a, b, 1e7, EffectType.Physical, HitOrigin.Direct, -1, world.GameNow);
        var got = Since(ap, am);
        var noFame = got.FindIndex(e => e.Code == EventCodes.KillRewardedNoFame);
        var season = got.FindIndex(e => e.Code == EventCodes.PersonalSeasonPointsGained);
        Check(b.Combat.Down is not null && noFame >= 0 && season > noFame && L(got[season].P, 0) == 500_000 && Gained(bp, bm) == 0
              && got.Any(e => e.Code == EventCodes.JournalAchievementProgressUpdate && e.P[1] is string[] and [Kills] && ((long[])e.P[2]!)[0] == 1)
              && b.PvpState.HitBy.Count == 1 && b.PvpState.DownCredits?.ContainsKey(a) == true,
              $"SPEC A1/J1: Bea knocked down; Ash gets 615, then 376 {{0 500000}} (20 + 5 x T6), KILL_PLAYER_02 600 {{1}}; Bea nothing; her attacker kept while down ({Show(got)})");

        // A knockdown and its execute are one result: without KnockdownIsKill the execute adds killplayer only (no refusal, no points again).
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { KnockdownIsKill = false } };
        var (_, ep, e) = field.Join("Eve", 8);
        var (_, _, f) = field.Join("Fin", 10);
        var em = ep.Events.Count;
        lock (world.Sync) Pvp.Damage(e, f, 1e7, EffectType.Physical, HitOrigin.Direct, -1, world.GameNow);
        var afterDown = e.Character.Goals!.Journal.GetValueOrDefault(Kills + "_1");
        lock (world.Sync)
        {
            Check(Death.Attackers(f) is [{ } only] && only.Player == e, "the execute still sees the knockdown's attacker");
            Death.Kill(f, e, e.Id, e.Character.Name, Death.Cause.NoLoss, false, world.GameNow);
        }
        Check(afterDown == 0 && e.Character.Goals.Journal.GetValueOrDefault(Kills + "_1") == 1 && Gained(ep, em) == 500_000 && e.Character.Goals.PvpRefused.Count == 0
              && f.PvpState is { HitBy.Count: 0, DownCredits: null },
              $"knockdown then execute: 50 points once, killplayer 0 then 1, nothing refused, the victim's attackers and credits cleared ({e.Character.Goals.Journal.GetValueOrDefault(Kills + "_1")})");
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { KnockdownIsKill = true } };

        // J6: with KnockdownFameFactor 0.5 the knockdown gives half the kill fame as UpdateFame, without KilledPlayer and 615.
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { KnockdownFameFactor = 0.5 } };
        var (_, cp, c) = field.Join("Cal", 4);
        var (_, _, d) = field.Join("Dee", 6);
        var fame = Rates.ScaleFame((long)Math.Round(Death.KillFame(d) * 0.5), world.Rates.Fame * world.Rates.CombatFame);
        var cm = cp.Events.Count;
        lock (world.Sync) Pvp.Damage(c, d, 1e7, EffectType.Physical, HitOrigin.Direct, -1, world.GameNow);
        var mine = Since(cp, cm);
        Check(fame > 0 && mine.Any(e => e.Code == EventCodes.UpdateFame && L(e.P, 2) == fame && e.P.GetValueOrDefault((byte)5) is null or false)
              && !mine.Any(e => e.Code is EventCodes.KilledPlayer or EventCodes.KillRewardedNoFame)
              && c.Character.Goals!.Journal.GetValueOrDefault("JOURNAL_PVP_OPEN_WORLD_PVP_KILLFAME_01_1") == fame,
              $"SPEC J6: UpdateFame {{2 {fame}, 5 false}}, no 163 and no 615, KILLFAME_01 at the fame ({Show(mine)})");
    }

    private static void Hook()
    {
        using var field = new Field();
        var world = field.World;
        var (_, ap, a) = field.Join("Ari");
        var (_, _, b) = field.Join("Bo");
        var (_, _, alt) = field.Join("Alt", account: a.Character.Account);
        var (_, _, low) = field.Join("Low", weapon: "T3_MAIN_SWORD", mount: "T5_MOUNT_OX"); // the mount does not make a T5 victim
        var (_, ep, e) = field.Join("Eli", account: Guid.Empty);
        var (_, _, f) = field.Join("Fay", account: Guid.Empty);
        var t0 = world.Bounties.DayStart(world.Bounties.UtcNow).AddHours(1).Ticks;
        PvpKill K(Player killer, PvpActor victim, long at, PvpOutcome outcome = PvpOutcome.Knockdown, (Player, double)[]? hits = null, bool crime = false, double seconds = 0) =>
            new(PvpActor.Of(killer), victim, [.. (hits ?? [(killer, 100)]).Select(h => (PvpActor.Of(h.Item1), h.Item2))], killer.Map!, outcome, crime, 0, at, Math.Max(1, hits?.Length ?? 1))
            { DuelSeconds = seconds };
        long Points(Player p, FakePeer peer, PvpKill k)
        {
            var mark = peer.Events.Count;
            lock (world.Sync) PvpGoals.OnPvpKill(world, k);
            return Gained(peer, mark);
        }
        long Min(double m) => (long)(m * TimeSpan.TicksPerMinute);

        // A2: the same victim again within 30 min nothing, after it again, a fourth in the day nothing.
        var times = new[] { 0, 1, 31, 62, 93 }.Select(m => Points(a, ap, K(a, PvpActor.Of(b), t0 + Min(m)))).ToList();
        Check(times.SequenceEqual([500_000L, 0, 500_000, 500_000, 0]) && a.Character.Goals!.PvpRefused is { } why && why["same victim cooldown"] == 1 && why["same victim today"] == 1,
              $"SPEC A2: 50, cooldown 0, 50, 50, a fourth today 0 (got {string.Join(", ", times)})");
        // A3/A4/A8: an alt, a T3 victim, a crime: nothing; two characters of the local account (legacy logins) do credit each other.
        Check(Points(a, ap, K(a, PvpActor.Of(alt), t0)) == 0 && Points(a, ap, K(a, PvpActor.Of(low), t0)) == 0
              && Points(a, ap, K(a, PvpActor.Of(f), t0, crime: true)) == 0 && Points(e, ep, K(e, PvpActor.Of(f), t0)) == 500_000
              && a.Character.Goals!.PvpRefused.Keys.ToHashSet().SetEquals(["same victim cooldown", "same victim today", "same account", "victim tier", "crime"])
              && PvpGoals.Describe(a).Contains("same account 1", StringComparison.Ordinal),
              $"SPEC A3/A4/A8: same account, T3, crime refused; the local account's two characters credited; .bounty names the refusals ({PvpGoals.Describe(a)})");
        // The local account exempt only while unclaimed: once a login owns it, its characters are one person's.
        var local = SqliteCharacterRepository.LocalAccountId;
        var (_, lp, l1) = field.Join("Lo", account: local);
        var (_, _, l2) = field.Join("Lu", account: local);
        var (_, _, l3) = field.Join("Ly", account: local);
        var unclaimed = Points(l1, lp, K(l1, PvpActor.Of(l2), t0));
        Check(unclaimed == 500_000 && field.Characters.ClaimLocalAccount("owner") is not null && Points(l1, lp, K(l1, PvpActor.Of(l3), t0)) == 0
              && l1.Character.Goals!.PvpRefused.ContainsKey("same account"), $"the unclaimed local account's characters credited ({unclaimed}), once claimed refused");

        // A5: an assist with 30 % of the damage gets half the points, one with 5 % nothing.
        var (_, cp, c) = field.Join("Cy");
        var (_, dp, d) = field.Join("Di");
        var (_, _, g) = field.Join("Gus");
        var (cm, dm) = (cp.Events.Count, dp.Events.Count);
        lock (world.Sync) PvpGoals.OnPvpKill(world, K(a, PvpActor.Of(g), t0, hits: [(a, 65), (c, 30), (d, 5)]));
        Check(Gained(cp, cm) == 250_000 && Gained(dp, dm) == 0, "SPEC A5: Cy (30 %) 25 points, Di (5 %) nothing");

        // A6: the daily cap (60 here): 50, 10, nothing, then 50 the next bounty day. A bot victim: half.
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { DailyPointsCap = 60 } };
        var (_, hp, h) = field.Join("Hal");
        var capped = new[] { b, c, d }.Select(v => Points(h, hp, K(h, PvpActor.Of(v), t0))).ToList();
        Check(capped.SequenceEqual([500_000L, 100_000, 0]) && Points(h, hp, K(h, PvpActor.Of(new Bot("p1", 6)), t0 + TimeSpan.TicksPerDay)) == 250_000,
              $"SPEC A6: cap 60: 50, 10, 0; the next day a T6 bot 25 (got {string.Join(", ", capped)})");
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { DailyPointsCap = 600 } };

        // A7 (rules): a duel of 10 s nothing; of 25 s 15 points twice, a third win of the pair that day nothing.
        var duels = new[] { 10.0, 25, 25, 25 }.Select(s => Points(c, cp, K(c, PvpActor.Of(d), t0, PvpOutcome.DuelWin, seconds: s))).ToList();
        Check(duels.SequenceEqual([0L, 150_000, 150_000, 0]), $"SPEC A7: 10 s 0, then 15, 15, a third 0 (got {string.Join(", ", duels)})");

        // B1-B3: three PvP bounties; kills count for the kill bounty only, the bot one only for a bot, the duel one for a duel.
        world.Goals = world.Goals with
        {
            Bounties = new BountyGoals
            {
                Daily = 0, PvpDaily = 3, Weekly = 0,
                Pool =
                [
                    new Bounty { Id = "K3", Kind = "pvpkill", Text = "Defeat {count}", Count = 3, MinTier = 4, Items = ["T5_SKILLBOOK_NONTRADABLE"] },
                    new Bounty { Id = "BOT", Kind = "pvpkill", Bots = "only", Count = 1 },
                    new Bounty { Id = "DUEL", Kind = "duel", Count = 1 },
                ],
            },
        };
        var (_, kp, k) = field.Join("Kit");
        var mark = kp.Events.Count;
        foreach (var v in new[] { b, c, d }) lock (world.Sync) PvpGoals.OnPvpKill(world, K(k, PvpActor.Of(v), t0));
        var done = k.Character.Goals!.BountiesDone;
        Check(done.Count == 1 && done.Single().EndsWith("/K3", StringComparison.Ordinal) && k.Inventory.Bag.Items.Any(i => i.Template.Name == "T5_SKILLBOOK_NONTRADABLE")
              && Since(kp, mark).Any(x => x.Code == EventCodes.SystemMessage && ((string)x.P[0]!).StartsWith("Bounty complete: Defeat 3", StringComparison.Ordinal)),
              "SPEC B1: three credited kills complete K3 (the tome, the chat line); the bot and duel bounties stay open");
        lock (world.Sync)
        {
            PvpGoals.OnPvpKill(world, K(k, PvpActor.Of(new Bot("p2", 5)), t0));
            PvpGoals.OnPvpKill(world, K(k, PvpActor.Of(b), t0 + Min(1), PvpOutcome.DuelWin, seconds: 30));
        }
        Check(done.Count == 3 && done.Any(x => x.EndsWith("/BOT", StringComparison.Ordinal)) && done.Any(x => x.EndsWith("/DUEL", StringComparison.Ordinal)),
              "SPEC B2/B3: a bot victim completes BOT, a duel win DUEL");
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { Enabled = false } };
        Check(world.Bounties.Current.Count == 0, "without Pvp:Enabled no PvP bounty is drawn");
        world.Goals = world.Goals with { Pvp = world.Goals.Pvp with { Enabled = true } };

        // J5: killingspree 60 s without assists: kills at 0, 20, 50 s give 3; a fourth at 120 s keeps 3. J2: the Steam mirror stays 0.
        var (_, _, s) = field.Join("Sam");
        const string Spree = "JOURNAL_PVP_OPEN_WORLD_PVP_KILLINGSPREE_NO_ASSIST_1";
        var victims = new[] { b, c, d, g };
        foreach (var (v, sec) in victims.Zip(new[] { 0, 20, 50, 120 })) lock (world.Sync) PvpGoals.OnPvpKill(world, K(s, PvpActor.Of(v), t0 + sec * TimeSpan.TicksPerSecond));
        var sg = s.Character.Goals!;
        Check(sg.Journal.GetValueOrDefault(Spree) == 3 && !sg.Journal.Keys.Any(x => x.StartsWith("SA_PVP_KILL_PLAYER_01_", StringComparison.Ordinal)) && !sg.Done.ContainsKey("SA_PVP_KILL_PLAYER_01"),
              $"SPEC J5/J2: the spree 3 in 60 s, kept at 3 after a lone fourth; SA_PVP_KILL_PLAYER_01 untouched (got {sg.Journal.GetValueOrDefault(Spree)})");
    }

    private static void Duel()
    {
        using var field = new Field();
        var world = field.World;
        var (asess, ap, a) = field.Join("Ida", 0);
        var (bsess, bp, b) = field.Join("Jon", 5);
        foreach (var p in new[] { a, b }) p.Character.Flag = Pvp.Blue; // not Hostile but for the duel
        lock (world.Sync)
        {
            asess.OnRequest(Request(new DuellingChallengePlayerRequest { bqx = b.Id, bqy = 0 }, 7), encrypted: false);
            bsess.OnRequest(Request(new DuellingAcceptChallengeRequest(), 7), encrypted: false);
        }
        Check(world.Duels.Of(a) is not null, "the duel is on");
        field.Run(10 + 21);
        var am = ap.Events.Count;
        lock (world.Sync) Pvp.Damage(a, b, 1e7, EffectType.Physical, HitOrigin.Direct, -1, world.GameNow);
        var got = Since(ap, am);
        var completed = got.Where(e => e.Code == EventCodes.AlbionJournalAchievementCompleted).Select(e => (string)e.P[0]!).ToList();
        Check(world.Duels.Of(a) is null && Gained(ap, am) == 150_000 && completed.Contains("JOURNAL_GUILD_PLAYER_DUEL_WIN_01") && completed.Contains("JOURNAL_GUILD_PLAYER_DUEL"),
              $"SPEC A7/J3: Ida wins after 21 s: 376 {{150000}}, DUEL_WIN_01 and DUEL completed ({Show(got)})");
    }
}
