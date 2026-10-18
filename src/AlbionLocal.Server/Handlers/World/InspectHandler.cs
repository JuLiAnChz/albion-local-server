using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The inspect window of another player or a bot (stageM bots, analysis/work/stageM/bots/NOTES.md §3; no capture holds op 143, the layout
/// is the client's code). CharacterInspectGui.OnShow (0x18117feb0) → gmq.a3j (0x180cc2620) → c32.i (0x180a74600) asks
/// GetCharacterEquipment (op 143, td) {0 the target's character Guid (cpx+0x1F0, NewCharacter key 7), 1 true}. Its callback gmq.ba(g7q)
/// (0x180cc2aa0) keeps a response (ae5) only on rc 0 and only when key 0 is that Guid; any other rc calls OnRequestFailed. The window reads
/// key 1, one item <b>object</b> id per EquipmentSlot, through the client's item cache (gmq.a3g 0x180cc2380, gxj.a 0x180d7a8c0: an unknown
/// id hides the slot), key 2, one byte per slot = mastery level + 1 (gmq.a3h 0x180cc2520 and gxj.a_3 0x180d7ae20 read it through gtl.b.d
/// 0x180d2d830, value - 1; slots 0-4 with a mastery category show it as AlbionItemPowerMasteryProgress), and key 3, the item power it shows
/// (gmq.a3n 0x180cc2920). A second consumer: the duel invite window, DuelInviteGui.OnCharacterEquipmentReceived (0x181101810), asks op 143
/// for the challenger, shows its gear through gxj.a and its item power (key 3) on rc 0 without comparing the Guid, and on any other rc shows
/// GENERIC_ERROR_FAILED_RETRIEVING and closes the invite; so the character is looked up on the requester's map first, then in the world.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>The rc of an inspect whose character is in no map of the world (the client calls OnRequestFailed for any rc but 0).</summary>
    public const short InspectNotFound = 1;

    /// <summary>
    /// GetCharacterEquipment (op 143) {0 character Guid, 1 bool}: the player or bot of that character on the requester's map, else on any
    /// map (a duel invite; else rc <see cref="InspectNotFound"/>); each item it wears goes to the requester first as its own item object
    /// (NewEquipmentItem 30, which a remote player's NewCharacter never brings), then {0 the Guid, 1 long[10] item object id per
    /// EquipmentSlot (0 = empty), 2 byte[10] <see cref="MasteryLevel"/> + 1 for slots 0-4 (0 elsewhere), 3 the average item power of its
    /// gear (<see cref="AverageItemPower"/>)}.
    /// </summary>
    [Handles(OperationCodes.GetCharacterEquipment, Status = SessionStatus.InWorld)]
    private AlbionResponse? GetCharacterEquipment(GetCharacterEquipmentRequest req)
    {
        if (Player is not { Map: { } map } player) return null;
        var target = map.Characters.FirstOrDefault(p => p.Character.Id == req.md)
            ?? world.Maps.Maps.SelectMany(m => m.Characters).FirstOrDefault(p => p.Character.Id == req.md);
        if (target is null)
        {
            Log.Info(Name, $"'{player.Character.Name}' inspects character {req.md}: not in the world");
            return new GenericResponse { ReturnCode = InspectNotFound };
        }
        var equipment = target.Inventory.Equipment;
        var ids = new long[equipment.Size];
        var mastery = new byte[equipment.Size];
        for (var slot = 0; slot < equipment.Size; slot++)
        {
            if (equipment[slot] is not { } item) continue;
            if (target != player) SendItem(item); // its own are known to its client already
            ids[slot] = item.Id;
            if (slot < 5) mastery[slot] = (byte)Math.Min(byte.MaxValue, MasteryLevel(target, item) + 1);
        }
        var power = (float)AverageItemPower(target);
        Log.Info(Name, $"'{player.Character.Name}' inspects '{target.Character.Name}'{(target.IsBot ? " (bot)" : "")}: {equipment.Count} item(s), item power {power:0}");
        return new GetCharacterEquipmentResponse { asz = req.md, as0 = ids, as1 = mastery, as2 = power };
    }

    /// <summary>
    /// The mastery level the inspect window shows for <paramref name="item"/> worn by <paramref name="p"/>: the highest destiny level
    /// (capped at its base levels, as <see cref="Game.StatCalculator.DestinyBonus"/>) among the achievements whose wear bonus covers the
    /// item, from the same board <see cref="Systems.Combat.ItemPower"/> uses (a bot's: Bots:Gear:Mastery × the full board), so the item
    /// power in key 3 and the per-item mastery agree; 0 without game data or progression [infer: which of the covering achievements the
    /// official server reports is not known].
    /// </summary>
    public static int MasteryLevel(Player p, Item item)
    {
        if (p.World.GameData is not { } data || p.Progression?.Levels is not { } levels) return 0;
        var best = 0;
        foreach (var (achievement, _) in data.WearBonuses(item.Template.Index))
            if (levels.TryGetValue(achievement, out var level) && level > 0)
                best = Math.Max(best, Math.Min(level, data.Achievements[achievement].BaseLevels));
        return best;
    }

    /// <summary>
    /// The item power the inspect window shows: the average of <see cref="Systems.Combat.ItemPower"/> (quality and mastery included) over
    /// mainhand, offhand, head, armor, shoes and cape, a two-handed weapon counting twice and an empty slot as 0 (the client's own figure is
    /// not known exactly [infer]).
    /// </summary>
    internal static double AverageItemPower(Player p)
    {
        var e = p.Inventory.Equipment;
        double Ip(int slot) => e[slot] is { } item ? Systems.Combat.ItemPower(p, item) : 0;
        var off = e[1] is null && e[0]?.Template.Attrs.GetValueOrDefault("twohanded") == "true" ? Ip(0) : Ip(1);
        return (Ip(0) + off + Ip(2) + Ip(3) + Ip(4) + Ip(6)) / 6;
    }
}
