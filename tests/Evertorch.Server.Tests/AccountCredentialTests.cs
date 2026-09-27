using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Logins, password hashes, and session tokens (Network Protocol §4; Persistence §4).
/// </summary>
[TestFixture]
public sealed class AccountCredentialTests
{
    private const string Password = "Correct-Horse-9";

    private static PasswordHasher Hasher(int iterations = TestServer.TestPasswordIterations)
    {
        return new PasswordHasher(Options.Create(new AccountOptions { PasswordIterations = iterations }));
    }

    // The shortest of a few runs, so a pause of the machine does not decide the comparison.
    private static double FastestMilliseconds(Action action)
    {
        double fastest = double.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            action();
            fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
        }

        return fastest;
    }

    [TestCase("a")]
    [TestCase("Alice.Smith_2-b")]
    [TestCase("0123456789012345678901234567890123456789012345678901234567890123")]
    public void Login_OfAllowedCharacters_IsKeptInLowerCase(string login)
    {
        bool isValid = AccountCredentialRules.TryNormalizeLogin(login, out string normalized);

        Assert.That(isValid, Is.True);
        Assert.That(normalized, Is.EqualTo(login.ToLowerInvariant()));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("dev:alice")]
    [TestCase("alice smith")]
    [TestCase("al!ce")]
    [TestCase("ålice")]
    [TestCase("01234567890123456789012345678901234567890123456789012345678901234")]
    public void Login_EmptyTooLongOrWithOtherCharacters_IsRefused(string? login)
    {
        Assert.That(AccountCredentialRules.TryNormalizeLogin(login, out _), Is.False);
    }

    [TestCase("12345678")]
    [TestCase("!~Az09{}")]
    public void Password_OfPrintableAsciiAtTheLengthBounds_IsAllowed(string password)
    {
        Assert.That(AccountCredentialRules.IsValidPassword(password), Is.True);
        Assert.That(AccountCredentialRules.IsValidPassword(new string('x', 128)), Is.True);
    }

    [TestCase(null)]
    [TestCase("1234567")]
    [TestCase("with space")]
    [TestCase("tab\tinside")]
    [TestCase("pässword1")]
    public void Password_TooShortOrWithSpacesOrOtherCharacters_IsRefused(string? password)
    {
        Assert.That(AccountCredentialRules.IsValidPassword(password), Is.False);
        Assert.That(AccountCredentialRules.IsValidPassword(new string('x', 129)), Is.False);
    }

    [TestCase(null, null)]
    [TestCase("md5", "5f4dcc3b5aa765d61d8327deb882cf99")]
    [TestCase(PasswordHasher.Scheme, "garbage")]
    [TestCase(PasswordHasher.Scheme, "999$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Verify_AStoredPasswordItCannotRead_FailsAsSlowlyAsAWrongPassword(string? scheme, string? hash)
    {
        PasswordHasher hasher = Hasher(200_000);
        string stored = hasher.Hash(Password);
        hasher.Verify("warm-up-1", scheme, hash);

        double wrong = FastestMilliseconds(() => hasher.Verify("Wrong-Password-1", PasswordHasher.Scheme, stored));
        double unreadable = FastestMilliseconds(() => hasher.Verify(Password, scheme, hash));

        Assert.That(hasher.Verify(Password, scheme, hash), Is.False);
        Assert.That(unreadable, Is.GreaterThan(wrong / 2), $"a wrong password took {wrong:0.0} ms");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("dev:tester")]
    public void SessionToken_OfTheWrongLength_DoesNotParse(string? text)
    {
        Assert.That(SessionToken.TryParse(text, out byte[] hash), Is.False);
        Assert.That(hash, Is.Empty);
    }

    [Test]
    public void Hash_IsPbkdf2Sha256OfThePasswordAndSalt()
    {
        string[] parts = Hasher().Hash(Password).Split('$');

        byte[] expected = Rfc2898DeriveBytes.Pbkdf2(
            Password,
            Convert.FromBase64String(parts[1]),
            1000,
            HashAlgorithmName.SHA256,
            32);

        Assert.That(Convert.FromBase64String(parts[2]), Is.EqualTo(expected));
    }

    [Test]
    public void Hash_IsTheIterationsSaltAndKey_AndDiffersEachTime()
    {
        PasswordHasher hasher = Hasher();

        string first = hasher.Hash(Password);
        string second = hasher.Hash(Password);

        string[] parts = first.Split('$');
        Assert.That(parts, Has.Length.EqualTo(3));
        Assert.That(parts[0], Is.EqualTo("1000"));
        Assert.That(Convert.FromBase64String(parts[1]), Has.Length.EqualTo(16));
        Assert.That(Convert.FromBase64String(parts[2]), Has.Length.EqualTo(32));
        Assert.That(second, Is.Not.EqualTo(first), "a new salt each time");
        Assert.That(first, Does.Not.Contain(Password));
    }

    [Test]
    public void SessionToken_Created_Is43CharactersOfBase64Url_AndHashesItsBytes()
    {
        var token = SessionToken.Create();

        Assert.That(token.Text, Has.Length.EqualTo(43));
        Assert.That(token.Text, Does.Match("^[A-Za-z0-9_-]{43}$"));
        byte[] bytes = Convert.FromBase64String(token.Text.Replace('-', '+').Replace('_', '/') + "=");
        Assert.That(token.Hash, Is.EqualTo(SHA256.HashData(bytes)));
        Assert.That(token.Hash, Has.Length.EqualTo(32));
    }

    [Test]
    public void SessionToken_Parsed_GivesTheHashItWasCreatedWith()
    {
        var token = SessionToken.Create();

        bool isParsed = SessionToken.TryParse(token.Text, out byte[] hash);

        Assert.That(isParsed, Is.True);
        Assert.That(hash, Is.EqualTo(token.Hash));
    }

    [Test]
    public void SessionToken_WithPaddingOrOtherAlphabetsOrUnusedBitsSet_DoesNotParse()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        string text = SessionToken.Create().Text;

        // The last character carries two unused bits; only the form with them clear is the token.
        char withUnusedBit = alphabet[alphabet.IndexOf(text[42], StringComparison.Ordinal) + 1];
        string[] others =
        {
            text.Substring(0, 42) + "=",
            "+" + text.Substring(1),
            "/" + text.Substring(1),
            " " + text.Substring(1),
            text.Substring(0, 42) + withUnusedBit
        };

        Assert.That(others, Has.None.EqualTo(text));
        Assert.That(others.Where(other => SessionToken.TryParse(other, out _)), Is.Empty);
    }

    [Test]
    public void SessionTokens_Created_AreNeverTheSame()
    {
        string[] texts = Enumerable.Range(0, 1000).Select(_ => SessionToken.Create().Text).ToArray();

        Assert.That(texts, Is.Unique);
    }

    [Test]
    public void Verify_AHashMadeWithOtherIterations_UsesItsOwnCount()
    {
        string stored = Hasher(2000).Hash(Password);

        Assert.That(Hasher().Verify(Password, PasswordHasher.Scheme, stored), Is.True);
    }

    [Test]
    public void Verify_TheRightPassword_Passes_AndAnyOtherFails()
    {
        PasswordHasher hasher = Hasher();
        string stored = hasher.Hash(Password);

        Assert.That(hasher.Verify(Password, PasswordHasher.Scheme, stored), Is.True);
        Assert.That(hasher.Verify(Password.ToUpperInvariant(), PasswordHasher.Scheme, stored), Is.False);
        Assert.That(hasher.Verify(Password + "x", PasswordHasher.Scheme, stored), Is.False);
    }
}
}
