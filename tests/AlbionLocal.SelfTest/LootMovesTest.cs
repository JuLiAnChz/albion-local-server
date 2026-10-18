using System.Numerics;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Taking single items out of loot bags (stageM loot-moves NOTES; the user's 2026-10-17 session: a shift+click or a drag out of the
/// loot window moved the icon but nothing, an axe dragged onto the mainhand could not cast): two bags in a row on 0204 with a
/// <see cref="ManualClock"/>, the client's InventoryMoveItem with the loot container as the source into a free slot, onto a stack and
/// onto the equipment (a two-hander replaced), the axe's spell change and cast afterwards, refusals put back, a split, a closed window,
/// take all; every item counted at the end, nothing duplicated.
/// </summary>
public static class LootMovesTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("loot moves: two bags in a row; shift+click into a free slot and onto a stack, a drag onto the mainhand (2H replaced, the axe's spells then cast), put-backs, a split, a closed window, take all; nothing duplicated", Moves),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private const string Ghoul = "T4_MOB_ROAMING_UNDEAD_GHOUL";

    private static readonly (string Item, int Amount)[] Drop = [("T4_MAIN_AXE", 1), ("T4_ORE", 4), ("T4_HIDE", 2), ("T4_OFF_SHIELD", 1)];

    private static void Moves()
    {
        using var arena = new Arena(mob => mob.Template.Name == Ghoul ? Drop : []);
        var world = arena.World;
        Check(Client.Items.Find("T4_2H_QUARTERSTAFF") is { TwoHanded: true }, "T4_2H_QUARTERSTAFF is a two-handed weapon");
        var (session, peer, me) = arena.Join("Taker", new InventoryItem("T4_2H_QUARTERSTAFF"));
        var inv = me.Inventory;
        var staff = inv.Equipment[0]!;
        int Owned(string name) => inv.Equipment.Items.Concat(inv.Bag.Items).Where(i => i.Template.Name == name).Sum(i => i.Count);
        var before = Drop.ToDictionary(d => d.Item, d => Owned(d.Item));
        var map = world.Maps.Find("0204")!;
        foreach (var offset in new[] { new Vector2(1.5f, 0), new Vector2(-1.5f, 0) })
        {
            var ghoul = arena.Spawn(Ghoul, offset);
            ghoul.Health.Set(1, world.GameNow);
            Send(session, new AttackStartRequest { wf = world.GameNow, co = ghoul.Id, cp = 0 });
            arena.Run(1.5);
        }
        var bags = map.Objects.Values.OfType<LootBag>().OrderBy(b => b.Id).ToList();
        Check(bags.Count == 2 && bags.All(b => b.Items.Count == Drop.Length), $"two ghouls, two bags of {Drop.Length} items (got {bags.Count})");
        var (bag1, bag2) = (bags[0], bags[^1]);
        Item In(LootBag b, string name) => b.Items.Items.Single(i => i.Template.Name == name);
        List<(EventCodes Code, Dictionary<byte, object?> P)> Move(Container from, Item item, Container to, int slot, long fromRev, long toRev)
        {
            var mark = peer.Events.Count;
            Send(session, new InventoryMoveItemRequest { ru = from.IndexOf(item), rv = from.Id, rw = fromRev, rx = slot, ry = to.Id, rz = toRev });
            return Since(peer, mark);
        }
        static bool Put(Dictionary<byte, object?> p, Item item, int slot, Container c, long? rev) =>
            L(p, 0) == item.Id && Convert.ToInt32(p.GetValueOrDefault((byte)1) ?? 0) == slot && new Guid((byte[])p[2]!) == c.Id
            && Convert.ToInt64(p.GetValueOrDefault((byte)3) ?? 0L) == (rev ?? 0); // a 0 is left out of the event
        var responses = peer.Responses.Count;

        // Bag 1. A shift+click (InventoryMoveItem loot → the first free slot): one put {id, slot, inventory, key 5}.
        Send(session, new RegisterToObjectRequest { zg = bag1.Id });
        var hide = In(bag1, "T4_HIDE");
        var free = inv.Bag.FreeSlot();
        var ev = Move(bag1.Items, hide, inv.Bag, free, 2, 5);
        Check(ev is [(EventCodes.InventoryPutItem, var put)] && Put(put, hide, free, inv.Bag, 5) && inv.Bag[free] == hide && !bag1.Items.Contains(hide) && bag1.Items.Count == 3,
              $"shift+click of the hide: InventoryPutItem {{id, {free}, inventory, 5}}, out of the bag (got {Show(ev)})");

        // A second take of the same window, onto the starter ore stack: the stack's new count, InventoryState of the loot {2} and the inventory {5}.
        var ore = In(bag1, "T4_ORE");
        var stack = inv.Bag.Items.First(i => i.Template.Name == "T4_ORE");
        var had = stack.Count;
        ev = Move(bag1.Items, ore, inv.Bag, inv.Bag.IndexOf(stack), 3, 6);
        Check(ev.Select(e => e.Code).SequenceEqual([EventCodes.NewSimpleItem, EventCodes.InventoryState, EventCodes.InventoryState])
              && L(ev[0].P, 0) == stack.Id && Convert.ToInt32(ev[0].P[2]) == had + 4 && L(ev[1].P, 3) == 3 && L(ev[2].P, 3) == 6 && stack.Count == had + 4
              && !bag1.Items.Contains(ore) && bag1.Items.Count == 2,
              $"after a first take, the ore onto the stack: {had} + 4, InventoryState of the loot (3) and the inventory (6) (got {Show(ev)})");

        // A drag of the axe onto the mainhand: it is equipped, the two-handed staff goes into the first free slot (no revision).
        var axe = In(bag1, "T4_MAIN_AXE");
        free = inv.Bag.FreeSlot();
        ev = Move(bag1.Items, axe, inv.Equipment, 0, 4, 2);
        var changed = ev.FirstOrDefault(e => e.Code == EventCodes.CharacterEquipmentChanged).P;
        Check(ev.Count >= 3 && ev[0].Code == EventCodes.InventoryPutItem && Put(ev[0].P, axe, 0, inv.Equipment, 2) && changed is not null
              && ev[^1].Code == EventCodes.InventoryPutItem && Put(ev[^1].P, staff, free, inv.Bag, null)
              && inv.Equipment[0] == axe && inv.Bag[free] == staff && inv.FindItem(axe.Id) == axe && !bag1.Items.Contains(axe)
              && changed[7] is int[] slots && axe.Active.Where(s => s >= 0).All(s => slots.Contains(s)),
              $"the axe dragged onto the mainhand: put {{axe, 0, equipment, 2}}, CharacterEquipmentChanged with its spells, put {{staff, {free}, inventory}} (got {Show(ev)})");
        var mark = peer.Events.Count;
        var spellSlot = Array.FindIndex(axe.Active, s => s >= 0);
        Send(session, new EquipmentItemChangeSpellRequest { kc = axe.Id, kd = true, ke = (byte)spellSlot, kf = axe.Active[spellSlot] });
        Check(Since(peer, mark).Any(e => e.Code == EventCodes.NewEquipmentItem && L(e.P, 0) == axe.Id), "its spell change finds the axe (NewEquipmentItem again; it was \"unknown item object\")");
        var target = arena.Spawn(Ghoul, new Vector2(0, 1.5f));
        arena.Run(0.1);
        Send(session, new CastStartRequest { wf = world.GameNow, dx = (byte)spellSlot, d0 = (short)axe.Active[spellSlot], d1 = target.Id, d4 = 1 });
        Check(peer.Responses.Count == responses, $"the axe's {Client.Spells!.Name(axe.Active[spellSlot])} in slot {spellSlot} casts (no 960 NotInSlot)");
        arena.Run(2);

        // Take all of the rest (InventoryMoveGivenItems): the shield; the bag is empty, Detach and Leave.
        var shield1 = In(bag1, "T4_OFF_SHIELD");
        mark = peer.Events.Count;
        Send(session, new InventoryMoveGivenItemsRequest { ro = bag1.Items.Id, rp = 5, rq = inv.Bag.Id, rr = 7, rs = [shield1.Id] });
        ev = Since(peer, mark);
        Check(ev.Any(e => e.Code == EventCodes.DetachItemContainer) && ev.Any(e => e.Code == EventCodes.Leave && L(e.P, 0) == bag1.Id) && inv.Bag.Contains(shield1)
              && !map.Objects.ContainsKey(bag1.Id) && me.OpenBag is null,
              $"take all: the shield into the inventory, DetachItemContainer, the bag's Leave (got {Show(ev)})");

        // Bag 2 (the second bag in a row). Refused moves put the item back {id, loot slot, loot Guid, key 2}: onto a taken inventory slot
        // (its item cannot go into the loot), into a slot it does not fit.
        Send(session, new RegisterToObjectRequest { zg = bag2.Id });
        var ore2 = In(bag2, "T4_ORE");
        var hide2 = In(bag2, "T4_HIDE");
        ev = Move(bag2.Items, hide2, inv.Bag, inv.Bag.IndexOf(staff), 2, 8);
        var ev2 = Move(bag2.Items, ore2, inv.Equipment, 2, 2, 3);
        Check(ev is [(EventCodes.InventoryPutItem, var back)] && Put(back, hide2, bag2.Items.IndexOf(hide2), bag2.Items, 2)
              && ev2 is [(EventCodes.InventoryPutItem, var back2)] && Put(back2, ore2, bag2.Items.IndexOf(ore2), bag2.Items, 2)
              && bag2.Items.Count == Drop.Length && inv.Bag.IndexOf(staff) >= 0,
              $"onto the staff's slot and ore onto the head: both put back into the loot, the bag unchanged (got {Show(ev)}; {Show(ev2)})");

        // The shield onto the empty offhand (next to the one-handed axe): equipped, nothing replaced.
        var shield2 = In(bag2, "T4_OFF_SHIELD");
        ev = Move(bag2.Items, shield2, inv.Equipment, 1, 2, 3);
        Check(ev.Count >= 2 && Put(ev[0].P, shield2, 1, inv.Equipment, 3) && ev.Any(e => e.Code == EventCodes.CharacterEquipmentChanged)
              && ev.Count(e => e.Code == EventCodes.InventoryPutItem) == 1 && inv.Equipment[1] == shield2,
              $"the shield dragged onto the offhand: put {{shield, 1, equipment, 3}}, CharacterEquipmentChanged (got {Show(ev)})");

        // A split drag of 2 of the 4 ore into a free slot: the loot stack's count, the new part's object and put {key 5}.
        free = inv.Bag.FreeSlot();
        mark = peer.Events.Count;
        Send(session, new InventorySplitStackIntoRequest { sh = bag2.Items.IndexOf(ore2), si = bag2.Items.Id, sj = inv.Bag.Id, sk = free, sl = 3, sm = 9, sn = 2 });
        ev = Since(peer, mark);
        var part = inv.Bag[free];
        Check(part is { Count: 2 } && part.Template == ore2.Template && part != ore2 && ore2.Count == 2 && bag2.Items.Contains(ore2)
              && ev.Any(e => e.Code == EventCodes.InventoryPutItem && Put(e.P, part, free, inv.Bag, 9)),
              $"a split of 2 ore out of the loot: 2 stay, 2 as a new object in slot {free} (got {Show(ev)})");

        // The window closed (UnRegisterFromObject): a move is put back; reopened, take all empties it.
        Send(session, new UnRegisterFromObjectRequest { ahc = bag2.Id });
        ev = Move(bag2.Items, hide2, inv.Bag, inv.Bag.FreeSlot(), 3, 10);
        Check(me.OpenBag is null && ev is [(EventCodes.InventoryPutItem, var closed), (EventCodes.InventoryState, var inBag)] && Put(closed, hide2, bag2.Items.IndexOf(hide2), bag2.Items, 3)
              && new Guid((byte[])inBag[2]!) == inv.Bag.Id && !inBag.ContainsKey(3) && bag2.Items.Contains(hide2),
              $"after UnRegisterFromObject nothing is taken: put back, and the inventory's state without a revision (the closed window may drop the put) (got {Show(ev)})");
        Send(session, new RegisterToObjectRequest { zg = bag2.Id });
        var rest = bag2.Items.ObjectIds().Where(id => id != 0).ToArray();
        Send(session, new InventoryMoveGivenItemsRequest { ro = bag2.Items.Id, rp = 4, rq = inv.Bag.Id, rr = 11, rs = rest });
        var gone = !map.Objects.ContainsKey(bag2.Id) && bag2.Items.Count == 0;
        // The same requests again find nothing to take.
        Send(session, new InventoryMoveGivenItemsRequest { ro = bag2.Items.Id, rp = 5, rq = inv.Bag.Id, rr = 12, rs = rest });
        mark = peer.Events.Count;
        Send(session, new InventoryMoveItemRequest { ru = 0, rv = bag2.Items.Id, rw = 6, rx = inv.Bag.FreeSlot(), ry = inv.Bag.Id, rz = 13 });
        ev = Since(peer, mark);
        Check(ev is [(EventCodes.InventoryState, var resync)] && new Guid((byte[])resync[2]!) == inv.Bag.Id && !resync.ContainsKey(3),
              $"a move out of the bag that is gone: the inventory's InventoryState without a revision, so the client's prediction goes back (got {Show(ev)})");
        var owned = Drop.ToDictionary(d => d.Item, d => Owned(d.Item));
        Check(gone && Drop.All(d => owned[d.Item] == before[d.Item] + 2 * d.Amount) && peer.Responses.Count == responses,
              $"take all empties bag 2; every item of both bags once in the inventory or equipment, none twice ({string.Join(", ", Drop.Select(d => $"{d.Item} {before[d.Item]} → {owned[d.Item]}"))})");
    }
}
