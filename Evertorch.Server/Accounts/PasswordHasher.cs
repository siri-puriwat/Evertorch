using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Salted PBKDF2-HMAC-SHA256 password hashes, stored as <c>&lt;iterations&gt;$&lt;salt&gt;$&lt;key&gt;</c> with the
///     last two in base64 (Persistence §4). Safe on any thread.
/// </summary>
public sealed class PasswordHasher
{
    public const string Scheme = "pbkdf2-sha256";

    private const int SaltLength = 16;
    private const int KeyLength = 32;
    private const char Separator = '$';

    private readonly int m_iterations;
    private readonly Lazy<string> m_dummyHash;

    public PasswordHasher(IOptions<AccountOptions> options)
    {
        m_iterations = options.Value.PasswordIterations;

        // Made on first use, so a server start does not pay for a hash it may never need.
        m_dummyHash = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyLength))));
    }

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] key = Derive(password, salt, m_iterations);
        return string.Join(
            Separator,
            m_iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    /// <summary>
    ///     Whether <paramref name="password" /> is the one stored. A stored password it cannot read, and an account
    ///     without one, are checked against a dummy hash of the configured cost first, so every refusal takes as long as
    ///     a wrong password and its time tells nothing (Network Protocol §4).
    /// </summary>
    public bool Verify(string password, string? scheme, string? hash)
    {
        int iterations = 0;
        byte[] salt = Array.Empty<byte>();
        byte[] expected = Array.Empty<byte>();
        bool isReadable = scheme == Scheme && TryRead(hash, out iterations, out salt, out expected);
        if (!isReadable)
        {
            TryRead(m_dummyHash.Value, out iterations, out salt, out expected);
        }

        byte[] actual = Derive(password, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && isReadable;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeyLength);
    }

    private static bool TryRead(string? hash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        key = Array.Empty<byte>();
        string[] parts = hash?.Split(Separator) ?? Array.Empty<string>();
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < AccountOptions.MinPasswordIterations
            || iterations > AccountOptions.MaxPasswordIterations)
        {
            return false;
        }

        salt = FromBase64(parts[1]);
        key = FromBase64(parts[2]);
        return salt.Length == SaltLength && key.Length == KeyLength;
    }

    private static byte[] FromBase64(string text)
    {
        byte[] bytes = new byte[text.Length];
        return Convert.TryFromBase64String(text, bytes, out int written)
            ? bytes.AsSpan(0, written).ToArray()
            : Array.Empty<byte>();
    }
}
}
