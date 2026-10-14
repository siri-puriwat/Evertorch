using System;

namespace Evertorch.Protocol
{
[Flags]
public enum EntityStateFlags : ushort
{
    None = 0,
    Moving = 1,

    /// <summary>The entity's HP reached 0; its body stays until it is removed or revived.</summary>
    Dead = 2,

    /// <summary>
    ///     The ailments everyone who sees a body sees (Gameplay Systems §9.1); their timers stay with the owner.
    /// </summary>
    Poisoned = 4,

    Stunned = 8,
    Frozen = 16,
    Asleep = 32,
    Silenced = 64,
    Blinded = 128
}
}
