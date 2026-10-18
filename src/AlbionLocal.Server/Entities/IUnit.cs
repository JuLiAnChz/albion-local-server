using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// What spells act on (ARCHITECTURE.md §4.1 Unit): a <see cref="Player"/> or a <see cref="Mob"/>, each with its list of
/// active spells (<see cref="Systems.EffectList"/>, stageD effects SPEC §1). The two have no common base class (a player is
/// no WorldObject), so the effect code reaches them through this. Touched only under <see cref="World.Sync"/>.
/// </summary>
public interface IUnit
{
    /// <summary>Object id (ActiveSpellEffectsUpdate key 0).</summary>
    long Id { get; }

    /// <summary>The map it is on; null once it left (a player's connection ended, a mob was removed).</summary>
    Map? Map { get; }

    /// <summary>Its active spells (buffs, debuffs, damage over time, crowd control).</summary>
    Systems.EffectList Effects { get; }

    /// <summary>An event about the unit to every client that knows it, its own included (a player's own session).</summary>
    void SendAround(AlbionEvent e);
}
