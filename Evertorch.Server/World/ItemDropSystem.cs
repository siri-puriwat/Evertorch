using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Rolls a dead monster's drop table and places the drops around its body, then removes each drop when its
///     lifetime ends (Gameplay Systems §10, drops research note). Only the server rolls, from the seeded random
///     source, so a supplied seed replays the same drops.
/// </summary>
public sealed class ItemDropSystem : ITickPhase
{
    /// <summary>
    ///     A roll draws one of this many values; a drop happens when the draw is below chance × this.
    /// </summary>
    public const int RollScale = 1_000_000;

    private const int MillisecondsPerSecond = 1000;

    // South-east, west, north, then around again; north is +Z. Relative to the body, one cell away.
    private static readonly int[] OffsetX = { 1, -1, 0 };
    private static readonly int[] OffsetZ = { -1, 0, 1 };

    private readonly WorldSimulation m_world;
    private readonly IRandomSource m_random;
    private readonly int m_tickRate;
    private readonly int m_lifetimeMs;
    private readonly List<ItemDropEntity> m_expired = new();

    public ItemDropSystem(
        WorldSimulation world,
        IRandomSource random,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_random = random;
        m_tickRate = simulation.Value.TickRate;
        m_lifetimeMs = worldOptions.Value.ItemDropLifetimeMs;
    }

    public TickPhase Phase => TickPhase.MonsterAi;

    public void Execute(in TickContext context)
    {
        long now = ToMilliseconds(context.Tick);
        foreach (MapInstance map in m_world.Maps)
        {
            m_expired.Clear();
            foreach (ItemDropEntity drop in map.ItemDrops)
            {
                // A reserved drop waits for its pickup's commit; if the pickup fails, it expires on the next pass.
                if (now >= drop.ExpiresAtMs && !drop.IsReserved)
                {
                    m_expired.Add(drop);
                }
            }

            foreach (ItemDropEntity drop in m_expired)
            {
                map.Remove(drop);
            }
        }
    }

    public static bool IsDropped(int draw, double chance)
    {
        return draw < chance * RollScale;
    }

    /// <summary>
    ///     Where the <paramref name="index" />th drop of one death lies: the first on the body, the rest one cell
    ///     around it, or on the body where an agent could not stand.
    /// </summary>
    public static WorldPosition Place(NavigationGrid grid, WorldPosition body, int index)
    {
        if (index == 0)
        {
            return body;
        }

        int offset = (index - 1) % OffsetX.Length;
        float x = body.X + OffsetX[offset] * grid.CellSize;
        float z = body.Z + OffsetZ[offset] * grid.CellSize;
        return grid.CanOccupy(x, z) && grid.TrySampleHeight(x, z, out float height)
            ? new WorldPosition(x, height, z)
            : body;
    }

    /// <summary>
    ///     Rolls every entry of the monster's drop table in order: its chance, then its amount when it drops. The
    ///     drops belong first to <paramref name="killer" />, or to nobody when no character killed the monster.
    /// </summary>
    public void DropLoot(MapInstance map, MonsterEntity monster, CharacterId killer, uint tick)
    {
        long expiresAtMs = ToMilliseconds(tick) + m_lifetimeMs;
        int placed = 0;
        foreach (MonsterDrop entry in monster.Definition.Drops)
        {
            if (!IsDropped(m_random.Next(RollScale), entry.Chance))
            {
                continue;
            }

            int amount = entry.MinAmount + m_random.Next(entry.MaxAmount - entry.MinAmount + 1);
            WorldPosition position = Place(map.Definition.Navigation, monster.Position, placed);
            m_world.SpawnItemDrop(map, entry.Item, (uint)amount, position, tick, expiresAtMs, killer);
            placed++;
        }
    }

    private long ToMilliseconds(uint tick)
    {
        return (long)(tick - 1) * MillisecondsPerSecond / m_tickRate;
    }
}
}
