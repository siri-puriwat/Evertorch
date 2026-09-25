namespace Evertorch.Rules
{
/// <summary>
///     Natural regeneration while alive (Gameplay Systems §2.1): how much HP and SP return, and how often.
/// </summary>
public readonly struct Regeneration
{
    public Regeneration(int health, int healthIntervalMs, int spirit, int spiritIntervalMs)
    {
        Health = health;
        HealthIntervalMs = healthIntervalMs;
        Spirit = spirit;
        SpiritIntervalMs = spiritIntervalMs;
    }

    public int Health { get; }

    public int HealthIntervalMs { get; }

    public int Spirit { get; }

    public int SpiritIntervalMs { get; }
}
}
