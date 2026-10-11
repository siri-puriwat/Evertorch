using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One of a refiner's tables (Network Protocol §6, §9): its ore and how many an attempt takes, what a failure does
///     and by how many levels, each level's chance and coins from +1, and the items that name it. Sent after the NPC's
///     <see cref="NpcServices" /> at each spawn of it, in index order. The chances and coins travel on purpose,
///     because the player sees them before each attempt (Content Pipeline §5).
/// </summary>
public sealed class RefineTable
{
    /// <summary>
    ///     The most levels a table holds, +10 at most (Gameplay Systems §11.6).
    /// </summary>
    public const int MaxLevels = 10;

    /// <summary>
    ///     The most items one table holds, the room of one message (Content Pipeline §4).
    /// </summary>
    public const int MaxItems = 13;

    /// <summary>
    ///     The most levels a downgrade lowers.
    /// </summary>
    public const int MaxLevelsLowered = 9;

    /// <summary>
    ///     A table without an item, with its ore's ID at the 64-byte limit and ten levels.
    /// </summary>
    public const int HeaderLength = sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(ushort)
        + ProtocolLimits.MaxDefinitionIdBytes + 4 * sizeof(byte) + MaxLevels * (sizeof(byte) + sizeof(uint))
        + sizeof(byte);

    /// <summary>
    ///     What each item adds at most: its ID at the 64-byte limit.
    /// </summary>
    public const int ItemLength = sizeof(ushort) + ProtocolLimits.MaxDefinitionIdBytes;

    public RefineTable(
        EntityId npc,
        byte index,
        ItemDefinitionId ore,
        byte oreQuantity,
        RefineFailure onFailure,
        byte levelsLowered,
        IReadOnlyList<RefineTableStep> steps,
        IReadOnlyList<ItemDefinitionId> items)
    {
        if (steps == null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (npc.Value <= 0
            || ore.Value.Length == 0
            || oreQuantity == 0
            || !IsValidFailure(onFailure, levelsLowered)
            || steps.Count == 0
            || steps.Count > MaxLevels
            || items.Count == 0
            || items.Count > MaxItems)
        {
            throw new ArgumentException(
                "A table names its NPC and ore, at least one ore, a failure with its levels lowered, 1 to 10 levels, "
                + "and 1 to 13 items.");
        }

        foreach (RefineTableStep step in steps)
        {
            if (step.Chance == 0 || step.Chance > 100)
            {
                throw new ArgumentException("A level's chance is 1 to 100.", nameof(steps));
            }
        }

        foreach (ItemDefinitionId item in items)
        {
            if (item.Value.Length == 0)
            {
                throw new ArgumentException("An item is named.", nameof(items));
            }
        }

        Npc = npc;
        Index = index;
        Ore = ore;
        OreQuantity = oreQuantity;
        OnFailure = onFailure;
        LevelsLowered = levelsLowered;
        Steps = steps;
        Items = items;
    }

    public EntityId Npc { get; }

    /// <summary>
    ///     The table's place among the NPC's tables, from 0.
    /// </summary>
    public byte Index { get; }

    public ItemDefinitionId Ore { get; }

    public byte OreQuantity { get; }

    public RefineFailure OnFailure { get; }

    /// <summary>
    ///     How many levels a failure lowers an item, for a downgrade; 0 otherwise.
    /// </summary>
    public byte LevelsLowered { get; }

    /// <summary>
    ///     The attempt that reaches each level, from +1.
    /// </summary>
    public IReadOnlyList<RefineTableStep> Steps { get; }

    /// <summary>
    ///     The items that name the table.
    /// </summary>
    public IReadOnlyList<ItemDefinitionId> Items { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out RefineTable? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.RefineTable)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadByte(out byte index)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string oreText)
            || !reader.TryReadByte(out byte oreQuantity)
            || !reader.TryReadByte(out byte failure)
            || !reader.TryReadByte(out byte levelsLowered)
            || !reader.TryReadByte(out byte levelCount)
            || npc <= 0
            || oreQuantity == 0
            || !IsValidFailure((RefineFailure)failure, levelsLowered)
            || levelCount == 0
            || levelCount > MaxLevels
            || !ItemDefinitionId.TryCreate(oreText, out ItemDefinitionId ore))
        {
            return false;
        }

        var steps = new RefineTableStep[levelCount];
        for (int level = 0; level < levelCount; level++)
        {
            if (!reader.TryReadByte(out byte chance)
                || !reader.TryReadUInt32(out uint coins)
                || chance == 0
                || chance > 100)
            {
                return false;
            }

            steps[level] = new RefineTableStep(chance, coins);
        }

        if (!reader.TryReadByte(out byte itemCount) || itemCount == 0 || itemCount > MaxItems)
        {
            return false;
        }

        var items = new ItemDefinitionId[itemCount];
        for (int item = 0; item < itemCount; item++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
                || !ItemDefinitionId.TryCreate(itemText, out items[item]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new RefineTable(
            new EntityId(npc),
            index,
            ore,
            oreQuantity,
            (RefineFailure)failure,
            levelsLowered,
            steps,
            items);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(long) + sizeof(byte)
            + WireText.GetEncodedLength(Ore.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 4 * sizeof(byte)
            + Steps.Count * (sizeof(byte) + sizeof(uint))
            + sizeof(byte);
        foreach (ItemDefinitionId item in Items)
        {
            length += WireText.GetEncodedLength(item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.RefineTable);
        writer.WriteInt64(Npc.Value);
        writer.WriteByte(Index);
        writer.WriteString(Ore.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteByte(OreQuantity);
        writer.WriteByte((byte)OnFailure);
        writer.WriteByte(LevelsLowered);
        writer.WriteByte((byte)Steps.Count);
        foreach (RefineTableStep step in Steps)
        {
            writer.WriteByte(step.Chance);
            writer.WriteUInt32(step.Coins);
        }

        writer.WriteByte((byte)Items.Count);
        foreach (ItemDefinitionId item in Items)
        {
            writer.WriteString(item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        }

        return writer.Position;
    }

    // A downgrade lowers 1 to 9 levels; destroy and keep lower none.
    private static bool IsValidFailure(RefineFailure failure, byte levelsLowered)
    {
        return failure switch
        {
            RefineFailure.Destroy => levelsLowered == 0,
            RefineFailure.Downgrade => levelsLowered >= 1 && levelsLowered <= MaxLevelsLowered,
            RefineFailure.Keep => levelsLowered == 0,
            _ => false
        };
    }
}
}
