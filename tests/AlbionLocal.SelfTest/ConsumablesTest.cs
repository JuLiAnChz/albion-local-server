using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// StageD consumables (analysis/work/stageD/consumables/SPEC.md §8): a heal potion from the potion slot (the captured event
/// order, 10 unrounded ticks of 0.013 × max, the slot cooldown), the fireworks stack used up, the soup's cast time, buff and
/// re-eat, a cancelled soup, the buff and cooldown over a relog, the energy potion's numbers, and tomes used from the inventory
/// (a batch of 3, a single one, the refusals). Light: the numbers the SPEC lists, the rest only as far as it shows the rule.
/// </summary>
public static class ConsumablesTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("consumables: heal potion (8, 32, 18, 19, ticks of 0.013 × max, 11, 21), cooldown; soup 0.8 s, regen × 2, re-eat, cancel; buff and cooldown over a relog", SlotCasts),
        ("consumables: fireworks stack 3 used up (32, 32, 27 + 90, then NotInSlot), POTION_ENERGY_P3 at full energy (+127-style direct, 4 × 0.053 over time, d8 [7000, 7000])", FireworksAndEnergy),
        ("consumables: tomes from the inventory (batch of 3: 474 span 1 s, uses 0.5 s apart, response after the last; count 1; refusals 14960)", Inventory),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static SpellTemplate T(string name) => Client.Spells!.Template(name) ?? throw new Exception($"{name} not in spells.xml");

    /// <summary>A starter character on the arena's stand with a sword, an empty board and <paramref name="setup"/>'s items, joined.</summary>
    private static (WorldSession Session, FakePeer Peer, Player Player) Join(Arena a, string name, Action<Character> setup)
    {
        var c = StarterKit.Default.NewCharacter(name);
        c.Equipment[0] = new InventoryItem("T4_MAIN_SWORD");
        c.Cluster = "0204";
        (c.PositionX, c.PositionY) = (a.Stand.X, a.Stand.Y);
        c.Progression = new Progression();
        setup(c);
        Check(a.Characters.Create(c), $"character {name} created");
        return Rejoin(a, c.Id);
    }

    /// <summary>A new connection joining character <paramref name="id"/>.</summary>
    private static (WorldSession Session, FakePeer Peer, Player Player) Rejoin(Arena a, Guid id)
    {
        var peer = new FakePeer();
        var session = new WorldSession(peer, a.World);
        peer.Disconnected = session.OnDisconnected;
        session.OnRequest(Request(new JoinRequest { ss = id }, 1), encrypted: false);
        return (session, peer, session.Player!);
    }

    private static readonly HashSet<EventCodes> Cast =
    [
        EventCodes.EnergyUpdate, EventCodes.NewSimpleItem, EventCodes.InventoryDeleteItem, EventCodes.CharacterEquipmentChanged, EventCodes.CastFinished,
        EventCodes.CastSpell, EventCodes.HealthUpdate, EventCodes.ActiveSpellEffectsUpdate, EventCodes.CastHit, EventCodes.RegenerationHealthChanged,
        EventCodes.RegenerationPlayerComboChanged,
    ];

    private static List<(EventCodes Code, Dictionary<byte, object?> P)> Of(FakePeer peer, int mark) => [.. Since(peer, mark).Where(e => Cast.Contains(e.Code))];

    private static bool About(double a, double b, double tolerance = 0.01) => Math.Abs(a - b) < tolerance;

    private static void SlotCasts()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (session, peer, me) = Join(arena, "Eater", c =>
        {
            c.Equipment[8] = new InventoryItem("T4_POTION_HEAL", 5);
            c.Equipment[9] = new InventoryItem("T1_MEAL_SOUP", 2);
        });
        var id = me.Character.Id;
        var baseRegen = Convert.ToSingle(peer.Responses[0].Parameters[14]);
        var heal = T("POTION_HEAL_P3");
        var soup = T("FOOD_HEALTH_REG_P1");
        Check(soup.Id == 4191 && me.Inventory.Equipment[8]!.Template.Name == "T4_POTION_HEAL", $"FOOD_HEALTH_REG_P1 is 4191 (got {soup.Id})");

        // 1. The heal potion (SPEC §8.1): 8 (no key 2), 32 {2: 4}, 18 {1 me, 3 12, 6 factor}, 19 {1 me}, the first tick, 11, 21.
        // The SPEC's numbers from the potion's data (the test character's own max differs; a new max would refill its bars):
        // 0.013 × 2068 = 26.884 per tick.
        var overTime = heal.Effects.First(e => e.Name.LocalName == "attributechangeovertime");
        var change = SpellTemplate.Float(overTime, "change");
        Check(About(change * 2068, 26.884, 1e-3) && (string?)overTime.Attribute("changetype") == "relativemax",
              $"POTION_HEAL_P3: change {change} relativemax, × 2068 = 26.884");
        lock (world.Sync) me.Health.Set(100, world.GameNow);
        var max = me.Health.Max;
        var mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.PotionSlot, d0 = heal.Id, d4 = 1 });
        var batch = Of(peer, mark);
        Check(batch.Select(e => e.Code).SequenceEqual([EventCodes.EnergyUpdate, EventCodes.NewSimpleItem, EventCodes.CastFinished, EventCodes.CastSpell,
                  EventCodes.HealthUpdate, EventCodes.ActiveSpellEffectsUpdate, EventCodes.CastHit]),
              $"heal potion: 8, 32, 18, 19, 6, 11, 21 (got {Show(batch)})");
        var (energy, stack, finished, spell, tick, list, hit) = (batch[0].P, batch[1].P, batch[2].P, batch[3].P, batch[4].P, batch[5].P, batch[6].P);
        Check(!energy.ContainsKey(2) && L(stack, 2) == 4 && !stack.ContainsKey(3) && L(finished, 1) == me.Id && L(finished, 3) == 12 && F(finished, 6) == 1
              && L(spell, 1) == me.Id && L(hit, 0) == me.Id && L(hit, 1) == me.Id && L(hit, 2) == heal.Id && L(hit, 3) == 1 && L(hit, 4) == 1,
              $"8 without key 2, 32 {{2 4}}, 18 {{1 me, 3 12, 6 1}}, 19 {{1 me}}, 21 {{0 me, 1 me, 2 spell, 3 1, 4 counter}} (got {Show(batch)})");
        Check(About(F(tick, 2), change * max, 1e-3) && Convert.ToByte(tick[5]) == 5 && Convert.ToByte(tick[4]) == 2 && list[1] is short[] and [var listed] && listed == heal.Id
              && list[7] is byte[] and [0x03] && list[8] is int[] and [9000],
              $"the first tick at the hit: +0.013 × {max} = {change * max:0.###} unrounded, origin 5; ASEU [POTION_HEAL_P3] {{0x03}} (its over-time effect is index 0) [9000] (got {Show(batch.Skip(4).Take(2))})");
        mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.PotionSlot, d0 = heal.Id, d4 = 2 });
        Check(peer.Responses[^1].ReturnCode == WorldSession.CastRefused && me.Inventory.Equipment[8]!.Count == 4, "a second potion at once: refused (Cooldown), the stack stays 4");
        arena.Run(9.5);
        var ticks = Since(peer, mark).Where(e => e.Code == EventCodes.HealthUpdate && Convert.ToByte(e.P[5]) == 5).ToList();
        Check(ticks.Count == 9 && ticks.All(t => About(F(t.P, 2), change * max, 1e-3)), $"9 more ticks at +1..+9 s, each 0.013 × max (got {Show(ticks)})");
        var cooldown = me.Combat.SlotCooldowns[Consumables.PotionSlot];
        Check(cooldown.End - cooldown.Start == SpellCasts.Seconds(105) && cooldown.Base == SpellCasts.Seconds(105), "the potion slot's cooldown: 105 s × factor 1");

        // 4. A cancel during the 0.8 s soup: nothing taken.
        mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.FoodSlot, d0 = soup.Id, d4 = 3 });
        arena.Run(0.4);
        Send(session, new CastCancelRequest { wf = world.GameNow });
        arena.Run(1);
        Check(me.Inventory.Equipment[9]!.Count == 2 && !Since(peer, mark).Any(e => e.Code is EventCodes.NewSimpleItem or EventCodes.InventoryDeleteItem or EventCodes.CastFinished),
              "a cancelled soup: no 32/27, no CastFinished, the stack stays 2");

        // 3. The soup: CastFinished key 5 − start = 8,000,000 ticks; 91 {5: 2 × base}; ASEU [4191] 203.0748, 196.49039, {0x18}, [1800000].
        mark = peer.Events.Count;
        var t = world.GameNow;
        Send(session, new CastStartRequest { wf = t, dx = Consumables.FoodSlot, d0 = soup.Id, d4 = 4 });
        arena.Run(0.9);
        batch = Of(peer, mark);
        Check(batch.Select(e => e.Code).SequenceEqual([EventCodes.EnergyUpdate, EventCodes.NewSimpleItem, EventCodes.CastFinished, EventCodes.CastSpell,
                  EventCodes.RegenerationHealthChanged, EventCodes.ActiveSpellEffectsUpdate, EventCodes.CastHit]),
              $"soup: 8, 32, 18, 19, 91, 11, 21 (got {Show(batch)})");
        var regen = batch[4].P;
        list = batch[5].P;
        var eaten = list[4] is long[] and [var s1] ? s1 : 0;
        var bonus = soup.Effects.Where(e => e.Name.LocalName == "buffovertime" && (string?)e.Attribute("type") == "hitpointsregenerationbonus").Sum(e => SpellTemplate.Float(e, "value"));
        Check(About((float)(51.692886f * (1 + bonus)), 103.38577, 1e-4), $"the soup's hitpointsregenerationbonus {bonus}: the captured 51.692886 → 103.38577");
        Check(L(batch[2].P, 5) - t == 8_000_000 && L(batch[2].P, 1) == me.Id && About(F(regen, 5), 2 * baseRegen, 1e-3) && !regen.ContainsKey(4),
              $"CastFinished key 5 = start + 8,000,000 ticks, key 1 me; 91 {{5: 2 × {baseRegen}}} (got {Show(batch.Skip(2).Take(3))})");
        Check(list[1] is short[] and [4191] && list[2] is float[] and [var ap] && About(ap, 186 * 1.0918, 1e-3) && list[3] is float[] and [var ccAp] && About(ccAp, 186 * 1.0564, 1e-3)
              && list[7] is byte[] and [0x18] && list[8] is int[] and [1800000] && list[9] is byte[] and [0] && !list.ContainsKey(10),
              $"ASEU [4191] {{2 203.0748, 3 196.49039, 7 {{0x18}}, 8 [1800000], 9 {{0}}}} (got {Show(batch.Skip(5).Take(1))})");
        mark = peer.Events.Count;
        Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.FoodSlot, d0 = soup.Id, d4 = 5 });
        arena.Run(0.9);
        batch = Of(peer, mark);
        Check(batch.Select(e => e.Code).SequenceEqual([EventCodes.EnergyUpdate, EventCodes.InventoryDeleteItem, EventCodes.CharacterEquipmentChanged, EventCodes.CastFinished,
                  EventCodes.CastSpell, EventCodes.RegenerationHealthChanged, EventCodes.RegenerationHealthChanged, EventCodes.ActiveSpellEffectsUpdate, EventCodes.CastHit]),
              $"the second (last) soup: 8, 27, 90, 18, 19, 91 (base), 91 (buffed), 11, 21 (got {Show(batch)})");
        var equipment = batch[2].P;
        list = batch[7].P;
        var eaten2 = list[4] is long[] and [var s2] ? s2 : 0;
        Check(equipment[2] is int[] types && types[9] == 0 && equipment[7] is int[] slots && slots[13] == -1 && Convert.ToByte(equipment[6]) == 0
              && !equipment.ContainsKey(8) && !equipment.ContainsKey(9) && me.Inventory.Equipment[9] is null,
              $"90 {{2 [9] = 0, 6 0, 7 [13] = -1}}, no keys 8/9; the food slot is empty (got {Show(batch.Skip(2).Take(1))})");
        Check(About(F(batch[5].P, 5), baseRegen, 1e-3) && About(F(batch[6].P, 5), 2 * baseRegen, 1e-3) && list[1] is short[] and [4191] && eaten2 > eaten,
              "the re-eat: 91 with the base regeneration after the removal, then doubled; one 4191 entry with a new start");

        // 5. Over a relog: 10 s later the buff (its start, 1800000) and the potion's slot cooldown (249, key 3 Int64 ticks); 31 min later none.
        peer.Disconnect();
        arena.Clock.Advance(TimeSpan.FromSeconds(10));
        (_, peer, me) = Rejoin(arena, id);
        var join = peer.Responses[0].Parameters;
        var own = Since(peer, 0).Where(e => e.Code is EventCodes.ActiveSpellEffectsUpdate or EventCodes.SpellCooldownUpdate).ToList();
        Check(About(Convert.ToSingle(join[14]), 2 * baseRegen, 1e-3) && own.Count == 2 && own[0].Code == EventCodes.ActiveSpellEffectsUpdate && L(own[0].P, 0) == me.Id
              && own[0].P[1] is short[] and [4191] && own[0].P[4] is long[] and [var kept] && kept == eaten2 && own[0].P[8] is int[] and [1800000],
              $"rejoin +10 s: Join key 14 = 2 × base; the own list [4191] with the old start and 1800000 (got {Show(own)})");
        var cd = own[1].P;
        Check(cd[0] is byte[] and [12] && cd[3] is long[] and [1_050_000_000] && cd[4] is byte[] and [0] && cd[1] is long[] and [var end] && cd[2] is long[] and [var start]
              && end - start == SpellCasts.Seconds(105),
              $"SpellCooldownUpdate {{0 [12], 1 [end], 2 [start], 3 Int64 [1050000000], 4 [0]}} (got {Show(own.Skip(1))})");
        peer.Disconnect();
        arena.Clock.Advance(TimeSpan.FromMinutes(31));
        (_, peer, me) = Rejoin(arena, id);
        Check(About(Convert.ToSingle(peer.Responses[0].Parameters[14]), baseRegen, 1e-3) && me.Effects.Count == 0
              && !Since(peer, 0).Any(e => e.Code == EventCodes.SpellCooldownUpdate || e.Code == EventCodes.ActiveSpellEffectsUpdate && L(e.P, 0) == me.Id),
              "rejoin +31 min: the base regeneration, no list, no cooldown");
    }

    private static void FireworksAndEnergy()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (session, peer, me) = Join(arena, "Gunner", c => c.Equipment[8] = new InventoryItem("T3_VANITY_CONSUMABLE_FIREWORKS_GREEN", 3));
        var fireworks = T("VANITY_CONSUMABLE_FIREWORKS_GREEN");
        var mark = peer.Events.Count;
        for (byte i = 1; i <= 3; i++)
        {
            Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.PotionSlot, d0 = fireworks.Id, d2 = me.PositionAt(DateTime.UtcNow), d4 = i });
            arena.Run(0.1);
        }
        var all = Of(peer, mark);
        var stacks = all.Where(e => e.Code is EventCodes.NewSimpleItem or EventCodes.InventoryDeleteItem or EventCodes.CharacterEquipmentChanged).ToList();
        Check(stacks.Select(e => e.Code).SequenceEqual([EventCodes.NewSimpleItem, EventCodes.NewSimpleItem, EventCodes.InventoryDeleteItem, EventCodes.CharacterEquipmentChanged])
              && L(stacks[0].P, 2) == 2 && L(stacks[1].P, 2) == 1 && stacks[3].P[2] is int[] types && types[8] == 0 && stacks[3].P[7] is int[] slots && slots[12] == -1
              && !all.Any(e => e.Code is EventCodes.ActiveSpellEffectsUpdate or EventCodes.CastHit)
              && all.Where(e => e.Code == EventCodes.CastFinished).All(e => !e.P.ContainsKey(1)),
              $"fireworks: 32 {{2 2}}, 32 {{2 1}}, 27, 90 {{2 [8] 0, 7 [12] -1}}; no 11/21, no CastFinished key 1 (ground) (got {Show(stacks)})");
        Send(session, new CastStartRequest { wf = world.GameNow, dx = Consumables.PotionSlot, d0 = fireworks.Id, d2 = me.PositionAt(DateTime.UtcNow), d4 = 4 });
        Check(peer.Responses[^1].ReturnCode == WorldSession.CastRefused, "a fourth cast from the empty slot: refused (NotInSlot)");

        // 6. POTION_ENERGY_P3 at full energy: {2 round(0.371 × max) unclamped, 3 max, 4 2}, then 4 × {2 0.053 × max, 4 5} at +1, +3, +5, +7 s.
        var (s2, p2, mage) = Join(arena, "Mage", c => c.Equipment[8] = new InventoryItem("T4_POTION_ENERGY", 2));
        var potion = T("POTION_ENERGY_P3");
        // The captured bar 342/342 from the potion's data: +round(0.371 × 342) = 127 and 0.053 × 342 = 18.126 per tick.
        var direct = SpellTemplate.Float(potion.Effects.First(e => e.Name.LocalName == "directattributechange"), "change");
        var over = SpellTemplate.Float(potion.Effects.First(e => e.Name.LocalName == "attributechangeovertime"), "change");
        Check(MathF.Round(direct * 342) == 127 && About(over * 342, 18.126, 1e-3), $"POTION_ENERGY_P3: {direct} × 342 → 127, {over} × 342 = 18.126");
        var full = mage.Energy.Max;
        mark = p2.Events.Count;
        var t = world.GameNow;
        Send(s2, new CastStartRequest { wf = t, dx = Consumables.PotionSlot, d0 = potion.Id, d4 = 1 });
        arena.Run(7.2);
        var energy = Since(p2, mark).Where(e => e.Code == EventCodes.EnergyUpdate && L(e.P, 0) == mage.Id && e.P.ContainsKey(2)).ToList();
        Check(energy.Count == 5 && F(energy[0].P, 2) == MathF.Round(direct * full) && F(energy[0].P, 3) == full && Convert.ToByte(energy[0].P[4]) == 2
              && energy.Skip(1).All(e => About(F(e.P, 2), over * full, 1e-3) && Convert.ToByte(e.P[4]) == 5)
              && energy.Skip(1).Select(e => L(e.P, 1) - t).SequenceEqual(new[] { 1.0, 3, 5, 7 }.Select(SpellCasts.Seconds)),
              $"energy potion at {full}/{full}: +{MathF.Round(direct * full)} (key 3 {full}, origin 2), then 4 × +{over * full:0.###} (origin 5) at +1, +3, +5, +7 s (got {Show(energy)})");
        var list = Since(p2, mark).First(e => e.Code == EventCodes.ActiveSpellEffectsUpdate && L(e.P, 0) == mage.Id).P;
        Check(list[1] is short[] and [var id] && id == potion.Id && list[7] is byte[] and [0xd8, 0x00] && list[8] is int[] and [7000, 7000],
              $"ASEU [POTION_ENERGY_P3] {{0xd8, 0x00}} (ceil(3 × 3 / 8) = 2 bytes) [7000, 7000] (got {Show([(EventCodes.ActiveSpellEffectsUpdate, list)])})");
    }

    private static void Inventory()
    {
        using var arena = new Arena();
        var world = arena.World;
        var spells = Client.Spells!;
        // An item used from the inventory whose spell has an effect that does not run (a loot chest, an avatar, …).
        var unsupported = Client.Items.All.First(i => i.Kind == "consumablefrominventoryitem" && spells.Template(spells.ConsumeSpell(i.Name)) is { } s
            && s.Effects.Any(e => !SpellCasts.Supported.Contains(e.Name.LocalName)));
        // One the runner knows but which does nothing here (a fame, fishing or destiny buff, an applyspell of one; SPEC §4.3).
        var inert = Client.Items.All.First(i => i.Kind == "consumablefrominventoryitem" && spells.Template(spells.ConsumeSpell(i.Name)) is { } s
            && s.Effects.All(e => SpellCasts.Supported.Contains(e.Name.LocalName)) && !Consumables.Supported(s, spells));
        var (session, peer, me) = Join(arena, "Reader", c =>
        {
            c.Inventory.Add(new InventoryItem("T4_SKILLBOOK_STANDARD", 3));
            c.Inventory.Add(new InventoryItem("T4_SKILLBOOK_STANDARD", 1));
            c.Inventory.Add(new InventoryItem("T4_SKILLBOOK_STANDARD", 2));
            c.Inventory.Add(new InventoryItem(unsupported.Name, 1));
            c.Inventory.Add(new InventoryItem(inert.Name, 1));
        });
        var books = me.Inventory.Bag.Items.Where(i => i.Template.Name == "T4_SKILLBOOK_STANDARD").ToList();
        var tome = T("T4_SKILLBOOK_NONTRADABLE_SPELL");
        List<(object Code, Dictionary<byte, object?> P)> Seq(int from) => [.. peer.Sequence.Skip(from).Select(x => x is EventData e
            ? ((object)(EventCodes)Code(e), CombatTest.Wire(e)) : ((object)"R", new Dictionary<byte, object?> { [0] = ((OperationResponse)x).ReturnCode }))];
        bool Is(object code, EventCodes e) => code is EventCodes c && c == e;

        // 7. {0 tome, 1 3} on a stack of 3: NSI(3, true), 474 (1 s), 3 × 19 0.5 s apart, NSI(2, true), NSI(1, true), NSI(1), 27, R, 475.
        var mark = peer.Sequence.Count;
        Send(session, new UseConsumableFromInventoryRequest { ah5 = books[0].Id, ah6 = 3 });
        arena.Run(1.2);
        var seq = Seq(mark).Where(x => x.Code is "R" || Is(x.Code, EventCodes.NewSimpleItem) || Is(x.Code, EventCodes.BatchUseItemStart) || Is(x.Code, EventCodes.CastSpell)
            || Is(x.Code, EventCodes.InventoryDeleteItem) || Is(x.Code, EventCodes.BatchUseItemEnd)).ToList();
        string Codes(IEnumerable<(object Code, Dictionary<byte, object?> P)> s) => string.Join(", ", s.Select(x => x.Code is EventCodes e ? ((int)e).ToString() : "R"));
        Check(Codes(seq) == "32, 474, 19, 32, 19, 32, 19, 32, 27, R, 475",
              $"a batch of 3: 32, 474, 19, 32, 19, 32, 19, 32, 27, R, 475 (got {Codes(seq)})");
        var nsi = seq.Where(x => Is(x.Code, EventCodes.NewSimpleItem)).Select(x => x.P).ToList();
        var start = seq[1].P;
        var casts = seq.Where(x => Is(x.Code, EventCodes.CastSpell)).Select(x => L(x.P, 4)).ToList();
        Check(nsi.Select(n => (L(n, 2), n.ContainsKey(3))).SequenceEqual([(3L, true), (2L, true), (1L, true), (1L, false)]) && L(start, 2) - L(start, 1) == 10_000_000
              && casts.Zip(casts.Skip(1), (a, b) => b - a).All(d => d == SpellCasts.Seconds(0.5)) && Convert.ToInt32(seq[9].P[0]) == 0
              && seq.Where(x => Is(x.Code, EventCodes.CastSpell)).All(x => L(x.P, 1) == me.Id && L(x.P, 3) == tome.Id) && !me.Inventory.Bag.Contains(books[0]),
              $"NSI 3/2/1 in use, then 1 without key 3; 474 key 2 − key 1 = 10,000,000; CastSpell {{1 me, 3 tome}} 0.5 s apart; rc 0 (got {Show(seq.Where(x => x.Code is EventCodes).Select(x => ((EventCodes)x.Code, x.P)))})");
        var fame = Seq(mark).Where(x => Is(x.Code, EventCodes.UpdateFame)).Select(x => x.P).ToList();
        Check(fame.Count == 3 && fame.All(f => L(f, 2) == 10000 * 10000L && L(f, 7) == books[0].Template.Index && f[15] is true && !f.ContainsKey(5) && !f.ContainsKey(1)),
              $"each use: UpdateFame {{2 10000 fame, 7 the tome's type, 15 true}}, no key 5, the total fame unchanged (got {Show(fame.Select(f => (EventCodes.UpdateFame, f)))})");

        // 8. Count 1: no 474/475; NSI(1, true), 19, the effects, NSI(1), 27, R.
        mark = peer.Sequence.Count;
        Send(session, new UseConsumableFromInventoryRequest { ah5 = books[1].Id, ah6 = 1 });
        arena.Run(0.2);
        seq = [.. Seq(mark).Where(x => x.Code is "R" || x.Code is EventCodes c && c is EventCodes.NewSimpleItem or EventCodes.CastSpell or EventCodes.UpdateFame
            or EventCodes.InventoryDeleteItem or EventCodes.BatchUseItemStart or EventCodes.BatchUseItemEnd)];
        Check(Codes(seq) == "32, 19, 82, 32, 27, R", $"count 1: 32, 19, 82, 32, 27, R (got {Codes(seq)})");

        // 9. Refusals: an unknown object, an item whose spell does not run, a batch under way: rc 14960 and no event.
        void Refused(long item, short count, string what)
        {
            var events = peer.Events.Count;
            var responses = peer.Responses.Count;
            Send(session, new UseConsumableFromInventoryRequest { ah5 = item, ah6 = count });
            Check(peer.Responses.Count == responses + 1 && peer.Responses[^1].ReturnCode == Consumables.GenericFailed && peer.Events.Count == events, $"{what}: rc 14960, no event");
        }
        Refused(987654321, 1, "an unknown item object");
        Refused(me.Inventory.Bag.Items.First(i => i.Template == unsupported).Id, 1, $"{unsupported.Name} (its spell does not run yet)");
        Refused(me.Inventory.Bag.Items.First(i => i.Template == inert).Id, 1, $"{inert.Name} (its buff does nothing here)");
        Send(session, new UseConsumableFromInventoryRequest { ah5 = books[2].Id, ah6 = 2 });
        Refused(books[2].Id, 1, "a second use while a batch runs");
        arena.Run(0.7);
        Check(!me.Inventory.Bag.Contains(books[2]) && me.Combat.Batch is null, "the running batch of 2 finished");
    }
}
