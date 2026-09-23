using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     Buckets a map's entities into square cells so "who is near whom" costs a few cell lookups rather than a
///     comparison of every pair. A player is interested in its own cell and the cells within the neighbour radius.
/// </summary>
public sealed class InterestGrid
{
    private readonly Dictionary<long, List<WorldEntity>> m_cells = new();
    private readonly Dictionary<EntityId, long> m_cellByEntity = new();
    private readonly float m_cellSize;
    private readonly int m_neighborRadius;

    public InterestGrid(float cellSize, int neighborRadius)
    {
        if (!(cellSize > 0f) || float.IsInfinity(cellSize))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "The interest cell size must be positive.");
        }

        if (neighborRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(neighborRadius), "The neighbour radius cannot be negative.");
        }

        m_cellSize = cellSize;
        m_neighborRadius = neighborRadius;
    }

    /// <summary>
    ///     Adds the entity or moves it to the cell its current position falls in.
    /// </summary>
    public void Update(WorldEntity entity)
    {
        long cell = ToKey(ToCell(entity.Position.X), ToCell(entity.Position.Z));
        if (m_cellByEntity.TryGetValue(entity.Id, out long previous))
        {
            if (previous == cell)
            {
                return;
            }

            RemoveFromCell(entity, previous);
        }

        if (!m_cells.TryGetValue(cell, out List<WorldEntity>? occupants))
        {
            occupants = new List<WorldEntity>();
            m_cells.Add(cell, occupants);
        }

        occupants.Add(entity);
        m_cellByEntity[entity.Id] = cell;
    }

    public void Remove(WorldEntity entity)
    {
        if (m_cellByEntity.TryGetValue(entity.Id, out long cell))
        {
            RemoveFromCell(entity, cell);
            m_cellByEntity.Remove(entity.Id);
        }
    }

    /// <summary>
    ///     Appends every other entity in the observer's area of interest.
    /// </summary>
    public void CollectVisible(WorldEntity observer, List<WorldEntity> visible)
    {
        int centerColumn = ToCell(observer.Position.X);
        int centerRow = ToCell(observer.Position.Z);
        for (int row = centerRow - m_neighborRadius; row <= centerRow + m_neighborRadius; row++)
        {
            for (int column = centerColumn - m_neighborRadius; column <= centerColumn + m_neighborRadius; column++)
            {
                if (!m_cells.TryGetValue(ToKey(column, row), out List<WorldEntity>? occupants))
                {
                    continue;
                }

                foreach (WorldEntity occupant in occupants)
                {
                    if (occupant.Id != observer.Id)
                    {
                        visible.Add(occupant);
                    }
                }
            }
        }
    }

    private static long ToKey(int column, int row)
    {
        return ((long)column << 32) | (uint)row;
    }

    private int ToCell(float coordinate)
    {
        return (int)Math.Floor(coordinate / m_cellSize);
    }

    private void RemoveFromCell(WorldEntity entity, long cell)
    {
        if (!m_cells.TryGetValue(cell, out List<WorldEntity>? occupants))
        {
            return;
        }

        occupants.Remove(entity);
        if (occupants.Count == 0)
        {
            m_cells.Remove(cell);
        }
    }
}
}
