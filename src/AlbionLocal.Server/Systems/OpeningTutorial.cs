using System.Numerics;
using System.Text.Json;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>Bounded lab implementation. The client renders its own quest texts/circles; only server-observed
/// conditions advance progress. Requests cannot skip the route, deliver twice or complete the unimplemented boat task.</summary>
public static class OpeningTutorial
{
    public const float TalkRange = 7; // Lab interaction limit; not claimed as an observed official distance.
    public const int Boundary = 8;

    public static bool Active(Player p) => p.Character.Tutorial is { Version: 1, Stage: >= 0 and <= Boundary }
        && p.World.OpeningTutorial is { } d && p.Map?.Cluster == d.Tutorial.StartCluster;

    public static void Joined(Player p)
    {
        if (!Active(p)) return;
        p.TutorialInsideSince = null; // Offline time never satisfies a stay-in-circle task.
        var log = p.Character.Tutorial!;
        p.Session.Send(new TutorialUpdateEvent { d18 = log.TutorialState });
        SendFull(p);
        SendProgress(p, TaskIndex(log.Stage));
        Log.Info("tutorial", $"'{p.Character.Name}' resumes opening stage {log.Stage}; boundary is boat-search, not quest completion");
    }

    public static void Update(Player p)
    {
        if (!Active(p) || p.Character.Tutorial is not { } log || log.Stage is 4 or 5 or Boundary) return;
        var data = p.World.OpeningTutorial!;
        var task = (log.Stage < 5 ? data.First : data.Tower).Tasks[TaskIndex(log.Stage)];
        if (task.Kind != "gotolocation") return; // A nearby NPC is not a conversation.
        var position = p.PositionAt(DateTime.UtcNow);
        var inside = float.IsFinite(position.X) && float.IsFinite(position.Y)
            && data.Markers(task.Marker).Any(m => Vector2.DistanceSquared(position, m.Position) <= m.Radius * m.Radius);
        if (!inside) { p.TutorialInsideSince = null; return; }
        var now = p.World.Clock.Now;
        p.TutorialInsideSince ??= now;
        if ((now - p.TutorialInsideSince.Value).TotalSeconds < task.Seconds) return;
        Change(p, log with { Stage = log.Stage + 1 });
    }

    /// <summary>239: action 0 interact/query, 1 accept (slot, selection), 2 give items, 3 complete,
    /// 4 abort, 5 clear. c32.i/ag/ah/cl/cm verify the action numbers. Only this bounded route is supported.</summary>
    public static short Request(Player p, long npcId, int action, byte slot, byte selection)
    {
        if (!Active(p) || p.Map?.Objects.GetValueOrDefault(npcId) is not TutorialQuestGiver npc
            || Vector2.DistanceSquared(p.PositionAt(DateTime.UtcNow), npc.Position) > TalkRange * TalkRange)
            return 1;
        var log = p.Character.Tutorial!;
        if (action == 0)
        {
            if (log.Stage == 3) Change(p, log with { Stage = 4 });
            Offer(p, npc);
            return 0;
        }
        if (slot != 0 || selection != 0) return 1;
        if (action == 3 && log.Stage == 4)
        {
            // Both observed opening definitions have empty rewards. Do not invent a payment.
            p.Session.Send(Full(p, state: 1, clear: true));
            Change(p, log with { Stage = 5 });
            Offer(p, npc);
            return 0;
        }
        if (action == 1 && log.Stage == 5)
        {
            Change(p, log with { Stage = 6, QuestId = Guid.NewGuid(), StartedAt = p.World.GameNow });
            // Clear the offer; the accepted quest is now carried by FullQuestInfo.
            Offer(p, npc);
            return 0;
        }
        return 1;
    }

