namespace Evertorch.Server
{
/// <summary>
///     What came of one roll for an ailment, as <c>evertorch.combat.ailments</c> counts it (System Architecture §10).
/// </summary>
public enum AilmentOutcome
{
    Landed,
    Resisted,

    /// <summary>The same ailment active, a hold while another holds, or a hold from the hit that broke one.</summary>
    Refused,

    /// <summary>A boss, or freeze on the Undead.</summary>
    Immune
}
}
