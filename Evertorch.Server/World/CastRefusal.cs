namespace Evertorch.Server
{
/// <summary>
///     Why a cast did not begin (Gameplay Systems §9); the command that asked for it is refused for this reason.
/// </summary>
public enum CastRefusal
{
    None,

    /// <summary>
    ///     Dead, a skill the character does not know, already acting, in an after-cast delay, or on cooldown.
    /// </summary>
    NotAllowedNow,

    NotEnoughSp,

    /// <summary>
    ///     An enemy skill without a live monster the caster may target, or a skill on itself naming another entity.
    /// </summary>
    InvalidTarget,

    /// <summary>
    ///     Beyond the skill's range and the tolerance, or out of sight.
    /// </summary>
    OutOfRange
}
}
