using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Scripting;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>The world tick driven by hand: <see cref="FakePeer"/> sessions on a <see cref="ManualClock"/>, no UDP and no loop timer.</summary>
public static class WorldTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("world: map of joined players, Post in the next tick, keepalive once a second to joined sessions only", Keepalive),
        ("world: saves after the change delay, with the periodic save, at once on disconnect", Saves),
        ("scripts: item script and player hooks at join, moves, item add and logout; none in the server; a conflict stops", Scripts),
        ("bars: join fills them, regeneration is lazy up to the cap", Bars),
    ];

    /// <summary>Records every call; bound by a mask without '@', so the sword's enchantment has it too.</summary>
    [ItemScript("T?_MAIN_SWORD")]
    private sealed class Recorder : ItemScript, IPlayerHooks
    {
        public readonly List<string> Calls = [];
        public override void OnEquip(Player p, Item item) => Calls.Add($"equip {item.Template.Name}");
        public override void OnUnequip(Player p, Item item) => Calls.Add($"unequip {item.Template.Name}");
        public void OnLogin(Player p, bool first) => Calls.Add($"login {first}");
        public void OnItemAdded(Player p, Item item) => Calls.Add($"added {item.Template.Name} x{item.Count}");

        /// <summary>Also wears the weapon down: an item change in OnLogout is saved with the character.</summary>
        public void OnLogout(Player p)
        {
            Calls.Add($"logout {(p.Map is null ? "off" : "on")} the map");
            if (p.Inventory.Equipment[0] is { } weapon) weapon.DurabilityRaw = 5000;
        }
    }

    /// <summary>Narrower than the exact base name on the enchantments (that name covers the base item and all of them).</summary>
    [ItemScript("T4_MAIN_SWORD@?")]
    private sealed class EnchantedSword : ItemScript;

    [ItemScript("t4_main_sword")]
    private sealed class OtherSword : ItemScript;

    [ItemScript("T4_MAIN_SWORD")]
    private sealed class SameSword : ItemScript;

    private static void Scripts()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var hero = characters.List()[0];
        hero.Equipment = [new("T4_MAIN_SWORD"), null, null, null, null, null, null, null, null, null];
        hero.Inventory = [new("T4_ORE", 10)];
        characters.Save(hero);
        var items = Items("8610\tT4_MAIN_SWORD\tweapon\tmainhand\t4\t13500", "8611\tT4_MAIN_SWORD@1\tweapon\tmainhand\t4\t13500", "1000\tT4_ORE\tsimpleitem\t\t4");
        var server = ScriptRegistry.Build(items);
        Check(server.PlayerHooks.Count == 0 && server.Bindings.Count == 0 && server.ItemScriptOf(items.Find("T4_MAIN_SWORD")!) is null,
              "the server assembly registers no scripts yet");
        try { _ = new ScriptRegistry([typeof(OtherSword), typeof(SameSword)], items); Check(false, "two exact bindings on one item were accepted"); }
        catch (InvalidOperationException e) { Check(e.Message.Contains("equally precise"), $"the error names the conflict (got {e.Message})"); }
        var ranked = new ScriptRegistry([typeof(SameSword), typeof(EnchantedSword)], items);
        Check(ranked.ItemScriptOf(items.Find("T4_MAIN_SWORD")!) is SameSword && ranked.ItemScriptOf(items.Find("T4_MAIN_SWORD@1")!) is EnchantedSword,
              "the exact base name binds the base item, the @-mask its enchantment");
        var scripts = new ScriptRegistry([typeof(Recorder)], items);
        var recorder = (Recorder)scripts.PlayerHooks[0];
        Check(scripts.ItemScriptOf(items.Find("T4_MAIN_SWORD@1")!) == recorder && scripts.ItemScriptOf(items.Find("T4_ORE")!) is null,
              "one instance for both roles, the mask covers the enchantment, not the ore");

        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, items, scripts: scripts) { Sync = new object(), Clock = new ManualClock() };
        (WorldSession, FakePeer) Join()
        {
            var peer = new FakePeer();
            var session = new WorldSession(peer, world);
            peer.Disconnected = session.OnDisconnected;
            session.OnRequest(Request(new JoinRequest { ss = hero.Id }, 1), encrypted: false);
            return (session, peer);
        }
        var (session, peer) = Join();
        Check(recorder.Calls is ["equip T4_MAIN_SWORD", "login True"], $"join: the equipped sword, then the login of a new character (got {string.Join("; ", recorder.Calls)})");
        recorder.Calls.Clear();
        void Move(int from, Guid fc, int to, Guid tc) =>
            session.OnRequest(Request(new InventoryMoveItemRequest { ru = from, rv = fc, rx = to, ry = tc, rz = 1 }, null), encrypted: false);
        Move(0, hero.EquipmentContainer, 3, hero.InventoryContainer);
        Move(3, hero.InventoryContainer, 0, hero.EquipmentContainer);
        Move(0, hero.InventoryContainer, 1, hero.InventoryContainer);
        session.OnRequest(Request(new SayRequest { aa9 = ".item add T4_ORE 2" }, null), encrypted: false);
        peer.Disconnect();
        Check(recorder.Calls is ["unequip T4_MAIN_SWORD", "equip T4_MAIN_SWORD", "added T4_ORE x2", "logout on the map"],
              $"out of and into the equipment (not inside the bag), the added ore, the logout before leaving the map (got {string.Join("; ", recorder.Calls)})");
        Check(session.Player!.Character.Equipment[0]?.Durability == 5000, "what OnLogout changes in the items is in the saved character");
        recorder.Calls.Clear();
        var (again, _) = Join();
        Check(recorder.Calls is ["equip T4_MAIN_SWORD", "login False"], "a second Join is no first login");
        recorder.Calls.Clear();
        again.OnRequest(Request(new JoinRequest { ss = hero.Id }, 1), encrypted: false);
        Check(recorder.Calls is ["logout on the map", "equip T4_MAIN_SWORD", "login False"],
              $"a new Join on one connection logs the old player out first (got {string.Join("; ", recorder.Calls)})");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Bars()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var hero = characters.List()[0];
        hero.Equipment = [];
        hero.Inventory = [];
        characters.Save(hero);
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Items()) { Sync = new object(), Clock = new ManualClock() };
        var session = new WorldSession(new FakePeer(), world);
        session.OnRequest(Request(new JoinRequest { ss = hero.Id }, 1), encrypted: false);
        var p = session.Player!;
        Check(p.Health is { Value: 1200, Max: 1200, RegenPerSecond: 12, RegenCap: 0 } && p.Energy is { Value: 120, Max: 120 } && p.MountHealth.Max == 0
              && Math.Abs(p.Health.Stamp - DateTime.UtcNow.Ticks) < TimeSpan.TicksPerMinute, "without game data: a naked character's bars, full, stamped now");
        var bar = new ResourceBar { Max = 100, RegenPerSecond = 10 };
        bar.Set(50, 0);
        Check(bar.At(2 * TimeSpan.TicksPerSecond) == 70 && bar.At(60 * TimeSpan.TicksPerSecond) == 100 && bar.At(-TimeSpan.TicksPerSecond) == 50,
              "value + regen * dt, capped at the maximum, never below the stamped value");
        bar.RegenCap = 0.8f;
        Check(bar.At(60 * TimeSpan.TicksPerSecond) == 80, "a regeneration cap stops it at that share");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Keepalive()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var hero = characters.List()[0];
        hero.Equipment = [];
        hero.Inventory = [];
        characters.Save(hero);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Items()) { Sync = new object(), Clock = clock };
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        peer.Disconnected = session.OnDisconnected;
        session.OnRequest(Request(new JoinRequest { ss = hero.Id }, 1), encrypted: false);
        var waiting = new FakePeer();
        _ = new WorldSession(waiting, world); // connected, never joins
        var map = world.Maps.Find(hero.Cluster);
        var player = session.Player!;
        Check(map?.Players.GetValueOrDefault(player.Id) == player && player.Map == map, "join puts the player on the map of its cluster");

        var ran = false;
        world.Post(() => ran = true);
        Check(!ran, "a posted action waits for the tick");
        // Three seconds of 50 ms ticks; the official server's first keepalive comes about 1 s after the Join response.
        string Wire(EventData e)
        {
            var w = new ByteWriter();
            Protocol18.WriteEventData(w, e);
            return Convert.ToHexString(w.ToArray()).ToLowerInvariant();
        }
        for (var i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            world.Update();
            Check(ran, "the posted action ran in the first tick");
        }
        Check(peer.Unsequenced.Select(u => $"{u.Channel}:{Wire(u.Event)}").ToArray() is ["1:0202000b010120", "1:0202000b020120", "1:0202000b030120"]
              && waiting.Unsequenced.Count == 0 && waiting.Events.Count == 0,
              "3 s after Join: keepalives 1, 2, 3 unsequenced on channel 1 with the official bytes (Int1 counter, FloatZero); none to a session that has not joined");

        var unhandled = OpcodeTable.For(typeof(WorldSession)).Unhandled.GetValueOrDefault(OperationCodes.Unused);
        session.OnRequest(new OperationRequest(2, new() { [0] = 6, [1] = 0f }), encrypted: false);
        Check(peer.Responses.Count == 1 && OpcodeTable.For(typeof(WorldSession)).Unhandled.GetValueOrDefault(OperationCodes.Unused) == unhandled,
              "the client's op 2 gets no response and is not an unhandled Unused");

        peer.Disconnect();
        var sent = peer.Unsequenced.Count;
        clock.Advance(TimeSpan.FromSeconds(2));
        world.Update();
        Check(map!.Players.Count == 0 && peer.Unsequenced.Count == sent, "a disconnect takes the player off the map and ends its keepalive");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// <see cref="SaveScheduler"/> on manual ticks: an item change is written <see cref="SaveScheduler.ChangeDelay"/> later,
    /// a walk with the periodic save (the position extrapolated then), a disconnect at once.
    /// </summary>
    private static void Saves()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var hero = characters.List()[0];
        hero.Equipment = [];
        hero.Inventory = [];
        characters.Save(hero);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Items()) { Sync = new object(), Clock = clock };
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        peer.Disconnected = session.OnDisconnected;
        session.OnRequest(Request(new JoinRequest { ss = hero.Id }, 1), encrypted: false);
        var live = session.Character!;
        void Ticks(double seconds)
        {
            for (var i = 0; i < seconds * 20; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(50));
                world.Update();
            }
        }
        Character Saved()
        {
            world.Saves.Flush();
            return characters.Find(hero.Id)!;
        }
        void Walk(float x, float y) => session.OnRequest(Request(new MoveRequest { v6 = [x, y], v8 = [x, y], v9 = 0f }, null), encrypted: false);

        Ticks(0.05);
        live.Inventory = [new("T4_ORE", 3)];
        world.Saves.MarkChanged(live); // as InventoryMoveItem after a move
        Ticks(1.5);
        Check(Saved().Inventory.Count == 0, "an item change is not written before the delay");
        Ticks(1);
        Check(Saved().Inventory is [{ Name: "T4_ORE", Count: 3 }], "and written 2 s after the change");
        Walk(4, 4);
        Ticks(30);
        Check(Saved().PositionX == 0, "a walk waits for the periodic save");
        Ticks(30);
        Check(Saved() is { PositionX: 4, PositionY: 4 }, "which writes where the player is, every 60 s");
        Walk(7, 7);
        peer.Disconnect();
        Check(Saved() is { PositionX: 7, PositionY: 7 }, "a disconnect saves at once");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);
}
