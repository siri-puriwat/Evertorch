using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Evertorch.Client;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     <c>POST /session</c> over HTTPS on the composed host (Network Protocol §4, §11; System Architecture §10, §13),
///     with the test's own certificate pinned by its client.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class GatewayTests
{
    private const string Password = "Correct-Horse-9";
    private const string WrongPassword = "Wrong-Horse-9";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private TestCertificate m_certificate = null!;

    // One host for the tests of the request and its answers, whose limits no test reaches.
    private TemporaryDirectory m_sharedRoot = null!;
    private InMemoryGameStore m_sharedStore = null!;
    private IHost m_shared = null!;

    [OneTimeSetUp]
    public void StartSharedHost()
    {
        m_certificate = new TestCertificate();
        m_sharedRoot = new TemporaryDirectory();
        m_sharedStore = new InMemoryGameStore();
        m_shared = StartHost(
            m_sharedRoot,
            m_sharedStore,
            null,
            "--Abuse:SignInBurst=10000",
            "--Abuse:SignInFailureBurst=1000");
        CreateAccount(m_shared, "alice");
        CreateAccount(m_shared, "disabled");
        m_sharedStore.Disable("disabled");
    }

    [OneTimeTearDown]
    public void StopSharedHost()
    {
        m_shared.StopAsync().GetAwaiter().GetResult();
        m_shared.Dispose();
        m_sharedRoot.Dispose();
        m_certificate.Dispose();
    }

    private IHost StartHost(
        TemporaryDirectory root,
        InMemoryGameStore store,
        CapturingLoggerProvider? logs,
        params string[] settings)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithGateway(
            new[] { "--Network:Port=0", "--Persistence:IdleProbeIntervalMs=100", "--Logging:LogLevel:Default=Debug" }
                .Concat(settings)
                .ToArray(),
            root.Path,
            store,
            m_certificate);
        if (logs != null)
        {
            builder.Logging.AddProvider(logs);
            builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning);
        }

        IHost host = builder.Build();
        host.Start();
        Assert.That(WaitUntil(() => host.Services.GetRequiredService<HealthProbe>().Evaluate().IsReady), Is.True,
            "ready");
        return host;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < WaitLimit)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
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

    private static void CreateAccount(IHost host, string login)
    {
        AccountCommandResult result = host.Services.GetRequiredService<IAdminCommandService>()
            .CreateAccountAsync(AdminActor.LocalConsole, login, Password)
            .GetAwaiter()
            .GetResult();
        Assert.That(result.Outcome, Is.EqualTo(AccountCommandOutcome.Created));
    }

    private static int GatewayPort(IHost host)
    {
        return host.Services.GetRequiredService<GatewayEndpoint>().Port;
    }

    private (HttpStatusCode Status, string Body, bool IsNoStore) Send(
        IHost host,
        HttpMethod method,
        string path,
        HttpContent? content)
    {
        using HttpClient http = m_certificate.CreateClient();
        using var request = new HttpRequestMessage(method, $"https://127.0.0.1:{GatewayPort(host)}{path}")
        {
            Content = content
        };
        using HttpResponseMessage response = http.SendAsync(request).GetAwaiter().GetResult();
        return (
            response.StatusCode,
            response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
            response.Headers.CacheControl?.NoStore == true);
    }

    private (HttpStatusCode Status, string Body, bool IsNoStore) Post(IHost host, string json)
    {
        return Send(host, HttpMethod.Post, GatewayEndpoint.SessionPath, TestCertificate.JsonContent(json));
    }

    [TestCase("alice", WrongPassword)]
    [TestCase("nobody", Password)]
    [TestCase("dev:tester", Password)]
    [TestCase("disabled", Password)]
    [TestCase("al!ce", Password)]
    [TestCase("alice", "short")]
    [TestCase("alice", "with space9")]
    public void SignIn_ForEveryCredentialRefusal_AnswersTheSame401(string login, string password)
    {
        (HttpStatusCode status, string body, bool isNoStore) =
            Post(m_shared, TestCertificate.SignInJson(login, password));

        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(body, Is.Empty);
        Assert.That(isNoStore, Is.True);
    }

    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\"}")]
    [TestCase("{\"login\":\"alice\",\"transports\":[\"udp\"]}")]
    [TestCase("{\"login\":7,\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]}")]
    [TestCase("{\"Login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"websocket\"]}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":\"udp\"}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\",1]}")]
    [TestCase("{\"login\":\"alice\",\"login\":\"bob\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]} {}")]
    [TestCase("{\"login\":\"alice\",\"password\":\"Correct-Horse-9\",\"transports\":[\"udp\"]")]
    public void SignIn_WithAMalformedBody_Answers400(string json)
    {
        (HttpStatusCode status, string body, _) = Post(m_shared, json);

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(body, Is.Empty);
    }

    [TestCase("wrong")]
    [TestCase("")]
    public void Play_WithAWrongKey_Answers403WithoutUpgrading(string key)
    {
        using ClientWebSocket socket = m_certificate.CreateWebSocket();
        var url = new Uri($"wss://127.0.0.1:{GatewayPort(m_shared)}{GatewayEndpoint.PlayPath}?key={key}");

        Action connect = () => socket.ConnectAsync(url, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(connect, Throws.InstanceOf<WebSocketException>());
        Assert.That(socket.HttpStatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase("GET", GatewayEndpoint.SessionPath)]
    [TestCase("PUT", GatewayEndpoint.SessionPath)]
    [TestCase("POST", "/session/")]
    [TestCase("POST", "/Session")]
    [TestCase("POST", "/health/ready")]
    public void OtherMethodsAndPaths_Answer404(string method, string path)
    {
        HttpContent content = TestCertificate.JsonContent(TestCertificate.SignInJson("alice", Password));

        Assert.That(Send(m_shared, new HttpMethod(method), path, content).Status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // Network Protocol §4: a refusal's time tells nothing, so a login nobody has costs a hash like a wrong password.
    [TestCase("ghost")]
    [TestCase("dev:ghost")]
    public void SignIn_ForALoginNobodyHas_TakesAsLongAsAWrongPassword(string login)
    {
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(
            root,
            new InMemoryGameStore(),
            null,
            "--Accounts:PasswordIterations=200000",
            "--Abuse:SignInFailureBurst=1000");
        CreateAccount(host, "alice");
        Post(host, TestCertificate.SignInJson("warm-up", Password));

        double wrong = FastestMilliseconds(() => Post(host, TestCertificate.SignInJson("alice", WrongPassword)));
        double nobody = FastestMilliseconds(() => Post(host, TestCertificate.SignInJson(login, Password)));

        Assert.That(nobody, Is.GreaterThan(wrong / 2), $"a wrong password took {wrong:0.0} ms");
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void Hello_WithADevelopmentToken_OnAServerThatTakesPasswords_IsRefused()
    {
        ServerContent content = m_shared.Services.GetRequiredService<ServerContent>();
        using var client = new SocketClient(content, "tester", "Gateway2");

        client.Connect(m_shared.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort);
        client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected);

        Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.AuthenticationFailed));
    }

    // Network Protocol §7: the browser's path end to end, with a managed WebSocket standing in for it.
    [Test]
    public void Play_WithTheKey_ThenAHelloWithASignedInToken_IsAnsweredWithServerHelloOverWebSocket()
    {
        GatewaySignInResult answer = SocketClient.SignIn(m_certificate, GatewayPort(m_shared), "alice", Password);
        uint content = m_shared.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;
        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, content,
            answer.Token);
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        ClientWebSocket socket = m_certificate.CreateWebSocket();
        socket.ConnectAsync(
                new Uri($"wss://127.0.0.1:{GatewayPort(m_shared)}{GatewayEndpoint.PlayPath}?key=evertorch"),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        using var peer = new TestWebSocketPeer(socket);

        peer.Send(ProtocolChannel.Control, payload);
        byte[]? first = peer.Receive(TimeSpan.FromSeconds(10));

        Assert.That(first, Is.Not.Null, "the server answered");
        Assert.That(first![0], Is.EqualTo((byte)ProtocolChannel.Control));
        Assert.That(MessageRouting.TryReadOpcode(first.AsSpan(1), out MessageOpcode opcode), Is.True);
        Assert.That(opcode, Is.EqualTo(MessageOpcode.ServerHello));
    }

    [Test]
    public void Play_WithoutAWebSocketUpgrade_Answers400()
    {
        Assert.That(
            Send(m_shared, HttpMethod.Get, $"{GatewayEndpoint.PlayPath}?key=evertorch", null).Status,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void SignIn_ThenAHelloWithItsToken_EntersTheWorld()
    {
        GatewaySignInResult answer = SocketClient.SignIn(m_certificate, GatewayPort(m_shared), "alice", Password);
        ServerContent content = m_shared.Services.GetRequiredService<ServerContent>();

        using var client = SocketClient.WithToken(content, answer.Token, "Gateway1");
        client.EnterWorld(answer.Port);

        Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo("Gateway1"));
    }

    [Test]
    public void SignIn_WhileTheDatabaseIsUnavailable_Answers503()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using IHost host = StartHost(root, store, null);
        CreateAccount(host, "alice");
        store.IsUnavailable = true;
        HealthProbe health = host.Services.GetRequiredService<HealthProbe>();
        Assert.That(WaitUntil(() => !health.Evaluate().IsReady), Is.True, "readiness followed the database");

        (HttpStatusCode status, string body, bool isNoStore) =
            Post(host, TestCertificate.SignInJson("alice", Password));

        Assert.That(status, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(body, Is.Empty);
        Assert.That(isNoStore, Is.True);
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void SignIn_WithABodyOverOneKibibyte_Answers400()
    {
        string json = TestCertificate.SignInJson("alice", Password).TrimEnd('}') + ",\"pad\":\"" + new string('x', 1024)
            + "\"}";

        Assert.That(Post(m_shared, json).Status, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void SignIn_WithAnotherContentType_Answers400()
    {
        var content = new StringContent(TestCertificate.SignInJson("alice", Password), Encoding.UTF8, "text/plain");

        Assert.That(
            Send(m_shared, HttpMethod.Post, GatewayEndpoint.SessionPath, content).Status,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void SignIn_WithExtraMembers_IgnoresThem()
    {
        string json = "{\"client\":{\"build\":\"x\"},\"login\":\"alice\",\"password\":\"Correct-Horse-9\","
            + "\"transports\":[\"websocket\",\"udp\"]}";

        Assert.That(Post(m_shared, json).Status, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void SignIn_WithTheRightPassword_AnswersTheUdpEndpointAndAStoredToken()
    {
        GatewaySignInResult answer = SocketClient.SignIn(m_certificate, GatewayPort(m_shared), "ALICE", Password);

        Assert.That(answer.Host, Is.EqualTo("127.0.0.1"), "the UDP bind address");
        Assert.That(answer.Port,
            Is.EqualTo(m_shared.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort));
        Assert.That(SessionToken.TryParse(answer.Token, out byte[] hash), Is.True);
        StoredSessionToken? stored =
            m_sharedStore.FindSessionTokenAsync(hash, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.ExpiresAt - DateTime.UtcNow,
            Is.InRange(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15)));
    }

    [Test]
    public void SignIns_FromOneAddressOverItsBurst_AreAnswered429()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using IHost host = StartHost(root, store, null, "--Abuse:SignInsPerSecond=1", "--Abuse:SignInBurst=3");

        HttpStatusCode[] answers = Enumerable.Range(0, 4)
            .Select(_ => Post(host, TestCertificate.SignInJson("nobody", Password)).Status)
            .ToArray();

        Assert.That(
            answers,
            Is.EqualTo(new[]
            {
                HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                HttpStatusCode.TooManyRequests
            }));
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void SignIns_LogTheAccountAndTheCertificate_AndNeverALoginPasswordOrToken()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        var store = new InMemoryGameStore();
        using IHost host = StartHost(root, store, logs, "--Logging:LogLevel:Evertorch.Audit=Debug");
        CreateAccount(host, "secret-login");
        ServerContent content = host.Services.GetRequiredService<ServerContent>();

        GatewaySignInResult answer = SocketClient.SignIn(m_certificate, GatewayPort(host), "secret-login", Password);
        Post(host, TestCertificate.SignInJson("secret-login", WrongPassword));
        Post(host, TestCertificate.SignInJson("secret-ghost", Password));
        Post(host, "{\"login\":\"secret-malformed\"}");
        using (var player = SocketClient.WithToken(content, answer.Token, "Logged1"))
        {
            player.EnterWorld(answer.Port);
        }

        string forgedToken = SessionToken.Create().Text;
        using (var forged = SocketClient.WithToken(content, forgedToken, "Forged1"))
        {
            forged.Connect(answer.Port);
            forged.PumpUntil(() => forged.Connection.State == ClientConnectionState.Disconnected);
            Assert.That(forged.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.AuthenticationFailed));
        }

        host.StopAsync().GetAwaiter().GetResult();

        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(lines, Has.Some.Contains("GatewayListening").And.Contains(m_certificate.Thumbprint));
        Assert.That(lines, Has.Some.Contains("SignedIn").And.Contains("900"));
        Assert.That(lines.Count(line => line.Contains("SignInRefused")), Is.EqualTo(2), "the two refused credentials");
        Assert.That(lines.Where(line => line.Contains("secret", StringComparison.OrdinalIgnoreCase)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains(Password) || line.Contains(WrongPassword)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains(answer.Token) || line.Contains(forgedToken)), Is.Empty);
    }

    [Test]
    public void SignIns_OfOneLoginOverItsFailures_Are429_EvenWithTheRightPassword_AndNoOtherLoginIs()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using IHost host = StartHost(root, store, null, "--Abuse:SignInFailureBurst=2",
            "--Abuse:SignInFailuresPerMinute=1");
        CreateAccount(host, "alice");
        CreateAccount(host, "bob");

        HttpStatusCode[] alice =
        {
            Post(host, TestCertificate.SignInJson("alice", WrongPassword)).Status,
            Post(host, TestCertificate.SignInJson("Alice", WrongPassword)).Status,
            Post(host, TestCertificate.SignInJson("alice", Password)).Status
        };
        HttpStatusCode[] ghost =
        {
            Post(host, TestCertificate.SignInJson("ghost", WrongPassword)).Status,
            Post(host, TestCertificate.SignInJson("ghost", WrongPassword)).Status,
            Post(host, TestCertificate.SignInJson("ghost", Password)).Status
        };
        HttpStatusCode bob = Post(host, TestCertificate.SignInJson("bob", Password)).Status;

        Assert.That(
            alice,
            Is.EqualTo(new[]
                { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        Assert.That(ghost, Is.EqualTo(alice), "a login nobody has is limited alike, so the limit tells nothing");
        Assert.That(bob, Is.EqualTo(HttpStatusCode.OK));
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void Start_WithACertificatePasswordThatDoesNotOpenIt_FailsWithoutRepeatingIt()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        using IHost host = TestHosts.CreateBuilderWithGateway(
                new[] { "--Network:Port=0", "--Gateway:CertificatePassword=hunter2-pfx" },
                root.Path,
                new InMemoryGameStore(),
                m_certificate)
            .Build();

        Action start = () => host.Start();

        Assert.That(
            start,
            Throws.InvalidOperationException
                .With.Message.Contains("Gateway:CertificatePath")
                .And.Message.Not.Contains("hunter2"));
    }
}
}
