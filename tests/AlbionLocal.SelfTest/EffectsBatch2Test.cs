using System.Numerics;
using System.Xml.Linq;
using AlbionLocal.Server;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// StageD effects batch 2 and 3 (analysis/work/stageD/effects/SPEC.md §7): the worn passives (a cape's CastFinished trigger with its
/// cooldown reset and "not" condition, an axe's heal on hit), the bow's multispell (ReplaceSpellSlotWithMultiSpell 457), the carry load
/// with a gatherer backpack's weightbonus, the gathering yield bonus of an Avalon tool, the loot chests (generateloot), summons
/// (spawnmob: limit, ttl, the summoner's death) and removespelleffectareasinarea. Light: the rules with the client numbers.
/// </summary>
public static class EffectsBatch2Test
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("effects batch 2: PASSIVE_CAPE_CAERLEON resets the Q cooldown at its CastFinished (SpellCooldownUpdate) once per cape cooldown; PASSIVE_HEALTHCHANCE_AXE heals 15 % of an auto (origin 4)", WornPassives),
        ("effects batch 2: SPEEDARCHER_KITE's multispell (457 {0 3012, 1 0, 2 hit + 8 s}): the slot casts SPEEDARCHER_KITE_MULTI_DASH once", MultiSpell),
        ("effects batch 3: carry load (backpack weightbonus 0.3 on fiber, maxload 50 + buffs, overload %), the Avalon axe's +10 % wood yield, a loot chest roll into the bag", LoadYieldLoot),
        ("effects batch 3: spawnmob (NewMob, limit 1 replaces, ttl Leave, the adds go with their summoner), removespelleffectareasinarea takes a FIREWALL off", SummonsAndAreas),
        ("effects batch 2: changeaggro target=self on the hit mob (HAMMER_SHOVE_SWING_EFFECT +66 threat) and with aggroradius around the caster; maketargettopthreat with its factor", ThreatTags),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static SpellTemplate T(string name) => Client.Spells!.Template(name) ?? throw new Exception($"{name} not in spells.xml");

    private static void WornPassives()
    {
        using var arena = new Arena();
        var world = arena.World;
        arena.Setup = c => c.Equipment[6] = new InventoryItem("T4_CAPEITEM_FW_CAERLEON") { Passive = ["PASSIVE_CAPE_CAERLEON"] };
        var (session, peer, me) = arena.Join("Caped", new InventoryItem("T4_MAIN_SWORD") { Active = ["HEROICSTRIKE2", "SWORD_SPIN", "MIGHTYBLOW"] });
        var ghoul = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(1.5f, 0));
        Check(Server.Systems.Passives.Of(me).Spells.Any(s => s.Spell.Name == "PASSIVE_CAPE_CAERLEON"), "the worn cape's passive is PASSIVE_CAPE_CAERLEON");
        var q = Client.Spells!.Template(me.Inventory.Equipment[0]!.Active[0])!;
        Check(q.RecastDelay > 1, $"the sword's Q {q} has a cooldown ({q.RecastDelay} s)");
        var mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = 0, d0 = q.Id, d1 = ghoul.Id, d4 = 1 });
        arena.Run(1.5);
        var events = Since(peer, mark);
        var cooldown = events.FirstOrDefault(e => e.Code == EventCodes.SpellCooldownUpdate).P;
        var cape = T("PASSIVE_CAPE_CAERLEON_COOLDOWN");
        Check(cooldown is not null && cooldown[0] is byte[] and [0] && me.Combat.Cooldowns[q.Id] <= world.GameNow
              && me.Effects.Spells.Any(s => s.Spell == cape) && events.Any(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && e.P[1] is short[] ids && ids.Contains(cape.Id)),
              $"the first Q: its cooldown is reset (SpellCooldownUpdate {{0 [0]}}), PASSIVE_CAPE_CAERLEON_COOLDOWN listed (got {Show(events.Where(e => e.Code is EventCodes.SpellCooldownUpdate or EventCodes.ActiveSpellEffectsUpdate))})");
        mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = 0, d0 = q.Id, d1 = ghoul.Id, d4 = 2 });
        arena.Run(1.5);
        Check(Since(peer, mark).All(e => e.Code != EventCodes.SpellCooldownUpdate) && me.Combat.Cooldowns[q.Id] > world.GameNow,
              "the second Q under the cape cooldown: not reset (<not><IfSpellCategoryActive cape_cooldown/></not>, none of its children may hold)");

        // The axe's PASSIVE_HEALTHCHANCE_AXE: 15 % of an auto-attack's damage back as health, HealthUpdate origin 4 with the passive's id.
        var (axeSession, axePeer, axer) = arena.Join("Healer", new InventoryItem("T4_MAIN_AXE") { Passive = ["PASSIVE_HEALTHCHANCE_AXE"] });
        lock (world.Sync) axer.Health.Set(axer.Health.Max / 2, world.GameNow);
        var target = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(-1.5f, 0));
        mark = axePeer.Events.Count;
        Send(axeSession, new AttackStartRequest { wf = world.GameNow, co = target.Id, cp = 0 });
        arena.Run(0.5);
        var after = Since(axePeer, mark).Where(e => e.Code == EventCodes.HealthUpdate).ToList();
        var hit = after.FirstOrDefault(e => L(e.P, 0) == target.Id).P;
        var heal = after.FirstOrDefault(e => L(e.P, 0) == axer.Id).P;
        var passive = T("PASSIVE_HEALTHCHANCE_AXE");
        Check(hit is not null && heal is not null && Convert.ToByte(heal[5]) == 4 && Convert.ToInt16(heal[7]) == passive.Id
              && Math.Abs(F(heal, 2) - Math.Round(-F(hit, 2) * 0.15f)) <= 1,
              $"the auto's damage {(hit is null ? "?" : -F(hit, 2))} heals {(heal is null ? "?" : F(heal, 2))} (15 %, origin 4, spell {passive.Id}) (got {Show(after)})");
    }

    private static void MultiSpell()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (session, peer, me) = arena.Join("Kiter", new InventoryItem("T4_2H_BOW") { Active = [null, null, "SPEEDARCHER_KITE"] });
        var kite = T("SPEEDARCHER_KITE");
        var dash = T("SPEEDARCHER_KITE_MULTI_DASH");
        var mark = peer.Events.Count;
        var t0 = world.GameNow;
        Send(session, new CastStartRequest { wf = t0, dx = 2, d0 = kite.Id, d4 = 1 });
        var replace = Since(peer, mark).FirstOrDefault(e => e.Code == EventCodes.ReplaceSpellSlotWithMultiSpell).P;
        Check(replace is not null && Convert.ToInt16(replace[0]) == kite.Id && kite.Id == 3012 && Convert.ToByte(replace[1]) == 0 && L(replace, 2) == t0 + SpellCasts.Seconds(8)
              && me.Combat.Multi.TryGetValue(2, out var m) && m.Spell == dash,
              $"ReplaceSpellSlotWithMultiSpell {{0 3012, 1 0, 2 hit + 8 s}} to the caster, slot 2 casts the dash (got {Show(Since(peer, mark).Where(e => e.Code == EventCodes.ReplaceSpellSlotWithMultiSpell))})");
        var slots = new int[14];
        slots[2] = kite.Id;
        CastRefusal Why(int spell) { lock (world.Sync) return SpellCasts.Check(me, slots, 2, spell, 0, arena.Stand + new Vector2(3, 0), 9, world.GameNow).Why; }
        Check(Why(dash.Id) == CastRefusal.None && Why(T("SPEEDARCHER_KITE_BUFF").Id) == CastRefusal.NotInSlot, "within the window slot 2 takes SPEEDARCHER_KITE_MULTI_DASH");
        Send(session, new CastStartRequest { wf = world.GameNow, dx = 2, d0 = dash.Id, d2 = arena.Stand + new Vector2(3, 0), d4 = 2 });
        arena.Run(1);
        Check(!me.Combat.Multi.ContainsKey(2) && Why(dash.Id) == CastRefusal.NotInSlot, "cast once (castableonlyonce): the slot is back to its own spell");
        // A window that lapses: the follow-up is refused after hit + 8 s.
        me.Combat.Cooldowns.Clear();
        Send(session, new CastStartRequest { wf = world.GameNow, dx = 2, d0 = kite.Id, d4 = 3 });
        arena.Run(8.5);
        Check(Why(dash.Id) == CastRefusal.NotInSlot, "after the 8 s window the dash is refused");
    }

    private static void LoadYieldLoot()
    {
        using var arena = new Arena();
        var world = arena.World;
        arena.Setup = c =>
        {
            c.Equipment[5] = new InventoryItem("T4_BACKPACK_GATHERER_FIBER") { Passive = ["PASSIVE_BACKPACK_FIBER_T4"] };
            c.Inventory = [new InventoryItem("T4_FIBER", 100), new InventoryItem("T4_ORE", 10)];
        };
        var (_, peer, me) = arena.Join("Gatherer", new InventoryItem("T4_2H_TOOL_AXE_AVALON") { Passive = ["PASSIVE_AVALON_YIELD_WOOD_T4"] });
        var fiber = Server.Systems.Passives.WeightOf(world.Items.Find("T4_FIBER")!);
        var load = Server.Systems.Passives.Load(me);
        Check(fiber == 0.51 && Math.Abs(load - (100 * 0.51 * 0.7 + 10 * 0.51)) < 1e-4,
              $"load (the bag only, the worn items weigh 0 as on the client): 100 fiber × 0.51 × (1 − 0.3) = 35.7 + 10 ore 5.1 = 40.8 kg (got {load:0.##})");
        var now = world.GameNow;
        var max = Server.Systems.Passives.MaxLoad(me);
        Check(Client.GameData!.MaxLoad == 50 && Math.Abs(max - (50 + me.Effects.Sum("maxload", now)) * (1 + me.Effects.Sum("maxloadbonus", now))) < 1e-9 && max >= 50,
              $"max load: characters.xml maxload 50 + the maxload buffs (got {max:0.##})");
        lock (world.Sync) me.Inventory.Bag[1]!.Count = 999;
        Check(Server.Systems.Passives.LoadPercent(me) > 150, $"999 ore overload: {Server.Systems.Passives.LoadPercent(me):0} % (IfOwnLoadPercent less 150 fails: no dash, no blink)");

        // The Avalon axe: +0.1 gathering yield on wood T2-T4 (resourcegatheringbuff), HarvestFinished key 6.
        var wood = Server.Systems.Passives.YieldBonus(me, "gatheringyield", "WOOD", 4);
        Check(Math.Abs(wood - 0.1) < 1e-6 && Server.Systems.Passives.YieldBonus(me, "gatheringyield", "WOOD", 5) == 0
              && Server.Systems.Passives.YieldBonus(me, "gatheringyield", "ROCK", 4) == 0 && Server.Systems.Passives.YieldBonus(me, "fishingyield", "WOOD", 4) == 0,
              $"PASSIVE_AVALON_YIELD_WOOD_T4: +0.1 on WOOD T4 (got {wood}), none on T5, rock or fishing");
        var rng = new Random(1);
        var bonuses = Enumerable.Range(0, 2000).Select(_ => Server.Systems.Passives.Bonus(3, 0.5, rng)).ToList();
        Check(Server.Systems.Passives.Bonus(10, (float)0.1, rng) == 1 && bonuses.All(b => b is 1 or 2) && Math.Abs(bonuses.Average() - 1.5) < 0.05,
              $"key 6: 10 × 0.1 = 1; 3 × 0.5 = 1.5 rounded at random to 1 or 2 (mean {bonuses.Average():0.###})");

        // A loot chest: one roll of its loot list into the bag (stacks or new objects with InventoryPutItem).
        var spells = Client.Spells!;
        var tables = world.Clusters!.Loot;
        var chest = Enumerable.Range(0, spells.Count).Select(i => spells.Template(i))
            .Select(s => (Spell: s, List: s?.Effects.FirstOrDefault(n => n.Name.LocalName == "generateloot")?.Attribute("lootlist")?.Value))
            .Where(x => x.List is not null && tables.Has(x.List)).Select(x => (x.Spell!, x.List!)).First();
        var given = new List<(ItemTemplate Item, int Count)>();
        var mark = peer.Events.Count;
        lock (world.Sync)
            for (var i = 0; i < 20 && given.Count == 0; i++)
                given = Consumables.GenerateLoot(new SpellCasts.SpellContext(me, me.Map!, chest.Item1, 0, 100, null, SpellMovement.PositionOf(me, world.GameNow), world.GameNow), chest.Item2);
        var got = Since(peer, mark);
        Check(given.Count > 0 && given.All(g => me.Inventory.Bag.Items.Any(i => i.Template == g.Item))
              && got.Any(x => x.Code is EventCodes.NewSimpleItem or EventCodes.NewEquipmentItem),
              $"{chest.Item1.Name} ({chest.Item2}): {string.Join(", ", given.Select(g => $"{g.Count} x {g.Item.Name}"))} into the bag with their item events");
    }

    private static void SummonsAndAreas()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, peer, me) = arena.Join("Witness", new InventoryItem("T4_MAIN_FIRESTAFF"));
        var map = world.Maps.Find("0204")!;
        var boss = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(4, 0));
        var node = new XElement("spawnmob", new XAttribute("target", "self"), new XAttribute("type", "T4_MOB_UNDEAD_SKELETON_SUMMON"), new XAttribute("ttl", "2"),
            new XAttribute("limit", "1"), new XAttribute("distancefromtarget", "1"), new XAttribute("anglefromcaster", "90"), new XAttribute("ttlaftercasterdeath", "0"));
        var mark = peer.Events.Count;
        Mob? first, second;
        lock (world.Sync)
        {
            first = Summons.Spawn(world, map, boss, node, null, boss.Position, world.GameNow);
        }
        arena.Run(0.1);
        Check(first is { Summoner: var s } && s == boss && Vector2.Distance(first.Position, boss.Position + new Vector2(1, 0)) < 0.01f
              && Since(peer, mark).Any(e => e.Code == EventCodes.NewMob && L(e.P, 0) == first.Id),
              $"the add stands 1 m at 90° off the boss's facing, summoned by it, NewMob to the player (got {first})");
        lock (world.Sync) second = Summons.Spawn(world, map, boss, node, null, boss.Position, world.GameNow);
        Check(first!.Removed && second is { Removed: false }, "limit 1: a second summon takes the first off");
        mark = peer.Events.Count;
        arena.Run(2.2);
        Check(second!.Removed && Since(peer, mark).Any(e => e.Code == EventCodes.Leave && L(e.P, 0) == second.Id), "ttl 2 s: the add leaves");
        Mob third;
        lock (world.Sync) third = Summons.Spawn(world, map, boss, node, null, boss.Position, world.GameNow)!;
        lock (world.Sync) Combat.Damage(me, boss, 1e6, EffectType.Magic, HitOrigin.Direct, -1, world.GameNow);
        Check(boss.Dead && third.Removed, "the boss dies: its add goes (ttlaftercasterdeath 0)");

        // A negative distancefromtarget stands on the far side (the Morgana demon gates); a summoner despawned without dying takes its adds along.
        var portal = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(-4, 0));
        node.SetAttributeValue("distancefromtarget", "-1");
        Mob behind;
        lock (world.Sync) behind = Summons.Spawn(world, map, portal, node, null, portal.Position, world.GameNow)!;
        Check(Vector2.Distance(behind.Position, portal.Position + new Vector2(-1, 0)) < 0.01f, $"distancefromtarget -1: 1 m behind (got {behind.Position})");
        lock (world.Sync) SpawnManager.Despawn(portal, world, respawn: false);
        Check(behind.Removed, "the summoner despawned (not killed): its add goes too");

        // removespelleffectareasinarea: the caster's FIREWALL within removeradius leaves at once.
        var wall = T("FIREWALL");
        lock (world.Sync) SpellCasts.Execute(new SpellCasts.SpellContext(me, map, wall, 1, 100, null, arena.Stand + new Vector2(3, 0), world.GameNow), wall);
        var area = map.Objects.Values.OfType<SpellArea>().Single();
        var remove = new XElement("removespelleffectareasinarea", new XAttribute("spell", "FIREWALL"), new XAttribute("removeradius", "10"), new XAttribute("ownedby", "friendly"));
        var other = new XElement("removespelleffectareasinarea", new XAttribute("spell", "FIREWALL"), new XAttribute("removeradius", "10"), new XAttribute("ownedby", "enemy"));
        int none, removed;
        lock (world.Sync)
        {
            none = SpellAreas.RemoveInArea(me, map, arena.Stand, other);
            removed = SpellAreas.RemoveInArea(me, map, arena.Stand, remove);
        }
        Check(none == 0 && removed == 1 && area.Gone && !map.Objects.ContainsKey(area.Id), "ownedby enemy leaves the player's wall; ownedby friendly takes it off");
    }

    private static void ThreatTags()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (_, _, me) = arena.Join("Tank", new InventoryItem("T4_2H_HAMMER"));
        var map = world.Maps.Find("0204")!;
        var ghoul = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(1.5f, 0));
        var near = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(0, 3));
        var far = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(0, 12));
        var shove = T("HAMMER_SHOVE_SWING_EFFECT"); // <changeaggro target="self" value="66.00"/>, an enemy effect: the hit mob is the context
        lock (world.Sync) SpellCasts.Execute(new SpellCasts.SpellContext(me, map, shove, 0, 1, ghoul, ghoul.Position, world.GameNow), shove);
        var threat = ghoul.Threat.GetValueOrDefault(me);
        Check(!ghoul.Dead && threat >= 66 && near.Threat.Count == 0, $"HAMMER_SHOVE_SWING_EFFECT: the hit ghoul has the caster's threat ≥ 66 (got {threat:0.##}), no other mob");

        // aggroradius around the caster (PBAOE_PULL_EFFECT-like: 7 m): the near mob, not the far one.
        var taunt = new XElement("changeaggro", new XAttribute("target", "self"), new XAttribute("value", "50"), new XAttribute("aggroradius", "7"));
        var top = new XElement("maketargettopthreat", new XAttribute("target", "self"), new XAttribute("factor", "2"));
        var spell = SpellTemplate.From(-2, "TEST_TAUNT", "activespell", new XElement("activespell", new XAttribute("uniquename", "TEST_TAUNT"), new XAttribute("target", "self"), taunt));
        lock (world.Sync) SpellCasts.Execute(new SpellCasts.SpellContext(me, map, spell, 0, 100, null, arena.Stand, world.GameNow), spell);
        Check(near.Threat.GetValueOrDefault(me) >= 50 && far.Threat.Count == 0, "aggroradius 7: the mob 3 m off gets 50 threat, the one 12 m off none");

        // maketargettopthreat factor 2: the caster's threat to the highest × 2 + 1.
        threat = ghoul.Threat.GetValueOrDefault(me); // the shove's 66 and the 7 m taunt's 50
        var topSpell = SpellTemplate.From(-3, "TEST_TOP", "activespell", new XElement("activespell", new XAttribute("uniquename", "TEST_TOP"), new XAttribute("target", "enemy"), top));
        lock (world.Sync) SpellCasts.Execute(new SpellCasts.SpellContext(me, map, topSpell, 0, 100, ghoul, ghoul.Position, world.GameNow), topSpell);
        Check(Math.Abs(ghoul.Threat.GetValueOrDefault(me) - (threat * 2 + 1)) < 1e-6, $"maketargettopthreat factor 2: {threat:0.##} → {threat * 2 + 1:0.##} (got {ghoul.Threat.GetValueOrDefault(me):0.##})");
    }
}
