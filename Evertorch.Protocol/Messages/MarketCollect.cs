using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Collects the character's listing <see cref="Listing" /> at the Broker <see cref="Npc" />: its sales less the tax
///     and its unsold units, ending it (Gameplay Systems §11.5). Answered after the commit.
/// </summary>
public sealed class MarketCollect
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(uint);

    public MarketCollect(EntityId npc, long listing, uint commandSequence)
    {
        if (listing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listing), listing, "A listing's ID is at least 1.");
        }

        Npc = npc;
        Listing = listing;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public long Listing { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a listing's ID below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out MarketCollect? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketCollect)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadInt64(out long listing)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || listing <= 0)
        {
            return false;
        }

        message = new MarketCollect(new EntityId(npc), listing, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketCollect);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(Listing);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
