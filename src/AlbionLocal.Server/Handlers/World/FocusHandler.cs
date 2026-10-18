using AlbionLocal.Photon;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The use flags and the auto-learn choice (analysis/work/stageJ/focus-lp/SPEC.md D4, D6, §5.2): two requests the official server never
/// answers (no response in any capture; the client never waits for one).
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// ChangeUseFlag (op 298, qm {0 UseFlag, 1 bool}): the craft window's "use focus" (1 AutoUseActionFocusForCrafting), the destiny board's
    /// learning point toggle (0 AutoUseLearningPoints) or the respec silver one (2), sent debounced when the wanted state differs from the
    /// one last sent (ctp.af_43). The bit is set (key 1 left out = false), saved with the character and sent back in the next Join (keys 32,
    /// 99, 100). Another flag: logged, nothing changes. No response.
    /// </summary>
    [Handles(OperationCodes.ChangeUseFlag, Status = SessionStatus.InWorld)]
    private AlbionResponse? ChangeUseFlag(ChangeUseFlagRequest req)
    {
        if (Player is not { } player) return null;
        if (req.e7 is < 0 or > 2)
        {
            Log.Info(Name, $"'{player.Character.Name}' ChangeUseFlag {req.e7}: no such flag");
            return null;
        }
        var c = player.Character;
        c.UseFlags = req.e8 ? c.UseFlags | (1 << req.e7) : c.UseFlags & ~(1 << req.e7);
        world.Saves.MarkChanged(c);
        Log.Info(Name, $"'{c.Name}' use flag {(UseFlagName)req.e7} {(req.e8 ? "on" : "off")}");
        return null;
    }

    /// <summary>
    /// SetAchievementsAutoLearn (op 312, aa3 {0 int[] add, 1 int[] remove, 2 byte LearnGroupType mask, 3 byte[] per category, 4 int[][] per
    /// group}; stageH ops-audit SPEC §5): the auto-learn nodes become (old ∪ key 0) \ key 1, keys 2-4 as sent (3 and 4 kept when left out),
    /// saved; the next Join's FullAutoLearnAchievementInfo (155) and FullAchievementInfo key 5 show them. No response.
    /// </summary>
    [Handles(OperationCodes.SetAchievementsAutoLearn, Status = SessionStatus.InWorld)]
    private AlbionResponse? SetAchievementsAutoLearn(SetAchievementsAutoLearnRequest req)
    {
        if (Player is not { } player) return null;
        var c = player.Character;
        var old = c.AutoLearn;
        var ids = new SortedSet<int>(old?.Ids ?? []);
        ids.UnionWith(req.afq ?? []);
        ids.ExceptWith(req.afr ?? []);
        c.AutoLearn = new AutoLearnSet([.. ids], req.afs, req.aft ?? old?.Categories, req.afu ?? old?.PerGroup);
        world.Saves.MarkChanged(c);
        Log.Info(Name, $"'{c.Name}' auto-learn: {ids.Count} node(s){(ids.Count > 0 ? $" ({string.Join(", ", ids.Take(8))}{(ids.Count > 8 ? ", …" : "")})" : "")}");
        return null;
    }

    /// <summary>The client's UseFlag (qm.UseFlag), for the log.</summary>
    private enum UseFlagName { AutoUseLearningPoints, AutoUseActionFocusForCrafting, AutoUseSilverForReSpecPointGainOnMaxLevel }
}
