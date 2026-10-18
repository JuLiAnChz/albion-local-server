using System.Numerics;
using System.Text.Json;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Commands;
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
/// The resource nodes of stage 4b (analysis/work/stage4b/SPEC.md): the client's placement and dynamic-template roll as the
/// verified reference port computes them (Fixtures/stage4b_nodes.json, made from port/fixtures by placement.py; client
/// data only), and the nodes on a map: listed at full charges, an enchanted one as event 40, gathered, emptied and
/// regrown through the 4a path.
/// </summary>
public static class ResourceNodeTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("resource nodes: MT19937, the day seeds, the dyn rolls (1207 10-14 and 10-15, 0204 10-15) and the placement of 1207 (10-14) and 0204 (10-15) equal placement.py", Golden),
        ("resource nodes on the map: list 39 at full charges, event 40 when enchanted, harvested, emptied (stays, 0 charges) and regrown as 4a; .harvest info, .near", OnTheMap),
    ];

    private static ClusterStore Store => Client.Clusters!;

    /// <summary>The store's view for the Login key 57 <paramref name="key57"/> (no flags, feature 112 on), as a server started that day.</summary>
    internal static ClusterStore Day(long key57) => Store.With(Store.Options with { Session = new LayoutSession(key57, [], true) });

    private static void Golden()
    {
        var mt = new ClientRandom(5489);
        Check(mt.NextUInt() == 3499211612 && mt.NextUInt() == 581869302 && mt.NextUInt() == 3890346734, "a7u = MT19937: the reference vector of seed 5489");
        var cmp = ClientCompare.Instance;
        Check(cmp.Compare("A_B", "AB") < 0 && cmp.Compare("A_1", "A1") < 0 && cmp.Compare("TREE_10", "TREE_9") < 0 && cmp.Compare("a-bc", "abb") > 0 && cmp.Compare("a-b", "ab") < 0
              && cmp.Compare("abc", "ABD") < 0,
              "the culture order: '_' before digits and letters, letters without case, '-' ignored but for the ordinal tie break");

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "stage4b_nodes.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var (cluster, day, key57) = (c.GetProperty("cluster").GetString()!, c.GetProperty("day").GetString()!, c.GetProperty("key57").GetInt64());
            var name = $"{cluster} {day}";
            var session = new LayoutSession(key57, [], true);
            var t = Store.Resolve(cluster, session);
            var r = t.Resources!;
            Check(r.Session.Day == day && r.SeedText == c.GetProperty("seed_string").GetString() && r.Seed == c.GetProperty("seed").GetInt32()
                  && r.DynamicSeedText == c.GetProperty("dyn_seed_string").GetString() && DaySeed.Of(ClusterNameInfo.Parse(t.Info.File)!.ShortName, key57) == c.GetProperty("dyn_seed").GetInt32(),
                  $"{name}: seeds {r.SeedText} {r.Seed}, dyn {r.DynamicSeedText}");
            var rolls = string.Join(" ", r.Rolls.Values.OrderBy(x => x.Slot, StringComparer.Ordinal).Select(x => $"{x.Slot}={x.Template}"));
            var want = string.Join(" ", c.GetProperty("dyn").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"{p.Name}={p.Value.GetString()}"));
            Check(rolls == want && t.Instances.Where(i => i.Roll is not null).All(i => i.Reference == i.Roll!.Template && i.Default != i.Reference && i.Folder is not null),
                  $"{name}: dyn roll {want} (got {rolls}); the rolled slots load their template");
            var counts = string.Join(" ", r.Counts.Select(x => $"{x.Entry.NodeType}/{x.Entry.Resource}/T{x.Entry.Tier}={x.Count}"));
            var wantCounts = string.Join(" ", c.GetProperty("counts").EnumerateArray().Select(x => $"{x[0].GetInt32()}/{x[1].GetString()}/T{x[2].GetInt32()}={x[3].GetInt32()}"));
            Check(r.Invalid is null && r.Preset == c.GetProperty("preset").GetString() && r.Mapping == c.GetProperty("mapping").GetString() && counts == wantCounts,
                  $"{name}: preset, mapping and entry counts (got {r.Invalid ?? ""} {r.Preset}/{r.Mapping} {counts})");
            if (!c.TryGetProperty("harvestables", out var expected)) continue;
            var got = r.Harvestables;
            var exact = 0;
            var bad = new List<string>();
            var i = 0;
            foreach (var e in expected.EnumerateArray())
            {
                if (i >= got.Count) { bad.Add($"#{i} missing"); i++; continue; }
                var h = got[i];
                var at = new Vector2(e[2].GetSingle(), e[3].GetSingle());
                var d = Math.Max(Math.Abs(h.Position.X - at.X), Math.Abs(h.Position.Y - at.Y));
                if (h.Type.Id != e[0].GetInt32() || h.Tier.Tier != e[1].GetInt32() || h.Tile != e[4].GetString() || d >= MapSpawner.BindTolerance)
                    bad.Add($"#{i} {h.Type.Name} T{h.Tier.Tier} {h.Tile} at ({h.Position.X}, {h.Position.Y}), port {e[0]} T{e[1]} {e[4]} at ({at.X}, {at.Y})");
                else if (d == 0) exact++;
                i++;
            }
            Check(got.Count == expected.GetArrayLength() && bad.Count == 0 && r.Placements.Count == c.GetProperty("placements").GetInt32()
                  && got.All(h => h.Charges == h.Tier.Charges && h.Prefab is not null),
                  $"{name}: {got.Count} resource nodes (port {expected.GetArrayLength()}), {r.Placements.Count} placements; first differences: {string.Join(" | ", bad.Take(5))}");
            Check(exact >= got.Count - got.Count(h => h.Prefab!.Compound && h.Prefab.NodeDirection % 90 != 0), $"{name}: positions equal (float32) except compound children at other angles ({exact} of {got.Count})");
        }
        var thetford = Store.Resolve("0000", new LayoutSession(LoginWorld.Key57, [], true));
        Check(thetford.Resources is { Invalid: not null, Harvestables.Count: 0, Rolls.Count: 0 }, "a city has no preset: no resource nodes, no dyn slots");
    }

    private static void OnTheMap()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var store = Store; // the self-test's store: 2026-10-14
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData) { Sync = new object(), Clock = clock, Clusters = store };
        var layout = store.Get("1207").Resources!;
        Check(layout.Session.Day == "2026-10-14" && layout.Harvestables.Count == 1026, $"the self-test's store resolves 1207 for 2026-10-14 (got {layout.Session.Day}, {layout.Harvestables.Count} nodes)");
        // A T2 tree (3 charges) to harvest with the T3 axe, and a T4 node 200 m away to enchant before it comes into view.
        var fiber = layout.Harvestables.First(h => h is { Type.Name: "WOOD", Tier.Tier: 2 });
        var far = layout.Harvestables.First(h => h.Tier.Tier == 4 && h.Tier.RareItems.Count > 0 && Vector2.Distance(h.Position, fiber.Position) > 200);

        var c = StarterKit.Default.NewCharacter("Gatherer");
        c.Cluster = "1207";
        var stand = fiber.Position - new Vector2(2, 0);
        (c.PositionX, c.PositionY) = (stand.X, stand.Y);
        c.Equipment = [];
        c.Inventory = [new("T3_2H_TOOL_AXE")];
        Check(characters.Create(c), "character created");
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
        var player = session.Player!;
        var map = player.Map!;
        var nodes = map.Objects.Values.OfType<Harvestable>().Where(h => h.IsResourceNode).ToList();
        var node = nodes.Single(h => h.Spawn == fiber);
        Check(nodes.Count == 1026 && nodes.All(n => n.Charges == n.FullCharges) && node.Kind == "resource node"
              && player.Visibility.Known.Contains(node) && nodes.Where(n => n.RareState > 0).All(n => n.Tier >= 4),
              $"1207 got its 1026 resource nodes at full charges, enchanted ones T4 or higher only ({nodes.Count(n => n.RareState > 0)} enchanted)");
        var lists = peer.Events.Where(e => WorldObjectTest.Code(e) == (int)EventCodes.NewSimpleHarvestableObjectList).ToList();
        int Index(EventData e, long id) => Array.IndexOf((long[])e.Parameters[0]!, id);
        Check(lists.Any(e => Index(e, node.Id) is var k and >= 0 && ((short[])e.Parameters[4]!)[k] == 3 && ((byte[])e.Parameters[2]!)[k] == 2
                  && ((float[])e.Parameters[3]!)[2 * k] == fiber.Position.X && ((float[])e.Parameters[3]!)[2 * k + 1] == fiber.Position.Y),
              "the T2 tree is in a list 39 with type, tier 2, its prefab position and 3 charges (max Charge@level)");
        Check(nodes.Where(n => n.RareState > 0).All(n => !lists.Any(e => Index(e, n.Id) >= 0)), "no enchanted node in a list");

        // The far T4 node, enchanted while no client knows it, comes as event 40 when the player stands next to it.
        var rare = nodes.Single(h => h.Spawn == far);
        rare.RareState = 2;
        var mark = peer.Events.Count;
        var near = far.Position - new Vector2(2, 0);
        session.OnRequest(Request(new MoveRequest { wf = GameTime.Now, v6 = [near.X, near.Y], v8 = [near.X, near.Y], v9 = 0f }, null), encrypted: false);
        var ev40 = peer.Events.Skip(mark).Where(e => WorldObjectTest.Code(e) == (int)EventCodes.NewHarvestableObject).ToList();
        var w = new ByteWriter();
        var e40 = ev40.SingleOrDefault(e => Convert.ToInt64(e.Parameters[0]) == rare.Id);
        Check(e40 is not null, "the enchanted node comes as NewHarvestableObject 40");
        Protocol18.WriteEventData(w, e40!);
        var p = Protocol18.ReadEventData(new ByteReader(w.ToArray())).Parameters;
        Check(p.Keys.Where(k => k < 250).Order().SequenceEqual(rare.Direction == 0 ? new byte[] { 0, 2, 5, 6, 7, 8, 10, 11 } : [0, 2, 5, 6, 7, 8, 9, 10, 11])
              && Convert.ToByte(p[5]) == rare.Type.Id && Convert.ToInt16(p[6]) == -1 && Convert.ToByte(p[7]) == 4 && p[8] is float[] pos && pos[0] == far.Position.X && pos[1] == far.Position.Y
              && Convert.ToInt16(p[10]) == rare.FullCharges && Convert.ToByte(p[11]) == 2 && Convert.ToInt64(p[2]) == map.CreatedTicks
              && !peer.Events.Skip(mark).Any(e => WorldObjectTest.Code(e) == (int)EventCodes.NewSimpleHarvestableObjectList && Index(e, rare.Id) >= 0),
              $"event 40 keys 0, 2, 5-8, (9), 10, 11: type, -1, tier 4, the prefab position, full charges, rare state 2; not in a list (got {string.Join(", ", p.Select(x => $"{x.Key}={x.Value}"))})");
        var replies = new List<string>();
        void Command(string line) { replies.Clear(); world.Commands.Execute(new CommandContext(world, player, AccountLevel.GameMaster, (t, _) => replies.Add(t)), line); }
        Command(".harvest info");
        var info = string.Join("\n", replies);
        Check(info.StartsWith($"{rare.Type.Name} T4.2 ") && info.Contains("  resource node: tier 4, enchantment 2; prefab ") && info.Contains(rare.TierData.RareItems[2]),
              $".harvest info: the node's tier and enchantment, its prefab, the enchanted item (got {info})");
        Command(".near 3");
        Check(replies.Count == 1 && replies[0].Contains($"resource node {rare.Type.Name} T4.2 at "), $".near lists the resource nodes (got {string.Join(" / ", replies)})");

        // Back at the tree: three harvests with the T3 axe (2 s x 0.7 = 1.4 s each, the 4a rules); the node empties and stays.
        session.OnRequest(Request(new MoveRequest { wf = GameTime.Now, v6 = [stand.X, stand.Y], v8 = [stand.X, stand.Y], v9 = 0f }, null), encrypted: false);
        var axe = player.Inventory.Bag[0]!;
        var wood = 0;
        for (var n = 0; n < 3; n++)
        {
            wood += Harvesting.Amount(node.TierData, node.Charges);
            session.OnRequest(Request(new HarvestStartRequest { Start = GameTime.Now + TimeSpan.TicksPerSecond, HarvestableId = node.Id, ToolObjectId = axe.Id }, 90 + n), encrypted: false);
            Check(peer.Responses[^1].ReturnCode == 0 && player.Harvest is { Duration: 1.4f }, $"harvest {n + 1} of the tree accepted, 1.4 s");
            clock.Advance(TimeSpan.FromSeconds(1.5));
            lock (world.Sync) world.Update();
        }
        Check(node.Charges == 0 && map.Objects.ContainsKey(node.Id) && player.Visibility.Known.Contains(node) && node.RegrowAt is not null && wood > 0
              && player.Inventory.Bag.Items.Any(i => i.Template.Name == "T2_WOOD" && i.Count == wood),
              $"emptied: 0 charges, still on the map and known (no Leave), regrowing; {wood} T2_WOOD");
        session.OnRequest(Request(new HarvestStartRequest { Start = GameTime.Now, HarvestableId = node.Id, ToolObjectId = axe.Id }, 99), encrypted: false);
        Check(peer.Responses[^1].ReturnCode == (short)HarvestRefusal.Depleted, "an empty node: 2083");
        clock.Advance(TimeSpan.FromMinutes(30));
        lock (world.Sync) world.Update();
        Check(node.Charges == node.FullCharges && node.RegrowAt is null, "regrown to its 3 charges as a 4a node");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);
}
