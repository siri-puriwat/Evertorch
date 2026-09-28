using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Evertorch.Client;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The console's <c>save</c> and <c>shutdown</c> (System Architecture §8, §10; Persistence §6): what reaches the
///     tick thread, what is audited, and what the clients are told.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AdminCommandTests
{
    private static IReadOnlyDictionary<string, object?> Audited(TestServer server, string eventName)
    {
        return server.AuditLogger.Entries.Single(entry => entry.EventId.Name == eventName).Fields;
    }

    [TestCase(100, "é", 64)]
    [TestCase(50, "€", 42)]
    [TestCase(40, "\U0001F600", 32)]
    [TestCase(10, "ok ", 10)]
    public void NoticeText_OverTheNoticeLimit_IsCutOnACharacterBoundary(int count, string character, int kept)
    {
        string reason = string.Concat(Enumerable.Repeat(character, count));

        string text = ShutdownRequest.ToNoticeText(reason);

        Assert.That(text, Is.EqualTo(string.Concat(Enumerable.Repeat(character, kept))));
        Assert.That(Encoding.UTF8.GetByteCount(text), Is.LessThanOrEqualTo(ProtocolLimits.MaxNoticeMessageBytes));
    }

    [Test]
    public void Console_Save_QueuesItAndTheAuditSaysHowMany()
    {
        var server = new TestServer();
        server.EnterWorld(7);
        var output = new StringWriter();

        new AdminConsole(server.Admin).Execute("save", output);
        server.Tick();

        Assert.That(output.ToString().Trim(), Is.EqualTo("Save queued for the next tick."));
        Assert.That(Audited(server, "OperatorSaved")["Queued"], Is.EqualTo(1));
    }

    [Test]
    public void Console_ShutdownWithoutAReason_StopsWithAnEmptyText()
    {
        var server = new TestServer();

        new AdminConsole(server.Admin).Execute("SHUTDOWN", new StringWriter());

        Assert.That(server.ApplicationLifetime.ApplicationStopping.IsCancellationRequested, Is.True);
        Assert.That(server.Shutdown.Message, Is.Empty);
    }

    [Test]
    public void Console_Shutdown_WithAReason_AuditsItAndStopsTheApplication()
    {
        var server = new TestServer();
        var output = new StringWriter();

        new AdminConsole(server.Admin).Execute("  shutdown   Back in five minutes  ", output);

        Assert.That(output.ToString().Trim(), Is.EqualTo("Shutting down."));
        Assert.That(server.ApplicationLifetime.ApplicationStopping.IsCancellationRequested, Is.True);
        Assert.That(server.Shutdown.Message, Is.EqualTo("Back in five minutes"));
        IReadOnlyDictionary<string, object?> audit = Audited(server, "OperatorShutdown");
        Assert.That(audit["Actor"], Is.EqualTo("console"));
        Assert.That(audit["Reason"], Is.EqualTo("Back in five minutes"));
    }

    [Test]
    public void NoticeText_WithABrokenSurrogate_StillEncodes()
    {
        string text = ShutdownRequest.ToNoticeText("\uD800back");

        Action encode = () => new DisconnectNotice(DisconnectReason.Maintenance, text).GetEncodedLength();

        Assert.That(text, Is.EqualTo("\uFFFDback"));
        Assert.That(encode, Throws.Nothing);
    }

    [Test]
    public void Save_LeavesACharacterThatIsLoggingOut_SoItsLogoutStillCompletes()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        server.RunsPersistence = false;
        server.SendLogout(player, 1);
        server.Tick();

        Task<int> saved = server.Admin.SaveAsync(AdminActor.LocalConsole);
        server.Tick();
        server.RunsPersistence = true;
        server.Tick(2);

        Assert.That(saved.IsCompletedSuccessfully, Is.True, "a tick ran the save");
        Assert.That(saved.Result, Is.Zero);
        Assert.That(server.Transport.ControlOpcodesSentTo(player), Does.Contain(MessageOpcode.LogoutComplete));
        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.Authenticated));
    }

    [Test]
    public void Save_QueuesACheckpointForEveryCharacterInTheWorldOrItsGracePeriod_AndAuditsTheCount()
    {
        var server = new TestServer(reconnectGraceMs: 30000);
        ConnectionId staying = server.EnterWorld(7);
        ConnectionId leaving = server.EnterWorld(8);
        server.Disconnect(leaving);
        server.Tick(2);
        int checkpointsBefore = server.Store.Checkpoints.Count;

        Task<int> saved = server.Admin.SaveAsync(AdminActor.LocalConsole);
        server.Tick(2);

        Assert.That(saved.IsCompletedSuccessfully, Is.True);
        Assert.That(saved.Result, Is.EqualTo(2));
        Assert.That(
            server.Store.Checkpoints.Skip(checkpointsBefore).Select(checkpoint => checkpoint.CharacterId),
            Is.EquivalentTo(new[] { 7L, 8L }));
        IReadOnlyDictionary<string, object?> audit = Audited(server, "OperatorSaved");
        Assert.That(audit["Actor"], Is.EqualTo("console"));
        Assert.That(audit["User"], Is.EqualTo(Environment.UserName));
        Assert.That(audit["Queued"], Is.EqualTo(2));
        Assert.That(server.SessionOf(staying).State, Is.EqualTo(SessionState.InWorld));
    }

    [Test]
    public void Shutdown_FromTheConsole_TellsEveryClientMaintenanceWithTheReason()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        using IHost host = TestHosts
            .CreateBuilder(new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true" }, root.Path)
            .Build();
        host.Start();
        using var client = new SocketClient(
            host.Services.GetRequiredService<ServerContent>(),
            "console-shutdown",
            "Console1");
        client.EnterWorld(host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort);

        host.Services.GetRequiredService<AdminConsole>().Execute("shutdown Back soon", new StringWriter());
        bool isStopping = host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
        host.StopAsync().GetAwaiter().GetResult();
        bool isClosed = client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected);

        Assert.That(isStopping, Is.True);
        Assert.That(isClosed, Is.True);
        Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(client.Connection.Notice?.Message, Is.EqualTo("Back soon"));
    }
}
}
