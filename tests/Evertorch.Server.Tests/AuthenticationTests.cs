using System.Linq;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Session tokens become accounts through <see cref="ISessionTokenValidator" />, off the tick thread, and admission
///     needs the database (Network Protocol §4, Persistence §9).
/// </summary>
[TestFixture]
public sealed class AuthenticationTests
{
    private static void AssertRefused(TestServer server, ConnectionId connection, DisconnectReason reason)
    {
        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(reason));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.False);
    }

    [Test]
    public void Disconnect_WhileTheAccountIsLookedUp_EndsQuietlyWhenItAnswers()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);
        server.Disconnect(connection);

        server.Tick(2);

        Assert.That(server.Sessions.Sessions, Is.Empty);
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.Transport.Disconnects, Is.Empty);
    }

    [Test]
    public void Hello_AfterTheDatabaseWasFoundUnreachable_IsRefusedAtOnceWithoutADatabaseCall()
    {
        var server = new TestServer();
        server.Store.IsUnavailable = true;
        server.Persistence.Probe();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ServerNotReady);
        Assert.That(server.Store.Logins, Is.Empty);
    }

    [Test]
    public void Hello_BeforeTheAccountAnswers_SendsNothingYet()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticating));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
    }

    [Test]
    public void Hello_ForADisabledAccount_IsRefusedWithAuthenticationFailed()
    {
        var server = new TestServer();
        server.Store.Disable("dev:tester");
        ConnectionId connection = server.Connect();

        server.SignIn(connection);

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_ForTheSameIdentityInAnotherLetterCase_SignsInToTheSameAccount()
    {
        var server = new TestServer();
        ConnectionId first = server.Connect();
        ConnectionId second = server.Connect();

        server.SignIn(first, "dev:Ann");
        server.SignIn(second, "dev:aNN");

        Assert.That(server.Store.AccountCount, Is.EqualTo(1));
        Assert.That(server.SessionOf(second).Account, Is.EqualTo(server.SessionOf(first).Account));
    }

    [Test]
    public void Hello_QueuedBehindWorkThatMeetsAnOutage_IsRefusedAsNotReady()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        ConnectionId connection = server.Connect();
        server.SendLogout(player, 1);
        server.SendHello(connection);
        server.Tick();
        server.Store.IsUnavailable = true;

        // The logout's checkpoint meets the outage first; the account lookup queued behind it is never tried.
        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ServerNotReady);
        Assert.That(server.Store.Logins, Has.Count.EqualTo(1), "only the player already in the world signed in");
    }

    [Test]
    public void Hello_WhenTheDatabaseHasPendingMigrations_IsRefusedAsNotReady()
    {
        var server = new TestServer();
        server.Store.PendingMigrations = new[] { "20990101000000_Later" };
        server.Persistence.Probe();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ServerNotReady);
    }

    [Test]
    public void Hello_WhenThePersistenceQueueIsFull_IsRefusedAsNotReady()
    {
        var server = new TestServer(persistence: new PersistenceOptions { QueueCapacity = 1 });
        server.RunsPersistence = false;
        server.Persistence.TryEnqueue(
            new PersistenceJob<int>("filler", default, 0, (_, _) => Task.FromResult(0), (_, _) =>
            {
            }));
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ServerNotReady);
    }

    [Test]
    public void Hello_WhileTheDatabaseIsUnreachable_IsRefusedAsNotReady()
    {
        var server = new TestServer();
        server.Store.IsUnavailable = true;
        ConnectionId connection = server.Connect();

        server.SignIn(connection);

        AssertRefused(server, connection, DisconnectReason.ServerNotReady);
    }

    [Test]
    public void Hello_WithANewIdentity_ProvisionsItsAccountAndKeepsItOnTheSession()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();

        server.SignIn(connection, "dev:Ann");

        Assert.That(server.Store.Logins, Is.EqualTo(new[] { "dev:ann" }));
        Assert.That(server.SessionOf(connection).Account, Is.EqualTo(new AccountId(1)));
        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(server.Transport.ControlOpcodesSentTo(connection), Is.EqualTo(new[] { MessageOpcode.ServerHello }));
    }

    [Test]
    public void Session_AfterSigningIn_KeepsNoTextAtAll()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SignIn(connection, "dev:very-secret-identity");

        string[] textProperties = typeof(ClientSession)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToArray();

        Assert.That(textProperties, Is.Empty, "the token is checked and dropped; the session keeps the account");
        Assert.That(
            server.Log.Entries.Select(entry => entry.Message),
            Has.None.Contains("very-secret-identity").And.Some.Contains("account 1"));
    }
}
}
