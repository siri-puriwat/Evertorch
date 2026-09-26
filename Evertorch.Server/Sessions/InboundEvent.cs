using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One already-decoded thing that happened on a connection, carried from a network thread to the tick thread.
/// </summary>
public readonly struct InboundEvent
{
    private InboundEvent(
        InboundEventKind kind,
        ConnectionId connection,
        ClientHello? hello,
        EnterWorldRequest enterWorld,
        MoveIntent intent,
        EntityId target = default,
        uint commandSequence = 0,
        string? name = null,
        int count = 0,
        SkillDefinitionId skill = default,
        byte mapEpoch = 0,
        long inventoryItem = 0,
        EquipmentSlot slot = EquipmentSlot.None)
    {
        Kind = kind;
        Connection = connection;
        Hello = hello;
        EnterWorld = enterWorld;
        Intent = intent;
        Target = target;
        CommandSequence = commandSequence;
        Name = name;
        Count = count;
        Skill = skill;
        MapEpoch = mapEpoch;
        InventoryItem = inventoryItem;
        Slot = slot;
    }

    public InboundEventKind Kind { get; }

    public ConnectionId Connection { get; }

    public ClientHello? Hello { get; }

    public EnterWorldRequest EnterWorld { get; }

    public MoveIntent Intent { get; }

    public EntityId Target { get; }

    public uint CommandSequence { get; }

    public string? Name { get; }

    /// <summary>
    ///     For <see cref="InboundEventKind.InputDropped" />, how many inputs were dropped.
    /// </summary>
    public int Count { get; }

    /// <summary>
    ///     For <see cref="InboundEventKind.UseSkill" />, the skill asked for.
    /// </summary>
    public SkillDefinitionId Skill { get; }

    /// <summary>
    ///     For <see cref="InboundEventKind.Move" />, the map epoch the input was made for.
    /// </summary>
    public byte MapEpoch { get; }

    /// <summary>
    ///     For <see cref="InboundEventKind.Equip" />, the inventory row to wear; for <see cref="InboundEventKind.UseItem" />,
    ///     the row to use.
    /// </summary>
    public long InventoryItem { get; }

    /// <summary>
    ///     For <see cref="InboundEventKind.Unequip" />, the slot to empty.
    /// </summary>
    public EquipmentSlot Slot { get; }

    public static InboundEvent Connected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Connected, connection, null, default, default);
    }

    public static InboundEvent Disconnected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Disconnected, connection, null, default, default);
    }

    public static InboundEvent Malformed(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Malformed, connection, null, default, default);
    }

    public static InboundEvent RateLimited(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.RateLimited, connection, null, default, default);
    }

    public static InboundEvent InputDropped(ConnectionId connection, int count)
    {
        return new InboundEvent(InboundEventKind.InputDropped, connection, null, default, default, count: count);
    }

    public static InboundEvent ForHello(ConnectionId connection, ClientHello hello)
    {
        return new InboundEvent(InboundEventKind.Hello, connection, hello, default, default);
    }

    public static InboundEvent ForEnterWorld(ConnectionId connection, EnterWorldRequest request)
    {
        return new InboundEvent(InboundEventKind.EnterWorld, connection, null, request, default);
    }

    public static InboundEvent ForMove(ConnectionId connection, MoveIntent intent, byte mapEpoch)
    {
        return new InboundEvent(InboundEventKind.Move, connection, null, default, intent, mapEpoch: mapEpoch);
    }

    public static InboundEvent ForTarget(ConnectionId connection, EntityId target)
    {
        return new InboundEvent(InboundEventKind.Target, connection, null, default, default, target);
    }

    public static InboundEvent ForAttack(ConnectionId connection, EntityId target, uint commandSequence)
    {
        return new InboundEvent(InboundEventKind.Attack, connection, null, default, default, target, commandSequence);
    }

    public static InboundEvent ForUseSkill(
        ConnectionId connection,
        SkillDefinitionId skill,
        EntityId target,
        uint commandSequence)
    {
        return new InboundEvent(
            InboundEventKind.UseSkill,
            connection,
            null,
            default,
            default,
            target,
            commandSequence,
            skill: skill);
    }

    public static InboundEvent ForPickup(ConnectionId connection, EntityId drop, uint commandSequence)
    {
        return new InboundEvent(InboundEventKind.Pickup, connection, null, default, default, drop, commandSequence);
    }

    public static InboundEvent ForEquip(ConnectionId connection, long inventoryItem, uint commandSequence)
    {
        return new InboundEvent(
            InboundEventKind.Equip,
            connection,
            null,
            default,
            default,
            commandSequence: commandSequence,
            inventoryItem: inventoryItem);
    }

    public static InboundEvent ForUseItem(ConnectionId connection, long inventoryItem, uint commandSequence)
    {
        return new InboundEvent(
            InboundEventKind.UseItem,
            connection,
            null,
            default,
            default,
            commandSequence: commandSequence,
            inventoryItem: inventoryItem);
    }

    public static InboundEvent ForUnequip(ConnectionId connection, EquipmentSlot slot, uint commandSequence)
    {
        return new InboundEvent(
            InboundEventKind.Unequip,
            connection,
            null,
            default,
            default,
            commandSequence: commandSequence,
            slot: slot);
    }

    public static InboundEvent ForCreateCharacter(ConnectionId connection, string name)
    {
        return new InboundEvent(InboundEventKind.CreateCharacter, connection, null, default, default, name: name);
    }

    public static InboundEvent ForInventoryResync(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.InventoryResync, connection, null, default, default);
    }

    public static InboundEvent ForCommand(InboundEventKind kind, ConnectionId connection, uint commandSequence)
    {
        return new InboundEvent(kind, connection, null, default, default, default, commandSequence);
    }
}
}
