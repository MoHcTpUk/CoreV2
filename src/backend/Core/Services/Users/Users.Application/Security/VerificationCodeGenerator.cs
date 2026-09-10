using System.Security.Cryptography;

namespace Users.Application.Security;

/// <summary>
///     Builds verification codes from the operating-system cryptographic RNG.
///     <see cref="RandomNumberGenerator.GetInt32(int)" /> draws each digit with rejection sampling, so the
///     digits are uniformly distributed — no modulo bias — and, unlike <see cref="System.Random" />, the
///     sequence cannot be reproduced by an attacker who knows when the code was created.
/// </summary>
public static class VerificationCodeGenerator
{
    public static string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        return string.Create(length, length, static (buffer, _) =>
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        });
    }
}
