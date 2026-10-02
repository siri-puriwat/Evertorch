using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Resolves basic attacks and skill casts (Gameplay Systems §6, §7, §9). Each tick it first resolves the impacts
///     and casts that are due, then begins the swings that may begin. Timing is kept in exact milliseconds: an event
///     runs on the first tick at or after its time, and a continuing schedule adds the interval to the previous nominal
///     start, so tick alignment never accumulates. Crash-fast by design (System Architecture §7).
/// </summary>
public sealed class CombatSystem : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly WorldSimulation m_world;
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly Targeting m_targeting;
    private readonly ItemDropSystem m_drops;
    private readonly CharacterProgression m_progression;
    private readonly StatusEffectSystem m_statusEffects;
    private readonly ServerContent m_content;
    private readonly ICombatRules m_rules;
    private readonly ISkillRules m_skillRules;
    private readonly ServerInstruments m_instruments;
    private readonly IRandomSource m_random;
    private readonly BossAnnouncer m_bosses;
    private readonly int m_tickRate;
    private readonly float m_rangeTolerance;
    private readonly List<WorldEntity> m_due = new();
    private readonly List<WorldEntity> m_attackers = new();
    private readonly List<PlayerEntity> m_struck = new();

    public CombatSystem(
        WorldSimulation world,
        SessionRegistry sessions,
        MessageSender sender,
        Targeting targeting,
        ItemDropSystem drops,
        CharacterProgression progression,
        StatusEffectSystem statusEffects,
        ServerContent content,
        ICombatRules rules,
        ISkillRules skillRules,
        IRandomSource random,
        ServerInstruments instruments,
        BossAnnouncer bosses,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_sessions = sessions;
        m_sender = sender;
        m_targeting = targeting;
        m_drops = drops;
        m_progression = progression;
        m_statusEffects = statusEffects;
        m_content = content;
        m_rules = rules;
        m_skillRules = skillRules;
        m_random = random;
        m_instruments = instruments;
        m_bosses = bosses;
        m_tickRate = simulation.Value.TickRate;
        m_rangeTolerance = worldOptions.Value.AttackRangeTolerance;
    }

    public TickPhase Phase => TickPhase.Combat;

    public void Execute(in TickContext context)
    {
        long now = TickMilliseconds(context.Tick);
        foreach (MapInstance map in m_world.Maps)
        {
            InterruptCastsOnDepartedTargets(map);
            ResolveImpacts(map, now, context.Tick);
            BeginSwings(map, now, context.Tick);
            foreach (WorldEntity entity in map.Entities)
            {
                entity.Combat.HasMovedThisTick = false;
            }
        }
    }

    /// <summary>
    ///     Begins <paramref name="player" />'s cast of <paramref name="skillId" /> at <paramref name="target" />, the
    ///     default value for itself, after the checks of Gameplay Systems §9 in their order. It resolves on the first
    ///     tick at or after its cast time, a cast of 0 ms in this tick's combat phase. The selected target and the
    ///     auto-attack stay as they were.
    /// </summary>
    public CastRefusal TryBeginCast(
        MapInstance map,
        PlayerEntity player,
        SkillDefinitionId skillId,
        EntityId target,
        uint tick)
    {
        long now = TickMilliseconds(tick);
        CombatState combat = player.Combat;
        if (player.IsDead
            || !player.Skills.TryGetValue(skillId, out int level)
            || !m_content.Skills.TryGetValue(skillId, out SkillDefinition? skill)
            || !skill.HasEffect
            || combat.IsCasting
            || combat.IsSwinging
            || now < combat.DelayEndsMs
            || now < combat.CooldownEndMs(skillId))
        {
            return CastRefusal.NotAllowedNow;
        }

        SkillLevel values = skill.ValuesAt(level);
        if (player.CurrentSpirit < values.SpCost)
        {
            return CastRefusal.NotEnoughSp;
        }

        WorldEntity? resolvedOn = player;

        // An ally skill names the caster by 0 or its own ID, like a self skill, or another player it knows; an enemy
        // skill a monster alone (Gameplay Systems §9).
        bool isOnCaster = target == default || target == player.Id;
        if (skill.TargetType == SkillTargetType.Self || (skill.TargetType == SkillTargetType.Ally && isOnCaster))
        {
            if (!isOnCaster)
            {
                return CastRefusal.InvalidTarget;
            }
        }
        else
        {
            ClientSession? session = FindSession(player);
            if (target == default
                || session == null
                || !(skill.TargetType == SkillTargetType.Ally
                    ? Targeting.IsSelectablePlayer(session, target)
                    : Targeting.IsAttackable(session, target))
                || !map.TryGetEntity(target, out resolvedOn)
                || resolvedOn == null)
            {
                return CastRefusal.InvalidTarget;
            }

            if (HorizontalDistance(player.Position, resolvedOn.Position) > skill.Range + m_rangeTolerance
                || !map.Definition.Navigation.HasLineOfSight(player.Position, resolvedOn.Position))
            {
                return CastRefusal.OutOfRange;
            }

            Face(player, resolvedOn);
        }

        CastTiming timing = m_skillRules.CalculateCastTiming(
            new SkillContext(skill, level, AttackerKind.Character, player.Stats.VariableCastPermille));
        bool isPaidNow = skill.SpPaidAt == SkillPaymentPoint.CastStart;
        if (isPaidNow && values.SpCost > 0)
        {
            player.CurrentSpirit -= values.SpCost;
            m_sender.SendHealth(player);
        }

        combat.BeginCast(skillId, level, resolvedOn.Id, now, now + timing.CastMs, isPaidNow);
        AnnounceCastStarted(map, player, resolvedOn, skillId, tick, timing.CastMs);
        return CastRefusal.None;
    }

    /// <summary>
    ///     Whether <paramref name="monster" /> could begin a cast of <paramref name="skillId" /> at
    ///     <paramref name="target" /> now (Gameplay Systems §9, §10): it is free, not swinging, casting, or in an
    ///     after-cast delay, the skill's cooldown is over, and the target is alive and in sight, within the skill's
    ///     range, or within its area for a skill that strikes around the monster.
    /// </summary>
    public bool CanMonsterCast(
        MapInstance map,
        MonsterEntity monster,
        SkillDefinitionId skillId,
        WorldEntity target,
        uint tick)
    {
        long now = TickMilliseconds(tick);
        CombatState combat = monster.Combat;
        return !monster.IsDead
            && !target.IsDead
            && m_content.Skills.TryGetValue(skillId, out SkillDefinition? skill)
            && skill.HasEffect
            && !combat.IsCasting
            && !combat.IsSwinging
            && now >= combat.DelayEndsMs
            && now >= combat.CooldownEndMs(skillId)
            && HorizontalDistance(monster.Position, target.Position) <= (skill.HasArea ? skill.AreaRadius : skill.Range)
            && map.Definition.Navigation.HasLineOfSight(monster.Position, target.Position);
    }

    /// <summary>
    ///     Begins a cast <see cref="CanMonsterCast" /> allowed. A monster casts at level 1, pays no SP, and its cast
    ///     time is the level's fixed and variable time together. A skill on itself, such as an area around it, names
    ///     the monster as its target, so neither the death nor the walk of the player it fights ends the cast.
    /// </summary>
    public void BeginMonsterCast(
        MapInstance map,
        MonsterEntity monster,
        SkillDefinitionId skillId,
        WorldEntity target,
        uint tick)
    {
        SkillDefinition skill = m_content.Skills[skillId];
        CastTiming timing = m_skillRules.CalculateCastTiming(new SkillContext(skill, 1, AttackerKind.Monster, 0));
        Face(monster, target);
        WorldEntity castOn = skill.TargetType == SkillTargetType.Self ? monster : target;
        long now = TickMilliseconds(tick);
        monster.Combat.BeginCast(skillId, 1, castOn.Id, now, now + timing.CastMs, true);
        AnnounceCastStarted(map, monster, castOn, skillId, tick, timing.CastMs);
    }

    /// <summary>
    ///     Ends <paramref name="caster" />'s cast, if any, without effect (Gameplay Systems §9): SP not yet paid is
    ///     not paid, and neither the after-cast delay nor the cooldown starts.
    /// </summary>
    public void InterruptCast(WorldEntity caster)
    {
        if (caster.Combat.IsCasting)
        {
            caster.Combat.EndCast();
            m_instruments.RecordCast(false);
        }
    }

    /// <summary>
    ///     Ends what <paramref name="player" /> was doing on <paramref name="map" /> before it leaves for another map:
    ///     its target, auto-attack, swing, and cast, and every swing, cast, and target aimed at it there.
    /// </summary>
    public void PrepareForTransfer(MapInstance map, PlayerEntity player)
    {
        player.Combat.IsAutoAttacking = false;
        player.Combat.EndSwing();
        InterruptCast(player);
        ClearTarget(player);
        foreach (WorldEntity other in map.Entities)
        {
            if (other.Target == player.Id)
            {
                ClearTarget(other);
            }

            if (other.Combat.IsSwinging && other.Combat.SwingTarget == player.Id)
            {
                other.Combat.EndSwing();
            }

            if (other.Combat.IsCasting && other.Combat.CastTarget == player.Id)
            {
                InterruptCast(other);
            }
        }
    }

    /// <summary>
    ///     Ends <paramref name="entity" />'s life: its own and its attackers' auto-attacks end, swings and casts at it
    ///     and its own cast are interrupted, and every client that knows it hears of the death.
    /// </summary>
    public void Kill(MapInstance map, WorldEntity entity, WorldEntity? source, uint tick)
    {
        if (entity.IsDead)
        {
            return;
        }

        entity.CurrentHealth = 0;
        entity.StateFlags = (entity.StateFlags | EntityStateFlags.Dead) & ~EntityStateFlags.Moving;
        entity.VelocityX = 0f;
        entity.VelocityY = 0f;
        entity.VelocityZ = 0f;
        entity.Combat.IsAutoAttacking = false;
        entity.Combat.EndSwing();
        InterruptCast(entity);
        ClearTarget(entity);
        if (entity is PlayerEntity dead)
        {
            m_statusEffects.EndAll(dead);
        }

        foreach (WorldEntity other in map.Entities)
        {
            if (other.Target == entity.Id)
            {
                ClearTarget(other);
            }

            if (other.Combat.IsSwinging && other.Combat.SwingTarget == entity.Id)
            {
                other.Combat.EndSwing();
            }

            if (other.Combat.IsCasting && other.Combat.CastTarget == entity.Id)
            {
                InterruptCast(other);
            }
        }

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(entity.Id))
            {
                EntityId known = source != null && session.Knows(source.Id) ? source.Id : default;
                m_sender.Send(session.Connection, new EntityDied(entity.Id, known, tick));
            }
        }

        if (entity is MonsterEntity monster)
        {
            // Before the award, which returns early for a monster that gives no experience (Gameplay Systems §2.2).
            m_progression.CreditQuests(map, monster);
            m_progression.AwardKill(map, monster);
            MostValuablePlayer? mostValuable = monster.Definition.IsBoss ? AwardMostValuable(map, monster) : null;
            CharacterId killer = source is PlayerEntity player ? player.Character : default;
            m_drops.DropLoot(map, monster, killer, tick);
            if (monster.Definition.IsBoss)
            {
                m_bosses.Fell(map, monster, mostValuable);
            }
        }
    }

    // A boss's most valuable player gains its MVP experience alone, and its prize is rolled from the drops' source
    // before the drops are (Gameplay Systems §10).
    private MostValuablePlayer? AwardMostValuable(MapInstance map, MonsterEntity boss)
    {
        CharacterSession? character = m_progression.ChooseMostValuable(map, boss);
        if (character == null)
        {
            return null;
        }

        long gained = m_progression.AwardMostValuable(character, boss.Definition.MvpExperience);
        return new MostValuablePlayer(character, gained, m_drops.RollPrize(boss.Definition));
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static CombatResult ToCombatResult(HitOutcome outcome)
    {
        switch (outcome)
        {
            case HitOutcome.Hit:
                return CombatResult.Hit;
            case HitOutcome.Critical:
                return CombatResult.Critical;
            default:
                return CombatResult.Miss;
        }
    }

    private static long DueMs(WorldEntity entity)
    {
        return entity.Combat.IsCasting ? entity.Combat.CastResolveMs : entity.Combat.ImpactMs;
    }

    private static void Face(WorldEntity entity, WorldEntity target)
    {
        WorldDirection facing = MovementModel.NormalizeOrZero(
            target.Position.X - entity.Position.X,
            target.Position.Z - entity.Position.Z);
        if (facing != default)
        {
            entity.Facing = facing;
        }
    }

    // A target that left the map (a player logging out, say) ends every cast at it at once, as its death would.
    private void InterruptCastsOnDepartedTargets(MapInstance map)
    {
        foreach (WorldEntity entity in map.Entities)
        {
            if (entity.Combat.IsCasting
                && entity.Combat.CastTarget != entity.Id
                && !map.TryGetEntity(entity.Combat.CastTarget, out WorldEntity? _))
            {
                InterruptCast(entity);
            }
        }
    }

    private void ResolveImpacts(MapInstance map, long now, uint tick)
    {
        m_due.Clear();
        foreach (WorldEntity entity in map.Entities)
        {
            if ((entity.Combat.IsSwinging && entity.Combat.ImpactMs <= now)
                || (entity.Combat.IsCasting && entity.Combat.CastResolveMs <= now))
            {
                m_due.Add(entity);
            }
        }

        // Several impacts and casts in one tick resolve in a fixed order, so an entity killed by an earlier one is
        // interrupted the same way on every run.
        m_due.Sort((left, right) =>
        {
            int byTime = DueMs(left).CompareTo(DueMs(right));
            return byTime != 0 ? byTime : left.Id.Value.CompareTo(right.Id.Value);
        });

        foreach (WorldEntity attacker in m_due)
        {
            if (attacker.Combat.IsCasting)
            {
                ResolveCast(map, attacker, now, tick);
                continue;
            }

            if (!attacker.Combat.IsSwinging)
            {
                continue;
            }

            EntityId targetId = attacker.Combat.SwingTarget;
            attacker.Combat.EndSwing();
            if (attacker.IsDead
                || !map.TryGetEntity(targetId, out WorldEntity? target)
                || target == null
                || target.IsDead)
            {
                continue;
            }

            Resolve(map, attacker, target, tick);
        }
    }

    private void Resolve(MapInstance map, WorldEntity attacker, WorldEntity target, uint tick)
    {
        HitResult hit = m_rules.CalculateHit(CreateHitContext(attacker, target));
        int amount = 0;
        CombatResult result = ToCombatResult(hit.Outcome);
        if (hit.DealsDamage)
        {
            DamageResult damage = m_rules.CalculateDamage(
                CreateDamageContext(attacker, target, hit.Outcome == HitOutcome.Critical));
            amount = damage.Amount;
            target.CurrentHealth = Math.Max(0, target.CurrentHealth - amount);
        }

        // What a boss's basic attacks deal counts toward its most valuable player; its skills do not (Gameplay Systems
        // §10).
        if (attacker is MonsterEntity boss && boss.Definition.IsBoss && target is PlayerEntity victim && amount > 0)
        {
            boss.LogMvpTaken(victim.Character, amount);
        }

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(target.Id))
            {
                EntityId source = session.Knows(attacker.Id) ? attacker.Id : default;
                m_sender.Send(
                    session.Connection,
                    new Damage(source, target.Id, result, (uint)amount, tick, target.SharedHealthPermille));
            }
        }

        AfterDamage(map, attacker, target, amount, tick);
    }

    // What follows damage however it was dealt: the experience log, the monster's grudge, the victim's own HP, and
    // its death.
    private void AfterDamage(MapInstance map, WorldEntity attacker, WorldEntity target, int amount, uint tick)
    {
        if (target is MonsterEntity damaged && attacker is PlayerEntity damager && amount > 0)
        {
            // The whole roll counts toward the experience share, a killing blow's overkill included.
            damaged.LogDamage(damager.Character, amount);
            if (damaged.Definition.IsBoss)
            {
                damaged.LogMvpDealt(damager.Character, amount);
            }
        }

        if (target is MonsterEntity monster && amount > 0 && monster.Brain.State != MonsterAiState.ReturnHome)
        {
            // A passive monster turns on whoever damages it; while it walks home it ignores damage.
            monster.Brain.LastAttacker = attacker.Id;
        }

        if (target is PlayerEntity player && amount > 0)
        {
            m_sender.SendHealth(player);
        }

        if (target.CurrentHealth == 0)
        {
            Kill(map, target, attacker, tick);
        }
    }

    // Pays what the cast still owes, applies its effect, and starts its after-cast delay and cooldown. A target that
    // is gone or dead by now leaves the cast interrupted, and so does one out of the skill's range or sight after a
    // cast time (Gameplay Systems §9, finding R1), a monster's cast included: an instant cast was checked as it began,
    // and only this tick's movement lies between, which would cut short a strike at a target walking off.
    private bool IsWithinReach(MapInstance map, WorldEntity caster, WorldEntity target, SkillDefinition skill)
    {
        return HorizontalDistance(caster.Position, target.Position) <= skill.Range + m_rangeTolerance
            && map.Definition.Navigation.HasLineOfSight(caster.Position, target.Position);
    }

    private void ResolveCast(MapInstance map, WorldEntity caster, long now, uint tick)
    {
        CombatState combat = caster.Combat;
        SkillDefinition skill = m_content.Skills[combat.CastSkill];
        int level = combat.CastLevel;
        SkillLevel values = skill.ValuesAt(level);
        WorldEntity? target = caster;
        if (combat.CastTarget != caster.Id
            && (!map.TryGetEntity(combat.CastTarget, out target)
                || target == null
                || target.IsDead
                || (combat.HasCastTime && !IsWithinReach(map, caster, target, skill))))
        {
            InterruptCast(caster);
            return;
        }

        if (caster is PlayerEntity paying && !combat.IsCastPaid && values.SpCost > 0)
        {
            paying.CurrentSpirit = Math.Max(0, paying.CurrentSpirit - values.SpCost);
            m_sender.SendHealth(paying);
        }

        CastTiming timing =
            m_skillRules.CalculateCastTiming(CreateSkillContext(caster, target, skill, level, false));
        combat.CompleteCast(now, timing.AfterCastDelayMs, timing.CooldownMs);
        m_instruments.RecordCast(true);

        if (caster is PlayerEntity owner)
        {
            // Its cooldown changed, so its owner gets the list again.
            ClientSession? ownerSession = FindSession(owner);
            if (ownerSession != null)
            {
                ownerSession.NeedsSkillList = true;
            }
        }

        if (values.Effect.Kind == SkillEffectKind.Status)
        {
            // Effects are players' alone: a job's status skill lands on its caster, a monster's on the player it
            // fights (§9.1), with no roll to resist it.
            if (target is PlayerEntity affected)
            {
                m_statusEffects.Apply(
                    affected,
                    values.Effect.Status,
                    values.Effect.StatPercent,
                    now + values.Effect.StatusDurationMs);
            }

            AnnounceResolved(map, caster, target, skill.Id, SkillOutcome.Applied, 0, tick);
            return;
        }

        if (skill.HasArea)
        {
            ResolveArea(map, caster, skill, level, tick);
            return;
        }

        SkillResolution resolution = m_skillRules.Resolve(
            CreateSkillContext(caster, target, skill, level, values.Effect.Kind == SkillEffectKind.Damage));
        if (resolution.Result == SkillResult.Healed)
        {
            target.CurrentHealth = Math.Min(target.MaxHealth, target.CurrentHealth + resolution.Amount);
            AnnounceResolved(map, caster, target, skill.Id, SkillOutcome.Healed, resolution.Amount, tick);
            if (target is PlayerEntity healed)
            {
                m_sender.SendHealth(healed);
            }

            return;
        }

        target.CurrentHealth = Math.Max(0, target.CurrentHealth - resolution.Amount);
        SkillOutcome outcome = resolution.Result == SkillResult.Hit ? SkillOutcome.Hit : SkillOutcome.Miss;
        AnnounceResolved(map, caster, target, skill.Id, outcome, resolution.Amount, tick);
        AfterDamage(map, caster, target, resolution.Amount, tick);
    }

    // Every live player within the area and in its caster's sight takes its own roll, in order of entity ID, since the
    // map keeps its players unordered (Gameplay Systems §9). One that stepped out takes nothing, and a strike that
    // reaches nobody is told to nobody.
    private void ResolveArea(MapInstance map, WorldEntity caster, SkillDefinition skill, int level, uint tick)
    {
        m_struck.Clear();
        foreach (PlayerEntity player in map.Players)
        {
            if (!player.IsDead
                && HorizontalDistance(caster.Position, player.Position) <= skill.AreaRadius
                && map.Definition.Navigation.HasLineOfSight(caster.Position, player.Position))
            {
                m_struck.Add(player);
            }
        }

        m_struck.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
        foreach (PlayerEntity player in m_struck)
        {
            SkillResolution resolution = m_skillRules.Resolve(CreateSkillContext(caster, player, skill, level, true));
            player.CurrentHealth = Math.Max(0, player.CurrentHealth - resolution.Amount);
            SkillOutcome outcome = resolution.Result == SkillResult.Hit ? SkillOutcome.Hit : SkillOutcome.Miss;
            AnnounceResolved(map, caster, player, skill.Id, outcome, resolution.Amount, tick);
            AfterDamage(map, caster, player, resolution.Amount, tick);
        }
    }

    // To every client that knows the caster; one that does not know the target is told 0 (Network Protocol §9).
    private void AnnounceCastStarted(
        MapInstance map,
        WorldEntity caster,
        WorldEntity target,
        SkillDefinitionId skill,
        uint tick,
        int castMs)
    {
        foreach (ClientSession other in SessionsOn(map))
        {
            if (other.Knows(caster.Id))
            {
                EntityId shownTarget = target != caster && other.Knows(target.Id) ? target.Id : default;
                m_sender.Send(
                    other.Connection,
                    new SkillCastStarted(caster.Id, skill, shownTarget, tick, (uint)castMs));
            }
        }
    }

    // To every client that knows the target; one that knows no caster is told 0 (Network Protocol §9).
    private void AnnounceResolved(
        MapInstance map,
        WorldEntity caster,
        WorldEntity target,
        SkillDefinitionId skill,
        SkillOutcome outcome,
        int amount,
        uint tick)
    {
        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(target.Id))
            {
                EntityId shownCaster = session.Knows(caster.Id) ? caster.Id : default;
                m_sender.Send(
                    session.Connection,
                    new SkillResolved(
                        shownCaster,
                        target.Id,
                        skill,
                        outcome,
                        (uint)amount,
                        tick,
                        target.SharedHealthPermille));
            }
        }
    }

    private SkillContext CreateSkillContext(
        WorldEntity caster,
        WorldEntity target,
        SkillDefinition skill,
        int level,
        bool isDamage)
    {
        AttackerKind kind = caster is PlayerEntity ? AttackerKind.Character : AttackerKind.Monster;
        int permille = caster is PlayerEntity player ? player.Stats.VariableCastPermille : 0;
        if (isDamage && skill.DamageType == SkillDamageType.Magical)
        {
            return new SkillContext(skill, level, kind, permille, CreateMagicContext(caster, target));
        }

        return isDamage
            ? new SkillContext(
                skill,
                level,
                kind,
                permille,
                CreateHitContext(caster, target),
                CreateDamageContext(caster, target, false))
            : new SkillContext(skill, level, kind, permille);
    }

    // A monster's magic attack is its definition's, a player's its derived one. Nothing gives hard magic defense yet,
    // and monsters carry no statistics to derive a soft one from.
    private MagicDamageContext CreateMagicContext(WorldEntity caster, WorldEntity target)
    {
        int magicAttack = caster switch
        {
            MonsterEntity monster => monster.Definition.MagicAttack,
            PlayerEntity player => player.Stats.MagicalAttack,
            _ => 0
        };
        int softMagicDefense = target is PlayerEntity defender ? defender.Stats.SoftMagicDefense : 0;
        return new MagicDamageContext(magicAttack, 0, softMagicDefense, m_random);
    }

    private HitContext CreateHitContext(WorldEntity attacker, WorldEntity target)
    {
        switch (attacker)
        {
            case PlayerEntity player when target is MonsterEntity monster:
                return new HitContext(
                    player.Stats.Hit,
                    player.Stats.Critical,
                    monster.Definition.Flee,
                    0,
                    0,
                    m_random);
            case MonsterEntity monster when target is PlayerEntity player:
                return new HitContext(
                    monster.Definition.Hit,
                    0,
                    player.Stats.Flee,
                    player.Stats.PerfectDodge,
                    player.Primary.Luk,
                    m_random);
            default:
                throw new InvalidOperationException(
                    $"No basic attack is defined from {attacker.Kind} {attacker.Id} to {target.Kind} {target.Id}.");
        }
    }

    private DamageContext CreateDamageContext(WorldEntity attacker, WorldEntity target, bool isCritical)
    {
        // Evertorch monsters have no primary statistics, so no soft defense (research note, intentional differences). A
        // player's weapon adds its attack, and its armor is its hard defense (equipment research note).
        switch (attacker)
        {
            case PlayerEntity player when target is MonsterEntity monster:
                return new DamageContext(
                    AttackerKind.Character,
                    player.Stats.PhysicalAttack,
                    player.Weapon?.Attack ?? 0,
                    monster.Definition.PhysicalDefense,
                    0,
                    isCritical,
                    m_random);
            case MonsterEntity monster when target is PlayerEntity player:
                return new DamageContext(
                    AttackerKind.Monster,
                    0,
                    monster.Definition.PhysicalAttack,
                    player.Armor?.Defense ?? 0,
                    player.Stats.SoftDefense,
                    isCritical,
                    m_random);
            default:
                throw new InvalidOperationException(
                    $"No basic attack is defined from {attacker.Kind} {attacker.Id} to {target.Kind} {target.Id}.");
        }
    }

    private void BeginSwings(MapInstance map, long now, uint tick)
    {
        m_attackers.Clear();
        foreach (WorldEntity entity in map.Entities)
        {
            // A cast and its after-cast delay hold the next swing back; the auto-attack goes on after them.
            if (entity.Combat.IsAutoAttacking
                && !entity.Combat.IsSwinging
                && !entity.Combat.IsCasting
                && now >= entity.Combat.DelayEndsMs
                && !entity.IsDead)
            {
                m_attackers.Add(entity);
            }
        }

        foreach (WorldEntity attacker in m_attackers)
        {
            if (!TryGetValidTarget(map, attacker, out WorldEntity? target) || target == null)
            {
                ClearTarget(attacker);
                continue;
            }

            if (attacker.Combat.HasMovedThisTick
                || attacker.Combat.NextNominalMs > now
                || !IsInRange(attacker, target)
                || !map.Definition.Navigation.HasLineOfSight(attacker.Position, target.Position))
            {
                continue;
            }

            Begin(map, attacker, target, now, tick);
        }
    }

    private bool TryGetValidTarget(MapInstance map, WorldEntity attacker, out WorldEntity? target)
    {
        target = null;
        if (attacker is PlayerEntity player)
        {
            ClientSession? session = FindSession(player);
            return session != null
                && Targeting.IsAttackable(session, player.Target)
                && map.TryGetEntity(player.Target, out target);
        }

        return map.TryGetEntity(attacker.Target, out target) && target != null && !target.IsDead;
    }

    private bool IsInRange(WorldEntity attacker, WorldEntity target)
    {
        float reach = attacker.AttackRange + (attacker is PlayerEntity ? m_rangeTolerance : 0f);
        return HorizontalDistance(attacker.Position, target.Position) <= reach;
    }

    private void Begin(MapInstance map, WorldEntity attacker, WorldEntity target, long now, uint tick)
    {
        AttackTiming timing = m_rules.CalculateAttackTiming(CreateAttackContext(attacker));

        // The schedule carries on only when this swing begins on the first tick at or after its nominal start;
        // any later start, because the attacker was walking or the target was out of reach, begins a new one.
        long next = attacker.Combat.NextNominalMs;
        bool isCarried = next != long.MinValue && tick > 1 && now >= next && TickMilliseconds(tick - 1) < next;
        long nominal = isCarried ? next : now;

        attacker.Combat.BeginSwing(target.Id, tick, nominal, timing);
        Face(attacker, target);

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(attacker.Id))
            {
                EntityId known = session.Knows(target.Id) ? target.Id : default;
                m_sender.Send(session.Connection, new AttackStarted(attacker.Id, known, tick, timing));
            }
        }
    }

    private AttackContext CreateAttackContext(WorldEntity attacker)
    {
        switch (attacker)
        {
            case PlayerEntity player:
                return AttackContext.ForAttackSpeed(player.Stats.AttackSpeed);
            case MonsterEntity monster:
                return AttackContext.ForFixedInterval(TimeSpan.FromMilliseconds(monster.Definition.AttackIntervalMs));
            default:
                throw new InvalidOperationException($"{attacker.Kind} {attacker.Id} cannot attack.");
        }
    }

    private void ClearTarget(WorldEntity entity)
    {
        entity.Combat.IsAutoAttacking = false;
        if (entity is PlayerEntity player)
        {
            ClientSession? session = FindSession(player);
            if (session != null)
            {
                m_targeting.ClearIfTargeting(session, player.Target);
                return;
            }
        }

        entity.Target = default;
    }

    private ClientSession? FindSession(PlayerEntity player)
    {
        return m_sessions.TryGet(player.Owner, out ClientSession? session)
            && session != null
            && session.Player == player
                ? session
                : null;
    }

    private IEnumerable<ClientSession> SessionsOn(MapInstance map)
    {
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Map == map)
            {
                yield return session;
            }
        }
    }

    private long TickMilliseconds(uint tick)
    {
        return (long)(tick - 1) * MillisecondsPerSecond / m_tickRate;
    }
}
}
