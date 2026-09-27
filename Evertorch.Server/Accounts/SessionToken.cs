using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     A session token: 32 random bytes written as 43 characters of base64url without padding, stored only as the
///     SHA-256 of the bytes (Network Protocol §4). Its text is a secret: it is never logged.
/// </summary>
public sealed class SessionToken
{
    public const int ByteLength = 32;
    public const int TextLength = 43;

    private SessionToken(string text, byte[] hash)
    {
        Text = text;
        Hash = hash;
    }

    /// <summary>
    ///     What the client presents in its hello.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     What the store keeps, <see cref="SessionTokenLimits.HashLength" /> bytes.
    /// </summary>
    public byte[] Hash { get; }

    public static SessionToken Create()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return new SessionToken(Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>
    ///     The stored hash of the token <paramref name="text" /> names. Only the canonical form parses, so two texts
    ///     never name one token.
    /// </summary>
    public static bool TryParse(string? text, out byte[] hash)
    {
        hash = Array.Empty<byte>();
        if (text == null || text.Length != TextLength || !IsCanonical(text))
        {
            return false;
        }

        byte[] bytes = new byte[ByteLength];
        if (!Base64Url.TryDecodeFromChars(text, bytes, out int written) || written != ByteLength)
        {
            return false;
        }

        hash = SHA256.HashData(bytes);
        return true;
    }

    // Checked before decoding: the decoder throws, rather than failing, on a character outside its alphabet and on a
    // last character whose two unused bits are set, and a text with them set would name the same bytes as the token.
    private static bool IsCanonical(string text)
    {
        foreach (char character in text)
        {
            if (ValueOf(character) < 0)
            {
                return false;
            }
        }

        return ValueOf(text[TextLength - 1]) % 4 == 0;
    }

    private static int ValueOf(char character)
    {
        switch (character)
        {
            case >= 'A' and <= 'Z':
                return character - 'A';
            case >= 'a' and <= 'z':
                return character - 'a' + 26;
            case >= '0' and <= '9':
                return character - '0' + 52;
            case '-':
                return 62;
            case '_':
                return 63;
            default:
                return -1;
        }
    }
}
}
