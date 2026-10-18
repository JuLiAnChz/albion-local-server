using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A buff shrine (analysis/work/stageN/shrines/SPEC.md §3): a &lt;shrine shrinetype&gt; tile as NewShrine (396, aut; cjb.bx 0x180AAA7B0 → clz):
/// key 0 id, 1 position, 2 direction, 3 the buffshrines.xml uniquename (clz.af(aut) looks the definition up with bdz.abo and its usespell and
/// effectspell with be7.abo), 4 the tiles.xml tile name (clz.aq0 builds its collider with the scene's GetTile; "No tileid set shrine" without),
/// 5 the state (clz.a: 0 <see cref="Locked"/>, 1 <see cref="Ready"/>, 2 <see cref="Spent"/>), 6 the state's GameTimeStamp (the time a spent
/// shrine is ready again), 7 the end of its interaction protection (no tooltip, no use before it; 0 here). UpdateShrine (397, a1h) {0 id,
/// 1 state, 2 key 6} changes it (clz.ajv). Landscape-bound like the corrupted shrine: never left, sent again on re-entry.
/// </summary>
public sealed class BuffShrine(long id, BuffShrineSpawn spawn, BuffShrineData.Definition definition, long ticks) : WorldObject(id, spawn)
{
    /// <summary>clz.a: a click sends UseShrine without the use spell (ShrineObjectView.Use); this server's lock while mobs of its faction are near.</summary>
    public const byte Locked = 0;

    /// <summary>clz.a: a click sends UseShrine {1 id, 2 1} and, answered rc 0, casts the usespell in slot 25 (cz6.a.d); the only state with the buff tooltip.</summary>
    public const byte Ready = 1;

    /// <summary>clz.a: a click only shows the client's state message (dcg), nothing goes out.</summary>
    public const byte Spent = 2;

    public BuffShrineSpawn Member { get; } = spawn;

    public BuffShrineData.Definition Definition { get; } = definition;

    public byte State { get; set; } = Ready;

    /// <summary>Key 6: the GameTimeStamp of the state (a spent one: when it is ready again).</summary>
    public long StateTicks { get; set; } = ticks;

    /// <summary>The uses left before it is spent (maxcharges; an InfiniteCapacity shrine never runs out).</summary>
    public int Charges { get; set; } = definition.MaxCharges;

    /// <summary>When a spent shrine is ready again (<see cref="World.Clock"/>); null = never (respawns="false": for the map's life).</summary>
    public TimeSpan? ReadyAt { get; set; }

    public override string Kind => "shrine";
    public override string TypeName => $"{Definition.Name} (state {State})";
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewShrineEvent
        {
            c9u = Id,                   // [0]
            c9v = Position,             // [1]
            c9w = Direction,            // [2]
            c9x = Definition.Name,      // [3] buffshrines.xml uniquename (bdz.abo)
            c9y = Member.Tile,          // [4] tiles.xml tile name (a9f.u GetTile)
            c9z = State,                // [5]
            c90 = StateTicks,           // [6]
            Extra = new() { [5] = State }, // state 0 goes out too
        };
    }

    public UpdateShrineEvent BuildUpdate() => new() { ddn = Id, d5q = State, d5r = StateTicks, Extra = new() { [1] = State } };
}
