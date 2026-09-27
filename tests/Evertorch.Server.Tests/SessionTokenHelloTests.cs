using System;
using System.Threading;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A hello carrying a session token the gateway issued (Network Protocol §4): accepted until it expires, then
///     answered <c>SessionExpired</c>; refused for anything else, alike.
/// </summary>
[TestFixture]
public sealed class SessionTokenHelloTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static TestServer PasswordOnlyServer()
    {
        return new TestServer(false);
    }

    private static AccountId CreateAccount(TestServer server, string login)
    {
        return server.GameStore
                .CreateAccountAsync(
                    login,
                    PasswordHasher.Scheme,
                    "1000$c2FsdA==$a2V5",
                    server.Time.GetUtcNow().UtcDateTime,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult()
            ?? throw new InvalidOperationException("taken");
    }

    private static SessionToken Issue(TestServer server, AccountId account, TimeSpan issuedAgo)
    {
        var token = SessionToken.Create();
        DateTime issuedAt = server.Time.GetUtcNow().UtcDateTime - issuedAgo;
        server.GameStore
            .IssueSessionTokenAsync(account, token.Hash, issuedAt, issuedAt + Lifetime, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return token;
    }

    private static void AssertRefused(TestServer server, ConnectionId connection, DisconnectReason reason)
    {
        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(reason));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.False);
    }

    [TestCase("dev:tester")]
    [TestCase("not-a-token")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void Hello_WithADevelopmentOrMisshapenToken_OnAServerThatTakesPasswordsOnly_IsRefusedAtOnce(string token)
    {
        TestServer server = PasswordOnlyServer();
        ConnectionId connection = server.Connect();
        server.SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion,
            token);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
        Assert.That(server.Store.Logins, Is.Empty, "nothing was provisioned");
    }

    [Test]
    public void Hello_WithATokenAtTheMomentItExpires_IsRefusedWithSessionExpired()
    {
        TestServer server = PasswordOnlyServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), Lifetime);
        ConnectionId connection = server.Connect();

        server.SignIn(connection, token.Text);

        AssertRefused(server, connection, DisconnectReason.SessionExpired);
    }

    [Test]
    public void Hello_WithATokenEndedByAPasswordChange_IsRefusedWithAuthenticationFailed()
    {
        TestServer server = PasswordOnlyServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), TimeSpan.Zero);
        server.GameStore
            .SetAccountPasswordAsync("alice", PasswordHasher.Scheme, "1000$b3RoZXI=$a2V5", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ConnectionId connection = server.Connect();

        server.SignIn(connection, token.Text);

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_WithATokenOfADisabledAccount_IsRefusedWithAuthenticationFailed()
    {
        TestServer server = PasswordOnlyServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), TimeSpan.Zero);
        server.Store.Disable("alice");
        ConnectionId connection = server.Connect();

        server.SignIn(connection, token.Text);

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_WithAWellFormedTokenNobodyWasIssued_IsRefusedWithAuthenticationFailed()
    {
        TestServer server = PasswordOnlyServer();
        ConnectionId connection = server.Connect();

        server.SignIn(connection, SessionToken.Create().Text);

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_WithAnExpiredToken_IsRefusedWithSessionExpired()
    {
        TestServer server = PasswordOnlyServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), TimeSpan.FromMinutes(16));
        ConnectionId connection = server.Connect();

        server.SignIn(connection, token.Text);

        AssertRefused(server, connection, DisconnectReason.SessionExpired);
        Assert.That(server.SessionManager.AuthenticationFailures, Is.EqualTo(1));
    }

    [Test]
    public void Hello_WithAnIssuedToken_AgainAndAgain_SignsInEachTimeUntilItExpires()
    {
        TestServer server = PasswordOnlyServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), TimeSpan.FromMinutes(14));
        ConnectionId first = server.Connect();
        server.SignIn(first, token.Text);
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = server.Connect();
        server.SignIn(second, token.Text);

        Assert.That(server.SessionOf(second).State, Is.EqualTo(SessionState.Authenticated));
    }

    [Test]
    public void Hello_WithAnIssuedToken_SignsInToItsAccount()
    {
        TestServer server = PasswordOnlyServer();
        AccountId account = CreateAccount(server, "alice");
        SessionToken token = Issue(server, account, TimeSpan.Zero);
        ConnectionId connection = server.Connect();

        server.SignIn(connection, token.Text);

        ClientSession session = server.SessionOf(connection);
        Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(session.Account, Is.EqualTo(account));
    }

    [Test]
    public void Hello_WithEitherKindOfToken_WhileDevelopmentSignInIsOn_SignsIn()
    {
        var server = new TestServer();
        SessionToken token = Issue(server, CreateAccount(server, "alice"), TimeSpan.Zero);
        ConnectionId issued = server.Connect();
        ConnectionId development = server.Connect();

        server.SignIn(issued, token.Text);
        server.SignIn(development);

        Assert.That(server.SessionOf(issued).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(server.SessionOf(development).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(server.SessionOf(development).Account, Is.Not.EqualTo(server.SessionOf(issued).Account));
    }
}
}
