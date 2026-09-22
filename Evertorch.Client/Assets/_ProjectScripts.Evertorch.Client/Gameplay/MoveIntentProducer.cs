using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The only place the client builds a <see cref="MoveIntent" />. Keyboard, gamepad, on-screen stick, and the path
///     follower all end here, so the server cannot tell the control schemes apart.
/// </summary>
public sealed class MoveIntentProducer
{
    public uint LastSequence { get; private set; }

    public MoveIntent Next(uint clientTick, WorldDirection direction)
    {
        // Wrapping is intended: the server compares sequences as a signed distance.
        LastSequence = unchecked(LastSequence + 1);
        WorldDirection normalized = MovementModel.NormalizeOrZero(direction.X, direction.Z);
        return new MoveIntent(LastSequence, clientTick, normalized.X, normalized.Z);
    }
}
}
