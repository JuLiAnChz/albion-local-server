using System.Numerics;
using System.Xml.Linq;
using AlbionLocal.Server;
using AlbionLocal.Server.Bots;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;
using Cls = AlbionLocal.Server.Systems.SpellTargets.Cls;
using Facts = AlbionLocal.Server.Systems.SpellTargets.Facts;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Stage J player spells on players (analysis/work/stageJ/pvp-spells/SPEC.md §4), phases 0 and 1: the client's target table on the
/// server (<see cref="SpellTargets"/>), the PvE data fixes (node classes, the applied spells' kind filter, IfTargetType, the worn crowd
/// control bonus, castablewhenstunned, the caster's id in the cast events), and the friendly spells on other players (heals with the
/// healer's healbonus, Friend and the client's table, duel isolation, friendother, the friendall chain); phase 2, the enemy spells on
/// Hostile players and bots (the cast and its events, DoTs, refusals, stuns and diminishing returns, the attack bookkeeping of an effect
/// without damage, interruptscasting, knockbacks, the killing hit, the mount bar, a radius node, a patrol); phase 3, the areas, channels
/// and drains on players (a one-target skill shot past a neutral and a party mate, removeonleave, interruptcast and the channel's linked
/// root, energy drains, the reflect cap); phase 4, the bots (their scans, the engage hook on a stun, a bot's spells and their PvP rules, the
/// hold after a forced path, a bot casting its gear's spell). Client data; one focus each.
/// </summary>
public static class PvpSpellTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("pvp spells T0.1: SpellTargets.Effect (friendall friend / hostile mob, enemyplayers / enemymobs, friendother self, D4 neither, allplayers self)", Table),
        ("pvp spells T0.2/T0.5: HELL_ARROW_DAMAGE hits a mob once (its enemymobs node), ARCANEORB2_EFFECT silences it with _MOBS only", MobClasses),
        ("pvp spells T0.3: FIREWALL_EFFECT on a mob lists its 3 s spellimmunity (IfTargetType mob), not the players' 0.25 s one", FirewallImmunity),
        ("pvp spells T0.4: a plate caster's worn bonusccdurationvsmobs 0.4 makes HOWL_SLOW on a mob 1.4 times as long", WornCcBonus),
        ("pvp spells T0.6: stunned, HOLYTOUCH (castablewhenstunned) is accepted and FIRESTAFFBOLT2 refused", CastableStunned),
        ("pvp spells T0.7: a friendall radius buff reaches the caster, not the attackable mob beside it", FriendlyRadius),
        ("pvp spells T0.8: a self cast names the caster in CastStart key 7, an all cast on oneself in CastFinished / CastSpell key 1 (an observer's)", SelfCastKey),
        ("pvp spells: an aura of class all (DASHDMG_EFFECT) skips its owner: the SPIRITSPEAR penalty stays after a dash that hits nothing", AuraOwner),
        ("pvp spells T1.1/T1.5: HOLYTOUCH on a Blue friend heals it, × the healer's healbonus, HealthUpdate {6 healer} to both, CastStart without key 2", HealFriend),
        ("pvp spells T1.2/T1.6: consensual, a Red stranger is no Friend: HOLYTOUCH refused, CELESTIAL_SPHERE's chain heals the friend only", Stranger),
        ("pvp spells T1.2: consensual, MAGICCIRCLE's friendall area pulses its buff on the owner and the friend, not on the Red stranger", FriendArea),
        ("pvp spells: ARMOR_WEB (an all area, enemy pulses) leaves a friend's CALTROPS_BUFF on and shows it no CastHit", WebSparesFriend),
        ("pvp spells T1.3/T1.4: a duelist takes no friendly cast (Pvp:DuelIsolation; false: accepted), HEALINGBEAM (friendother) not on oneself", DuelAndSelf),
        ("pvp spells T2.1: Red on Red, consensual: FIRESTAFFBOLT2 at B, CastStart {7 B, no 2}, CastHit then HealthUpdate {5 2, 6 A}, the DoT listed {10 A}, 4 ticks origin 5", BoltOnPlayer),
        ("pvp spells T2.2/T2.7: consensual, a bolt at a Blue player is refused (NotHostile); with Pvp:Spells false at a Red one too, its auto-attack accepted", HostileRefusals),
        ("pvp spells T2.3: a stun on B ends its flag cast, then its cast (CastCancel {1 true}); a second stun 1 s later lasts × (1 − 0.3 × 0.8)", StunOnPlayer),
        ("pvp spells T2.4: official, HOWL_SLOW (no damage) by Red A on Blue B: both in combat, the attack penalty, the slow listed {10 A}", DebuffEngages),
        ("pvp spells: HOWL_EFFECT (interruptscasting) on B cancels its cast; its enemyplayers debuffs are on, the enemymobs ones not", InterruptsCasting),
        ("pvp spells T2.5: a knockback on B: ForcedMovement {0 B, 9 A} to B and A, B's own Moves ignored on the path", KnockbackOnPlayer),
        ("pvp spells T2.6/T2.8: a spell's killing hit knocks B down (KnockedDown {2 A}, A among its attackers); on a mounted B it hits the mount bar", KillingHitAndMount),
        ("pvp spells: SWORD_SPIN_DAMAGE's radius node hits the Red player beside the caster (origin 3), not the Blue one (consensual)", RadiusOnPlayers),
        ("pvp spells: a Blue player's knockback on a Red patrol engages it (it may answer him) and its brain leaves the path alone", SpellOnBot),
        ("pvp spells T3.1: SEARING_FLAME (1 target) flies past Blue C and party mate D: CastHit and damage on Red B only", SkillShotArea),
        ("pvp spells T3.2: GLACIALFIELD (removeonleave) slows Red B; B walks out: the slow comes off, B's list goes out", AreaLeave),
        ("pvp spells T3.3/T3.4: INTERRUPT ends B's channel (ChannelingEnded {1 2}); A's DISEMBOWEL channel ends: its root on B comes off", Channels),
        ("pvp spells: INTERRUPT spares B's cast of a spell with interruptiblebyspell=\"false\" (as on a mob)", SpellProof),
        ("pvp spells: an energy drain takes 10 % from Red B {5 A}, none from Blue C; a harvester's drain channel from a player", EnergyDrain),
        ("pvp spells: a reflect sends back at most 2 × the reflector's maximum health per hit (maxReflectCapacityFactorOfMaxHp)", ReflectCap),
        ("pvp spells T4.1: a Red player's stun without damage engages a patrol bot, UNDEADHAND's area over it hits it", AreaOnBot),
        ("pvp spells T4.2: a patrol bot's UNDEADHAND hits the Red player in it and not the ghoul, its TAUNT pulls no mob; a Blue player's MAGICCIRCLE skips the bot", BotAreaSkips),
        ("pvp spells: a patrol bot's UNDEADHAND spares the Red player it spared after a knockdown, hits the other Red one", BotAreaSpares),
        ("pvp spells: a gatherer bot's spells reach only a player it answers (engaged), not another Red one", GathererEngagedOnly),
        ("pvp spells T4.3: a knocked-back patrol bot sends no Move before its path's end + 300 ms, then fights on from there", BotHeldAfterPath),
        ("pvp spells: a patrol bot's own gear has a spell in Q, W, E, armour, helmet and shoes (a full choice, SPEC §2.9)", BotFullKit),
        ("pvp spells: Bots:Combat:Spells Full, a patrol bot casts its FIRESTAFFBOLT2 at a Red player: CastStart {0 bot, 7 player}, HealthUpdate {6 bot, 7 bolt}", BotCasts),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static SpellTemplate T(string name) => Client.Spells!.Template(name) ?? throw new Exception($"{name} not in spells.xml");

    private static bool Is(Dictionary<byte, object?> p, long id) => p.TryGetValue(0, out var v) && v is IConvertible && Convert.ToInt64(v) == id;

    /// <summary>A yellow zone (PvpAllowed) without lethal hits.</summary>
    private static readonly ClusterPvp Yellow = new(PvpRule.PvpAllowed, PlayersAreLethal: false);

    private static ServerConfig Config(PvpMode mode = PvpMode.Official) => new() { JoinByCharacterId = true, PvpMode = mode };

    /// <summary>Runs <paramref name="spell"/> as <paramref name="me"/>'s hit now on <paramref name="target"/> (null = itself).</summary>
    private static void Hit(Arena arena, Player me, SpellTemplate spell, IUnit? target) => Hit(arena.World, me, spell, target);

    private static void Hit(World world, Player me, SpellTemplate spell, IUnit? target)
    {
        lock (world.Sync)
        {
            var now = world.GameNow;
            SpellCasts.Execute(new SpellCasts.SpellContext(me, me.Map!, spell, 1, 200, target, target is null ? me.PositionAt(DateTime.UtcNow) : SpellMovement.PositionOf(target, now), now), spell);
        }
    }

    /// <summary>Runs <paramref name="spell"/> as <paramref name="me"/>'s hit now at the ground point <paramref name="point"/> (no unit).</summary>
    private static void HitAt(Arena arena, Player me, SpellTemplate spell, Vector2 point)
    {
        lock (arena.World.Sync)
            SpellCasts.Execute(new SpellCasts.SpellContext(me, me.Map!, spell, 1, 200, null, point, arena.World.GameNow), spell);
    }

    /// <summary>Puts <paramref name="p"/> at the stand + <paramref name="offset"/>, facing +x.</summary>
    private static void Place(Arena arena, Player p, Vector2 offset)
    {
        lock (arena.World.Sync) p.Teleport(arena.Stand + offset, 90, DateTime.UtcNow);
    }

    /// <summary>The ids in a long[] key (ActiveSpellEffectsUpdate key 10), empty without it.</summary>
    private static long[] Ids(Dictionary<byte, object?> p, byte k) => p.GetValueOrDefault(k) is Array a ? [.. a.Cast<object>().Select(Convert.ToInt64)] : [];

    /// <summary>Two Red players A and B (and with <paramref name="blue"/> a Blue C) at the stand of an arena under <paramref name="mode"/>.</summary>
    private static (Arena Arena, (WorldSession Session, FakePeer Peer, Player Player) A, (WorldSession Session, FakePeer Peer, Player Player) B, Player? C) Reds(PvpMode mode, bool blue = false,
        ServerConfig? config = null, Action<Character>? setupB = null)
    {
        var arena = new Arena(config: config ?? Config(mode), pvp: Yellow);
        arena.Setup = c => c.Flag = Pvp.Red;
        var a = arena.Join("Ann", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = c =>
        {
            c.Flag = Pvp.Red;
            setupB?.Invoke(c);
        };
        var b = arena.Join("Bob", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = null;
        var c = blue ? arena.Join("Cid", new InventoryItem("T4_MAIN_SWORD")).Player : null;
        return (arena, a, b, c);
    }

    /// <summary>A CastStart of <paramref name="spell"/> in slot 0 at <paramref name="target"/> (0 = none) checked as the handler does, started when accepted.</summary>
    private static (PendingCast? Cast, CastRefusal Why) Cast(Arena arena, Player me, SpellTemplate spell, long target, byte counter = 1)
    {
        lock (arena.World.Sync)
        {
            var slots = new int[14];
            slots[0] = spell.Id;
            var (cast, why) = SpellCasts.Check(me, slots, 0, spell.Id, target, me.PositionAt(DateTime.UtcNow), counter, arena.World.GameNow);
            if (cast is not null) SpellCasts.Start(me, cast);
            return (cast, why);
        }
    }

    private static void Table()
    {
        var friend = new Facts(Self: false, Hostile: false, Friend: true, Player: true, Targetable: true, false, false, false, false, false, false);
        var hostilePlayer = friend with { Friend = false, Hostile = true };
        var mob = new Facts(Self: false, Hostile: true, Friend: false, Player: false, Targetable: true, false, false, false, false, false, false);
        var self = friend with { Self = true };
        var neither = friend with { Friend = false };
        Check(SpellTargets.Effect(Cls.FriendAll, friend) && !SpellTargets.Effect(Cls.FriendAll, hostilePlayer) && !SpellTargets.Effect(Cls.EnemyPlayers, mob)
              && SpellTargets.Effect(Cls.EnemyMobs, mob) && !SpellTargets.Effect(Cls.FriendOther, self) && !SpellTargets.Effect(Cls.All, neither)
              && SpellTargets.Effect(Cls.AllPlayers, self) && !SpellTargets.Effect(Cls.All, mob with { Targetable = false })
              && SpellTargets.Parse("friendallplayers") == Cls.FriendAllPlayers && (byte)Cls.FriendAllPlayers == 24 && SpellTargets.Parse("") == Cls.None,
              "FriendAll(friend) true, (hostile) false; EnemyPlayers(mob) false, EnemyMobs(mob) true; FriendOther(self) false; All(neither, untargetable mob) false; AllPlayers(self) true; friendallplayers = 24");
    }

    private static void MobClasses()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, peer, me) = arena.Join("Archer", new InventoryItem("T4_MAIN_SWORD"));
        var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(3, 0));
        var arrow = T("HELL_ARROW_DAMAGE");
        var mark = peer.Events.Count;
        Hit(arena, me, arrow, mob);
        var hits = Since(peer, mark).Count(e => e.Code == EventCodes.HealthUpdate && Is(e.P, mob.Id) && Convert.ToInt16(e.P[7]) == arrow.Id);
        Check(hits == 1, $"HELL_ARROW_DAMAGE on a mob: one HealthUpdate (enemymobs −391), not the enemyplayers node's too (got {hits})");
        var orb = T("ARCANEORB2_EFFECT");
        var other = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(-6, 0)); // a fresh mob, out of the arrow's way
        Hit(arena, me, orb, other);
        bool Has(string name) => other.Effects.Spells.Any(s => s.Spell.Name == name);
        Check(Has("ARCANEORB2_SILENCE_MOBS") && !Has("ARCANEORB2_SILENCE_PLAYERS"),
              $"ARCANEORB2_EFFECT on a mob: ARCANEORB2_SILENCE_MOBS and not _PLAYERS (got {string.Join(", ", other.Effects.Spells.Select(s => s.Spell.Name))})");
    }

    private static void FirewallImmunity()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, _, me) = arena.Join("Mage", new InventoryItem("T4_MAIN_SWORD"));
        var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(3, 0));
        var fire = T("FIREWALL_EFFECT");
        Hit(arena, me, fire, mob);
        arena.Run(1);
        bool immune;
        lock (world.Sync) immune = SpellEffects.Immune(mob, fire, world.GameNow);
        Check(!mob.Immune && immune, "FIREWALL_EFFECT on a mob: 1 s later it is still immune to it (the 3 s mob immunity, not the 0.25 s player one)");
    }

    private static void WornCcBonus()
    {
        var howl = T("HOWL_SLOW");
        int Duration(bool plate)
        {
            using var arena = new Arena { Setup = c => { if (!plate) c.Equipment[3] = null; } };
            var world = arena.World;
            var (_, _, me) = arena.Join(plate ? "Plate" : "Cloth", new InventoryItem("T4_MAIN_SWORD"));
            var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(4, 0));
            lock (world.Sync) return SpellEffects.Make(world, me, 100, 157.9, howl.Effects[0], 0, mob, world.GameNow)?.DurationMs ?? 0;
        }
        var (bare, plated) = (Duration(false), Duration(true));
        Check(bare == 2947 && Math.Abs(plated - 2947 * 1.4) <= 1, $"HOWL_SLOW on the soldier: 2947 ms bare, × (1 + 0.4) with T4_ARMOR_PLATE_SET1 worn (got {bare}, {plated} ms)");
    }

    private static void CastableStunned()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, _, me) = arena.Join("Cleric", new InventoryItem("T4_MAIN_SWORD"));
        var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(3, 0));
        var stun = T("UNDEADHAND_STUN");
        lock (world.Sync)
            SpellEffects.Apply(world, me, stun, mob.Id, 100, 100, world.GameNow, [SpellEffects.Make(world, mob, 100, 100, stun.Effects[0], 0, me, world.GameNow)]);
        var (_, touch) = Cast(arena, me, T("HOLYTOUCH"), me.Id);
        var (_, bolt) = Cast(arena, me, T("FIRESTAFFBOLT2"), mob.Id, 2);
        Check(SpellEffects.Stunned(me, world.GameNow) && touch == CastRefusal.None && bolt == CastRefusal.Stunned,
              $"stunned: HOLYTOUCH accepted, FIRESTAFFBOLT2 refused as Stunned (got {touch}, {bolt})");
    }

    private static void FriendlyRadius()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, _, me) = arena.Join("Bard", new InventoryItem("T4_MAIN_SWORD"));
        var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(2, 0));
        var xml = new XElement("activespell", new XAttribute("target", "self"),
            new XElement("buffovertime", new XAttribute("target", "friendall"), new XAttribute("type", "movespeedbonus"), new XAttribute("value", "0.2"),
                new XAttribute("time", "5"), new XAttribute("effectarearadius", "6")));
        var song = SpellTemplate.From(T("HOLYTOUCH_EFFECT").Id, "TEST_FRIENDALL_RADIUS_BUFF", "activespell", xml);
        Hit(arena, me, song, null);
        Check(me.Effects.Find(song.Id, me.Id) is not null && mob.Effects.Count == 0,
              $"a friendall radius buff: on the caster, the soldier 2 m away has none (caster {me.Effects.Count}, mob {mob.Effects.Count})");
    }

    private static void SelfCastKey()
    {
        using var arena = new Arena();
        var (_, _, me) = arena.Join("Runner", new InventoryItem("T4_MAIN_SWORD"));
        var (_, watcher, _) = arena.Join("Watcher", new InventoryItem("T4_MAIN_SWORD"));
        var mark = watcher.Events.Count;
        var (cast, why) = Cast(arena, me, T("DEFENSERUN"), me.Id);
        var start = Since(watcher, mark).FirstOrDefault(e => e.Code == EventCodes.CastStart && Is(e.P, me.Id)).P;
        Check(cast is not null && start is not null && L(start, 7) == me.Id, $"DEFENSERUN (self): CastStart key 7 = the caster ({why}; got {(start is null ? "none" : Show([(EventCodes.CastStart, start)]))})");
        mark = watcher.Events.Count;
        var (all, allWhy) = Cast(arena, me, T("DYNAMIC_DEFENSE"), me.Id, 2);
        var events = Since(watcher, mark).Where(e => Is(e.P, me.Id)).ToList();
        var finished = events.FirstOrDefault(e => e.Code == EventCodes.CastFinished).P;
        var spell = events.FirstOrDefault(e => e.Code == EventCodes.CastSpell).P;
        Check(all is not null && finished is not null && spell is not null && L(finished, 1) == me.Id && L(spell, 1) == me.Id,
              $"DYNAMIC_DEFENSE (all) on oneself: CastFinished key 1 and CastSpell key 1 = the caster ({allWhy}; got {Show(events.Take(4))})");
    }

    private static void AuraOwner()
    {
        using var arena = new Arena();
        var (_, _, me) = arena.Join("Spear", new InventoryItem("T4_MAIN_SWORD"));
        Hit(arena, me, T("SPIRITSPEAR_PENALTY"), null);
        var before = me.Effects.Spells.Any(s => s.Spell.Name == "SPIRITSPEAR_PENALTY");
        var (cast, why) = Cast(arena, me, T("DASHDMG"), 0);
        arena.Run(1);
        var after = me.Effects.Spells.Any(s => s.Spell.Name == "SPIRITSPEAR_PENALTY");
        Check(cast is not null && before && after, $"DASHDMG beside only the guard tower ({why}): SPIRITSPEAR_PENALTY before {before}, after the dash {after} (want both)");
    }

    private static void HealFriend()
    {
        using var arena = new Arena(config: Config(), pvp: Yellow);
        var world = arena.World;
        var (_, healerPeer, healer) = arena.Join("Healer", new InventoryItem("T4_MAIN_SWORD"));
        var (_, friendPeer, friend) = arena.Join("Friend", new InventoryItem("T4_MAIN_SWORD"));
        lock (world.Sync) friend.Health.Set(friend.Health.Max - 600, world.GameNow);
        var full = healer.Health.At(world.GameNow);
        var mark = (healerPeer.Events.Count, friendPeer.Events.Count);
        var (cast, why) = Cast(arena, healer, T("HOLYTOUCH"), friend.Id);
        arena.Run(0.5);
        var effect = T("HOLYTOUCH_EFFECT");
        bool Heal((EventCodes Code, Dictionary<byte, object?> P) e) =>
            e.Code == EventCodes.HealthUpdate && Is(e.P, friend.Id) && L(e.P, 6) == healer.Id && Convert.ToInt16(e.P[7]) == effect.Id && F(e.P, 2) > 0;
        var heals = Since(friendPeer, mark.Item2).Where(Heal).ToList();
        var start = Since(friendPeer, mark.Item2).FirstOrDefault(e => e.Code == EventCodes.CastStart && Is(e.P, healer.Id)).P;
        var expected = Combat.Rounded(Combat.Heal(55, cast?.AbilityPower ?? 0) * 1.1); // T4_ARMOR_PLATE_SET1 healbonus 0.1
        Check(cast is not null && heals.Count > 0 && Math.Abs(F(heals[0].P, 2) - expected) <= 0.5f && Since(healerPeer, mark.Item1).Any(Heal)
              && healer.Health.At(world.GameNow) == full && Combat.HealBonusFactor(healer, world.GameNow) == 1.1
              && start is not null && L(start, 7) == friend.Id && !start.ContainsKey(2),
              $"HOLYTOUCH at the friend ({why}): CastStart {{7 friend, no 2}}, HealthUpdate {{0 friend, 2 +{expected}, 6 healer, 7 HOLYTOUCH_EFFECT}} to both, the healer's own health unchanged (got {Show(heals.Take(2))}{(start is null ? "" : ", " + Show([(EventCodes.CastStart, start)]))})");
    }

    private static void Stranger()
    {
        using var arena = new Arena(config: Config(PvpMode.Consensual), pvp: Yellow);
        var world = arena.World;
        var (_, _, healer) = arena.Join("Healer", new InventoryItem("T4_MAIN_SWORD"));
        var (_, friendPeer, friend) = arena.Join("Friend", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = c => c.Flag = Pvp.Red;
        var (_, _, red) = arena.Join("Red", new InventoryItem("T4_MAIN_SWORD"));
        var (_, why) = Cast(arena, healer, T("HOLYTOUCH"), red.Id);
        Check(Pvp.Of(healer, red) == Relation.Neutral && why == CastRefusal.NotFriend,
              $"consensual: the Red stranger is Neutral to the Blue healer, but Hostile in the client's table: HOLYTOUCH refused as NotFriend (got {why})");
        lock (world.Sync)
            foreach (var p in new[] { healer, friend, red }) p.Health.Set(p.Health.Max - 600, world.GameNow);
        var mark = friendPeer.Events.Count;
        var (sphere, sphereWhy) = Cast(arena, healer, T("CELESTIAL_SPHERE"), friend.Id, 2);
        arena.Run(3);
        var effect = T("CELESTIAL_SPHERE_EFFECT").Id;
        var healed = Since(friendPeer, mark).Where(e => e.Code == EventCodes.HealthUpdate && Convert.ToInt16(e.P[7]) == effect && F(e.P, 2) > 0)
            .Select(e => L(e.P, 0)).ToHashSet();
        Check(sphere is not null && healed.Contains(friend.Id) && healed.Contains(healer.Id) && !healed.Contains(red.Id),
              $"CELESTIAL_SPHERE (chaintarget friendall) at the friend ({sphereWhy}): its heals reach the friend and the healer, never the Red stranger beside them (healed {string.Join(", ", healed)})");
    }

    private static void FriendArea()
    {
        using var arena = new Arena(config: Config(PvpMode.Consensual), pvp: Yellow);
        var (_, _, healer) = arena.Join("Healer", new InventoryItem("T4_MAIN_SWORD"));
        var (_, _, friend) = arena.Join("Friend", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = c => c.Flag = Pvp.Red;
        var (_, _, red) = arena.Join("Red", new InventoryItem("T4_MAIN_SWORD"));
        var (cast, why) = Cast(arena, healer, T("MAGICCIRCLE"), healer.Id);
        arena.Run(1);
        bool Has(Player p) => p.Effects.Spells.Any(s => s.Spell.Name == "MAGICCIRCLE_EFFECT");
        Check(cast is not null && Has(healer) && Has(friend) && !Has(red),
              $"MAGICCIRCLE ({why}) 1 s on: MAGICCIRCLE_EFFECT on the owner {Has(healer)}, the friend {Has(friend)}, the Red stranger {Has(red)} (want true, true, false)");
    }

    private static void WebSparesFriend()
    {
        using var arena = new Arena(config: Config(), pvp: Yellow);
        var (_, _, me) = arena.Join("Weaver", new InventoryItem("T4_MAIN_SWORD"));
        var (_, friendPeer, friend) = arena.Join("Friend", new InventoryItem("T4_MAIN_SWORD"));
        Hit(arena, friend, T("CALTROPS_BUFF"), null);
        var mark = friendPeer.Events.Count;
        var (cast, why) = Cast(arena, me, T("ARMOR_WEB"), me.Id);
        arena.Run(1);
        var buff = friend.Effects.Spells.Any(s => s.Spell.Name == "CALTROPS_BUFF");
        var hits = Since(friendPeer, mark).Count(e => e.Code is EventCodes.CastHit or EventCodes.CastHits && Is(e.P, friend.Id));
        Check(cast is not null && SpellTargets.Friend(me, friend, arena.World.GameNow) && buff && hits == 0,
              $"ARMOR_WEB ({why}) around a friend: its CALTROPS_BUFF stays {buff}, CastHit(s) naming it {hits} (want 0)");
    }

    private static void DuelAndSelf()
    {
        CastRefusal Heal(bool isolation, bool self)
        {
            using var arena = new Arena(config: Config() with { PvpDuelIsolation = isolation }, pvp: Yellow);
            var world = arena.World;
            var (_, _, healer) = arena.Join("Healer", new InventoryItem("T4_MAIN_SWORD"));
            var (_, _, duelist) = arena.Join("Duelist", new InventoryItem("T4_MAIN_SWORD"));
            var (_, _, rival) = arena.Join("Rival", new InventoryItem("T4_MAIN_SWORD"));
            lock (world.Sync)
            {
                world.Duels.Challenge(duelist, rival.Id, 0);
                world.Duels.Accept(rival);
            }
            Check(world.Duels.Of(duelist) is not null, "the duel is on");
            return self ? Cast(arena, healer, T("HEALINGBEAM"), healer.Id, 2).Why : Cast(arena, healer, T("HOLYTOUCH"), duelist.Id).Why;
        }
        var (isolated, open) = (Heal(true, false), Heal(false, false));
        Check(isolated == CastRefusal.InDuel && open == CastRefusal.None,
              $"a heal at a duelist: refused as InDuel, with Pvp:DuelIsolation false accepted (got {isolated}, {open})");
        var beam = Heal(true, true);
        Check(beam == CastRefusal.NoTarget, $"HEALINGBEAM (friendother) with key 5 = the caster: refused (got {beam})");
    }

    // ---- phase 2: enemy spells on players ----

    private static void BoltOnPlayer()
    {
        var (arena, (_, _, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var bolt = T("FIRESTAFFBOLT2");
        var mark = bPeer.Events.Count;
        var (cast, why) = Cast(arena, a, bolt, b.Id);
        arena.Run(1); // 0.8 s cast, 0.12 s hit delay
        var got = Since(bPeer, mark);
        var start = got.FirstOrDefault(e => e.Code == EventCodes.CastStart && Is(e.P, a.Id)).P;
        var at = got.FindIndex(e => e.Code == EventCodes.CastHit && Is(e.P, b.Id));
        Check(cast is not null && start is not null && L(start, 7) == b.Id && !start.ContainsKey(2), $"the bolt at B is accepted ({why}): CastStart {{7 B, no 2}}");
        Check(at >= 0 && L(got[at].P, 1) == a.Id && L(got[at].P, 2) == bolt.Id && L(got[at].P, 3) == 1 && at + 1 < got.Count
              && got[at + 1] is { Code: EventCodes.HealthUpdate } update && Is(update.P, b.Id) && L(update.P, 5) == (byte)HitOrigin.Direct && L(update.P, 6) == a.Id
              && F(update.P, 2) < 0,
              $"CastHit {{0 B, 1 A, 2 bolt, 3 1}} right before HealthUpdate {{5 2, 6 A}} ({Show(got.Skip(Math.Max(0, at)).Take(2))})");
        Check(got.Any(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && Is(e.P, b.Id) && Ids(e.P, 1).Contains(bolt.Id) && Ids(e.P, 10).Contains(a.Id)),
              "B's list has the bolt's DoT with key 10 = A");
        arena.Run(4);
        var ticks = Since(bPeer, mark).Count(e => e.Code == EventCodes.HealthUpdate && Is(e.P, b.Id) && L(e.P, 5) == (byte)HitOrigin.OverTime && L(e.P, 6) == a.Id);
        Check(ticks == 4, $"the DoT ticks 4 times on B with origin 5 (got {ticks})");
    }

    private static void HostileRefusals()
    {
        var bolt = T("FIRESTAFFBOLT2");
        var (arena, (_, _, a), _, c) = Reds(PvpMode.Consensual, blue: true);
        using (arena)
        {
            var (_, why) = Cast(arena, a, bolt, c!.Id);
            Check(why == CastRefusal.NotHostile, $"consensual: the bolt of Red A at Blue C is refused as NotHostile (got {why})");
        }
        var (off, (session, aPeer, a2), (_, _, b2), _) = Reds(PvpMode.Consensual, config: Config(PvpMode.Consensual) with { PvpSpells = false });
        using (off)
        {
            var (_, offWhy) = Cast(off, a2, bolt, b2.Id);
            var answers = aPeer.Responses.Count;
            Send(session, new AttackStartRequest { wf = off.World.GameNow, co = b2.Id, cp = 0 });
            Check(offWhy == CastRefusal.NotHostile && aPeer.Responses.Count == answers,
                  $"Pvp:Spells false: the bolt at Red B is refused as NotHostile ({offWhy}), A's AttackStart on B is accepted (no response)");
        }
    }

    private static void StunOnPlayer()
    {
        using var arena = new Arena(config: Config(), pvp: Yellow);
        var world = arena.World;
        arena.Setup = c => c.Flag = Pvp.Red;
        var (_, _, a) = arena.Join("Ann", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = null;
        var (_, bPeer, b) = arena.Join("Bob", new InventoryItem("T4_MAIN_SWORD"));
        var stun = T("UNDEADHAND_STUN");
        short flagged;
        lock (world.Sync) flagged = Flagging.Prepare(b, Pvp.Red);
        Hit(arena, a, stun, b);
        var first = b.Effects.Find(stun.Id, a.Id)?.Subs[0].DurationMs ?? 0;
        Check(flagged == 0 && b.PvpState.FlagCast is null && first > 0, $"official: Blue B flags Red ({flagged}), Red A's stun ({first} ms) ends the flag cast");
        arena.Run(1);
        Hit(arena, a, stun, b);
        var second = b.Effects.Find(stun.Id, a.Id)?.Subs[0].DurationMs ?? 0;
        Check(Math.Abs(second - first * (1 - 0.3 * 0.8)) <= 1, $"the second stun 1 s later: {second} ms = {first} × 0.76 (diminishing returns)");
        arena.Run(second / 1000.0 + 0.1);
        var (cast, why) = Cast(arena, b, T("FIRESTAFFBOLT2"), a.Id, 2);
        var mark = bPeer.Events.Count;
        Hit(arena, a, stun, b);
        var cancel = Since(bPeer, mark).FirstOrDefault(e => e.Code == EventCodes.CastCancel && Is(e.P, b.Id)).P;
        Check(cast is not null && cancel is not null && cancel[1] is true && L(cancel, 2) == 2 && b.Combat.Cast is null,
              $"B's bolt ({why}) under way: the third stun sends it CastCancel {{1 true, 2 2}}");
    }

    private static void DebuffEngages()
    {
        using var arena = new Arena(config: Config(), pvp: Yellow);
        var world = arena.World;
        arena.Setup = c => c.Flag = Pvp.Red;
        var (_, _, a) = arena.Join("Ann", new InventoryItem("T4_MAIN_SWORD"));
        arena.Setup = null;
        var (_, bPeer, b) = arena.Join("Bob", new InventoryItem("T4_MAIN_SWORD"));
        var slow = T("HOWL_SLOW");
        var mark = bPeer.Events.Count;
        Check(!a.Combat.InCombat && !b.Combat.InCombat && a.Character.Reputation == 0, "nobody in combat, A's reputation 0");
        Hit(arena, a, slow, b);
        var listed = Since(bPeer, mark).Any(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && Is(e.P, b.Id) && Ids(e.P, 1).Contains(slow.Id) && Ids(e.P, 10).Contains(a.Id));
        Check(a.Combat.InCombat && b.Combat.InCombat && a.Character.Reputation < 0 && listed && b.Health.At(world.GameNow) == b.Health.Max,
              $"HOWL_SLOW on Blue B: both in combat, A's attack penalty (reputation {a.Character.Reputation}), the slow listed {{10 A}}, no damage");
    }

    private static void InterruptsCasting()
    {
        var (arena, (_, _, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var howl = T("HOWL_EFFECT");
        var (cast, why) = Cast(arena, b, T("FIRESTAFFBOLT2"), a.Id, 3);
        var mark = bPeer.Events.Count;
        Hit(arena, a, howl, b);
        var cancel = Since(bPeer, mark).FirstOrDefault(e => e.Code == EventCodes.CastCancel && Is(e.P, b.Id)).P;
        var on = b.Effects.Find(howl.Id, a.Id)?.Subs.Where(s => s.Active).Select(s => (string?)s.Node.Attribute("target")).ToList() ?? [];
        Check(cast is not null && cancel is not null && cancel[1] is true && b.Combat.Cast is null && on is ["enemyplayers", "enemyplayers", "enemyplayers", "enemyplayers"],
              $"B's bolt ({why}) is cancelled by HOWL_EFFECT's hit; its active debuffs by class: {string.Join(", ", on)} (want 4 × enemyplayers)");
    }

    private static void KnockbackOnPlayer()
    {
        var (arena, (_, aPeer, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var world = arena.World;
        var knock = T("MOUNTSPELL_BIGCLEAVE_KNOCKBACK");
        var mark = (aPeer.Events.Count, bPeer.Events.Count);
        Hit(arena, a, knock, b);
        bool Push((EventCodes Code, Dictionary<byte, object?> P) e) => e.Code == EventCodes.ForcedMovement && Is(e.P, b.Id) && L(e.P, 9) == a.Id && L(e.P, 1) == 2;
        bool ignored;
        lock (world.Sync) ignored = SpellMovement.Moving(b, world.GameNow) && !SpellMovement.AcceptMove(b, 0, world.GameNow);
        Check(Since(bPeer, mark.Item2).Any(Push) && Since(aPeer, mark.Item1).Any(Push) && ignored,
              $"ForcedMovement {{0 B, 1 2, 9 A}} to B and A; on the path a Move of B without the echo is ignored ({ignored})");
    }

    private static void KillingHitAndMount()
    {
        var bolt = T("FIRESTAFFBOLT2");
        var (arena, (_, _, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using (arena)
        {
            var world = arena.World;
            lock (world.Sync) b.Health.Set(5, Math.Max(world.GameNow, b.Health.Stamp));
            var mark = bPeer.Events.Count;
            Hit(arena, a, bolt, b);
            var down = Since(bPeer, mark).FirstOrDefault(e => e.Code == EventCodes.KnockedDown && Is(e.P, b.Id)).P;
            Check(b.Combat.Down is { Kind: KnockdownKind.Pvp } && down is not null && L(down, 2) == a.Id && Death.Attackers(b).Any(x => x.Player == a),
                  "the bolt's killing hit: B is down by A (KnockedDown {2 A}), A among its attackers");
        }
        var (rider, (_, _, a2), (session, peer2, b2), _) = Reds(PvpMode.Consensual, setupB: c => c.Equipment[Mounts.MountSlot] = new InventoryItem("T5_MOUNT_OX"));
        using (rider)
        {
            var world = rider.World;
            Send(session, new MountRequest { wf = world.GameNow, v3 = Mounts.ItemOf(b2)!.Id, v4 = true });
            rider.Run(3.1);
            var (health, bar) = (b2.Health.At(world.GameNow), b2.MountHealth.At(world.GameNow));
            var mark = peer2.Events.Count;
            Hit(rider, a2, bolt, b2);
            var got = Since(peer2, mark);
            Check(b2.IsMounted && b2.MountHealth.At(world.GameNow) < bar && b2.Health.At(world.GameNow) == health
                  && got.Any(e => e.Code == EventCodes.MountHealthUpdate && L(e.P, 6) == a2.Id) && !got.Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, b2.Id)),
                  $"mounted B: the bolt hits the mount bar (312 {{6 A}}), not the health ({Show(got.Where(e => e.Code is EventCodes.MountHealthUpdate or EventCodes.HealthUpdate))})");
        }
    }

    private static void RadiusOnPlayers()
    {
        var (arena, (_, _, a), (_, bPeer, b), c) = Reds(PvpMode.Consensual, blue: true);
        using var held = arena;
        var world = arena.World;
        var mark = bPeer.Events.Count;
        Hit(arena, a, T("SWORD_SPIN_DAMAGE"), null);
        var hit = Since(bPeer, mark).Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, b.Id) && L(e.P, 5) == (byte)HitOrigin.Area && L(e.P, 6) == a.Id);
        Check(hit && c!.Health.At(world.GameNow) == c.Health.Max && a.Health.At(world.GameNow) == a.Health.Max,
              "a spin's damage pulse: Red B takes it (HealthUpdate {5 3, 6 A}), Blue C and the caster none");
    }

    private static void SpellOnBot()
    {
        var options = new BotOptions { Enabled = true, ThinkSeconds = (0.3, 0.3), JoinGraceSeconds = 0, RouteDwellSeconds = (300, 300), RouteEndDwellSeconds = (300, 300) };
        using var f = new BotTest.Field(options, BotTest.NoPopulation, new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false, WarningType: "yellow"));
        var nav = f.Nav();
        var at = nav.Exits[0].Arrival;
        var (_, _, blue) = f.Join("Bern", at + new Vector2(2, 0));
        Player bot;
        lock (f.World.Sync)
        {
            bot = f.World.Bots.Spawn(f.Map, BotRole.Patrol, 4, at, 0) ?? throw new InvalidOperationException("no patrol spawned");
            if (bot.IsMounted) Mounts.Dismount(bot);
        }
        var before = Pvp.Of(bot, blue);
        Hit(f.World, blue, T("MOUNTSPELL_BIGCLEAVE_KNOCKBACK"), bot);
        f.Run(0.6);
        var forced = bot.Effects.Forced;
        var drift = forced is null ? float.MaxValue : Vector2.Distance(bot.PositionAt(f.World.Bots.Time), forced.At(f.World.GameNow));
        Check(before == Relation.Neutral && Pvp.Of(bot, blue) == Relation.Hostile && forced is { Kind: 2 } && drift < 0.2f,
              $"the knockback engages the patrol (Neutral → {Pvp.Of(bot, blue)}); 0.6 s on it is still on its path ({drift:0.##} m off)");
    }

    // ---- phase 4: bots ----

    /// <summary>A yellow zone for the bot fields (patrols walk there; the pve preset's consensual mode).</summary>
    private static readonly ClusterPvp BotYellow = new(PvpRule.PvpAllowed, PlayersAreLethal: false, WarningType: "yellow");

    /// <summary>Patrols that dwell on their route (no walk of their own) and attack on sight at once; bot spells as given.</summary>
    private static BotOptions Patrols(BotSpells spells = BotSpells.Off) => new()
    {
        Enabled = true, ThinkSeconds = (0.3, 0.3), JoinGraceSeconds = 0, RouteDwellSeconds = (300, 300), RouteEndDwellSeconds = (300, 300),
        Spells = spells, SpellChance = 1, ReactionMs = 0,
    };

    /// <summary>A bot of <paramref name="role"/> at <paramref name="at"/> on the field's map, on foot.</summary>
    private static Player Bot(BotTest.Field f, Vector2 at, BotRole role = BotRole.Patrol)
    {
        lock (f.World.Sync)
        {
            var bot = f.World.Bots.Spawn(f.Map, role, 4, at, 0) ?? throw new InvalidOperationException($"no {role} spawned");
            if (bot.IsMounted) Mounts.Dismount(bot);
            return bot;
        }
    }

    /// <summary>Runs <paramref name="spell"/> as <paramref name="me"/>'s hit now at the ground point <paramref name="point"/> (no unit).</summary>
    private static void HitAt(World world, Player me, SpellTemplate spell, Vector2 point)
    {
        lock (world.Sync)
            SpellCasts.Execute(new SpellCasts.SpellContext(me, me.Map!, spell, 1, 200, null, point, world.GameNow), spell);
    }

    private static void AreaOnBot()
    {
        using var f = new BotTest.Field(Patrols(), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, peer, red) = f.Join("Rhea", at + new Vector2(2, 0), Pvp.Red);
        var bot = Bot(f, at);
        Hit(f.World, red, T("UNDEADHAND_STUN"), bot);
        var now = f.World.GameNow;
        var engaged = bot.Bot!.Fight.Engaged.ContainsKey(red.Id) && SpellEffects.Stunned(bot, now) && bot.Health.At(now) == bot.Health.Max;
        var mark = peer.Events.Count;
        HitAt(f.World, red, T("UNDEADHAND"), bot.PositionAt(f.World.Bots.Time));
        var hurt = Since(peer, mark).Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, bot.Id) && L(e.P, 6) == red.Id && F(e.P, 2) < 0);
        Check(engaged && hurt, $"the stun alone engages the patrol (Bots.Attacked through Pvp.Engage) {engaged}; the 2.5 m area over it hits it (HealthUpdate {{6 A}}) {hurt}");
    }

    private static void BotAreaSkips()
    {
        using var f = new BotTest.Field(Patrols(), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, redPeer, red) = f.Join("Rhea", at + new Vector2(1, 0), Pvp.Red);
        var (_, _, blue) = f.Join("Bern", at + new Vector2(-1, 0));
        var bot = Bot(f, at);
        Mob ghoul;
        lock (f.World.Sync) ghoul = SpawnManager.SpawnAt(f.Map, Client.Clusters!.Mobs.Find("T4_MOB_ROAMING_UNDEAD_GHOUL")!, at + new Vector2(0, 1), 0, f.World);
        var mark = redPeer.Events.Count;
        HitAt(f.World, bot, T("UNDEADHAND"), at);
        var hit = Since(redPeer, mark).Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, red.Id) && L(e.P, 6) == bot.Id && F(e.P, 2) < 0);
        Hit(f.World, bot, T("TAUNT"), red); // changeaggro self 200, aggroradius 5: the ghoul 1 m off
        var spared = ghoul.Health.At(f.World.GameNow) == ghoul.Health.Max && !ghoul.Effects.Spells.Any() && !ghoul.Threat.ContainsKey(bot);
        var spell = T("MAGICCIRCLE");
        (PendingCast? Cast, CastRefusal Why) circle;
        lock (f.World.Sync)
        {
            var slots = Enumerable.Repeat(-1, 14).ToArray();
            slots[0] = spell.Id;
            circle = SpellCasts.Check(blue, slots, 0, spell.Id, blue.Id, blue.PositionAt(DateTime.UtcNow), 1, f.World.GameNow);
            if (circle.Cast is not null) SpellCasts.Start(blue, circle.Cast);
        }
        f.Run(1);
        bool Has(Player p) => p.Effects.Spells.Any(s => s.Spell.Name == "MAGICCIRCLE_EFFECT");
        Check(hit && spared && circle.Cast is not null && Has(blue) && !Has(bot),
              $"the bot's area: the Red player hit {{6 bot}} {hit}, the ghoul in it untouched and without the TAUNT's threat {spared}; MAGICCIRCLE ({circle.Why}) on its Blue owner {Has(blue)}, on the bot {Has(bot)} (want false)");
    }

    private static void BotAreaSpares()
    {
        using var f = new BotTest.Field(Patrols(), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, aPeer, a) = f.Join("Rhea", at + new Vector2(1, 0), Pvp.Red);
        var (_, bPeer, b) = f.Join("Rolf", at + new Vector2(-1, 0), Pvp.Red);
        var bot = Bot(f, at);
        lock (f.World.Sync) bot.Bot!.Fight.Spared[a.Character.Id] = f.World.GameNow + TimeSpan.FromMinutes(5).Ticks; // as after its knockdown
        var (aMark, bMark) = (aPeer.Events.Count, bPeer.Events.Count);
        HitAt(f.World, bot, T("UNDEADHAND"), at);
        bool Hurt(FakePeer peer, int mark, Player p) =>
            Since(peer, mark).Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, p.Id) && L(e.P, 6) == bot.Id && F(e.P, 2) < 0);
        var (spared, hit) = (!Hurt(aPeer, aMark, a), Hurt(bPeer, bMark, b));
        Check(spared && hit, $"the patrol's area: the spared Red player untouched {spared}, the other Red one hit {{6 bot}} {hit}");
    }

    private static void BotFullKit()
    {
        using var f = new BotTest.Field(Patrols(), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        f.Join("Bern", at + new Vector2(2, 0)); // a human on the map: bots spawn
        var bot = Bot(f, at);
        var gear = bot.Inventory.Equipment;
        short[] Of(int slot) => gear[slot]?.Active ?? [-1];
        var slots = Of(0).Concat([Of(3)[0], Of(2)[0], Of(4)[0]]).ToArray(); // the weapon's 3, then armour, helmet, shoes
        Check(slots.Length == 6 && slots.All(s => s >= 0), $"the patrol's Q, W, E, armour, helmet, shoes: [{string.Join(", ", slots)}], want no -1");
    }

    private static void GathererEngagedOnly()
    {
        using var f = new BotTest.Field(new BotOptions { Enabled = true, GathererFlag = BotGathererFlag.Red }, BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, _, a) = f.Join("Rhea", at + new Vector2(2, 0), Pvp.Red);
        var (_, _, b) = f.Join("Rolf", at + new Vector2(-2, 0), Pvp.Red);
        var bot = Bot(f, at, BotRole.Gatherer);
        bool Hostile(Player p) => SpellTargets.Of(bot, p, f.World.GameNow).Hostile;
        var before = (Hostile(a), Hostile(b), Pvp.Refusal(bot, a, f.World.GameNow) is null);
        Hit(f.World, a, T("UNDEADHAND_STUN"), bot);
        var after = (Hostile(a), Hostile(b));
        Check(before == (false, false, true) && after == (true, false),
              $"a Red gatherer may hit Red players by the zone ({before.Item3}), yet its spells take neither before {(before.Item1, before.Item2)}; after A's stun A only {after}");
    }

    private static void BotHeldAfterPath()
    {
        using var f = new BotTest.Field(Patrols(), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, peer, blue) = f.Join("Bern", at + new Vector2(2, 0));
        var bot = Bot(f, at);
        f.Run(0.5); // it is in view
        int Moves(int from) => peer.Events.Skip(from).Count(e => e.Code == MovementCodec.PhotonEventCode && Convert.ToInt64(e.Parameters[0]) == bot.Id);
        var mark = peer.Events.Count;
        Hit(f.World, blue, T("MOUNTSPELL_BIGCLEAVE_KNOCKBACK"), bot);
        var end = bot.Effects.Forced?.End ?? 0;
        var to = bot.Effects.Forced?.To ?? Vector2.Zero;
        while (f.World.GameNow < end + TimeSpan.FromMilliseconds(250).Ticks) f.Run(0.05);
        var held = Moves(mark);
        var stands = Vector2.Distance(bot.PositionAt(f.World.Bots.Time), to) < 0.05f;
        f.Run(1);
        var after = Moves(mark);
        Check(end > 0 && held == 0 && stands && after > 0,
              $"knocked back (path end {end > 0}): {held} Move(s) up to end + 250 ms (want 0), it stands at the path's end {stands}; {after} once the hold is over (want > 0)");
    }

    private static void BotCasts()
    {
        using var f = new BotTest.Field(Patrols(BotSpells.Full), BotTest.NoPopulation, BotYellow);
        var at = f.Nav().Exits[0].Arrival;
        var (_, peer, red) = f.Join("Rhea", at + new Vector2(6, 0), Pvp.Red);
        var bot = Bot(f, at);
        var bolt = T("FIRESTAFFBOLT2");
        lock (f.World.Sync)
        {
            var staff = Client.Items.Find("T4_MAIN_FIRESTAFF")!;
            bot.Inventory.Equipment.Put(0, new Item(f.World.ObjectIds.Next(), staff, 1, 1, staff.FullDurabilityRaw, [bolt.Id, -1, -1], []));
            foreach (var slot in new[] { 1, 2, 3, 4 }) // the bolt alone: no spell of its offhand and armour first
                if (bot.Inventory.Equipment[slot] is { } item) Array.Fill(item.Active, (short)-1);
            bot.RecalculateStats(f.World.GameNow);
        }
        var mark = peer.Events.Count;
        f.Run(4);
        var seen = Since(peer, mark);
        var cast = seen.Any(e => e.Code == EventCodes.CastStart && Is(e.P, bot.Id) && Convert.ToInt16(e.P[5]) == bolt.Id && L(e.P, 7) == red.Id);
        var hit = seen.Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, red.Id) && L(e.P, 6) == bot.Id && e.P.ContainsKey(7) && Convert.ToInt16(e.P[7]) == bolt.Id);
        Check(cast && hit, $"the patrol attacks on sight and casts its bolt: CastStart {{0 bot, 5 bolt, 7 player}} {cast}, the bolt's HealthUpdate {{6 bot, 7 bolt}} {hit}");
    }

    // ---- phase 3: areas, channels and drains on players ----

    private static void SkillShotArea()
    {
        var (arena, (_, aPeer, a), (_, bPeer, b), c) = Reds(PvpMode.Consensual, blue: true);
        using var held = arena;
        var world = arena.World;
        arena.Setup = x => x.Flag = Pvp.Red;
        var (_, dPeer, d) = arena.Join("Dan", new InventoryItem("T4_MAIN_SWORD"));
        lock (world.Sync)
        {
            Check(world.Parties.Invite(a, d.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(d, true, false, Parties.NoReason) == 0, "A and D form a party");
            foreach (var m in a.Map!.Objects.Values.OfType<Mob>().Where(m => Vector2.Distance(m.Position, arena.Stand) < 40).ToList())
                SpawnManager.Despawn(m, world, respawn: false); // nothing else in the flame's way
        }
        Place(arena, a, Vector2.Zero);
        Place(arena, c!, new Vector2(4, 0));
        Place(arena, d, new Vector2(7, 0));
        Place(arena, b, new Vector2(10, 0));
        var mark = (aPeer.Events.Count, bPeer.Events.Count);
        HitAt(arena, a, T("SEARING_FLAME"), arena.Stand + new Vector2(16, 0));
        arena.Run(1);
        bool Names((EventCodes Code, Dictionary<byte, object?> P) e, Player p) => e.Code is EventCodes.CastHit or EventCodes.CastHits && Is(e.P, p.Id);
        var seen = Since(bPeer, mark.Item2);
        var hitB = seen.Any(e => Names(e, b)) && seen.Any(e => e.Code == EventCodes.HealthUpdate && Is(e.P, b.Id) && L(e.P, 6) == a.Id && F(e.P, 2) < 0);
        var spared = !Since(aPeer, mark.Item1).Any(e => Names(e, c!) || Names(e, d)) && c!.Health.At(world.GameNow) == c.Health.Max && d.Health.At(world.GameNow) == d.Health.Max;
        Check(hitB && spared && b.Effects.Spells.Any(s => s.Spell.Name == "SEARING_FLAME_DOT"),
              $"the flame passes Blue C (4 m) and party mate D (7 m) and hits Red B (10 m): CastHit and HealthUpdate {{6 A}} on B {hitB}, C and D untouched {spared}, B burns");
    }

    private static void AreaLeave()
    {
        var (arena, (_, _, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        Place(arena, b, new Vector2(6, 0));
        HitAt(arena, a, T("GLACIALFIELD"), arena.Stand + new Vector2(6, 0));
        arena.Run(0.2);
        bool Slowed() => b.Effects.Spells.Any(s => s.Spell.Name == "GLACIALFIELD_EFFECT");
        var before = Slowed();
        var mark = bPeer.Events.Count;
        Place(arena, b, new Vector2(30, 0));
        arena.Run(0.2);
        var effect = T("GLACIALFIELD_EFFECT").Id;
        var list = Since(bPeer, mark).LastOrDefault(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && Is(e.P, b.Id)).P;
        Check(before && !Slowed() && list is not null && !Ids(list, 1).Contains(effect),
              $"GLACIALFIELD on B {before}; 24 m out of the field the slow is gone {!Slowed()}, B's list without it {(list is null ? "not sent" : "sent")}");
    }

    private static void Channels()
    {
        var (arena, (_, _, a), (_, bPeer, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var world = arena.World;
        var disembowel = T("DISEMBOWEL");
        var (channel, why) = Cast(arena, b, disembowel, a.Id);
        arena.Run(0.3);
        var mark = bPeer.Events.Count;
        var on = b.Combat.Channel is not null;
        Hit(arena, a, T("INTERRUPT"), b);
        var ended = Since(bPeer, mark).FirstOrDefault(e => e.Code == EventCodes.ChannelingEnded && Is(e.P, b.Id)).P;
        Check(channel is not null && on && ended is not null && L(ended, 1) == 2 && b.Combat.Channel is null,
              $"B's DISEMBOWEL channel at A ({why}, on {on}): A's INTERRUPT ends it, ChannelingEnded {{1 2}} ({(ended is null ? "none" : Show([(EventCodes.ChannelingEnded, ended)]))})");
        arena.Run(0.5);
        var (mine, mineWhy) = Cast(arena, a, disembowel, b.Id, 2);
        arena.Run(0.3);
        bool Rooted() { lock (world.Sync) return b.Effects.On(world.GameNow).Any(x => x.Spell.Caster == a.Id && x.Sub.Tag == "root"); }
        var rooted = Rooted();
        mark = bPeer.Events.Count;
        lock (world.Sync) SpellCasts.EndChannel(a, SpellCasts.ChannelEnd.Cancelled);
        Check(mine is not null && rooted && !Rooted() && Since(bPeer, mark).Any(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && Is(e.P, b.Id)),
              $"A's DISEMBOWEL at B ({mineWhy}): B rooted {rooted}; the channel ends: the linked root is off, B's list goes out");
    }

    private static void SpellProof()
    {
        var (arena, (_, _, a), (_, _, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var world = arena.World;
        var xml = new XElement("activespell", new XAttribute("target", "self"), new XAttribute("castingtime", "2"), new XAttribute("interruptiblebyspell", "false"));
        var steady = SpellTemplate.From(T("DISEMBOWEL").Id, "TEST_STEADY_CAST", "activespell", xml);
        PendingCast cast;
        lock (world.Sync)
        {
            var now = world.GameNow;
            cast = new PendingCast(steady, 0, 1, null, 100, null, b.PositionAt(DateTime.UtcNow), now, now + SpellCasts.Seconds(2), 0);
            SpellCasts.Start(b, cast);
        }
        Hit(arena, a, T("INTERRUPT"), b);
        Check(!steady.InterruptibleBySpell && b.Combat.Cast == cast, $"A's INTERRUPT on B's 2 s cast of an interruptiblebyspell=\"false\" spell: still casting {b.Combat.Cast == cast}");
    }

    private static void EnergyDrain()
    {
        var (arena, (_, _, a), (_, bPeer, b), c) = Reds(PvpMode.Consensual, blue: true);
        using var held = arena;
        var world = arena.World;
        var xml = new XElement("activespell", new XAttribute("target", "self"),
            new XElement("directattributechange", new XAttribute("target", "enemy"), new XAttribute("attribute", "energy"), new XAttribute("change", "-0.1"),
                new XAttribute("changetype", "relativemax"), new XAttribute("effectarearadius", "7")));
        var drain = SpellTemplate.From(T("UNDEAD_HARVESTER_ENERGY_DRAIN").Id, "TEST_ENERGY_DRAIN", "activespell", xml);
        float Energy(Player p) { lock (world.Sync) return p.Energy.At(world.GameNow); }
        var (fullB, fullC) = (Energy(b), Energy(c!));
        var mark = bPeer.Events.Count;
        Hit(arena, a, drain, null);
        var update = Since(bPeer, mark).FirstOrDefault(e => e.Code == EventCodes.EnergyUpdate && Is(e.P, b.Id)).P;
        Check(update is not null && Math.Abs(F(update, 2) + 0.1f * b.Energy.Max) < 0.01f && L(update, 5) == a.Id && Math.Abs(Energy(b) - (fullB - 0.1f * b.Energy.Max)) < 0.5f
              && Energy(c!) == fullC && b.Combat.InCombat,
              $"A's drain around it: Red B −10 % (EnergyUpdate {{2 −{0.1f * b.Energy.Max:0.#}, 5 A}}), in combat; Blue C keeps {fullC} (got {(update is null ? "none" : Show([(EventCodes.EnergyUpdate, update)]))})");
        var mob = arena.Spawn("T4_MOB_UNDEAD_SOLDIER_VETERAN", new Vector2(2, 0));
        var harvest = Client.Spells!.Template((short)(T("UNDEAD_HARVESTER_ENERGY_DRAIN").Id + 1))!;
        var fullA = Energy(a);
        lock (world.Sync)
            MobCasts.Execute(new MobCasts.MobHit(mob, a.Map!, harvest, 1, 100, a, SpellMovement.PositionOf(a, world.GameNow), world.GameNow), harvest);
        Check(Math.Abs(Energy(a) - (fullA - 0.1f * a.Energy.Max)) < 0.5f, $"the harvester's drain channel tick on A: −10 % of its maximum ({fullA} → {Energy(a)})");
    }

    private static void ReflectCap()
    {
        var (arena, (_, aPeer, a), (_, _, b), _) = Reds(PvpMode.Consensual);
        using var held = arena;
        var world = arena.World;
        var xml = new XElement("activespell", new XAttribute("target", "self"),
            new XElement("reflectdamageactive", new XAttribute("target", "self"), new XAttribute("damagetype", "all"), new XAttribute("astype", "magic"),
                new XAttribute("amountpercent", "10"), new XAttribute("time", "5")));
        var mirror = SpellTemplate.From(T("TEST_REFLECTACTIVE1").Id, "TEST_REFLECT_10", "activespell", xml);
        Hit(arena, b, mirror, null);
        var mark = aPeer.Events.Count;
        long now;
        lock (world.Sync)
        {
            now = world.GameNow;
            Pvp.Damage(a, b, 400, EffectType.Physical, HitOrigin.Direct, -1, now);
        }
        var back = Since(aPeer, mark).FirstOrDefault(e => e.Code == EventCodes.HealthUpdate && Is(e.P, a.Id) && L(e.P, 5) == (byte)HitOrigin.Reflected).P;
        var cap = Combat.Rounded(2 * b.Health.Max * Combat.ResistanceOf(a, EffectType.Magic) * Pvp.DefenseFactor(a, now));
        Check(back is not null && Math.Abs(-F(back, 2) - cap) <= 1,
              $"10 × a 400 hit is capped at 2 × B's {b.Health.Max} maximum health: A takes {cap} back (got {(back is null ? "none" : Show([(EventCodes.HealthUpdate, back)]))})");
    }
}
