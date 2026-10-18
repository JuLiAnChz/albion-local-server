using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.AI;

/// <summary>
/// A fighting mob that runs its mobs.xml &lt;AI&gt; rotation (stageD mobspells, analysis/work/stageD/mobspells/SPEC.md §2, §4;
/// ARCHITECTURE.md §6.3 level 1) on top of <see cref="AggressiveAI"/>'s aggro, threat, chase, leash and swings (with
/// <c>aggressive: false</c> the <see cref="PassiveAI"/> rules: no aggro of its own, passivepursuitradius).
/// <list type="bullet">
/// <item><b>Think</b> (every CombatSettings.MobAiThink while in combat, the first on the entry tick, R5/R6): nothing while it casts,
/// channels, stands after a cast (standtime), winds up a swing (Attack k1..k4, R4) or within a cooldowntillnextcast lockout (R3);
/// otherwise the Behaviour nodes in document order, the first that works runs and ends the think (R1). A spell beats the swing.</item>
/// <item><b>Spell</b> node: its slot's cooldown (recastdelay from the cast end, R2; cooldowngroup: every slot of the group, Q4) and
/// energy (round(energyusage × abilitypower / 100), half to even), its candidates by target and targetSelection, the first one
/// for which its conditions hold and which is in range (castrange + both collision radii, the ground point within castrange).</item>
/// <item><b>Conditions</b>: ANDed in XML order with short-circuit; IfChance rolls at most once per CombatSettings.MobAiChanceReroll
/// on a think that reaches it, per node (R5, Q1); fight duration from the combat entry (R6); variables with their TTL (R8).</item>
/// <item><b>After a cast</b>: busy until (channel end or cast end) + standtime, the next swing at max(cast end + 1 / attackspeed,
/// that), the no-hit leash restarted at the CastStart.</item>
/// <item><b>Events</b>: the node's onCastStart / onCastSuccessful / onCastInterrupted / onChannelingHit (R9), the global onSpawn (first
/// tick), onReset (the reset start) and onDeath (the kill).</item>
/// </list>
/// Touched only under <see cref="World.Sync"/>.
/// </summary>
public class BehaviourAI : AggressiveAI, IMobCaster
{
    private readonly bool _aggressive;

    public BehaviourAI(Mob me, AiScript script, bool aggressive = true) : base(me)
    {
        Script = script;
        _aggressive = aggressive;
        _slotReady = new long[Math.Max(1, script.Slots.Count)];
        foreach (var v in script.Vars) _vars[v.Name] = (v.Initial, null);
    }

    public AiScript Script { get; }

    protected override bool Aggressive => _aggressive;

    protected override float Pursuit => _aggressive ? Me.Template.PursuitRadius : Me.Template.PassivePursuitRadius;

    /// <summary>The cast under way (its channel included); null when none.</summary>
    public MobCast? Cast { get; private set; }

    /// <summary>GameTimeStamp of the combat entry (the fight clock, R6); null out of combat.</summary>
    private long? _combatStart;

    /// <summary>GameTimeStamp until which it stands after a cast (standtime, R4), and of the end of the cooldowntillnextcast lockout (R3).</summary>
    private long _busyUntil, _nextSpellAt;

    /// <summary>GameTimeStamp each slot is ready again (R2, R7), and each cooldowngroup.</summary>
    private readonly long[] _slotReady;

    private readonly Dictionary<string, long> _groupReady = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>GameTimeStamp each spell that only appears in events is ready again (no slot of its own, Q13: it still sends 0).</summary>
    private readonly Dictionary<string, long> _eventReady = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><see cref="World.Clock"/> time of its next think.</summary>
    private TimeSpan _nextThink;

    /// <summary>Per IfChance node: until when its last roll holds and what it was (R5).</summary>
    private readonly Dictionary<AiLeaf, (TimeSpan Until, bool Result)> _rolls = [];

    /// <summary>Its variables (R8): value, and when a timeToLiveSeconds one returns to its initial value.</summary>
    private readonly Dictionary<string, (string Value, TimeSpan? Expires)> _vars = new(StringComparer.OrdinalIgnoreCase);

    private bool _spawned;

    /// <summary>The &lt;Movement&gt; under way (stageN spells-bosses) and when its walk should be over; null = none.</summary>
    private AiMovement? _moving;

    private long _moveEnd;

    /// <summary>GameTimeStamp each &lt;Movement&gt; node may run again (cooldowntillnextmovement from its end).</summary>
    private readonly Dictionary<AiMovement, long> _moveReady = [];

    /// <summary>GameTimeStamp of its next IdleBehaviour action (out of combat); null = not scheduled.</summary>
    private long? _idleAt;

