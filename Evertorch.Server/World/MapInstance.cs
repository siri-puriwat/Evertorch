using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
/// One running copy of a map: its definition, its players, and their interest cells.
/// </summary>
public sealed class MapInstance
{
    private readonly Dictionary<EntityId, PlayerEntity> m_players = new Dictionary<EntityId, PlayerEntity>();

    public MapInstance(MapDefinition definition, uint instanceNumber, InterestGrid interest)
    {
        Definition = definition;
        InstanceNumber = instanceNumber;
        Interest = interest;
    }

    public MapDefinition Definition { get; }

    public uint InstanceNumber { get; }

    public InterestGrid Interest { get; }

    public IReadOnlyCollection<PlayerEntity> Players => m_players.Values;

    public void Add(PlayerEntity player)
    {
        m_players.Add(player.Id, player);
        Interest.Update(player);
    }

    public bool Remove(PlayerEntity player)
    {
        Interest.Remove(player);
        return m_players.Remove(player.Id);
    }

    public bool Contains(EntityId entity)
    {
        return m_players.ContainsKey(entity);
    }
}
}
