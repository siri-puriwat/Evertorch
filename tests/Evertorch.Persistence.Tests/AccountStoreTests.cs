using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Development accounts are provisioned on first use (Persistence §4).
/// </summary>
[TestFixture]
public sealed class AccountStoreTests
{
    private static readonly DateTime FirstLogin = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

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
        return $"dev:{Guid.NewGuid():N}";
    }

    private AccountId? Provision(string login, DateTime now)
    {
        return m_store.ProvisionAccountAsync(login, now, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Test]
    public void ProvisionAccount_AgainLater_RecordsTheLastLogin()
    {
        string login = NewLogin();
        Provision(login, FirstLogin);

        Provision(login, FirstLogin.AddDays(2));

        long lastLoginDay = m_sql.Scalar(
            $"SELECT extract(day FROM last_login_at AT TIME ZONE 'UTC')::bigint FROM accounts WHERE login_normalized = '{login}'");
        Assert.That(lastLoginDay, Is.EqualTo(25));
    }

    [Test]
    public void ProvisionAccount_ForADisabledAccount_ReturnsNull()
    {
        string login = NewLogin();
        Provision(login, FirstLogin);
        m_sql.Execute($"UPDATE accounts SET status = 'disabled' WHERE login_normalized = '{login}'");

        Assert.That(Provision(login, FirstLogin), Is.Null);
    }

    [Test]
    public void ProvisionAccount_ForANewLogin_CreatesItOnce()
    {
        string login = NewLogin();

        AccountId? first = Provision(login, FirstLogin);
        AccountId? second = Provision(login, FirstLogin.AddHours(1));

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM accounts WHERE login_normalized = '{login}'"), Is.EqualTo(1));
    }

    [Test]
    public void ProvisionAccount_ForTwoLogins_GivesTwoAccounts()
    {
        AccountId? first = Provision(NewLogin(), FirstLogin);
        AccountId? second = Provision(NewLogin(), FirstLogin);

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void ProvisionAccount_ManyTimesAtOnce_CreatesOneAccount()
    {
        string login = NewLogin();

        AccountId?[] accounts = Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ =>
                        Task.Run(() => m_store.ProvisionAccountAsync(login, FirstLogin, CancellationToken.None))))
            .GetAwaiter()
            .GetResult();

        Assert.That(accounts.Distinct().Count(), Is.EqualTo(1));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM accounts WHERE login_normalized = '{login}'"), Is.EqualTo(1));
    }

    [Test]
    public void ProvisionAccount_WhenTheDatabaseIsUnreachable_ThrowsStoreUnavailable()
    {
        var unreachable = new PostgresGameStore(
            "Host=127.0.0.1;Port=1;Database=evertorch;Username=evertorch;Password=unused;Timeout=1");
        Func<Task> provision = () => unreachable.ProvisionAccountAsync(NewLogin(), FirstLogin, CancellationToken.None);

        Assert.That(provision, Throws.InstanceOf<StoreUnavailableException>());
    }
}
}
