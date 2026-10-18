using System.Globalization;
using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// A server cast of CharacterSpellSlot.FakeSpell (25): the client puts an allowfakeslot spell into slot 25 and casts it at an object
/// (stageM chest-slot25 NOTES): its <c>useobject</c> effect is the server's at the cast's end. One record per player in
/// <see cref="FakeSlotCasts"/>: the map, the aim, the spell, the client's counter, its GameTimeStamps and the <see cref="World.Clock"/> time it is due.
/// </summary>
public abstract record FakeSlotCast(Map Map, Vector2 Aim, int SpellId, byte Counter, long Start, long End, TimeSpan Due)
{
    /// <summary>What the cast is, for the log ("corrupted dungeon enter cast", "mists enter cast", "chest opening").</summary>
    public abstract string What { get; }

    /// <summary>The CharacterSpellSlot it is cast from (CastStart key 9, CastFinished key 3): 25 but for the escape (21).</summary>
    public virtual byte CastSlot => FakeSlotCasts.Slot;

    /// <summary>Where CastSpell (19) puts the hit: the aim, or the object it is cast at.</summary>
    public virtual Vector2 HitAt => Aim;

    /// <summary>Checked every tick: why the cast cannot go on (its object gone), or null.</summary>
    public virtual string? Broken(Player p) => null;

    /// <summary>At its due time: whether it may end now (false: wait, e.g. for a map read in the background).</summary>
    public virtual bool Ready(Player p) => true;

    /// <summary>Past its due time and still not <see cref="Ready"/>: why it gives up, or null to wait on.</summary>
    public virtual string? GivesUp(Player p, TimeSpan now) => null;

    /// <summary>Whether a hit ends it (the spell's damagecancelscasting: true but for ENTER_MISTS); <see cref="FakeSlotCasts.Interrupt"/> passes "damage" by otherwise.</summary>
    public virtual bool DamageCancels => true;

    /// <summary>It ended without its effect (interrupted, or its player gone or on another map): what it holds is let go (a mists build waited for).</summary>
    public virtual void Dropped(Player p) { }

    /// <summary>The effect, after CastFinished and CastSpell.</summary>
    public abstract void Complete(Player p);
}

/// <summary>
/// The fake-slot server casts under way (slot 25: the corrupted dungeon entrance, the mists entrance, the loot chest opening; stageL corrupted
/// SPEC §2.2, stageL mists SPEC §3.5, stageM chest-slot25 NOTES): CastStart (14) {8 counter, 9 25} to the observers at the start (the client casts its own), CastFinished (18)
/// {3 25, 4 counter} and CastSpell (19) {6 counter, 7 0} to the player and its observers at castingtime, then the cast's effect. Damage,
/// a knockdown, CastCancel, another cast or attack, mounting (all through <see cref="Flagging.Interrupt"/> or the CombatHandler), a map
/// change or the cast's object leaving end it (damage not the mists' one, <see cref="FakeSlotCast.DamageCancels"/>). <see cref="World.Update"/> runs <see cref="Update"/>. Under <see cref="World.Sync"/>.
/// </summary>
public sealed class FakeSlotCasts(World world)
{
    /// <summary>CharacterSpellSlot.FakeSpell (25).</summary>
    public const byte Slot = 25;

    private readonly Dictionary<Player, FakeSlotCast> _casts = [];

    /// <summary>The cast of <paramref name="p"/> under way, or null.</summary>
    public FakeSlotCast? Of(Player p) => _casts.GetValueOrDefault(p);

    /// <summary>Every cast under way, by its player (the mists count the teammates on their way into a zone).</summary>
    public IEnumerable<KeyValuePair<Player, FakeSlotCast>> All => _casts;

    /// <summary>
    /// Starts <paramref name="cast"/> for <paramref name="p"/> (its checks done by the caller): one under way is replaced, the player
    /// stands up from a knockdown pose and dismounts (dismountbeforecasting), CastStart (14) goes to the observers.
    /// </summary>
    public void Start(Player p, FakeSlotCast cast)
    {
        Interrupt(p, "a new " + cast.What, interrupted: false);
        Knockdown.Acted(p, "casts");
        Mounts.BeforeAction(p, "casts");
        p.SendToObservers(new CastStartEvent
        {
            ddn = p.Id, bwb = cast.Start, bwc = cast.Aim, bwe = cast.End, bwf = (short)cast.SpellId, bwg = -1, Extra = new() { [8] = cast.Counter, [9] = cast.CastSlot },
        });
        _casts[p] = cast;
    }

