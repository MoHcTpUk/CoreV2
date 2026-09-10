using System.Security.Cryptography;

namespace Users.Application.Security;

/// <summary>
///     Builds the temporary password the verification flow hands to the identity service. Every character
///     and the final shuffle come from the cryptographic RNG; picking with
///     <see cref="RandomNumberGenerator.GetInt32(int)" /> keeps the alphabet uniform whatever its size.
/// </summary>
public static class TemporaryPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Special = "!@#$%^&*()-_=+[]{}|;:,.<>?";
    private const string AllCharacters = Uppercase + Lowercase + Digits + Special;

    /// <summary>Minimum length that still leaves room for one character of every required class.</summary>
    private const int MinimumLength = 4;

    public static string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinimumLength);

        var password = new char[length];

        password[0] = Pick(Uppercase);
        password[1] = Pick(Lowercase);
        password[2] = Pick(Digits);
        password[3] = Pick(Special);

        for (var i = MinimumLength; i < length; i++) password[i] = Pick(AllCharacters);

        RandomNumberGenerator.Shuffle<char>(password);

        return new string(password);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
