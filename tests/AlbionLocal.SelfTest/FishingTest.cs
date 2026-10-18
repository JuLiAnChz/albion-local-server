using System.Numerics;
using System.Text.Json;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Fishing (analysis/work/stageG/fishing/SPEC.md §12): the zones of the template against every zone of the capture 002835
/// (Fixtures/fishing_zones_002835.json: positions, capacity, prefab only), a full successful cast with an observer on a
/// <see cref="ManualClock"/> (and one entering mid-minigame), early reel-in, refusals ending the session, the Casting fallback, a
/// ground cast, a missed bite, a too-early success, the rod gone at the strike, the zone pick and the charge regrowth. Runs after
/// <see cref="WorldObjectTest"/>, whose Joins resolve Thetford in the shared store.
/// </summary>
public static class FishingTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("fishing: the template zones are the 52 captured ones (position, capacity 15/5/1, prefab), content/fishing.json = built-in, rod 3015, loot lists", Zones),
        ("fishing: a full cast in Thetford (states 3/4/5, float 1-4, zone charge, FishingMiniGame to the fisher only, Pull/GiveLine, success burst, Cancel), a miss, a too-early success, zone pick and regrowth", Cast),
    ];

    private static ClusterStore Store => Client.Clusters!;

    private static void Zones()
    {
        // The shipped file gives the built-in values (the server without the file behaves the same).
        var shipped = FishingSettings.Load(Path.Combine(DataDir(), "..", "content", "fishing.json"));
        var builtIn = FishingSettings.Default;
        Check(shipped.Zones.Count == builtIn.Zones.Count && shipped.Zones.All(z => builtIn.Zones[z.Key] == z.Value) && shipped.MiniGames.Count == 9
              && shipped.MiniGames.Values.All(v => v.SequenceEqual(FishingSettings.CapturedMiniGame)) && shipped.CatchTimeout == builtIn.CatchTimeout
              && shipped.Freshwater == builtIn.Freshwater && shipped with { Zones = builtIn.Zones, MiniGames = builtIn.MiniGames } == builtIn,
              "content/fishing.json parses to the built-in settings (10 zone kinds, 9 minigame settings = the captured one)");
        var rod = Client.Items.Find("T3_2H_TOOL_FISHINGROD")!;
        var fish = Client.Items.Find("T4_FISH_FRESHWATER_ALL_COMMON")!;
        Check(rod.Index == 3015 && Server.Systems.Fishing.IsRod(rod) && Server.Systems.Fishing.Speed(rod) == 0.05 && Server.Systems.Fishing.Fame(fish) == 40
              && Server.Systems.Fishing.SettingName([(fish, 1)]) == "T3" && !Server.Systems.Fishing.IsRod(Client.Items.Find("T3_2H_TOOL_SICKLE")!)
              && Client.GameData!.FishingSafety == 3,
              "T3_2H_TOOL_FISHINGROD is item 3015 with fishing=true and fishingspeed 0.05; a T4 common fish 40 fame, setting T3; gamedata Fishing safety 3");
        var loot = Store.Loot;
        var thetford = Store.Get("0000");
        Check(Server.Systems.Fishing.ListOf(thetford.Name, builtIn.Zones["FRESHWATER_BASE"], false, loot) == "FISHING_ZONE_FRESHWATER_BASE_SWAMPCITY_LOOT"
              && Server.Systems.Fishing.ListOf(thetford.Name, builtIn.Zones["FRESHWATER_SPOT_TREASURE"], false, loot) == "T1_FISHING_ZONE_TREASURE"
              && Server.Systems.Fishing.ListOf(thetford.Name, builtIn.Freshwater, false, loot) == "T1_FISHING_ZONE_DEFAULT_FRESHWATER_LOOT"
              && Server.Systems.Fishing.BaseList(ClusterName.Parse("0204_WRL_SW_AUTO_T4_UND_ROY.cluster.xml"), false) == "T4_FISHING_ZONE_FRESHWATER_BASE_SWAMP_LOOT"
              && Server.Systems.Fishing.BaseList(ClusterName.Parse("1234_WRL_HL_AUTO_T6_UND_ROY.cluster.xml"), true) == "T6_FISHING_ZONE_OCEAN_BASE_ALL_LOOT"
              && loot.Has(Server.Systems.Fishing.BaseList(ClusterName.Parse("1234_WRL_HL_AUTO_T6_UND_ROY.cluster.xml"), false)),
              "loot lists: Thetford SWAMPCITY, its treasure and default lists, 0204 T4 SWAMP, a T6 highland ocean / freshwater");

        // Every zone of the capture at a template zone of its position (the fixture keeps 0.1 m), with its capacity and prefab.
        // Capture-derived fixtures are not in the public repository: the comparison is skipped without it.
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fishing_zones_002835.json");
        if (!File.Exists(fixture)) { Console.WriteLine("      (capture fixture fishing_zones_002835.json absent: comparison skipped)"); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
        var day15 = ResourceNodeTest.Day(Capture002835Key57);
        var (matched, own) = (0, 0);
        // The capture tool files a zone under the cluster of the last Join response, so the few that came between a cluster
        // change and the next Join are under the wrong one (Thetford's 140 under 1000, the bank's and market's under 0000):
        // each must be a zone of its own cluster or of another cluster of the fixture.
        var templates = doc.RootElement.EnumerateObject().Select(c => day15.Get(c.Name)).ToList();
        foreach (var cluster in doc.RootElement.EnumerateObject())
        {
            foreach (var z in cluster.Value.EnumerateObject())
            {
                var pos = new Vector2(z.Value.GetProperty("pos")[0].GetSingle(), z.Value.GetProperty("pos")[1].GetSingle());
                var spawn = templates.OrderBy(t => t.Id == cluster.Name ? 0 : 1).SelectMany(t => t.FishingZones).FirstOrDefault(f => Vector2.Distance(f.Position, pos) <= 0.06f);
                var kind = spawn?.Zone is { } name ? builtIn.Zones.GetValueOrDefault(name) : null;
                Check(kind is not null && kind.MaxCharges == z.Value.GetProperty("capacity").GetInt32() && kind.Prefab == z.Value.GetProperty("prefab").GetString()
                      && z.Value.GetProperty("seen").EnumerateArray().All(s => s[0].GetInt32() + s[1].GetInt32() == kind.MaxCharges),
                      $"{cluster.Name} zone {z.Name} at ({pos.X}, {pos.Y}): a template {spawn?.Zone} with capacity {kind?.MaxCharges}, prefab '{kind?.Prefab}'");
                matched++;
                if (spawn!.Key.Cluster == cluster.Name) own++;
            }
        }
        Check(matched == 52 && own == 41, $"52 captured zones, most in their own cluster (got {matched}, {own})");

        // The wire: NewFishingZoneObject keys 2/3 Int16, key 4 a String also when "".
        var map = new Map("0000");
        var zone = new FishingZone(1, thetford.FishingZones.First(f => f.Zone == "FRESHWATER_BASE"), builtIn.Zones["FRESHWATER_BASE"]) { Charges = 14 };
        var wire = Wire(AlbionSerializer.BuildEvent(zone.Changed()));
        Check(wire[2] is (short)14 && wire[3] is (short)1 && wire[4] is "" && wire[1] is float[] { Length: 2 } && map.FishingZones.Count == 0,
              "NewFishingZoneObject: 2 = 14 and 3 = 1 as Int16, 4 = \"\" sent");
    }

    /// <summary>The keys of an event as the client gets them (written and read back as Protocol18).</summary>
    private static Dictionary<byte, object?> Wire(EventData e)
    {
        var w = new ByteWriter();
        Protocol18.WriteEventData(w, e);
        return Protocol18.ReadEventData(new ByteReader(w.ToArray())).Parameters;
    }

    private static int Code(EventData e) => WorldObjectTest.Code(e);

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static void Cast()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var anchor = GameTime.Now;
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = Store, MapSeed = 11, GameTicks = () => anchor + clock.Now.Ticks,
        };
        try
        {
            var thetford = Store.Get("0000");
            // A round base zone of Thetford with no spot in it: casts into its centre from 6 m.
            var spawn = thetford.FishingZones.First(f => f.Zone == "FRESHWATER_BASE" && f.Radius > 0
                && thetford.FishingZones.All(o => o == f || o.Zone == "FRESHWATER_BASE" || Vector2.Distance(o.Position, f.Position) > f.Radius + 6));
            var stand = spawn.Position + new Vector2(6, 0);
            (WorldSession Session, FakePeer Peer, Player Player) Join(string name, Vector2 at)
            {
                var c = StarterKit.Default.NewCharacter(name);
                (c.Cluster, c.PositionX, c.PositionY) = ("0000", at.X, at.Y);
                c.Inventory = [new("T3_2H_TOOL_FISHINGROD")];
                Check(characters.Create(c), $"character {name} created");
                var peer = new FakePeer();
                var session = new WorldSession(peer, world);
                session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
                return (session, peer, session.Player!);
            }
            var (fs, fp, fisher) = Join("Fisher", stand);
            var (_, op, observer) = Join("Watcher", stand + new Vector2(0, 3));
            Check(fisher.Observers.Contains(observer), "the two players see each other");
            var map = fisher.Map!;
            var zone = map.FishingZones.Single(z => z.Spawn.Key == spawn.Key);
            Check(fisher.Visibility.Knows(zone) && observer.Visibility.Knows(zone) && zone.Charges == 15, "both know the zone, 15 charges");
            var rod = fisher.Inventory.Bag.Items.Single(i => i.Template.Name == "T3_2H_TOOL_FISHINGROD");
            void Tick(double seconds) { clock.Advance(TimeSpan.FromSeconds(seconds)); lock (world.Sync) world.Update(); }
            List<EventData> Since(FakePeer p, int from) => [.. p.Sequence.Skip(from).OfType<EventData>()];
            string Names(IEnumerable<object> seq) => string.Join(", ", seq.Select(x => x switch
            {
                EventData e => ((EventCodes)Code(e)).ToString(),
                OperationResponse r => $"rc {r.ReturnCode}",
                _ => "?",
            }));
            IEnumerable<EventData> Of(IEnumerable<EventData> events, EventCodes code) => events.Where(e => Code(e) == (int)code);
            int State(EventData e) => Convert.ToInt32(e.Parameters[3]);

            // 1. FishingStart → FishingStart event {3 = 3, 4 = the request's key 0} to both, then rc 0.
            var start = world.GameNow;
            var (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingStartRequest { wf = start, lr = spawn.Position, ls = rod.Id }, 20), encrypted: false);
            var got = fp.Sequence.Skip(f0).ToList();
            Check(Names(got) == "FishingStart, rc 0" && got[0] is EventData s3 && Wire(s3) is var k3 && k3.Keys.Where(k => k < 250).Order().SequenceEqual(new byte[] { 0, 1, 2, 3, 4 })
                  && Convert.ToInt64(k3[1]) == rod.Id && k3[2] is 3015 && k3[3] is 3 && Convert.ToInt64(k3[4]) == start
                  && Of(Since(op, o0), EventCodes.FishingStart).Count() == 1,
                  $"FishingStart: event {{0-4: rod id, 3015, 3 Casting, the stamp}} to both, then rc 0 (got {Names(got)})");

            // 2. Cast after 0.5 s of charge: state 4; the float after 0.44 + 0.3 × 0.5 = 0.59 s.
            Tick(0.5);
            fs.OnRequest(Request(new FishingCastRequest { wf = start + TimeSpan.TicksPerSecond / 2, ln = spawn.Position, lo = true }, 21), encrypted: false);
            Check(Of(Since(fp, f0), EventCodes.FishingStart).Select(State).SequenceEqual([3, 4]) && fisher.Fishing is { State: FishingState.Cast, Power: 0.5f } s
                  && s.Zone == zone, $"FishingCast in water: state 4, power 0.5, the base zone picked (got {fisher.Fishing?.State}, {fisher.Fishing?.Power}, {fisher.Fishing?.Zone}, want {zone}; {Names(fp.Sequence.Skip(f0))})");
            Tick(0.55);
            Check(fisher.Fishing!.Float is null, "no float before its flight");
            Tick(0.05);
            var floatAt = clock.Now;
            Check(fisher.Fishing.Float is { } fl && Of(Since(op, o0), EventCodes.NewFloatObject).Single() is var nf && Convert.ToInt32(nf.Parameters[4]) == 1
                  && Convert.ToInt64(nf.Parameters[3]) == fisher.Id && Of(Since(fp, f0), EventCodes.NewFloatObject).Count() == 1,
                  "the float splashes at 0.6 s (state 1, owner the fisher) for both");

            // 3. The bite within the base zone's 2-25 s (÷ 1.05 for the rod's fishingspeed): state 5 with the bite time, float 2.
            var bitten = TimeSpan.Zero;
            for (var i = 0; i < 600 && fisher.Fishing?.State != FishingState.Bitten; i++) Tick(0.05);
            bitten = clock.Now - floatAt;
            var bite = Of(Since(fp, f0), EventCodes.FishingStart).Last();
            Check(fisher.Fishing?.State == FishingState.Bitten && bitten.TotalSeconds >= 2 / 1.05 - 0.06 && bitten.TotalSeconds <= 25 / 1.05 + 0.06
                  && State(bite) == 5 && Convert.ToInt64(bite.Parameters[4]) == fisher.Fishing.BiteStamp && Convert.ToInt32(Of(Since(op, o0), EventCodes.NewFloatObject).Last().Parameters[4]) == 2,
                  $"the bite after {bitten.TotalSeconds:0.##} s: FishingStart 5 with its own stamp, the float bitten for the observer");

            // 4. The strike: zone 14/1, float 3, FishingMiniGame (keys 3-15 Single, 14 left out), rc 0; the observer no minigame.
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingCatchRequest { wf = world.GameNow, lp = false }, 22), encrypted: false);
            got = fp.Sequence.Skip(f0).ToList();
            var mg = Of(Since(fp, f0), EventCodes.FishingMiniGame).SingleOrDefault();
            var mgWire = mg is null ? [] : Wire(mg);
            Check(Names(got) == "NewFishingZoneObject, NewFloatObject, FishingMiniGame, rc 0" && zone.Charges == 14
                  && Wire(Of(Since(op, o0), EventCodes.NewFishingZoneObject).Single()) is var zw && zw[2] is (short)14 && zw[3] is (short)1
                  && mgWire[1] is 6 && mgWire[3] is 2f && mgWire[7] is 1.1f && mgWire[9] is -1f && mgWire[15] is 7f && !mgWire.ContainsKey(14)
                  && Enumerable.Range(3, 13).Where(k => k != 14).All(k => mgWire[(byte)k] is float)
                  && !Of(Since(op, o0), EventCodes.FishingMiniGame).Any() && Convert.ToInt32(Of(Since(op, o0), EventCodes.NewFloatObject).Single().Parameters[4]) == 3
                  && fisher.Fishing is { State: FishingState.MiniGame, Catch.Count: > 0 },
                  $"strike: zone 14 (key 3 = 1), float 3, FishingMiniGame {{1 6, 3-15 the captured setting as Single}} to the fisher only, rc 0 (got {Names(got)})");
            Check(fisher.Fishing!.Catch.All(c => c.Item.CanCreate), $"the catch is creatable ({string.Join(", ", fisher.Fishing!.Catch.Select(c => c.Item.Name))})");
            // A player who gets the fisher in view now (state MiniGame) gets the whole FishingMiniGame after its NewCharacter (§12.12).
            var (_, lp, _) = Join("Latecomer", stand + new Vector2(-3, 0));
            var seen = lp.Events.FindIndex(e => Code(e) == (int)EventCodes.NewCharacter && Convert.ToInt64(e.Parameters[0]) == fisher.Id);
            var late = lp.Events.FindIndex(e => Code(e) == (int)EventCodes.FishingMiniGame && Convert.ToInt64(e.Parameters[0]) == fisher.Id);
            Check(seen >= 0 && late > seen && !lp.Events.Any(e => Code(e) == (int)EventCodes.FishingStart),
                  "a new observer during the minigame: NewCharacter, then FishingMiniGame (no FishingStart)");
            // Deterministic below: one T4 common (40 fishing fame).
            fisher.Fishing.Catch = [(Client.Items.Find("T4_FISH_FRESHWATER_ALL_COMMON")!, 1)];

            // 5. Pull / GiveLine → FishingStart 7 / 8 to the observer.
            fs.OnRequest(Request(new FishingPullRequest { wf = world.GameNow }, 23), encrypted: false);
            Tick(1.5);
            fs.OnRequest(Request(new FishingGiveLineRequest { wf = world.GameNow }, 24), encrypted: false);
            Check(Of(Since(op, o0), EventCodes.FishingStart).Select(State).SequenceEqual([7, 8]), "Pull / GiveLine: FishingStart 7, 8 to the observer");

            // 6. FishingFinish(true) 3 s after the strike: rod wear, fame 40, the fish, FishingStart 9, float 4; nothing of it but 9/4 to the observer.
            Tick(1.5);
            var durability = rod.DurabilityRaw;
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingFinishRequest { wf = world.GameNow, lq = true }, null), encrypted: false);
            got = fp.Sequence.Skip(f0).ToList();
            var fame = Of(Since(fp, f0), EventCodes.UpdateFame).SingleOrDefault();
            var fishItem = fisher.Inventory.Bag.Items.SingleOrDefault(i => i.Template.Name == "T4_FISH_FRESHWATER_ALL_COMMON");
            Check(Names(got) == "NewEquipmentItem, PersonalSeasonPointsGained, UpdateFame, NewSimpleItem, InventoryPutItem, FishingStart, NewFloatObject"
                  && fame is not null && Convert.ToInt64(fame.Parameters[2]) == 40 * 10000 && fame.Parameters.ContainsKey(5) && !fame.Parameters.ContainsKey(15)
                  && fishItem is { Count: 1 } && rod.DurabilityRaw == durability - 10000
                  && State(Of(Since(fp, f0), EventCodes.FishingStart).Single()) == 9
                  && Since(op, o0).Select(e => ((EventCodes)Code(e)).ToString()).SequenceEqual(["FishingStart", "NewFloatObject"])
                  && Convert.ToInt32(Of(Since(op, o0), EventCodes.NewFloatObject).Single().Parameters[4]) == 4,
                  $"success: rod -1 durability, UpdateFame 40 (key 5, no 15), the fish, FishingStart 9, float 4; the observer only 9 and 4 (got {Names(got)})");

            // 7. The client's FishingCancel: FishingCancel {1 9} and the float's Leave, to both.
            var floatId = fisher.Fishing!.Float!.Id;
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingCancelRequest(), null), encrypted: false);
            Check(Names(fp.Sequence.Skip(f0)) == "FishingCancel, Leave" && Convert.ToInt32(fp.Events[^2].Parameters[1]) == 9 && Convert.ToInt64(fp.Events[^1].Parameters[0]) == floatId
                  && Names(op.Sequence.Skip(o0)) == "FishingCancel, Leave" && fisher.Fishing is null && !map.Objects.ContainsKey(floatId),
                  "Cancel: FishingCancel {1 9}, the float's Leave, to both; the session is gone");

            // 7b. Reel-in before any bite: FishingCancel {1 4}, rc 0. A refused request ends the session too (the client aborts
            // on its own and sends no FishingCancel): a strike while Casting gets rc 1 and FishingCancel {1 3}. A Casting without
            // FishingCast ends 1 s + 5 s later. A ground cast: FishingStart 11, then the client's Cancel gives {1 11}, nothing else.
            var cancels = Of(fp.Events, EventCodes.FishingCancel).Count();
            int LastCancel() => Convert.ToInt32(Of(fp.Events, EventCodes.FishingCancel).Last().Parameters[1]);
            fs.OnRequest(Request(new FishingStartRequest { wf = world.GameNow, lr = spawn.Position, ls = rod.Id }, 25), encrypted: false);
            fs.OnRequest(Request(new FishingCastRequest { wf = world.GameNow, ln = spawn.Position, lo = true }, 26), encrypted: false);
            f0 = fp.Sequence.Count;
            fs.OnRequest(Request(new FishingCatchRequest { wf = world.GameNow, lp = true }, 27), encrypted: false);
            Check(Names(fp.Sequence.Skip(f0)) == "FishingCancel, rc 0" && LastCancel() == 4 && fisher.Fishing is null, "reel-in before the float: FishingCancel {1 4}, rc 0");
            fs.OnRequest(Request(new FishingStartRequest { wf = world.GameNow, lr = spawn.Position, ls = rod.Id }, 28), encrypted: false);
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingCatchRequest { wf = world.GameNow, lp = false }, 29), encrypted: false);
            Check(Names(fp.Sequence.Skip(f0)) == "FishingCancel, rc 1" && LastCancel() == 3 && Names(op.Sequence.Skip(o0)) == "FishingCancel" && fisher.Fishing is null,
                  $"a strike while Casting: FishingCancel {{1 3}} to both, rc 1 (got {Names(fp.Sequence.Skip(f0))})");
            fs.OnRequest(Request(new FishingStartRequest { wf = world.GameNow, lr = spawn.Position, ls = rod.Id }, 28), encrypted: false);
            Tick(5.9);
            var waiting = fisher.Fishing is { State: FishingState.Casting };
            Tick(0.15);
            Check(waiting && fisher.Fishing is null && LastCancel() == 3, "no FishingCast: the session ends 6 s after FishingStart (FishingCancel 3)");
            fs.OnRequest(Request(new FishingStartRequest { wf = world.GameNow, lr = spawn.Position, ls = rod.Id }, 28), encrypted: false);
            f0 = fp.Sequence.Count;
            fs.OnRequest(Request(new FishingCastRequest { wf = world.GameNow, ln = spawn.Position, lo = false }, 26), encrypted: false);
            var ground = Of(Since(fp, f0), EventCodes.FishingStart).SingleOrDefault();
            Tick(1);
            var bagCount = fisher.Inventory.Bag.Items.Count();
            fs.OnRequest(Request(new FishingCancelRequest(), null), encrypted: false);
            Check(ground is not null && State(ground) == 11 && LastCancel() == 11 && fisher.Fishing is null && fisher.Inventory.Bag.Items.Count() == bagCount
                  && !Of(Since(fp, f0), EventCodes.UpdateFame).Any() && Of(fp.Events, EventCodes.FishingCancel).Count() == cancels + 4,
                  $"a ground cast: FishingStart 11, the client's Cancel {{1 11}}, no item or fame (got {Names(fp.Sequence.Skip(f0))})");

            // 8. A missed bite: FishingCancel {1 5, 2 1} and the Leave 0.9 s after it (0.8 s window + 0.1 s slack, ± a tick); the zone keeps its charges.
            int Cast(long at)
            {
                fs.OnRequest(Request(new FishingStartRequest { wf = at, lr = spawn.Position, ls = rod.Id }, 30), encrypted: false);
                fs.OnRequest(Request(new FishingCastRequest { wf = at, ln = spawn.Position, lo = true }, 31), encrypted: false);
                var n = 0;
                for (; n < 700 && fisher.Fishing?.State != FishingState.Bitten; n++) Tick(0.05);
                return n;
            }
            Cast(world.GameNow);
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            var ticks = 0;
            while (fisher.Fishing is not null && ticks < 40) { Tick(0.05); ticks++; }
            var cancel = Of(Since(fp, f0), EventCodes.FishingCancel).SingleOrDefault();
            Check(ticks == 18 && cancel is not null && Convert.ToInt32(cancel.Parameters[1]) == 5 && Convert.ToInt32(cancel.Parameters[2]) == 1
                  && Names(fp.Sequence.Skip(f0)) == "FishingCancel, Leave" && Names(op.Sequence.Skip(o0)) == "FishingCancel, Leave" && zone.Charges == 14,
                  $"miss: FishingCancel {{1 5, 2 1}} + Leave 0.9 s after the bite (got {ticks} ticks, {Names(fp.Sequence.Skip(f0))}), zone still 14");

            // 9. A success 1 s after the strike counts as a fail: FishingStart 10, float 5, no item; the charge stays used.
            Cast(world.GameNow);
            fs.OnRequest(Request(new FishingCatchRequest { wf = world.GameNow, lp = false }, 32), encrypted: false);
            Tick(1);
            var items = fisher.Inventory.Bag.Items.Count();
            (f0, o0) = (fp.Sequence.Count, op.Sequence.Count);
            fs.OnRequest(Request(new FishingFinishRequest { wf = world.GameNow, lq = true }, null), encrypted: false);
            Check(Names(fp.Sequence.Skip(f0)) == "FishingStart, NewFloatObject" && State(fp.Events[^2]) == 10 && Convert.ToInt32(fp.Events[^1].Parameters[4]) == 5
                  && fisher.Inventory.Bag.Items.Count() == items && zone.Charges == 13,
                  $"a success 1 s after the strike: Fail (10), float 5, no item, the zone at 13 (got {Names(fp.Sequence.Skip(f0))})");
            // Without the client's FishingCancel the server ends it after 5 s.
            Tick(5.05);
            Check(fisher.Fishing is null && Convert.ToInt32(Of(fp.Events, EventCodes.FishingCancel).Last().Parameters[1]) == 10, "the fallback ends the failed session after 5 s (FishingCancel 10)");
            Check(fp.Events.Concat(op.Events).Where(e => Code(e) == (int)EventCodes.FishingStart).All(e => State(e) is not (6 or 12 or 13)), "never FishingStart 6, 12 or 13");

            // 9b. The rod leaves the bag before the strike: ToolFail (FishingCancel {1 12}), rc 1, no charge taken.
            Cast(world.GameNow);
            var rodSlot = fisher.Inventory.Bag.IndexOf(rod);
            fisher.Inventory.Bag.Take(rodSlot);
            fs.OnRequest(Request(new FishingCatchRequest { wf = world.GameNow, lp = false }, 33), encrypted: false);
            fisher.Inventory.Bag.Put(rodSlot, rod);
            Check(fisher.Fishing is null && LastCancel() == 12 && zone.Charges == 13 && fp.Sequence.Last() is OperationResponse { ReturnCode: 1 },
                  "the rod gone at the strike: FishingCancel {1 12}, rc 1, the zone still 13");

            // 10. The zone pick on 0204 (Thetford has base zones only): a spot with charges wins over the base zone around it, an
            // empty one is skipped (SPEC §7.1, 7 of 10 captured strikes); a point in no zone is the default freshwater. Then the
            // spot's regrowth: at 0 it waits 90 s + 120-180 s for its charge.
            var wild = new Map("0204");
            lock (world.Sync) MapSpawner.Populate(wild, Store.Get("0204"), world);
            var spot = wild.FishingZones.FirstOrDefault(z => z.Name == "FRESHWATER_SPOT_FISH" && wild.FishingZones.Any(b => b.Name == "FRESHWATER_BASE" && b.Contains(z.Position)));
            Check(spot is not null && wild.FishingZones.Count(z => z.Settings.Prefab == "FishingNodeSwarm") > 0, "0204: a fish spot inside a base zone, swarms");
            var around = wild.FishingZones.Where(b => b.Name == "FRESHWATER_BASE" && b.Contains(spot!.Position)).ToList();
            Check(Server.Systems.Fishing.Pick(world, wild, spot!.Position).Zone == spot && Server.Systems.Fishing.Pick(world, wild, new Vector2(10000, 10000)) is (null, false),
                  "the spot wins over its base zone; far off: the default freshwater");
            var at = clock.Now;
            lock (world.Sync) Server.Systems.Fishing.TakeCharge(wild, spot, at);
            Check(spot.Charges == 0 && around.Contains(Server.Systems.Fishing.Pick(world, wild, spot.Position).Zone!)
                  && spot.RegrowAt is { } due && due - at >= TimeSpan.FromSeconds(210) && due - at <= TimeSpan.FromSeconds(270),
                  $"the empty spot is skipped (its base zone pays); its charge is due in 90 + 120-180 s (got {(spot.RegrowAt - at)?.TotalSeconds:0} s)");
            lock (world.Sync) Server.Systems.Fishing.Update(wild, at + TimeSpan.FromSeconds(209));
            Check(spot.Charges == 0, "not back before its time");
            lock (world.Sync) Server.Systems.Fishing.Update(wild, at + TimeSpan.FromSeconds(271));
            Check(spot.Charges == 1 && spot.RegrowAt is null, "the spot regrew to 1");
        }
        finally
        {
            world.Dispose();
            characters.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