    /// <summary>
    /// The cast under way ends without its effect: <paramref name="interrupted"/> by the server (damage, a knockdown) tells the caster as
    /// well, its own CastCancel or another action only the observers. A no-op without one, and for "damage" on a cast that damage does
    /// not cancel (<see cref="FakeSlotCast.DamageCancels"/>).
    /// </summary>
    public void Interrupt(Player p, string why, bool interrupted = true)
    {
        world.Corrupted.InterruptShrine(p, why); // stageL phase B: a Demonic Shrine use under way too (no fake-slot cast, ends the same way)
        if (!_casts.TryGetValue(p, out var cast) || why == "damage" && !cast.DamageCancels) return;
        _casts.Remove(p);
        cast.Dropped(p);
        if (p.Map is not null)
        {
            p.SendToObservers(new CastCancelEvent { ddn = p.Id, buo = interrupted, bup = cast.Counter, Extra = new() { [9] = (byte)0 } });
            if (interrupted) p.Session.Send(new CastCancelEvent { ddn = p.Id, buo = true, bup = cast.Counter, buq = SpellCasts.CooldownFactorOf(p) });
        }
        Log.Info("world", $"'{p.Character.Name}''s {cast.What} is cancelled: {why}");
    }

    /// <summary>Every tick: a player gone or on another map drops its cast, a knockdown or a broken one ends it, a due one completes.</summary>
    public void Update()
    {
        if (_casts.Count == 0) return;
        var now = world.Now;
        foreach (var (p, cast) in _casts.ToList())
        {
            if (p.Map != cast.Map || p.Session.Player != p)
            {
                _casts.Remove(p);
                cast.Dropped(p);
            }
            else if (p.Combat.Down is not null) Interrupt(p, "knocked down");
            else if (cast.Broken(p) is { } broken) Interrupt(p, broken);
            else if (now >= cast.Due && p.Session.Status == SessionStatus.InWorld)
            {
                if (cast.Ready(p))
                {
                    _casts.Remove(p);
                    Finish(p, cast);
                }
                else if (cast.GivesUp(p, now) is { } why) Interrupt(p, why);
            }
        }
    }

    /// <summary>CastFinished (18) and CastSpell (19) to the player and its observers, then the cast's effect.</summary>
    private void Finish(Player p, FakeSlotCast cast)
    {
        var now = world.GameNow;
        p.SendAround(new CastFinishedEvent
        {
            ddn = p.Id, buy = (short)cast.SpellId, bu1 = cast.End, bu2 = SpellCasts.CooldownFactorOf(p), Extra = new() { [3] = cast.CastSlot, [4] = cast.Counter },
        });
        p.SendAround(new CastSpellEvent
        {
            ddn = p.Id, bvx = cast.HitAt, bvy = (short)cast.SpellId, bvz = cast.End, bv0 = now, Extra = new() { [6] = cast.Counter, [7] = (short)0 },
        });
        cast.Complete(p);
    }

    // ---- the loot chest opening (useobject type="lootchest": OPEN_LOOTCHEST, OPEN_LOOTCHEST_SOLO, OPEN_TREASURECHEST) ----

    /// <summary>A chest opening under way: the chest, opened at the end by <see cref="RandomDungeons.UseChest"/>.</summary>
    private sealed record ChestCast(Map Map, Vector2 Aim, int SpellId, byte Counter, long Start, long End, TimeSpan Due, LootChest Chest)
        : FakeSlotCast(Map, Aim, SpellId, Counter, Start, End, Due)
    {
        public override string What => "chest opening";

        public override Vector2 HitAt => Chest.Position;

        public override string? Broken(Player p) =>
            Chest.Map != Map || !Map.Objects.ContainsKey(Chest.Id) ? $"{Chest} is gone"
            : !Chest.MayOpen(p) ? $"{Chest} is locked (ChestState {Chest.ViewerState(p).State})"
            : Vector2.Distance(p.PositionAt(DateTime.UtcNow), Chest.Position) > p.World.FakeCasts.ChestReach(Chest) + p.World.Combat.LootRangeSlack ? $"moved away from {Chest}"
            : null;

        public override void Complete(Player p)
        {
            if (p.World.Dungeons.UseChest(p, Chest) is { } why) Log.Info("world", $"'{p.Character.Name}' chest opening of {Chest} failed at its end: {why}");
        }
    }

