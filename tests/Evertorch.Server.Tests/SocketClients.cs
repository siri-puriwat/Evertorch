using System;
using System.Diagnostics;
using System.Threading;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Plays the frame loop of several <see cref="SocketClient" />s on the test's thread, each one's frame in turn, the
///     way two game windows run beside each other.
/// </summary>
internal static class SocketClients
{
    public static bool PumpUntil(Func<bool> condition, params SocketClient[] clients)
    {
        return PumpUntil(condition, SocketClient.Limit, clients);
    }

    public static bool PumpUntil(Func<bool> condition, TimeSpan limit, params SocketClient[] clients)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < limit)
        {
            foreach (SocketClient client in clients)
            {
                client.Pump();
            }

            if (condition())
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    public static void PumpFor(TimeSpan duration, params SocketClient[] clients)
    {
        PumpUntil(() => false, duration, clients);
    }
}
}
