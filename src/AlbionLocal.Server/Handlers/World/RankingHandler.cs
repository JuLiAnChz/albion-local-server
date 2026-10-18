using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>The ranking window (fame, PvP, gathering … per player, guild or alliance).</summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// A page of a ranking list (op GetRankings, client class ut: 0 Ranking, 1 filter text, 2 TimeFrame, 3
    /// RankingEntityTypes, 4 RankingCharacterFilter, 5 first row, 6 rows wanted). There are no rankings yet, so no rows,
    /// but keys 0-4 must echo the request: c2x.b 0x180a46420 files the answer under the cache entry of those five values,
    /// and RankingGui.OnRankingsChanged 0x181613770 ignores it unless they equal the list on screen (the five that
    /// RankingGui.RequestRankings 0x181614620 sends). The empty answer of opcode-stubs.json echoed zeros, which fitted only
    /// the default list, not the guild and alliance tabs. Key 6 is the list's row count, 0; key 7 the last update, 0 =
    /// "@RANKING_NO_LAST_UPDATE" (and the page is asked again when it is next shown, c2x.b 0x180a46b90); key 8 = 0 leaves
    /// the browse button off.
    /// </summary>
    [Handles(OperationCodes.GetRankings, Status = SessionStatus.InWorld)]
    private GetRankingsResponse GetRankings(GetRankingsRequest req) =>
        new GetRankingsResponse { a3t = req.op, a3u = req.oq, a3v = req.or, a3w = req.os, a3x = req.ot }.FillEmpty();
}
