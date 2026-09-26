using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Evertorch.Server
{
/// <summary>
///     Parses development console lines into <see cref="IAdminCommandService" /> calls and prints the answers.
/// </summary>
public sealed class AdminConsole
{
    private const string Help = "Commands: status, players, save, shutdown [reason], help";
    private const string ShutdownCommand = "shutdown";

    private readonly IAdminCommandService m_commands;
    private readonly AdminActor m_actor = AdminActor.LocalConsole;

    public AdminConsole(IAdminCommandService commands)
    {
        m_commands = commands;
    }

    /// <summary>
    ///     Reads lines until the input ends or stopping is requested. A process without a console has no input, so this
    ///     simply returns.
    /// </summary>
    public void Run(TextReader input, TextWriter output, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            string? line = input.ReadLine();
            if (line == null)
            {
                return;
            }

            Execute(line, output);
        }
    }

    public void Execute(string line, TextWriter output)
    {
        string command = line.Trim();
        if (command.Length == 0)
        {
            return;
        }

        if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
        {
            WriteStatus(m_commands.GetStatus(m_actor), output);
        }
        else if (string.Equals(command, "players", StringComparison.OrdinalIgnoreCase))
        {
            WritePlayers(m_commands.GetPlayers(m_actor), output);
        }
        else if (string.Equals(command, "save", StringComparison.OrdinalIgnoreCase))
        {
            // The count arrives with the audit event, once a tick has run the save.
            _ = m_commands.SaveAsync(m_actor);
            output.WriteLine("Save queued for the next tick.");
        }
        else if (IsShutdown(command, out string reason))
        {
            m_commands.Shutdown(m_actor, reason);
            output.WriteLine("Shutting down.");
        }
        else if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine(Help);
        }
        else
        {
            output.WriteLine($"Unknown command. {Help}");
        }
    }

    // "shutdown" alone, or followed by white space and the reason.
    private static bool IsShutdown(string command, out string reason)
    {
        reason = string.Empty;
        if (!command.StartsWith(ShutdownCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (command.Length == ShutdownCommand.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(command[ShutdownCommand.Length]))
        {
            return false;
        }

        reason = command.Substring(ShutdownCommand.Length).Trim();
        return true;
    }

    private static void WriteStatus(ServerStatus status, TextWriter output)
    {
        output.WriteLine(
            Format(
                "uptime {0}, tick {1} at {2} Hz",
                status.PublishedAt.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture),
                status.Tick,
                status.TickRate));
        output.WriteLine(
            Format(
                "tick time last {0:0.00} ms, max {1:0.00} ms, overruns {2}, skipped steps {3}",
                status.LastTickDuration.TotalMilliseconds,
                status.MaxTickDuration.TotalMilliseconds,
                status.Overruns,
                status.SkippedSteps));
        output.WriteLine(
            Format(
                "sessions connected {0}, signed in {1}, in world {2}; authentication failures {3}",
                status.ConnectedSessions,
                status.AuthenticatedSessions,
                status.InWorldSessions,
                status.AuthenticationFailures));
        output.WriteLine(status.IsAdmissionOpen ? "admission open" : "admission closed");
        output.WriteLine(
            Format(
                "database {0}, persistence jobs waiting {1}, checkpoints waiting {2}, retries {3}",
                DatabaseStateText(status.Persistence.State),
                status.Persistence.PendingJobs,
                status.Persistence.WaitingCheckpoints,
                status.Persistence.Retries));
        output.WriteLine(
            Format(
                "inbound queue {0}, dropped {1}, malformed {2}, ignored {3}",
                status.InboundQueueDepth,
                status.InboundDropped,
                status.MalformedMessages,
                status.IgnoredEvents));
        output.WriteLine(
            Format(
                "rate limits: messages over budget {0}, commands throttled {1}; violations {2}, disconnects for "
                + "violations {3}",
                status.Abuse.OverBudgetMessages,
                status.Abuse.ThrottledCommands,
                status.Abuse.Violations,
                status.Abuse.ViolationDisconnects));
        output.WriteLine(
            Format(
                "network bytes in {0}, out {1}, packets in {2}, out {3}, lost {4}",
                status.Transport.BytesReceived,
                status.Transport.BytesSent,
                status.Transport.PacketsReceived,
                status.Transport.PacketsSent,
                status.Transport.PacketsLost));
        foreach (KeyValuePair<string, int> map in status.PlayersPerMap)
        {
            status.MonstersPerMap.TryGetValue(map.Key, out int monsters);
            output.WriteLine(Format("map {0}: {1} players, {2} monsters", map.Key, map.Value, monsters));
        }
    }

    private static void WritePlayers(IReadOnlyList<PlayerSummary> players, TextWriter output)
    {
        if (players.Count == 0)
        {
            output.WriteLine("No players in the world.");
            return;
        }

        foreach (PlayerSummary player in players)
        {
            output.WriteLine(
                Format(
                    "connection {0} character {1} entity {2} {3} at {4} rtt {5} ms inputs queued {6} stale {7} dropped {8}"
                    + " refused {9} level {10} exp {11} other epoch {12} coins {13}",
                    player.Connection,
                    player.Character.Value,
                    player.Entity.Value,
                    player.Map.Value,
                    player.Position,
                    player.RoundTripMilliseconds,
                    player.QueuedInputs,
                    player.StaleInputs,
                    player.DroppedInputs,
                    player.RefusedCommands,
                    player.Level,
                    player.Experience,
                    player.OtherEpochInputs,
                    player.Coins));
        }
    }

    private static string DatabaseStateText(DatabaseState state)
    {
        switch (state)
        {
            case DatabaseState.Available:
                return "available";
            case DatabaseState.Unavailable:
                return "unavailable";
            case DatabaseState.PendingMigrations:
                return "waiting for migrations";
            default:
                return "not yet asked";
        }
    }

    private static string Format(string format, params object[] arguments)
    {
        return string.Format(CultureInfo.InvariantCulture, format, arguments);
    }
}
}
