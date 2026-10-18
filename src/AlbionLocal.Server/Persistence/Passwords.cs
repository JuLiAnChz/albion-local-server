using System.Security.Cryptography;

namespace AlbionLocal.Server.Persistence;

/// <summary>
/// Account passwords (analysis/work/accounts, server spec §1): PBKDF2-SHA256 (<see cref="Rfc2898DeriveBytes.Pbkdf2(string, byte[], int, HashAlgorithmName, int)"/>)
/// with a random 16-byte salt, stored with its parameters as "pbkdf2-sha256$iterations$salt$hash" (both base64), so a
/// later change of the iteration count still verifies the old hashes. The client sends the password as plain, trimmed text
/// (Login key 7, never hashed by it); only the hash is kept, and neither the password nor the hash is ever logged.
/// </summary>
public static class Passwords
{
    /// <summary>PBKDF2 iterations of a new hash.</summary>
    public const int Iterations = 100_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16, HashBytes = 32;

    /// <summary>The stored form of <paramref name="password"/>, with a new random salt.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Whether <paramref name="password"/> matches <paramref name="stored"/> (a constant-time compare); a malformed hash matches nothing.</summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts is not [Scheme, var iterations, var salt, var hash] || !int.TryParse(iterations, out var n) || n is < 1 or > 10_000_000)
            return false;
        byte[] saltBytes, expected;
        try { (saltBytes, expected) = (Convert.FromBase64String(salt), Convert.FromBase64String(hash)); }
        catch (FormatException) { return false; }
        if (expected.Length == 0) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, n, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static readonly Lazy<string> DummyHash = new(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))));

    /// <summary>
    /// The work of a <see cref="Verify"/> for a login name without an account, so that its refusal (201, the same as a wrong
    /// password) takes as long as a wrong password's and the timing does not tell which names exist.
    /// </summary>
    public static void Dummy(string password) => Verify(password, DummyHash.Value);

    /// <summary>
    /// What is wrong with a new password, as the client's registration dialog checks it (CreateAccountDialog.ValidatePassword)
    /// plus the server's own rule: <see cref="PasswordProblem"/> flags, 0 = fine. 8 to 72 characters, printable ASCII only,
    /// not the account name (ignoring case), and no space at either end (the login screen trims what it sends, so such a
    /// password could never be typed in).
    /// </summary>
    public static PasswordProblem Check(string? password, string name)
    {
        if (string.IsNullOrEmpty(password)) return PasswordProblem.NotProvided;
        var problems = PasswordProblem.None;
        if (password.Length is < 8 or > 72) problems |= PasswordProblem.Length;
        if (password.Any(ch => ch is < ' ' or > '~') || password.Trim() != password) problems |= PasswordProblem.InvalidCharacters;
        if (password.Equals(name, StringComparison.OrdinalIgnoreCase)) problems |= PasswordProblem.MatchesName;
        return problems;
    }
}

/// <summary>
/// The CreateAccount response's key 9 (client enum aek.ValidationErrors, a byte of flags): the reasons the client shows for
/// RC 163.
/// </summary>
[Flags]
public enum PasswordProblem : byte
{
    None = 0,
    Length = 1,
    InvalidCharacters = 2,
    MatchesName = 4,
    Blacklisted = 8,
    NotProvided = 16,
    InvalidEmail = 32,
}