    /// <summary>The cast time and range of OPEN_LOOTCHEST without spell data (spells.xml: castingtime 2, castrange 5).</summary>
    public const double DefaultChestSeconds = 2, DefaultChestRange = 5;

    /// <summary>The opening spell of <paramref name="chest"/> (its template's <see cref="Data.LootChestTemplate.Spell"/>), null without one or without spell data.</summary>
    private Data.SpellTemplate? ChestSpell(LootChest chest) => chest.Template.Spell is { } name ? world.Spells?.Template(name) : null;

    /// <summary>
    /// How far from <paramref name="chest"/>'s centre its opener may stand: its opening spell's castrange (else <see cref="DefaultChestRange"/>)
    /// + <see cref="SpellCasts.RangeTolerance"/>; RegisterToObject and the loot moves accept at least as much (WorldSession.InReach).
    /// </summary>
    public double ChestReach(LootChest chest) => (ChestSpell(chest)?.CastRange is > 0 and var r ? r : DefaultChestRange) + SpellCasts.RangeTolerance;

    /// <summary>
    /// CastStart on slot 25 with a <c>useobject type="lootchest"</c> spell aimed at <paramref name="aim"/> (the client's chest click for an
    /// Unlocked chest, cz5.u 0x180A268F0; no UseLootChest follows): the map's chest nearest the aim within castrange + 1 m of it, the player
    /// within castrange + 1 m of the chest (<see cref="ChestReach"/>), the spell the chest's own (lootchests.xml spell, when it names one: the
    /// client casts the chest's), standing, no other server cast, the chest openable by the player (<see cref="LootChest.MayOpen"/>,
    /// <see cref="CorruptedDungeons.ChestRefused"/>, <see cref="Hellgates.ChestRefused"/>); then a server cast of the chest spell's castingtime (ignorecasttimereductionbonus: no
    /// cast-speed factor). The cast breaks off when the player gets farther than that reach + Combat:LootRangeSlack. Returns why it was refused, or null.
    /// </summary>
    public string? StartChest(Player p, int spellId, Vector2 aim, byte counter, long start)
    {
        if (p.Map is not { } map) return "not on a map";
        if (p.Combat.Down is { } down) return $"knocked down ({down.Kind})";
        if (p.PvpState.Execute is not null || p.PvpState.FlagCast is not null || p.Combat.Cast is not null) return "a cast is under way";
        var spell = world.Spells?.Template(spellId);
        var range = spell?.CastRange is > 0 and var r ? r : DefaultChestRange;
        var chest = map.Objects.Values.OfType<LootChest>().MinBy(c => Vector2.Distance(c.Position, aim));
        var at = p.PositionAt(DateTime.UtcNow);
        if (chest is null || Vector2.Distance(chest.Position, aim) > range + SpellCasts.RangeTolerance)
            return string.Create(CultureInfo.InvariantCulture, $"no chest within {range:0.#} m of the aim ({aim.X:0.#}, {aim.Y:0.#})");
        if (chest.Template.Spell is { } own && spell is not null && !string.Equals(own, spell.Name, StringComparison.OrdinalIgnoreCase))
            return $"{chest} opens with {own}, not {spell.Name}";
        if (ChestSpell(chest) is { } chestSpell) spell = chestSpell;
        if (Vector2.Distance(at, chest.Position) > ChestReach(chest))
            return string.Create(CultureInfo.InvariantCulture, $"{chest} is {Vector2.Distance(at, chest.Position):0.#} m away (reach {ChestReach(chest):0.#} m)");
        if (!chest.MayOpen(p)) return $"{chest} is locked (ChestState {chest.ViewerState(p).State})";
        if (world.Corrupted.ChestRefused(p, chest) is { } refused) return refused;
        if (world.Hellgates.ChestRefused(p, chest) is { } lost) return lost; // stageL hellgates phase B: a match's chests are the winners'
        var seconds = spell is not null ? spell.CastingTime : DefaultChestSeconds; // 0.5 s OPEN_LOOTCHEST_SOLO, 5 s OPEN_TREASURECHEST
        Start(p, new ChestCast(map, aim, spellId, counter, start, start + SpellCasts.Seconds(seconds), world.Now + TimeSpan.FromSeconds(seconds), chest));
        Log.Info("world", $"'{p.Character.Name}' casts {spell?.Name ?? spellId.ToString(CultureInfo.InvariantCulture)} on {chest}");
        if (seconds <= 0) Update(); // an instant opening ends in this tick
        return null;
    }

