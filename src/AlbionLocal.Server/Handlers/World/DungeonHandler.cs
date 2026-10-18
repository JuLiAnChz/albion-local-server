using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The cluster changes of the random dungeons (analysis/work/stageG/dungeons/SPEC.md, "SPEC" here, §2.2-§2.7, §6.2): into a
/// dungeon through an open-world exit, between its floors, out of it, and the dungeon escape. They are ChangeCluster requests
/// like any exit's (op 41) and end in the same commit (<see cref="Commit"/>); an instanced target is answered with exactly
/// {0: instance id, 3: layout bytes} (§2.3: never key 0 without key 3 for a "@RANDOMDUNGEON@" id, the client would throw in
/// ciy.a_9), a way out with {0: entrance cluster}. A floor not made yet is built in the background and the answer follows with
/// the kept request id, as a target cluster still being resolved (1669 meanwhile).
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// Whether the request is a dungeon change, and its answer (null = deferred or none): the escape form {0: 0, 3: true} inside a
    /// dungeon (§2.7), a <see cref="RandomDungeonExit"/>, or an exit of a dungeon floor. False = an ordinary exit (or no object).
    /// </summary>
    private bool DungeonChange(Player player, ChangeClusterRequest req, int? requestId, bool encrypted, out ChangeClusterResponse? response)
    {
        response = null;
        if (player.Map is not { } map) return false;
        var floor = world.Dungeons.Find(map.Cluster);
        if (req.eh && floor is { } here)
        {
            response = Escape(player, here.Group);
            return true;
        }
        if (!map.Objects.TryGetValue(req.ee, out var o)) return false;
        if (o is RandomDungeonExit entrance)
        {
            response = EnterDungeon(player, entrance, requestId, encrypted);
            return true;
        }
        if (o is Exit exit && floor is { } f)
        {
            response = FloorExit(player, exit, f.Group, f.Index, requestId, encrypted);
            return true;
        }
        return false;
    }

    /// <summary>
    /// §2.2, §6.2: into the dungeon behind <paramref name="exit"/>. Checked: knocked down (1640), in pos ± max(size/2,
    /// Dungeons:MinSquare/2) + Transfer:PositionTolerance (1640), the exit cooldown (1647), a map's reservation (1641); then the player's dungeon (made on
    /// the first use) and its floor 1, arriving at the floor's exit …0001; the exit counts as used. The exit must still stand on
    /// the player's map (1642, §2.2): a retry after a floor build may come after it despawned.
    /// </summary>
    private ChangeClusterResponse? EnterDungeon(Player player, RandomDungeonExit exit, int? requestId, bool encrypted)
    {
        if (!player.Map!.Objects.TryGetValue(exit.Id, out var live) || !ReferenceEquals(live, exit))
            return Refuse(player, exit.Id, ChangeClusterRefusal.ExitInfoNotFound, $"random dungeon exit {exit.Name} is gone");
        if (player.Combat.Down is { } down) return Refuse(player, exit.Id, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
        // StageL corrupted (SPEC §2.2): a corrupted portal opens the client's entrance window, whose Enter is the slot-25 cast, never a change.
        if (exit.Info.Corrupted) return Refuse(player, exit.Id, ChangeClusterRefusal.ExitNotConnected, $"{exit.Name} is a corrupted portal (its entrance window casts the entry)");
        // StageL mists (SPEC §3.5): a mists portal is entered by ENTER_MISTS in slot 25 (Systems/Mists), never by a change.
        if (exit.Info.MistsMode is not null) return Refuse(player, exit.Id, ChangeClusterRefusal.ExitNotConnected, $"{exit.Name} is a mists portal (ENTER_MISTS enters it)");
        var size = Vector2.Max(exit.Size, new Vector2(world.DungeonSettings.MinSquare));
        if (Reach(player, player.Map!, exit.Position, size, $"random dungeon exit {exit.Name}") is { } refused)
            return Refuse(player, exit.Id, refused.Code, refused.Why);
        // §2.2, after the reach: a map's reservation lets in only the characters it lists and the members of its party now (1641, [assume] for the party).
        if (exit.Reservation is { } reserved && !reserved.Characters.Contains(player.Character.Id)
            && !(world.Parties.Of(player.Character.Id) is { } party && reserved.Parties.Contains(party.Id)))
            return Refuse(player, exit.Id, ChangeClusterRefusal.ExitNotConnected, $"random dungeon exit {exit.Name} is reserved ({reserved.Profile})");
        if (world.Dungeons.GroupFor(player, exit) is not { } group)
            return Refuse(player, exit.Id, ChangeClusterRefusal.ExitNotConnected, $"{exit.Name}: no dungeon can be made ({exit.Generator}; randomdungeons.xml read?)");
        return ToFloor(player, group, 0, DungeonExits.Up, exit.Id, requestId, encrypted, exit, () => EnterDungeon(player, exit, requestId, encrypted));
    }

    /// <summary>
    /// ExitEnterStart (237) on a random dungeon portal (stageM exit NOTES §2, §4): a prefab in usage mode 1 (the PORTAL_SOLO ones) is
    /// clicked, and the client channels for gamedata ExitSettings/@channeledexitusagetime (2 s) instead of sending ChangeCluster. Checked
    /// as <see cref="EnterDungeon"/> checks a walk-in, up to the reservation: not corrupted (its window casts the entry, the client never
    /// sends 237 for it) nor a mists portal (ENTER_MISTS casts it), not knocked down, no change of this session waiting, in the square and past the exit cooldown. Then the channel
    /// (<see cref="RandomDungeons.StartExit"/>; null). Else why not: the handler sends 254.
    /// </summary>
    private string? StartDungeonExit(Player player, RandomDungeonExit exit)
    {
        if (exit.Info.Corrupted) return "a corrupted portal (its entrance window casts the entry)";
        if (exit.Info.MistsMode is not null) return "a mists portal (ENTER_MISTS in slot 25 enters it)"; // stageL mists SPEC §3.5
        if (player.Combat.Down is { } down) return $"knocked down ({down.Kind})";
        if (_changing is not null) return $"exit object {_changing.ExitId} is being resolved already";
        var size = Vector2.Max(exit.Size, new Vector2(world.DungeonSettings.MinSquare));
        if (Reach(player, player.Map!, exit.Position, size, $"random dungeon exit {exit.Name}") is { } refused) return $"{refused.Code}: {refused.Why}";
        if (exit.Reservation is { } reserved && !reserved.Characters.Contains(player.Character.Id)
            && !(world.Parties.Of(player.Character.Id) is { } party && reserved.Parties.Contains(party.Id)))
            return $"reserved ({reserved.Profile})";
        world.Dungeons.StartExit(player, exit);
        return null;
    }

    /// <summary>
    /// The channel at <paramref name="exit"/> is done (<see cref="RandomDungeons.UpdateExits"/>, after 255): the entry as a walk-in makes
    /// it (<see cref="EnterDungeon"/>), its answer pushed without a request id, as <see cref="Teleport"/> pushes ({0: instance id, 3:
    /// layout}). A floor still being made answers later the same way (<see cref="ResumeDungeon"/>). A refusal is no push (an error RC
    /// with no request id is not known to be handled): 254 and a chat line.
    /// </summary>
    internal void EnterChannelled(Player player, RandomDungeonExit exit)
    {
        if (_closed || Status != SessionStatus.InWorld || Player != player) return;
        var resp = _changing is not null
            ? Refuse(player, exit.Id, ChangeClusterRefusal.AlreadyEnteringExit, $"exit object {_changing.ExitId} is being resolved already")
            : EnterDungeon(player, exit, requestId: null, encrypted: false);
        if (resp is not null) PushChannelled(player, resp);
    }

    /// <summary>The answer of a channelled entry: RC 0 pushed as the ChangeCluster response; a refusal as 254 and a system line.</summary>
    private void PushChannelled(Player player, ChangeClusterResponse resp)
    {
        if (resp.ReturnCode == 0)
        {
            SendResponse(OperationCodes.ChangeCluster, resp, requestId: null, encrypted: false);
            return;
        }
        player.SendAround(new ExitEnterCancelEvent { ddn = player.Id });
        Expeditions.Tell(player, $"The dungeon cannot be entered now ({(ChangeClusterRefusal)resp.ReturnCode}).");
    }

    /// <summary>
    /// §2.7: an exit of floor <paramref name="index"/>: …0001 goes up (out from floor 1, arriving in front of the open-world exit),
    /// …0002 down (out from the last floor); the other floor is entered at its exit of the other Guid.
    /// </summary>
    private ChangeClusterResponse? FloorExit(Player player, Exit exit, DungeonGroup group, int index, int? requestId, bool encrypted)
    {
        if (player.Combat.Down is { } down) return Refuse(player, exit.Id, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
        if (Reach(player, player.Map!, exit.Position, Transfers.Size(exit.Spawn), $"dungeon exit {Short(exit.World.Id)}") is { } refused)
            return Refuse(player, exit.Id, refused.Code, refused.Why);
        var up = exit.World.Id == DungeonExits.Up;
        if (up && index == 0 || !up && index + 1 >= group.FloorCount) return LeaveDungeon(player, group, up ? "up" : "down");
        return ToFloor(player, group, up ? index - 1 : index + 1, up ? DungeonExits.Down : DungeonExits.Up, exit.Id, requestId, encrypted, null,
            () => FloorExit(player, exit, group, index, requestId, encrypted));
    }

    /// <summary>
    /// §2.7 "Dungeon escape": {0: 0L, 1: null, 2: ddp.k, 3: true} after the escape spell (c32.j), inside a dungeon: out to the
    /// entrance as through …0001 of floor 1 (no square, no cooldown: the client sends it only when the spell is done).
    /// </summary>
    private ChangeClusterResponse? Escape(Player player, DungeonGroup group)
    {
        if (player.Combat.Down is { } down) return Refuse(player, 0, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
        return LeaveDungeon(player, group, "escape");
    }

    /// <summary>Out to the entrance cluster at the exit's spawnpos (§6.4), answered {0: entrance cluster} (a world.xml id, no layout).</summary>
    private ChangeClusterResponse LeaveDungeon(Player player, DungeonGroup group, string how)
    {
        Commit(player, group.Out, $"dungeon:{group.Ids[0]}:{how}");
        return new ChangeClusterResponse { anu = group.Out.Cluster }; // [0] the entrance, byte-identical to the new Join's key 8
    }

    /// <summary>
    /// Into floor <paramref name="index"/> at its exit <paramref name="door"/>: the commit, then {0: id, 3: layout}. A floor not made
    /// yet is built (<see cref="RandomDungeons.Build"/>) and <paramref name="retry"/> runs again when it is in. Coming from the
    /// open world (<paramref name="entrance"/>), the exit counts as used and the character keeps the way back
    /// (<see cref="Character.DungeonReturn"/>) for when the dungeon is gone at its next login.
    /// </summary>
    private ChangeClusterResponse? ToFloor(Player player, DungeonGroup group, int index, Guid door, long exitId, int? requestId, bool encrypted,
        RandomDungeonExit? entrance, Func<ChangeClusterResponse?> retry)
    {
        if (group.Floors[index] is not { } floor)
        {
            Task<DungeonFloor> build;
            try { build = world.Dungeons.Build(group, index); }
            catch (InvalidOperationException e) { return Refuse(player, exitId, ChangeClusterRefusal.ExitNotConnected, e.Message); }
            var pending = new PendingChange(player, player.Map!, exitId, requestId, encrypted);
            _changing = pending;
            world.Dungeons.Sent(group, player.Character.Id); // not empty while the floor is made for this player
            Log.Info(Name, $"'{player.Character.Name}' goes to floor {index + 1} of {group.Ids[0]}: making it, the answer follows");
            build.ContinueWith(t => world.Post(() => ResumeDungeon(pending, group, index, t, retry)), TaskScheduler.Default);
            return null;
        }
        // SPEC §6.4: the target exit's spawnpos as it is (the bank doors' collision push of stage 3 is not known for dungeon exits).
        if (floor.Through(door, 0) is not { } to)
            return Refuse(player, exitId, ChangeClusterRefusal.ExitNotConnected, $"floor {floor.Id} has no exit {door} with a spawnpos");
        if (entrance is not null)
        {
            world.Dungeons.Used(entrance);
            world.Dungeons.Entering(player, group); // stageH: the dungeon of the day's chat line, sent after the floor's Join
            player.Character.DungeonReturn = new DungeonReturn(group.Out.Cluster, group.Out.Position.X, group.Out.Position.Y, group.Out.Direction);
        }
        world.Dungeons.Sent(group, player.Character.Id); // occupied until the Join of the loading client
        Commit(player, to, $"dungeon:{floor.Id}");
        return new ChangeClusterResponse { anu = floor.Id, anx = floor.Bytes }; // [0] the instance id (= Join key 8), [3] the a9p layout
    }

    /// <summary>A floor build is in (World.Post): if the session still waits on the same map, the change runs again and is answered with the kept request id; a failed build is refused with 1641.</summary>
    private void ResumeDungeon(PendingChange pending, DungeonGroup group, int index, Task<DungeonFloor> build, Func<ChangeClusterResponse?> retry)
    {
        if (_changing != pending) return;
        _changing = null;
        if (_closed || Status != SessionStatus.InWorld || Player != pending.Player || pending.Player.Map != pending.Map)
        {
            Log.Info(Name, $"floor {index + 1} of {group.Ids[0]}: the player has left {pending.Map.Cluster} meanwhile, no answer");
            return;
        }
        ChangeClusterResponse? resp;
        if (!build.IsCompletedSuccessfully || group.Closed)
        {
            Log.Warn(Name, $"floor {index + 1} of {group.Ids[0]}: {(group.Closed ? "the dungeon closed" : build.Exception?.InnerException?.Message ?? "not made")}");
            resp = Refuse(pending.Player, pending.ExitId, ChangeClusterRefusal.ExitNotConnected, "dungeon floor not made");
        }
        else
        {
            world.Dungeons.Adopt(group, index, build.Result);
            resp = retry();
        }
        if (resp is null) return;
        // stageM exit: a clicked portal's entry (no request id: it came from the 237 channel) is pushed, a refusal told.
        if (pending.RequestId is null) PushChannelled(pending.Player, resp);
        else SendResponse(OperationCodes.ChangeCluster, resp, pending.RequestId, pending.Encrypted);
    }
}
