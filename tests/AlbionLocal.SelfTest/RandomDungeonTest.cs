using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

namespace AlbionLocal.SelfTest;

/// <summary>
/// Random dungeons (stageG, analysis/work/stageG/dungeons/SPEC.md, "SPEC" here, §12): the layout bytes against golden_a9p.json
/// (rdgen.py, 517 bytes), the ids and names, the generator's rules over the four factions, the exits on the 0204 markers of
/// 2026-10-15 with event 324, and a dungeon walked through on FakePeer sessions and a manual clock: in, the floors, out, the
/// escape, the login inside, the closing.
/// </summary>
public static class RandomDungeonTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("dungeons: layout bytes = golden_a9p.json (517, sha256), read back; ids and names; the golden floor's exits ...0001/...0002 and arrivals", Golden),
        ("dungeons: the client's generator over 4 factions x solo/veteran x 20 seeds (doors meet, branches, rooms reached from the Start, budget, highlights + boss, one Exit last, ...0001/...0002, usage)", Generator),
        ("dungeons: 0204 (2026-10-15) markers, event 324 at (90, -260) (keys, prefab/visualization, reroll resend with the same id), fill ratio", ExitEvent),
        ("dungeons: in (1640, deferred answer {0, 3}), Join key 8, NewExit ...0001/...0002, 1647, floor 2 and back, escape, out to 0204, login inside, closing", Walk),
        ("dungeons: a SOLO portal clicked (237: corrupted 254, 9 m off 254, accepted no response and 253 to another, 238 254, 2 s: 255, the push {0, 3}, the Join)", Click),
        ("dungeons: inside (level-2 mob buff, lootchests.xml chests: NewLootChest, UseLootChest x Rates:Loot), a dungeon map (exit, 391, 14965), death -> entrance", Inside),
        ("dungeons: PvE (group scaling 0.55 for one, dungeon of the day x1.5 and its chat line, a boss chest BossLocked until its guards are gone)", Pve),
    ];

    private static ClusterStore Store => Client.Clusters!;

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static int Code(EventData e) => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode));

    private static T Fill<T>(OperationResponse r) where T : new()
    {
        var m = new T();
        AlbionSerializer.FillFields(m, r.Parameters);
        return m;
    }

    private static bool Near(Vector2 a, Vector2 b, float tolerance = 0.01f) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    /// <summary>SPEC §5.5: the fixed two-template layout of golden.py.</summary>
    private static DungeonLayout GoldenLayout()
    {
        const string id = "@RANDOMDUNGEON@00000000-0000-0000-0000-0000000000aa";
        return new DungeonLayout
        {
            Id = id, Name = id + "_RDG_FR_GRN_T4_HER_NON", Origin = new Vector2(-40, -40), Size = new Vector2(80, 160),
            Templates =
            [
                new DungeonTemplate("instanceslot_00", "HER_MINE_START_FirstLevel_Solo_01", "GREEN", Vector2.Zero, 0, ["Layer_06", "Layer_12"]),
                new DungeonTemplate("instanceslot_01", "HER_MINE_EXIT_Elevator_01", "GREEN", new Vector2(0, 80), 0, ["Layer_06", "Layer_02"]),
            ],
            DisplayName = "Hidden Mines", ClusterType = "RD_SAFEAREA", UpCluster = "0204", DownCluster = "0204", Entrance = "0204",
            EntrancePosition = new Vector2(90, -260), Subtype = 1, Generator = "HERETIC_RANDOM_DUNGEON_SOLO",
        };
    }

    private static void Golden()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dungeon_golden_a9p.json")));
        var golden = doc.RootElement;
        var layout = GoldenLayout();
        var bytes = layout.Write();
        Check(bytes.Length == 517 && bytes.Length == golden.GetProperty("length").GetInt32() && Convert.ToHexString(bytes).ToLowerInvariant() == golden.GetProperty("hex").GetString()
              && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == "5f08093c000f8fc163d4dba26a354382cf93aca9f4899148080c2d28aff136b8",
              $"the a9p writer gives the golden 517 bytes, sha256 5f08093c… (got {bytes.Length})");
        var back = DungeonLayout.Read(bytes);
        Check(back is { Id: var i, Templates.Count: 2, Generator: "HERETIC_RANDOM_DUNGEON_SOLO", Floor: 1, Floors: 1, UpExit: var g, Flags: 0 } && i == layout.Id && g == Guid.Empty
              && back.Templates[1].Position == new Vector2(0, 80) && back.Templates[0].Layers.SequenceEqual(["Layer_06", "Layer_12"]),
              "read back as the client reads it: type 1, 2 templates, ak = HERETIC_RANDOM_DUNGEON_SOLO");
        var threw = false;
        try { _ = (layout with { UpName = null! }).Write(); }
        catch (InvalidDataException) { threw = true; }
        var badGuid = false;
        try { _ = DungeonLayout.Read([.. bytes, 0]); }
        catch (InvalidDataException) { badGuid = true; }
        Check(threw && badGuid, "a null string makes the writer throw (as BinaryWriter.Write(null)); trailing bytes make the reader throw");

        var shipped = DungeonSettings.Parse(File.ReadAllText(Path.Combine(DataDir(), "..", "content", "dungeons.json")), "dungeons.json");
        var builtIn = DungeonSettings.Default;
        Check(shipped == builtIn with { PerPartySubtypes = shipped.PerPartySubtypes, GroupScaledSubtypes = shipped.GroupScaledSubtypes, DailyFactions = shipped.DailyFactions }
              && shipped.PerPartySubtypes.SetEquals(builtIn.PerPartySubtypes) && shipped.GroupScaledSubtypes.SetEquals(builtIn.GroupScaledSubtypes)
              && shipped.DailyFactions.SequenceEqual(builtIn.DailyFactions),
              "content/dungeons.json parses and holds the built-in values");
        var id = DungeonLayout.InstanceId(Guid.NewGuid());
        var name = DungeonLayout.ClusterName(id, false, 4, "HERETIC");
        Check(Regex.IsMatch(id, "^@RANDOMDUNGEON@[0-9a-f-]{36}$") && name.Split('_') is [var i0, "RDG", "FR", "GRN", "T4", "HER", "NON"] && i0 == id
              && ClusterNameInfo.Parse(name) is { Type: 17, Biome: 4, SubBiome: 0, Tier: 4, Faction: 4 } && ClusterName.Parse(name).TemplateFolder == "GREEN",
              $"id {id} and name {name}: [id, RDG, FR, GRN, T4, HER, NON], parsed as RandomDungeon Forest/Green T4 Heretic");

        var floor = Store.FromLayout(layout);
        var exits = floor.Exits;
        var want = golden.GetProperty("exits").EnumerateArray().Select(e => (Pos: new Vector2(e.GetProperty("pos")[0].GetSingle(), e.GetProperty("pos")[1].GetSingle()),
            Arrival: new Vector2(e.GetProperty("arrival")[0].GetSingle(), e.GetProperty("arrival")[1].GetSingle()), Facing: e.GetProperty("facing").GetSingle(),
            Guid: Guid.Parse(e.GetProperty("guid").GetString()!))).ToList();
        Check(exits.Count == 2 && exits.Zip(want).All(x => x.First.Guid == x.Second.Guid && Near(x.First.Position, x.Second.Pos) && x.First.SpawnPosition is { } sp
                  && Near(sp, x.Second.Arrival) && Math.Abs(Transfers.Facing(x.First.Position, sp) - x.Second.Facing) < 0.01f && x.First.World is { } w
                  && w.Id == x.Second.Guid && w.TargetCluster == "0204")
              && Transfers.Size(exits[0]) == new Vector2(6, 6) && Transfers.Size(exits[1]) == new Vector2(10, 10) && floor.Problems.Count == 0
              && floor.Info.Id == layout.Id && floor.Origin == new Vector2(-40, -40),
              $"the golden floor's exits in load order: …0001 at (-5, -14) 6x6, …0002 at (0, 91) 10x10, arrivals and facings as rdgen (got {string.Join("; ", exits.Select(e => $"{e.Guid} {e.Position} {e.SpawnPosition}"))})");
        Check(!exits.Any(e => exits.Any(x => Transfers.InSquare(x.Position, Transfers.Size(x), e.SpawnPosition!.Value))), "no arrival inside an exit square");
    }

    private static void Generator()
    {
        var data = Store.Dungeons;
        Check(data.Generators.Count > 30 && data.Exits.ContainsKey("T4_PORTAL_ROYAL_SOLO") && data.Visualizations.ContainsKey("PORTAL_SOLO")
              && data.Templates.Count(t => true) > 100 && data.EmptyLifetime("SOLO") == 30 && data.EmptyLifetime("ELITE") == 7200 && data.EmptyLifetime("VETERAN") == 300
              && data.MaxLifetimeSeconds == 87000 && data.Difficulties[2] is { Silver: 1.8768, MobFame: 1.6592, MobModifier: "MOB_RANDOM_DUNGEON_BUFF_02" }
              && data.ClusterTypeFor("SAFEAREA", "HERETIC_RANDOM_DUNGEON_PORTAL_SOLO") == "RD_SAFEAREA" && data.ClusterTypeFor("NO_SUCH", null) == "DUNGEON_SAFEAREA",
              "randomdungeons.xml: generators, exits, visualizations, lifetimes (SOLO 30, ELITE 7200, default 300, max 87000), level 2 factors, RD_SAFEAREA");
        Check(DungeonGenerator.RotateMask(DungeonGenerator.F, 1) == DungeonGenerator.R && DungeonGenerator.RotateMask(DungeonGenerator.L, 1) == DungeonGenerator.F
              && DungeonGenerator.RotateMask(3, 2) == 12 && DungeonGenerator.UsageFits("First", 1, 1) && DungeonGenerator.UsageFits("FirstAndOthers", 2, 3)
              && DungeonGenerator.UsageFits("LastAndOthers", 2, 3) && DungeonGenerator.UsageFits("Others", 2, 3) && !DungeonGenerator.UsageFits("Others", 3, 3)
              && DungeonGenerator.UsageFits("Last", 2, 2) && !DungeonGenerator.UsageFits("Last", 1, 2) && !DungeonGenerator.UsageFits("First", 2, 2) && DungeonGenerator.UsageFits(null, 2, 2),
              "door masks turn F → R → B → L; dungeonlevelusage per floor (all five values)");
        var byName = data.Templates.SelectMany(i => i.Names.Select(n => (n.Name, Info: i))).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().Info);
        var problems = new List<string>();
        var (made, branches, junctions, sizes) = (0, 0, 0, new List<int>());
        foreach (var faction in new[] { "HERETIC", "KEEPER", "MORGANA", "UNDEAD" })
            foreach (var (generatorName, subtype) in new[] { ($"{faction}_RANDOM_DUNGEON_SOLO", (byte)1), ($"{faction}_RANDOM_DUNGEON", (byte)2) })
                for (var seed = 0; seed < 20; seed++)
                {
                    var plan = DungeonGenerator.Draw(data.Generators[generatorName], subtype, new Random(seed), seed % 4 == 3 ? 2 : 0);
                    var floors = plan.PathLengths.Count;
                    for (var f = 1; f <= floors; f++)
                    {
                        var id = DungeonLayout.InstanceId(Guid.NewGuid());
                        var links = new DungeonGenerator.Links(f == 1 ? "0204" : "@RANDOMDUNGEON@up", "", f == 1 ? Guid.Empty : DungeonExits.Down,
                            f < floors ? "@RANDOMDUNGEON@down" : "0204", "", f < floors ? DungeonExits.Up : Guid.Empty, "0204", new Vector2(90, -260));
                        DungeonLayout layout;
                        ClusterTemplate t;
                        try { (layout, t) = RandomDungeons.MakeFloor(Store, data, plan, f, id, 4, "RD_SAFEAREA", subtype, links, DungeonGenerator.IsDragon(data, plan), seed * 31 + f); }
                        catch (InvalidDataException e) { problems.Add($"{generatorName} seed {seed} floor {f}: {e.Message}"); continue; }
                        made++;
                        var where = $"{generatorName} seed {seed} floor {f}/{floors}";
                        var infos = layout.Templates.Select(x => byName.GetValueOrDefault(x.Template)).ToList();
                        if (infos.Any(i => i is null)) { problems.Add($"{where}: a template not in templatemetadata"); continue; }
                        var pathLength = infos.TakeWhile(i => i!.Type is "Start" or "Basic" or "Highlight" or "Exit").Count();
                        if (infos[0]!.Type != "Start" || infos[pathLength - 1]!.Type != "Exit" || infos.Count(i => i!.Type == "Exit") != 1 || infos.Count(i => i!.Type == "Start") != 1)
                            problems.Add($"{where}: not Start first and exactly one Exit last");
                        if (!DungeonGenerator.UsageFits(infos[0]!.LevelUsage, f, floors) || !DungeonGenerator.UsageFits(infos[pathLength - 1]!.LevelUsage, f, floors))
                            problems.Add($"{where}: Start/Exit usage {infos[0]!.LevelUsage}/{infos[pathLength - 1]!.LevelUsage} does not fit");
                        // DGEN: every door of a room meets the opposite door of another (a highlight's spare doors may stay shut), all rooms
                        // hang together from the Start, the budget is spent, the floor's highlights (and the last floor's boss) are there.
                        var doors = new List<(int Room, Vector2 At, int Dir)>();
                        for (var k = 0; k < pathLength; k++)
                            foreach (var d in DungeonGenerator.Doors(infos[k]!))
                            {
                                var turns = (int)(layout.Templates[k].Rotation / 90);
                                doors.Add((k, layout.Templates[k].Position + DungeonGenerator.Turn(d.Offset, turns), DungeonGenerator.RotateMask(d.Dir, turns)));
                            }
                        var meets = doors.Select(d => doors.Where(o => o.Room != d.Room && Vector2.Distance(o.At, d.At) < 0.01f && DungeonGenerator.RotateMask(o.Dir, 2) == d.Dir).Select(o => o.Room).ToList()).ToList();
                        var linked = new HashSet<int> { 0 };
                        for (var grew = true; grew;)
                        {
                            grew = false;
                            for (var n = 0; n < doors.Count; n++)
                                if (linked.Contains(doors[n].Room)) foreach (var o in meets[n]) grew |= linked.Add(o);
                        }
                        for (var n = 0; n < doors.Count; n++)
                            if (meets[n].Count == 0 && infos[doors[n].Room]!.Type != "Highlight")
                                problems.Add($"{where}: door {n} of {layout.Templates[doors[n].Room].Template} meets no room");
                        if (linked.Count != pathLength) problems.Add($"{where}: {pathLength - linked.Count} room(s) not reached from the Start");
                        var rooms = infos.Take(pathLength).ToList();
                        var spent = rooms.Where(i => i!.Type is "Basic" or "Highlight").Sum(i => i!.PathSize);
                        var bossNeeded = f == floors && data.Templates.Any(i => i.Type == "Highlight" && i.EndBoss && DungeonGenerator.Infos(data, plan).Contains(i));
                        if (spent < plan.PathLengths[f - 1] - rooms[^1]!.PathSize || rooms.Where(i => i!.Type == "Basic").Sum(i => i!.PathSize) > plan.PathLengths[f - 1] + 4
                            || rooms.Count(i => i!.Type == "Highlight") != plan.Highlights[f - 1] + (bossNeeded ? 1 : 0) || bossNeeded != rooms.Any(i => i!.EndBoss))
                            problems.Add($"{where}: {spent} cells of path for {plan.PathLengths[f - 1]}, {rooms.Count(i => i!.Type == "Highlight")} highlight(s) for {plan.Highlights[f - 1]}{(bossNeeded ? " + boss" : "")}");
                        branches += rooms.Count(i => i!.Type == "Highlight" && DungeonGenerator.Doors(i).Count == 1);
                        junctions += rooms.Count(i => i!.Type == "Basic" && DungeonGenerator.Doors(i).Count >= 3);
                        sizes.Add(pathLength);
                        if (t.Instances.Any(i => i.Folder is null)) problems.Add($"{where}: a template file is missing");
                        var exitOwners = t.Exits.Select(e => layout.Templates.First(x => x.Slot == e.Key.Slot)).ToList();
                        if (t.Exits.Count != 2 || t.Exits[0].Guid != DungeonExits.Up || t.Exits[1].Guid != DungeonExits.Down
                            || byName[exitOwners[0].Template].Type != "Start" || byName[exitOwners[1].Template].Type != "Exit")
                            problems.Add($"{where}: exits {string.Join(",", t.Exits.Select(e => e.Guid.ToString()[^1]))} not …0001 of the Start then …0002 of the Exit");
                        if (t.Exits.Any(e => e.SpawnPosition is not { } sp || t.Exits.Any(x => Transfers.InSquare(x.Position, Transfers.Size(x), sp))))
                            problems.Add($"{where}: an arrival lies in an exit square or is missing");
                        if (layout.Write() is var b && DungeonLayout.Read(b) is var r && (r.Templates.Count != layout.Templates.Count || r.UpCluster != links.UpCluster || r.DownExit != links.DownExit))
                            problems.Add($"{where}: the bytes do not read back");
                    }
                }
        Check(problems.Count == 0 && made >= 160 && branches > 20 && junctions * 2 >= branches && sizes.Min() >= 3 && sizes.Max() > 10,
              $"{made} floors made ({sizes.Min()}-{sizes.Max()} rooms, {branches} dead-end highlight branches on {junctions} junctions), every rule holds"
              + (problems.Count > 0 ? $"; {problems.Count} problem(s): {string.Join(" | ", problems.Take(8))}" : ""));
    }

    /// <summary>The 0204 of 2026-10-15 (capture 002835's day): its dungeon exit markers, as the world sees them.</summary>
    private static ClusterStore Day15 => ResourceNodeTest.Day(Capture002835Key57);

    private static void ExitEvent()
    {
        var store = Day15;
        var t0204 = store.Get("0204");
        var solo = t0204.DungeonMarkers.Where(m => m.Type == "SOLO").ToList();
        var marker = solo.FirstOrDefault(m => Near(m.Position, new Vector2(90, -260), 0.001f));
        Check(solo.Count == 67 && marker is { Direction: 180 } && t0204.DungeonMarkers.All(m => m.Type != "FIXED" || m.FixedExit is not null),
              $"0204 on 2026-10-15: 67 SOLO markers, one at (90, -260) turned 180 (got {solo.Count}, {marker?.Direction})");

        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 5,
        };
        var c = NewHero(characters, "0204", new Vector2(92, -262));
        var (session, peer, _) = Join(world, c.Id);
        var map = world.Maps.Find("0204")!;
        var data = store.Dungeons;
        var cap = (int)Math.Floor(solo.Count * data.Distribution[("Royal", "SOLO", 4)].MaxFillRatio);
        var filled = world.Dungeons.ExitsOf(map).Where(e => e.SubtypeName == "SOLO").ToList();
        // StageL corrupted (SPEC §6 phase B test 7, Corrupted:OpenWorldPortals): Royal SOLO T4 draws CORRUPTED_SOLO_NONLETHAL 1 time in 3, a
        // corrupted portal (324 key 12 true, no key 4: no random dungeon beneath).
        var corrupted = filled.Where(e => e.Name == "CORRUPTED_SOLO_NONLETHAL").ToList();
        var portal = corrupted.FirstOrDefault()?.BuildCreate(session.Player!).OfType<NewRandomDungeonExitEvent>().Single();
        Check(cap == 33 && filled.Count == cap && filled.All(e => e.Name is "T4_PORTAL_ROYAL_SOLO" or "CORRUPTED_SOLO_NONLETHAL" && solo.Any(m => m.Position == e.Position))
              && corrupted.Count is >= 5 and <= 18 && portal is { c8z: true, c8r: null } && corrupted.All(e => e.Info.Corrupted),
              $"the map fills floor(67 x 0.5) = 33 SOLO markers at once, T4_PORTAL_ROYAL_SOLO and about a third CORRUPTED_SOLO_NONLETHAL (key 12 true, no key 4) (got {filled.Count}, {corrupted.Count} corrupted)");

        var exit = world.Dungeons.SpawnOnMarker(map, marker!, "T4_PORTAL_ROYAL_SOLO");
        var sent = peer.Events.Where(e => Code(e) == (int)EventCodes.NewRandomDungeonExit && Convert.ToInt64(e.Parameters[0]) == exit.Id).ToList();
        var p = sent.FirstOrDefault()?.Parameters;
        var prefabs = new Dictionary<string, int>
        {
            ["SWAMP_GREEN_RANDOM_EXIT_10x10_PORTAL_SOLO_A"] = 225, ["SWAMP_GREEN_RANDOM_EXIT_10x10_PORTAL_SOLO_B"] = 226, ["SWAMP_GREEN_RANDOM_EXIT_10x10_PORTAL_SOLO_C"] = 227,
        };
        Check(sent.Count == 1 && p is not null && !p.ContainsKey(2) && p[3] is "T4_PORTAL_ROYAL_SOLO" && p[4] is "RD_SAFEAREA" && p[6] is string prefab
              && prefabs.TryGetValue(prefab, out var vis) && p[7] is int k7 && k7 == vis && p[18] is 1 && p[17] is -1L && p[21] is "" && p[8] is true && p[9] is byte
              && p[15] is (byte)0 && p[20] is (byte)0 && p[1] is float[] { Length: 2 } xz && xz[0] == 90 && xz[1] == -260
              && p.Keys.Where(x => x < 250).Order().SequenceEqual(new byte[] { 0, 1, 3, 4, 6, 7, 8, 9, 15, 17, 18, 20, 21 }),
              $"event 324 at the marker: no key 2 (180 + 180), 3 the exit, 4 RD_SAFEAREA, 6/7 a PORTAL_SOLO prefab with its index, 18 = 1, 17 = -1, 21 = \"\", the captured key set (got {string.Join(",", p?.Keys.Order() ?? Enumerable.Empty<byte>())})");
        var level = (byte)(exit.Level == 2 ? 3 : 2);
        var before = peer.Events.Count;
        world.Dungeons.Reroll(exit, level);
        var again = peer.Events.Skip(before).ToList();
        Check(again is [{ } re] && Code(re) == (int)EventCodes.NewRandomDungeonExit && Convert.ToInt64(re.Parameters[0]) == exit.Id && re.Parameters[9] is byte l && l == level,
              "a reroll: event 324 again with the same key 0 and the new key 9, no Leave before it");
        Check(Near(exit.ArrivalOut("0204").Position, new Vector2(90, -260) + exit.SpawnPos) && exit.Rotation == 0, "the way out: the marker + R(0)·spawnpos");
        session.OnDisconnected("test");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static Character NewHero(SqliteCharacterRepository characters, string cluster, Vector2 at, IReadOnlyList<InventoryItem>? bag = null)
    {
        var c = StarterKit.Default.NewCharacter("D" + Guid.NewGuid().ToString("N")[..10]);
        c.Cluster = cluster;
        c.PositionX = at.X;
        c.PositionY = at.Y;
        c.Equipment = [];
        c.Inventory = [.. bag ?? []];
        Check(characters.Create(c), "character created");
        return c;
    }

    private static (WorldSession Session, FakePeer Peer, JoinResponse Join) Join(World world, Guid key)
    {
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        lock (world.Sync) session.OnRequest(Request(new JoinRequest { ss = key }, null), encrypted: true);
        Check(peer.Responses is [{ ReturnCode: 0 }], "join answered");
        return (session, peer, Fill<JoinResponse>(peer.Responses[0]));
    }

    private static void Move(World world, WorldSession session, Vector2 to)
    {
        lock (world.Sync)
            session.OnRequest(Request(new MoveRequest { wf = DateTime.UtcNow.Ticks, v6 = [to.X, to.Y], v8 = [to.X, to.Y], v9 = 0f }, null), encrypted: false);
    }

    /// <summary>ChangeCluster as the client sends it; then the world ticks until the answer is in (a floor being made answers later).</summary>
    private static OperationResponse? Change(World world, WorldSession session, FakePeer peer, ChangeClusterRequest req, int requestId)
    {
        var before = peer.Responses.Count;
        lock (world.Sync) session.OnRequest(Request(req, requestId), encrypted: false);
        for (var waited = 0; peer.Responses.Count == before && waited < 30000; waited += 10)
        {
            Thread.Sleep(10);
            lock (world.Sync) world.Update();
        }
        return peer.Responses.Count > before ? peer.Responses[before] : null;
    }

    private static OperationResponse? Use(World world, WorldSession session, FakePeer peer, long exit, int requestId) =>
        Change(world, session, peer, new ChangeClusterRequest { ee = exit, ef = "", eg = [-1] }, requestId);

    private static bool Refused(OperationResponse? r, short rc, int requestId) =>
        r is not null && r.ReturnCode == rc && r.Parameters.GetValueOrDefault(ReservedKeys.RequestId) is int id && id == requestId && !r.Parameters.ContainsKey(0);

    private static Exit FloorExit(World world, string id, Guid guid) => world.Maps.Find(id)!.Objects.Values.OfType<Exit>().Single(e => e.World.Id == guid);

    /// <summary>
    /// SPEC §7, §10 and the death inside: the data of lootchests.xml and the chest points of generated floors; then, on FakePeer sessions
    /// and a manual clock in 0204, a dungeon map used from the bag (its exit on a free SOLO marker at the map's level, event 391), the
    /// portal at level 2 and its floor (every mob with MOB_RANDOM_DUNGEON_BUFF_02 and 1.36 × its health, the chests of its points), GM
    /// chests opened with Rates:Loot 0 and 20 (NewLootChest keys, UseLootChest, LootChestOpened, AttachItemContainer), the map refused
    /// inside (14965), and a death inside respawning in front of the portal.
    /// </summary>
    private static void Inside()
    {
        var store = Day15;
        _ = store.Get("0204");
        var data = store.Dungeons;
        var chests = store.LootChests;
        Check(chests.Spawnpoints.GetValueOrDefault("HERETIC_SOLO_LOOTCHEST_STANDARD") is { Chests: [("HERETIC_SOLO_CHEST_STANDARD", _)] }
              && chests.Chests.GetValueOrDefault("HERETIC_SOLO_CHEST_STANDARD") is { Prefab: "LOOTCHEST_REGULAR_01", States: [{ State: "standard", Rarity: 0 } standard] }
              && standard.LootFor(4)?.Elements("LootListReference").Any(e => (string?)e.Attribute("name") == "T4_RANDOM_DUNGEON_STANDARD") == true
              && standard.LootFor(9) == standard.LootByTier[8] && chests.Chests.Count > 300,
              "lootchests.xml: HERETIC_SOLO_LOOTCHEST_STANDARD → HERETIC_SOLO_CHEST_STANDARD (LOOTCHEST_REGULAR_01, standard = rarity 0, T4 loot T4_RANDOM_DUNGEON_STANDARD)");
        var (points, unbound) = (0, 0);
        foreach (var faction in new[] { "HERETIC", "KEEPER", "MORGANA", "UNDEAD" })
            for (var seed = 0; seed < 5; seed++)
            {
                var plan = DungeonGenerator.Draw(data.Generators[$"{faction}_RANDOM_DUNGEON_SOLO"], 1, new Random(seed), 1);
                var links = new DungeonGenerator.Links("0204", "", Guid.Empty, "0204", "", Guid.Empty, "0204", new Vector2(90, -260));
                var (_, t) = RandomDungeons.MakeFloor(Store, data, plan, 1, DungeonLayout.InstanceId(Guid.NewGuid()), 4, "RD_SAFEAREA", 1, links, DungeonGenerator.IsDragon(data, plan), seed);
                points += t.LootChests.Count;
                unbound += t.LootChests.Count(p => p.Behaviour is null);
            }
        Check(points > 0 && unbound == 0, $"the solo floors of 4 factions x 5 seeds have chest points in their reward layers, all bound to lootchests.xml ({points}, {unbound} unbound)");

        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 11, DungeonSettings = DungeonSettings.Default with { Floors = 1 },
        };
        const string token = "T4_RANDOM_DUNGEON_SOLO_TOKEN_D1";
        var hero = NewHero(characters, "0204", new Vector2(91, -259), [new InventoryItem(token, 3)]);
        var (a, peerA, _) = Join(world, hero.Id);
        var map = world.Maps.Find("0204")!;
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }

        // A dungeon map from the bag ("use all" on the stack: one map only): maxdistance 1, so its exit on a free SOLO marker of 0204 (a GREEN
        // cluster: the ROYAL block) or of a GREEN neighbour (dungeon-maps SPEC §3; the neighbours read beforehand), level 1, 900 s; event 391.
        var near = world.Dungeons.MapClusters("0204", 0, 1, true);
        foreach (var c in near) _ = store.Get(c);
        var map1 = a.Player!.Inventory.Bag.Items.First(i => i.Template.Name == token);
        var mark = peerA.Events.Count;
        lock (world.Sync) a.OnRequest(Request(new UseConsumableFromInventoryRequest { ah5 = map1.Id, ah6 = 2 }, 20), encrypted: false);
        for (var i = 0; i < 5; i++) Tick(0.2);
        var made = near.Select(world.Maps.Find).OfType<Map>().SelectMany(world.Dungeons.ExitsOf).Where(e => e.FromMap).ToList();
        var info = peerA.Events.Skip(mark).FirstOrDefault(e => Code(e) == (int)EventCodes.RandomDungeonPositionInfo)?.Parameters;
        Check(near.Count > 1 && near[0] == "0204" && !near.Contains("0000") && made is [{ FromMap: true, Level: 1, Name: "T4_PORTAL_ROYAL_CONSUMABLE_SOLO", SubtypeName: "SOLO", Marker: not null } placed]
              && placed.Expires is { } expires && Math.Abs((expires - clock.Now).TotalSeconds - 899) < 2 && info is not null && info[0] as string == placed.Map!.Cluster
              && info[4] is "T4_PORTAL_ROYAL_CONSUMABLE_SOLO" && Convert.ToInt32(info[3]) == 1 && info[1] is float[] { Length: 2 } xz && Near(new Vector2(xz[0], xz[1]), placed.Position)
              && map1.Count == 2 && peerA.Responses.LastOrDefault() is { ReturnCode: 0 },
              $"the dungeon map: T4_PORTAL_ROYAL_CONSUMABLE_SOLO on a free SOLO marker of 0204 or a GREEN neighbour ({string.Join(",", near)}), level 1, gone in 900 s; 391 {{0 its cluster, 1 its position, 3 Solo, 4 the exit}} (got {string.Join("; ", made)})");

        // Used, the map's portal stays for its time (not lifetimeafterlastuse, 30 s for a solo portal).
        var mapExit = made.First();
        lock (world.Sync) world.Dungeons.Used(mapExit);
        Tick(40);
        Check(mapExit.Map!.Objects.ContainsKey(mapExit.Id), "a used map portal still stands 40 s later (it goes at its time, with its reservation)");

        // Every SOLO marker of 0204 taken: the next map's portal stands in a GREEN neighbour, which is made for it and kept while it stands;
        // 391 names that cluster with key 6 false (the client opens the world map on it).
        lock (world.Sync)
            foreach (var m in store.Get("0204").DungeonMarkers.Where(m => m.Type == "SOLO" && !world.Dungeons.ExitsOf(map).Any(e => e.Marker == m)).ToList())
                world.Dungeons.SpawnOnMarker(map, m, "T4_PORTAL_ROYAL_SOLO");
        mark = peerA.Events.Count;
        lock (world.Sync) a.OnRequest(Request(new UseConsumableFromInventoryRequest { ah5 = map1.Id, ah6 = 1 }, 21), encrypted: false);
        Tick(0.2);
        var far = peerA.Events.Skip(mark).FirstOrDefault(e => Code(e) == (int)EventCodes.RandomDungeonPositionInfo)?.Parameters;
        var farMap = far?[0] is string farCluster ? world.Maps.Find(farCluster) : null;
        Check(far is not null && far[0] as string != "0204" && near.Contains((string)far[0]!) && far.GetValueOrDefault((byte)6) is null or false && farMap is not null
              && world.Dungeons.ExitsOf(farMap).Any(e => e.FromMap && e.Expires is not null) && world.Dungeons.HoldsMapExit(farMap) && map1.Count == 1,
              $"0204 full: the map's portal in the neighbour {far?[0]}, 391 key 6 false (absent), its map held while the portal stands");

        // The portal at level 2: its floor's mobs carry MOB_RANDOM_DUNGEON_BUFF_02 (hitpointsmaxbonus 0.36), the chest points their chests.
        var marker = store.Get("0204").DungeonMarkers.First(m => Near(m.Position, new Vector2(90, -260), 0.001f));
        RandomDungeonExit portal;
        lock (world.Sync)
        {
            portal = world.Dungeons.SpawnOnMarker(map, marker, "T4_PORTAL_ROYAL_SOLO");
            world.Dungeons.Reroll(portal, 2);
        }
        Tick(10);
        Check(Use(world, a, peerA, portal.Id, 30) is { ReturnCode: 0 }, "into the level-2 portal");
        var group = world.Dungeons.Groups.Single(g => g.Exit == portal);
        var (b, peerB, _) = Join(world, hero.Id);
        var floor = world.Dungeons.Floor(group.Ids[0])!;
        var floorMap = world.Maps.Find(floor.Id)!;
        var mobs = floorMap.Objects.Values.OfType<Mob>().ToList();
        Check(group.Level == 2 && floorMap.Difficulty?.MobModifier == "MOB_RANDOM_DUNGEON_BUFF_02" && mobs.Count > 0
              && mobs.All(m => m.LevelBuff?.Name == "MOB_RANDOM_DUNGEON_BUFF_02" && m.Effects.Spells.Any(s => s.Spell.Name == "MOB_RANDOM_DUNGEON_BUFF_02")
                  && Math.Abs(m.Health.Max - Math.Round(m.Template.HitPointsMax * 1.36)) <= 1 && m.Health.At(world.GameNow) >= m.Health.Max - 0.5f),
              $"level 2: every one of the floor's {mobs.Count} mobs has MOB_RANDOM_DUNGEON_BUFF_02 and 1.36 x its health, full");
        Check(floorMap.Objects.Values.OfType<LootChest>().Count() == floor.Template.LootChests.Count(p => p.Behaviour is not null),
              $"a chest on each bound chest point of the floor ({floor.Template.LootChests.Count})");

        // GM chests next to the player: NewLootChest; opened with Rates:Loot 0 (empty) and 20 (items), then registered to as a bag.
        var me = b.Player!;
        LootChest Chest(float dx)
        {
            lock (world.Sync) return world.Dungeons.SpawnChest(floorMap, "HERETIC_SOLO_LOOTCHEST_STANDARD", me.PositionAt(DateTime.UtcNow) + new Vector2(dx, 0), 90);
        }
        var poor = Chest(1);
        var sent = peerB.Events.LastOrDefault(e => Code(e) == (int)EventCodes.NewLootChest)?.Parameters;
        Check(sent is not null && Convert.ToInt64(sent[0]) == poor.Id && sent[3] is "HERETIC_SOLO_CHEST_STANDARD" && sent[5] is (byte)6 && sent[23] is (byte)6
              && sent[4] is string tile && tile == poor.Template.TileFor("Forest", "GREEN") && tile.Length > 0 && sent[18] is "HERETIC_SOLO_LOOTCHEST_STANDARD"
              && sent[21] is 0 && sent.ContainsKey(6) && sent.ContainsKey(7) && !sent.ContainsKey(24) && sent[13] is long.MinValue && sent[2] is float r && Math.Abs(r - 90) < 0.01f,
              $"NewLootChest {{0 id, 2 rotation, 3 the LootChest, 4 its Forest/green tile, 5/23 Unlocked, 6/7 stamps, 13 MinValue, 18 the spawnpoint, 21 0 shown}} (got {string.Join(",", sent?.Keys.Order() ?? Enumerable.Empty<byte>())})");
        OperationResponse? Open(LootChest chest, int requestId)
        {
            var count = peerB.Responses.Count;
            lock (world.Sync) b.OnRequest(Request(new UseLootChestRequest { wf = world.GameNow, aif = chest.Id }, requestId), encrypted: false);
            return peerB.Responses.Count > count ? peerB.Responses[^1] : null;
        }
        world.Rates = world.Rates with { Loot = 0 };
        Check(Open(poor, 40) is { ReturnCode: 0 } && poor is { Rolled: true, LockState: 8, Items.Count: 0 }
              && peerB.Events.Any(e => Code(e) == (int)EventCodes.LootChestOpened && Convert.ToInt64(e.Parameters[0]) == poor.Id)
              && peerB.Events.Any(e => Code(e) == (int)EventCodes.UpdateLootChest && Convert.ToInt64(e.Parameters[0]) == poor.Id && e.Parameters[1] is (byte)8)
              && Open(poor, 39) is { ReturnCode: 1 },
              "UseLootChest with Rates:Loot 0: rc 0, LootChestOpened, rolled empty: UpdateLootChest {0, 1 Empty}, not opened again");
        var update = peerB.Events.Last(e => Code(e) == (int)EventCodes.UpdateLootChest && Convert.ToInt64(e.Parameters[0]) == poor.Id).Parameters;
        Check(Enumerable.Range(0, 20).All(k => update.ContainsKey((byte)k)) && update[13] is 0 && update[14] is false && update[16] is (byte)8
              && update[18] is (short)0 && update[19] is (byte)0 && update[8] is long opened && opened != long.MinValue,
              $"UpdateLootChest is the full state (stageH bosses SPEC §3.2): keys 0-19, 13 rarity 0, 14 not personal, 16 Empty, 8 the opening (got {string.Join(",", update.Keys.Order())})");
        world.Rates = world.Rates with { Loot = 20 };
        var rich = Chest(-1);
        Check(Open(rich, 41) is { ReturnCode: 0 } && rich.Items.Count > 0 && Open(rich, 42) is { ReturnCode: 0 } && rich.Rolled,
              $"with Rates:Loot 20 the same chest kind holds items ({rich.Items.Count}); a second use rolls nothing new");
        lock (world.Sync) b.OnRequest(Request(new RegisterToObjectRequest { zg = rich.Id }, null), encrypted: false);
        Check(peerB.Events.Any(e => Code(e) == (int)EventCodes.AttachItemContainer && Convert.ToInt64(e.Parameters[0]) == rich.Id),
              "RegisterToObject on the chest: AttachItemContainer as for a loot bag");
        world.Rates = world.Rates with { Loot = 1 };
        ChestMessages(store, me);

        // The second map inside the dungeon: no marker here, 14965 and nothing used.
        var map2 = me.Inventory.Bag.Items.First(i => i.Template.Name == token);
        lock (world.Sync) b.OnRequest(Request(new UseConsumableFromInventoryRequest { ah5 = map2.Id, ah6 = 1 }, 50), encrypted: false);
        Check(peerB.Responses[^1].ReturnCode == Consumables.NoDungeonFound && map2.Count == 1, "a dungeon map inside a dungeon: 14965 @RANDOMDUNGEONMAP_NO_DUNGEON_FOUND, not used");

        // A death inside: the respawn pushes the player out, in front of the portal.
        bool respawned;
        lock (world.Sync)
        {
            Knockdown.Start(me, me.Id, "test", world.GameNow, KnockdownKind.Dead);
            respawned = Death.Respawn(me, "test");
        }
        Check(respawned && peerB.Responses[^1] is { ReturnCode: 0 } push && push.Parameters[0] is "0204" && !push.Parameters.ContainsKey(ReservedKeys.RequestId),
              "dead inside: the respawn's push {0: \"0204\"} without 255");
        var (_, _, jc) = Join(world, hero.Id);
        Check(jc.a8i == "0204" && Near(jc.a8j, portal.ArrivalOut("0204").Position), $"respawned in front of the portal (got {jc.a8i} {jc.a8j})");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// StageH PvE (PVE-DESIGN.md §3 step 10): the settings' formulas, then a level-0 solo portal with SOLO scaled and its faction the dungeon
    /// of the day: the floor's mobs × 0.55, its factors 1.5, the chat line after the floor's Join; a GM boss chest next to a GM miniboss is BossLocked
    /// (NewLootChest key 5 = 0, not to be opened) and unlocks (UpdateLootChest 6) at the miniboss's death, a later miniboss no guard of it; one guarded by that unlocks when it is taken off.
    /// </summary>
    private static void Pve()
    {
        var d = DungeonSettings.Default;
        var at = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        Check(Math.Abs(d.GroupFactor("VETERAN", 1) - 0.55) < 1e-9 && Math.Abs(d.GroupFactor("elite", 3) - 0.85) < 1e-9 && d.GroupFactor("VETERAN", 6) == 1
              && d.GroupFactor("SOLO", 1) == 1 && (d with { GroupScaling = false }).GroupFactor("VETERAN", 1) == 1
              && d.DailyFaction(at) != d.DailyFaction(at.AddSeconds(-1)) && d.DailyFaction(at) == d.DailyFaction(at.AddHours(23.9))
              && Enumerable.Range(0, 4).Select(i => d.DailyFaction(at.AddDays(i))).Distinct().Count() == 4 && (d with { DailyBonus = 1 }).DailyFaction(at) is null
              && RandomDungeons.IsBossChest("HERETIC_SOLO_LOOTCHEST_BOSS_RARE") && !RandomDungeons.IsBossChest("HERETIC_SOLO_LOOTCHEST_RARE"),
              "Dungeons: group factor 0.55/0.85/1 (veteran, elite; solo and off 1); the dungeon of the day turns at 10:00 UTC through the 4 factions; BOSS spawnpoints");

        var store = Day15;
        _ = store.Get("0204");
        var marker = store.Get("0204").DungeonMarkers.First(m => Near(m.Position, new Vector2(90, -260), 0.001f));
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        // DailyFactions = [the portal generator's faction]: its dungeon is the dungeon of the day whatever the date (HERETIC first; the world is
        // made again with the drawn one, the same draw with the same MapSeed).
        World MakeWorld(string f) => new(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 11,
            DungeonSettings = DungeonSettings.Default with { Floors = 1, GroupScaledSubtypes = new HashSet<string> { "SOLO" }, DailyFactions = [f] },
        };
        var world = MakeWorld("HERETIC");
        var hero = NewHero(characters, "0204", new Vector2(91, -259));
        var (a, peerA, _) = Join(world, hero.Id);
        var map = world.Maps.Find("0204")!;
        RandomDungeonExit portal;
        lock (world.Sync)
        {
            portal = world.Dungeons.SpawnOnMarker(map, marker, "T4_PORTAL_ROYAL_SOLO");
            world.Dungeons.Reroll(portal, 0);
        }
        var faction = store.Dungeons.Generators[portal.Generator].Faction;
        if (faction != "HERETIC")
        {
            world.Dispose();
            world = MakeWorld(faction);
            (a, peerA, _) = Join(world, hero.Id);
            map = world.Maps.Find("0204")!;
            lock (world.Sync)
            {
                portal = world.Dungeons.SpawnOnMarker(map, marker, "T4_PORTAL_ROYAL_SOLO");
                world.Dungeons.Reroll(portal, 0);
            }
        }
        clock.Advance(TimeSpan.FromSeconds(10));
        lock (world.Sync) world.Update();
        var mark = peerA.Events.Count;
        Check(Use(world, a, peerA, portal.Id, 30) is { ReturnCode: 0 }, "into the level-0 portal");
        var group = world.Dungeons.Groups.Single(g => g.Exit == portal);
        var early = peerA.Events.Skip(mark).Any(e => Code(e) == (int)EventCodes.SystemMessage);
        var (b, peerB, _) = Join(world, hero.Id);
        var line = peerB.Events.FirstOrDefault(e => Code(e) == (int)EventCodes.SystemMessage)?.Parameters;
        var floorMap = world.Maps.Find(group.Ids[0])!;
        var mobs = floorMap.Objects.Values.OfType<Mob>().ToList();
        Check(group is { Players: 1, Bonus: 1.5 } && floorMap.Difficulty is { MobModifier: null, Silver: 1.5, Loot: 1.5, MobFame: 1.5 } && mobs.Count > 0
              && mobs.All(m => Math.Abs(m.HealthFactor - 0.55) < 1e-9 && Math.Abs(m.DamageFactor - 0.55) < 1e-9
                  && Math.Abs(m.Health.Max - SpellEffects.MobMaxHealth(m, world.GameNow)) < 1 && m.Health.Max < m.Template.HitPointsMax)
              && !early && line?[0] is string text && text.Contains($"{RandomDungeons.DailyName(faction)} dungeons give x1.5", StringComparison.Ordinal),
              $"a {faction} dungeon of the day for 1: Bonus 1.5, the floor's factors 1.5, its {mobs.Count} mobs x0.55 health and damage, the chat line after the floor's Join");

        // A GM miniboss 2 m ahead (the floor's own bosses taken off first), then a boss chest 1 m ahead: BossLocked until the miniboss dies.
        var me = b.Player!;
        var pos = me.PositionAt(DateTime.UtcNow);
        var miniboss = store.Mobs.All.FirstOrDefault(m => m.Category == "miniboss" && m.Tier == 4 && m.Faction == faction) ?? store.Mobs.All.First(m => m.Category == "miniboss");
        Mob boss;
        LootChest chest;
        lock (world.Sync)
        {
            foreach (var m in mobs.Where(m => m.Template.Category is "boss" or "miniboss")) SpawnManager.Despawn(m, world, respawn: false);
            boss = SpawnManager.SpawnAt(floorMap, miniboss, pos + new Vector2(2, 0), 0, world);
            chest = world.Dungeons.SpawnChest(floorMap, $"{faction}_SOLO_LOOTCHEST_BOSS_RARE", pos + new Vector2(1, 0), 90);
        }
        var sent = peerB.Events.LastOrDefault(e => Code(e) == (int)EventCodes.NewLootChest && Convert.ToInt64(e.Parameters[0]) == chest.Id)?.Parameters;
        var count = peerB.Responses.Count;
        lock (world.Sync) b.OnRequest(Request(new UseLootChestRequest { wf = world.GameNow, aif = chest.Id }, 40), encrypted: false);
        Check(chest.LockState == RandomDungeons.BossLocked && sent?[5] is (byte)0 && peerB.Responses.Count > count && peerB.Responses[^1].ReturnCode == WorldSession.ChestLocked && !chest.Rolled,
              $"{chest.SpawnpointName} next to {miniboss.Name}: NewLootChest key 5 BossLocked (0), UseLootChest refused (rc 10126, the client's locked message)");
        // A second miniboss after the chest's check (as a respawn would come) is no guard of it; a second chest then has it as its guard
        // until it is taken off (.mob kill's SpawnManager.Despawn).
        mark = peerB.Events.Count;
        Mob extra;
        LootChest second;
        lock (world.Sync)
        {
            extra = SpawnManager.SpawnAt(floorMap, miniboss, pos + new Vector2(3, 0), 0, world);
            Combat.Damage(me, boss, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
            second = world.Dungeons.SpawnChest(floorMap, $"{faction}_SOLO_LOOTCHEST_BOSS_RARE", pos + new Vector2(-1, 0), 90);
        }
        var held = second.LockState == RandomDungeons.BossLocked;
        lock (world.Sync) SpawnManager.Despawn(extra, world, respawn: false);
        Check(boss.Dead && chest.LockState == 6 && peerB.Events.Skip(mark).Any(e => Code(e) == (int)EventCodes.UpdateLootChest && Convert.ToInt64(e.Parameters[0]) == chest.Id
                  && e.Parameters[1] is (byte)6) && held && second.LockState == 6,
              "the miniboss dead: UpdateLootChest {0 the chest, 1 Unlocked} with a later miniboss standing; a second chest locked by it until it is taken off");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// stageH bosses SPEC §8.5-8.7 and 8.13 (the 0204 capture's key sets): NewLootChest of a locked POI chest (STATIC_UNDEAD_CHAMPION on
    /// STATIC_UNDEAD_VETERAN_POI, protected, with a MinimapDisplay) and of a personal camp chest locked by its quest for
    /// <paramref name="viewer"/>, both with the rarity hidden (−1); the personal chest's UpdateLootChest per viewer; key 4 for every
    /// lootchests.xml chest but the eight legendary book chests (neither tiles nor prefab).
    /// </summary>
    private static void ChestMessages(ClusterStore store, Player viewer)
    {
        var data = store.LootChests;
        var swamp = store.Get("0204").Name;
        LootChest Make(string chest, string spawnpoint, byte state, Vector2 at, float dir, bool personal) =>
            new(1000 + (int)state, new LootChestSpawn
            {
                Key = new SpawnKey("0204", "test-chest", state), Tile = "", Position = at, Direction = dir,
                Component = new XElement("lootchestspawnpoint", new XAttribute("spawnbehaviour", spawnpoint)), SpawnBehaviour = spawnpoint,
            }, data.Chests[chest], data.Chests[chest].States[0], data.Chests[chest].TileFor(swamp.Biome, swamp.TemplateFolder), state, 12345)
            { Personal = personal, MinimapDisplay = !personal, ProtectedByMobs = !personal };
        static Dictionary<byte, object?> Wire(AlbionEvent e) => AlbionSerializer.BuildEvent(e).Parameters;
        static string Keys(Dictionary<byte, object?> p) => string.Join(",", p.Keys.Where(k => k < 250).Order());

        var poi = Wire(Make("STATIC_UNDEAD_CHAMPION", "STATIC_UNDEAD_VETERAN_POI", 2, new Vector2(-90, -141), 249.2f, false).BuildCreate(viewer).Single());
        Check(Keys(poi) == "0,1,2,3,4,5,6,7,8,10,13,16,17,18,19,21,23,26,28,29,31" && poi[4] is "LOOTCHEST_REGULAR_02" && poi[5] is (byte)2 && poi[23] is (byte)2
              && poi[18] is "STATIC_UNDEAD_VETERAN_POI" && poi[21] is -1 && poi[13] is long.MinValue && poi[16] is 10000L,
              $"NewLootChest of the locked 0204 POI chest: the captured key set, 4 LOOTCHEST_REGULAR_02, 5/23 2, 21 -1 (got {Keys(poi)})");
        var camp = Make("UNDEAD_DYNAMIC_CAMP_PERSONAL_SMALL_LC", "UNDEAD_DYNAMIC_CAMP_PERSONAL_SMALL", 4, new Vector2(2.5f, -227.5f), 134, true);
        var own = Wire(camp.BuildCreate(viewer).Single());
        Check(Keys(own) == "0,1,2,3,4,5,6,7,15,17,18,21,22,23,24,29,31" && own[4] is "SWAMP_GREEN_LOOTCHEST_DYNAMIC_CAMP_UNDEAD_SMALL" && own[22] is true
              && own[23] is (byte)4 && own[21] is -1,
              $"NewLootChest of the personal camp chest, quest not done: the captured key set, 4 SWAMP_GREEN_…_UNDEAD_SMALL, 22 true, 23 4, 21 -1 (got {Keys(own)})");
        var locked = Wire(camp.Update(viewer));
        camp.SetViewerState(viewer, 6, 23456);
        var unlocked = Wire(camp.Update(viewer));
        Check(Keys(locked) == "0,1,2,3,4,5,6,7,8,9,10,12,13,14,15,16,17,18,19" && locked[13] is -1 && locked[14] is true && locked[16] is (byte)4
              && unlocked[16] is (byte)6 && unlocked[17] is 23456L && unlocked[13] is 0 && unlocked[1] is (byte)4 && camp.MayOpen(viewer),
              $"UpdateLootChest of the personal chest: keys 0-10, 12-19, 13 -1 and 16 4 while locked; 16 6, 17 the unlock, 13 its rarity once the viewer's quest is done (got {Keys(locked)})");
        camp.SetState(8, 34567); // what an empty roll leaves on the chest (Roll is internal)
        var c = StarterKit.Default.NewCharacter("Second");
        var other = new Player(viewer.Session, viewer.World, 2, c, new Inventory(c.EquipmentContainer, c.InventoryContainer), null);
        var second = Wire(camp.Update(other));
        Check(Wire(camp.BuildCreate(other).Single())[23] is (byte)4 && second[16] is (byte)4 && second[17] is 12345L && !camp.MayOpen(other) && camp.MayOpen(viewer),
              "a personal chest opened (and emptied) by one viewer: a second one still sees 23 / 393 key 16 = 4 and may not open it (SPEC §8.10)");
        var tileless = data.Chests.Values.Where(c => c.TileFor("Forest", "GREEN").Length == 0).Select(c => c.Name).ToList();
        Check(tileless.Count == 8 && tileless.All(n => n.EndsWith("_BOOKCHEST_LEGENDARY", StringComparison.Ordinal)),
              $"key 4 for every lootchests.xml chest on Forest/green but the 8 legendary book chests (got {tileless.Count})");
    }

    private static void Walk()
    {
        var store = Day15;
        _ = store.Get("0204");
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 7, DungeonSettings = DungeonSettings.Default with { Floors = 2 },
        };
        var hero = NewHero(characters, "0204", new Vector2(70, -240));
        var account = characters.Account.Id;
        var loginPeer = new FakePeer();
        var login = new LoginSession(loginPeer, world.Config, characters, StarterKit.Default) { World = world };
        lock (world.Sync)
        {
            login.OnRequest(Request(TestLogin(), 1), encrypted: true);
            login.OnRequest(Request(new SelectCharacterRequest { abc = hero.Name }, null), encrypted: true);
        }
        var (a, peerA, _) = Join(world, account);
        var map = world.Maps.Find("0204")!;
        var marker = store.Get("0204").DungeonMarkers.First(m => Near(m.Position, new Vector2(90, -260), 0.001f));
        RandomDungeonExit portal;
        lock (world.Sync) portal = world.Dungeons.SpawnOnMarker(map, marker, "T4_PORTAL_ROYAL_SOLO");

        // In: outside the square 1640; inside before the 10 s cooldown 1647; then the floor is made and the answer follows.
        Check(Refused(Use(world, a, peerA, portal.Id, 10), 1640, 10), "20 m off the portal: 1640");
        Move(world, a, new Vector2(91, -259));
        Check(Refused(Use(world, a, peerA, portal.Id, 11), 1647, 11), "in the square right after the Join: 1647 (10 s in a WRL cluster)");
        clock.Advance(TimeSpan.FromSeconds(10));
        var resp = Use(world, a, peerA, portal.Id, 12);
        var group = world.Dungeons.Groups.Single();
        var id1 = group.Ids[0];
        Check(resp is { ReturnCode: 0 } && resp.Parameters.Keys.Order().SequenceEqual(new byte[] { 0, 3, ReservedKeys.OperationCode, ReservedKeys.RequestId })
              && resp.Parameters[0] is string key0 && key0 == id1 && resp.Parameters[3] is byte[] k3 && DungeonLayout.Read(k3) is { Floor: 1, Floors: 2 } l1 && l1.Id == id1
              && l1.UpCluster == "0204" && l1.DownCluster == group.Ids[1] && l1.DownName == group.NameOf(1) && l1.DownExit == DungeonExits.Up && resp.Parameters[ReservedKeys.RequestId] is 12
              && a.Status == SessionStatus.Transferring && group.Key.StartsWith("player:", StringComparison.Ordinal) && portal.Frozen && portal.LastUse is not null,
              $"inside after the cooldown: RC 0 with exactly {{0: id1, 3: layout, 253, 255}}, floor 1 of 2 leading down to floor 2 (got {string.Join(",", resp?.Parameters.Keys.Order() ?? Enumerable.Empty<byte>())})");
        Check(world.Saves.Load(hero.Id) is { } saved && saved.Cluster == id1 && saved.DungeonReturn is { Cluster: "0204" } ret && Near(new Vector2(ret.X, ret.Y), portal.ArrivalOut("0204").Position),
              "saved in the dungeon with the way back to 0204");
        lock (world.Sync) login.OnRequest(Request(new GetGameServerByClusterRequest { mt = id1 }, 4), encrypted: true);
        Check(loginPeer.Responses[^1] is { ReturnCode: 0 } gs && gs.Parameters[0] is "127.0.0.1:5056", "op 17 for the instance id: this game server");

        var floor1 = world.Dungeons.Floor(id1)!;
        var (b, peerB, jb) = Join(world, account);
        var up1 = floor1.Template.Exits[0];
        List<NewExitEvent> NewExits(FakePeer peer) => [.. peer.Events.Where(e => Code(e) == (int)EventCodes.NewExit).Select(e =>
        {
            var m = new NewExitEvent();
            AlbionSerializer.FillFields(m, e.Parameters);
            return m;
        })];
        var newExits = NewExits(peerB);
        Check(jb.a8i == id1 && Near(jb.a8j, up1.SpawnPosition!.Value) && Math.Abs(jb.a8k - Transfers.Facing(up1.Position, up1.SpawnPosition!.Value)) < 0.01f && jb.a94 == "0204",
              $"Join: key 8 = id1, key 9 the …0001 exit's spawnpos, 10 its facing, 66 = 0204 (got {jb.a8i} {jb.a8j})");
        var response = peerB.Sequence.FindIndex(x => x is OperationResponse);
        Check(peerB.Sequence.Take(response).OfType<EventData>().Any(e => Code(e) == (int)EventCodes.NewExit) && newExits.Count >= 1
              && newExits.All(e => e.ExitGuid == DungeonExits.Up ? Near(e.Position, up1.Position, 0.001f) : e.ExitGuid == DungeonExits.Down && Near(e.Position, floor1.Template.Exits[1].Position, 0.001f))
              && newExits.Count(e => e.ExitGuid == DungeonExits.Up) == 1 && newExits.Count(e => e.ExitGuid == DungeonExits.Down) == 1,
              $"phase A: NewExit key 1 = …0001 and …0002 at their exit tiles, whatever the distance (got {newExits.Count})");
        Check(world.Maps.Find(id1) is { Template: not null } m1 && world.Dungeons.IsLive(id1) && floor1.Template.MobsBound
              && floor1.Template.MobSpawnPoints.Any(p => p.Block is not null || p.Template is not null) && m1.Objects.Values.OfType<Mob>().Any(),
              $"the floor map is keyed by the instance id and has the mobs of its active layers ({floor1.Template.Summary()})");

        // 1647 two seconds after the Join; then floor 2 through …0002 and back up through floor 2's …0001.
        var down = FloorExit(world, id1, DungeonExits.Down);
        Move(world, b, down.Position);
        Check(NewExits(peerB).Count(e => e.ExitGuid == DungeonExits.Down) == 1, "near the far end: …0002 still known, not sent again");
        clock.Advance(TimeSpan.FromSeconds(2));
        Check(Refused(Use(world, b, peerB, down.Id, 20), 1647, 20), "an exit 2 s after the Join: 1647");
        clock.Advance(TimeSpan.FromSeconds(10));
        resp = Use(world, b, peerB, down.Id, 21);
        var id2 = group.Ids[1];
        Check(resp is { ReturnCode: 0 } && resp.Parameters[0] is string to2 && to2 == id2 && resp.Parameters[3] is byte[] k3b && DungeonLayout.Read(k3b) is { Floor: 2 } l2
              && l2.UpCluster == id1 && l2.UpName == floor1.Layout.Name && l2.UpExit == DungeonExits.Down && l2.DownCluster == "0204"
              && floor1.Layout.DownCluster == id2,
              "…0002 on floor 1: {0: id2, 3: layout2}, layout2 ab = id1, ac = name1, ad = …0002; floor 1's ae was id2 already");
        var floor2 = world.Dungeons.Floor(id2)!;
        var start2 = Store.Dungeons.Templates.First(i => i.Names.Any(n => n.Name == floor2.Layout.Templates[0].Template));
        Check(DungeonGenerator.UsageFits(start2.LevelUsage, 2, 2), $"floor 2's Start fits the last floor ({start2.LevelUsage})");
        var (c2, peerC2, jc2) = Join(world, account);
        Check(jc2.a8i == id2 && Near(jc2.a8j, floor2.Template.Exits[0].SpawnPosition!.Value), "on floor 2 at its …0001 exit");
        var up2 = FloorExit(world, id2, DungeonExits.Up);
        Move(world, c2, up2.Position);
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(Use(world, c2, peerC2, up2.Id, 30) is { ReturnCode: 0 } back && back.Parameters[0] is string bk && bk == id1 && back.Parameters.ContainsKey(3), "…0001 on floor 2: back to floor 1");
        var (d1, peerD1, jd1) = Join(world, account);
        Check(jd1.a8i == id1 && Near(jd1.a8j, floor1.Template.Exits[1].SpawnPosition!.Value), "floor 1 at its …0002 arrival");

        // Down again and the escape from floor 2: out to 0204 in front of the portal.
        var downAgain = FloorExit(world, id1, DungeonExits.Down);
        Move(world, d1, downAgain.Position);
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(Use(world, d1, peerD1, downAgain.Id, 40) is { ReturnCode: 0 }, "down again");
        var (e2, peerE2, _) = Join(world, account);
        clock.Advance(TimeSpan.FromSeconds(1));
        resp = Change(world, e2, peerE2, new ChangeClusterRequest { ee = 0, ef = null, eg = [-1], eh = true }, 41);
        Check(resp is { ReturnCode: 0 } && resp.Parameters[0] is "0204" && !resp.Parameters.ContainsKey(3), "the escape {0: 0, 1: null, 2: [-1], 3: true} on floor 2: {0: \"0204\"}");
        var (f0, peerF0, jf) = Join(world, account);
        var outside = portal.ArrivalOut("0204");
        Check(jf.a8i == "0204" && Near(jf.a8j, outside.Position) && Math.Abs(jf.a8k - outside.Direction) < 0.01f
              && (portal.Prefab != "SWAMP_GREEN_RANDOM_EXIT_10x10_PORTAL_SOLO_B" || Near(jf.a8j, new Vector2(92.9f, -261.75f))),
              $"back in 0204 at the marker + R(0)·spawnpos {portal.SpawnPos} ({portal.Prefab}) (got {jf.a8j})");

        // The way back in finds the same (per-player) dungeon; then out through floor 1's …0001.
        Move(world, f0, new Vector2(90, -260));
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(Use(world, f0, peerF0, portal.Id, 50) is { ReturnCode: 0 } again && again.Parameters[0] is string ag && ag == id1 && world.Dungeons.Groups.Count == 1,
              "entering the portal again: the same dungeon");
        var (g1, peerG1, _) = Join(world, account);
        var out1 = FloorExit(world, id1, DungeonExits.Up);
        Move(world, g1, out1.Position);
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(Use(world, g1, peerG1, out1.Id, 51) is { ReturnCode: 0 } o && o.Parameters[0] is "0204" && !o.Parameters.ContainsKey(3), "…0001 on floor 1: {0: \"0204\"}");
        var (h0, peerH0, _) = Join(world, account);

        // In once more and log out inside: SelectCharacter gives keys 1 = id1 and 4 = layout 1, the Join key 8 = id1.
        Move(world, h0, new Vector2(90, -260));
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(Use(world, h0, peerH0, portal.Id, 60) is { ReturnCode: 0 }, "in again");
        // A slow scene load: 45 s between the commit and the Join do not close the solo dungeon (30 s empty lifetime).
        lock (world.Sync) world.Update();
        clock.Advance(TimeSpan.FromSeconds(45));
        lock (world.Sync) world.Update();
        Check(world.Dungeons.IsLive(id1) && !group.Closed, "45 s loading before the Join: the dungeon stays (the arrival counts as inside)");
        clock.Advance(TimeSpan.FromSeconds(1)); // the next timer pass (Dungeons:UpdateSeconds) is due again
        var (i1, _, _) = Join(world, account);
        lock (world.Sync) i1.OnDisconnected("logout");
        lock (world.Sync) login.OnRequest(Request(new SelectCharacterRequest { abc = hero.Name }, null), encrypted: true);
        var select = Fill<SelectCharacterResponse>(loginPeer.Responses[^1]);
        Check(select.blo == id1 && select.blr is { } layoutBytes && layoutBytes.SequenceEqual(floor1.Bytes), "SelectCharacter inside: key 1 = id1, key 4 = floor 1's layout");
        var (j1, _, jj) = Join(world, account);
        Check(jj.a8i == id1, "and the Join key 8 = id1");
        lock (world.Sync) j1.OnDisconnected("logout");

        // Closed after 30 s empty (SOLO): the maps go, the next SelectCharacter takes the way back.
        lock (world.Sync) world.Update();
        clock.Advance(TimeSpan.FromSeconds(31));
        lock (world.Sync) world.Update();
        Check(!world.Dungeons.IsLive(id1) && world.Maps.Find(id1) is null && world.Maps.Find(id2) is null && world.Dungeons.Groups.Count == 0, "empty for 30 s: closed, its maps gone");
        lock (world.Sync) login.OnRequest(Request(new SelectCharacterRequest { abc = hero.Name }, null), encrypted: true);
        select = Fill<SelectCharacterResponse>(loginPeer.Responses[^1]);
        Check(select.blo == "0204" && select.blr is not { Length: > 0 }, "SelectCharacter for the closed id: the way back, 0204, no layout");
        var (k0, peerK0, jk) = Join(world, account);
        Check(jk.a8i == "0204" && Near(jk.a8j, portal.ArrivalOut("0204").Position), "joined in front of the portal");

        // A player inside at the max lifetime: the push {0: "0204"} without a request id.
        Move(world, k0, new Vector2(90, -260));
        clock.Advance(TimeSpan.FromSeconds(10));
        var portal2 = world.Dungeons.ExitsOf(world.Maps.Find("0204")!).Contains(portal) ? portal : null;
        lock (world.Sync) portal2 ??= world.Dungeons.SpawnOnMarker(world.Maps.Find("0204")!, marker, "T4_PORTAL_ROYAL_SOLO");
        Check(Use(world, k0, peerK0, portal2.Id, 70) is { ReturnCode: 0 }, "into a new dungeon");
        var (l1s, peerL1, _) = Join(world, account);
        clock.Advance(TimeSpan.FromSeconds(87000));
        lock (world.Sync) world.Update();
        Check(peerL1.Responses[^1] is { ReturnCode: 0 } push && push.Parameters[0] is "0204" && !push.Parameters.ContainsKey(ReservedKeys.RequestId)
              && l1s.Status == SessionStatus.Transferring && world.Dungeons.Groups.Count == 0,
              "at the max lifetime with a player inside: the push {0: \"0204\"} without 255, the dungeon closed");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// stageM exit (analysis/work/stageM/exit/NOTES.md): a PORTAL_SOLO prefab is clicked, so the client channels and sends ExitEnterStart
    /// (237) {0: time, 1: the exit} instead of ChangeCluster. On 0204: a corrupted portal 254; 9 m off 254; next to it 253 to a
    /// second player and no response (a response would end the client's bar at once); 238 during the channel 254 and no push; again, 2 s later 255, then the ChangeCluster push {0: id, 3: layout} without
    /// a request id, and the Join into floor 1.
    /// </summary>
    private static void Click()
    {
        var store = Day15;
        _ = store.Get("0204");
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 7, DungeonSettings = DungeonSettings.Default with { Floors = 1 },
        };
        var hero = NewHero(characters, "0204", new Vector2(99, -260));
        var other = NewHero(characters, "0204", new Vector2(94, -262));
        var (a, peerA, _) = Join(world, hero.Id);
        var (_, peerB, _) = Join(world, other.Id);
        var map = world.Maps.Find("0204")!;
        var marker = store.Get("0204").DungeonMarkers.First(m => Near(m.Position, new Vector2(90, -260), 0.001f));
        RandomDungeonExit portal;
        lock (world.Sync) portal = world.Dungeons.SpawnOnMarker(map, marker, "T4_PORTAL_ROYAL_SOLO");
        var corrupted = world.Dungeons.ExitsOf(map).First(e => e.Info.Corrupted);
        clock.Advance(TimeSpan.FromSeconds(10)); // past the exit cooldown after the Join
        List<EventData> Of(FakePeer on, EventCodes code, int from) => [.. on.Events.Skip(from).Where(e => Code(e) == (int)code)];
        OperationResponse? Op(AlbionLocal.Server.Protocol.AlbionRequest req, int id)
        {
            var count = peerA.Responses.Count;
            lock (world.Sync) a.OnRequest(Request(req, id), encrypted: false);
            return peerA.Responses.Count > count ? peerA.Responses[^1] : null;
        }
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }

        var mark = peerA.Events.Count;
        var r1 = Op(new ExitEnterStartRequest { wf = world.GameNow, kq = corrupted.Id }, 50);
        var r2 = Op(new ExitEnterStartRequest { wf = world.GameNow, kq = portal.Id }, 51);
        Check(r1 is { ReturnCode: 0 } && r2 is { ReturnCode: 0 } && Of(peerA, EventCodes.ExitEnterCancel, mark).Count == 2 && !world.Dungeons.Exiting(a.Player!),
              "237 on a corrupted portal, and on the solo portal from 9 m: RC 0 and 254 each, no channel");

        Move(world, a, new Vector2(91, -259));
        mark = peerA.Events.Count;
        var markB = peerB.Events.Count;
        var r3 = Op(new ExitEnterStartRequest { wf = world.GameNow, kq = portal.Id }, 52);
        var start = Of(peerB, EventCodes.ExitEnterStart, markB);
        Check(r3 is null && start is [var s] && Convert.ToInt64(s.Parameters[0]) == a.Player!.Id && Convert.ToInt64(s.Parameters[3]) == portal.Id
              && Of(peerA, EventCodes.ExitEnterCancel, mark).Count == 0 && world.Dungeons.Exiting(a.Player!),
              "237 next to the solo portal: no response (any 237 response ends the client's bar), 253 {player, start, end, exit} to the other player, the channel");
        var pushes = peerA.Responses.Count;
        Op(new ExitEnterCancelRequest(), 53);
        Tick(2.5);
        Check(Of(peerA, EventCodes.ExitEnterCancel, mark).Count == 1 && Of(peerB, EventCodes.ExitEnterCancel, markB).Count == 1 && peerA.Responses.Count == pushes + 1
              && a.Status == SessionStatus.InWorld, "238 during the channel: 254 to both, no push after 2 s");

        mark = peerA.Events.Count;
        Op(new ExitEnterStartRequest { wf = world.GameNow, kq = portal.Id }, 54);
        pushes = peerA.Responses.Count;
        Tick(1);
        Check(Of(peerA, EventCodes.ExitEnterFinished, mark).Count == 0 && peerA.Responses.Count == pushes, "1 s into the channel: nothing yet");
        Tick(1.1);
        for (var waited = 0; peerA.Responses.Count == pushes && waited < 30000; waited += 10)
        {
            Thread.Sleep(10);
            lock (world.Sync) world.Update();
        }
        var push = peerA.Responses.Count > pushes ? peerA.Responses[pushes] : null;
        var group = world.Dungeons.Groups.SingleOrDefault(g => g.Exit == portal);
        Check(Of(peerA, EventCodes.ExitEnterFinished, mark) is [var f] && Convert.ToInt64(f.Parameters[1]) == portal.Id && Of(peerA, EventCodes.ExitEnterCancel, mark).Count == 0
              && push is { ReturnCode: 0 } && !push.Parameters.ContainsKey(ReservedKeys.RequestId) && group is not null && push.Parameters[0] is string id && id == group.Ids[0]
              && push.Parameters[3] is byte[] && a.Status == SessionStatus.Transferring && portal.LastUse is not null,
              $"2 s later: 255 {{player, exit}}, then the ChangeCluster push {{0: floor 1, 3: layout}} without a request id (got {string.Join(",", push?.Parameters.Keys.Order() ?? Enumerable.Empty<byte>())})");
        var (_, _, jb) = Join(world, hero.Id);
        Check(jb.a8i == group!.Ids[0], $"the Join into floor 1 (got {jb.a8i})");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