    public override string Describe() => base.Describe() + string.Create(CultureInfo.InvariantCulture,
        $", <AI> of {Script.Source}{(Cast is { } c ? $", casting {c}" : "")}{(_combatStart is { } s ? $", fight {(Me.GameNow - s) / 1e7:0.#} s" : "")}");

    protected override void Update(World world, Map map)
    {
        if (!_spawned)
        {
            _spawned = true;
            RunEvent(world, map, Script.Events, "onSpawn", null);
        }
        Idle(world, map);
        base.Update(world, map);
    }

    protected override void OnEnterCombat(World world)
    {
        _idleAt = null;
        _combatStart = world.GameNow;
        _nextThink = world.Now;
    }

    /// <summary>The fight's &lt;AI&gt; step (see the class summary): true while it casts or stands, or when a cast has just started.</summary>
    protected override bool Act(World world, Map map, Player target)
    {
        var now = world.GameNow;
        if (_moving is { } mv)
        {
            // A reposition under way (stageN spells-bosses): no chase or swing until it has walked there; then onMovementFinished.
            if (Me.Motion.Moving && now < _moveEnd) return true;
            _moving = null;
            RunEvent(world, map, mv.Events, "onMovementFinished", null);
        }
        if (Cast is not null || now < _busyUntil)
        {
            if (Me.Motion.Moving) Me.Motion.Stop(MotionMaster.Facing(Me.Position, target.PositionAt(DateTime.UtcNow)));
            return true;
        }
        if (!world.MobSpells || world.Spells is null || world.Now < _nextThink) return false;
        var step = world.Combat.MobAiThink;
        while (_nextThink <= world.Now) _nextThink += step;
        if (now < WindupEnd || now < _nextSpellAt) return false;
        foreach (var node in Script.Behaviour)
            if (Run(world, map, node, dry: false))
                return Cast is not null || world.GameNow < _busyUntil; // the think ends; only a cast keeps the swing back
        return false;
    }

    // ---- nodes (SPEC §4.2) ----

    /// <summary>Runs one node of its script now, as a think would (.mob ai, the self-test); false off its map.</summary>
    public bool Try(AiNode node, bool dry = false) => Me.World is { } world && Me.Map is { } map && Run(world, map, node, dry);

    /// <summary>Whether a condition of its script holds now for <paramref name="candidate"/> (null: its current target).</summary>
    public bool Check(AiCond? c, IUnit? candidate = null) => Me.World is { } world && Me.Map is { } map && Holds(world, map, c, candidate);

    /// <summary>How many IfChance rolls it has taken (R5: at most one per node per CombatSettings.MobAiChanceReroll).</summary>
    public int ChanceRolls { get; private set; }

    /// <summary>
    /// Runs <paramref name="node"/>; <paramref name="dry"/>: only whether it would work (SelectRandom OnlySelectWorkingActions).
    /// If: its one action when its conditions hold; SelectFirst: the first child that works; SelectRandom: a weighted pick; CompoundAction:
    /// its first Spell child starts (or it has none), then the others run; Variable and BroadcastVariable always work; Movement and
    /// unknown tags never do (5f.4).
    /// </summary>
    private bool Run(World world, Map map, AiNode node, bool dry, bool @event = false)
    {
        switch (node)
        {
            case AiSpell s:
                return TrySpell(world, map, s, dry, @event);
            case AiIf i:
                return Holds(world, map, i.When, null) && i.Action is { } a && Run(world, map, a, dry, @event);
            case AiSelectFirst f:
                return Holds(world, map, f.When, null) && f.Children.Any(c => Run(world, map, c, dry, @event));
            case AiSelectRandom r:
            {
                if (!Holds(world, map, r.When, null)) return false;
                var pool = r.FailIfNotWorking ? r.Children : [.. r.Children.Where(c => Run(world, map, c, dry: true, @event))];
                if (pool.Length == 0) return false;
                if (dry && !r.FailIfNotWorking) return true;
                var pick = Pick(map, pool);
                return Run(world, map, pick, dry, @event);
            }
            case AiCompound c:
            {
                if (!Holds(world, map, c.When, null)) return false;
                var lead = c.Children.OfType<AiSpell>().FirstOrDefault();
                if (lead is not null && !Run(world, map, lead, dry, @event)) return false;
                if (dry) return true;
                foreach (var other in c.Children.Where(x => x != lead)) Run(world, map, other, dry: false, @event);
                return true;
            }
            case AiVariable v:
                if (!Holds(world, map, v.When, null)) return false;
                if (!dry) SetVar(world, v.Name, v.Op, v.Value);
                return true;
            case AiBroadcast b:
                if (!Holds(world, map, b.When, null)) return false;
                if (!dry) Broadcast(world, map, b);
                return true;
            case AiMovement m:
                return TryMove(world, map, m, dry);
            case AiUnknown u:
                Combat.LogOnce("ainode:" + u.Tag, $"<AI> node <{u.Tag}> is not supported yet: never works");
                return false;
        }
        return false;
    }

    /// <summary>A weighted pick (weight, default 1) on the map's random.</summary>
    private static AiNode Pick(Map map, AiNode[] pool)
    {
        var total = pool.Sum(n => Math.Max(0, n.Weight));
        if (total <= 0) return pool[0];
        var r = map.Random.NextDouble() * total;
        foreach (var n in pool)
        {
            r -= Math.Max(0, n.Weight);
            if (r < 0) return n;
        }
        return pool[^1];
    }

    /// <summary>
    /// A Spell node (SPEC §4.2 SpellNode.TryRun): the spell is an active or toggle spell, its slot and group are ready, the energy is
    /// there; the first candidate for which its conditions hold and which is in range gets the cast. An event's spell (<paramref name="event"/>)
    /// skips nothing else but runs only when no cast is under way. A spell without effects or cast time (MOB_SPEAK) only takes a
    /// counter and its cooldown and does not end the think (SPEC §9 Q9: nothing of it was captured).
    /// </summary>
    private bool TrySpell(World world, Map map, AiSpell s, bool dry, bool @event)
    {
        if (Cast is not null)
        {
            // onCastStart / onCastSuccessful spells: the cast that raised the event is still under way (no such data yet).
            if (@event) Combat.LogOnce("aievcast:" + s.Spell, $"<AI> event spell {s.Spell} of {Me.Template.Name} during a cast is not supported yet (5f.4): not cast");
            return false;
        }
        if (world.Spells?.Template(s.Spell) is not { Kind: "activespell" or "togglespell" } spell)
        {
            Combat.LogOnce("aispell:" + s.Spell, $"<AI> spell {s.Spell} of {Me.Template.Name} is not an active spell of spells.xml: never cast");
            return false;
        }
        var now = world.GameNow;
        var ready = s.Numbered ? _slotReady[Math.Min(s.Slot, _slotReady.Length - 1)] : _eventReady.GetValueOrDefault(s.Spell);
        if (ready > now || s.Group is { } g && _groupReady.GetValueOrDefault(g) > now) return false;
        var ap = Me.Template.AbilityPower;
        var cost = spell.EnergyUsage <= 0 ? 0 : spell.EnergyUsageIsRelative ? (int)Math.Round(spell.EnergyUsage * Me.Energy.Max) : Combat.MobEnergyCost(spell.EnergyUsage, ap);
        if (Me.Energy.At(now) < cost) return false;
        foreach (var candidate in Candidates(world, map, s, spell))
        {
            if (!Holds(world, map, s.When, candidate, s.Target == "self")) continue;
            if (!InRange(world, spell, s, candidate, out var point)) continue;
            if (dry) return true;
            if (spell.Effects.Count == 0 && spell.CastingTime <= 0 && !spell.HasChannel)
            {
                Me.NextCastCounter();
                SetCooldown(s, spell, now);
                RunEvent(world, map, s.Events, "onCastSuccessful", null);
                return false;
            }
            var start = now;
            var cast = new MobCast
            {
                Caster = Me, Spell = spell, Node = s, Slot = s.Slot, Counter = Me.NextCastCounter(), Target = spell.Target == "ground" ? null : candidate,
                Point = spell.Target == "ground" ? point : Me.Position, Start = start, End = start + SpellCasts.Seconds(spell.CastingTime), Cost = cost, Ap = ap, Owner = this,
            };
            Cast = cast;
            RestartLeash(world);
            // It turns to its aim before the CastStart, standing or walking with a cast time (it stops then), so the telegraph and a
            // casterrotation area point where the spell goes, not along the last leg of its MobNav path (stageN spells-bosses). An instant
            // cast during a walk (a return home, a roam) does not stop it.
            var aim = spell.Target == "ground" ? point : Position(candidate);
            var turn = (spell.Target == "ground" || candidate != Me) && Vector2.DistanceSquared(aim, Me.Position) > 1e-4f;
            if (spell.CastingTime > 0 && Me.Motion.Moving) Me.Motion.Stop(turn ? MotionMaster.Facing(Me.Position, aim) : null);
            else if (turn && !Me.Motion.Moving) Me.Motion.Stop(MotionMaster.Facing(Me.Position, aim));
            MobCasts.Start(world, map, cast);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The candidates of a Spell node (SPEC §4.2 step 2): self the mob; current the NewMob key 12 target; anythreat the valid threat
    /// list; anyenemy the valid players within castrange; anyfriend the living mobs of its faction within castrange (itself too);
    /// currentortopthreatorself; ordered by targetSelection (Closest, Furthest, Lowest/HighestHealth, Lowest/HighestEnergy; Random and
    /// the default, Q5: shuffled on the map's random when there is more than one).
    /// </summary>
    private List<IUnit> Candidates(World world, Map map, AiSpell s, SpellTemplate spell)
    {
        var now = world.GameNow;
        var here = Me.Position;
        var reach = spell.CastRange + Me.Template.CollisionRadius + world.CollisionRadius;
        IEnumerable<Player> threat = Me.Threat.Keys.Where(p => MobCasts.Valid(p, map, now));
        Player? current = Me.Threat.Keys.FirstOrDefault(p => p.Id == Me.TargetId && MobCasts.Valid(p, map, now));
        List<IUnit> list = s.Target switch
        {
            "self" => [Me],
            "current" => current is null ? [] : [current],
            "anythreat" => [.. threat],
            "anyenemy" => [.. map.Players.Values.Where(p => MobCasts.Valid(p, map, now) && Vector2.Distance(here, p.PositionAt(DateTime.UtcNow)) <= reach)],
            "anyfriend" => [.. map.Objects.Values.OfType<Mob>().Where(m => !m.Dead && !m.Removed && string.Equals(m.Template.Faction, Me.Template.Faction, StringComparison.OrdinalIgnoreCase)
                && Vector2.Distance(here, m.Position) <= spell.CastRange + Me.Template.CollisionRadius + m.Template.CollisionRadius)],
            "currentortopthreatorself" => [(IUnit?)current ?? (IUnit?)threat.OrderByDescending(p => Me.Threat[p]).FirstOrDefault() ?? Me],
            _ => [],
        };
        if (list.Count == 0 && s.Target is not ("self" or "current" or "anythreat" or "anyenemy" or "anyfriend" or "currentortopthreatorself"))
            Combat.LogOnce("aitarget:" + s.Target, $"<AI> spell target '{s.Target}' is not supported yet: no candidate");
        if (list.Count < 2) return list;
        double Health(IUnit u) => HealthOf(u, now);
        double Energy(IUnit u) => EnergyOf(u, now);
        return (s.Selection ?? "").ToLowerInvariant() switch
        {
            "closest" => [.. list.OrderBy(u => Vector2.Distance(here, Position(u)))],
            "furthest" => [.. list.OrderByDescending(u => Vector2.Distance(here, Position(u)))],
            "lowesthealth" => [.. list.OrderBy(Health)],
            "highesthealth" => [.. list.OrderByDescending(Health)],
            "lowestenergy" => [.. list.OrderBy(Energy)],
            "highestenergy" => [.. list.OrderByDescending(Energy)],
            _ => [.. list.OrderBy(_ => map.Random.Next())],
        };
    }

    private static Vector2 Position(IUnit u) => u switch { Player p => p.PositionAt(DateTime.UtcNow), Mob m => m.Position, _ => Vector2.Zero };

    /// <summary>The health of a player or a mob as a fraction of its max (0 for anything else).</summary>
    private static double HealthOf(IUnit u, long now) => u switch
    {
        Player p => p.Health.At(now) / Math.Max(1, p.Health.Max), Mob m => m.Health.At(now) / Math.Max(1, m.Health.Max), _ => 0,
    };

    /// <summary>The energy of a player or a mob as a fraction of its max (0 without energy).</summary>
    private static double EnergyOf(IUnit u, long now) => u switch
    {
        Player { Energy.Max: > 0 } p => p.Energy.At(now) / p.Energy.Max, Mob { Energy.Max: > 0 } m => m.Energy.At(now) / m.Energy.Max, _ => 0,
    };

    /// <summary>
    /// Whether <paramref name="candidate"/> is in range of <paramref name="spell"/> (SPEC §4.2 steps 3-4), and the ground point: a
    /// self spell needs none; an enemy or friend spell the centre distance within castrange + both collision radii; a ground spell
    /// its point (the candidate's position, the mob's for node target self, + groundtargetoffset along mob → target turned by
    /// groundtargetangle, from the mob with groundtargetrelativeto="caster"; pulled in with adjustgroundtargettorange) within
    /// castrange.
    /// </summary>
    private bool InRange(World world, SpellTemplate spell, AiSpell s, IUnit candidate, out Vector2 point)
    {
        var here = Me.Position;
        // StageN mobnav: no spell at a player it does not see (a wall between them on the map's walk grid).
        if (candidate is Player && Me.Map is { } onMap && !Sees(world, onMap, here, Position(candidate)))
        {
            point = Position(candidate);
            return false;
        }
        var there = Position(candidate);
        point = there;
        var slack = Me.Template.CollisionRadius + (candidate is Player ? world.CollisionRadius : candidate is Mob m && m != Me ? m.Template.CollisionRadius : 0);
        if (spell.Target == "ground")
        {
            if (s.GroundOffset != 0)
            {
                var line = there - here;
                var dir = line.LengthSquared() > 1e-6f ? Vector2.Normalize(line) : SpellMovement.Facing(Me.Direction);
                var a = s.GroundAngle * Math.PI / 180;
                var turned = new Vector2((float)(dir.X * Math.Cos(a) + dir.Y * Math.Sin(a)), (float)(-dir.X * Math.Sin(a) + dir.Y * Math.Cos(a)));
                point = (s.RelativeToCaster || candidate == Me ? here : there) + turned * (float)s.GroundOffset;
            }
            var d = Vector2.Distance(here, point);
            if (d > spell.CastRange && spell.AdjustGroundTargetToRange && d > 0.001f)
            {
                point = here + (point - here) * (spell.CastRange / d);
                return true;
            }
            return candidate == Me || d <= spell.CastRange;
        }
        if (candidate == Me || spell.Target is "self" or "all") return true;
        return Vector2.Distance(here, there) <= spell.CastRange + slack;
    }

    // ---- movement and idle (5f.4, stageN spells-bosses) ----

    /// <summary>
    /// A &lt;Movement&gt; node: off its cooldown and not while casting, the first candidate (self; current; anythreat in random order) for
    /// which its conditions hold gets the walk: <see cref="AiMovement.Distance"/> m at <see cref="AiMovement.Angle"/>° (clockwise) from the
    /// line mob → candidate (self: from its facing), at attackmovespeed. The walk grid keeps it out of the walls: the line must be clear of
    /// blocked cells, else the furthest half or more of it that is (none: the candidate does not work). onMovementStarted runs at once,
    /// onMovementFinished at the arrival (<see cref="Act"/>); the cooldown runs from the planned arrival.
    /// </summary>
    private bool TryMove(World world, Map map, AiMovement m, bool dry)
    {
        var now = world.GameNow;
        if (Cast is not null || _moving is not null || _moveReady.GetValueOrDefault(m) > now || m.Distance <= 0) return false;
        var speed = Me.Template.AttackMoveSpeed > 0 ? Me.Template.AttackMoveSpeed : Me.Template.MoveSpeed;
        if (speed <= 0 || SpellEffects.Rooted(Me, now)) return false;
        Player? current = Me.Threat.Keys.FirstOrDefault(p => p.Id == Me.TargetId && MobCasts.Valid(p, map, now));
        List<IUnit> candidates = m.Target switch
        {
            "self" => [Me],
            "anythreat" => [.. Me.Threat.Keys.Where(p => MobCasts.Valid(p, map, now)).OrderBy(_ => map.Random.Next())],
            _ => current is null ? [] : [current],
        };
        var here = Me.Position;
        var grid = MobNav.Grid(world, map);
        foreach (var candidate in candidates)
        {
            if (!Holds(world, map, m.When, candidate)) continue;
            var line = candidate == Me ? Vector2.Zero : Position(candidate) - here;
            var dir = line.LengthSquared() > 1e-6f ? Vector2.Normalize(line) : SpellMovement.Facing(Me.Direction);
            var a = m.Angle * Math.PI / 180;
            var turned = new Vector2((float)(dir.X * Math.Cos(a) + dir.Y * Math.Sin(a)), (float)(-dir.X * Math.Sin(a) + dir.Y * Math.Cos(a)));
            Vector2? to = null;
            for (var k = 10; k >= 5 && to is null; k--)
            {
                var at = here + turned * (float)(m.Distance * k / 10);
                if (grid is null || grid.ClearFree(here, at) || (!grid.Free(here) && grid.ClearOpen(here, at))) to = at;
            }
            if (to is not { } dest) continue;
            if (dry) return true;
            Me.Motion.MoveTo(dest, speed, candidate == Me ? null : MotionMaster.Facing(dest, Position(candidate)));
            if (!Me.Motion.Moving) return false;
            _moving = m;
            _moveEnd = now + SpellCasts.Seconds(Vector2.Distance(here, dest) / speed + 0.5);
            _moveReady[m] = _moveEnd + SpellCasts.Seconds(m.Cooldown);
            if (!m.AutoAttacks) NextSwing = Math.Max(NextSwing, _moveEnd);
            RestartLeash(world);
            AlbionLocal.Photon.Log.Debug("ai", string.Create(CultureInfo.InvariantCulture,
                $"{Me} repositions {Vector2.Distance(here, dest):0.#} m at {m.Angle:0}° from {(candidate == Me ? "its facing" : $"#{candidate.Id}")}"));
            RunEvent(world, map, m.Events, "onMovementStarted", null);
            return true;
        }
        return false;
    }

    /// <summary>
    /// IdleBehaviour (out of combat, World.MobSpells): idleTimeUntilFirstAction after it became idle, then every idleTimeAfterActionStartMin
    /// to Max seconds, its nodes in order until one works (the wisps' and scavengers' idle talk and throws).
    /// </summary>
    private void Idle(World world, Map map)
    {
        if (Script.Idle.Count == 0 || State != MobState.Idle || !world.MobSpells || Cast is not null) return;
        var now = world.GameNow;
        var (first, min, max) = Script.IdleTimes;
        if (_idleAt is not { } at)
        {
            _idleAt = now + SpellCasts.Seconds(first);
            return;
        }
        if (now < at) return;
        foreach (var node in Script.Idle)
            if (Run(world, map, node, dry: false)) break;
        _idleAt = now + SpellCasts.Seconds(min + map.Random.NextDouble() * Math.Max(0, max - min));
    }

    // ---- the cast's callbacks ----

    void IMobCaster.OnCastEvent(MobCast cast, string name)
    {
        if (cast.Node is { } node && Me.World is { } world && Me.Map is { } map) RunEvent(world, map, node.Events, name, cast);
    }

    /// <summary>
    /// CastFinished went out (SPEC R2-R4): the slot (and its cooldowngroup) on cooldown until end + recastdelay, the lockout of
    /// cooldowntillnextcast from the end, busy until (channel end or end) + standtime, the next swing at max(end + 1 / attackspeed, that).
    /// </summary>
    void IMobCaster.OnCastFinished(MobCast cast)
    {
        if (cast.Node is { } node) SetCooldown(node, cast.Spell, cast.End);
        if (cast.Node is { Cdtnc: > 0 } n) _nextSpellAt = Math.Max(_nextSpellAt, cast.End + SpellCasts.Seconds(n.Cdtnc));
        var free = (cast.ChannelEnd ?? cast.End) + SpellCasts.Seconds(cast.Spell.StandTime);
        _busyUntil = Math.Max(_busyUntil, free);
        var t = Me.Template;
        NextSwing = Math.Max(t.AttackSpeed > 0 ? cast.End + SpellCasts.Seconds(1 / t.AttackSpeed) : cast.End, free);
    }

    /// <summary>
    /// interruptcast (stageD effects batch 2): its cast under way, not finished yet, is cancelled (CastCancel {1 true}) unless its spell is
    /// interruptiblebyspell="false" or a notinterruptible is on the mob (batch 3).
    /// </summary>
    public void Interrupt()
    {
        if (Cast is not { Finished: false } cast || !cast.Spell.InterruptibleBySpell || SpellEffects.NotInterruptible(Me, Me.World?.GameNow ?? 0)) return;
        MobCasts.Cancel(cast, interrupted: true);
        Cast = null;
    }

    void IMobCaster.OnCastOver(MobCast cast)
    {
        if (Cast == cast) Cast = null;
    }

    private void SetCooldown(AiSpell node, SpellTemplate spell, long end)
    {
        var ready = end + SpellCasts.Seconds(spell.RecastDelay);
        if (!node.Numbered) _eventReady[node.Spell] = Math.Max(_eventReady.GetValueOrDefault(node.Spell), ready);
        else
        {
            var slot = Math.Min(node.Slot, _slotReady.Length - 1);
            _slotReady[slot] = Math.Max(_slotReady[slot], ready);
        }
        if (node.Group is { } g) _groupReady[g] = Math.Max(_groupReady.GetValueOrDefault(g), ready);
    }

    /// <summary>
    /// The actions of event <paramref name="name"/> in <paramref name="events"/>, at once (their spells skip the think's busy rules).
    /// A dead mob casts nothing: the onDeath spells (3 bosses) are logged once and skipped (5f.4).
    /// </summary>
    private void RunEvent(World world, Map map, IReadOnlyDictionary<string, AiNode[]> events, string name, MobCast? cast)
    {
        if (!events.TryGetValue(name, out var actions)) return;
        foreach (var a in actions)
        {
            if (a is AiSpell { Spell: var spell } && Me.Dead)
                Combat.LogOnce("aideath:" + spell, $"<AI> {name} spell {spell} of {Me.Template.Name} is not supported yet (5f.4): a dead mob casts nothing");
            if (a is not AiSpell || (!Me.Dead && world.MobSpells && world.Spells is not null)) Run(world, map, a, dry: false, @event: true);
        }
    }

    // ---- reset and death (SPEC §4.3) ----

    /// <summary>
    /// The leash reset: the cast cancelled (CastCancel without key 1), its channel and areas gone, the resetOnMobReset variables, the
    /// fight clock and the slot cooldowns cleared (Q6); onReset once the reset has cleared the mob's effects (<see cref="OnResetDone"/>),
    /// so what its spells apply stays (until the arrival home clears it).
    /// </summary>
    protected override void OnReset(World world, Map map)
    {
        Stop(death: false);
        foreach (var v in Script.Vars.Where(v => v.ResetOnMobReset)) _vars[v.Name] = (v.Initial, null);
        _combatStart = null;
        Array.Clear(_slotReady);
        _groupReady.Clear();
        _eventReady.Clear();
        _busyUntil = _nextSpellAt = 0;
        _rolls.Clear();
    }

    protected override void OnResetDone(World world, Map map) => RunEvent(world, map, Script.Events, "onReset", null);

    public override void OnDeath()
    {
        Stop(death: true);
        if (Me.World is { } world && Me.Map is { } map) RunEvent(world, map, Script.Events, "onDeath", null);
        foreach (var v in Script.Vars.Where(v => v.ResetOnMobDeath)) _vars[v.Name] = (v.Initial, null);
        _combatStart = null;
        base.OnDeath();
    }

    public override void Halt(World world, Map map)
    {
        Stop(death: false);
        _combatStart = null;
        base.Halt(world, map);
    }

    /// <summary>The cast under way is cancelled as captured (CastCancel {0, 2 counter, 9 0}, no key 1) and the mob's areas leave.</summary>
    private void Stop(bool death)
    {
        if (Cast is { } cast) MobCasts.Cancel(cast, interrupted: false);
        Cast = null;
        MobCasts.OwnerGone(Me, death);
    }

    // ---- variables (R8) ----

    /// <summary>Variable <paramref name="name"/> now: its value, or its initial value once its TTL has run out.</summary>
    public string Var(World world, string name)
    {
        if (!_vars.TryGetValue(name, out var v)) return "";
        if (v.Expires is { } at && world.Now >= at)
        {
            var initial = Script.Vars.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.Initial ?? "";
            _vars[name] = (initial, null);
            return initial;
        }
        return v.Value;
    }

    /// <summary>Variable op (set, add, subtract, negate: a bool flips, a number changes sign); a set restarts its TTL.</summary>
    public void SetVar(World world, string name, string op, string value)
    {
        var current = Var(world, name);
        double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        var next = op switch
        {
            "add" => (Num(current) + Num(value)).ToString(CultureInfo.InvariantCulture),
            "subtract" => (Num(current) - Num(value)).ToString(CultureInfo.InvariantCulture),
            "negate" => bool.TryParse(current, out var b) ? (!b).ToString().ToLowerInvariant() : (-Num(current)).ToString(CultureInfo.InvariantCulture),
            _ => value,
        };
        var decl = Script.Vars.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        _vars[name] = (next, decl is { TimeToLive: > 0 } ? world.Now + TimeSpan.FromSeconds(decl.TimeToLive) : null);
    }

    /// <summary>BroadcastVariable: the variable set on every living BehaviourAI mob within radius (itself only with includeSelf).</summary>
    private void Broadcast(World world, Map map, AiBroadcast b)
    {
        foreach (var m in map.Objects.Values.OfType<Mob>().ToList())
        {
            if (m.Dead || m.Removed || (m == Me && !b.IncludeSelf) || Vector2.Distance(m.Position, Me.Position) > b.Radius) continue;
            if (m.AI is BehaviourAI other) other.SetVar(world, b.Name, "set", b.Value);
        }
    }

    // ---- conditions (SPEC §1, R5, R6, R10) ----

    /// <summary>
    /// Whether <paramref name="c"/> holds for <paramref name="candidate"/> (null: the current target; <paramref name="self"/>, a
    /// node with target self: the IfTarget* leaves read the current target, the spell-target ones the mob); null holds.
    /// </summary>
    private bool Holds(World world, Map map, AiCond? c, IUnit? candidate, bool self = false) => c switch
    {
        null => true,
        AiAll all => all.Parts.All(p => Holds(world, map, p, candidate, self)),
        AiAny any => any.Parts.Any(p => Holds(world, map, p, candidate, self)),
        AiNot not => not.Parts.Length == 0 || !not.Parts.All(p => Holds(world, map, p, candidate, self)),
        AiLeaf leaf => Leaf(world, map, leaf, candidate, self),
        _ => false,
    };

    /// <summary>The fight duration in seconds (0 out of combat).</summary>
    public double FightSeconds(World world) => _combatStart is { } s ? (world.GameNow - s) / (double)TimeSpan.TicksPerSecond : 0;

    private bool Leaf(World world, Map map, AiLeaf leaf, IUnit? candidate, bool self)
    {
        var now = world.GameNow;
        // SPEC §4.2 step 3: the target-dependent leaves see the candidate (a friend of an anyfriend spell too), the current
        // target only without one or on a self spell.
        IUnit? current = Me.Threat.Keys.FirstOrDefault(p => p.Id == Me.TargetId);
        var target = candidate is null || (self && candidate == Me) ? current : candidate;
        var spellTarget = candidate ?? current;
        switch (leaf.Kind)
        {
            case "iffightduration": return leaf.Compare(FightSeconds(world));
            case "ifchance": return Chance(world, map, leaf);
            case "ifownhealthpercentage": return leaf.Compare(Me.Health.At(now) / Math.Max(1, Me.Health.Max));
            case "ifownenergypercentage": return leaf.Compare(Me.Energy.Max <= 0 ? 0 : Me.Energy.At(now) / Me.Energy.Max);
            case "iftargethealthpercentage": return target is not null && leaf.Compare(HealthOf(target, now));
            case "iftargetenergypercentage": return target is not null && leaf.Compare(EnergyOf(target, now));
            case "iftargetrange": return target is not null && leaf.Compare(Vector2.Distance(Me.Position, Position(target)));
            case "ifisanyenemyinrange":
                return map.Players.Values.Any(p => MobCasts.Valid(p, map, now) && Vector2.Distance(Me.Position, p.PositionAt(DateTime.UtcNow)) <= leaf.Range);
            case "ifisanyfactionmemberinrange":
                return map.Objects.Values.OfType<Mob>().Any(m => m != Me && !m.Dead && string.Equals(m.Template.Faction, Me.Template.Faction, StringComparison.OrdinalIgnoreCase)
                    && Vector2.Distance(Me.Position, m.Position) <= leaf.Range);
            case "ifistargettopofthreatlist":
                return target is Player top && Me.Threat.Count > 0 && Me.Threat.MaxBy(t => t.Value).Key == top;
            case "iftotalnumberoftargetsonthreatlist": return leaf.Compare(Me.Threat.Keys.Count(p => MobCasts.Valid(p, map, now)));
            case "ifmobdistancetohome": return leaf.Compare(Vector2.Distance(Me.Position, Me.Home));
            case "ifsummonedmobcount": return leaf.Compare(Summons.Of(map, Me).Count()); // its living adds (stageN spells-bosses)
            case "ifistargetcasting": return target is Player { Combat.Cast: not null } || target is Mob { AI: BehaviourAI { Cast.Finished: false } };
            case "ifistargetchanneling":
                return target is Player { Combat.Channel: not null } || target is Mob { AI: BehaviourAI { Cast: { Finished: true, Channel: not null } } };
            case "iftargettype": return string.Equals((string?)leaf.Element.Attribute("type"), spellTarget is Mob ? "mob" : "player", StringComparison.OrdinalIgnoreCase);
            case "ifvariable": return leaf.Name is { } name && leaf.CompareText(Var(world, name));
            case "ifspellactive":
            {
                // who="Caster": the mob; SpellTarget / EffectTarget: the candidate (R10). The entry of a spell with a timed or
                // permanent sub-effect in force.
                IUnit? who = leaf.Who.Equals("Caster", StringComparison.OrdinalIgnoreCase) ? Me : spellTarget;
                if (who is null) return false;
                who.Effects.Prune(now);
                return who.Effects.Spells.Any(s => string.Equals(s.Spell.Name, leaf.Spell, StringComparison.OrdinalIgnoreCase) && s.Live(now)
                    && (!leaf.CasterChargesOnly || s.Caster == Me.Id) && s.Stacks >= leaf.AtLeastCharges);
            }
            case "ifspellcategoryactive":
            {
                IUnit? who = leaf.Who.Equals("Caster", StringComparison.OrdinalIgnoreCase) ? Me : spellTarget;
                return who is not null && who.Effects.Spells.Any(s => s.Spell.Category == leaf.Category && s.Live(now));
            }
            default:
                // StageN spells-bosses: the spell conditions the <AI> trees share (IfTargetWeaponTypeEquipped, IfVariableOnTarget,
                // IfIsAnyMobWithVariableInRange, IfTargetMobTier, IfTargetCCEffect, IfTargetPositionIsInPursuitRange, …).
                if (SpellConditions.Holds(leaf.Element, new SpellConditions.Facts(Me, spellTarget ?? Me, target ?? Me, map, now, 0)) is { } known) return known;
                Combat.LogOnce("aicond:" + leaf.Kind, $"<AI> condition <{leaf.Tag}> is not supported yet: read as false");
                return false;
        }
    }

    /// <summary>
    /// IfChance (R5): rolled on the map's random at most once per CombatSettings.MobAiChanceReroll, on a think that reaches it; the
    /// roll holds until then (a failed one reads false). <see cref="World.MobAiChance"/> replaces the value (a test hook).
    /// </summary>
    private bool Chance(World world, Map map, AiLeaf leaf)
    {
        if (_rolls.TryGetValue(leaf, out var roll) && world.Now < roll.Until) return roll.Result;
        ChanceRolls++;
        var result = map.Random.NextDouble() < (world.MobAiChance ?? leaf.Number);
        _rolls[leaf] = (world.Now + world.Combat.MobAiChanceReroll, result);
        return result;
    }
}
