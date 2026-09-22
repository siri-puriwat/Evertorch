using System;
using System.Collections.Generic;
using Evertorch.Client;

namespace Evertorch.Server.Tests
{
/// <summary>
///     One in-memory server and any number of simulated clients on a shared millisecond clock. The server ticks every
///     50 ms; each client ticks at the same rate but out of phase with it, as a real client would.
/// </summary>
internal sealed class ClientServerRig
{
    private const int TickMilliseconds = 1000 / TestServer.TickRate;

    private readonly List<SimulatedClient> m_clients = new();
    private readonly List<int> m_phases = new();
    private int m_nowMilliseconds;

    public TestServer Server { get; } = new();

    public int NowMilliseconds => m_nowMilliseconds;

    public SimulatedClient AddClient(long character, int seed, int phaseMilliseconds)
    {
        var client = new SimulatedClient(Server, character, seed, () => m_nowMilliseconds / 1000.0);
        m_clients.Add(client);
        m_phases.Add(phaseMilliseconds % TickMilliseconds);
        return client;
    }

    /// <param name="beforeClientTick">Called just before a client's tick, to script its input.</param>
    public void Advance(int milliseconds, Action<SimulatedClient>? beforeClientTick = null)
    {
        for (int step = 0; step < milliseconds; step++)
        {
            m_nowMilliseconds++;
            if (m_nowMilliseconds % TickMilliseconds == 0)
            {
                Server.Tick();
            }

            for (int index = 0; index < m_clients.Count; index++)
            {
                SimulatedClient client = m_clients[index];
                client.Poll();
                if (client.Connection.State == ClientConnectionState.InWorld)
                {
                    client.World.Advance(0.001f);
                    if (m_nowMilliseconds % TickMilliseconds == m_phases[index])
                    {
                        beforeClientTick?.Invoke(client);
                        client.Tick();
                    }
                }
            }
        }
    }

    public void ConnectAll()
    {
        foreach (SimulatedClient client in m_clients)
        {
            client.Connection.Connect("loopback", 0);
        }
    }

    /// <summary>
    ///     Advances until the condition holds. Returns the time it took, or -1 when the limit was reached first.
    /// </summary>
    public int AdvanceUntil(
        Func<bool> condition,
        int limitMilliseconds,
        Action<SimulatedClient>? beforeClientTick = null)
    {
        for (int elapsed = 0; elapsed <= limitMilliseconds; elapsed++)
        {
            if (condition())
            {
                return elapsed;
            }

            Advance(1, beforeClientTick);
        }

        return -1;
    }
}
}
