using Evertorch.Game;

namespace Evertorch.Client
{
public interface IMoveIntentSink
{
    void Send(MoveIntent intent);
}
}
