using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
/// A* over the cells of one <see cref="NavigationGrid"/>. Costs are whole numbers and ties break on cell index, so
/// the same request yields the same path on every platform. Not thread-safe: it reuses its working buffers.
/// </summary>
public sealed class GridPathfinder
{
    private const int CardinalCost = 10;
    private const int DiagonalCost = 14;
    private const int DirectionCount = 8;

    private static readonly int[] ColumnSteps = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] RowSteps = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly NavigationGrid m_grid;
    private readonly int[] m_costFromStart;
    private readonly int[] m_cameFrom;
    private readonly int[] m_visitStamp;
    private readonly bool[] m_isClosed;
    private readonly byte[] m_links;
    private readonly bool[] m_hasLinks;
    private readonly List<OpenNode> m_open = new List<OpenNode>();
    private readonly List<WorldPosition> m_cellPath = new List<WorldPosition>();
    private int m_stamp;

    public GridPathfinder(NavigationGrid grid)
    {
        m_grid = grid ?? throw new ArgumentNullException(nameof(grid));
        int cellCount = grid.Columns * grid.Rows;
        m_costFromStart = new int[cellCount];
        m_cameFrom = new int[cellCount];
        m_visitStamp = new int[cellCount];
        m_isClosed = new bool[cellCount];
        m_links = new byte[cellCount];
        m_hasLinks = new bool[cellCount];
    }

    /// <summary>
    /// Finds a walkable route and writes its waypoints, excluding the start and ending exactly on the goal. Returns
    /// false, leaving <paramref name="waypoints"/> empty, when the goal cannot be stood on, cannot be reached, or
    /// the search would expand more than <paramref name="maxExpandedNodes"/> cells.
    /// </summary>
    public bool TryFindPath(
        WorldPosition start,
        WorldPosition goal,
        int maxExpandedNodes,
        List<WorldPosition> waypoints)
    {
        if (waypoints == null)
        {
            throw new ArgumentNullException(nameof(waypoints));
        }

        waypoints.Clear();
        if (!m_grid.TryGetCellIndex(start.X, start.Z, out int startColumn, out int startRow)
            || !m_grid.TryGetCellIndex(goal.X, goal.Z, out int goalColumn, out int goalRow)
            || !m_grid.CanOccupy(start.X, start.Z)
            || !m_grid.CanOccupy(goal.X, goal.Z)
            || !m_grid.TrySampleHeight(goal.X, goal.Z, out float goalHeight))
        {
            return false;
        }

        WorldPosition groundedGoal = new WorldPosition(goal.X, goalHeight, goal.Z);
        int startCell = ToCell(startColumn, startRow);
        int goalCell = ToCell(goalColumn, goalRow);
        if (!Search(startCell, goalCell, maxExpandedNodes))
        {
            return false;
        }

        BuildCellPath(startCell, goalCell, groundedGoal);
        Smooth(start, waypoints);
        return true;
    }

    private bool Search(int startCell, int goalCell, int maxExpandedNodes)
    {
        m_stamp++;
        m_open.Clear();
        Visit(startCell, 0, -1);
        Push(new OpenNode(startCell, Heuristic(startCell, goalCell), 0));

        int expanded = 0;
        while (m_open.Count > 0)
        {
            OpenNode current = Pop();
            if (m_isClosed[current.Cell])
            {
                continue;
            }

            if (current.Cell == goalCell)
            {
                return true;
            }

            expanded++;
            if (expanded > maxExpandedNodes)
            {
                return false;
            }

            m_isClosed[current.Cell] = true;
            ExpandNeighbours(current.Cell, goalCell);
        }

        return false;
    }

    private void ExpandNeighbours(int cell, int goalCell)
    {
        int column = cell % m_grid.Columns;
        int row = cell / m_grid.Columns;
        byte links = LinksOf(cell, column, row);

        for (int direction = 0; direction < DirectionCount; direction++)
        {
            if ((links & (1 << direction)) == 0)
            {
                continue;
            }

            int neighbour = ToCell(column + ColumnSteps[direction], row + RowSteps[direction]);
            if (m_visitStamp[neighbour] == m_stamp && m_isClosed[neighbour])
            {
                continue;
            }

            int stepCost = (direction & 1) == 0 ? CardinalCost : DiagonalCost;
            int cost = m_costFromStart[cell] + stepCost;
            if (m_visitStamp[neighbour] == m_stamp && cost >= m_costFromStart[neighbour])
            {
                continue;
            }

            Visit(neighbour, cost, cell);
            Push(new OpenNode(neighbour, cost + Heuristic(neighbour, goalCell), cost));
        }
    }

    // Asking the grid is the expensive part of a search, and the answer never changes, so it is kept per cell.
    private byte LinksOf(int cell, int column, int row)
    {
        if (m_hasLinks[cell])
        {
            return m_links[cell];
        }

        byte links = 0;
        for (int direction = 0; direction < DirectionCount; direction++)
        {
            if (m_grid.CanTraverse(column, row, column + ColumnSteps[direction], row + RowSteps[direction]))
            {
                links |= (byte)(1 << direction);
            }
        }

        m_links[cell] = links;
        m_hasLinks[cell] = true;
        return links;
    }

    private void Visit(int cell, int cost, int cameFrom)
    {
        m_visitStamp[cell] = m_stamp;
        m_isClosed[cell] = false;
        m_costFromStart[cell] = cost;
        m_cameFrom[cell] = cameFrom;
    }

    private void BuildCellPath(int startCell, int goalCell, WorldPosition goal)
    {
        m_cellPath.Clear();
        m_cellPath.Add(goal);
        for (int cell = m_cameFrom[goalCell]; cell >= 0 && cell != startCell; cell = m_cameFrom[cell])
        {
            m_cellPath.Add(m_grid.GetCellCenter(cell % m_grid.Columns, cell / m_grid.Columns));
        }

        m_cellPath.Reverse();
    }

    // Greedy string pulling: from each anchor, jump to the farthest waypoint that can be walked to in a straight
    // line. When even the next one cannot, it is kept anyway and the mover slides around the corner.
    private void Smooth(WorldPosition start, List<WorldPosition> waypoints)
    {
        WorldPosition anchor = start;
        int next = 0;
        while (next < m_cellPath.Count)
        {
            int farthest = next;
            for (int candidate = m_cellPath.Count - 1; candidate > next; candidate--)
            {
                if (m_grid.HasLineOfSight(anchor, m_cellPath[candidate]))
                {
                    farthest = candidate;
                    break;
                }
            }

            anchor = m_cellPath[farthest];
            waypoints.Add(anchor);
            next = farthest + 1;
        }
    }

    private int Heuristic(int cell, int goalCell)
    {
        int deltaColumn = Math.Abs((cell % m_grid.Columns) - (goalCell % m_grid.Columns));
        int deltaRow = Math.Abs((cell / m_grid.Columns) - (goalCell / m_grid.Columns));
        int longer = Math.Max(deltaColumn, deltaRow);
        int shorter = Math.Min(deltaColumn, deltaRow);
        return (CardinalCost * longer) + ((DiagonalCost - CardinalCost) * shorter);
    }

    private int ToCell(int column, int row)
    {
        return (row * m_grid.Columns) + column;
    }

    private void Push(OpenNode node)
    {
        m_open.Add(node);
        int child = m_open.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (!m_open[child].IsBefore(m_open[parent]))
            {
                break;
            }

            Swap(child, parent);
            child = parent;
        }
    }

    private OpenNode Pop()
    {
        OpenNode top = m_open[0];
        int last = m_open.Count - 1;
        m_open[0] = m_open[last];
        m_open.RemoveAt(last);

        int parent = 0;
        while (true)
        {
            int left = (parent * 2) + 1;
            int right = left + 1;
            int smallest = parent;
            if (left < m_open.Count && m_open[left].IsBefore(m_open[smallest]))
            {
                smallest = left;
            }

            if (right < m_open.Count && m_open[right].IsBefore(m_open[smallest]))
            {
                smallest = right;
            }

            if (smallest == parent)
            {
                return top;
            }

            Swap(parent, smallest);
            parent = smallest;
        }
    }

    private void Swap(int first, int second)
    {
        OpenNode held = m_open[first];
        m_open[first] = m_open[second];
        m_open[second] = held;
    }

    private readonly struct OpenNode
    {
        public OpenNode(int cell, int estimatedTotal, int costFromStart)
        {
            Cell = cell;
            EstimatedTotal = estimatedTotal;
            CostFromStart = costFromStart;
        }

        public int Cell { get; }

        public int EstimatedTotal { get; }

        public int CostFromStart { get; }

        // Lower estimate first, then the node that is already farther along, then the lower cell index. The last
        // rule makes the order total, which is what keeps results identical everywhere.
        public bool IsBefore(OpenNode other)
        {
            if (EstimatedTotal != other.EstimatedTotal)
            {
                return EstimatedTotal < other.EstimatedTotal;
            }

            if (CostFromStart != other.CostFromStart)
            {
                return CostFromStart > other.CostFromStart;
            }

            return Cell < other.Cell;
        }
    }
}
}