    // ---- the dungeon escape (slot 21: ESCAPE_DUNGEON 7 s, ESCAPE_CORRUPTED_DUNGEON 300 s, ESCAPE_HELLGATE 900 s; effect escapedungeon) ----

    /// <summary>CharacterSpellSlot 21: the escape the client puts there inside an instance (dpf.ak 0x1814C4990, the corrupted transition window's Surface).</summary>
    public const byte EscapeSlot = 21;

    /// <summary>
    /// An escape under way (analysis/work/stageN/corrupted-exit/SPEC.md): at its end DungonEscapeReady (467, no keys) to the caster, whose
    /// handler (ctp.ajv 0x1d3 → c5y → LocalPlayerCharacterView.OnDungeonEscapeReady 0x1819C1A20) sends ChangeCluster {0: 0, 3: true}, which the
    /// dungeon, expedition, corrupted and hellgate handlers answer with the way out.
    /// </summary>
    private sealed record EscapeCast(Map Map, Vector2 Aim, int SpellId, byte Counter, long Start, long End, TimeSpan Due, string Spell)
        : FakeSlotCast(Map, Aim, SpellId, Counter, Start, End, Due)
    {
        public override string What => $"escape ({Spell})";

        public override byte CastSlot => EscapeSlot;

        public override void Complete(Player p)
        {
            p.Session.Send(new DungonEscapeReadyEvent());
            Log.Info("world", $"'{p.Character.Name}''s {What} is done: DungonEscapeReady, the client asks the way out");
        }
    }

    /// <summary>Whether <paramref name="p"/> is inside an instance whose ChangeCluster {0: 0, 3: true} leads out (a random dungeon, an expedition, a corrupted dungeon, a hellgate).</summary>
    public bool MayEscape(Player p) => p.Map?.Cluster is { } c
        && (world.Dungeons.Find(c) is not null || world.Expeditions.Find(c) is not null || world.Corrupted.Find(c) is not null || world.Hellgates.Find(c) is not null);

    /// <summary>
    /// CastStart in slot 21 inside an instance (<see cref="MayEscape"/>): a spell with the escapedungeon effect (any of the three: the client picks
    /// it by the instance's state), standing, no other cast; then a server cast of its castingtime (ignorecasttimereductionbonus), damage cancels it
    /// (damagecancelscasting). The official server runs the escape as any spell and tells the end with 467; without it the client waits forever.
    /// Returns why it was refused, or null.
    /// </summary>
    public string? StartEscape(Player p, int spellId, byte counter, long start)
    {
        if (p.Map is not { } map) return "not on a map";
        if (!MayEscape(p)) return "not inside a dungeon";
        if (p.Combat.Down is { } down) return $"knocked down ({down.Kind})";
        if (p.PvpState.Execute is not null || p.PvpState.FlagCast is not null || p.Combat.Cast is not null) return "a cast is under way";
        var spell = world.Spells?.Template(spellId);
        if (world.Spells is not null && (spell is null || !spell.Effects.Any(e => e.Name.LocalName == "escapedungeon")))
            return $"{spell?.Name ?? spellId.ToString(CultureInfo.InvariantCulture)} is no escape spell";
        var seconds = spell?.CastingTime ?? DefaultEscapeSeconds;
        var name = spell?.Name ?? "ESCAPE_DUNGEON";
        Start(p, new EscapeCast(map, p.PositionAt(DateTime.UtcNow), spellId, counter, start, start + SpellCasts.Seconds(seconds), world.Now + TimeSpan.FromSeconds(seconds), name));
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"'{p.Character.Name}' casts {name} ({seconds:0.#} s) in {map.Cluster}"));
        if (seconds <= 0) Update();
        return null;
    }

    /// <summary>ESCAPE_DUNGEON's castingtime without spell data.</summary>
    public const double DefaultEscapeSeconds = 7;
}
