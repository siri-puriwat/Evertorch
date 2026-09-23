using System;

namespace Evertorch.Protocol
{
[Flags]
public enum EntityStateFlags : ushort
{
    None = 0,
    Moving = 1,

    /// <summary>The entity's HP reached 0; its body stays until it is removed or revived.</summary>
    Dead = 2
}
}
