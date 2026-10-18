using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace AlbionLocal.Server.Data;

/// <summary>
/// The offline golden checks of a resolved cluster (SPEC stage 2 §2 "Offline golden checks"; the data track verified
/// them on 0000, 0006, 0007, 1000, 1001, 1002 and 1207, which the self-test pins): world.xml lists, for each cluster,
/// what the client data must resolve to, so each check compares the resolver's output with world.xml.
/// <list type="bullet">
/// <item>critical: the active critical members are exactly the criticalObjects entries that are not unused (0000: 104
/// of 105, questgiverobject_02 is unused; <see cref="ClusterTemplate.CriticalObjects"/>), with the same type;</item>
/// <item>exits: every exit tile sits at its world.xml exit/@pos (|d| ≤ 0.001 per axis), and every world.xml exit has its tile;</item>
/// <item>distribution: static harvestables per (harvestables.xml resource, tier), plus the harvestable of every spawn
/// point's named mob (mobs.xml Loot/Harvestable: a rabbit carries HIDE T1), equal distribution/resource (0000: 271 WOOD
/// T1; 1207: 1572 WOOD, 1 ROCK and 231 HIDE). Silver nodes have no resource and are not counted;</item>
/// <item>mobcounts: the spawn points per named mob equal mobcounts (1000: 6 MOB_RABBIT; 1207: 231 MOB_RABBIT and 2
/// T6_MOB_GUARD_TOWER), and every name is a mobs.xml mob, except the placeholders <see cref="MobPlaceholders"/>, which
/// world.xml does not count either;</item>
/// <item>realestates: the real-estate members equal distribution@realestates (80 in 0000);</item>
/// <item>bounds: every object lies inside the cluster's origin and size;</item>
/// <item>resolve: nothing failed to resolve (<see cref="ClusterTemplate.Problems"/>).</item>
/// </list>
/// <see cref="Run"/> is the server's --check-content mode: every world.xml cluster, a summary, and an exit code.
/// </summary>
public static class ContentCheck
{
    public static readonly string[] Checks = ["load", "critical", "exits", "distribution", "mobcounts", "realestates", "bounds", "resolve"];

    /// <summary>
    /// Spawn-point mob names that are no mob (345319: 152,263 RANDOM, 22 WORLDBOSS): RANDOM, a spawn behaviour picks the
    /// mob; WORLDBOSS, the spawn point's worldboss attribute names a worldbosses.xml boss, which names the mob.
    /// </summary>
    public static readonly IReadOnlySet<string> MobPlaceholders = new HashSet<string>(StringComparer.Ordinal) { "RANDOM", "WORLDBOSS" };

