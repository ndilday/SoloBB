using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState BlockPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam defenderTeam,
        Guid defenderPlayerId)
    {
        var attacker = FindTeamPlayer(attackerTeam, attackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId)
            ?? throw new InvalidOperationException("Attacker is not part of this match.");
        if (attackerPlacement.State == PlayerPitchState.Prone &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeEvent, SkillEffect.JumpUp))
        {
            var defenderPlacement = FindStandingPlacement(match, defenderPlayerId, defenderTeam.Id, "defender");
            if (!PlacementsAreAdjacent(attackerPlacement, defenderPlacement))
            {
                throw new InvalidOperationException("Blocks require adjacent players.");
            }

            var jumpUpAction = BeginPlayerAction(match, ruleset, attackerTeam, attacker, PlayerTurnAction.Block, goForItsUsed: 0);
            if (jumpUpAction.Prevented)
            {
                return jumpUpAction.Match;
            }

            return ResolveJumpUpRoll(jumpUpAction.Match, ruleset, attackerTeam, defenderTeam, attacker, defenderPlayerId);
        }

        attackerPlacement = ValidateBlock(match, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);
        var blockAction = BeginPlayerAction(match, ruleset, attackerTeam, attacker, PlayerTurnAction.Block, goForItsUsed: 0);
        if (blockAction.Prevented)
        {
            return blockAction.Match;
        }

        var activatedMatch = blockAction.Match;
        var foulAppearance = ResolveFoulAppearance(activatedMatch, ruleset, attacker, defender);
        if (foulAppearance.BlockPrevented)
        {
            return foulAppearance.Match;
        }

        return ResolveBlock(activatedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
    }

    /// <summary>
    /// Resolves a Dump-Off interrupt. Passing a <paramref name="targetSquare"/> throws the carrier's Quick
    /// Pass; passing null declines it. The held block resumes once the pass (and anything it spawns) settles.
    /// </summary>
    public MatchState ResolvePendingDumpOff(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam carrierTeam,
        LeagueTeam blockingTeam,
        PitchSquare? targetSquare,
        bool usePassSkillReroll = false,
        bool useCloudBurster = false)
    {
        var pending = match.PendingDumpOff
            ?? throw new InvalidOperationException("There is no pending Dump-Off choice.");

        if (pending.CarrierTeamId != carrierTeam.Id || pending.BlockingTeamId != blockingTeam.Id)
        {
            throw new InvalidOperationException("Pending Dump-Off teams do not match the selected teams.");
        }

        var block = pending.Block;
        var cleared = match with { PendingDumpOff = null };

        if (targetSquare is null)
        {
            var carrier = FindTeamPlayer(carrierTeam, pending.CarrierPlayerId);
            var declined = cleared with
            {
                Log = [.. cleared.Log, new MatchLogEntry { Message = $"{carrier.Name} declines Dump-Off." }]
            };
            return ResumeBlock(declined, ruleset, blockingTeam, carrierTeam, block);
        }

        var passedMatch = DumpOffPassBall(cleared, ruleset, carrierTeam, pending.CarrierPlayerId, targetSquare, blockingTeam, usePassSkillReroll, useCloudBurster);

        // If the Quick Pass spawned its own choice (interception or reroll), hold the block until that
        // settles; otherwise resolve it now.
        return HasActionPending(passedMatch)
            ? passedMatch with { DeferredBlock = block }
            : ResumeBlock(passedMatch, ruleset, blockingTeam, carrierTeam, block);
    }

    /// <summary>
    /// Re-enters a block held behind an interrupt. Skips the Dump-Off check (already offered) and no-ops if
    /// the attacker can no longer make the block (e.g. they were removed while the pass resolved).
    /// </summary>
    private MatchState ResumeBlock(MatchState match, Ruleset ruleset, LeagueTeam attackerTeam, LeagueTeam defenderTeam, DeferredBlock block)
    {
        var cleared = match with { DeferredBlock = null };
        var attackerPlacement = cleared.Placements.FirstOrDefault(placement => placement.PlayerId == block.AttackerPlayerId);
        var defenderPlacement = cleared.Placements.FirstOrDefault(placement => placement.PlayerId == block.DefenderPlayerId);
        if (attackerPlacement is null || attackerPlacement.State != PlayerPitchState.Standing ||
            defenderPlacement is null || !OccupiesPitch(defenderPlacement.State))
        {
            return cleared;
        }

        var attacker = FindTeamPlayer(attackerTeam, block.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, block.DefenderPlayerId);
        return ResolveBlock(cleared, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender, block.DefenderStrengthBonus, block.PreventFollowUp, skipDumpOff: true);
    }

    /// <summary>
    /// Resumes a block deferred behind a Dump-Off pass once that pass has fully settled. A no-op when there
    /// is no deferred block or another choice is still outstanding. The two supplied teams are matched to
    /// the stored attacker/defender ids in either order.
    /// </summary>
    private MatchState ResumeDeferredBlock(MatchState match, Ruleset ruleset, LeagueTeam teamA, LeagueTeam? teamB)
    {
        if (match.DeferredBlock is not DeferredBlock block || HasActionPending(match))
        {
            return match;
        }

        var attackerTeam = teamA.Id == block.AttackerTeamId ? teamA : teamB;
        var defenderTeam = teamA.Id == block.DefenderTeamId ? teamA : teamB;
        if (attackerTeam is null || defenderTeam is null)
        {
            return match;
        }

        return ResumeBlock(match, ruleset, attackerTeam, defenderTeam, block);
    }

    /// <summary>
    /// True when an action-level choice (interception, reroll, ball placement) is still outstanding and must
    /// be resolved before a deferred block can resume.
    /// </summary>
    private static bool HasActionPending(MatchState match)
    {
        return match.PendingInterception is not null ||
            match.PendingReroll is not null ||
            match.PendingBallPlacement is not null ||
            match.PendingBombThrow is not null;
    }

    /// <summary>
    /// Resolves the Jump Up agility roll a prone player makes to stand and block, offering a team/Pro
    /// reroll on a failure before the attempt fizzles.
    /// </summary>
    private MatchState ResolveJumpUpRoll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        Player attacker,
        Guid defenderPlayerId,
        int? forcedRoll = null)
    {
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
        var defenderPlacement = FindStandingPlacement(match, defenderPlayerId, defenderTeam.Id, "defender");
        var jumpUpRoll = forcedRoll ?? _dice.RollD6();
        var jumpUpTarget = Math.Clamp(attacker.Stats.Agility + 1, 2, 6);
        if (!RollSucceeds(jumpUpRoll, jumpUpTarget, ruleset.Dice))
        {
            if (forcedRoll is null && ActionRerollOffered(match, ruleset, attackerTeam, attacker, GameEventKind.BlockRoll))
            {
                return CreatePendingActionReroll(
                    match, ruleset, attackerTeam, attacker, PendingRerollKind.JumpUp, GameEventKind.BlockRoll, jumpUpRoll, jumpUpTarget,
                    ActionRerollContext(match, defenderPlacement.Square!) with { SecondaryPlayerId = defenderPlayerId },
                    $"{attacker.Name} attempts a Jump Up block: rolled {jumpUpRoll} vs {jumpUpTarget}+, failed. Choose whether to reroll.");
            }

            return match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} attempts a Jump Up block: rolled {jumpUpRoll} vs {jumpUpTarget}+, failed and remains prone." }
                ]
            };
        }

        var stoodMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == attacker.Id
                    ? placement with { State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} attempts a Jump Up block: rolled {jumpUpRoll} vs {jumpUpTarget}+, success." }
            ]
        };

        var attackerPlacement = stoodMatch.Placements.First(placement => placement.PlayerId == attacker.Id);
        var jumpUpFoulAppearance = ResolveFoulAppearance(stoodMatch, ruleset, attacker, defender);
        if (jumpUpFoulAppearance.BlockPrevented)
        {
            return jumpUpFoulAppearance.Match;
        }

        return ResolveBlock(stoodMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
    }

    /// <summary>
    /// Re-enters a block with a forced Dauntless roll after the player has chosen to reroll (or declined),
    /// resuming just before the block dice are rolled.
    /// </summary>
    private MatchState ResolveDauntlessRoll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        Player attacker,
        Guid defenderPlayerId,
        int defenderStrengthBonus,
        bool preventFollowUp,
        int forcedRoll)
    {
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
        var attackerPlacement = match.Placements.First(placement => placement.PlayerId == attacker.Id);
        return ResolveBlock(match, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender, defenderStrengthBonus, preventFollowUp, forcedRoll);
    }

    public MatchState MultipleBlockPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam defenderTeam,
        Guid firstDefenderPlayerId,
        Guid secondDefenderPlayerId)
    {
        if (firstDefenderPlayerId == secondDefenderPlayerId)
        {
            throw new InvalidOperationException("Multiple Block requires two different defenders.");
        }

        var attacker = FindTeamPlayer(attackerTeam, attackerPlayerId);
        if (!PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeEvent, SkillEffect.MultipleBlock))
        {
            throw new InvalidOperationException($"{attacker.Name} does not have Multiple Block.");
        }

        var firstDefender = FindTeamPlayer(defenderTeam, firstDefenderPlayerId);
        var secondDefender = FindTeamPlayer(defenderTeam, secondDefenderPlayerId);
        var attackerPlacement = ValidateBlock(match, attackerTeam, attackerPlayerId, defenderTeam, firstDefenderPlayerId);
        _ = ValidateBlock(match, attackerTeam, attackerPlayerId, defenderTeam, secondDefenderPlayerId);

        var action = BeginPlayerAction(match, ruleset, attackerTeam, attacker, PlayerTurnAction.Block, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var activatedMatch = action.Match with
        {
            PendingMultipleBlock = new PendingMultipleBlockContinuation
            {
                AttackerTeamId = attackerTeam.Id,
                DefenderTeamId = defenderTeam.Id,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = secondDefender.Id
            },
            Log =
            [
                .. action.Match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} uses Multiple Block against {firstDefender.Name} and {secondDefender.Name}." }
            ]
        };

        var foulAppearance = ResolveFoulAppearance(activatedMatch, ruleset, attacker, firstDefender);
        if (foulAppearance.BlockPrevented)
        {
            return foulAppearance.Match with { PendingMultipleBlock = null };
        }

        var resolvedFirst = ResolveBlock(
            foulAppearance.Match,
            ruleset,
            attackerTeam,
            attacker,
            attackerPlacement,
            defenderTeam,
            firstDefender,
            defenderStrengthBonus: 2,
            preventFollowUp: true);

        return CanContinuePendingMultipleBlock(resolvedFirst, attackerTeam.Id)
            ? ContinueMultipleBlock(resolvedFirst, ruleset, attackerTeam, defenderTeam)
            : resolvedFirst;
    }

    public MatchState ContinueMultipleBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam)
    {
        var pending = match.PendingMultipleBlock
            ?? throw new InvalidOperationException("There is no pending Multiple Block continuation.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending Multiple Block teams do not match the selected teams.");
        }

        if (!CanContinuePendingMultipleBlock(match, attackerTeam.Id))
        {
            throw new InvalidOperationException("Resolve other pending choices before continuing Multiple Block.");
        }

        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attackerPlacement = ValidateBlock(match, attackerTeam, attacker.Id, defenderTeam, defender.Id);
        var baseMatch = match with { PendingMultipleBlock = null };
        var foulAppearance = ResolveFoulAppearance(baseMatch, ruleset, attacker, defender);
        if (foulAppearance.BlockPrevented)
        {
            return foulAppearance.Match;
        }

        return ResolveBlock(
            foulAppearance.Match,
            ruleset,
            attackerTeam,
            attacker,
            attackerPlacement,
            defenderTeam,
            defender,
            defenderStrengthBonus: 2,
            preventFollowUp: true);
    }

    public MatchState ChooseBlockDie(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        int roll)
    {
        var pending = match.PendingBlock
            ?? throw new InvalidOperationException("There is no pending block choice.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending block teams do not match the selected teams.");
        }

        if (!pending.Rolls.Contains(roll))
        {
            throw new InvalidOperationException($"Roll {roll} is not available for this block.");
        }

        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attackerPlacement = match.Placements.First(placement => placement.PlayerId == pending.AttackerPlayerId);
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == pending.DefenderPlayerId);
        var strength = new BlockStrength(pending.AttackerStrength, pending.DefenderStrength, pending.Rolls.Count);

        return ResolveChosenBlockDie(
            match with { PendingBlock = null },
            ruleset,
            attackerTeam,
            attacker,
            attackerPlacement,
            defenderTeam,
            defender,
            defenderPlacement,
            strength,
            pending.Rolls,
            roll,
            pending.PreventFollowUp,
            allowTeamReroll: !pending.AlreadyRerolled);
    }

    public MatchState RerollPendingBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam)
    {
        var pending = match.PendingBlock
            ?? throw new InvalidOperationException("There is no pending block choice.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending block teams do not match the selected teams.");
        }

        if (pending.AlreadyRerolled)
        {
            throw new InvalidOperationException("These block dice have already been rerolled and cannot be rerolled again.");
        }

        if (!CanUseTeamReroll(match, ruleset, attackerTeam))
        {
            throw new InvalidOperationException($"{attackerTeam.Name} has no team rerolls available.");
        }

        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attackerPlacement = match.Placements.First(placement => placement.PlayerId == pending.AttackerPlayerId);
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == pending.DefenderPlayerId);
        var strength = new BlockStrength(pending.AttackerStrength, pending.DefenderStrength, pending.Rolls.Count);

        var spentMatch = SpendTeamReroll(match with { PendingBlock = null }, ruleset, attackerTeam);
        var rerolledRolls = Enumerable.Range(0, pending.Rolls.Count).Select(_ => _dice.RollD6()).ToArray();
        spentMatch = spentMatch with
        {
            Log =
            [
                .. spentMatch.Log,
                new MatchLogEntry { Message = $"{attackerTeam.Name} uses a team reroll: block dice rerolled from {string.Join(", ", pending.Rolls)} to {string.Join(", ", rerolledRolls)}." }
            ]
        };

        return rerolledRolls.Length > 1
            ? spentMatch with
            {
                PendingBlock = new PendingBlockChoice
                {
                    AttackerTeamId = attackerTeam.Id,
                    DefenderTeamId = defenderTeam.Id,
                    AttackerPlayerId = attacker.Id,
                    DefenderPlayerId = defender.Id,
                    Rolls = rerolledRolls,
                    AttackerStrength = pending.AttackerStrength,
                    DefenderStrength = pending.DefenderStrength,
                    PreventFollowUp = pending.PreventFollowUp,
                    AlreadyRerolled = true
                }
            }
            : ResolveChosenBlockDie(
                spentMatch,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                strength,
                rerolledRolls,
                rerolledRolls[0],
                pending.PreventFollowUp,
                allowTeamReroll: false);
    }

    public MatchState ChoosePushSquare(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        PitchSquare square)
    {
        var pending = match.PendingPush
            ?? throw new InvalidOperationException("There is no pending push choice.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending push teams do not match the selected teams.");
        }

        if (!pending.LegalSquares.Contains(square))
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is not a legal push square.");
        }

        if (pending.Continuation is PendingPushContinuation continuation)
        {
            var continuationBaseMatch = match with { PendingPush = null };
            var chainPushedName = PlayerName(pending.DefenderPlayerId);
            var deferChainPush = IsOnPitch(ruleset, square);
            var chainPushedMatch = deferChainPush
                ? MovePushedPlayer(continuationBaseMatch, pending.DefenderPlayerId, square)
                : PushPlacement(continuationBaseMatch, ruleset, null, pending.DefenderPlayerId, chainPushedName, pending.DefenderSquare, square, knockDown: false, () => new InjuryResolution(PlayerPitchState.Standing), stripBall: false);
            IReadOnlyList<PendingChainPushResolution>? deferredChainPushes = deferChainPush
                ?
                [
                    new PendingChainPushResolution
                    {
                        PlayerId = pending.DefenderPlayerId,
                        PlayerName = chainPushedName,
                        Source = pending.DefenderSquare,
                        Destination = square
                    }
                ]
                : null;
            var continuedDefender = FindTeamPlayer(defenderTeam, continuation.PlayerId);
            var continuedAttacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
            var continuedStripBall = ShouldStripBall(ruleset, continuedAttacker, continuedDefender, chainPushedMatch.Ball.CarrierPlayerId == continuedDefender.Id, continuation.KnockDown);
            var continuedMovedMatch = MovePushedPlayer(chainPushedMatch, continuation.PlayerId, continuation.Destination);
            var continuedLoggedMatch = continuedMovedMatch with
            {
                Log =
                [
                    .. continuedMovedMatch.Log,
                    new MatchLogEntry { Message = $"{pending.ResultMessage} {chainPushedName} is chain-pushed to {square.X},{square.Y}." },
                    new MatchLogEntry { Message = $"{continuation.ResultMessage} {continuedDefender.Name} is pushed to {continuation.Destination.X},{continuation.Destination.Y}." }
                ]
            };

            return CompleteBlockPush(
                continuedLoggedMatch,
                ruleset,
                attackerTeam,
                continuedAttacker,
                defenderTeam,
                continuedDefender,
                continuation.Source,
                continuation.Destination,
                continuation.KnockDown,
                continuedStripBall,
                pending.PreventFollowUp,
                deferredChainPushes);
        }

        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var occupant = FindPushOccupant(match, square, pending.DefenderPlayerId);
        if (occupant is not null)
        {
            var chainSquares = LegalPushSquares(match, ruleset, pending.DefenderSquare, square, occupant.PlayerId);
            if (chainSquares.Length > 1)
            {
                return match with
                {
                    PendingPush = pending with
                    {
                        DefenderPlayerId = occupant.PlayerId,
                        DefenderSquare = square,
                        LegalSquares = chainSquares,
                        KnockDefenderDown = false,
                        ResultMessage = $"{pending.ResultMessage} Choose where to chain-push {PlayerName(occupant.PlayerId)}.",
                        Continuation = new PendingPushContinuation
                        {
                            PlayerId = defender.Id,
                            Source = pending.DefenderSquare,
                            Destination = square,
                            KnockDown = pending.KnockDefenderDown,
                            ResultMessage = pending.ResultMessage
                        }
                    },
                    Log =
                    [
                        .. match.Log,
                        new MatchLogEntry { Message = $"{pending.ResultMessage} Choose where to chain-push {PlayerName(occupant.PlayerId)}." }
                    ]
                };
            }
        }
        var baseMatch = match with { PendingPush = null };
        var stripBall = ShouldStripBall(ruleset, attacker, defender, baseMatch.Ball.CarrierPlayerId == defender.Id, pending.KnockDefenderDown);
        var pushedMatch = IsOnPitch(ruleset, square)
            ? MovePushedPlayer(baseMatch, defender.Id, square)
            : baseMatch;
        var pushMessage = IsOnPitch(ruleset, square)
            ? $"{pending.ResultMessage} {defender.Name} is pushed to {square.X},{square.Y}."
            : $"{pending.ResultMessage} {defender.Name} is pushed off the pitch into the crowd.";
        var loggedMatch = pushedMatch with
        {
            Log =
            [
                .. pushedMatch.Log,
                new MatchLogEntry { Message = pushMessage }
            ]
        };

        return CompleteBlockPush(
            loggedMatch,
            ruleset,
            attackerTeam,
            attacker,
            defenderTeam,
            defender,
            pending.DefenderSquare,
            square,
            pending.KnockDefenderDown,
            stripBall,
            pending.PreventFollowUp);
    }

    public MatchState ResolvePendingFollowUp(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        bool useFollowUp)
    {
        var pending = match.PendingFollowUp
            ?? throw new InvalidOperationException("There is no pending follow-up choice.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending follow-up teams do not match the selected teams.");
        }

        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var baseMatch = match with { PendingFollowUp = null };
        if (!useFollowUp)
        {
            var declinedMatch = baseMatch with
            {
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} does not follow up." }
                ]
            };

            return pending.DeferredBlockPush is PendingBlockPushResolution declinedPush
                ? CompleteBlockPushAfterConsequences(
                    ResolveBlockPushConsequences(declinedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, declinedPush),
                    attackerTeam,
                    attacker,
                    defenderTeam,
                    defender)
                : CompleteBlockActivationIfDone(declinedMatch, attacker.Id, attackerTeam.Id);
        }

        var followedMatch = MoveAttackerToFollowUpSquare(baseMatch, attacker, pending.FollowUpSquare);
        return pending.DeferredBlockPush is PendingBlockPushResolution followedPush
            ? CompleteBlockPushAfterConsequences(
                ResolveBlockPushConsequences(followedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, followedPush),
                attackerTeam,
                attacker,
                defenderTeam,
                defender)
            : CompleteBlockActivationIfDone(followedMatch, attacker.Id, attackerTeam.Id);
    }

    public MatchState ChooseBallPlacement(MatchState match, LeagueTeam team, PitchSquare square)
    {
        var pending = match.PendingBallPlacement
            ?? throw new InvalidOperationException("There is no pending ball placement choice.");

        if (pending.TeamId != team.Id)
        {
            throw new InvalidOperationException("Pending ball placement belongs to another team.");
        }

        if (!pending.LegalSquares.Contains(square))
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is not a legal ball placement square.");
        }

        var player = FindTeamPlayer(team, pending.PlayerId);
        if (string.Equals(pending.Reason, "Touchback", StringComparison.OrdinalIgnoreCase))
        {
            var receiverPlacement = match.Placements.FirstOrDefault(placement =>
                placement.TeamId == team.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square == square)
                ?? throw new InvalidOperationException("Touchback must be given to a standing receiving player.");
            var receiver = FindTeamPlayer(team, receiverPlacement.PlayerId);
            return match with
            {
                Phase = MatchPhase.OffensivePlayerTurn,
                DriveState = DriveState.InProgress,
                PendingBallPlacement = null,
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                Activations = [],
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"Touchback. {receiver.Name} receives the ball." }
                ]
            };
        }

        return match with
        {
            PendingBallPlacement = null,
            Ball = new BallState { Square = square },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} uses {pending.Reason}; ball is placed at {square.X},{square.Y}." }
            ]
        };
    }
    public MatchState ResolvePendingStandFirm(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        bool useStandFirm)
    {
        var pending = match.PendingStandFirm
            ?? throw new InvalidOperationException("There is no pending Stand Firm choice.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending Stand Firm teams do not match the selected teams.");
        }

        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == pending.DefenderPlayerId);
        var baseMatch = match with { PendingStandFirm = null };

        if (useStandFirm)
        {
            var stoodFirmMatch = pending.KnockDefenderDown
                ? KnockPlayerDown(baseMatch, ruleset, defender, defenderPlacement, ResolveBlockInjury(ruleset, attacker, defender), pending.DefenderSquare)
                : baseMatch;
            stoodFirmMatch = pending.KnockDefenderDown
                ? AwardCasualtyIfCaused(stoodFirmMatch, attackerTeam, attacker, defenderTeam, defender.Id)
                : stoodFirmMatch;

            return stoodFirmMatch with
            {
                Log =
                [
                    .. stoodFirmMatch.Log,
                    new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} uses Stand Firm and is not pushed." }
                ]
            };
        }

        if (pending.LegalSquares.Count == 0)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, baseMatch.Ball.CarrierPlayerId == defender.Id, pending.KnockDefenderDown);
            var crowdSquare = CrowdPushDestination(pending.DefenderSquare);
            var loggedMatch = baseMatch with
            {
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} declines Stand Firm. No legal push square is available; {defender.Name} is pushed into the crowd." }
                ]
            };

            return CompleteBlockPush(
                loggedMatch,
                ruleset,
                attackerTeam,
                attacker,
                defenderTeam,
                defender,
                pending.DefenderSquare,
                crowdSquare,
                pending.KnockDefenderDown,
                stripBall,
                pending.PreventFollowUp);
        }

        if (pending.LegalSquares.Count == 1)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, baseMatch.Ball.CarrierPlayerId == defender.Id, pending.KnockDefenderDown);
            var pushedMatch = IsOnPitch(ruleset, pending.LegalSquares[0])
                ? MovePushedPlayer(baseMatch, defender.Id, pending.LegalSquares[0])
                : baseMatch;
            var pushMessage = IsOnPitch(ruleset, pending.LegalSquares[0])
                ? $"{pending.ResultMessage} {defender.Name} declines Stand Firm and is pushed to {pending.LegalSquares[0].X},{pending.LegalSquares[0].Y}."
                : $"{pending.ResultMessage} {defender.Name} declines Stand Firm and is pushed off the pitch into the crowd.";
            var loggedMatch = pushedMatch with
            {
                Log =
                [
                    .. pushedMatch.Log,
                    new MatchLogEntry { Message = pushMessage }
                ]
            };

            return CompleteBlockPush(
                loggedMatch,
                ruleset,
                attackerTeam,
                attacker,
                defenderTeam,
                defender,
                pending.DefenderSquare,
                pending.LegalSquares[0],
                pending.KnockDefenderDown,
                stripBall,
                pending.PreventFollowUp);
        }

        return baseMatch with
        {
            PendingPush = new PendingPushChoice
            {
                AttackerTeamId = pending.AttackerTeamId,
                DefenderTeamId = pending.DefenderTeamId,
                AttackerPlayerId = pending.AttackerPlayerId,
                DefenderPlayerId = pending.DefenderPlayerId,
                DefenderSquare = pending.DefenderSquare,
                LegalSquares = pending.LegalSquares,
                KnockDefenderDown = pending.KnockDefenderDown,
                ResultMessage = $"{pending.ResultMessage} {defender.Name} declines Stand Firm.",
                PreventFollowUp = pending.PreventFollowUp
            },
            Log =
            [
                .. baseMatch.Log,
                new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} declines Stand Firm. Choose a push square." }
            ]
        };
    }

    public MatchState BlitzPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        PitchSquare destination,
        LeagueTeam defenderTeam,
        Guid defenderPlayerId)
    {
        var attacker = FindTeamPlayer(attackerTeam, attackerPlayerId);
        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        var attackerPlacementBeforeMove = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId);
        var defenderPlacementBeforeMove = match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderPlayerId);
        var existingActivation = GetActivation(match, attackerPlayerId, attackerTeam.Id);
        // The blitz may resolve from the attacker's current square when they are already a blitzer, when they
        // have only taken plain Move steps (a lazy blitz that upgrades here), or when they have not activated
        // yet (a standing-adjacent blitz with no movement).
        var canBlitzFromCurrentState = existingActivation is null ||
            existingActivation.Action == PlayerTurnAction.Blitz ||
            existingActivation is { Action: PlayerTurnAction.Move, Completed: false, DeclaredOnly: false };
        var isBlockingFromCurrentSquare =
            canBlitzFromCurrentState &&
            attackerPlacementBeforeMove?.Square == destination &&
            attackerPlacementBeforeMove.State == PlayerPitchState.Standing &&
            defenderPlacementBeforeMove?.Square is not null &&
            defenderPlacementBeforeMove.State == PlayerPitchState.Standing &&
            PlacementsAreAdjacent(attackerPlacementBeforeMove, defenderPlacementBeforeMove);

        if (isBlockingFromCurrentSquare)
        {
            var blitzAction = BeginPlayerAction(
                match,
                ruleset,
                attackerTeam,
                attacker,
                PlayerTurnAction.Blitz,
                existingActivation?.GoForItsUsed ?? 0,
                existingActivation?.MovementSquaresUsed ?? 0);
            if (blitzAction.Prevented)
            {
                return blitzAction.Match;
            }

            var currentAttackerPlacement = ValidateBlock(blitzAction.Match, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);
            var currentDefender = FindTeamPlayer(defenderTeam, defenderPlayerId);
            var currentFoulAppearance = ResolveFoulAppearance(blitzAction.Match, ruleset, attacker, currentDefender);
            if (currentFoulAppearance.BlockPrevented)
            {
                return currentFoulAppearance.Match;
            }

            return ResolveBlock(blitzAction.Match, ruleset, attackerTeam, attacker, currentAttackerPlacement, defenderTeam, currentDefender);
        }

        var movedMatch = MovePlayerCore(match, ruleset, attackerTeam, attackerPlayerId, destination, PlayerTurnAction.Blitz, defenderTeam, defenderPlayerId);
        if (movedMatch.Phase != match.Phase || movedMatch.ActiveTeamId != match.ActiveTeamId || movedMatch.PendingReroll is not null)
        {
            return movedMatch;
        }

        var attackerPlacement = ValidateBlock(movedMatch, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
        var foulAppearance = ResolveFoulAppearance(movedMatch, ruleset, attacker, defender);
        if (foulAppearance.BlockPrevented)
        {
            return foulAppearance.Match;
        }

        return ResolveBlock(movedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
    }

    private MatchState ResolveBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        Player defender,
        int defenderStrengthBonus = 0,
        bool preventFollowUp = false,
        int? forcedDauntlessRoll = null,
        bool skipDumpOff = false)
    {
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == defender.Id);

        // BB2020 Dump-Off: a standing ball carrier with the skill may throw a Quick Pass before any block
        // dice are rolled. Raise the interrupt on the first roll attempt only (not on Dauntless re-entry).
        if (!skipDumpOff &&
            forcedDauntlessRoll is null &&
            match.PendingMultipleBlock is null &&
            defenderPlacement.State == PlayerPitchState.Standing &&
            match.Ball.CarrierPlayerId == defender.Id &&
            PlayerHasHookedEffect(ruleset, defender, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.DumpOff))
        {
            return match with
            {
                PendingDumpOff = new PendingDumpOffChoice
                {
                    CarrierTeamId = defenderTeam.Id,
                    BlockingTeamId = attackerTeam.Id,
                    CarrierPlayerId = defender.Id,
                    Block = new DeferredBlock
                    {
                        AttackerTeamId = attackerTeam.Id,
                        DefenderTeamId = defenderTeam.Id,
                        AttackerPlayerId = attacker.Id,
                        DefenderPlayerId = defender.Id,
                        DefenderStrengthBonus = defenderStrengthBonus,
                        PreventFollowUp = preventFollowUp
                    }
                },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{defender.Name} may use Dump-Off before {attacker.Name}'s block." }
                ]
            };
        }

        var strength = ResolveBlockStrength(match, ruleset, attackerTeam, attackerPlacement, defenderTeam, defenderPlacement, attacker, defender, defenderStrengthBonus, forcedDauntlessRoll);

        // A failed Dauntless roll may be rerolled before the block dice are rolled. Skip the prompt while a
        // Multiple Block is mid-sequence so its continuation state machine is not interrupted.
        if (forcedDauntlessRoll is null &&
            strength.DauntlessRoll is int dauntlessRoll &&
            !strength.DauntlessReachedStrength &&
            match.PendingMultipleBlock is null &&
            ActionRerollOffered(match, ruleset, attackerTeam, attacker, GameEventKind.BlockRoll))
        {
            return CreatePendingActionReroll(
                match, ruleset, attackerTeam, attacker, PendingRerollKind.Dauntless, GameEventKind.BlockRoll, dauntlessRoll, strength.DauntlessTarget,
                ActionRerollContext(match, defenderPlacement.Square!) with
                {
                    SecondaryPlayerId = defender.Id,
                    DefenderStrengthBonus = defenderStrengthBonus,
                    PreventFollowUp = preventFollowUp
                },
                FormatDauntlessRoll(attacker, defender, strength, " Choose whether to reroll."));
        }

        if (strength.DauntlessRoll is not null)
        {
            match = match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = FormatDauntlessRoll(attacker, defender, strength) }
                ]
            };
        }

        var rolls = Enumerable.Range(0, strength.Dice).Select(_ => _dice.RollD6()).ToArray();
        var attackerAction = GetActivation(match, attacker.Id, attackerTeam.Id)?.Action ?? PlayerTurnAction.Block;
        if (attackerAction == PlayerTurnAction.Block &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.AfterRoll, SkillEffect.Brawler) &&
            rolls.Contains(2))
        {
            var brawlerRoll = _dice.RollD6();
            var replaced = false;
            rolls = rolls
                .Select(roll =>
                {
                    if (roll != 2 || replaced)
                    {
                        return roll;
                    }

                    replaced = true;
                    return brawlerRoll;
                })
                .ToArray();
            match = match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} uses Brawler: one Both Down result is rerolled to {brawlerRoll}." }
                ]
            };
        }

        // Present the roll for the player to confirm, even for a single die, so 1-die
        // blocks display the same way 2-die blocks do.
        return match with
        {
            PendingBlock = new PendingBlockChoice
            {
                AttackerTeamId = attackerTeam.Id,
                DefenderTeamId = defenderTeam.Id,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = defender.Id,
                Rolls = rolls,
                AttackerStrength = strength.AttackerStrength,
                DefenderStrength = strength.DefenderStrength,
                PreventFollowUp = preventFollowUp
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: ST {strength.AttackerStrength}-{strength.DefenderStrength}, rolled {string.Join(", ", rolls)}. {(rolls.Length > 1 ? "Choose block dice." : "Confirm block die.")}" }
            ]
        };
    }

    private MatchState ResolveChosenBlockDie(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        Player defender,
        PlayerPlacement defenderPlacement,
        BlockStrength strength,
        IReadOnlyList<int> rolls,
        int roll,
        bool preventFollowUp = false,
        bool allowTeamReroll = true)
    {
        var rollText = string.Join(", ", rolls);
        var strengthText = $"ST {strength.AttackerStrength}-{strength.DefenderStrength}, block dice {strength.Dice}";
        var attackerAction = GetActivation(match, attacker.Id, attackerTeam.Id)?.Action ?? PlayerTurnAction.Block;

        if (roll == 2 &&
            attackerAction == PlayerTurnAction.Blitz &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.Juggernaut))
        {
            return ResolvePushAfterBlock(
                match,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                knockDefenderDown: false,
                $"{attacker.Name} uses Juggernaut against {defender.Name}: {strengthText}, rolled {rollText}, Both Down becomes pushed back.",
                suppressStandFirm: true,
                preventFollowUp: preventFollowUp);
        }

        if (roll <= 1)
        {
            if (allowTeamReroll && CanUseTeamReroll(match, ruleset, attackerTeam))
            {
                return CreatePendingBlockReroll(
                    match,
                    attackerTeam,
                    attacker,
                    defenderTeam,
                    defender,
                    strength,
                    rolls,
                    roll,
                    preventFollowUp);
            }

            var injuryState = ResolveFallInjury(attacker);
            var knockedDown = KnockPlayerDown(match, ruleset, attacker, attackerPlacement, injuryState, attackerPlacement.Square!);
            return ApplyTurnover(knockedDown with
            {
                Log =
                [
                    .. knockedDown.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, attacker down." }
                ]
            }, ruleset, attackerTeam.Id);
        }

        if (roll == 2)
        {
            var attackerHasWrestle = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.Wrestle);
            var defenderHasWrestle = PlayerHasHookedEffect(ruleset, defender, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.Wrestle);
            if (attackerHasWrestle || defenderHasWrestle)
            {
                var proneMatch = ApplyPlayerPitchState(match, attacker.Id, PlayerPitchState.Prone, attackerPlacement.Square);
                proneMatch = ApplyPlayerPitchState(proneMatch, defender.Id, PlayerPitchState.Prone, defenderPlacement.Square);
                var ball = proneMatch.Ball;
                var looseBallMatch = proneMatch;
                var wrestleLog = new List<MatchLogEntry>();
                if (ball.CarrierPlayerId == attacker.Id || ball.CarrierPlayerId == defender.Id)
                {
                    var dropSquare = ball.CarrierPlayerId == attacker.Id ? attackerPlacement.Square! : defenderPlacement.Square!;
                    var scatterSquare = ScatterFrom(ruleset, dropSquare);
                    var landing = ResolveLooseBall(proneMatch, ruleset, scatterSquare);
                    looseBallMatch = landing.Match with { Log = proneMatch.Log };
                    ball = looseBallMatch.Ball;
                    wrestleLog.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
                    wrestleLog.AddRange(landing.Log);
                }

                var wrestledMatch = looseBallMatch with
                {
                    Ball = ball,
                    Log =
                    [
                        .. looseBallMatch.Log,
                        new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, Wrestle places both players prone." },
                        .. wrestleLog
                    ]
                };

                return match.Ball.CarrierPlayerId == attacker.Id
                    ? ApplyTurnover(wrestledMatch, ruleset, attackerTeam.Id)
                    : CompleteBlockActivationIfDone(wrestledMatch, attacker.Id, attackerTeam.Id);
            }

            var attackerHasBlock = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.BothDownProtection);
            var defenderHasBlock = PlayerHasHookedEffect(ruleset, defender, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.BothDownProtection);

            if (!attackerHasBlock && allowTeamReroll && CanUseTeamReroll(match, ruleset, attackerTeam))
            {
                return CreatePendingBlockReroll(match, attackerTeam, attacker, defenderTeam, defender, strength, rolls, roll, preventFollowUp, resultDescription: "both down");
            }

            var nextMatch = match;
        if (!defenderHasBlock)
        {
            nextMatch = KnockPlayerDown(nextMatch, ruleset, defender, defenderPlacement, ResolveBlockInjury(ruleset, attacker, defender), defenderPlacement.Square!);
            nextMatch = AwardCasualtyIfCaused(nextMatch, attackerTeam, attacker, defenderTeam, defender.Id);
        }

            if (!attackerHasBlock)
            {
                nextMatch = KnockPlayerDown(nextMatch, ruleset, attacker, attackerPlacement, ResolveFallInjury(attacker), attackerPlacement.Square!);
            }

            var resolvedMatch = nextMatch with
            {
                Log =
                [
                    .. nextMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, both down. Block protects {(attackerHasBlock ? attacker.Name : "nobody")}{(defenderHasBlock ? (attackerHasBlock ? $" and {defender.Name}" : defender.Name) : "")}." }
                ]
            };

            return attackerHasBlock
                ? CompleteBlockActivationIfDone(resolvedMatch, attacker.Id, attackerTeam.Id)
                : ApplyTurnover(resolvedMatch, ruleset, attackerTeam.Id);
        }

        if (roll <= 4)
        {
            return ResolvePushAfterBlock(
                match,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                knockDefenderDown: false,
                resultMessage: $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, pushed back.",
                preventFollowUp: preventFollowUp);
        }

        if (roll == 5)
        {
            var defenderHasDodge = PlayerHasHookedEffect(ruleset, defender, GameEventKind.DodgeRoll, GameEventStage.AfterRoll, SkillEffect.DodgeReroll);
            var attackerHasTackle = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.DodgeRoll, GameEventStage.AfterRoll, SkillEffect.CancelDodgeReroll);
            var dodgesStumble = defenderHasDodge && !attackerHasTackle;
            return ResolvePushAfterBlock(
                match,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                knockDefenderDown: !dodgesStumble,
                resultMessage: dodgesStumble
                    ? $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, defender stumbles but uses Dodge to stay up, pushed back."
                    : $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, defender stumbles.",
                preventFollowUp: preventFollowUp);
        }

        return ResolvePushAfterBlock(
            match,
            ruleset,
            attackerTeam,
            attacker,
            attackerPlacement,
            defenderTeam,
            defender,
            defenderPlacement,
            knockDefenderDown: true,
            resultMessage: $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, defender down.",
            preventFollowUp: preventFollowUp);
    }

    private MatchState CreatePendingBlockReroll(
        MatchState match,
        LeagueTeam attackerTeam,
        Player attacker,
        LeagueTeam defenderTeam,
        Player defender,
        BlockStrength strength,
        IReadOnlyList<int> rolls,
        int chosenRoll,
        bool preventFollowUp,
        string resultDescription = "attacker down")
    {
        return match with
        {
            PendingBlockReroll = new PendingBlockRerollChoice
            {
                AttackerTeamId = attackerTeam.Id,
                DefenderTeamId = defenderTeam.Id,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = defender.Id,
                Rolls = rolls,
                ChosenRoll = chosenRoll,
                AttackerStrength = strength.AttackerStrength,
                DefenderStrength = strength.DefenderStrength,
                Dice = strength.Dice,
                TeamRerollAvailable = true,
                PreventFollowUp = preventFollowUp,
                MatchBeforeRoll = match
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: ST {strength.AttackerStrength}-{strength.DefenderStrength}, block dice {strength.Dice}, rolled {string.Join(", ", rolls)}, chose {chosenRoll}, {resultDescription}. Choose whether to reroll." }
            ]
        };
    }

    public MatchState ResolvePendingBlockReroll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        LeagueTeam defenderTeam,
        bool useTeamReroll)
    {
        var pending = match.PendingBlockReroll
            ?? throw new InvalidOperationException("There is no pending block reroll.");

        if (pending.AttackerTeamId != attackerTeam.Id || pending.DefenderTeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Pending block reroll teams do not match the selected teams.");
        }

        var baseMatch = pending.MatchBeforeRoll with { PendingBlockReroll = null };
        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attackerPlacement = baseMatch.Placements.First(placement => placement.PlayerId == attacker.Id);
        var defenderPlacement = baseMatch.Placements.First(placement => placement.PlayerId == defender.Id);
        var strength = new BlockStrength(pending.AttackerStrength, pending.DefenderStrength, pending.Dice);

        if (!useTeamReroll)
        {
            return ResolveChosenBlockDie(
                baseMatch,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                strength,
                pending.Rolls,
                pending.ChosenRoll,
                pending.PreventFollowUp,
                allowTeamReroll: false);
        }

        if (!pending.TeamRerollAvailable || !CanUseTeamReroll(baseMatch, ruleset, attackerTeam))
        {
            throw new InvalidOperationException($"{attackerTeam.Name} has no team rerolls available.");
        }

        var rerolledMatch = SpendTeamReroll(baseMatch, ruleset, attackerTeam);
        var rerolledRolls = Enumerable.Range(0, pending.Dice).Select(_ => _dice.RollD6()).ToArray();
        rerolledMatch = rerolledMatch with
        {
            Log =
            [
                .. rerolledMatch.Log,
                new MatchLogEntry { Message = $"{attackerTeam.Name} uses a team reroll: block dice rerolled from {string.Join(", ", pending.Rolls)} to {string.Join(", ", rerolledRolls)}." }
            ]
        };

        return rerolledRolls.Length > 1
            ? rerolledMatch with
            {
                PendingBlock = new PendingBlockChoice
                {
                    AttackerTeamId = attackerTeam.Id,
                    DefenderTeamId = defenderTeam.Id,
                    AttackerPlayerId = attacker.Id,
                    DefenderPlayerId = defender.Id,
                    Rolls = rerolledRolls,
                    AttackerStrength = pending.AttackerStrength,
                    DefenderStrength = pending.DefenderStrength,
                    PreventFollowUp = pending.PreventFollowUp,
                    AlreadyRerolled = true
                }
            }
            : ResolveChosenBlockDie(
                rerolledMatch,
                ruleset,
                attackerTeam,
                attacker,
                attackerPlacement,
                defenderTeam,
                defender,
                defenderPlacement,
                strength,
                rerolledRolls,
                rerolledRolls[0],
                pending.PreventFollowUp,
                allowTeamReroll: false);
    }

    private MatchState ResolvePushAfterBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        Player defender,
        PlayerPlacement defenderPlacement,
        bool knockDefenderDown,
        string resultMessage,
        bool suppressStandFirm = false,
        bool preventFollowUp = false)
    {
        var attackerAction = GetActivation(match, attacker.Id, attackerPlacement.TeamId)?.Action ?? PlayerTurnAction.Block;
        var legalSquares = LegalPushSquares(match, ruleset, attackerPlacement.Square!, defenderPlacement.Square!, attacker, defender, attackerAction);
        if (!suppressStandFirm &&
            PlayerHasHookedEffect(ruleset, defender, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.StandFirm))
        {
            return match with
            {
                PendingStandFirm = new PendingStandFirmChoice
                {
                    AttackerTeamId = attackerPlacement.TeamId,
                    DefenderTeamId = defenderPlacement.TeamId,
                    AttackerPlayerId = attacker.Id,
                    DefenderPlayerId = defender.Id,
                    DefenderSquare = defenderPlacement.Square!,
                    LegalSquares = legalSquares,
                    KnockDefenderDown = knockDefenderDown,
                    ResultMessage = resultMessage,
                    PreventFollowUp = preventFollowUp
                },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{resultMessage} {defender.Name} can use Stand Firm." }
                ]
            };
        }

        if (legalSquares.Length == 0)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, match.Ball.CarrierPlayerId == defender.Id, knockDefenderDown);
            var crowdSquare = CrowdPushDestination(defenderPlacement.Square!);
            var loggedMatch = match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{resultMessage} No legal push square is available; {defender.Name} is pushed into the crowd." }
                ]
            };

            return CompleteBlockPush(
                loggedMatch,
                ruleset,
                attackerTeam,
                attacker,
                defenderTeam,
                defender,
                defenderPlacement.Square!,
                crowdSquare,
                knockDefenderDown,
                stripBall,
                preventFollowUp);
        }

        if (legalSquares.Length == 1)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, match.Ball.CarrierPlayerId == defender.Id, knockDefenderDown);
            var pushedMatch = IsOnPitch(ruleset, legalSquares[0])
                ? MovePushedPlayer(match, defender.Id, legalSquares[0])
                : match;
            var pushMessage = IsOnPitch(ruleset, legalSquares[0])
                ? $"{resultMessage} {defender.Name} is pushed to {legalSquares[0].X},{legalSquares[0].Y}."
                : $"{resultMessage} {defender.Name} is pushed off the pitch into the crowd.";
            var loggedMatch = pushedMatch with
            {
                Log =
                [
                    .. pushedMatch.Log,
                    new MatchLogEntry { Message = pushMessage }
                ]
            };

            return CompleteBlockPush(
                loggedMatch,
                ruleset,
                attackerTeam,
                attacker,
                defenderTeam,
                defender,
                defenderPlacement.Square!,
                legalSquares[0],
                knockDefenderDown,
                stripBall,
                preventFollowUp);
        }

        return match with
        {
            PendingPush = new PendingPushChoice
            {
                AttackerTeamId = attackerPlacement.TeamId,
                DefenderTeamId = defenderPlacement.TeamId,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = defender.Id,
                DefenderSquare = defenderPlacement.Square!,
                LegalSquares = legalSquares,
                KnockDefenderDown = knockDefenderDown,
                ResultMessage = resultMessage,
                PreventFollowUp = preventFollowUp
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{resultMessage} Choose a push square." }
            ]
        };
    }

    private MatchState CompleteBlockPush(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        LeagueTeam defenderTeam,
        Player defender,
        PitchSquare followUpSquare,
        PitchSquare defenderDestination,
        bool knockDefenderDown,
        bool stripBall,
        bool preventFollowUp = false,
        IReadOnlyList<PendingChainPushResolution>? chainPushes = null)
    {
        var blocksMadeBeforePush = GetBlocksMade(match, attacker.Id, attackerTeam.Id);
        var countedMatch = IncrementActivationBlocksMade(match, attacker.Id, attackerTeam.Id);
        var deferredPush = new PendingBlockPushResolution
        {
            Source = followUpSquare,
            Destination = defenderDestination,
            KnockDefenderDown = knockDefenderDown,
            StripBall = stripBall,
            ChainPushes = chainPushes ?? []
        };

        if (preventFollowUp)
        {
            var blockedFollowUpMatch = countedMatch with
            {
                Log =
                [
                    .. countedMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} cannot follow up while using Multiple Block." }
                ]
            };
            var multipleBlockMatch = AwardCasualtyIfCaused(
                ResolveBlockPushConsequences(blockedFollowUpMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, deferredPush),
                attackerTeam,
                attacker,
                defenderTeam,
                defender.Id);

            // Only end the activation once the second Multiple Block has resolved; while a
            // continuation is still pending the attacker has another block to make.
            return multipleBlockMatch.PendingMultipleBlock is null
                ? CompleteBlockActivationIfDone(multipleBlockMatch, attacker.Id, attackerTeam.Id)
                : multipleBlockMatch;
        }

        if (!CanFollowUp(countedMatch, attacker.Id, attackerTeam.Id, followUpSquare, defender.Id))
        {
            return CompleteBlockPushAfterConsequences(
                ResolveBlockPushConsequences(countedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, deferredPush),
                attackerTeam,
                attacker,
                defenderTeam,
                defender);
        }

        if (PlayerHasHookedEffect(ruleset, defender, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Fend))
        {
            var fendMatch = countedMatch with
            {
                Log =
                [
                    .. countedMatch.Log,
                    new MatchLogEntry { Message = $"{defender.Name} uses Fend; {attacker.Name} cannot follow up." }
                ]
            };
            return CompleteBlockPushAfterConsequences(
                ResolveBlockPushConsequences(fendMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, deferredPush),
                attackerTeam,
                attacker,
                defenderTeam,
                defender);
        }

        // Frenzy makes the follow-up mandatory on any push, regardless of whether the defender was
        // knocked down. The optional follow-up prompt below is skipped entirely for a Frenzy attacker.
        var hasFrenzy = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Frenzy);
        if (hasFrenzy)
        {
            var followedMatch = MoveAttackerToFollowUpSquare(countedMatch, attacker, followUpSquare);
            followedMatch = followedMatch with
            {
                Log =
                [
                    .. followedMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} must follow up with Frenzy." }
                ]
            };
            followedMatch = ResolveBlockPushConsequences(followedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, deferredPush);
            followedMatch = AwardCasualtyIfCaused(followedMatch, attackerTeam, attacker, defenderTeam, defender.Id);
            if (HasPostPushPendingChoice(followedMatch))
            {
                return followedMatch;
            }

            // Frenzy grants a second block only when the first block left the defender standing and
            // adjacent. If the defender was knocked down, or this is already the second block, the
            // mandatory follow-up still happens but no further block is thrown.
            var canSecondBlock = !knockDefenderDown && blocksMadeBeforePush == 0;
            var attackerPlacement = followedMatch.Placements.First(placement => placement.PlayerId == attacker.Id);
            var defenderPlacement = followedMatch.Placements.FirstOrDefault(placement => placement.PlayerId == defender.Id);
            if (canSecondBlock &&
                defenderPlacement?.Square is not null &&
                defenderPlacement.State == PlayerPitchState.Standing &&
                attackerPlacement.Square is not null &&
                PlacementsAreAdjacent(attackerPlacement, defenderPlacement))
            {
                return ResolveBlock(followedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
            }

            return CompleteBlockActivationIfDone(followedMatch, attacker.Id, attackerTeam.Id);
        }

        if (countedMatch.PendingApothecary is not null || countedMatch.PendingBallPlacement is not null)
        {
            return CompleteBlockActivationIfDone(countedMatch, attacker.Id, attackerTeam.Id);
        }

        return countedMatch with
        {
            PendingFollowUp = new PendingFollowUpChoice
            {
                AttackerTeamId = attackerTeam.Id,
                DefenderTeamId = defenderTeam.Id,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = defender.Id,
                FollowUpSquare = followUpSquare,
                DeferredBlockPush = deferredPush
            },
            Log =
            [
                .. countedMatch.Log,
                new MatchLogEntry { Message = $"{attacker.Name} may follow up to {followUpSquare.X},{followUpSquare.Y}." }
            ]
        };
    }

    private static bool CanFollowUp(MatchState match, Guid attackerPlayerId, Guid attackerTeamId, PitchSquare followUpSquare, Guid? ignoredPlayerId = null)
    {
        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId);
        return attackerPlacement is { TeamId: var teamId, State: PlayerPitchState.Standing, Square: not null } &&
            teamId == attackerTeamId &&
            match.Placements.All(placement =>
                placement.PlayerId == attackerPlayerId ||
                placement.PlayerId == ignoredPlayerId ||
                !PlacementOccupiesSquare(placement, followUpSquare) ||
                !OccupiesPitch(placement.State));
    }

    private static MatchState MoveAttackerToFollowUpSquare(MatchState match, Player attacker, PitchSquare followUpSquare)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == attacker.Id
                    ? placement with { Square = followUpSquare }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} follows up to {followUpSquare.X},{followUpSquare.Y}." }
            ]
        };
    }

    private BlockStrength ResolveBlockStrength(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        PlayerPlacement defenderPlacement,
        Player attacker,
        Player defender,
        int defenderStrengthBonus = 0,
        int? forcedDauntlessRoll = null)
    {
        var attackerAssists = CountAssists(match, ruleset, attackerTeam, defenderTeam, defenderPlacement, attackerPlacement.PlayerId);
        var defenderAssists = CountAssists(match, ruleset, defenderTeam, attackerTeam, attackerPlacement, defenderPlacement.PlayerId);
        var attackerAction = GetActivation(match, attacker.Id, attackerTeam.Id)?.Action ?? PlayerTurnAction.Block;
        var attackerBaseStrength = attacker.Stats.Strength + (attackerAction == PlayerTurnAction.Blitz && PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeRoll, SkillEffect.Horns) ? 1 : 0);
        int? dauntlessRoll = null;
        var dauntlessReached = false;
        var dauntlessTarget = 0;
        if (attackerBaseStrength < defender.Stats.Strength &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeRoll, SkillEffect.Dauntless))
        {
            dauntlessTarget = Math.Clamp(defender.Stats.Strength - attackerBaseStrength + 1, 2, 6);
            dauntlessRoll = forcedDauntlessRoll ?? _dice.RollD6();
            if (dauntlessRoll + attackerBaseStrength > defender.Stats.Strength)
            {
                attackerBaseStrength = defender.Stats.Strength;
                dauntlessReached = true;
            }
        }

        var attackerStrength = attackerBaseStrength + attackerAssists;
        var defenderStrength = defender.Stats.Strength + defenderAssists + defenderStrengthBonus;
        var dice = ResolveBlockDice(attackerStrength, defenderStrength);

        return new BlockStrength(attackerStrength, defenderStrength, dice, dauntlessRoll, dauntlessReached, dauntlessTarget);
    }

    private static string FormatDauntlessRoll(Player attacker, Player defender, BlockStrength strength, string suffix = "")
    {
        var result = strength.DauntlessReachedStrength ? "matched strength" : "did not match strength";
        return $"{attacker.Name} uses Dauntless against {defender.Name}: rolled {strength.DauntlessRoll} vs {strength.DauntlessTarget}+, {result}.{suffix}";
    }

    private int CountAssists(MatchState match, Ruleset ruleset, LeagueTeam assistingTeam, LeagueTeam opposingTeam, PlayerPlacement targetPlacement, Guid primaryPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == assistingTeam.Id &&
            placement.PlayerId != primaryPlayerId &&
            placement.PlayerId != targetPlacement.PlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            PlacementsAreAdjacent(placement, targetPlacement) &&
            (!IsMarkedByOpponent(match, assistingTeam.Id, placement.PlayerId, square, targetPlacement.PlayerId) ||
                (PlayerHasHookedEffect(ruleset, FindTeamPlayer(assistingTeam, placement.PlayerId), GameEventKind.BlockRoll, GameEventStage.ModifyTarget, SkillEffect.GuardAssist) &&
                    match.ActiveTeamId != opposingTeam.Id &&
                    !IsMarkedByOpponentWithHookedEffect(match, ruleset, assistingTeam.Id, opposingTeam, placement.PlayerId, square, targetPlacement.PlayerId, GameEventKind.BlockRoll, GameEventStage.ModifyTarget, SkillEffect.Defensive))));
    }

    private int CountFoulAssists(
        MatchState match,
        Guid assistingTeamId,
        PlayerPlacement victimPlacement,
        Guid foulerPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == assistingTeamId &&
            placement.PlayerId != foulerPlayerId &&
            placement.PlayerId != victimPlacement.PlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            PlacementsAreAdjacent(placement, victimPlacement) &&
            !IsMarkedByOpponent(match, assistingTeamId, placement.PlayerId, square, victimPlacement.PlayerId));
    }

    private static bool IsMarkedByOpponentWithHookedEffect(
        MatchState match,
        Ruleset ruleset,
        Guid teamId,
        LeagueTeam opposingTeam,
        Guid playerId,
        PitchSquare square,
        Guid ignoredOpponentId,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != ignoredOpponentId &&
            placement.PlayerId != playerId &&
            HasActiveTackleZone(placement) &&
            IsAdjacentToPlacement(placement, square) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), eventKind, stage, effect));
    }

    private static int ResolveBlockDice(int attackerStrength, int defenderStrength)
    {
        var high = Math.Max(attackerStrength, defenderStrength);
        var low = Math.Max(1, Math.Min(attackerStrength, defenderStrength));
        return high >= low * 2 ? 3 : high > low ? 2 : 1;
    }
    private PlayerPlacement ValidateBlock(
        MatchState match,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam defenderTeam,
        Guid defenderPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only block during a player turn.");
        }

        if (attackerTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can block during its turn.");
        }

        if (attackerTeam.Id == defenderTeam.Id)
        {
            throw new InvalidOperationException("A player cannot block a teammate.");
        }

        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId)
            ?? throw new InvalidOperationException("Attacker is not part of this match.");
        var defenderPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderPlayerId)
            ?? throw new InvalidOperationException("Defender is not part of this match.");

        if (attackerPlacement.TeamId != attackerTeam.Id || defenderPlacement.TeamId != defenderTeam.Id)
        {
            throw new InvalidOperationException("Block participants are assigned to the wrong teams.");
        }

        if (attackerPlacement.Square is null || attackerPlacement.State is not PlayerPitchState.Standing)
        {
            throw new InvalidOperationException("Only standing players on the pitch can block.");
        }

        if (defenderPlacement.Square is null || defenderPlacement.State is not PlayerPitchState.Standing)
        {
            throw new InvalidOperationException("Only standing players on the pitch can be blocked.");
        }

        if (!PlacementsAreAdjacent(attackerPlacement, defenderPlacement))
        {
            throw new InvalidOperationException("Blocks require adjacent players.");
        }

        return attackerPlacement;
    }

    private MatchState KnockPlayerDown(MatchState match, Ruleset ruleset, Player player, PlayerPlacement placement, InjuryResolution injury, PitchSquare square)
    {
        var log = new List<MatchLogEntry>();
        var nextMatch = match;
        var apothecary = CreatePendingApothecaryIfAvailable(nextMatch, placement, player.Name, injury);
        nextMatch = apothecary.Match;
        injury = apothecary.Injury;
        log.AddRange(injury.Log ?? []);
        log.AddRange(apothecary.Log);
        nextMatch = ApplyPlayerPitchState(nextMatch, player.Id, injury.State, OccupiesPitch(injury.State) ? square : null, injury.Casualty);

        if (match.Ball.CarrierPlayerId == player.Id)
        {
            var safeSquares = SafePairOfHandsSquares(match, ruleset, player, square, placement.IsLarge);
            if (safeSquares.Length > 0)
            {
                nextMatch = nextMatch with
                {
                    Ball = new BallState(),
                    PendingBallPlacement = new PendingBallPlacementChoice
                    {
                        TeamId = placement.TeamId,
                        PlayerId = player.Id,
                        LegalSquares = safeSquares,
                        Reason = "Safe Pair of Hands"
                    }
                };
                log.Add(new MatchLogEntry { Message = $"{player.Name} may use Safe Pair of Hands to place the ball." });
            }
            else
            {
                var scatterSquare = ScatterFrom(ruleset, square);
                var preLandingLog = nextMatch.Log;
                var landing = ResolveLooseBall(nextMatch, ruleset, scatterSquare);
                nextMatch = landing.Match with { Log = preLandingLog };
                log.AddRange(landing.Log.Prepend(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." }));
            }
        }

        return nextMatch with
        {
            Log = [.. nextMatch.Log, .. log]
        };
    }

    private MatchState CompleteBlockPushAfterConsequences(
        MatchState match,
        LeagueTeam attackerTeam,
        Player attacker,
        LeagueTeam defenderTeam,
        Player defender)
    {
        var awardedMatch = AwardCasualtyIfCaused(match, attackerTeam, attacker, defenderTeam, defender.Id);
        return CompleteBlockActivationIfDone(awardedMatch, attacker.Id, attackerTeam.Id);
    }

    private MatchState ResolveBlockPushConsequences(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        LeagueTeam defenderTeam,
        Player defender,
        PendingBlockPushResolution push)
    {
        var resolvedMatch = match;
        foreach (var chainPush in push.ChainPushes)
        {
            resolvedMatch = PushPlacement(
                resolvedMatch,
                ruleset,
                null,
                chainPush.PlayerId,
                chainPush.PlayerName,
                chainPush.Source,
                chainPush.Destination,
                knockDown: false,
                () => new InjuryResolution(PlayerPitchState.Standing),
                stripBall: false);
        }

        resolvedMatch = PushPlayer(
            resolvedMatch,
            ruleset,
            defender,
            push.Source,
            push.Destination,
            push.KnockDefenderDown,
            () => ResolveBlockInjury(ruleset, attacker, defender),
            push.StripBall);

        return resolvedMatch;
    }

    private static bool HasPostPushPendingChoice(MatchState match)
    {
        return match.PendingApothecary is not null ||
            match.PendingBallPlacement is not null ||
            match.PendingReroll is not null;
    }

    private static MatchState MovePushedPlayer(MatchState match, Guid playerId, PitchSquare destination)
    {
        var placement = FindPlacement(match, playerId)
            ?? throw new InvalidOperationException("Pushed player is not part of this match.");

        return ApplyPlayerPitchState(match, playerId, placement.State, destination, placement.Casualty);
    }

    private static PitchSquare CrowdPushDestination(PitchSquare source)
    {
        return new PitchSquare(source.X, -1);
    }

    private MatchState PushPlayer(MatchState match, Ruleset ruleset, Player player, PitchSquare source, PitchSquare destination, bool knockDown, Func<InjuryResolution>? resolveKnockdownState = null, bool stripBall = false)
    {
        return PushPlacement(
            match,
            ruleset,
            player,
            player.Id,
            player.Name,
            source,
            destination,
            knockDown,
            resolveKnockdownState ?? (() => ResolveFallInjury(player)),
            stripBall);
    }

    private MatchState PushPlacement(
        MatchState match,
        Ruleset ruleset,
        Player? player,
        Guid playerId,
        string playerName,
        PitchSquare source,
        PitchSquare destination,
        bool knockDown,
        Func<InjuryResolution> resolveKnockdownState,
        bool stripBall)
    {
        var placement = FindPlacement(match, playerId)
            ?? throw new InvalidOperationException("Pushed player is not part of this match.");

        // A push-fan square that lies off the pitch sends the player into the crowd.
        if (!IsOnPitch(ruleset, destination))
        {
            return PushPlayerIntoCrowd(match, ruleset, placement);
        }

        var occupant = FindPushOccupant(match, destination, ignoredPlayerId: playerId);
        if (occupant is not null)
        {
            var chainDestination = LegalPushSquares(match, ruleset, source, destination, occupant.PlayerId).FirstOrDefault();
            match = chainDestination is null
                ? PushPlayerIntoCrowd(match, ruleset, occupant)
                : PushPlacement(match, ruleset, null, occupant.PlayerId, occupant.PlayerId.ToString(), destination, chainDestination, knockDown: false, () => new InjuryResolution(occupant.State), stripBall: false);
            placement = FindPlacement(match, playerId)
                ?? throw new InvalidOperationException("Pushed player is not part of this match.");
        }

        var injury = knockDown ? resolveKnockdownState() : new InjuryResolution(PlayerPitchState.Standing);
        var nextState = injury.State;
        if (!knockDown)
        {
            nextState = placement.State;
            injury = new InjuryResolution(nextState);
        }

        match = ApplyPlayerPitchState(match, playerId, nextState, OccupiesPitch(nextState) ? destination : null, injury.Casualty);

        var ball = match.Ball;
        var log = new List<MatchLogEntry>();
        if (ball.CarrierPlayerId == playerId && (knockDown || stripBall))
        {
            var safeSquares = player is null ? [] : SafePairOfHandsSquares(match, ruleset, player, destination, placement.IsLarge);
            if (player is not null && safeSquares.Length > 0)
            {
                ball = new BallState();
                match = match with
                {
                    PendingBallPlacement = new PendingBallPlacementChoice
                    {
                        TeamId = placement.TeamId,
                        PlayerId = player.Id,
                        LegalSquares = safeSquares,
                        Reason = "Safe Pair of Hands"
                    }
                };
                log.Add(new MatchLogEntry { Message = $"{playerName} may use Safe Pair of Hands to place the ball." });
            }
            else
            {
                var scatterSquare = ScatterFrom(ruleset, destination);
                var preLandingLog = match.Log;
                var landing = ResolveLooseBall(match, ruleset, scatterSquare);
                match = landing.Match with { Log = preLandingLog };
                ball = match.Ball;
                log.Add(new MatchLogEntry { Message = stripBall && !knockDown ? $"Strip Ball knocks the ball loose to {scatterSquare.X},{scatterSquare.Y}." : $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
                log.AddRange(landing.Log);
            }
        }
        else if (ball.CarrierPlayerId is null && ball.Square == destination)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var preLandingLog = match.Log;
            var landing = ResolveLooseBall(match, ruleset, scatterSquare);
            match = landing.Match with { Log = preLandingLog };
            ball = match.Ball;
            log.Add(new MatchLogEntry { Message = $"Ball is pushed from {destination.X},{destination.Y} to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        log.AddRange(injury.Log ?? []);

        return match with
        {
            Ball = ball,
            Log = [.. match.Log, .. log]
        };
    }

    private static PitchSquare[] LegalPushSquares(MatchState match, Ruleset ruleset, PitchSquare attackerSquare, PitchSquare defenderSquare, Guid defenderPlayerId)
    {
        var dx = Math.Sign(defenderSquare.X - attackerSquare.X);
        var dy = Math.Sign(defenderSquare.Y - attackerSquare.Y);
        var candidates = new List<PitchSquare>();

        if (dx != 0 && dy != 0)
        {
            candidates.Add(new PitchSquare(defenderSquare.X + dx, defenderSquare.Y + dy));
            candidates.Add(new PitchSquare(defenderSquare.X + dx, defenderSquare.Y));
            candidates.Add(new PitchSquare(defenderSquare.X, defenderSquare.Y + dy));
        }
        else if (dx != 0)
        {
            candidates.Add(new PitchSquare(defenderSquare.X + dx, defenderSquare.Y - 1));
            candidates.Add(new PitchSquare(defenderSquare.X + dx, defenderSquare.Y));
            candidates.Add(new PitchSquare(defenderSquare.X + dx, defenderSquare.Y + 1));
        }
        else
        {
            candidates.Add(new PitchSquare(defenderSquare.X - 1, defenderSquare.Y + dy));
            candidates.Add(new PitchSquare(defenderSquare.X, defenderSquare.Y + dy));
            candidates.Add(new PitchSquare(defenderSquare.X + 1, defenderSquare.Y + dy));
        }

        var onPitchCandidates = candidates
            .Where(square => IsOnPitch(ruleset, square))
            .Distinct()
            .ToArray();
        var emptyCandidates = onPitchCandidates
            .Where(square => FindPushOccupant(match, square, defenderPlayerId) is null)
            .ToArray();

        if (emptyCandidates.Length > 0)
        {
            return emptyCandidates;
        }

        // No unoccupied on-pitch square is available. The player may still be pushed into an
        // occupied on-pitch square whose own chain-push can be resolved, or off the pitch into
        // the crowd. Every off-pitch fan square represents the same crowd push, so they collapse
        // to a single option.
        var crowdSquare = candidates
            .Where(square => !IsOnPitch(ruleset, square))
            .Take(1);

        return
        [
            .. onPitchCandidates.Where(square => CanResolvePushDestination(match, ruleset, defenderSquare, square, defenderPlayerId)),
            .. crowdSquare
        ];
    }

    private static bool CanResolvePushDestination(MatchState match, Ruleset ruleset, PitchSquare source, PitchSquare destination, Guid pushedPlayerId)
    {
        var occupant = FindPushOccupant(match, destination, pushedPlayerId);
        if (occupant is null)
        {
            return true;
        }

        return LegalPushSquares(match, ruleset, source, destination, occupant.PlayerId).Length > 0;
    }

    private static PlayerPlacement? FindPushOccupant(MatchState match, PitchSquare square, Guid ignoredPlayerId)
    {
        return match.Placements.FirstOrDefault(placement =>
            placement.PlayerId != ignoredPlayerId &&
            PlacementOccupiesSquare(placement, square) &&
            OccupiesPitch(placement.State));
    }

    private MatchState PushPlayerIntoCrowd(MatchState match, Ruleset ruleset, PlayerPlacement placement)
    {
        var injuryRoll = Roll2D6();
        var playerName = PlayerName(placement.PlayerId);
        var injuryState = ResolveInjury(injuryRoll);
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, playerName, injuryState);
        match = apothecary.Match;
        injuryState = apothecary.Injury;
        var crowdState = injuryState.State is PlayerPitchState.KnockedOut or PlayerPitchState.Casualty or PlayerPitchState.Dead
            ? injuryState.State
            : PlayerPitchState.Reserve;
        var log = new List<MatchLogEntry>();
        var carriedBallIntoCrowd = match.Ball.CarrierPlayerId == placement.PlayerId && placement.Square is PitchSquare;
        var crowdMatch = match with
        {
            Ball = carriedBallIntoCrowd ? new BallState() : match.Ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == placement.PlayerId
                    ? current with
                    {
                        Square = null,
                        State = crowdState,
                        StunnedRecoveryHalf = null,
                        StunnedRecoveryTurn = null,
                        Casualty = crowdState is PlayerPitchState.Casualty or PlayerPitchState.Dead ? injuryState.Casualty : null
                    }
                    : current)
                .ToArray()
        };

        if (carriedBallIntoCrowd && placement.Square is PitchSquare square)
        {
            var scatterSquare = ScatterFrom(ruleset, square);
            var landing = ResolveLooseBall(crowdMatch, ruleset, scatterSquare);
            crowdMatch = landing.Match with { Log = match.Log };
            log.Add(new MatchLogEntry { Message = $"Ball scatters in from the crowd to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var crowdLog = new List<MatchLogEntry>
        {
            new() { Message = $"{playerName} injury roll {injuryRoll}: {FormatInjuryOutcome(crowdState)}." },
            new() { Message = $"{playerName} is pushed into the crowd: {FormatInjuryOutcome(crowdState)}." }
        };
        if (injuryState.Casualty is not null)
        {
            crowdLog.Add(new MatchLogEntry { Message = $"{playerName} casualty roll {injuryState.Casualty.Roll}: {FormatCasualtyResult(injuryState.Casualty.Result)}." });
        }
        crowdLog.AddRange(apothecary.Log);
        crowdLog.AddRange(log);

        return crowdMatch with
        {
            Log =
            [
                .. match.Log,
                .. crowdLog
            ]
        };
    }

    private static int CasualtySeverity(CasualtyResult result)
    {
        return result switch
        {
            CasualtyResult.BadlyHurt => 1,
            CasualtyResult.SeriouslyHurt => 2,
            CasualtyResult.SeriousInjury => 3,
            CasualtyResult.LastingInjury => 4,
            CasualtyResult.Dead => 5,
            _ => 5
        };
    }
    private InjuryResolution ResolveFallInjury(Player player, bool armBarApplies = false)
    {
        var armorRoll = Roll2D6();
        if (armorRoll <= player.Stats.Armor)
        {
            if (!armBarApplies || armorRoll + 1 <= player.Stats.Armor)
            {
                return WithInjuryLog(new InjuryResolution(PlayerPitchState.Prone), player, armorRoll, 0, player.Stats.Armor, armorBroken: false, injuryRoll: null, injuryModifier: 0);
            }

            // Arm Bar's +1 is what broke the armour, so the injury roll itself is unmodified.
            var armBarInjuryRoll = Roll2D6();
            return WithInjuryLog(ResolveInjury(armBarInjuryRoll), player, armorRoll, 1, player.Stats.Armor, armorBroken: true, injuryRoll: armBarInjuryRoll, injuryModifier: 0);
        }

        var injuryRoll = Roll2D6();
        var injuryModifier = armBarApplies ? 1 : 0;
        return WithInjuryLog(ResolveInjury(injuryRoll + injuryModifier), player, armorRoll, 0, player.Stats.Armor, armorBroken: true, injuryRoll, injuryModifier);
    }
    private static MatchState AwardCasualtyIfCaused(MatchState match, LeagueTeam team, Player player, LeagueTeam victimTeam, Guid victimPlayerId)
    {
        var victim = match.Placements.FirstOrDefault(placement => placement.PlayerId == victimPlayerId);
        if (victim?.Casualty is null || CasualtyAwardExists(match, player.Id, victimPlayerId))
        {
            return match;
        }

        var victimPlayer = FindTeamPlayer(victimTeam, victimPlayerId);
        return match with
        {
            PlayerAwards = AddPlayerAward(
                match,
                team.Id,
                player.Id,
                MatchPlayerAwardKind.Casualty,
                2,
                victimPlayerId,
                team.Name,
                player.Name,
                victimTeam.Id,
                victimTeam.Name,
                victimPlayer.Name,
                victim.Casualty.Result)
        };
    }

    private static bool CasualtyAwardExists(MatchState match, Guid playerId, Guid victimPlayerId)
    {
        return match.PlayerAwards.Any(award =>
            award.Kind == MatchPlayerAwardKind.Casualty &&
            award.PlayerId == playerId &&
            award.VictimPlayerId == victimPlayerId);
    }

    private static MatchPlayerAward[] AddPlayerAward(
        MatchState match,
        Guid teamId,
        Guid playerId,
        MatchPlayerAwardKind kind,
        int starPlayerPoints,
        Guid? victimPlayerId = null,
        string? teamName = null,
        string? playerName = null,
        Guid? victimTeamId = null,
        string? victimTeamName = null,
        string? victimPlayerName = null,
        CasualtyResult? casualtyResult = null)
    {
        return
        [
            .. match.PlayerAwards,
            new MatchPlayerAward
            {
                TeamId = teamId,
                PlayerId = playerId,
                VictimPlayerId = victimPlayerId,
                VictimTeamId = victimTeamId,
                Kind = kind,
                StarPlayerPoints = starPlayerPoints,
                TeamName = teamName,
                PlayerName = playerName,
                VictimTeamName = victimTeamName,
                VictimPlayerName = victimPlayerName,
                CasualtyResult = casualtyResult
            }
        ];
    }
    private InjuryResolution ResolveBlockInjury(Ruleset ruleset, Player attacker, Player defender)
    {
        var armorRoll = Roll2D6();
        var hasMightyBlowArmor = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.ArmorRoll, GameEventStage.AfterRoll, SkillEffect.MightyBlow);
        var hasMightyBlowInjury = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.InjuryRoll, GameEventStage.AfterRoll, SkillEffect.MightyBlow);
        var hasIronHardSkin = PlayerHasHookedEffect(ruleset, defender, GameEventKind.ArmorRoll, GameEventStage.BeforeResolve, SkillEffect.IronHardSkin);
        var clawsBreaksArmor = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.ArmorRoll, GameEventStage.BeforeResolve, SkillEffect.Claws) &&
            !hasIronHardSkin &&
            armorRoll >= 8;
        if (armorRoll <= defender.Stats.Armor && !clawsBreaksArmor)
        {
            if (hasIronHardSkin || !hasMightyBlowArmor || armorRoll + 1 <= defender.Stats.Armor)
            {
                return WithInjuryLog(new InjuryResolution(PlayerPitchState.Prone), defender, armorRoll, 0, defender.Stats.Armor, armorBroken: false, injuryRoll: null, injuryModifier: 0);
            }

            // Mighty Blow's +1 broke the armour, so the injury roll itself is unmodified.
            var mightyBlowInjuryRoll = Roll2D6();
            return WithInjuryLog(ResolveInjury(ruleset, defender, mightyBlowInjuryRoll), defender, armorRoll, 1, defender.Stats.Armor, armorBroken: true, injuryRoll: mightyBlowInjuryRoll, injuryModifier: 0);
        }

        var injuryRoll = Roll2D6();
        var injuryModifier = hasMightyBlowInjury ? 1 : 0;
        var armorNote = clawsBreaksArmor && armorRoll <= defender.Stats.Armor ? "Claws" : null;
        return WithInjuryLog(ResolveInjury(ruleset, defender, injuryRoll + injuryModifier), defender, armorRoll, 0, defender.Stats.Armor, armorBroken: true, injuryRoll, injuryModifier, armorNote);
    }

    /// <summary>
    /// Attaches armour/injury/casualty roll detail lines to an <see cref="InjuryResolution"/> so the
    /// resolution carries its own log. Application sites (block knockdowns, pushes, dodge/go-for-it falls)
    /// splice <see cref="InjuryResolution.Log"/> into the match log next to the outcome they already report.
    /// </summary>
    private static InjuryResolution WithInjuryLog(
        InjuryResolution injury,
        Player victim,
        int armorRoll,
        int armorModifier,
        int armorValue,
        bool armorBroken,
        int? injuryRoll,
        int injuryModifier,
        string? armorNote = null)
    {
        var noteText = armorNote is null ? "" : $" ({armorNote})";
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"{victim.Name} armour roll {FormatModifiedRoll(armorRoll, armorModifier)} vs AV {armorValue}+, {(armorBroken ? "broken" : "holds")}{noteText}." }
        };

        if (armorBroken && injuryRoll is int roll)
        {
            log.Add(new MatchLogEntry { Message = $"{victim.Name} injury roll {FormatModifiedRoll(roll, injuryModifier)}: {FormatInjuryOutcome(injury.State)}." });
        }

        if (injury.Casualty is not null)
        {
            log.Add(new MatchLogEntry { Message = $"{victim.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
        }

        return injury with { Log = log };
    }

    private static string FormatModifiedRoll(int roll, int modifier)
    {
        return modifier == 0 ? $"{roll}" : $"{roll} +{modifier} = {roll + modifier}";
    }

    private InjuryResolution ResolveInjury(int injuryRoll)
    {
        if (injuryRoll >= 10)
        {
            var casualtyRoll = RollD16();
            var casualtyResult = ResolveCasualty(casualtyRoll);
            return new InjuryResolution(
                casualtyResult == CasualtyResult.Dead ? PlayerPitchState.Dead : PlayerPitchState.Casualty,
                new CasualtyRoll { Roll = casualtyRoll, Result = casualtyResult });
        }

        return new InjuryResolution(injuryRoll >= 8 ? PlayerPitchState.KnockedOut : PlayerPitchState.Stunned);
    }

    private InjuryResolution ResolveInjury(Ruleset ruleset, Player player, int injuryRoll)
    {
        if (injuryRoll == 8 &&
            PlayerHasHookedEffect(ruleset, player, GameEventKind.InjuryRoll, GameEventStage.BeforeResolve, SkillEffect.ThickSkull))
        {
            return new InjuryResolution(PlayerPitchState.Stunned);
        }

        if (injuryRoll < 10)
        {
            return ResolveInjury(injuryRoll);
        }

        var casualtyRoll = RollD16();
        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.InjuryRoll, GameEventStage.AfterRoll, "decay"))
        {
            var decayRoll = RollD16();
            casualtyRoll = CasualtySeverity(ResolveCasualty(decayRoll)) > CasualtySeverity(ResolveCasualty(casualtyRoll))
                ? decayRoll
                : casualtyRoll;
        }

        var casualtyResult = ResolveCasualty(casualtyRoll);
        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.InjuryRoll, GameEventStage.AfterRoll, "regeneration") &&
            _dice.RollD6() >= 4)
        {
            return new InjuryResolution(PlayerPitchState.Reserve);
        }

        return new InjuryResolution(
            casualtyResult == CasualtyResult.Dead ? PlayerPitchState.Dead : PlayerPitchState.Casualty,
            new CasualtyRoll { Roll = casualtyRoll, Result = casualtyResult });
    }
    private static CasualtyResult ResolveCasualty(int casualtyRoll)
    {
        return casualtyRoll switch
        {
            <= 6 => CasualtyResult.BadlyHurt,
            <= 9 => CasualtyResult.SeriouslyHurt,
            <= 12 => CasualtyResult.SeriousInjury,
            <= 14 => CasualtyResult.LastingInjury,
            _ => CasualtyResult.Dead
        };
    }

    private static string FormatCasualtyResult(CasualtyResult result)
    {
        return result switch
        {
            CasualtyResult.BadlyHurt => "badly hurt",
            CasualtyResult.SeriouslyHurt => "seriously hurt",
            CasualtyResult.SeriousInjury => "serious injury",
            CasualtyResult.LastingInjury => "lasting injury",
            CasualtyResult.Dead => "dead",
            _ => result.ToString().ToLowerInvariant()
        };
    }
}
