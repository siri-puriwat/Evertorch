using System;
using System.Security.Cryptography;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     The one random source every gameplay outcome draws from: monster placement, combat rolls, AI, and drops. A
///     configured seed replays a run exactly; without one a seed is drawn and logged so the run can still be replayed.
/// </summary>
public sealed class ServerRandom : IRandomSource
{
    private readonly SeededRandomSource m_source;

    public ServerRandom(ulong seed, bool isConfigured)
    {
        Seed = seed;
        IsConfigured = isConfigured;
        m_source = new SeededRandomSource(seed);
    }

    public ulong Seed { get; }

    public bool IsConfigured { get; }

    public int Next(int exclusiveMax)
    {
        return m_source.Next(exclusiveMax);
    }

    public static ServerRandom FromOptions(WorldOptions options)
    {
        if (options.RandomSeed.HasValue)
        {
            return new ServerRandom(options.RandomSeed.Value, true);
        }

        byte[] bytes = RandomNumberGenerator.GetBytes(sizeof(ulong));
        return new ServerRandom(BitConverter.ToUInt64(bytes, 0), false);
    }
}
}
