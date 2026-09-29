using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The choice of a skill's target after its press, as in the reference game (Prototype Content §4): a skill for an
///     enemy or an ally waits here until a click or tap names what it is for, and a skill on the caster never waits.
///     A monster is an enemy skill's target; a player, or the caster itself, an ally skill's.
/// </summary>
public sealed class SkillTargeting
{
    public bool IsChoosing { get; private set; }

    /// <summary>
    ///     The skill waiting for its target; the default value while nothing is being chosen.
    /// </summary>
    public SkillDefinitionId Skill { get; private set; }

    /// <summary>
    ///     The waiting skill's target type, meaningful only while <see cref="IsChoosing" />.
    /// </summary>
    public SkillTargetType TargetType { get; private set; }

    /// <summary>
    ///     Whether <paramref name="entity" /> can take a skill of <paramref name="targetType" />: a live monster for an
    ///     enemy skill, a live player or the living caster for an ally skill, and nothing for a skill on the caster.
    /// </summary>
    public static bool CanTake(ClientWorld world, SkillTargetType targetType, EntityId entity)
    {
        switch (targetType)
        {
            case SkillTargetType.Enemy:
                return IsLive(world, entity, EntityKind.Monster);
            case SkillTargetType.Ally:
                return entity == world.LocalEntity ? !world.IsLocalDead : IsLive(world, entity, EntityKind.Player);
            default:
                return false;
        }
    }

    /// <summary>
    ///     What a press of <paramref name="skill" /> does: a skill on the caster ends any choice and goes at once; the
    ///     same ally skill pressed again ends the choice and goes on the caster; the same enemy skill again changes
    ///     nothing; any other begins the choice of its target, in place of one under way.
    /// </summary>
    public SkillPress Press(SkillDefinitionId skill, SkillTargetType targetType)
    {
        if (targetType == SkillTargetType.Self)
        {
            Cancel();
            return SkillPress.Instant;
        }

        if (IsChoosing && Skill == skill)
        {
            if (targetType != SkillTargetType.Ally)
            {
                return SkillPress.StillChoosing;
            }

            Cancel();
            return SkillPress.OnCaster;
        }

        IsChoosing = true;
        Skill = skill;
        TargetType = targetType;
        return SkillPress.Choosing;
    }

    /// <summary>
    ///     Ends the choice; true when one was under way.
    /// </summary>
    public bool Cancel()
    {
        bool wasChoosing = IsChoosing;
        IsChoosing = false;
        Skill = default;
        TargetType = default;
        return wasChoosing;
    }

    /// <summary>
    ///     Whether a click or tap on <paramref name="entity" /> names the waiting skill's target.
    /// </summary>
    public bool Accepts(ClientWorld world, EntityId entity)
    {
        return IsChoosing && CanTake(world, TargetType, entity);
    }

    /// <summary>
    ///     Appends what a click or tap may choose while the skill waits: the live monsters for an enemy skill, and the
    ///     live players with the living caster, drawn at <paramref name="casterDrawnAt" />, for an ally skill. Nothing
    ///     else is a candidate, so an NPC or a drop in front of a target never takes its click.
    /// </summary>
    public void CollectCandidates(ClientWorld world, WorldPosition casterDrawnAt, List<PickCandidate> candidates)
    {
        if (!IsChoosing)
        {
            return;
        }

        if (TargetType == SkillTargetType.Enemy)
        {
            world.CollectTargetCandidates(candidates);
            return;
        }

        world.CollectPlayerCandidates(candidates);
        if (!world.IsLocalDead)
        {
            candidates.Add(new PickCandidate(world.LocalEntity, casterDrawnAt));
        }
    }

    private static bool IsLive(ClientWorld world, EntityId entity, EntityKind kind)
    {
        return entity != default
            && world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
            && remote.Kind == kind
            && !remote.IsDead;
    }
}
}
