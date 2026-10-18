using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server;

/// <summary>
/// The server side of the resource nodes (stage 4b, analysis/work/stage4b/SPEC.md §5): which ones are enchanted. The
/// nodes themselves are the client's own placement (<see cref="ResourceLayout"/>), spawned by <see cref="MapSpawner"/> as
/// <see cref="Harvestable"/>s and gathered, emptied and regrown as the static ones (World/Harvesting.cs). Their rare
/// (enchantment) state is the server's (SPEC §5.4): rolled per node of a tier that has RareState items (T4 and up) from
/// rareresourcedistribution.xml, the cluster's world.xml distribution (SAFE: T4 4504/400/80/16, else the tier-less entry),
/// when the map is made and again every checkintervall (3600 s) for the nodes that are full and that no client on the
/// map may still hold (<see cref="VisibilityTracker.Holds"/>: a list-39 node is forgotten without a Leave, so its id
/// stays with the client until its next Join), so a node never switches between list 39 and event 40 in a client's
/// view (how the official server does that was not captured). World:RareResources off: no node is enchanted. Touched only under <see cref="World.Sync"/>.
/// </summary>
public static class ResourceNodes
{
    /// <summary>The map's rare states at its creation: every resource node rolled once, the next roll one check interval later.</summary>
    public static void Start(Map map, ClusterTemplate template, ClusterStore? store, World world)
    {
        if (!world.Config.RareResources || store is null || map.ResourceNodes.Count == 0) return;
        RareResourceDistributions rare;
        try { rare = store.RareResources; }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Log.Warn("world", $"map {map.Cluster}: no enchanted resource nodes, rareresourcedistribution.bin: {e.Message}");
            return;
        }
        var distribution = template.Info.Attrs.GetValueOrDefault("rareresourcedistribution");
        map.RareWeights = tier => rare.Weights(distribution, tier);
        map.RareInterval = rare.CheckInterval;
        foreach (var node in map.ResourceNodes) node.RareState = Roll(map, node);
        map.NextRareRoll = world.Now + rare.CheckInterval;
    }

    /// <summary>
    /// The check interval's roll (the world tick, <see cref="Harvesting.Update"/>): every full resource node that no player
    /// on the map holds gets a new rare state; the others keep theirs until every client that was sent them has left the map.
    /// </summary>
    public static void Update(Map map, TimeSpan now)
    {
        if (map.NextRareRoll is not { } due || now < due || map.RareInterval is not { } interval) return;
        map.NextRareRoll = now + interval;
        var changed = 0;
        foreach (var node in map.ResourceNodes)
        {
            if (node.Charges < node.FullCharges || node.RegrowAt is not null || map.Players.Values.Any(p => p.Visibility.Holds(node))) continue;
            var state = Roll(map, node);
            if (state != node.RareState) changed++;
            node.RareState = state;
        }
        Log.Debug("world", $"map {map.Cluster}: rare states rolled again, {changed} resource node(s) changed");
    }

    /// <summary>A weighted rare state for <paramref name="node"/> (0 for a tier without RareState items or a state without its item).</summary>
    public static byte Roll(Map map, Harvestable node)
    {
        if (node.TierData.RareItems.Count == 0 || map.RareWeights?.Invoke(node.Tier) is not { Count: > 0 } weights) return 0;
        var total = weights.Sum(w => Math.Max(0, w.Weight));
        if (total <= 0) return 0;
        var r = map.Random.Next(total);
        foreach (var (state, weight) in weights)
        {
            if (weight <= 0) continue;
            if (r < weight) return state > 0 && node.TierData.RareItems.ContainsKey(state) ? state : (byte)0;
            r -= weight;
        }
        return 0;
    }
}
