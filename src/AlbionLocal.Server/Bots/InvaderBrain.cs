using System.Numerics;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Bots;

/// <summary>
/// StageL corrupted (analysis/work/stageL/corrupted/SPEC.md §4.5): a corrupted dungeon's bot invader; stageL hellgates (hellgates SPEC §5.2):
/// a bot of a hellgate's enemy team. It hunts its target (<paramref name="targetOf"/>: the run's owner, or the nearest standing member of the
/// hellgate's party) over the whole map (no leash, it thinks seen or not): a path to where the target is, a new one when the target moved 8 m
/// from its aim; in its view it attacks (<see cref="BotCombat.Engage"/>, then <see cref="BotCombat.Think"/> fights with the bots'
/// auto-attacks). Hostile to its targets alone while their fight lasts (<see cref="BotPvp.Of"/>); after it, it stands until it goes
/// (<see cref="CorruptedDungeons"/>, <see cref="Hellgates"/>). Never a population slot, never refilled. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class InvaderBrain(BotManager bots, Func<Player, Player?> targetOf) : IBotBrain
{
    /// <summary>How far the target may move from the aim of the path under way before a new one (m).</summary>
    public const float ReplanDistance = 8;

    private Vector2? _aimed;
    private string _state = "waits";

    public string State => _state;

    public void Attacked(Player bot, Player by) => _aimed = null;

    public void Think(Player bot, TimeSpan now)
    {
        var info = bot.Bot!;
        if (info.Fight.Active && BotCombat.Think(bots, bot)) return;
        if (bot.Map is not { } map || bots.NavOf(map) is not { } nav || bot.Combat.Down is not null) return;
        if (targetOf(bot) is not { Combat.Down: null } target)
        {
            if (info.Motion.Busy) info.Motion.Stop(bot, null, bots);
            (_state, _aimed) = ("waits", null);
            return;
        }
        if (bot.Observers.Contains(target) && Pvp.Refusal(bot, target, bot.World.GameNow) is null)
        {
            _state = $"fights '{target.Character.Name}'";
            BotCombat.Engage(bots, bot, target);
            if (info.Fight.TargetId != 0) return;
        }
        var at = target.PositionAt(DateTime.UtcNow);
        if (info.Motion.Busy && _aimed is { } aimed && Vector2.Distance(aimed, at) < ReplanDistance) return;
        if (BotNav.PathFrom(bots, nav, bot.PositionAt(bots.Time), at) is not { } path) return;
        (_state, _aimed) = ($"hunts '{target.Character.Name}'", at);
        bots.Walk(bot, path, null);
    }
}
