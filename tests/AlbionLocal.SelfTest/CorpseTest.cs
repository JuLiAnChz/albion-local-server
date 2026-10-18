using System.Numerics;
using AlbionLocal.Server;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The skinnable corpse of a killed hide mob (World/Corpses.cs; HARVEST.md §2 "Event 40", stage 5 SPEC §5c.1): a T1 swamp
/// toad killed in 0204 leaves a HIDE T1 corpse in the kill batch with the captured key layout, another player is refused
/// during the loot protection, the killer skins it with a T1 knife into its inventory, and it leaves at its decay. On the arena of <see cref="CombatTest"/> (no AI, a manual clock).
/// </summary>
public static class CorpseTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("corpse: a killed T1 toad leaves a HIDE corpse (event 40 keys as captured, in the kill batch), protected from others, skinned to 2 T1_HIDE, gone at its decay", Toad),
    ];

    private static void Toad()
    {
        Check(Client.GameData!.MobLootProtectionSeconds == 120 && Client.Clusters!.Mobs.Find("T1_MOB_HIDE_SWAMP_TOAD")?.Loot?.Element("Harvestable") is { } entry
              && (string?)entry.Attribute("type") == "HIDE" && (string?)entry.Attribute("tier") == "1",
              "gamedata <Loot mobprotection> 120 s; mobs.xml T1_MOB_HIDE_SWAMP_TOAD <Loot><Harvestable type=HIDE tier=1>");
        using var arena = new Arena();
        arena.Setup = c => c.Inventory.Add(new InventoryItem("T1_2H_TOOL_KNIFE"));
        var (session, peer, me) = arena.Join("Skinner", new InventoryItem("T4_MAIN_AXE"));
        var (otherSession, otherPeer, other) = arena.Join("Other", new InventoryItem("T4_MAIN_SWORD"));
        var toad = arena.Spawn("T1_MOB_HIDE_SWAMP_TOAD", new Vector2(1.5f, 0));
        arena.Run(0.1);
        var world = arena.World;
        var map = world.Maps.Find("0204")!;

        // 1. The kill (20 hp: one auto of the T4 axe). Event 40 in the batch, before the death clear and the lethal hit.
        var mark = peer.Events.Count;
        var otherMark = otherPeer.Events.Count;
        for (var swing = 0; swing < 5 && !toad.Dead; swing++)
        {
            Send(session, new AttackStartRequest { wf = world.GameNow, co = toad.Id, cp = (byte)(swing % 3) });
            arena.Run(1);
        }
        var batch = Since(peer, mark);
        var at = batch.FindIndex(e => e.Code == EventCodes.NewHarvestableObject);
        var clear = batch.FindIndex(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && L(e.P, 0) == toad.Id);
        var lethal = batch.FindIndex(e => e.Code == EventCodes.HealthUpdate && L(e.P, 0) == toad.Id && !e.P.ContainsKey(3));
        Check(toad.Dead && at >= 0 && clear > at && lethal > clear, $"the toad died; NewHarvestableObject before the death clear and the lethal hit (got {Show(batch)})");
        var ev = batch[at].P;
        var corpse = (Harvestable)map.Objects[L(ev, 0)];
        var keys = ev.Keys.Where(k => k < 250).ToHashSet();
        var hide = Client.Clusters!.Harvestables.Find("HIDE")!;
        Check(keys.IsSupersetOf([0, 1, 2, 3, 5, 6, 7, 8, 10, 11]) && keys.All(k => k is not 4 and not 12 and <= 12)
              && L(ev, 1) == toad.Id && L(ev, 2) == L(batch[lethal].P, 1) + 120 * TimeSpan.TicksPerSecond
              && ev[3] is byte[] { Length: 16 } owner && new Guid(owner) == me.Character.Id
              && Convert.ToByte(ev[5]) == hide.Id && Convert.ToInt16(ev[6]) == toad.Template.Id && Convert.ToByte(ev[7]) == 1
              && ev[8] is float[] pos && new Vector2(pos[0], pos[1]) == toad.Position && Convert.ToInt16(ev[10]) == 2 && Convert.ToByte(ev[11]) == 0
              && corpse is { Listed: false, Kind: "corpse", Charges: 2 },
              $"event 40 {{0 id, 1 the toad, 2 the kill + 120 s, 3 my Guid, 5 HIDE, 6 the toad's type, 7 T1, 8 the death position, 10 charges 2, 11 0}}, no 4/12 (got {Show([batch[at]])})");
        var decay = corpse.Corpse!.DecayAt;
        Check(Since(otherPeer, otherMark).Any(e => e.Code == EventCodes.NewHarvestableObject && L(e.P, 0) == corpse.Id)
              && decay > arena.Clock.Now + TimeSpan.FromSeconds(115) && decay <= arena.Clock.Now + TimeSpan.FromSeconds(120),
              "the other player in view gets the corpse too (normal visibility); it decays 120 s after the kill (HIDE T1 decaytimeseconds)");

        // 2. The other player is refused during the protection (cn2.a 7 → 2086), no HarvestStart event; anyone after it.
        var otherKnife = other.Inventory.Bag.Items.First(i => i.Template.Name == "T1_2H_TOOL_KNIFE");
        otherMark = otherPeer.Events.Count;
        var responses = otherPeer.Responses.Count;
        Send(otherSession, new HarvestStartRequest { Start = world.GameNow, HarvestableId = corpse.Id, ToolObjectId = otherKnife.Id });
        Check(otherPeer.Responses.Count == responses + 1 && otherPeer.Responses[^1].ReturnCode == (short)HarvestRefusal.Protected
              && !Since(otherPeer, otherMark).Any(e => e.Code == EventCodes.HarvestStart) && other.Harvest is null
              && corpse.Corpse!.MayHarvest(other, corpse.Corpse.ProtectedUntil) && !corpse.Corpse.MayHarvest(other, corpse.Corpse.ProtectedUntil - 1),
              "another player during the protection: rc 2086 (Protected), no event; free for anyone once key 2 is reached");

        // 3. The killer skins it: HIDE T1 with a T1 knife 1 s, both charges at once (maxchargesperharvest 2) → 2 T1_HIDE, no key 8.
        var knife = me.Inventory.Bag.Items.First(i => i.Template.Name == "T1_2H_TOOL_KNIFE");
        mark = peer.Events.Count;
        Send(session, new HarvestStartRequest { Start = world.GameNow, HarvestableId = corpse.Id, ToolObjectId = knife.Id });
        Check(peer.Responses[^1].ReturnCode == 0 && Since(peer, mark).FirstOrDefault(e => e.Code == EventCodes.HarvestStart).P is { } start && F(start, 5) == 1f,
              "the killer's HarvestStart is accepted: 1 s (HIDE T1 harvesttimeseconds 1, same tier)");
        arena.Run(1.1);
        var finish = Since(peer, mark).FirstOrDefault(e => e.Code == EventCodes.HarvestFinished).P;
        var hides = me.Inventory.Bag.Items.Where(i => i.Template.Name == "T1_HIDE").Sum(i => i.Count);
        Check(finish is not null && Convert.ToInt32(finish[4]) == Client.Items.Find("T1_HIDE")!.Index && Convert.ToInt16(finish[5]) == 2 && !finish.ContainsKey(8)
              && hides == 2 && corpse.Charges == 0 && corpse.RegrowAt is null && map.Objects.ContainsKey(corpse.Id),
              $"HarvestFinished {{4 T1_HIDE, 5: 2}} without key 8; 2 T1_HIDE in the inventory; the corpse stays, empty, and does not regrow (got {hides} hides)");

        // 4. The decay: HIDE T1 decaytimeseconds 120 from the kill, depleted or not, with a Leave to both players.
        mark = peer.Events.Count;
        otherMark = otherPeer.Events.Count;
        arena.Clock.Advance(decay - arena.Clock.Now - TimeSpan.FromSeconds(1));
        lock (world.Sync) world.Update();
        Check(map.Objects.ContainsKey(corpse.Id), "still there 1 s before its decay");
        arena.Clock.Advance(TimeSpan.FromSeconds(1));
        lock (world.Sync) world.Update();
        Check(!map.Objects.ContainsKey(corpse.Id)
              && Since(peer, mark).Any(e => e.Code == EventCodes.Leave && L(e.P, 0) == corpse.Id)
              && Since(otherPeer, otherMark).Any(e => e.Code == EventCodes.Leave && L(e.P, 0) == corpse.Id),
              "gone at its decay, with a Leave to both players");
    }
    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);
}
