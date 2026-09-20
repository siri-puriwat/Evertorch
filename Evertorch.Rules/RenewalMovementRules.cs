using System;

namespace Evertorch.Rules
{
public sealed class RenewalMovementRules : IMovementRules
{
    public const float MaxSpeed = 20f;

    public MovementParameters CalculateMovement(MovementContext context)
    {
        if (float.IsNaN(context.BaseSpeed))
        {
            return new MovementParameters(0f);
        }

        return new MovementParameters(Math.Max(0f, Math.Min(MaxSpeed, context.BaseSpeed)));
    }
}
}
