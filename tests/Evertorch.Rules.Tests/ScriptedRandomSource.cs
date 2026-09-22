using System;
using System.Collections.Generic;

namespace Evertorch.Rules.Tests
{
/// <summary>
///     Replays a fixed list of draws and records the bound each one was asked for.
/// </summary>
internal sealed class ScriptedRandomSource : IRandomSource
{
    private readonly Queue<int> m_values;

    public ScriptedRandomSource(params int[] values)
    {
        m_values = new Queue<int>(values);
    }

    public List<int> RequestedBounds { get; } = new();

    public int Next(int exclusiveMax)
    {
        RequestedBounds.Add(exclusiveMax);
        if (m_values.Count == 0)
        {
            throw new InvalidOperationException("The rule drew more random numbers than the test scripted.");
        }

        int value = m_values.Dequeue();
        if (value < 0 || value >= exclusiveMax)
        {
            throw new InvalidOperationException("Scripted value " + value + " is outside the requested bound.");
        }

        return value;
    }
}
}
