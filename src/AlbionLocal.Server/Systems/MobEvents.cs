using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The mobs.xml spells outside the &lt;AI&gt; rotation (stageN spells-bosses, audit item 11; analysis/work/stageN/spells-bosses/SPEC.md §2):
/// the &lt;SpawnSpells&gt; a mob casts on itself when it appears (MOB_IS_BOSS's marker, the traps' invincibility, the hounds' auras), the
/// &lt;DeathSpells&gt; it casts as it dies (the demons' death blasts, the elites' fame buff MOB_ELITE_FAME_BUFF_APPLY, the summoners' adds
/// removal) and the &lt;KillRewardBuff&gt; its threat list gets (the dragons' curse). They run only with World.MobSpells, as the rotation.
/// A cast here is instant: CastSpell {0 mob, 1 target (none for a ground spell), 2 the ground point, 3 spell, 4 = 5 now, 6 counter,
/// 7 0} to its observers, then its effects (<see cref="MobCasts.Execute"/>). Touched only under <see cref="World.Sync"/>.
/// </summary>
public static class MobEvents
{
    /// <summary>The spawn spells of <paramref name="mob"/>, once (the first world tick it has on a map with players).</summary>
    public static void Spawned(World world, Map map, Mob mob)
    {
        mob.SpawnSpellsCast = true;
        if (!world.MobSpells || world.Spells is not { } spells) return;
        foreach (var name in mob.Template.SpawnSpells)
        {
            if (spells.Template(name) is not { } spell)
            {
                Combat.LogOnce("spawnspell:" + name, $"spawn spell {name} of {mob.Template.Name} is not in spells.xml");
                continue;
            }
            var now = world.GameNow;
            MobCasts.Execute(new MobCasts.MobHit(mob, map, spell, 0, mob.Template.AbilityPower, mob, mob.Position, now) { Depth = 1 }, spell);
        }
    }

    /// <summary>
    /// <paramref name="mob"/> died by <paramref name="killer"/>'s hit (<see cref="Combat.Kill"/>, after its AI's death): its death spells at
    /// their targets (self: the mob, a ground spell at its feet; killer; anythreat: a random player of <paramref name="threat"/>, the
    /// threat list before the death cleared it; anyenemy: the nearest valid player; anyfriend: the nearest living mob of its faction),
    /// then its kill reward buffs on every player of <paramref name="threat"/> still on the map.
    /// </summary>
    public static void Died(World world, Map map, Mob mob, Player killer, IReadOnlyList<Player> threat)
    {
        if (!world.MobSpells || world.Spells is not { } spells) return;
        var now = world.GameNow;
        foreach (var (name, target) in mob.Template.DeathSpells)
        {
            if (spells.Template(name) is not { } spell)
            {
                Combat.LogOnce("deathspell:" + name, $"death spell {name} of {mob.Template.Name} is not in spells.xml");
                continue;
            }
            var valid = threat.Where(p => MobCasts.Valid(p, map, now)).ToList();
            IUnit? unit = target switch
            {
                "killer" => MobCasts.Valid(killer, map, now) ? killer : null,
                "anythreat" => valid.Count == 0 ? null : valid[map.Random.Next(valid.Count)],
                "anyenemy" => map.Players.Values.Where(p => MobCasts.Valid(p, map, now))
                    .OrderBy(p => Vector2.DistanceSquared(p.PositionAt(DateTime.UtcNow), mob.Position)).FirstOrDefault(),
                "anyfriend" => map.Objects.Values.OfType<Mob>().Where(m => m != mob && !m.Dead && !m.Removed
                        && string.Equals(m.Template.Faction, mob.Template.Faction, StringComparison.OrdinalIgnoreCase)
                        && Vector2.Distance(m.Position, mob.Position) <= Math.Max(spell.CastRange, 1) + m.Template.CollisionRadius)
                    .OrderBy(m => Vector2.DistanceSquared(m.Position, mob.Position)).FirstOrDefault(),
                _ => mob,
            };
            if (unit is null) continue;
            CastNow(world, map, mob, spell, unit, unit is Player p ? p.PositionAt(DateTime.UtcNow) : unit is Mob m ? m.Position : mob.Position);
        }
        foreach (var (name, _) in mob.Template.KillRewardBuffs)
        {
            if (spells.Template(name) is not { } buff) continue;
            foreach (var p in threat.Where(p => p.Map == map && !p.Combat.Untouchable))
                MobCasts.Execute(new MobCasts.MobHit(mob, map, buff, 0, mob.Template.AbilityPower, p, p.PositionAt(DateTime.UtcNow), now) { Depth = 1 }, buff);
        }
    }

    /// <summary>An instant cast of <paramref name="spell"/> by <paramref name="mob"/> at <paramref name="target"/> (a ground spell: at <paramref name="point"/>).</summary>
    public static void CastNow(World world, Map map, Mob mob, SpellTemplate spell, IUnit target, Vector2 point)
    {
        var now = world.GameNow;
        var counter = mob.NextCastCounter();
        var ground = spell.Target == "ground";
        mob.SendAround(new CastSpellEvent
        {
            ddn = mob.Id, bvw = ground ? 0 : target.Id, bvx = ground ? point : default, bvy = spell.Id, bvz = now, bv0 = now,
            Extra = new() { [6] = counter, [7] = (short)0 },
        });
        Log.Debug("ai", $"{mob} casts {spell} #{counter} at its {(mob.Dead ? "death" : "spawn")}");
        MobCasts.Execute(new MobCasts.MobHit(mob, map, spell, counter, mob.Template.AbilityPower, ground ? null : target, point, now), spell);
    }
}
