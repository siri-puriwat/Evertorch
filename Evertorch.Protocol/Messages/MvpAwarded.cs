using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver was a boss's most valuable player (Network Protocol §9), sent once its prize settled: the boss, the
///     MVP experience the award gained, and the prize with its amount and where it went, or no prize.
/// </summary>
public sealed class MvpAwarded
{
    public MvpAwarded(
        MonsterDefinitionId monster,
        ulong mvpExperience,
        ItemDefinitionId? item,
        uint amount,
        PrizePlacement placed)
    {
        if (monster == default)
        {
            throw new ArgumentException("A monster is required.", nameof(monster));
        }

        if (!IsConsistent(item != null, amount, placed))
        {
            throw new ArgumentException("A prize has an amount and a place, and no prize has neither.", nameof(item));
        }

        Monster = monster;
        MvpExperience = mvpExperience;
        Item = item;
        Amount = amount;
        Placed = placed;
    }

    public MonsterDefinitionId Monster { get; }

    /// <summary>
    ///     What the boss's MVP experience added, which the base level's cap may cut to 0: server-only content, sent on
    ///     purpose to this player alone (Content Pipeline §5).
    /// </summary>
    public ulong MvpExperience { get; }

    /// <summary>
    ///     The prize, or null when the boss gave none.
    /// </summary>
    public ItemDefinitionId? Item { get; }

    public uint Amount { get; }

    public PrizePlacement Placed { get; }

    private string ItemText => Item?.Value ?? string.Empty;

    public static bool TryRead(ReadOnlySpan<byte> source, out MvpAwarded? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MvpAwarded)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string monsterText)
            || !reader.TryReadUInt64(out ulong mvpExperience)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint amount)
            || !reader.TryReadByte(out byte placedValue)
            || !reader.IsAtEnd
            || !MonsterDefinitionId.TryCreate(monsterText, out MonsterDefinitionId monster))
        {
            return false;
        }

        ItemDefinitionId? item = null;
        if (itemText.Length > 0)
        {
            if (!ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId named))
            {
                return false;
            }

            item = named;
        }

        var placed = (PrizePlacement)placedValue;
        if (!WireEnums.IsDefined(placed) || !IsConsistent(item != null, amount, placed))
        {
            return false;
        }

        message = new MvpAwarded(monster, mvpExperience, item, amount, placed);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Monster.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(ulong)
            + WireText.GetEncodedLength(ItemText, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + sizeof(byte);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MvpAwarded);
        writer.WriteString(Monster.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt64(MvpExperience);
        writer.WriteString(ItemText, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Amount);
        writer.WriteByte((byte)Placed);
        return writer.Position;
    }

    // The item, its amount, and its place agree on whether there is a prize (Network Protocol §6).
    private static bool IsConsistent(bool hasItem, uint amount, PrizePlacement placed)
    {
        return hasItem == amount > 0 && hasItem == (placed != PrizePlacement.None);
    }
}
}
