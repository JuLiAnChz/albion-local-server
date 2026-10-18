using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>Buff shrines (analysis/work/stageN/shrines/SPEC.md): a solo dungeon floor places one; UseShrine and the slot-25 cast apply its buff once.</summary>
public static class ShrineTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("shrines: a solo dungeon floor has GENERAL_SHRINE_COMBAT_BUFF; NewShrine 396, UseShrine rc 0, OPEN_BUFFSHRINE in slot 25 -> a BUFFSHRINE buff, 397 spent, a second use 10165", Use),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static int Code(EventData e) => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode));

    private static void Use()
    {
        var store = ResourceNodeTest.Day(Capture002835Key57);
        _ = store.Get("0204");
        var def = store.BuffShrines.Find("GENERAL_SHRINE_COMBAT_BUFF");
        Check(def is { UseSpell: "OPEN_BUFFSHRINE", EffectSpell: "BUFFSHRINE_RANDOM_BUFFS", Respawns: false, MaxCharges: 1, Supported: true },
              "buffshrines.xml: GENERAL_SHRINE_COMBAT_BUFF opens with OPEN_BUFFSHRINE, applies BUFFSHRINE_RANDOM_BUFFS, does not respawn");

        // The shrine layer of a solo room (weightedlayerblock "shrine", 2 in 10) on some floor of the first seeds.
        var data = store.Dungeons;
        BuffShrineSpawn? found = null;
        for (var seed = 0; seed < 40 && found is null; seed++)
        {
            var plan = DungeonGenerator.Draw(data.Generators["HERETIC_RANDOM_DUNGEON_SOLO"], 1, new Random(seed), 1);
            var links = new DungeonGenerator.Links("0204", "", Guid.Empty, "0204", "", Guid.Empty, "0204", new Vector2(90, -260));
            var (_, t) = RandomDungeons.MakeFloor(store, data, plan, 1, DungeonLayout.InstanceId(Guid.NewGuid()), 4, "RD_SAFEAREA", 1, links, DungeonGenerator.IsDragon(data, plan), seed);
            found = t.BuffShrines.FirstOrDefault();
        }
        Check(found is { Tile: "SHRINE_NON_COMBAT_BUFF", Definition.Name: "GENERAL_SHRINE_COMBAT_BUFF" }, "a solo dungeon floor places a GENERAL_SHRINE_COMBAT_BUFF shrine");

        var characters = Repository(NewDir());
        var clock = new ManualClock();
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = store, MapSeed = 5,
        };
        world.MobAi = false;
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }
        var c = StarterKit.Default.NewCharacter("S" + Guid.NewGuid().ToString("N")[..10]);
        (c.Cluster, c.PositionX, c.PositionY) = ("0204", 91, -259);
        Check(characters.Create(c), "character created");
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        lock (world.Sync) session.OnRequest(Request(new JoinRequest { ss = c.Id }, null), encrypted: true);
        var me = session.Player!;
        var map = world.Maps.Find("0204")!;
        BuffShrine shrine;
        lock (world.Sync) shrine = world.Shrines.Place(map, found! with { Position = new Vector2(93, -259) });
        Tick(0.1);
        var created = peer.Events.LastOrDefault(e => Code(e) == (int)EventCodes.NewShrine)?.Parameters;
        Check(created is not null && Convert.ToInt64(created[0]) == shrine.Id && created[3] is "GENERAL_SHRINE_COMBAT_BUFF" && created[4] is "SHRINE_NON_COMBAT_BUFF"
              && Convert.ToInt32(created[5]) == BuffShrine.Ready, "NewShrine {0 id, 3 uniquename, 4 tile, 5 1 (ready)}");

        OperationResponse? Send(AlbionRequest req, int id)
        {
            var count = peer.Responses.Count;
            lock (world.Sync) session.OnRequest(Request(req, id), encrypted: false);
            return peer.Responses.Count > count ? peer.Responses[^1] : null;
        }
        int Buffs() => me.Effects.Spells.Count(s => s.Spell.Name.StartsWith("BUFFSHRINE_RANDOM_BUFF_", StringComparison.Ordinal));
        var mark = peer.Events.Count;
        var use = Send(new UseShrineRequest { wf = world.GameNow, aij = shrine.Id, aik = BuffShrine.Ready }, 10);
        var cast = Send(new CastStartRequest { wf = world.GameNow, dx = FakeSlotCasts.Slot, d0 = world.Spells!.IdOf("OPEN_BUFFSHRINE"), d2 = shrine.Position, d4 = 1 }, 11);
        Tick(0.1);
        var buffs = Buffs();
        var update = peer.Events.Skip(mark).LastOrDefault(e => Code(e) == (int)EventCodes.UpdateShrine)?.Parameters;
        Check(use is { ReturnCode: 0 } && cast is null && buffs >= 1 && shrine.State == BuffShrine.Spent
              && update is not null && Convert.ToInt64(update[0]) == shrine.Id && Convert.ToInt32(update[1]) == BuffShrine.Spent,
              $"UseShrine rc 0, the slot-25 OPEN_BUFFSHRINE (no answer) applies {buffs} BUFFSHRINE_RANDOM_BUFF_* buff(s); UpdateShrine {{1 2}} (on: {string.Join(", ", me.Effects.Spells.Select(s => s.Spell.Name))})");
        var again = Send(new UseShrineRequest { wf = world.GameNow, aij = shrine.Id, aik = BuffShrine.Ready }, 12);
        var recast = Send(new CastStartRequest { wf = world.GameNow, dx = FakeSlotCasts.Slot, d0 = world.Spells.IdOf("OPEN_BUFFSHRINE"), d2 = shrine.Position, d4 = 2 }, 13);
        Tick(0.1);
        Check(again is { ReturnCode: BuffShrines.NotUsable } && recast is { ReturnCode: WorldSession.CastRefused } && Buffs() == buffs,
              "a spent shrine: UseShrine 10165, the cast refused (960), no second buff");
    }
}
