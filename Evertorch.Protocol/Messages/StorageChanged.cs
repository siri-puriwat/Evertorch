using System;

namespace Evertorch.Protocol
{
/// <summary>
///     One committed change of the account's storage (Network Protocol §9): from <see cref="PriorRevision" /> to
///     <see cref="NewRevision" />, the row as it now is, 0 when it was emptied. A client that sees another prior
///     revision reads the storage again.
/// </summary>
public sealed class StorageChanged
{
    public StorageChanged(uint priorRevision, uint newRevision, StorageEntry row)
    {
        PriorRevision = priorRevision;
        NewRevision = newRevision;
        Row = row;
    }

    public uint PriorRevision { get; }

    public uint NewRevision { get; }

    public StorageEntry Row { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out StorageChanged? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StorageChanged)
            || !reader.TryReadUInt32(out uint prior)
            || !reader.TryReadUInt32(out uint next)
            || !StorageEntry.TryRead(ref reader, true, out StorageEntry row)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new StorageChanged(prior, next, row);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + 2 * sizeof(uint) + Row.GetEncodedLength();
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StorageChanged);
        writer.WriteUInt32(PriorRevision);
        writer.WriteUInt32(NewRevision);
        Row.Write(ref writer);
        return writer.Position;
    }
}
}
