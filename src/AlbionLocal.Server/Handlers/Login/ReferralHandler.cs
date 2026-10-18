using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>The referral ("invite a friend") window's season, asked right after Login.</summary>
public sealed partial class LoginSession
{
    /// <summary>
    /// The referral season (op GetReferralSeasonDetails, no fields; c3d.i.c 0x180a5fe70 keeps the answer for
    /// FriendsInviteGui.SetByReferralDetails 0x1813bee20): 0 int[] and 1 reward item names (none here), 2 the season end,
    /// shown as "@REFERRAL_SEASONAL_REWARDS_DATE", 3 the texture (none). The empty answer showed 0001-01-01; the end given
    /// here is the first second of the next month (UTC), as the official answer of 2026-10-15 (2026-11-01 00:00:01). It is
    /// cosmetic: there are no referral rewards on this server.
    /// </summary>
    [Handles(OperationCodes.GetReferralSeasonDetails, Status = SessionStatus.Authed)]
    private GetReferralSeasonDetailsResponse GetReferralSeasonDetails(GetReferralSeasonDetailsRequest req) =>
        new GetReferralSeasonDetailsResponse { aa5 = ReferralSeasonEnd(DateTime.UtcNow) }.FillEmpty();

    /// <summary>The referral season end shown at <paramref name="utcNow"/>: the next month's first day, 00:00:01 UTC, as ticks.</summary>
    public static long ReferralSeasonEnd(DateTime utcNow) =>
        new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 1, DateTimeKind.Utc).AddMonths(1).Ticks;
}
