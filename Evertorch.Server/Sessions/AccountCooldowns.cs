using System.Collections.Generic;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     Accounts disconnected for violations, each refused until its re-admission cooldown is over (Network Protocol
///     §11). Every cooldown has the same length, so they end in the order they started and expired ones are forgotten
///     from the front. Tick thread only.
/// </summary>
public sealed class AccountCooldowns
{
    private readonly Dictionary<AccountId, uint> m_endsAt = new();
    private readonly Queue<KeyValuePair<AccountId, uint>> m_started = new();
    private readonly uint m_ticks;

    public AccountCooldowns(uint ticks)
    {
        m_ticks = ticks;
    }

    public int Count => m_endsAt.Count;

    public void Start(AccountId account, uint tick)
    {
        Forget(tick);
        if (m_ticks == 0)
        {
            return;
        }

        uint endsAt = tick + m_ticks;
        m_endsAt[account] = endsAt;
        m_started.Enqueue(new KeyValuePair<AccountId, uint>(account, endsAt));
    }

    public bool IsCoolingDown(AccountId account, uint tick)
    {
        Forget(tick);
        return m_endsAt.ContainsKey(account);
    }

    private void Forget(uint tick)
    {
        while (m_started.Count > 0 && unchecked((int)(tick - m_started.Peek().Value)) >= 0)
        {
            KeyValuePair<AccountId, uint> started = m_started.Dequeue();

            // A later cooldown of the same account replaced this one.
            if (m_endsAt.TryGetValue(started.Key, out uint endsAt) && endsAt == started.Value)
            {
                m_endsAt.Remove(started.Key);
            }
        }
    }
}
}
