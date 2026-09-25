using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Where the local skill use sends its request; the server checks the skill, its cost, and its target, and decides.
/// </summary>
public interface ISkillCommandSink
{
    /// <summary>
    ///     Returns the command sequence the request carried; 0 when nothing was sent.
    /// </summary>
    uint SendUseSkill(SkillDefinitionId skill, EntityId target);
}
}
