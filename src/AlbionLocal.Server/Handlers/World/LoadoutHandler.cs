using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// StageN qol (analysis/work/stageN/qol/SPEC.md §2): loadouts (ops 425-431), the vault and market windows' saved builds. The client's buffer
/// (ctm, one per owner Guid) reads the headers, creates an empty loadout and fills it with an update; the server stores what it is sent
/// (<see cref="Loadout"/>, saved with the character) and gives it back unchanged. Only the character's own loadouts (owner = its id) are
/// made; another owner (the guild Guid of Join key 56) gets the official empty headers and AccessDenied on create. Equip moves the listed
/// items from the inventory and the bank tabs the client has open into the equipment and the inventory; nothing is ever created.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>RCs the client maps (ctm.c.e, ctm.b.e, ctm.a.d 0x180BF3DE0): create 17001 NoEmptySlots / 17002 AccessDenied, update 17122
    /// UpdateConflict, read 17041 (quietly dropped), -500 internal error.</summary>
    public const short LoadoutNoEmptySlots = 0x4269, LoadoutAccessDenied = 0x426A, LoadoutNotFound = 0x4291, LoadoutConflict = 0x42E2;

    /// <summary>Loadouts a character may keep (the client has its own limit, ctm.h; this only bounds the save).</summary>
    public const int MaxLoadouts = 100;

    /// <summary>The per-slot status bytes of LoadoutEquip key 1 (a8n): 0 nothing asked, 1 equipped, 4 kept as it was, 5 not found.</summary>
    public const byte EquipNone = 0, EquipDone = 1, EquipKept = 4, EquipMissing = 5;

    private List<Loadout>? Loadouts(Guid owner) => Player is { } p && owner == p.Character.Id ? p.Character.Loadouts : null;

    /// <summary>
    /// LoadoutReadHeaders (op 427, xk {0 owner Guid}): {0 owner, 2 ids, 3 positions, 4 names, 5 colours, 6 icons} by position (002835: the
    /// official answer for the character carried one loadout this way); none or another owner: {0 owner} only, as official.
    /// </summary>
    [Handles(OperationCodes.LoadoutReadHeaders, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutReadHeaders(LoadoutReadHeadersRequest req)
    {
        if (Loadouts(req.uc) is not { Count: > 0 } list) return new LoadoutReadHeadersResponse { bcm = req.uc };
        var sorted = list.OrderBy(l => l.Position).ToList();
        return new LoadoutReadHeadersResponse
        {
            bcm = req.uc, bco = [.. sorted.Select(l => l.Id)], bcp = [.. sorted.Select(l => l.Position)], bcq = [.. sorted.Select(l => l.Name)],
            bcr = [.. sorted.Select(l => l.Color)], bcs = [.. sorted.Select(l => l.Icon)],
        };
    }

    /// <summary>LoadoutCreate (op 425, xf: 0 owner, 1 name, 2 colour, 3 icon, 4 a8m source): an empty loadout; {0 its id, 1 true}. The client then reads it and sends its content.</summary>
    [Handles(OperationCodes.LoadoutCreate, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutCreate(LoadoutCreateRequest req)
    {
        if (Loadouts(req.tw) is not { } list) return new LoadoutCreateResponse { ReturnCode = LoadoutAccessDenied };
        if (list.Count >= MaxLoadouts) return new LoadoutCreateResponse { ReturnCode = LoadoutNoEmptySlots };
        var loadout = new Loadout
        {
            Id = Guid.NewGuid().ToString("N"), Owner = req.tw, Name = req.tx ?? "", Color = req.ty, Icon = req.tz,
            Position = list.Count == 0 ? 0 : list.Max(l => l.Position) + 1,
        };
        list.Add(loadout);
        world.Saves.MarkDirty(Player!.Character);
        Log.Info(Name, $"loadout '{loadout.Name}' created ({loadout.Id})");
        return new LoadoutCreateResponse { bce = loadout.Id, bcf = true };
    }

    /// <summary>LoadoutRead (op 426, xj: 0 owner, 1 id): the stored loadout (ahu, every key as it was sent); an unknown id: 17041.</summary>
    [Handles(OperationCodes.LoadoutRead, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutRead(LoadoutReadRequest req)
    {
        if (Loadouts(req.ua)?.FirstOrDefault(l => l.Id == req.ub) is not { } l) return new LoadoutReadResponse { ReturnCode = LoadoutNotFound };
        return new LoadoutReadResponse
        {
            bct = req.ua, bcu = l.Id, bcv = l.Name, bcw = l.Color, bcx = l.Icon, bcy = l.Version,
            bcz = l.Equipment, bc0 = l.Quality, bc1 = l.SlotStacks, bc2 = l.ActiveSpells, bc3 = l.PassiveSpells,
            bc4 = l.PreferenceA, bc5 = l.PreferenceB, bc6 = l.PreferenceC, bc7 = l.PotionFoodPreference,
            bc8 = l.Items, bc9 = l.ItemQuality, bda = l.ItemStacks,
        };
    }

    /// <summary>
    /// LoadoutUpdate (op 428, xl: 0 owner, 1 id, 2 version read, 3 name, 4 colour, 5 icon, 6-17 the content, c32.i(ctg) 0x180A6AF50): stored
    /// whole with the version raised; {0 id, 1 new version}. A stale version is taken as well (one client per character), only logged.
    /// </summary>
    [Handles(OperationCodes.LoadoutUpdate, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutUpdate(LoadoutUpdateRequest req)
    {
        if (Loadouts(req.ud) is not { } list) return new LoadoutUpdateResponse { ReturnCode = LoadoutConflict };
        var index = list.FindIndex(l => l.Id == req.ue);
        if (index < 0) return new LoadoutUpdateResponse { ReturnCode = LoadoutConflict };
        var old = list[index];
        if (req.uf != old.Version) Log.Info(Name, $"loadout {old.Id}: update of version {req.uf}, stored {old.Version} (taken)");
        list[index] = old with
        {
            Name = req.ug ?? old.Name, Color = req.uh, Icon = req.ui, Version = old.Version + 1,
            Equipment = req.uj ?? [], Quality = req.uk ?? [], SlotStacks = req.ul ?? [], ActiveSpells = req.um ?? [], PassiveSpells = req.un ?? [],
            PreferenceA = req.uo ?? [], PreferenceB = req.up ?? [], PreferenceC = req.uq ?? [], PotionFoodPreference = req.ur ?? [],
            Items = req.us ?? [], ItemQuality = req.ut ?? [], ItemStacks = req.uu ?? [],
        };
        world.Saves.MarkDirty(Player!.Character);
        return new LoadoutUpdateResponse { bdb = old.Id, bdc = old.Version + 1 };
    }

    /// <summary>LoadoutDelete (op 429, xg: 0 owner, 1 id): {0 owner, 1 id}, which the client drops from its list.</summary>
    [Handles(OperationCodes.LoadoutDelete, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutDelete(LoadoutDeleteRequest req)
    {
        if (Loadouts(req.t1) is { } list && list.RemoveAll(l => l.Id == req.t2) > 0) world.Saves.MarkDirty(Player!.Character);
        return new LoadoutDeleteResponse { bcg = req.t1, bch = req.t2 };
    }

    /// <summary>LoadoutOrderUpdate (op 430, xi: 0 owner, 1 ids, 2 their new positions, 3 int echoed): {0 owner, 1 the int}.</summary>
    [Handles(OperationCodes.LoadoutOrderUpdate, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutOrderUpdate(LoadoutOrderUpdateRequest req)
    {
        if (Loadouts(req.t6) is { } list && req.t7 is { } ids && req.t8 is { } positions)
        {
            for (var i = 0; i < ids.Length && i < positions.Length; i++)
            {
                var index = list.FindIndex(l => l.Id == ids[i]);
                if (index >= 0) list[index] = list[index] with { Position = positions[i] };
            }
            world.Saves.MarkDirty(Player!.Character);
        }
        return new LoadoutOrderUpdateResponse { bck = req.t6, bcl = req.t9 };
    }

    /// <summary>
    /// LoadoutEquip (op 431, xh: 0 owner, 1 a Guid of the local player (ctp+0x558), 2 id): {0 0, 1 a status per equipment slot}. Each slot the
    /// loadout names takes an item of that uniquename (quality by the slot's first preference, the closest first) from the inventory, else from
    /// an open bank tab; the item it replaces goes where that one came from. Missing ones stay missing (status 5), the slot keeps its item. Then
    /// the inventory part: what the bag lacks of each listed entry comes from the open bank tabs (whole stacks, a split for the rest), while
    /// the bag has room. Never in an archetype without equipment changes (corrupted dungeons) and never the mount slot while the mount is in use.
    /// </summary>
    [Handles(OperationCodes.LoadoutEquip, Status = SessionStatus.InWorld)]
    private AlbionResponse? LoadoutEquip(LoadoutEquipRequest req)
    {
        if (Player is not { } player || Loadouts(req.t3)?.FirstOrDefault(l => l.Id == req.t5) is not { } loadout)
            return new LoadoutEquipResponse { ReturnCode = LoadoutNotFound, bcj = new byte[Inventory.EquipmentSlots] };
        var status = new byte[Inventory.EquipmentSlots];
        var swapping = Systems.Pvp.RulesOf(world, player.Map).AllowEquipmentSwapping;
        var equipment = player.Inventory.Equipment;
        var bag = player.Inventory.Bag;
        var sources = new List<Container> { bag };
        sources.AddRange(player.OpenVaults.Values.Select(v => v.Container));
        var equipped = false;
        var mountMoved = false;
        for (var slot = 0; slot < status.Length; slot++)
        {
            if (slot >= loadout.Equipment.Length || loadout.Equipment[slot] is not { Length: > 0 } want) continue;
            var quality = slot < loadout.Quality.Length ? loadout.Quality[slot] : 0;
            var preference = slot < loadout.PreferenceA.Length ? loadout.PreferenceA[slot] : 2;
            if (equipment[slot] is { } worn && worn.Template.Name == want && QualityFits(worn.Quality, quality, preference)) { status[slot] = EquipKept; continue; }
            if (!swapping || slot == Systems.Mounts.MountSlot && player.Mount.State != MountState.None) { status[slot] = EquipKept; continue; }
            var found = sources.SelectMany(c => Enumerable.Range(0, c.Size).Select(s => (C: c, Slot: s, Item: c[s])))
                .Where(x => x.Item is { } i && i.Template.Name == want && QualityFits(i.Quality, quality, preference) && equipment.Fits(i, slot))
                .OrderBy(x => Math.Abs(x.Item!.Quality - quality)).ThenBy(x => sources.IndexOf(x.C)).FirstOrDefault();
            if (found.Item is not { } item) { status[slot] = EquipMissing; continue; }
            var displaced = equipment[slot];
            if (displaced is not null && !found.C.Fits(displaced, found.Slot)) { status[slot] = EquipMissing; continue; }
            equipment.Put(slot, item);
            found.C.Put(found.Slot, displaced);
            if (displaced is not null) world.Scripts.OnUnequip(player, displaced);
            world.Scripts.OnEquip(player, item);
            Systems.Consumables.Equipped(player, slot);
            SendEvent(new InventoryPutItemEvent { ckw = item.Id, ckx = slot, cky = equipment.Id });
            if (displaced is not null) SendEvent(new InventoryPutItemEvent { ckw = displaced.Id, ckx = found.Slot, cky = found.C.Id });
            status[slot] = EquipDone;
            equipped = true;
            mountMoved |= slot == Systems.Mounts.MountSlot;
        }
        if (equipped) SendEquipmentChanged(player, intoEquipment: true);
        if (mountMoved) Systems.Mounts.Unequipped(player);
        var fetched = FetchInventory(player, loadout);
        if (equipped || fetched > 0) player.SaveItems();
        Log.Info(Name, $"loadout '{loadout.Name}' equipped: slots [{string.Join(",", status)}], {fetched} stack(s) from the bank");
        return new LoadoutEquipResponse { bcj = status };
    }

    /// <summary>A quality <paramref name="have"/> against the loadout's <paramref name="want"/> under a LoadoutItemPreference (0 ≤, 1 =, 2 ≥).</summary>
    private static bool QualityFits(int have, int want, int preference) => want <= 0 || preference switch
    {
        0 => have <= want,
        1 => have == want,
        _ => have >= want,
    };

    /// <summary>The inventory part of a loadout: per entry, what the bag lacks comes out of the open bank tabs. Returns the stacks moved or split.</summary>
    private int FetchInventory(Player player, Loadout loadout)
    {
        var bag = player.Inventory.Bag;
        var vaults = player.OpenVaults.Values.Select(v => v.Container).ToList();
        if (vaults.Count == 0) return 0;
        var claimed = new Dictionary<string, int>(StringComparer.Ordinal);
        var moved = 0;
        for (var i = 0; i < loadout.Items.Length; i++)
        {
            if (loadout.Items[i] is not { Length: > 0 } name) continue;
            var quality = i < loadout.ItemQuality.Length ? loadout.ItemQuality[i] : 0;
            var need = Math.Max(1, i < loadout.ItemStacks.Length ? loadout.ItemStacks[i] : 1);
            bool Matches(Item it) => it.Template.Name == name && (quality <= 1 || !it.Template.IsEquipment || it.Quality == quality);
            var inBag = bag.Items.Where(Matches).Sum(it => it.Count) - claimed.GetValueOrDefault(name);
            var have = Math.Clamp(inBag, 0, need);
            claimed[name] = claimed.GetValueOrDefault(name) + have;
            need -= have;
            foreach (var vault in vaults)
                for (var s = 0; s < vault.Size && need > 0; s++)
                {
                    if (vault[s] is not { } stack || !Matches(stack)) continue;
                    var free = bag.FreeSlot();
                    if (free < 0) return moved;
                    if (stack.Count <= need)
                    {
                        vault.Take(s);
                        bag.Put(free, stack);
                        SendEvent(new InventoryPutItemEvent { ckw = stack.Id, ckx = free, cky = bag.Id });
                        need -= stack.Count;
                        claimed[name] += stack.Count;
                    }
                    else
                    {
                        stack.Count -= need;
                        SendCount(stack);
                        var part = CreateItem(stack.Template, (stack.Source ?? new InventoryItem(stack.Template.Name)) with { Count = need, Quality = stack.Quality },
                            SpellUsable(player.Progression));
                        bag.Put(free, part);
                        SendEvent(new InventoryPutItemEvent { ckw = part.Id, ckx = free, cky = bag.Id });
                        claimed[name] += need;
                        need = 0;
                    }
                    moved++;
                }
        }
        return moved;
    }
}
