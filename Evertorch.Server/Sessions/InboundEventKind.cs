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
    Sell = 21,

    /// <summary>
    ///     A request to accept <see cref="InboundEvent.Quest" /> from the NPC <see cref="InboundEvent.Target" />,
    ///     carrying a command sequence.
    /// </summary>
    AcceptQuest = 22,

    /// <summary>
    ///     A request to turn in <see cref="InboundEvent.Quest" /> to the NPC <see cref="InboundEvent.Target" />,
    ///     carrying a command sequence.
    /// </summary>
    CompleteQuest = 23,

    /// <summary>
    ///     A request to raise <see cref="InboundEvent.Stat" /> by <see cref="InboundEvent.Quantity" /> steps with stat
    ///     points, carrying a command sequence.
    /// </summary>
    AllocateStat = 24,

    /// <summary>
    ///     A request to learn one level of <see cref="InboundEvent.Skill" /> with a skill point, carrying a command
    ///     sequence.
    /// </summary>
    LearnSkill = 25,

    /// <summary>
    ///     A request to the NPC <see cref="InboundEvent.Target" /> for the reset of the character's build, carrying a
    ///     command sequence.
    /// </summary>
    ResetBuild = 26,

    /// <summary>
    ///     A request to the NPC <see cref="InboundEvent.Target" /> to change the character's job to
    ///     <see cref="InboundEvent.Job" />, carrying a command sequence.
    /// </summary>
    ChangeJob = 27,

    /// <summary>
    ///     A chat line on <see cref="InboundEvent.Channel" />, to <see cref="InboundEvent.Name" /> for a whisper, with
    ///     the words in <see cref="InboundEvent.Text" />, carrying a command sequence.
    /// </summary>
    Chat = 28,

    /// <summary>
    ///     An invite of the character named <see cref="InboundEvent.Name" /> to the sender's party, carrying a command
    ///     sequence.
    /// </summary>
    PartyInvite = 29,

    /// <summary>
    ///     The answer, <see cref="InboundEvent.IsAccepted" />, to the invite of the character named
    ///     <see cref="InboundEvent.Name" />, carrying a command sequence.
    /// </summary>
    PartyReply = 30,

    /// <summary>
    ///     A departure from the sender's party, carrying a command sequence.
    /// </summary>
    PartyLeave = 31,

    /// <summary>
    ///     The leader's removal of the member named <see cref="InboundEvent.Name" />, carrying a command sequence.
    /// </summary>
    PartyKick = 32,

    /// <summary>
    ///     The leader's passing of the lead to the member named <see cref="InboundEvent.Name" />, carrying a command
    ///     sequence.
    /// </summary>
    PartyLead = 33,

    /// <summary>
    ///     A request to trade with the character named <see cref="InboundEvent.Name" />, carrying a command sequence.
    /// </summary>
    TradeRequest = 34,

    /// <summary>
    ///     The answer, <see cref="InboundEvent.IsAccepted" />, to the request of the character named
    ///     <see cref="InboundEvent.Name" />, carrying a command sequence.
    /// </summary>
    TradeReply = 35,

    /// <summary>
    ///     How much of row <see cref="InboundEvent.InventoryItem" />, or of the coins for row 0, the open trade offers:
    ///     <see cref="InboundEvent.Quantity" />, carrying a command sequence.
    /// </summary>
    TradeOffer = 36,

    /// <summary>
    ///     The sender's lock of its own offer, carrying a command sequence.
    /// </summary>
    TradeLock = 37,

    /// <summary>
    ///     The sender's confirm of the trade, carrying a command sequence.
    /// </summary>
    TradeConfirm = 38,

    /// <summary>
    ///     The end of the sender's open trade or request, carrying a command sequence.
    /// </summary>
    TradeCancel = 39,

    /// <summary>
    ///     A read of the account's storage at the Storekeeper <see cref="InboundEvent.Target" />, carrying a command
    ///     sequence.
    /// </summary>
    StorageOpen = 40,

    /// <summary>
    ///     <see cref="InboundEvent.Quantity" /> of the bag's row <see cref="InboundEvent.InventoryItem" /> into storage at
    ///     the Storekeeper <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    StorageDeposit = 41,

    /// <summary>
    ///     <see cref="InboundEvent.Quantity" /> of the storage's row <see cref="InboundEvent.InventoryItem" /> into the bag
    ///     at the Storekeeper <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    StorageWithdraw = 42
}
}
