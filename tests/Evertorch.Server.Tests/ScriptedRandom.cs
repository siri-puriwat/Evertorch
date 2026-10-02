using System.Collections.Generic;
using Evertorch.Rules;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Returns the queued draws in order, then the fallback forever, and records the bound of every draw.
/// </summary>
internal sealed class ScriptedRandom : IRandomSource
{
    private readonly Queue<int> m_draws;
    private readonly int m_fallback;

    public ScriptedRandom(int fallback, params int[] draws)
    {
        m_fallback = fallback;
        m_draws = new Queue<int>(draws);
    }

    public int Calls => Bounds.Count;

    public List<int> Bounds { get; } = new();

    public int Next(int exclusiveMax)
    {
        Bounds.Add(exclusiveMax);
        return m_draws.Count > 0 ? m_draws.Dequeue() : m_fallback;
    }
}
}
