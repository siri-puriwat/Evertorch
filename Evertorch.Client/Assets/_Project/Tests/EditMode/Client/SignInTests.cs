using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The sign-in's request and answer as the client writes and reads them, and its words for each refusal (Network
///     Protocol §4).
/// </summary>
[TestFixture]
public sealed class SignInTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE";

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("{\"transport\":\"websocket\",\"host\":\"127.0.0.1\",\"port\":7777,\"token\":\"" + Token + "\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"\",\"port\":7777,\"token\":\"" + Token + "\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"127.0.0.1\",\"port\":0,\"token\":\"" + Token + "\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"127.0.0.1\",\"port\":70000,\"token\":\"" + Token + "\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"127.0.0.1\",\"port\":7777,\"token\":\"short\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"127.0.0.1\",\"port\":7777,\"token\":\"dev:tester\"}")]
    [TestCase("{\"transport\":\"udp\",\"host\":\"a b\",\"port\":7777,\"token\":\"" + Token + "\"}")]
    public void ReadAnswer_OfAnythingElse_IsNothing(string json)
    {
        Assert.That(GatewaySignIn.ReadAnswer(json), Is.Null);
    }

    [TestCase(0, "could not be reached, or its certificate is not trusted")]
    [TestCase(400, "could not read the sign-in")]
    [TestCase(401, "Wrong login or password.")]
    [TestCase(429, "Too many sign-ins")]
    [TestCase(503, "not ready")]
    [TestCase(500, "HTTP 500")]
    public void Messages_SayWhatEachAnswerMeans(long status, string expected)
    {
        Assert.That(SignInMessages.ForStatus(status), Does.Contain(expected));
    }

    [TestCase(RuntimePlatform.WebGLPlayer, false)]
    [TestCase(RuntimePlatform.WindowsPlayer, true)]
    [TestCase(RuntimePlatform.WindowsEditor, true)]
    [TestCase(RuntimePlatform.Android, true)]
    [TestCase(RuntimePlatform.IPhonePlayer, true)]
    public void CanConnectFrom_EveryPlatformButTheWeb_UntilItPlaysOverWebSocket(RuntimePlatform platform, bool expected)
    {
        Assert.That(GameClient.CanConnectFrom(platform), Is.EqualTo(expected));
    }

    [Test]
    public void AuthenticationFailed_AsksForTheLoginAndPassword()
    {
        Assert.That(
            DisconnectMessages.ForReason(DisconnectReason.AuthenticationFailed),
            Is.EqualTo("Sign-in failed. Check the login and password and sign in again."));
    }

    [Test]
    public void ReadAnswer_OfTheGatewaysAnswer_GivesWhereToConnectAndTheToken()
    {
        SignInAnswer? answer = GatewaySignIn.ReadAnswer(
            "{\"transport\":\"udp\",\"host\":\"127.0.0.1\",\"port\":7777,\"token\":\"" + Token + "\"}");

        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Host, Is.EqualTo("127.0.0.1"));
        Assert.That(answer.Port, Is.EqualTo(7777));
        Assert.That(answer.Token, Is.EqualTo(Token));
    }

    [Test]
    public void RequestJson_EscapesAQuoteOrABackslashInThePassword()
    {
        string json = GatewaySignIn.RequestJson("alice", "a\"b\\c!d~e");

        Assert.That(json,
            Is.EqualTo("{\"login\":\"alice\",\"password\":\"a\\\"b\\\\c!d~e\",\"transports\":[\"udp\"]}"));
    }

    [Test]
    public void RequestJson_IsTheLoginPasswordAndTheUdpTransport_InThatOrder()
    {
        string json = GatewaySignIn.RequestJson("alice", "Correct-Horse-9");

        Assert.That(json,
            Is.EqualTo("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]}"));
    }
}
}
