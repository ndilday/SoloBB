using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState ResolvePendingReroll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        bool useTeamReroll,
        string? skillId = null,
        LeagueTeam? opposingTeam = null)
    {
        // A Dump-Off pass that spawned this reroll (pass or catch) holds its block until the pass settles.
        return ResumeDeferredBlock(ResolvePendingRerollCore(match, ruleset, team, useTeamReroll, skillId, opposingTeam), ruleset, team, opposingTeam);
    }

    private MatchState ResolvePendingRerollCore(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        bool useTeamReroll,
        string? skillId = null,
        LeagueTeam? opposingTeam = null)
    {
        var pending = match.PendingReroll
            ?? throw new InvalidOperationException("There is no pending reroll.");

        if (pending.TeamId != team.Id)
        {
            throw new InvalidOperationException("Pending reroll belongs to another team.");
        }

        if (useTeamReroll && !pending.TeamRerollAvailable)
        {
            throw new InvalidOperationException($"{team.Name} has no team rerolls available.");
        }

        if (!string.IsNullOrWhiteSpace(skillId) && !pending.SkillRerollIds.Contains(skillId))
        {
            throw new InvalidOperationException("That skill reroll is not available.");
        }

        var baseMatch = pending.Context.MatchBeforeRoll with { PendingReroll = null };
        var player = FindTeamPlayer(team, pending.PlayerId);
        if (pending.Kind == PendingRerollKind.Pass)
        {
            if (!useTeamReroll && string.IsNullOrWhiteSpace(skillId))
            {
                return PassBallCore(
                    baseMatch,
                    ruleset,
                    team,
                    player.Id,
                    pending.Context.Destination,
                    opposingTeam,
                    usePassSkillReroll: false,
                    pending.Context.UseCloudBurster,
                    pending.Context.IsDumpOff,
                    pending.Context.IsHailMary,
                    forcedPassRoll: pending.Roll);
            }

            if (useTeamReroll &&
                SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.AfterRoll, SkillEffect.Loner))
            {
                var lonerRoll = _dice.RollD6();
                if (lonerRoll < 4)
                {
                    var failedLonerMatch = baseMatch with
                    {
                        Log =
                        [
                            .. baseMatch.Log,
                            new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll cannot be used." }
                        ]
                    };

                    return PassBallCore(
                        failedLonerMatch,
                        ruleset,
                        team,
                        player.Id,
                        pending.Context.Destination,
                        opposingTeam,
                        usePassSkillReroll: false,
                        pending.Context.UseCloudBurster,
                        pending.Context.IsDumpOff,
                        pending.Context.IsHailMary,
                        forcedPassRoll: pending.Roll);
                }

                baseMatch = baseMatch with
                {
                    Log =
                    [
                        .. baseMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll may be used." }
                    ]
                };
            }

            var passRerolledMatch = useTeamReroll
                ? SpendTeamReroll(baseMatch, ruleset, team)
                : baseMatch;
            var passReroll = _dice.RollD6();
            passRerolledMatch = passRerolledMatch with
            {
                Log =
                [
                    .. passRerolledMatch.Log,
                    new MatchLogEntry
                    {
                        Message = useTeamReroll
                            ? $"{team.Name} uses a team reroll: pass rerolled from {pending.Roll} to {passReroll} vs {pending.Target}+."
                            : $"{player.Name} uses {skillId}: pass rerolled from {pending.Roll} to {passReroll} vs {pending.Target}+."
                    }
                ]
            };

            return PassBallCore(
                passRerolledMatch,
                ruleset,
                team,
                player.Id,
                pending.Context.Destination,
                opposingTeam,
                usePassSkillReroll: false,
                pending.Context.UseCloudBurster,
                pending.Context.IsDumpOff,
                pending.Context.IsHailMary,
                forcedPassRoll: passReroll);
        }

        if (pending.Kind is PendingRerollKind.Catch
            or PendingRerollKind.ThrowTeamMate
            or PendingRerollKind.KickTeamMate
            or PendingRerollKind.Landing
            or PendingRerollKind.BombThrow
            or PendingRerollKind.BombCatch
            or PendingRerollKind.HypnoticGaze
            or PendingRerollKind.JumpUp
            or PendingRerollKind.Dauntless)
        {
            return ResolveActionReroll(baseMatch, ruleset, team, opposingTeam, player, pending, useTeamReroll, skillId);
        }

        if (!useTeamReroll && string.IsNullOrWhiteSpace(skillId))
        {
            return ResolveDeclinedMovementReroll(baseMatch, ruleset, team, player, pending);
        }

        if (useTeamReroll &&
            SkillHookResolver.PlayerHasHookedEffect(ruleset, player, PendingRerollEventKind(pending.Kind), GameEventStage.AfterRoll, SkillEffect.Loner))
        {
            var lonerRoll = _dice.RollD6();
            if (lonerRoll < 4)
            {
                var failedLonerMatch = baseMatch with
                {
                    Log =
                    [
                        .. baseMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll cannot be used." }
                    ]
                };

                return ResolveDeclinedMovementReroll(failedLonerMatch, ruleset, team, player, pending);
            }

            baseMatch = baseMatch with
            {
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll may be used." }
                ]
            };
        }

        var rerolledMatch = useTeamReroll
            ? SpendTeamReroll(baseMatch, ruleset, team)
            : baseMatch;

        if (string.Equals(skillId, "pro", StringComparison.OrdinalIgnoreCase))
        {
            var proRoll = _dice.RollD6();
            if (proRoll < 3)
            {
                var failedProMatch = rerolledMatch with
                {
                    Log =
                    [
                        .. rerolledMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} attempts Pro: rolled {proRoll}, no reroll." }
                    ]
                };

                return ResolveDeclinedMovementReroll(failedProMatch, ruleset, team, player, pending);
            }

            rerolledMatch = rerolledMatch with
            {
                Log =
                [
                    .. rerolledMatch.Log,
                    new MatchLogEntry { Message = $"{player.Name} attempts Pro: rolled {proRoll}, reroll available." }
                ]
            };
        }

        var reroll = _dice.RollD6();
        rerolledMatch = rerolledMatch with
        {
            Log =
            [
                .. rerolledMatch.Log,
                new MatchLogEntry
                {
                    Message = useTeamReroll
                        ? $"{team.Name} uses a team reroll: {FormatRerollKind(pending.Kind)} rerolled from {pending.Roll} to {reroll} vs {pending.Target}+."
                        : $"{player.Name} uses {skillId}: {FormatRerollKind(pending.Kind)} rerolled from {pending.Roll} to {reroll} vs {pending.Target}+."
                }
            ]
        };

        if (!RollSucceeds(reroll, pending.Target, ruleset.Dice))
        {
            return ResolveDeclinedMovementReroll(rerolledMatch, ruleset, team, player, pending with { Roll = reroll });
        }

        return ContinueMovementAfterRerollSuccess(rerolledMatch, ruleset, team, opposingTeam, player, pending, reroll);
    }

    /// <summary>
    /// Routes a single-roll reroll choice (catch, throw/kick team-mate, landing, bomb throw/catch,
    /// Hypnotic Gaze, Jump Up, Dauntless) to the right re-entry continuation. Each continuation re-runs
    /// only the rolled step with a forced value, never the action declaration, so activations and any
    /// pre-roll consumable rolls are not repeated.
    /// </summary>
    private MatchState ResolveActionReroll(
        MatchState baseMatch,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player player,
        PendingRerollChoice pending,
        bool useTeamReroll,
        string? skillId)
    {
        var context = pending.Context;
        var (eventKind, resolve) = pending.Kind switch
        {
            PendingRerollKind.Catch => (
                GameEventKind.CatchRoll,
                (Func<MatchState, int, MatchState>)((m, roll) => ResolveForcedCatch(m, ruleset, team, opposingTeam, player, context, roll))),
            PendingRerollKind.HypnoticGaze => (
                GameEventKind.SpecialAction,
                (m, roll) => ResolveHypnoticGazeRoll(m, ruleset, team, player, opposingTeam!, context.SecondaryPlayerId!.Value, roll)),
            PendingRerollKind.ThrowTeamMate or PendingRerollKind.KickTeamMate => (
                GameEventKind.ThrowTeamMate,
                (m, roll) => ResolveTeamMateThrow(m, ruleset, team, opposingTeam, player, FindTeamPlayer(team, context.SecondaryPlayerId!.Value), context.Destination, context.LaunchKind!, roll)),
            PendingRerollKind.Landing => (
                GameEventKind.SpecialAction,
                (m, roll) => ResolveLaunchedPlayerLanding(m, ruleset, team, opposingTeam, player, context.Destination, context.LaunchKind!, context.CollisionDepth, roll)),
            PendingRerollKind.BombThrow => (
                GameEventKind.BombThrow,
                (m, roll) => ResolveBombThrow(m, ruleset, team, opposingTeam!, player, context.Destination, context.BombThrownBack, roll)),
            PendingRerollKind.BombCatch => (
                GameEventKind.BombThrow,
                (m, roll) => ResolveBombLanding(m, ruleset, ResolveBounceTeam(context.BounceOriginalTeamId!.Value, team, opposingTeam), ResolveBounceTeam(context.BounceOpposingTeamId!.Value, team, opposingTeam), context.Destination, roll)),
            PendingRerollKind.JumpUp => (
                GameEventKind.BlockRoll,
                (m, roll) => ResolveJumpUpRoll(m, ruleset, team, opposingTeam!, player, context.SecondaryPlayerId!.Value, roll)),
            PendingRerollKind.Dauntless => (
                GameEventKind.BlockRoll,
                (m, roll) => ResolveDauntlessRoll(m, ruleset, team, opposingTeam!, player, context.SecondaryPlayerId!.Value, context.DefenderStrengthBonus, context.PreventFollowUp, roll)),
            _ => throw new InvalidOperationException($"Unsupported action reroll kind {pending.Kind}.")
        };

        return ResolveSimpleReroll(baseMatch, ruleset, team, player, pending, useTeamReroll, skillId, eventKind, resolve);
    }

    /// <summary>
    /// Generic team/Pro reroll handler for single-roll choices. Applies the Loner check before a team
    /// reroll, the Pro check for a Pro skill reroll, logs the outcome, and then re-runs the rolled step
    /// via <paramref name="resolveWithForcedRoll"/> with either the original roll (declined) or the new
    /// roll (rerolled).
    /// </summary>
    private MatchState ResolveSimpleReroll(
        MatchState baseMatch,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PendingRerollChoice pending,
        bool useTeamReroll,
        string? skillId,
        GameEventKind eventKind,
        Func<MatchState, int, MatchState> resolveWithForcedRoll)
    {
        var label = FormatRerollKind(pending.Kind);
        if (!useTeamReroll && string.IsNullOrWhiteSpace(skillId))
        {
            return resolveWithForcedRoll(baseMatch, pending.Roll);
        }

        if (useTeamReroll &&
            SkillHookResolver.PlayerHasHookedEffect(ruleset, player, eventKind, GameEventStage.AfterRoll, SkillEffect.Loner))
        {
            var lonerRoll = _dice.RollD6();
            if (lonerRoll < 4)
            {
                return resolveWithForcedRoll(
                    baseMatch with
                    {
                        Log = [.. baseMatch.Log, new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll cannot be used." }]
                    },
                    pending.Roll);
            }

            baseMatch = baseMatch with
            {
                Log = [.. baseMatch.Log, new MatchLogEntry { Message = $"{player.Name} checks Loner: rolled {lonerRoll} vs 4+, team reroll may be used." }]
            };
        }

        var rerolledMatch = useTeamReroll
            ? SpendTeamReroll(baseMatch, ruleset, team)
            : baseMatch;

        if (string.Equals(skillId, "pro", StringComparison.OrdinalIgnoreCase))
        {
            var proRoll = _dice.RollD6();
            if (proRoll < 3)
            {
                return resolveWithForcedRoll(
                    rerolledMatch with
                    {
                        Log = [.. rerolledMatch.Log, new MatchLogEntry { Message = $"{player.Name} attempts Pro: rolled {proRoll}, no reroll." }]
                    },
                    pending.Roll);
            }

            rerolledMatch = rerolledMatch with
            {
                Log = [.. rerolledMatch.Log, new MatchLogEntry { Message = $"{player.Name} attempts Pro: rolled {proRoll}, reroll available." }]
            };
        }

        var reroll = _dice.RollD6();
        rerolledMatch = rerolledMatch with
        {
            Log =
            [
                .. rerolledMatch.Log,
                new MatchLogEntry
                {
                    Message = useTeamReroll
                        ? $"{team.Name} uses a team reroll: {label} rerolled from {pending.Roll} to {reroll} vs {pending.Target}+."
                        : $"{player.Name} uses {skillId}: {label} rerolled from {pending.Roll} to {reroll} vs {pending.Target}+."
                }
            ]
        };

        return resolveWithForcedRoll(rerolledMatch, reroll);
    }

    private static IReadOnlyList<string> AvailableProReroll(Ruleset ruleset, Player player, GameEventKind eventKind)
    {
        return SkillHookResolver.PlayerHasHookedEffect(ruleset, player, eventKind, GameEventStage.AfterRoll, SkillEffect.Pro)
            ? ["pro"]
            : [];
    }

    /// <summary>
    /// True when the acting player may reroll a failed action roll: the team has a team reroll available
    /// this turn, or the player has Pro hooked for the roll's event.
    /// </summary>
    private bool ActionRerollOffered(MatchState match, Ruleset ruleset, LeagueTeam team, Player player, GameEventKind eventKind)
    {
        return CanUseTeamReroll(match, ruleset, team) || AvailableProReroll(ruleset, player, eventKind).Count > 0;
    }

    private static PendingRerollContext ActionRerollContext(MatchState preRollMatch, PitchSquare destination)
    {
        return new PendingRerollContext
        {
            MatchBeforeRoll = preRollMatch,
            Action = PlayerTurnAction.Special,
            Destination = destination,
            Path = [],
            StepIndex = 0,
            MovementAllowance = 0
        };
    }

    private MatchState CreatePendingActionReroll(
        MatchState preRollMatch,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PendingRerollKind kind,
        GameEventKind eventKind,
        int roll,
        int target,
        PendingRerollContext context,
        string failMessage)
    {
        return preRollMatch with
        {
            PendingReroll = new PendingRerollChoice
            {
                TeamId = team.Id,
                PlayerId = player.Id,
                Kind = kind,
                Roll = roll,
                Target = target,
                TeamRerollAvailable = CanUseTeamReroll(preRollMatch, ruleset, team),
                SkillRerollIds = AvailableProReroll(ruleset, player, eventKind),
                Context = context
            },
            Log = [.. preRollMatch.Log, new MatchLogEntry { Message = failMessage }]
        };
    }

    private MatchState ResolveForcedCatch(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player catcher,
        PendingRerollContext context,
        int catchRoll)
    {
        switch (context.CatchResume)
        {
            case CatchResumeKind.Bounce:
                var bounceOriginal = ResolveBounceTeam(context.BounceOriginalTeamId!.Value, team, opposingTeam);
                var bounceOpposing = context.BounceOpposingTeamId is Guid opposingId
                    ? ResolveBounceTeam(opposingId, team, opposingTeam)
                    : null;
                return BounceBall(
                    match,
                    ruleset,
                    bounceOriginal,
                    context.Destination,
                    context.BounceAllowDivingCatch,
                    bounceOpposing,
                    context.BounceCount,
                    forcedCatchRoll: catchRoll);

            case CatchResumeKind.HandOff:
                var carrier = FindTeamPlayer(team, context.CatchPasserPlayerId!.Value);
                var receiverPlacement = FindStandingPlacement(match, catcher.Id, team.Id, "receiver");
                return ResolveHandOffCatch(match, ruleset, team, opposingTeam, carrier, catcher, receiverPlacement, catchRoll);

            default:
                var passer = FindTeamPlayer(team, context.CatchPasserPlayerId!.Value);
                return ResolvePassLanding(
                    match,
                    ruleset,
                    team,
                    opposingTeam,
                    passer,
                    intendedReceiver: null,
                    context.Destination,
                    context.CatchPassRangeName!,
                    context.CatchPassRoll,
                    context.CatchPassTarget,
                    catchRoll);
        }
    }

    private LeagueTeam ResolveBounceTeam(Guid teamId, LeagueTeam team, LeagueTeam? opposingTeam)
    {
        if (RegisteredTeam(teamId) is LeagueTeam registered)
        {
            return registered;
        }

        if (team.Id == teamId)
        {
            return team;
        }

        return opposingTeam?.Id == teamId
            ? opposingTeam
            : throw new InvalidOperationException("Unable to resolve the team for a bounce-catch reroll.");
    }

    public MatchState ResolvePendingDivingTackle(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam dodgingTeam,
        LeagueTeam tacklerTeam,
        bool useDivingTackle)
    {
        var pending = match.PendingDivingTackle
            ?? throw new InvalidOperationException("There is no pending Diving Tackle choice.");

        if (pending.DodgingTeamId != dodgingTeam.Id || pending.TacklerTeamId != tacklerTeam.Id)
        {
            throw new InvalidOperationException("Pending Diving Tackle choice belongs to different teams.");
        }

        var context = pending.Context;
        var baseMatch = context.MatchBeforeRoll with { PendingDivingTackle = null };
        var dodger = FindTeamPlayer(dodgingTeam, pending.DodgerPlayerId);
        var tackler = FindTeamPlayer(tacklerTeam, pending.TacklerPlayerId);

        if (pending.IsLeap)
        {
            if (useDivingTackle)
            {
                var leapTacklerPlacement = FindPlacement(baseMatch, pending.TacklerPlayerId)
                    ?? throw new InvalidOperationException("Diving Tackler is not part of this match.");
                var leapTackledMatch = ApplyDivingTackle(baseMatch, leapTacklerPlacement, pending.DodgerSquare, tackler.Name);
                // The -2 modifier turns the previously successful leap into a fall.
                return ResolveFailedDodge(leapTackledMatch, ruleset, dodgingTeam, dodger, pending.Destination, pending.Roll, pending.TargetWithDivingTackle, context.ArmBarApplies);
            }

            var leapDeclinedMatch = baseMatch with
            {
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"{tackler.Name} declines Diving Tackle; {dodger.Name} leaps to {pending.Destination.X},{pending.Destination.Y}: rolled {pending.Roll} vs {pending.TargetWithoutDivingTackle}+, success." }
                ]
            };

            return FinalizeSuccessfulLeap(leapDeclinedMatch, ruleset, dodgingTeam, dodger, pending.Destination);
        }

        if (useDivingTackle)
        {
            var tacklerPlacement = FindPlacement(baseMatch, pending.TacklerPlayerId)
                ?? throw new InvalidOperationException("Diving Tackler is not part of this match.");
            var tackledMatch = ApplyDivingTackle(baseMatch, tacklerPlacement, pending.DodgerSquare, tackler.Name);
            return CreatePendingMovementReroll(
                tackledMatch,
                ruleset,
                dodgingTeam,
                dodger,
                PendingRerollKind.Dodge,
                pending.Roll,
                pending.TargetWithDivingTackle,
                context.Action,
                context.Destination,
                context.Path,
                context.StepIndex,
                context.MovementAllowance,
                tacklerTeam,
                context.BreakTackleUsed,
                context.ArmBarApplies,
                context.GoForItNumber,
                context.BlitzDefenderPlayerId);
        }

        var movedMatch = baseMatch with
        {
            Placements = baseMatch.Placements
                .Select(placement => placement.PlayerId == dodger.Id
                    ? placement with { Square = pending.Destination }
                    : placement)
                .ToArray(),
            Log =
            [
                .. baseMatch.Log,
                new MatchLogEntry { Message = $"{tackler.Name} declines Diving Tackle; {dodger.Name} dodges from {pending.DodgerSquare.X},{pending.DodgerSquare.Y} to {pending.Destination.X},{pending.Destination.Y}: rolled {pending.Roll} vs {pending.TargetWithoutDivingTackle}+, success." }
            ]
        };

        if (movedMatch.Ball.CarrierPlayerId is null && movedMatch.Ball.Square == pending.Destination)
        {
            var pickupMatch = ResolvePickup(
                movedMatch,
                ruleset,
                dodgingTeam,
                dodger,
                pending.Destination,
                context.Action,
                context.Destination,
                context.Path,
                context.StepIndex,
                context.MovementAllowance,
                context.BlitzDefenderPlayerId);
            if (pickupMatch.Ball.CarrierPlayerId != dodger.Id)
            {
                return pickupMatch;
            }

            movedMatch = pickupMatch;
        }

        return ContinueMovementFromStep(
            movedMatch,
            ruleset,
            dodgingTeam,
            tacklerTeam,
            dodger,
            context.Action,
            context.Destination,
            context.Path,
            context.StepIndex + 1,
            context.MovementAllowance,
            context.GoForItNumber,
            context.BreakTackleUsed,
            context.BlitzDefenderPlayerId);
    }
    public MatchState ResolvePendingApothecary(MatchState match, LeagueTeam team, bool useApothecary)
    {
        var pending = match.PendingApothecary
            ?? throw new InvalidOperationException("There is no pending apothecary choice.");

        if (pending.TeamId != team.Id)
        {
            throw new InvalidOperationException("Pending apothecary choice belongs to another team.");
        }

        if (!useApothecary)
        {
            return match with
            {
                PendingApothecary = null,
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{team.Name} declines to use an apothecary." }
                ]
            };
        }

        if (TeamApothecariesRemaining(match, team.Id) <= 0)
        {
            throw new InvalidOperationException($"{team.Name} has no apothecary available.");
        }

        var player = FindTeamPlayer(team, pending.PlayerId);
        var rerolledCasualtyRoll = RollD16();
        var rerolledCasualty = new CasualtyRoll
        {
            Roll = rerolledCasualtyRoll,
            Result = ResolveCasualty(rerolledCasualtyRoll)
        };
        var chosen = CasualtySeverity(rerolledCasualty.Result) < CasualtySeverity(pending.OriginalCasualty.Result)
            ? rerolledCasualty
            : pending.OriginalCasualty;
        var finalState = chosen.Result == CasualtyResult.Dead ? PlayerPitchState.Dead : PlayerPitchState.Casualty;
        var spentMatch = SpendApothecary(match, team.Id);

        return spentMatch with
        {
            PendingApothecary = null,
            Placements = spentMatch.Placements
                .Select(placement => placement.PlayerId == pending.PlayerId
                    ? placement with
                    {
                        State = finalState,
                        Square = null,
                        Casualty = chosen,
                        StunnedRecoveryHalf = null,
                        StunnedRecoveryTurn = null
                    }
                    : placement)
                .ToArray(),
            Log =
            [
                .. spentMatch.Log,
                new MatchLogEntry { Message = $"{team.Name} uses an apothecary for {player.Name}: rolled {rerolledCasualty.Roll}, {FormatCasualtyResult(rerolledCasualty.Result)}; final result {FormatCasualtyResult(chosen.Result)}." }
            ]
        };
    }
    private static int TeamRerollsRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeRerollsRemaining : match.AwayRerollsRemaining;
    }
    private static bool CanContinuePendingMultipleBlock(MatchState match, Guid attackerTeamId)
    {
        return match.PendingMultipleBlock is not null &&
            match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn &&
            match.ActiveTeamId == attackerTeamId &&
            match.PendingBlock is null &&
            match.PendingBlockReroll is null &&
            match.PendingPush is null &&
            match.PendingStandFirm is null &&
            match.PendingFollowUp is null &&
            match.PendingBallPlacement is null &&
            match.PendingReroll is null &&
            match.PendingApothecary is null &&
            match.PendingInterception is null &&
            match.PendingKickoffEvent is null;
    }
    private static bool CanUseTeamReroll(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        return EffectiveTeamRerollsRemaining(match, ruleset, team) > 0 &&
            !match.TeamRerollUses.Any(use =>
                use.TeamId == team.Id &&
                use.Half == match.Half &&
                use.Turn == match.Turn);
    }

    private static int EffectiveTeamRerollsRemaining(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        var standardRerolls = TeamRerollsRemaining(match, team.Id);
        var leaderReroll = LeaderRerollAvailable(match, ruleset, team) ? 1 : 0;
        return standardRerolls + leaderReroll;
    }

    private static bool LeaderRerollAvailable(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        var availableFlag = team.Id == match.HomeTeamId
            ? match.HomeLeaderRerollAvailable
            : match.AwayLeaderRerollAvailable;

        return availableFlag && HasLeaderOnPitch(match, ruleset, team);
    }

    private static MatchState SpendTeamReroll(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        var nextUses = match.TeamRerollUses
            .Append(new TeamRerollUse { TeamId = team.Id, Half = match.Half, Turn = match.Turn })
            .ToArray();

        if (team.Id == match.HomeTeamId)
        {
            if (match.HomeRerollsRemaining > 0)
            {
                return match with
                {
                    HomeRerollsRemaining = match.HomeRerollsRemaining - 1,
                    TeamRerollUses = nextUses
                };
            }

            if (LeaderRerollAvailable(match, ruleset, team))
            {
                return match with
                {
                    HomeLeaderRerollAvailable = false,
                    TeamRerollUses = nextUses
                };
            }
        }

        if (match.AwayRerollsRemaining > 0)
        {
            return match with
            {
                AwayRerollsRemaining = match.AwayRerollsRemaining - 1,
                TeamRerollUses = nextUses
            };
        }

        if (LeaderRerollAvailable(match, ruleset, team))
        {
            return match with
            {
                AwayLeaderRerollAvailable = false,
                TeamRerollUses = nextUses
            };
        }

        throw new InvalidOperationException($"{team.Name} has no team rerolls available.");
    }
    private static int TeamBribesRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeBribesRemaining : match.AwayBribesRemaining;
    }

    private static MatchState SpendBribe(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeBribesRemaining = Math.Max(0, match.HomeBribesRemaining - 1) }
            : match with { AwayBribesRemaining = Math.Max(0, match.AwayBribesRemaining - 1) };
    }

    private static MatchState SendOffPlayer(MatchState match, Guid playerId, string message)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == playerId
                    ? placement with { Square = null, State = PlayerPitchState.SentOff, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Ball = match.Ball.CarrierPlayerId == playerId ? new BallState() : match.Ball,
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    private static int TeamApothecariesRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeApothecariesRemaining : match.AwayApothecariesRemaining;
    }

    private static MatchState SpendApothecary(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeApothecariesRemaining = Math.Max(0, match.HomeApothecariesRemaining - 1) }
            : match with { AwayApothecariesRemaining = Math.Max(0, match.AwayApothecariesRemaining - 1) };
    }

    private static ApothecaryResolution CreatePendingApothecaryIfAvailable(MatchState match, PlayerPlacement placement, string playerName, InjuryResolution injury)
    {
        if (injury.Casualty is null || TeamApothecariesRemaining(match, placement.TeamId) <= 0)
        {
            return new ApothecaryResolution(match, injury, []);
        }

        return new ApothecaryResolution(
            match with
            {
                PendingApothecary = new PendingApothecaryChoice
                {
                    TeamId = placement.TeamId,
                    PlayerId = placement.PlayerId,
                    OriginalCasualty = injury.Casualty
                }
            },
            injury,
            [
                new MatchLogEntry { Message = $"{playerName} may use an apothecary on {FormatCasualtyResult(injury.Casualty.Result)}." }
            ]);
    }
    private static int TeamKickoffStaff(MatchState match, Guid teamId, bool cheerleaders)
    {
        if (teamId == match.HomeTeamId)
        {
            return cheerleaders ? match.HomeCheerleaders : match.HomeAssistantCoaches;
        }

        return cheerleaders ? match.AwayCheerleaders : match.AwayAssistantCoaches;
    }

    private static int TeamFame(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeFame : match.AwayFame;
    }
    private static IReadOnlyList<string> AvailableSkillRerolls(Ruleset ruleset, Player player, PendingRerollKind kind)
    {
        var (eventKind, effect) = kind switch
        {
            PendingRerollKind.Dodge => (GameEventKind.DodgeRoll, SkillEffect.DodgeReroll),
            PendingRerollKind.Pickup => (GameEventKind.PickupRoll, SkillEffect.PickupReroll),
            PendingRerollKind.GoForIt => (GameEventKind.GoForItRoll, SkillEffect.GoForItReroll),
            PendingRerollKind.Pass => (GameEventKind.PassRoll, SkillEffect.PassReroll),
            _ => throw new InvalidOperationException("Unknown reroll kind.")
        };

        var rerolls = SkillHookResolver
            .SkillIdsForHookedEffect(ruleset, player, eventKind, GameEventStage.AfterRoll, effect)
            .ToArray();
        if (SkillHookResolver.PlayerHasHookedEffect(ruleset, player, eventKind, GameEventStage.AfterRoll, SkillEffect.Pro))
        {
            rerolls = [.. rerolls, "pro"];
        }

        return rerolls;
    }
    private static GameEventKind PendingRerollEventKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.Dodge => GameEventKind.DodgeRoll,
            PendingRerollKind.Pickup => GameEventKind.PickupRoll,
            PendingRerollKind.GoForIt => GameEventKind.GoForItRoll,
            PendingRerollKind.Pass => GameEventKind.PassRoll,
            _ => throw new InvalidOperationException("Unknown reroll kind.")
        };
    }
    private static string FormatRerollKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.GoForIt => "go-for-it",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}
