using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
///     Splits a list of rows into the consecutive parts of a message that carry it (Network Protocol §6).
/// </summary>
internal static class WireParts
{
    /// <summary>
    ///     Fills each part in order until the next row would pass the part's rows or
    ///     <see cref="ProtocolLimits.MaxReliableMessageBytes" />; an empty list is one empty part.
    /// </summary>
    public static List<List<T>> Pack<T>(
        IReadOnlyList<T> entries,
        int headerLength,
        int maxEntries,
        Func<T, int> lengthOf)
    {
        var parts = new List<List<T>> { new() };
        int length = headerLength;
        foreach (T entry in entries)
        {
            int entryLength = lengthOf(entry);
            if (parts[parts.Count - 1].Count == maxEntries
                || length + entryLength > ProtocolLimits.MaxReliableMessageBytes)
            {
                parts.Add(new List<T>());
                length = headerLength;
            }

            parts[parts.Count - 1].Add(entry);
            length += entryLength;
        }

        if (parts.Count > byte.MaxValue)
        {
            throw new ArgumentException("A list this large cannot be numbered in parts.", nameof(entries));
        }

        return parts;
    }
}
}
