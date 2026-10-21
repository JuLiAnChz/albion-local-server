using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>Standalone NPC bound to the GUID already placed by the client landscape.</summary>
public sealed class TutorialQuestGiver(long id, CriticalMember spawn, int type) : WorldObject(id, spawn)
{
    public override string Kind => "tutorial quest giver";
    public override string TypeName => "WOUNDEDCRAFTER";
    public override bool LeaveOnRange => true;
    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewQuestGiverObjectEvent { c8i = Id, c8j = Position, c8k = spawn.Direction,
            c8l = spawn.Guid!.Value, c8m = type };
        yield return Systems.OpeningTutorial.NpcInfo(viewer, this);
    }
}