    /// <summary>
    /// The failed checks of <paramref name="t"/>, one line each, prefixed with the check's name ("critical: …"); empty =
    /// all pass. <paramref name="mobs"/>: <see cref="Mobs"/>.
    /// </summary>
    public static IReadOnlyList<string> Check(ClusterTemplate t, HarvestableStore harvestables, MobFacts mobs)
    {
        var failures = new List<string>();
        static string First<T>(IEnumerable<T> items) => items.FirstOrDefault()?.ToString() ?? "";

        var expected = t.CriticalObjects.Values.Where(o => !o.Unused).ToDictionary(o => o.Path, StringComparer.Ordinal);
        var resolved = t.Critical.GroupBy(m => m.Path).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = expected.Keys.Where(p => !resolved.ContainsKey(p)).Order(StringComparer.Ordinal).ToList();
        var extra = resolved.Keys.Where(p => !expected.ContainsKey(p)).Order(StringComparer.Ordinal).ToList();
        var twice = resolved.Where(r => r.Value > 1).Select(r => r.Key).ToList();
        var typed = t.Critical.Where(m => expected.TryGetValue(m.Path, out var o) && o.Type != m.Type).Select(m => $"{m.Path} {m.Type} (world.xml {expected[m.Path].Type})").ToList();
        if (expected.Count == 0 && resolved.Count > 0)
            failures.Add($"critical: world.xml has no criticalObjects for the cluster; {resolved.Count} members resolved ({Kinds(t.Critical.Select(m => m.Type))})");
        else if (missing.Count + extra.Count + twice.Count + typed.Count > 0)
            failures.Add($"critical: {resolved.Count} resolved, {expected.Count} in world.xml; {missing.Count} not resolved ({Kinds(missing.Select(p => expected[p].Type))}; first {First(missing)}), "
                + $"{extra.Count} not in world.xml ({Kinds(t.Critical.Where(m => extra.Contains(m.Path)).Select(m => m.Type))}; first {First(extra)}), "
                + $"{twice.Count} twice, {typed.Count} of another type (first {First(typed)})");

        var offExits = t.Exits.Where(e => e.World is null || !Same(e.Position, e.World.Position)).ToList();
        var noTile = t.Info.Exits.Where(w => t.Exits.All(e => e.Guid != w.Id)).ToList();
        if (offExits.Count + noTile.Count > 0)
            failures.Add($"exits: {offExits.Count} of {t.Exits.Count} exit tiles not at their world.xml exit (first {First(offExits.Select(e => $"{e.Key} {e.Guid} at {P(e.Position)}, world.xml {(e.World is null ? "none" : P(e.World.Position))}"))}), "
                + $"{noTile.Count} of {t.Info.Exits.Count} world.xml exits without a tile (first {First(noTile.Select(w => $"{w.Id} at {P(w.Position)}"))})");

        var counted = new Dictionary<(string Resource, int Tier), int>();
        foreach (var h in t.Harvestables)
            if (h.Type.Resource is { } resource) Add(counted, (resource, h.Tier.Tier)); // silver nodes have none and are not counted
        foreach (var m in t.MobSpawnPoints)
            if (m.Mob is not null && mobs.Carried.TryGetValue(m.Mob, out var carried) && harvestables.Find(carried.Type)?.Resource is { } resource)
                Add(counted, (resource, carried.Tier));
        var distribution = new Dictionary<(string Resource, int Tier), int>();
        foreach (var r in t.Info.Distribution) Add(distribution, (r.Resource, r.Tier), r.Count);
        if (Differences(counted, distribution, k => $"{k.Resource} T{k.Tier}") is { Length: > 0 } byResource)
            failures.Add($"distribution: {byResource}");

        var spawns = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var m in t.MobSpawnPoints)
        {
            if (m.Mob is null || MobPlaceholders.Contains(m.Mob)) continue;
            Add(spawns, m.Mob);
            if (!mobs.Names.Contains(m.Mob)) unknown.Add(m.Mob);
        }
        var mobCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in t.Info.MobCounts) Add(mobCounts, m.Mob, m.Count);
        if (Differences(spawns, mobCounts, k => k) is { Length: > 0 } byMob) failures.Add($"mobcounts: {byMob}");
        if (unknown.Count > 0) failures.Add($"mobcounts: {unknown.Count} spawn point(s) name a mob mobs.xml does not have ({Kinds(unknown)})");

        if (t.RealEstates.Count != t.Info.RealEstates)
            failures.Add($"realestates: {t.RealEstates.Count} resolved, distribution@realestates {t.Info.RealEstates}");

        var outside = t.Objects.Concat(t.Critical).Where(o => o.Position.X < t.Origin.X || o.Position.Y < t.Origin.Y
            || o.Position.X > t.Origin.X + t.Size.X || o.Position.Y > t.Origin.Y + t.Size.Y).ToList();
        if (outside.Count > 0)
            failures.Add($"bounds: {outside.Count} object(s) outside origin {P(t.Origin)} size {P(t.Size)} (first {First(outside.Select(o => $"{o.GetType().Name} {o.Key} {o.Tile} at {P(o.Position)}"))})");

        if (t.Problems.Count > 0) failures.Add($"resolve: {string.Join("; ", t.Problems.Take(3))}{(t.Problems.Count > 3 ? $"; +{t.Problems.Count - 3} more" : "")}");
        return failures;
    }

    /// <summary>What the checks need of mobs.xml: the mob names, and the harvestable each mob carries.</summary>
    public sealed record MobFacts(IReadOnlySet<string> Names, IReadOnlyDictionary<string, (string Type, int Tier)> Carried);

    /// <summary>
    /// mobs.bin: the uniquename of every &lt;Mob&gt;, and the harvestable it carries (the first Harvestable in its element,
    /// e.g. MOB_RABBIT carries HIDE T1) where it has one.
    /// </summary>
    public static MobFacts Mobs(GameDataFile mobs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var carried = new Dictionary<string, (string Type, int Tier)>(StringComparer.Ordinal);
        foreach (var mob in mobs.Elements())
        {
            if (mob.Name.LocalName != "Mob" || (string?)mob.Attribute("uniquename") is not { } name) continue;
            names.Add(name);
            if (mob.Descendants("Harvestable").FirstOrDefault() is { } h && (string?)h.Attribute("type") is { } type)
                carried.TryAdd(name, (type, TileMath.Int((string?)h.Attribute("tier"))));
        }
        return new MobFacts(names, carried);
    }

    /// <summary>
    /// --check-content: resolves every world.xml cluster (uncached), runs <see cref="Check"/> on each and writes a
    /// summary to <paramref name="output"/>: load times and memory of the shared indexes, the world.bin values that did
    /// not parse (<see cref="WorldInfo.Problems"/>), per check the clusters passing and the first failing ones
    /// (<paramref name="all"/>: every failure, untrimmed; -v), the object totals. Returns 0 when world.bin read cleanly
    /// and every cluster passes every check, else 1.
    /// </summary>
    public static int Run(ClusterStore store, TextWriter output, bool all = false)
    {
        var total = Stopwatch.StartNew();
        string S(TimeSpan t) => t.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        output.WriteLine($"check-content: cluster data in {store.Folder}");
        var before = GC.GetTotalMemory(true);
        var world = store.World;
        var afterWorld = GC.GetTotalMemory(true);
        var tiles = store.Tiles;
        var afterTiles = GC.GetTotalMemory(true);
        output.WriteLine($"  world.bin: {world.Clusters.Count} clusters in {S(world.LoadTime)}, {Mb(afterWorld - before)} held"
            + (world.Problems.Count == 0 ? "" : $"; {world.Problems.Count} value(s) unreadable, read as empty or 0 (fail):"));
        foreach (var problem in all ? world.Problems : world.Problems.Take(8)) output.WriteLine($"      {problem}");
        if (world.Problems.Count > 8 && !all) output.WriteLine($"      ... {world.Problems.Count - 8} more");
        output.WriteLine($"  tiles.bin: {tiles.TileCount} tiles ({tiles.TilesWithComponents} with components, {tiles.TilesWithCollision} with walk-blocking cells), {tiles.CompoundCount} compounds "
            + $"({tiles.KeptChildren} of {tiles.CompoundChildren} children kept) in {S(tiles.LoadTime)}, {Mb(afterTiles - afterWorld)} held");
        var watch = Stopwatch.StartNew();
        var harvestables = store.Harvestables;
        var mobs = Mobs(GameDataFile.Open(store.Folder, "mobs.bin"));
        output.WriteLine($"  buildings.bin {store.Buildings.Count} buildings, harvestables.bin {harvestables.Count} types, mobs.bin {mobs.Names.Count} mobs "
            + $"({mobs.Carried.Count} carry a harvestable) in {S(watch.Elapsed)}");
        // The mobs' <AI> (stageD mobspells §4.1): every block compiles, and what the interpreter does not run yet, by count.
        var ai = store.Mobs.All.Where(m => m.AI is not null).Select(m => (Mob: m, Script: store.Mobs.Scripts.For(m))).ToList();
        var unknown = ai.Where(x => x.Script is not null).Select(x => x.Script!).Distinct().SelectMany(s => s.Unknown)
            .GroupBy(u => u.Key).Select(g => (Tag: g.Key, Count: g.Sum(u => u.Value))).OrderByDescending(u => u.Count).ToList();
        output.WriteLine($"  <AI>: {ai.Count} mobs, {ai.Count(x => x.Script is null)} unresolved; not run yet: "
            + (unknown.Count == 0 ? "none" : string.Join(", ", unknown.Select(u => $"{u.Tag} {u.Count}"))));
        var sharedFile = world.Clusters.GroupBy(c => c.File).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(c => (c.Id, Others: string.Join(" ", g.Where(o => o != c).Select(o => o.Id))))).ToDictionary(x => x.Id, x => x.Others);

        var failed = Checks.ToDictionary(c => c, _ => new List<(string Cluster, string Message)>());
        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int instances = 0, templates = 0;
        var slowest = (Cluster: "", Time: TimeSpan.Zero);
        var expeditionSpawns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // stageH: the bound mobs of each expedition cluster
        watch.Restart();
        foreach (var info in world.Clusters)
        {
            try
            {
                var t = store.Resolve(info.Id);
                if (t.LoadTime > slowest.Time) slowest = (info.Id, t.LoadTime);
                instances += t.Instances.Count;
                foreach (var o in t.Objects) totals[o.GetType().Name] = totals.GetValueOrDefault(o.GetType().Name) + 1;
                totals["CriticalMember"] = totals.GetValueOrDefault("CriticalMember") + t.Critical.Count;
                if (info.Type.Contains("EXPEDITION", StringComparison.Ordinal))
                    expeditionSpawns[info.Id] = t.MobSpawnPoints.Select(m => m.Template?.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
                foreach (var f in Check(t, harvestables, mobs))
                {
                    var check = f[..f.IndexOf(':')];
                    var message = f[(f.IndexOf(':') + 2)..];
                    if (check == "critical" && sharedFile.TryGetValue(info.Id, out var others)) message = $"[.cluster file shared with {others}] {message}";
                    failed[check].Add((info.Id, message));
                }
            }
            catch (InvalidDataException e)
            {
                failed["load"].Add((info.Id, e.Message));
            }
            // The parsed templates are the memory of this run: drop them now and then (the server keeps its few).
            if (store.TemplateCount >= 300)
            {
                templates += store.TemplateCount;
                store.ForgetTemplates();
            }
        }
        templates += store.TemplateCount;
        var resolveTime = watch.Elapsed;
        var clusters = world.Clusters.Count;
        output.WriteLine($"  resolved {clusters} clusters ({instances} template instances, {templates} templates parsed) in {S(resolveTime)}; "
            + $"slowest {slowest.Cluster} {S(slowest.Time)}");
        output.WriteLine($"  objects: {string.Join(", ", totals.Select(t => $"{t.Value} {t.Key}"))}");
        // StageH expeditions (SPEC §4.3): the playable ones (a world.xml cluster), the spawn aliases, and every task mob with a spawn point.
        try
        {
            var exp = store.Expeditions;
            var playable = exp.Expeditions.Where(e => ExpeditionData.Playable(e, id => world.Find(id) is not null)).ToList();
            var missing = playable.SelectMany(e => e.Steps.SelectMany(step => step).Where(task => task.Mob is { } m && !(expeditionSpawns.GetValueOrDefault(e.World)?.Contains(m) ?? false))
                .Select(task => $"{e.Name} {task.Mob}")).ToList();
            output.WriteLine($"  expeditions: {exp.Categories.Count} categories, {exp.Expeditions.Count} expeditions, {playable.Count} playable, "
                + $"{ExpeditionData.SpawnAliases.Count} spawn aliases, {missing.Count} task(s) without a spawn{(missing.Count > 0 ? ": " + string.Join(", ", missing.Take(8)) : "")}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException) { output.WriteLine($"  expeditions: not read ({e.Message})"); }
        var clean = world.Clusters.Count(c => failed.Values.All(list => list.All(f => f.Cluster != c.Id)));
        output.WriteLine($"  {clean} of {clusters} clusters pass every check");
        foreach (var (check, list) in failed)
        {
            output.WriteLine($"  {check,-12} {clusters - list.Select(f => f.Cluster).Distinct().Count(),5} pass, {list.Count,4} fail");
            foreach (var (cluster, message) in all ? list : list.Take(8)) output.WriteLine($"      {cluster}: {(all ? message : Trim(message))}");
            if (list.Count > 8 && !all) output.WriteLine($"      ... {list.Count - 8} more: {string.Join(" ", list.Skip(8).Take(40).Select(f => f.Cluster))}{(list.Count > 48 ? " ..." : "")}");
        }
        using var process = Process.GetCurrentProcess();
        output.WriteLine($"  done in {S(total.Elapsed)}, peak working set {Mb(process.PeakWorkingSet64)}");
        return clean == clusters && world.Problems.Count == 0 ? 0 : 1;
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "...";

    /// <summary>"exit x2, realestate" for a list of types.</summary>
    private static string Kinds(IEnumerable<string> types) =>
        string.Join(", ", types.GroupBy(k => k).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));

    private static bool Same(Vector2 a, Vector2 b) => Math.Abs(a.X - b.X) <= 0.001f && Math.Abs(a.Y - b.Y) <= 0.001f;

    private static string P(Vector2 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.###}, {v.Y:0.###})");

    private static void Add<TKey>(Dictionary<TKey, int> counts, TKey key, int n = 1) where TKey : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + n;

    /// <summary>"WOOD T1 resolved 270, world.xml 271; …" for every key whose counts differ; "" when none.</summary>
    private static string Differences<TKey>(Dictionary<TKey, int> resolved, Dictionary<TKey, int> expected, Func<TKey, string> name) where TKey : notnull =>
        string.Join("; ", resolved.Keys.Union(expected.Keys)
            .Where(k => resolved.GetValueOrDefault(k) != expected.GetValueOrDefault(k))
            .Select(k => $"{name(k)} resolved {resolved.GetValueOrDefault(k)}, world.xml {expected.GetValueOrDefault(k)}"));
}
