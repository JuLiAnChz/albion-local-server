using AlbionLocal.Photon.Serialization;
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
/// The goals of PVE-DESIGN.md step 11 (analysis/work/stageH/journals/SPEC.md §B, §C, its self-test list §F 4-12): the season data,
/// the points formula and the eligibility, the a35 tracker rules, the journal's mission tree, content/goals.json; then on a Thetford
/// session a tracked gather achievement to its completion and claim (600/362/601, op 144, op 514), the daily challenge's unlock and
/// claims (376/375, op 372, op 375), a bounty, and all of it saved.
/// </summary>
public static class GoalTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("goals: personalseasons.xml (12 seasons, ADCSEASON_10@2026), points 3 x sqrt(4.5) = 63640 and the captured premium samples, eligibility, journal mission trees, content/goals.json", Data),
        ("goals: a35 trackers: an unfinished daily carries over midnight and unlocks its three rewards, a finished one restarts, the secondary is capped at its day", Trackers),
        ("goals: tracked T2 gathering 5/10/15/19 (600 kind 1), done at 80 (362, 600, 601), op 144 lists, claim 514 [0] then [20561]; daily challenge 376/375, op 372, claim 375 0 then 15001; a bounty; saved", Live),
        ("goals: a full bag at the unlock keeps the granted MAIN_01 and the daily extra owed; .bounty claim gives them once there is room (CLAIMED)", Owed),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static ClusterStore Store => Client.Clusters!;

    private static readonly DateTime October = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static void Data()
    {
        var data = Store.Seasons;
        var oct = data.Of(October.AddDays(16))!;
        Check(data.Seasons.Count == 12 && oct.Name == "ADCSEASON_10" && data.IdOf(October) == "ADCSEASON_10@2026" && data.Parse("ADCSEASON_10@2026") is ({ Month: 10 }, var start) && start == October
              && oct.Trackers.Select(t => t.Type).SequenceEqual(["daily", "main", "secondary"]) && oct.Trackers[0].Cap(5) == 12_000_000 && oct.Trackers[1].Cap(0) == 6_000_000_000
              && oct.Trackers[2].Cap(3) == 1_500_000_000 && oct.Trackers[2].Cap(7) == 3_000_000_000 && oct.Trackers[0].ResetDays == 1,
              "personalseasons.xml: 12 seasons, October = ADCSEASON_10 from 2026-10-01, daily cap 1200, main 600000, secondary 150000 to day 6 and 300000 from day 7");
        Check(Seasons.Points(3, 0.5, 45_000) == 63640 && Seasons.Points(3, 0.5, 67_500) == 77942 && Seasons.Points(1.5, 0.5, 3_031_500) == 261168
              && Seasons.Points(1.5, 0.5, 18_640_500) == 647620 && Seasons.Points(1.5, 0.5, 0) == 0,
              "SPEC §F.4: 3 x sqrt 4.5 = 63640; with the captured premium 1.5: 4.5 gathering 77942, 202.1 and 1242.7 PvE 261168 and 647620");
        PersonalSeasonData.Reward Flags(bool bought, bool premium, bool non) => new("R", "itemreward", null, 1, bought, premium, non);
        Check(!Flags(true, false, false).Eligible(false, 0) && Flags(true, false, false).Eligible(true, 7) && !Flags(true, false, false).Eligible(true, 6)
              && !oct.Find("REWARD_DAILY_03")!.Value.Reward.Eligible(true, 31) && oct.Find("REWARD_DAILY_01")!.Value.Reward.Eligible(false, 0)
              && !Seasons.Eligible(oct.Find("REWARD_MAIN_01")!.Value.Reward),
              "SPEC §F.11: boughtpremium needs premium for more than 6 days; DAILY_03 is for non-premium only, DAILY_01 for all; no MAIN_01 without premium");

        var journal = Store.Journal;
        var t2 = journal.Find("JOURNAL_GATHERING_RESOURCES_ANY_TYPE_T2")!;
        var hardcore = journal.Find("JOURNAL_PVE_EXPEDITION_FINISH_5_UNIQUE_HARDCORE")!;
        var t3 = journal.Find("JOURNAL_PVE_ROAMINGMOBS_KILL_T3_MOBS_01")!;
        Check(t2 is { Max: 80, RewardItem: "T3_SILVERBAG_NONTRADABLE", Root.Children: [{ Kind: "gather", Index: 1, Goal: 80, Supported: true }] }
              && hardcore.Root.Children is [{ Kind: "any", Index: 1, Goal: 5, Children: [{ Index: 2 }, ..] } any] && any.Children.Count == 10
              && hardcore.SubKey(any.Children[0]) == "PVE_EXPEDITIONJOURNAL_PVE_EXPEDITION_FINISH_5_UNIQUE_HARDCORE_2"
              && t3.Root.Children is [{ Kind: "killmob", Goal: 12, Supported: true, Filters: [{ Kind: "or", Children.Count: 4 }] }]
              && journal.Achievements.Count == journal.Entries.Count && journal.CategoryRewards.Any(r => r is { Id: "PVE_1", Category: "PVE", Share: 0.2 })
              && journal.LeavesOf("killmob").Count > 300 && journal.LeavesOf("killmob").Any(m => m.Mobs.Contains("T8_MOB_RD_DRAGON_FIRE_ELITE_BOSS")),
              "the journal: T2 gathering one gather leaf of 80; the hardcore any count=5 is _1, its first child _2 (SPEC §B.3); T3 roaming kills 12 with an or filter; PVE_1 at 0.2; killmob mobid=\"…\" supported");

        var goals = GoalSettings.Load(Path.Combine(DataDir(), "..", "content", "goals.json"));
        var items = goals.Bounties.Pool.SelectMany(b => b.Items).Concat(goals.Season.DailyExtra).Select(i => i.Split(':')[0]).ToList();
        Check(goals is { Season: { Pve: 3, BossBonus: 25, GrantPremiumRewards: true }, Bounties: { Daily: 3, Weekly: 1 } } && goals.Bounties.Pool.Count == 16
              && items.All(i => Client.Items.Find(i) is { CanCreate: true }) && Client.Items.Find("T3_SILVERBAG_NONTRADABLE") is { CanCreate: true },
              "content/goals.json: pve 3, boss bonus 25, premium rewards granted, 3 daily + 1 weekly bounties of 16, every reward item creatable");
        static bool Refused(string text)
        {
            try { GoalSettings.Parse(text); return false; }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { return true; }
        }
        Check(Refused("""{ "Season": { "Pvee": 3 } }""") && Refused("""{ "Bounties": { "Pool": [ { "Id": "A", "Kind": "dance" } ] } }""")
              && Refused("""{ "Bounties": { "ResetUtc": "25:00" } }""")
              && GoalSettings.Parse("{}") is { Season: { Enabled: true, Pve: null, BossBonus: 0, GrantPremiumRewards: false }, Journal.Enabled: true, Bounties.Pool.Count: 0 },
              "goals.json: an unknown key, a bad bounty kind or reset time refused; {} = the built-in settings");
    }

    private static void Trackers()
    {
        var season = Store.Seasons.Of(October)!;
        var (daily, main, secondary) = (season.Trackers[0], season.Trackers[1], season.Trackers[2]);
        List<string> Gain(SeasonLog log, long raw, DateTime at)
        {
            var unlocked = new List<PersonalSeasonData.Reward>();
            foreach (var t in season.Trackers) Seasons.Add(log, season, October, t, raw, at, unlocked);
            return [.. unlocked.Select(r => r.Id)];
        }
        var night = new DateTime(2026, 10, 3, 23, 59, 0, DateTimeKind.Utc);
        var log = new SeasonLog();
        Check(Gain(log, 10_000_000, night).Count == 0, "1000 points: nothing unlocked");
        var got = Gain(log, 3_000_000, night.AddMinutes(2));
        Check(log.Points[daily.Name] == 12_000_000 && log.Points[main.Name] == 13_000_000 && got.SequenceEqual(["REWARD_DAILY_01", "REWARD_DAILY_02", "REWARD_DAILY_03"]),
              $"SPEC §F.5: daily 1000 + 300 after midnight is no reset, capped at 1200 (main 1300), the three daily rewards unlocked (got {string.Join(",", got)})");
        var finished = new SeasonLog();
        Gain(finished, 12_000_000, night);
        Check(Gain(finished, 3_000_000, night.AddMinutes(2)).Count == 0 && finished.Points[daily.Name] == 3_000_000 && finished.Points[main.Name] == 15_000_000,
              "SPEC §F.5b: a finished daily starts again at its period: 300, the main tracker 1500");
        var capped = new SeasonLog();
        var day3 = October.AddDays(3);
        Gain(capped, 1_499_900_000, day3);
        Check(Gain(capped, 500_000, day3).Contains("REWARD_SECONDARY_01") && capped.Points[secondary.Name] == 1_500_000_000
              && Gain(capped, 1_000_000, day3.AddHours(1)).Count == 0 && capped.Points[secondary.Name] == 1_500_000_000,
              "SPEC §F.5c: the secondary at 149990 + 50 crosses 150000 on day 3, then stays at its cap until day 7");
    }

    private static void Live()
    {
        _ = Store.Journal;
        _ = Store.Seasons;
        using var town = new EconomyTest.Town();
        var (session, peer, me) = town.Join("Seeker", 0);
        const string T2 = "JOURNAL_GATHERING_RESOURCES_ANY_TYPE_T2";
        me.Character.JournalTracked = [T2];
        var fiber = Client.Items.Find("T2_FIBER")!;
        var mark = peer.Events.Count;
        lock (town.World.Sync)
            foreach (var n in new[] { 5, 5, 5, 4 }) AlbionJournal.Gathered(me, fiber, n);
        var updates = Since(peer, mark).Where(e => e.Code == EventCodes.JournalAchievementProgressUpdate).ToList();
        Check(updates.Count == 4 && updates.All(e => L(e.P, 0) == 1 && e.P[1] is string[] and [T2]) && updates.Select(e => ((long[])e.P[2]!)[0]).SequenceEqual([5L, 10, 15, 19]),
              $"SPEC §F.8: the tracked T2 gathering 5, 10, 15, 19 as 600 kind 1 (got {Show(updates)})");
        mark = peer.Events.Count;
        lock (town.World.Sync) AlbionJournal.Gathered(me, fiber, 70);
        var done = Since(peer, mark);
        var tracker = done.LastOrDefault(e => e.Code == EventCodes.JournalAchievementProgressUpdate);
        Check(done.Any(e => e.Code == EventCodes.AlbionJournalAchievementCompleted && (string)e.P[0]! == T2 && (string)e.P[2]! == "")
              && tracker.P is { } tp && !tp.ContainsKey(0) && ((long[])tp[2]!)[0] == 80
              && done.LastOrDefault(e => e.Code == EventCodes.JournalClaimableRewardUpdate).P is { } badge && L(badge, 0) >= 1,
              $"at 80: 362 {{0 name, 2 \"\"}}, 600 kind 0 with 80, 601 with the claimable count (got {Show(done)})");

        OperationResponse Ask(AlbionRequest req)
        {
            var before = peer.Responses.Count;
            lock (town.World.Sync) session.OnRequest(Request(req, 9), encrypted: false);
            return peer.Responses.Skip(before).Single();
        }
        var stats = Ask(new GetCharacterSteamAchievementsRequest { mc = "Seeker" });
        Check(stats.Parameters[1] is string[] doneList && doneList.Contains(T2) && stats.Parameters[2] is string[] going && stats.Parameters[3] is long[] progress
              && going.Length == progress.Length && !going.Intersect(doneList).Any() && progress.All(p => p > 0) && stats.Parameters[5] is string[] { Length: 0 },
              "SPEC §F.10: op 144 lists T2 done, the others in progress parallel to their progress (none done), nothing claimed");
        var claim = Ask(new ClaimAlbionJournalRewardRequest { fi = [T2] });
        var again = Ask(new ClaimAlbionJournalRewardRequest { fi = [T2, "NO_SUCH_THING"] });
        Check(claim.Parameters[0] is short[] and [0] && me.Inventory.Bag.Items.Any(i => i.Template.Name == "T3_SILVERBAG_NONTRADABLE")
              && again.Parameters[0] is short[] and [AlbionJournal.AlreadyClaimed, AlbionJournal.InvalidReward],
              "SPEC §F.9: the claim gives the T3 silver bag ([0]); again [20561], an unknown id 20563");

        // The Adventurer's Challenge: 1200 points finish the daily (no premium: all three daily rewards claimable).
        var id = Store.Seasons.IdOf(Seasons.UtcNow(town.World))!;
        mark = peer.Events.Count;
        lock (town.World.Sync) Seasons.Gain(me, 12_000_000);
        var season = Since(peer, mark);
        Check(season.FirstOrDefault(e => e.Code == EventCodes.PersonalSeasonPointsGained).P is { } gained && L(gained, 0) == 12_000_000
              && season.FirstOrDefault(e => e.Code == EventCodes.NewUnlockedPersonalSeasonRewards).P is { } unlocked && (string)unlocked[0]! == id
              && unlocked[1] is string[] and ["REWARD_DAILY_01", "REWARD_DAILY_02", "REWARD_DAILY_03"],
              $"1200 points: 376 {{0 12000000}}, then 375 {{{id}, the three daily rewards}} (got {Show(season)})");
        var tracker372 = Ask(new GetPersonalSeasonTrackerDataRequest { oe = id }).Parameters;
        Check((string)tracker372[0]! == id && tracker372[2] is string[] { Length: 3 } && tracker372[3] is long[] and [12_000_000, 12_000_000, _]
              && tracker372[8] is string[] { Length: 3 } r8 && tracker372[9] is string[] r9 && r9.Length == 3 && r9.All(s => s == "UNLOCKED_CLAIMABLE") && r8.Contains("REWARD_DAILY_02"),
              "SPEC §F.7: op 372: the id, three trackers with their points, the three daily rewards UNLOCKED_CLAIMABLE (8/9 parallel)");
        var first = Ask(new ClaimPersonalSeasonRewardRequest { fr = id, fs = "REWARD_DAILY_02" });
        var second = Ask(new ClaimPersonalSeasonRewardRequest { fr = id, fs = "REWARD_DAILY_02" });
        var log = me.Character.Goals!.Seasons[id];
        Check(first.ReturnCode == 0 && second.ReturnCode == Seasons.RcAlreadyClaimed && log.Points["PSPTRACKER_DAILY_01"] == 12_000_000
              && log.Points["PSPTRACKER_MAIN_01"] == 12_000_000 + 500_000_000 && log.Rewards["REWARD_DAILY_02"] == Seasons.Claimed
              && Ask(new ClaimPersonalSeasonRewardRequest { fr = id, fs = "REWARD_MAIN_01" }).ReturnCode == Seasons.RcNotEnoughPoints,
              "SPEC §F.6: claiming DAILY_02 adds 50000 to main and secondary, not the daily; again 15001; MAIN_01 still locked 15002");

        // A bounty of the day: two T2 harvests for 100 silver and a tome.
        town.World.Goals = GoalSettings.Default with
        {
            Bounties = new BountyGoals { Daily = 1, Weekly = 0, Pool = [new Bounty { Id = "FIBER", Kind = "gather", Text = "Gather {count}", Count = 2, Silver = 100, Items = ["T4_SKILLBOOK_NONTRADABLE"] }] },
        };
        mark = peer.Events.Count;
        lock (town.World.Sync)
        {
            town.World.Bounties.Gathered(me, fiber, 1);
            town.World.Bounties.Gathered(me, fiber, 1);
        }
        var lines = Since(peer, mark).Where(e => e.Code == EventCodes.SystemMessage).Select(e => (string)e.P[0]!).ToList();
        Check(lines is ["Bounty: Gather 2 1/2", var end] && end.StartsWith("Bounty complete: Gather 2", StringComparison.Ordinal) && me.Character.Silver == 100 * 10000
              && me.Inventory.Bag.Items.Any(i => i.Template.Name == "T4_SKILLBOOK_NONTRADABLE") && town.World.Bounties.Describe(me).First() == "Gather 2: 2/2 (done)",
              $"a bounty: a line at half, the completion with 100 silver and the tome, .bounty shows it done (got {string.Join(" | ", lines)})");

        lock (town.World.Sync) peer.Disconnect();
        town.World.Saves.Flush();
        var saved = town.Characters.Find(me.Character.Id)!.Goals;
        Check(saved is { } g && g.Done.ContainsKey(T2) && g.Claimed.Contains(T2) && g.Seasons[id].Rewards["REWARD_DAILY_02"] == Seasons.Claimed
              && g.BountiesDone.Count == 1 && g.Seasons[id].Points["PSPTRACKER_MAIN_01"] == 512_000_000,
              "saved: the journal's done and claimed, the season's points and states, the bounty done");
    }

    private static void Owed()
    {
        _ = Store.Seasons;
        using var town = new EconomyTest.Town();
        var world = town.World;
        world.Goals = GoalSettings.Default with { Season = new SeasonGoals { GrantPremiumRewards = true, DailyExtra = ["T4_SILVERBAG_NONTRADABLE"] } };
        var (_, _, me) = town.Join("Packed", 0);
        var id = Store.Seasons.IdOf(Seasons.UtcNow(world))!;
        var mount = Store.Seasons.Of(Seasons.UtcNow(world))!.Find("REWARD_MAIN_01")!.Value.Reward.Item!;
        var sword = Client.Items.Find("T4_MAIN_SWORD")!;
        var bag = me.Inventory.Bag;
        List<int> fill;
        lock (world.Sync)
        {
            fill = [.. Enumerable.Range(0, bag.Size).Where(n => bag[n] is null)];
            foreach (var n in fill) bag.Put(n, new Item(world.ObjectIds.Next(), sword, 1, 1, 0, [], []));
            Seasons.Gain(me, 6_000_000_000);
        }
        var log = me.Character.Goals!.Seasons[id];
        Check(log.Owed.Contains("REWARD_MAIN_01") && log.Rewards["REWARD_MAIN_01"] == Seasons.Ineligible && me.Character.Goals.ItemsOwed is ["T4_SILVERBAG_NONTRADABLE"]
              && Client.Items.Find(mount) is { CanCreate: true }, $"a full bag: {mount} (MAIN_01) and the daily silver bag wait");
        int owed, given;
        lock (world.Sync)
        {
            foreach (var n in fill) bag.Take(n);
            owed = Seasons.OwedCount(me);
            given = Seasons.ClaimOwed(me);
        }
        Check(given == owed && Seasons.OwedCount(me) == 0 && log.Rewards["REWARD_MAIN_01"] == Seasons.Claimed
              && bag.Items.Any(i => i.Template.Name == mount) && bag.Items.Any(i => i.Template.Name == "T4_SILVERBAG_NONTRADABLE"),
              $"with room: all {owed} owed given, MAIN_01 CLAIMED, the mount and the silver bag in the bag (given {given})");
    }
}
