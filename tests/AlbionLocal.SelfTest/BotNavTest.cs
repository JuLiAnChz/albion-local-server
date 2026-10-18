using System.Numerics;
using System.Text.Json;
using AlbionLocal.Server.Data;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Stage I bots, navigation (analysis/work/stageI/bots/SPEC.md §6 phase 1, tests 1.4 and 1.5): the walk grid of Thetford (0000) and
/// Birchcopse (1207) resolved without a layout session against the research's golden values (Fixtures/walkgrid_golden.json from
/// bots-world/golden.py: blocked and walkable cell counts within 0.5 %, 25 blocked and 25 reachable cells), the placed resource
/// prefabs blocking with the session, the lattice paths between every pair of 0000's exits and 1207's seven road routes.
/// </summary>
public static class BotNavTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("bot nav: 0000 and 1207 walk grids match the golden counts and cells; 1207's rocks block with the layout session", Golden),
        ("bot nav: lattice paths between every pair of 0000 exits stay walkable; 1207 has its 7 road routes on walkable ground", Paths),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    /// <summary>The cluster resolved without a layout session: default templates, no resource nodes (as golden.py); cached per id.</summary>
    private static ClusterTemplate Plain(string id) => PlainCache.GetOrAdd(id, i => Client.Clusters!.Resolve(i));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ClusterTemplate> PlainCache = new();

    private static void Golden()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "walkgrid_golden.json")));
        foreach (var id in new[] { "0000", "1207" })
        {
            var g = doc.RootElement.GetProperty(id);
            var grid = WalkGrid.Build(Plain(id));
            int blocked = g.GetProperty("blockedCells").GetInt32(), walkable = g.GetProperty("componentCells").GetInt32();
            Check(Math.Abs(grid.BlockedCells - blocked) <= blocked * 0.005 && Math.Abs(grid.ComponentCells - walkable) <= walkable * 0.005,
                  $"{id}: {grid.BlockedCells} blocked (golden {blocked}), {grid.ComponentCells} walkable (golden {walkable}), within 0.5 %");
            static (int, int) Cell(JsonElement c) => (c[0].GetInt32(), c[1].GetInt32());
            var notBlocked = g.GetProperty("blocked").EnumerateArray().Select(Cell).Where(c => !grid.Blocked(c.Item1, c.Item2)).ToList();
            var notWalkable = g.GetProperty("reachable").EnumerateArray().Select(Cell).Where(c => !grid.Walkable(c.Item1, c.Item2)).ToList();
            Check(notBlocked.Count == 0 && notWalkable.Count == 0, $"{id}: the 25 golden blocked cells blocked ({notBlocked.Count} not), the 25 reachable ones walkable ({notWalkable.Count} not)");
        }
        // With the day's layout the placed resource prefabs block too: every ROCK node's centre cell, and a walkable cell within 4 m of it.
        var live = Client.Clusters!.Get("1207");
        var withRocks = WalkGrid.Build(live);
        var rocks = live.Resources!.Harvestables.Where(h => h.Type.Name.StartsWith("ROCK", StringComparison.Ordinal)).ToList();
        Check(withRocks.BlockedCells > doc.RootElement.GetProperty("1207").GetProperty("blockedCells").GetInt32() && rocks.Count > 0
              && rocks.All(r => withRocks.Blocked((int)Math.Floor(r.Position.X), (int)Math.Floor(r.Position.Y)) && withRocks.Nearest(r.Position, 4) is not null),
              $"1207 with the layout: {withRocks.BlockedCells} blocked cells, every one of the {rocks.Count} rock nodes' centre cells blocked with a walkable cell within 4 m");
    }

    private static void Paths()
    {
        var thetford = ClusterNav.Build(Plain("0000"), 4, 20000);
        var exits = thetford.Exits.Select(e => e.Arrival).ToList();
        var bad = new List<string>();
        for (var i = 0; i < exits.Count; i++)
            for (var j = i + 1; j < exits.Count; j++)
            {
                var path = thetford.Roads.Path(exits[i], exits[j], 20000);
                var length = path is null ? 0 : path.Zip(path.Skip(1), Vector2.Distance).Sum();
                if (path is null || path[0] != exits[i] || path[^1] != exits[j] || !path.Zip(path.Skip(1)).All(l => Clear(thetford.Grid, l.First, l.Second))
                    || length > 2 * Vector2.Distance(exits[i], exits[j]) + 1)
                    bad.Add($"{i}-{j}");
            }
        Check(exits.Count >= 8 && bad.Count == 0, $"0000: {thetford.Summary()}; paths between the {exits.Count} exits, every leg walkable at 0.25 m, at most twice the straight line (bad: {string.Join(", ", bad)})");
        var birchcopse = ClusterNav.Build(Plain("1207"), 4, 20000);
        Check(birchcopse.Roads.Routes.Count == 7 && birchcopse.Roads.Routes.All(r => r.All(birchcopse.Grid.Walkable)),
              $"1207: {birchcopse.Summary()}; 7 routes, every point walkable");
    }

    /// <summary>Every 0.25 m sample of a → b in a walkable cell.</summary>
    internal static bool Clear(WalkGrid grid, Vector2 a, Vector2 b)
    {
        var n = Math.Max(1, (int)(Vector2.Distance(a, b) / 0.25f));
        for (var i = 0; i <= n; i++)
            if (!grid.Walkable(Vector2.Lerp(a, b, (float)i / n))) return false;
        return true;
    }
}
