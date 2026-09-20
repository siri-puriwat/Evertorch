namespace Evertorch.Rules
{
public interface IMovementRules
{
    MovementParameters CalculateMovement(MovementContext context);
}
}