    private static void Change(Player p, OpeningTutorialProgress next)
    {
        var old = p.Character.Tutorial!;
        var data = p.World.OpeningTutorial!;
        if (next.Stage is >= 0 and <= 3 or >= 6 and <= 8)
        {
            var task = (next.Stage < 5 ? data.First : data.Tower).Tasks[TaskIndex(next.Stage)];
            if (task.TutorialState is { } state) next = next with { TutorialState = data.Tutorial.Find(state)!.Id };
        }
        p.Character.Tutorial = next;
        p.TutorialInsideSince = null;
        p.World.Saves.MarkChanged(p.Character);
        p.Session.Send(new TutorialUpdateEvent { d18 = next.TutorialState });
        if (next.Stage != 5) { SendFull(p); SendProgress(p, TaskIndex(old.Stage)); }
        foreach (var npc in p.Map!.Objects.Values.OfType<TutorialQuestGiver>()) p.Session.Send(NpcInfo(p, npc));
        Log.Info("tutorial", $"'{p.Character.Name}' opening stage {old.Stage} -> {next.Stage}");
    }

    public static QuestGiverInfoForPlayerEvent NpcInfo(Player p, TutorialQuestGiver npc) => new()
    {
        BuildingObjectId = npc.Id, QuestGiverGuid = npc.Spawn is CriticalMember m ? m.Guid!.Value : Guid.Empty,
        // Type is already known from NewQuestGiverObject. This field is the questgiverTYPE index, not its object type.
        QuestGiverType = p.World.Clusters?.QuestGiverTypes.IdOf("WOUNDEDCRAFTER") ?? 1,
        dr4 = p.Character.Tutorial?.Stage == 5, BarStamp = p.World.GameNow,
    };

    private static int TaskIndex(int stage) => stage switch { <= 2 => stage, 3 or 4 or 5 => 3, _ => stage - 6 };
    private static string Variables(Player p, OpeningTutorialData.Quest quest)
    {
        var d = p.World.OpeningTutorial!;
        return JsonSerializer.Serialize(quest.Variables.Select(kind => kind switch
        {
            "dynamiccluster" => (object)d.Tutorial.StartCluster,
            // bop.o8 expects a list of [cluster-name, questgiver-GUID] pairs.
            "dynamicquestgiver" => new[] { new[] { d.Tutorial.StartCluster, d.Crafter.Guid!.Value.ToString() } },
            _ => throw new InvalidDataException($"Unsupported opening variable {kind}"),
        }).ToArray());
    }
    private static FullQuestInfoEvent Full(Player p, byte state = 0, bool clear = false)
    {
        var d = p.World.OpeningTutorial!;
        var log = p.Character.Tutorial!;
        var quest = log.Stage < 6 ? d.First : d.Tower;
        return new FullQuestInfoEvent { ddn = p.Id, cdi = quest.Id, cdj = d.Crafter.Guid!.Value,
            cdk = d.Tutorial.StartCluster, cdm = clear ? Guid.Empty : log.QuestId,
            cdn = 0, cdo = log.StartedAt, cdp = state, cdq = Variables(p, quest) };
    }
    private static void SendFull(Player p) { if (p.Character.Tutorial!.Stage != 5) p.Session.Send(Full(p)); }
    private static void SendProgress(Player p, int previous)
    {
        var stage = p.Character.Tutorial!.Stage;
        var index = TaskIndex(stage);
        var task = (stage < 6 ? p.World.OpeningTutorial!.First : p.World.OpeningTutorial!.Tower).Tasks[index];
        p.Session.Send(new QuestProgressInfoEvent { ddn = p.Id, dsd = index,
            dse = task.Seconds > 0 ? JsonSerializer.Serialize(new[] { new[] { task.Seconds } }) : "[[]]",
            dsf = [stage == 4], dsg = previous, dsh = index });
    }
    private static void Offer(Player p, TutorialQuestGiver npc)
    {
        var d = p.World.OpeningTutorial!;
        var log = p.Character.Tutorial!;
        p.Session.Send(NpcInfo(p, npc));
        p.Session.Send(new QuestGiverQuestOfferedEvent { ddn = npc.Id, dr6 = log.Stage == 5 ? log.QuestId : Guid.Empty,
            dr7 = log.Stage == 5 ? d.Tower.Id : -1, dr8 = 0, dr9 = Variables(p, d.Tower),
            dsa = 0, dsb = log.StartedAt, dsc = 0 });
    }
}

