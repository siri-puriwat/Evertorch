namespace Evertorch.Server
{
public interface ITickPhase
{
    TickPhase Phase { get; }

    void Execute(in TickContext context);
}
}
