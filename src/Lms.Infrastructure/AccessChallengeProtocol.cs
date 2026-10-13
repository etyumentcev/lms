using System.Security.Cryptography;
using System.Text;

namespace Lms.Infrastructure;

internal static class AccessChallengeProtocol
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    internal static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(10);
        var chars = new char[16];
        var buffer = 0; var bits = 0; var index = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { bits -= 5; chars[index++] = Alphabet[(buffer >> bits) & 31]; }
        }
        return new(chars);
    }

    internal static string Format(string code)
        => string.Join('-', Enumerable.Range(0, 4).Select(i => code.Substring(i * 4, 4)));

    internal static string Canonical(string code)
    {
        var canonical = code.Replace("-", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
        if (canonical.Length != 16 || canonical.Any(c => !Alphabet.Contains(c))) throw new FormatException();
        return canonical;
    }

    internal static byte[] Mac(byte[] key, Guid challengeId, string purpose, string code)
        => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(
            (purpose == "Activation" ? "m1-activation-v1:" : purpose == "Recovery" ? "m1-recovery-v1:" : throw new ArgumentOutOfRangeException(nameof(purpose))) +
            challengeId.ToString("N") + ":" + Canonical(code)));

    internal static bool Matches(byte[] key, AccessChallenge challenge, string candidate)
    {
        byte[] mac;
        try { mac = Mac(key, challenge.ChallengeId, challenge.Purpose, candidate); }
        catch (FormatException) { mac = new byte[32]; }
        return CryptographicOperations.FixedTimeEquals(mac, challenge.SecretMac);
    }
}
