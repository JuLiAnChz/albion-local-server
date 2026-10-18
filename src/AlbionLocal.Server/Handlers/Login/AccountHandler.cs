using System.Net.Mail;
using AlbionLocal.Photon;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>Registration from the client's login screen (analysis/work/accounts, server spec §4).</summary>
public sealed partial class LoginSession
{
    /// <summary>
    /// CreateAccount (op 4, client class rh) from CreateAccountDialog: "Create account" on the login screen connects to this
    /// login server (BeforeLoginState.OnClickCreateAccount, LoginGui.Connect state 2) and, after the consent popups, sends
    /// key 6 = the email (trimmed), 7 = the password (not trimmed), 8 Steam id, 9 device id, 10 language, 11 hardware JSON,
    /// 16-18 the consents, 21-23 platform ids (all ignored here). The dialog has checked the password already (8-72
    /// characters, ASCII, not the email, typed twice) and the email by a regex; the server checks again
    /// (<see cref="Passwords.Check"/>), adds that the password has no space at either end (login trims it), and answers:
    /// 165 when Accounts:Registration is off, 167 for an email that is no address (the client formats the message with it),
    /// 163 with key 9 = <see cref="PasswordProblem"/> flags, 161 when the name has an account, 206 (no case of its own: the
    /// generic error) past the <see cref="LoginThrottle"/> or when the account cannot be stored, else 0: the account exists
    /// at once, empty, with no email to confirm. The client then fills its login form with the email and the password
    /// and shows "@LOGINGUI_CREATEACCOUNT_OK_EMAIL_SENT"; the user presses Login. The account's level is
    /// Accounts:DefaultLevel (Player), as for every account made after the start. Keys 0-4 echo the client's version (VersionedOperation, ajb),
    /// as the Login response does.
    /// </summary>
    [Handles(OperationCodes.CreateAccount)]
    private CreateAccountResponse CreateAccount(CreateAccountRequest req)
    {
        var r = new CreateAccountResponse { bm1 = req.aiu, bm2 = req.aiv, bm3 = req.aiw, bm4 = req.aix, bm5 = req.aiy };
        var email = (req.hp ?? "").Trim();
        if (!config.Registration)
            return Answer(LoginCodes.CreationDisabled, "account creation is disabled (Accounts:Registration)");
        if (email.Length == 0)
        {
            r.aon = (byte)PasswordProblem.InvalidEmail;
            return Answer(LoginCodes.ValidationErrors, "no email");
        }
        if (!IsEmail(email)) return Answer(LoginCodes.InvalidEmail, "the email is not an address");
        if (Passwords.Check(req.hq, email) is not PasswordProblem.None and var problems)
        {
            r.aon = (byte)problems;
            return Answer(LoginCodes.ValidationErrors, $"the password is not accepted ({problems})");
        }
        if (!Throttle.Allow(Peer.EndPoint.Address, UtcNow()))
            return Answer(LoginCodes.LoginServiceUnavailable, $"too many password checks from {Peer.EndPoint.Address} (a minute's limit)");
        Account? made;
        try { made = characters.CreateAccount(email, Passwords.Hash(req.hq!), config.DefaultAccountLevel); }
        catch (Exception e)
        {
            Log.Error(Name, $"create account failed: {e.Message}");
            return Answer(LoginCodes.LoginServiceUnavailable, "the account could not be stored"); // the client's generic error
        }
        if (made is null) return Answer(LoginCodes.NameAlreadyTaken, "the name has an account already");
        Log.Info(Name, $"{made} created from the registration dialog (an empty account)");
        return r;

        CreateAccountResponse Answer(short code, string why)
        {
            Log.Info(Name, $"create account refused ({code}): {why}");
            r.ReturnCode = code;
            return r;
        }
    }

    /// <summary>A plain address check: one '@' with text on both sides, a dot in the domain, no spaces, at most 254 characters.</summary>
    private static bool IsEmail(string text) =>
        text.Length <= 254 && !text.Any(char.IsWhiteSpace) && text.IndexOf('@') is > 0 and var at && at == text.LastIndexOf('@')
        && text.IndexOf('.', at) is > 0 and var dot && dot > at + 1 && dot < text.Length - 1 && MailAddress.TryCreate(text, out _);
}
