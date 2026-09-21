using Evertorch.Game;

namespace Evertorch.Client
{
public interface IMapProvider
{
    bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid);
}
}
