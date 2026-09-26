using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     The checks every NPC command starts with (Gameplay Systems §6.1): an NPC the session knows on its own map (1),
///     within reach (2). No dialogue state is kept, so each command is checked alone.
/// </summary>
public static class NpcReach
{
    /// <param name="reach">How far, horizontally and centre to centre, the character may stand from the NPC.</param>
    public static CommandRejectionReason Check(
        ClientSession session,
        EntityId npc,
        float reach,
        out NpcEntity? found)
    {
        found = null;
        if (!session.KnownEntities.Contains(npc) || session.Map == null || !session.Map.TryGetNpc(npc, out found))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        WorldPosition self = session.Player!.Position;
        WorldPosition at = found!.Position;
        float dx = self.X - at.X;
        float dz = self.Z - at.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz) > reach
            ? CommandRejectionReason.OutOfRange
            : CommandRejectionReason.None;
    }
}
}
