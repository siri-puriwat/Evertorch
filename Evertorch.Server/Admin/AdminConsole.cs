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
    private const string Help = "Commands: status, players, help";

    private readonly IAdminCommandService m_commands;

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
            WriteStatus(m_commands.GetStatus(), output);
        }
        else if (string.Equals(command, "players", StringComparison.OrdinalIgnoreCase))
        {
            WritePlayers(m_commands.GetPlayers(), output);
        }
        else if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine(Help);
        }
        else
        {
            output.WriteLine("Unknown command. " + Help);
        }
    }

    private static void WriteStatus(ServerStatus status, TextWriter output)
    {
        output.WriteLine(Format("tick {0} at {1} Hz", status.Tick, status.TickRate));
        output.WriteLine(
            Format(
                "tick time last {0:0.00} ms, max {1:0.00} ms, overruns {2}, skipped steps {3}",
                status.LastTickDuration.TotalMilliseconds,
                status.MaxTickDuration.TotalMilliseconds,
                status.Overruns,
                status.SkippedSteps));
        output.WriteLine(
            Format("sessions connected {0}, in world {1}", status.ConnectedSessions, status.InWorldSessions));
        output.WriteLine(
            Format(
                "inbound queue {0}, dropped {1}, malformed {2}, ignored {3}",
                status.InboundQueueDepth,
                status.InboundDropped,
                status.MalformedMessages,
                status.IgnoredEvents));
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
            output.WriteLine(Format("map {0}: {1} players", map.Key, map.Value));
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
                    "connection {0} character {1} entity {2} {3} at {4} rtt {5} ms inputs queued {6} stale {7} dropped {8}",
                    player.Connection,
                    player.Character.Value,
                    player.Entity.Value,
                    player.Map.Value,
                    player.Position,
                    player.RoundTripMilliseconds,
                    player.QueuedInputs,
                    player.StaleInputs,
                    player.DroppedInputs));
        }
    }

    private static string Format(string format, params object[] arguments)
    {
        return string.Format(CultureInfo.InvariantCulture, format, arguments);
    }
}
}
