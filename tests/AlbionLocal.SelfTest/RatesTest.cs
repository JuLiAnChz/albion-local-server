using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Commands;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The rate modifiers (config section Rates, <see cref="Rates"/>): the config with the old World:MobRespawnScale, data/rates.json,
/// .rates, and their effect where the quantities are computed: ×3 combat fame (UpdateFame), ×10 silver (the pile), ×2 harvest
/// (HarvestFinished key 5 and the stack), ×4 regrowth.
/// </summary>
public static class RatesTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("rates: config section, World:MobRespawnScale as Rates:Respawn, data/rates.json, .rates show/set/save/reset and its level", Config),
        ("rates: .rates combatfame 3 triples the kill fame (UpdateFame keys 2/3, total), silver 10 the pile", KillRates),
        ("rates: harvest 2 doubles a T1 tree's yield (HarvestFinished key 5, the stack), regrowth 4 a quarter of the time", HarvestRates),
    ];

    private static void Config()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "albionlocal.json");
        Check(ServerConfig.Load(path, ["--Preset=official"]).Rates == new Rates() && new Rates().Describe(changedOnly: true) == "all 1", "no file, Preset official: every rate 1 (pve: PveTest)");
        File.WriteAllText(path, """{ "Rates": { "Harvest": 2, "combatFame": 3 }, "World": { "MobRespawnScale": 0.01 } }""");
        var c = ServerConfig.Load(path, ["--Preset=official", "--Rates:Silver=1.5"]);
        Check(c.Rates is { Harvest: 2, CombatFame: 3, Silver: 1.5, Fame: 1 } && Math.Abs(c.Rates.Respawn - 100) < 1e-9,
              $"Rates keys (any case, command line too); World:MobRespawnScale 0.01 is Rates:Respawn 100 (got {c.Rates.Describe()})");
        bool Fails(params string[] args)
        {
            try { ServerConfig.Load(path, args); return false; }
            catch (InvalidDataException) { return true; }
        }
        Check(Fails("--Rates:Respawn=2") && Fails("--Rates:Loot=-1") && Fails("--Rates:Regrowth=0") && Fails("--Rates:Speed=2"),
              "refused: Rates:Respawn beside the old World:MobRespawnScale, a negative rate, regrowth 0, an unknown rate");

        // .rates: shown at GameMaster, set only by Admin; save writes the rates that differ from the config's, reset removes them.
        using var repo = Repository(dir);
        var world = new World(new ServerConfig { SavesPath = dir }, repo, Client.Items) { Sync = new object(), Clock = new ManualClock() };
        var replies = new List<(string Text, bool Error)>();
        void Run(AccountLevel level, string line)
        {
            replies.Clear();
            lock (world.Sync) world.Commands.Execute(new CommandContext(world, null, level, (t, e) => replies.Add((t, e))), line);
        }
        var file = Rates.FilePath(dir);
        Run(AccountLevel.GameMaster, ".rates");
        Check(replies is [{ Error: false, Text: var shown }] && shown.Contains("Harvest 1") && shown.Contains("Respawn 1"), $".rates shows every rate (got {string.Join(" / ", replies)})");
        Run(AccountLevel.GameMaster, ".rates harvest 2");
        Check(replies is [{ Error: true }] && world.Rates.Harvest == 1, "a GameMaster cannot change them");
        Run(AccountLevel.Admin, ".rates harvest 2.5 save");
        Check(replies is [{ Error: false }] && world.Rates.Harvest == 2.5 && File.Exists(file) && Rates.Load(file, new Rates()) == new Rates { Harvest = 2.5 },
              $"Admin: .rates harvest 2.5 save sets it and writes {file} (got {string.Join(" / ", replies)})");
        Run(AccountLevel.Admin, ".rates respawn 0");
        Check(replies is [{ Error: true }] && world.Rates.Respawn == 1, "respawn 0 is refused");
        world.MobRespawnScale = 0.5;
        Check(world.Rates.Respawn == 2 && world.MobRespawnScale == 0.5, ".mob respawnscale's factor is 1 / Rates:Respawn");
        Run(AccountLevel.Admin, ".rates reset save");
        Check(replies is [{ Error: false }] && world.Rates == new Rates() && !File.Exists(file), ".rates reset save: the config's rates, the file removed");
        world.Dispose();
    }

    private static void KillRates()
    {
        using var arena = new Arena();
        var world = arena.World;
        var (session, peer, me) = arena.Join("Rated", new InventoryItem("T4_MAIN_SWORD"));
        var replies = new List<string>();
        var gm = new CommandContext(world, me, AccountLevel.Admin, (t, _) => replies.Add(t));
        lock (world.Sync)
        {
            world.Commands.Execute(gm, ".rates combatfame 3");
            world.Commands.Execute(gm, ".rates silver 10");
        }
        Check(world.Rates is { CombatFame: 3, Silver: 10 }, $".rates combatfame 3, .rates silver 10 (got {string.Join(" / ", replies)})");
        var ghoul = arena.Spawn("T4_MOB_ROAMING_UNDEAD_GHOUL", new Vector2(1.5f, 0));
        lock (world.Sync) ghoul.Health.Set(1, Math.Max(world.GameNow, ghoul.Health.Stamp));
        var mark = peer.Events.Count;
        Send(session, new AttackStartRequest { wf = world.GameNow, co = ghoul.Id, cp = 0 });
        arena.Run(0.4);
        var fame = Since(peer, mark).Where(e => e.Code == EventCodes.UpdateFame).Select(e => e.P).ToList();
        Check(ghoul.Dead && fame is [var f] && L(f, 2) == 3 * 2021000 && L(f, 3) == 3 * 1081000 && L(f, 1) == 3 * 2021000 && me.Character.Fame == 3 * 2021000,
              $"the ghoul's 202.1 fame (108.1 bonus) ×3: UpdateFame 606.3 / 324.3, total 606.3 (got {Show(Since(peer, mark).Where(e => e.Code == EventCodes.UpdateFame))})");
        var piles = world.Maps.Find("0204")!.Objects.Values.OfType<SilverPile>().Where(p => p.SourceId == ghoul.Id).ToList();
        Check(piles is [{ Amount: >= 460 and <= 500 }], $"silver ×10: a pile of 471-494 instead of 47-49 (got {string.Join(", ", piles.Select(p => p.Amount))})");
    }

    private static void HarvestRates()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true, Rates = new Rates { Harvest = 2, Regrowth = 4 } }, characters, Client.Items, Client.GameData)
            { Sync = new object(), Clock = clock, Clusters = Client.Clusters };
        // The T1 tree of HarvestTest near the Thetford start (WOOD T1, 2 charges: 2 wood by hand in 2 s), the player 2 m west of it.
        var tree = Client.Clusters!.Get("0000").Harvestables.OrderBy(h => Vector2.Distance(h.Position, new Vector2(44.5f, 3.5f))).First();
        var c = StarterKit.Default.NewCharacter("Gatherer");
        c.Cluster = "0000";
        (c.PositionX, c.PositionY) = (tree.Position.X - 2, tree.Position.Y);
        c.Equipment = [];
        c.Inventory = [];
        Check(characters.Create(c), "character created");
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
        var player = session.Player!;
        var node = player.Map!.Objects.Values.OfType<Harvestable>().Single(h => h.Key == tree.Key);
        var mark = peer.Events.Count;
        session.OnRequest(Request(new HarvestStartRequest { Start = GameTime.Now + TimeSpan.TicksPerSecond, HarvestableId = node.Id, ToolObjectId = -1 }, 78), encrypted: false);
        clock.Advance(TimeSpan.FromSeconds(2));
        lock (world.Sync) world.Update();
        var finished = Since(peer, mark).Where(e => e.Code == EventCodes.HarvestFinished).Select(e => e.P).ToList();
        var wood = player.Inventory.Bag.Items.FirstOrDefault(i => i.Template.Name == "T1_WOOD");
        Check(finished is [var h] && L(h, 5) == 4 && wood is { Count: 4 } && node.Charges == 0,
              $"harvest ×2: HarvestFinished key 5 = 4 (2 at rate 1), a stack of 4 T1_WOOD, the tree's 2 charges taken (got {Show(Since(peer, mark).Where(e => e.Code == EventCodes.HarvestFinished))}, stack {wood?.Count})");
        Check(node.RegrowAt is { } at && at - clock.Now <= TimeSpan.FromSeconds(45),
              $"regrowth ×4: the next charge within 180 / 4 = 45 s (at rate 1: 60-180 s; got {(node.RegrowAt - clock.Now)?.TotalSeconds:0.#} s)");
        // A yield above the stack limit (a harvest at .rates harvest 1000) fits no free slot: the start is refused as inventory full.
        Check(Harvesting.Fit(player.Inventory.Bag, Client.Items.Find("T1_WOOD")!, 2000) is null && Harvesting.Fit(player.Inventory.Bag, Client.Items.Find("T1_WOOD")!, 2) is not null,
              "2000 wood fit no slot (stack limit), 2 do");
        world.Dispose();
        characters.Dispose();
    }

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);
}
