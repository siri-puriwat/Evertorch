using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One running copy of a map: its definition, its entities, and their interest cells.
/// </summary>
public sealed class MapInstance
{
    private readonly Dictionary<EntityId, WorldEntity> m_entities = new();
    private readonly Dictionary<EntityId, PlayerEntity> m_players = new();
    private readonly Dictionary<EntityId, MonsterEntity> m_monsters = new();
    private readonly Dictionary<EntityId, ItemDropEntity> m_itemDrops = new();

    public MapInstance(MapDefinition definition, uint instanceNumber, InterestGrid interest)
    {
        Definition = definition;
        InstanceNumber = instanceNumber;
        Interest = interest;
    }

    public MapDefinition Definition { get; }

    public uint InstanceNumber { get; }

    public InterestGrid Interest { get; }

    public IReadOnlyCollection<WorldEntity> Entities => m_entities.Values;

    public IReadOnlyCollection<PlayerEntity> Players => m_players.Values;

    public IReadOnlyCollection<MonsterEntity> Monsters => m_monsters.Values;

    public IReadOnlyCollection<ItemDropEntity> ItemDrops => m_itemDrops.Values;

    public void Add(WorldEntity entity)
    {
        m_entities.Add(entity.Id, entity);
        switch (entity)
        {
            case PlayerEntity player:
                m_players.Add(player.Id, player);
                break;
            case MonsterEntity monster:
                m_monsters.Add(monster.Id, monster);
                break;
            case ItemDropEntity drop:
                m_itemDrops.Add(drop.Id, drop);
                break;
        }

        Interest.Update(entity);
    }

    public bool Remove(WorldEntity entity)
    {
        Interest.Remove(entity);
        m_players.Remove(entity.Id);
        m_monsters.Remove(entity.Id);
        m_itemDrops.Remove(entity.Id);
        return m_entities.Remove(entity.Id);
    }

    public bool TryGetEntity(EntityId entity, out WorldEntity? found)
    {
        return m_entities.TryGetValue(entity, out found);
    }

    public bool TryGetPlayer(EntityId entity, out PlayerEntity? player)
    {
        return m_players.TryGetValue(entity, out player);
    }

    public bool TryGetMonster(EntityId entity, out MonsterEntity? monster)
    {
        return m_monsters.TryGetValue(entity, out monster);
    }

    public bool Contains(EntityId entity)
    {
        return m_entities.ContainsKey(entity);
    }
}
}
