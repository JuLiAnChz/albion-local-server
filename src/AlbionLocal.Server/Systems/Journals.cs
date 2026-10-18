using System.Globalization;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// Labourer journals filling in the inventory (analysis/work/stageH/journals/SPEC.md §A.3, PVE-DESIGN.md step 4): every fame gain
/// (<see cref="Destiny.Award"/>, after its UpdateFame) goes into one journal of each kind whose mission it matches: gatherfame = a
/// harvest of one of its validitems, craftitemfame = a craft of one of its validitems of at least its mintier (the board's share,
/// destinyandjournalcraftfamefactor), killmobfame = a PvE kill of a mob of at least its mintier, fishingfame = a catch, fameearned =
/// any gain. The journal of a kind is the partly filled one, else one _EMPTY one (split off its stack into a free slot; none free:
/// JournalFillError 292, the kind is skipped); it takes gain × maxfame / value (<see cref="JournalData.Fill"/>) and its NewJournalItem
/// (35, key 8 the fame). Full: it becomes a _FULL item (onto a _FULL stack with room, else a new object in its slot: InventoryDeleteItem,
/// NewJournalItem, InventoryPutItem) and JournalGotFull (291) {0 the player, 1 the _FULL id, 2 0} shows the overhead text; the fame
/// over the maxfame is lost. Only the inventory (not the equipment, not a vault). content/journals.json Fill off = nothing.
/// Touched only under <see cref="World.Sync"/>.
/// </summary>
public static class Journals
{
    /// <summary>Whether a gain of <paramref name="ctx"/> (from <paramref name="source"/>: the harvested, caught or crafted item) fills a journal of <paramref name="j"/>.</summary>
    public static bool Fills(JournalData j, FameContext ctx, ItemTemplate? source, bool pvp) => j.Mission switch
    {
        "gatherfame" => ctx.Source == FameSource.Harvest && source is not null && (j.ValidItems.Count == 0 || j.ValidItems.Contains(source.BaseName)),
        "craftitemfame" => ctx.Source == FameSource.Craft && source is not null && source.Tier >= j.MinTier
            && (j.ValidItems.Count == 0 || j.ValidItems.Contains(source.BaseName)),
        "killmobfame" => ctx.Source == FameSource.Kill && !pvp && ctx.TargetTier >= j.MinTier,
        "fishingfame" => ctx.Source == FameSource.Fishing,
        "fameearned" => true,
        _ => false,
    };

    /// <summary>
    /// The journals <paramref name="gain"/> (a7m raw: UpdateFame key 2, a craft's board share) fills for <paramref name="p"/> (see the
    /// class); the items are saved when one changed.
    /// </summary>
    public static void OnFame(Player p, long gain, FameContext ctx, ItemTemplate? source, bool pvp)
    {
        if (gain <= 0 || !p.World.Journals.Fill) return;
        var bag = p.Inventory.Bag;
        var kinds = bag.Items.Select(i => i.Template).Where(t => t.Journal is { State: JournalState.Empty } j && Fills(j, ctx, source, pvp)).Distinct().ToList();
        if (kinds.Count == 0) return;
        foreach (var kind in kinds)
            Fill(p, kind, gain);
        p.SaveItems();
    }

    /// <summary>One kind's journal takes <paramref name="gain"/> (nothing changes when its stack has no free slot to split one off into).</summary>
    private static void Fill(Player p, ItemTemplate kind, long gain)
    {
        var bag = p.Inventory.Bag;
        var j = kind.Journal!;
        // A single empty one before a stack (no split needed); none (only ones at the maxfame, see MakeItem): the kind is skipped.
        var journal = bag.Items.FirstOrDefault(i => i.Template == kind && i.PartlyFilled)
            ?? bag.Items.Where(i => i.Template == kind && i.JournalFame == 0).OrderBy(i => i.Count > 1).FirstOrDefault();
        if (journal is null) return;
        if (journal.Count > 1)
        {
            // One off the stack of empty ones, into the lowest free slot.
            if (bag.FreeSlot() is var free && free < 0)
            {
                p.Session.Send(new JournalFillErrorEvent());
                return;
            }
            journal.Count--;
            p.Session.Send(Network.WorldSession.CountEvent(journal));
            journal = p.Session.NewItem(kind, 1);
            bag.Put(free, journal);
            p.Session.SendItem(journal);
            p.Session.Send(new InventoryPutItemEvent { ckw = journal.Id, ckx = free, cky = bag.Id });
        }
        journal.JournalFame = Math.Min(j.MaxFame, journal.JournalFame + j.Fill(gain));
        if (journal.JournalFame < j.MaxFame)
        {
            p.Session.Send(Network.WorldSession.CountEvent(journal));
            return;
        }
        // Full: the _FULL item instead (the client cannot change an object's type, cjb.bm), the rest of the gain is lost.
        var full = p.World.Items[j.Full]!;
        var slot = bag.IndexOf(journal);
        bag.Take(slot);
        p.Session.Send(new InventoryDeleteItemEvent { ckr = journal.Id });
        if (bag.Items.FirstOrDefault(i => i.Template == full && i.JournalFame == j.MaxFame && i.Count < Network.WorldSession.MaxStack(full)) is { } stack)
        {
            stack.Count++;
            p.Session.Send(Network.WorldSession.CountEvent(stack));
        }
        else
        {
            var item = p.Session.NewItem(full, 1);
            bag.Put(slot, item);
            p.Session.SendItem(item);
            p.Session.Send(new InventoryPutItemEvent { ckw = item.Id, ckx = slot, cky = bag.Id });
            p.World.Scripts.OnItemAdded(p, item);
        }
        p.Session.Send(new JournalGotFullEvent { cmj = p.Id, cmk = full.Index, cml = 0 });
        Log.Info("journals", string.Create(CultureInfo.InvariantCulture, $"'{p.Character.Name}': {full.Name} filled ({j.MaxFame / 10000.0:0.####} fame)"));
    }
}
