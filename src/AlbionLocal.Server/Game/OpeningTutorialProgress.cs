namespace AlbionLocal.Server.Game;

/// <summary>Versioned bounded tutorial state. 0..2 walking, 3 talk, 4 ready to deliver, 5 tower offered,
/// 6..7 ascending, 8 boat-search boundary. Immutable so repository snapshots cannot share mutable progress.</summary>
public sealed record OpeningTutorialProgress(int Stage, Guid QuestId, long StartedAt)
{
    public int Version { get; init; } = 1;
    public int TutorialState { get; init; }
}
