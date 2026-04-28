using System.Security.Cryptography;
using System.Text;

namespace gas_station.Common;

public static class PasswordHasher
{
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string plainValue, string hash)
    {
        var plainHash = Hash(plainValue);
        return string.Equals(plainHash, hash, StringComparison.OrdinalIgnoreCase);
    }
}
