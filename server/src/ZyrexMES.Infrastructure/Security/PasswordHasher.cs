using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace ZyrexMES.Infrastructure.Security;

public static class PasswordHasher
{
    // Stored format: argon2id$<salt-b64>$<hash-b64>; params: m=19456,t=2,p=4 (OWASP baseline)
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Compute(password, salt);
        return $"argon2id${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 3 || parts[0] != "argon2id") return false;
        try
        {
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Compute(password, Convert.FromBase64String(parts[1]));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Compute(string password, byte[] salt) =>
        new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt, MemorySize = 19456, Iterations = 2, DegreeOfParallelism = 4,
        }.GetBytes(32);
}
