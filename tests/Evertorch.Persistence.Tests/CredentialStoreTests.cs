using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Accounts with passwords and their session tokens (Persistence §4, §11).
/// </summary>
[TestFixture]
public sealed class CredentialStoreTests
{
    private const string Scheme = "pbkdf2-sha256";
    private const string Hash = "1000$c2FsdA==$a2V5";
    private const string OtherHash = "1000$b3RoZXI=$a2V5";

    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;
    private Sql m_sql = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
        m_sql = new Sql(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private static string NewLogin()
    {
        return $"user{Guid.NewGuid():N}".Substring(0, 20);
    }

    private static byte[] NewTokenHash()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    private AccountId Create(string login)
    {
        return m_store.CreateAccountAsync(login, Scheme, Hash, Now, CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("taken");
    }

    private void Issue(AccountId account, byte[] hash, DateTime issuedAt, TimeSpan lifetime)
    {
        m_store.IssueSessionTokenAsync(account, hash, issuedAt, issuedAt + lifetime, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private StoredSessionToken? Find(byte[] hash)
    {
        return m_store.FindSessionTokenAsync(hash, CancellationToken.None).GetAwaiter().GetResult();
    }

    private long TokenCount(AccountId account)
    {
        return m_sql.Scalar($"SELECT count(*) FROM session_tokens WHERE account_id = {account.Value}");
    }

    [TestCase(0)]
    [TestCase(31)]
    [TestCase(33)]
    public void TokenCalls_WithAHashThatIsNotThirtyTwoBytes_AreRefused(int length)
    {
        AccountId account = Create(NewLogin());
        byte[] hash = new byte[length];

        Action issue = () => Issue(account, hash, Now, TimeSpan.FromMinutes(15));
        Action find = () => Find(hash);

        Assert.That(issue, Throws.ArgumentException);
        Assert.That(find, Throws.ArgumentException);
    }

    [Test]
    public void CreateAccount_ForANewLogin_StoresTheHashAndScheme()
    {
        string login = NewLogin();

        AccountId account = Create(login);

        AccountCredentials? credentials =
            m_store.FindAccountCredentialsAsync(login, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(credentials, Is.Not.Null);
        Assert.That(credentials!.Account, Is.EqualTo(account));
        Assert.That(credentials.PasswordScheme, Is.EqualTo(Scheme));
        Assert.That(credentials.PasswordHash, Is.EqualTo(Hash));
        Assert.That(credentials.IsDisabled, Is.False);
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM accounts WHERE id = {account.Value} AND last_login_at IS NULL"),
            Is.EqualTo(1),
            "made, not yet signed in");
    }

    [Test]
    public void CreateAccount_ForATakenLogin_ReturnsNullAndKeepsTheFirst()
    {
        string login = NewLogin();
        Create(login);

        AccountId? second = m_store.CreateAccountAsync(login, Scheme, OtherHash, Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(second, Is.Null);
        Assert.That(
            m_store.FindAccountCredentialsAsync(login, CancellationToken.None).GetAwaiter().GetResult()!.PasswordHash,
            Is.EqualTo(Hash));
    }

    [Test]
    public void FindAccountCredentials_ForADevelopmentAccount_HasNoPassword()
    {
        string login = $"dev:{Guid.NewGuid():N}";
        AccountId? account = m_store.ProvisionAccountAsync(login, Now, CancellationToken.None).GetAwaiter().GetResult();

        AccountCredentials? credentials =
            m_store.FindAccountCredentialsAsync(login, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(credentials!.Account, Is.EqualTo(account));
        Assert.That(credentials.PasswordScheme, Is.Null);
        Assert.That(credentials.PasswordHash, Is.Null);
    }

    [Test]
    public void FindAccountCredentials_ForADisabledOrMissingAccount_SaysSo()
    {
        string login = NewLogin();
        Create(login);
        m_sql.Execute($"UPDATE accounts SET status = 'disabled' WHERE login_normalized = '{login}'");

        AccountCredentials? disabled =
            m_store.FindAccountCredentialsAsync(login, CancellationToken.None).GetAwaiter().GetResult();
        AccountCredentials? missing =
            m_store.FindAccountCredentialsAsync(NewLogin(), CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(disabled!.IsDisabled, Is.True);
        Assert.That(missing, Is.Null);
    }

    [Test]
    public void FindSessionToken_OfADisabledAccount_SaysSo_AndAnUnknownHashIsNull()
    {
        AccountId account = Create(NewLogin());
        byte[] hash = NewTokenHash();
        Issue(account, hash, Now, TimeSpan.FromMinutes(15));
        m_sql.Execute($"UPDATE accounts SET status = 'disabled' WHERE id = {account.Value}");

        Assert.That(Find(hash)!.IsAccountDisabled, Is.True);
        Assert.That(Find(NewTokenHash()), Is.Null);
    }

    [Test]
    public void IssueSessionToken_DeletesTokensExpiredOverADayAgo_OfEveryAccount()
    {
        AccountId other = Create(NewLogin());
        byte[] longGone = NewTokenHash();
        byte[] recentlyExpired = NewTokenHash();
        Issue(other, longGone, Now.AddDays(-2), TimeSpan.FromMinutes(15));
        Issue(other, recentlyExpired, Now.AddHours(-23), TimeSpan.FromMinutes(15));

        Issue(Create(NewLogin()), NewTokenHash(), Now, TimeSpan.FromMinutes(15));

        Assert.That(Find(longGone), Is.Null);
        Assert.That(Find(recentlyExpired), Is.Not.Null, "kept to answer that it expired");
    }

    [Test]
    public void IssueSessionToken_KeepsTheAccountsNewestTenLiveTokens()
    {
        AccountId account = Create(NewLogin());
        AccountId other = Create(NewLogin());
        byte[] expired = NewTokenHash();
        Issue(account, expired, Now.AddHours(-1), TimeSpan.FromMinutes(15));
        Issue(other, NewTokenHash(), Now, TimeSpan.FromMinutes(15));
        byte[][] live = Enumerable.Range(0, 12).Select(_ => NewTokenHash()).ToArray();

        for (int index = 0; index < live.Length; index++)
        {
            Issue(account, live[index], Now.AddSeconds(index), TimeSpan.FromMinutes(15));
        }

        Assert.That(Find(live[0]), Is.Null);
        Assert.That(Find(live[1]), Is.Null);
        Assert.That(live.Skip(2).Select(Find), Has.All.Not.Null);
        Assert.That(Find(expired), Is.Not.Null, "an expired token is not a live one");
        Assert.That(TokenCount(other), Is.EqualTo(1));
    }

    [Test]
    public void IssueSessionToken_ManyAtOnceForOneAccount_KeepsExactlyTheNewestTen()
    {
        AccountId account = Create(NewLogin());
        byte[][] hashes = Enumerable.Range(0, 24).Select(_ => NewTokenHash()).ToArray();

        Task[] signIns = hashes
            .Select((hash, index) =>
                Task.Run(() => Issue(account, hash, Now.AddSeconds(index), TimeSpan.FromMinutes(15))))
            .ToArray();
        Task.WaitAll(signIns);

        Assert.That(TokenCount(account), Is.EqualTo(10));
        Assert.That(hashes.Skip(14).Select(Find), Has.All.Not.Null, "the ten issued last");
    }

    [Test]
    public void IssueSessionToken_StoresItsHashAccountAndExpiry_AndRecordsTheLogin()
    {
        AccountId account = Create(NewLogin());
        byte[] hash = NewTokenHash();

        Issue(account, hash, Now, TimeSpan.FromMinutes(15));

        StoredSessionToken? token = Find(hash);
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Account, Is.EqualTo(account));
        Assert.That(token.ExpiresAt, Is.EqualTo(Now.AddMinutes(15)));
        Assert.That(token.IsAccountDisabled, Is.False);
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM accounts WHERE id = {account.Value} AND last_login_at = '{Now:O}'"),
            Is.EqualTo(1));
    }

    [Test]
    public void SetAccountPassword_ForALoginNobodyHas_ReturnsNull()
    {
        AccountId? changed = m_store.SetAccountPasswordAsync(NewLogin(), Scheme, OtherHash, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(changed, Is.Null);
    }

    [Test]
    public void SetAccountPassword_ReplacesTheHash_AndDeletesOnlyThatAccountsTokens()
    {
        string login = NewLogin();
        AccountId account = Create(login);
        AccountId other = Create(NewLogin());
        Issue(account, NewTokenHash(), Now, TimeSpan.FromMinutes(15));
        Issue(account, NewTokenHash(), Now.AddHours(-3), TimeSpan.FromMinutes(15));
        Issue(other, NewTokenHash(), Now, TimeSpan.FromMinutes(15));

        AccountId? changed = m_store.SetAccountPasswordAsync(login, Scheme, OtherHash, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(changed, Is.EqualTo(account));
        Assert.That(
            m_store.FindAccountCredentialsAsync(login, CancellationToken.None).GetAwaiter().GetResult()!.PasswordHash,
            Is.EqualTo(OtherHash));
        Assert.That(TokenCount(account), Is.Zero);
        Assert.That(TokenCount(other), Is.EqualTo(1));
    }
}
}
