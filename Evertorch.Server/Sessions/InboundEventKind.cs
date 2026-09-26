namespace Evertorch.Server
{
public enum InboundEventKind
{
    Connected = 0,
    Disconnected = 1,
    Hello = 2,
    EnterWorld = 3,

    /// <summary>
    ///     A movement intent. A request to stop is the same event with a zero direction.
    /// </summary>
    Move = 5,

    /// <summary>
    ///     A request to select a target; entity 0 clears it.
    /// </summary>
    Target = 6,

    /// <summary>
    ///     A request to attack <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    Attack = 7,

    /// <summary>
    ///     A request to end auto-attack, carrying a command sequence.
    /// </summary>
    Cancel = 8,

    /// <summary>
    ///     A request to revive a dead character, carrying a command sequence.
    /// </summary>
    Respawn = 9,

    /// <summary>
    ///     A request to create a character named <see cref="InboundEvent.Name" /> on the signed-in account.
    /// </summary>
    CreateCharacter = 10,

    /// <summary>
    ///     A request to leave the world for character selection, carrying a command sequence.
    /// </summary>
    Logout = 11,

    /// <summary>
    ///     A request for a whole inventory after the client saw a revision it could not apply.
    /// </summary>
    InventoryResync = 12,

    /// <summary>
    ///     A request to pick up the drop <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    Pickup = 13,

    /// <summary>
    ///     The peer sent something that is not a well-formed client message on its proper channel.
    /// </summary>
    Malformed = 4,

    /// <summary>
    ///     The network thread found the peer over its message budget on a reliable channel, or the heaviest peer of a
    ///     full queue; the connection is to be closed with <c>RateLimited</c>. Never dropped, like connect and
    ///     disconnect.
    /// </summary>
    RateLimited = 14,

    /// <summary>
    ///     The network thread dropped <see cref="InboundEvent.Count" /> of the peer's inputs over its message budget, for
    ///     the violation score. A peer has at most one waiting, and it is never dropped.
    /// </summary>
    InputDropped = 15,

    /// <summary>
    ///     A request to cast <see cref="InboundEvent.Skill" /> at <see cref="InboundEvent.Target" />, 0 for the caster,
    ///     carrying a command sequence.
    /// </summary>
    UseSkill = 16,

    /// <summary>
    ///     A request to wear the inventory row <see cref="InboundEvent.InventoryItem" />, carrying a command sequence.
    /// </summary>
    Equip = 17,

    /// <summary>
    ///     A request to empty the equipment slot <see cref="InboundEvent.Slot" />, carrying a command sequence.
    /// </summary>
    Unequip = 18,

    /// <summary>
    ///     A request to use one unit of the inventory row <see cref="InboundEvent.InventoryItem" />, carrying a command
    ///     sequence.
    /// </summary>
    UseItem = 19,

    /// <summary>
    ///     A request to buy <see cref="InboundEvent.Quantity" /> of <see cref="InboundEvent.Item" /> from the NPC
    ///     <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    Buy = 20,

    /// <summary>
    ///     A request to sell <see cref="InboundEvent.Quantity" /> of the inventory row
    ///     <see cref="InboundEvent.InventoryItem" /> to the NPC <see cref="InboundEvent.Target" />, carrying a command
    ///     sequence.
    /// </summary>
    Sell = 21
}
}
