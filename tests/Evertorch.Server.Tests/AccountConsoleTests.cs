using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The console's <c>account create</c> and <c>account password</c> (System Architecture §10; Network Protocol §4):
///     what is stored, what is answered and audited, and that the password is never repeated.
/// </summary>
[TestFixture]
public sealed class AccountConsoleTests
{
    private const string Password = "Correct-Horse-9";
    private const string OtherPassword = "Battery~Staple~7";

    private static string Run(TestServer server, string line)
    {
        var output = new StringWriter();
        Task answered = new AdminConsole(server.Admin).Execute(line, output);
        Assert.That(answered.Wait(TimeSpan.FromSeconds(10)), Is.True, "the command was answered");
        return output.ToString().Trim();
    }

    private static IEnumerable<string> EveryLogLine(TestServer server)
    {
        return server.AuditLogger.Entries
            .Select(entry => entry.Message + " " + string.Join(" ", entry.Fields.Values))
            .Concat(server.Log.Entries.Select(entry => entry.Message))
            .Concat(server.PersistenceLog.Entries.Select(entry => entry.Message));
    }

    private static bool Verifies(TestServer server, string login, string password)
    {
        AccountCredentials? credentials = server.GameStore.FindAccountCredentialsAsync(login, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var hasher = new PasswordHasher(
            Options.Create(
                new AccountOptions { PasswordIterations = TestServer.TestPasswordIterations }));
        return credentials != null
            && hasher.Verify(password, credentials.PasswordScheme, credentials.PasswordHash);
    }

    [TestCase("account")]
    [TestCase("account create alice")]
    [TestCase("account create alice one two")]
    [TestCase("account delete alice Correct-Horse-9")]
    public void Account_WithTheWrongWords_PrintsItsUsageAndStoresNothing(string line)
    {
        var server = new TestServer();

        string answer = Run(server, line);

        Assert.That(answer, Is.EqualTo("Usage: account create|password <login> <password>"));
        Assert.That(server.Store.AccountCount, Is.Zero);
    }

    [TestCase("account create dev:alice Correct-Horse-9", "a login is 1 to 64")]
    [TestCase("account create al!ce Correct-Horse-9", "a login is 1 to 64")]
    [TestCase("account create alice short1", "a password is 8 to 128")]
    [TestCase("account password alice short1", "a password is 8 to 128")]
    public void Account_WithALoginOrPasswordTheRulesRefuse_SaysWhyAndStoresNothing(string line, string reason)
    {
        var server = new TestServer();

        string answer = Run(server, line);

        Assert.That(answer, Does.StartWith("Not ").And.Contain(reason));
        Assert.That(server.Store.AccountCount, Is.Zero);
        Assert.That(server.AuditLogger.Entries, Is.Empty);
    }

    [Test]
    public void AccountCreate_AndItsAnswerAndAudit_NeverRepeatTheLoginOrThePassword()
    {
        var server = new TestServer();

        string answer = Run(server, $"account create Alice.Smith {Password}");
        string refused = Run(server, $"account create alice.smith {OtherPassword}");
        string changed = Run(server, $"account password ALICE.SMITH {OtherPassword}");

        string[] said = EveryLogLine(server).Concat(new[] { answer, refused, changed }).ToArray();
        Assert.That(said, Has.None.Contains(Password).And.None.Contains(OtherPassword));
        Assert.That(said, Has.None.Contains("alice").IgnoreCase);
        Assert.That(
            server.AuditLogger.Entries.Select(entry => entry.EventId.Name),
            Is.EqualTo(new[] { "OperatorAccountCreated", "OperatorPasswordChanged" }));
    }

    [Test]
    public void AccountCreate_ForANewLogin_StoresItsHashAndAuditsTheAccount()
    {
        var server = new TestServer();

        string answer = Run(server, $"account create Alice {Password}");

        Assert.That(answer, Is.EqualTo("Account 1 created."));
        Assert.That(Verifies(server, "alice", Password), Is.True, "stored under the lower-case login");
        Assert.That(Verifies(server, "alice", OtherPassword), Is.False);
        (_, EventId eventId, _, IReadOnlyDictionary<string, object?> fields) =
            server.AuditLogger.Entries.Single();
        Assert.That(eventId.Id, Is.EqualTo(6005));
        Assert.That(fields["Actor"], Is.EqualTo("console"));
        Assert.That(fields["Account"], Is.EqualTo(1L));
    }

    [Test]
    public void AccountCreate_ForATakenLogin_KeepsTheFirstPassword()
    {
        var server = new TestServer();
        Run(server, $"account create alice {Password}");

        string answer = Run(server, $"account create ALICE {OtherPassword}");

        Assert.That(answer, Is.EqualTo("Not created: an account already has that login."));
        Assert.That(Verifies(server, "alice", Password), Is.True);
        Assert.That(server.AuditLogger.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void AccountCreate_WhileTheDatabaseIsUnavailable_SaysSo()
    {
        var server = new TestServer();
        server.Store.IsUnavailable = true;

        string answer = Run(server, $"account create alice {Password}");

        Assert.That(answer, Is.EqualTo("Not changed: the database is unavailable."));
        Assert.That(server.AuditLogger.Entries, Is.Empty);
    }

    [Test]
    public void AccountCreate_WhileTheStoreHasNotAnswered_LeavesTheConsoleFreeForAShutdown()
    {
        var server = new TestServer();
        using var release = new ManualResetEventSlim();
        server.Store.BeforeAccountWrite = () => release.Wait(TimeSpan.FromSeconds(10));
        var console = new AdminConsole(server.Admin);
        var output = new StringWriter();

        Task pending = console.Execute($"account create alice {Password}", TextWriter.Synchronized(output));
        Task shutdown = console.Execute("shutdown", TextWriter.Synchronized(output));

        Assert.That(shutdown.IsCompleted, Is.True);
        Assert.That(server.ApplicationLifetime.ApplicationStopping.IsCancellationRequested, Is.True);
        Assert.That(pending.IsCompleted, Is.False, "the account is still waiting for the store");
        release.Set();
        Assert.That(pending.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(output.ToString(), Does.Contain("Account 1 created."));
    }

    [Test]
    public void AccountPassword_ForALoginNobodyHas_SaysSo()
    {
        var server = new TestServer();

        string answer = Run(server, $"account password alice {Password}");

        Assert.That(answer, Is.EqualTo("Not changed: no account has that login."));
        Assert.That(server.AuditLogger.Entries, Is.Empty);
    }

    [Test]
    public void AccountPassword_ReplacesTheHash_AndEndsTheAccountsTokens()
    {
        var server = new TestServer();
        Run(server, $"account create alice {Password}");
        Run(server, $"account create bob {Password}");
        DateTime now = server.Time.GetUtcNow().UtcDateTime;
        foreach (long account in new[] { 1L, 2L })
        {
            server.GameStore.IssueSessionTokenAsync(
                    new AccountId(account),
                    SessionToken.Create().Hash,
                    now,
                    now.AddMinutes(15),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        string answer = Run(server, $"account password alice {OtherPassword}");

        Assert.That(answer, Is.EqualTo("Password of account 1 changed; its sessions ended."));
        Assert.That(Verifies(server, "alice", OtherPassword), Is.True);
        Assert.That(Verifies(server, "alice", Password), Is.False);
        Assert.That(server.Store.SessionTokenCount(new AccountId(1)), Is.Zero);
        Assert.That(server.Store.SessionTokenCount(new AccountId(2)), Is.EqualTo(1), "another account's stay");
        Assert.That(server.AuditLogger.Entries.Last().EventId.Id, Is.EqualTo(6006));
    }
}
}
