using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;
using static SoloBB.Core.Services.RollTargets;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState MovePlayer(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.Move, opposingTeam);
    }

    public MatchState MovePlayerAsBlitz(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.Blitz, opposingTeam);
    }

    public MatchState MovePlayerAsPass(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.Pass, opposingTeam);
    }

    public MatchState MovePlayerAsHandOff(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.HandOff, opposingTeam);
    }

    public MatchState LeapPlayer(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only leap during a player turn.");
        }

        var player = FindTeamPlayer(team, playerId);
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.Leap))
        {
            throw new InvalidOperationException($"{player.Name} does not have Leap.");
        }

        var placement = FindStandingPlacement(match, playerId, team.Id, "leaper");
        if (!IsOnPitch(ruleset, destination))
        {
            throw new InvalidOperationException("Leap destination must be on the pitch.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && PlacementOccupiesSquare(current, destination) && OccupiesPitch(current.State)))
        {
            throw new InvalidOperationException("Leap destination must be empty.");
        }

        var startSquare = placement.Square!;
        var distance = Math.Max(Math.Abs(destination.X - startSquare.X), Math.Abs(destination.Y - startSquare.Y));
        if (distance is < 1 or > 2)
        {
            throw new InvalidOperationException("Leap destination must be one or two squares away.");
        }

        var tackleZones = CountOpposingTackleZones(match, team.Id, playerId, destination);
        var veryLongLegsModifier = PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.VeryLongLegs) ? -1 : 0;
        var target = Math.Clamp(player.Stats.Agility + 1 + Math.Max(0, tackleZones + veryLongLegsModifier), 2, 6);
        var roll = _dice.RollD6();
        var leapedAction = BeginPlayerAction(match, ruleset, team, player, PlayerTurnAction.Move, goForItsUsed: 0);
        if (leapedAction.Prevented)
        {
            return leapedAction.Match;
        }

        var leapedMatch = leapedAction.Match;
        if (!RollSucceeds(roll, target, ruleset.Dice))
        {
            return ResolveFailedDodge(
                leapedMatch,
                ruleset,
                team,
                player,
                destination,
                roll,
                target,
                ArmBarApplies(match, ruleset, opposingTeam, playerId, startSquare, destination));
        }

        var movedMatch = leapedMatch with
        {
            Placements = leapedMatch.Placements
                .Select(current => current.PlayerId == playerId ? current with { Square = destination } : current)
                .ToArray(),
            Log =
            [
                .. leapedMatch.Log,
                new MatchLogEntry { Message = $"{player.Name} leaps to {destination.X},{destination.Y}: rolled {roll} vs {target}+ ({tackleZones} opposing tackle zones), success." }
            ]
        };

        if (movedMatch.Ball.CarrierPlayerId is null && movedMatch.Ball.Square == destination)
        {
            return ResolvePickup(movedMatch, ruleset, team, player, destination, PlayerTurnAction.Move, destination, [destination], 0, 1);
        }

        return IsTouchdown(movedMatch, ruleset, team, playerId, destination)
            ? ScoreTouchdown(movedMatch, ruleset, team)
            : movedMatch;
    }

    public MatchState MoveOnTheBallPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare destination,
        bool kickoffMove = false,
        LeagueTeam? opposingTeam = null)
    {
        var player = FindTeamPlayer(team, playerId);
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.OnTheBall))
        {
            throw new InvalidOperationException($"{player.Name} does not have On the Ball.");
        }

        var placement = FindStandingPlacement(match, playerId, team.Id, "On the Ball player");
        var path = BuildMovementPath(placement.Square!, destination);
        if (path.Length is < 1 or > 3)
        {
            throw new InvalidOperationException("On the Ball can move up to three squares.");
        }

        if (path.Any(square => match.Placements.Any(current => current.PlayerId != playerId && PlacementOccupiesSquare(current, square) && OccupiesPitch(current.State))))
        {
            throw new InvalidOperationException("On the Ball movement cannot pass through occupied squares.");
        }

        if (kickoffMove && CrossesMidline(ruleset, placement.Square!, destination))
        {
            throw new InvalidOperationException("On the Ball kickoff movement cannot cross into the opponent's half.");
        }

        var nextMatch = match;
        for (var stepIndex = 0; stepIndex < path.Length; stepIndex++)
        {
            var currentPlacement = nextMatch.Placements.First(current => current.PlayerId == playerId);
            var currentSquare = currentPlacement.Square!;
            var nextSquare = path[stepIndex];

            if (IsMarkedByOpponent(nextMatch, ruleset, opposingTeam, team.Id, playerId, currentSquare))
            {
                var dodgeRoll = _dice.RollD6();
                var opposingTackleZones = CountOpposingTackleZones(nextMatch, ruleset, opposingTeam, team.Id, playerId, nextSquare);
                var dodgeTarget = DodgeTarget(ruleset, player, opposingTackleZones);
                if (!RollSucceeds(dodgeRoll, dodgeTarget, ruleset.Dice))
                {
                    var failedMatch = ResolveFailedDodge(nextMatch, ruleset, team, player, nextSquare, dodgeRoll, dodgeTarget);
                    return failedMatch with
                    {
                        Log =
                        [
                            .. failedMatch.Log,
                            new MatchLogEntry { Message = $"{player.Name} fell while using On the Ball." }
                        ]
                    };
                }

                nextMatch = nextMatch with
                {
                    Log =
                    [
                        .. nextMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} dodges during On the Ball: rolled {dodgeRoll} vs {dodgeTarget}+ ({opposingTackleZones} opposing tackle zones), success." }
                    ]
                };
            }

            var tentacles = ApplyTentacles(nextMatch, ruleset, opposingTeam, player, currentSquare);
            nextMatch = tentacles.Match;
            if (tentacles.Held)
            {
                return nextMatch;
            }

            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(current => current.PlayerId == playerId ? current with { Square = nextSquare } : current)
                    .ToArray()
            };
            nextMatch = ApplyShadowing(nextMatch, ruleset, opposingTeam, player, currentSquare, nextSquare);
        }

        return nextMatch with
        {
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = $"{player.Name} uses On the Ball to move to {destination.X},{destination.Y}." }
            ]
        };
    }

    public MatchState ContinueRunningPassMove(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare destination,
        LeagueTeam? opposingTeam = null)
    {
        var player = FindTeamPlayer(team, playerId);
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.AfterEvent, SkillEffect.RunningPass))
        {
            throw new InvalidOperationException($"{player.Name} does not have Running Pass.");
        }

        var activation = GetActivation(match, playerId, team.Id)
            ?? throw new InvalidOperationException("Running Pass can only continue after a pass activation.");
        if (activation.Action != PlayerTurnAction.Pass)
        {
            throw new InvalidOperationException("Running Pass can only continue after a Pass action.");
        }

        return MovePlayerCore(match with
        {
            Activations = match.Activations
                .Where(current => current.PlayerId != playerId || current.TeamId != team.Id || current.Half != match.Half || current.Turn != match.Turn)
                .ToArray()
        }, ruleset, team, playerId, destination, PlayerTurnAction.Pass, opposingTeam);
    }

    public MatchState BallAndChainMove(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        LeagueTeam opposingTeam)
    {
        var actor = FindTeamPlayer(team, playerId);
        if (!PlayerHasHookedEffect(ruleset, actor, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.BallAndChain))
        {
            throw new InvalidOperationException($"{actor.Name} does not have Ball and Chain.");
        }

        var placement = ValidateSpecialActor(match, team, actor, requireStanding: true);
        var action = BeginPlayerAction(match, ruleset, team, actor, PlayerTurnAction.Special, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var direction = _dice.RollD8();
        var destination = ScatterDirection(placement.Square!, direction);
        if (!IsOnPitch(ruleset, destination))
        {
            var injury = ResolveFallInjury(actor);
            var crowdMatch = KnockPlayerDown(action.Match, ruleset, actor, placement, injury, placement.Square!);
            return ApplyTurnover(crowdMatch with
            {
                Log = [.. crowdMatch.Log, new MatchLogEntry { Message = $"{actor.Name} moves randomly with Ball and Chain direction {direction}, leaves the pitch, and falls." }]
            }, ruleset, team.Id);
        }

        var occupant = action.Match.Placements.FirstOrDefault(current =>
            current.PlayerId != playerId &&
            PlacementOccupiesSquare(current, destination) &&
            OccupiesPitch(current.State));
        if (occupant is null)
        {
            return action.Match with
            {
                Placements = action.Match.Placements
                    .Select(current => current.PlayerId == playerId
                        ? current with { Square = destination }
                        : current)
                    .ToArray(),
                Log = [.. action.Match.Log, new MatchLogEntry { Message = $"{actor.Name} moves randomly with Ball and Chain to {destination.X},{destination.Y}." }]
            };
        }

        if (occupant.TeamId == team.Id)
        {
            var teammate = FindTeamPlayer(team, occupant.PlayerId);
            var hitMatch = ResolveSpecialArmorAttack(action.Match, ruleset, team, teammate, occupant, armorModifier: 0, $"{actor.Name}'s Ball and Chain hits teammate {teammate.Name}");
            return hitMatch with
            {
                Log = [.. hitMatch.Log, new MatchLogEntry { Message = $"{actor.Name}'s random Ball and Chain movement is blocked by a teammate." }]
            };
        }

        var defender = FindTeamPlayer(opposingTeam, occupant.PlayerId);
        var attackerPlacement = action.Match.Placements.First(current => current.PlayerId == playerId);
        return ResolveBlock(action.Match with
        {
            Log = [.. action.Match.Log, new MatchLogEntry { Message = $"{actor.Name} moves randomly with Ball and Chain into {defender.Name}." }]
        }, ruleset, team, actor, attackerPlacement, opposingTeam, defender);
    }


    private MatchState MovePlayerCore(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare destination,
        PlayerTurnAction action,
        LeagueTeam? opposingTeam = null,
        Guid? blitzDefenderPlayerId = null)
    {
        if (match.Phase is MatchPhase.Complete)
        {
            throw new InvalidOperationException("Players cannot move after the match is complete.");
        }

        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only move during a player turn.");
        }

        if (match.PendingReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending reroll before taking another action.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can move during its turn.");
        }

        if (!IsOnPitch(ruleset, destination))
        {
            throw new InvalidOperationException($"Square {destination.X},{destination.Y} is outside the pitch.");
        }

        var player = team.Players.FirstOrDefault(current => current.Id == playerId)
            ?? throw new InvalidOperationException($"Team '{team.Name}' does not contain player '{playerId}'.");
        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != team.Id)
        {
            throw new InvalidOperationException("Player is not assigned to the moving team.");
        }

        if (placement.Square is null || placement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone))
        {
            throw new InvalidOperationException("Only standing or prone players on the pitch can move.");
        }

        var isStandingUp = placement.State == PlayerPitchState.Prone;
        var path = BuildMovementPath(placement.Square!, destination);
        if (placement.Rooted && path.Length > 0)
        {
            throw new InvalidOperationException($"{player.Name} is rooted and cannot move.");
        }

        if (path.Length == 0 && !isStandingUp)
        {
            throw new InvalidOperationException("Choose a different square to move to.");
        }

        if (path.Any(square => match.Placements.Any(current => current.PlayerId != playerId && current.Square == square)))
        {
            throw new InvalidOperationException("Movement paths cannot pass through occupied squares.");
        }

        var existingActivation = GetActivation(match, player.Id, team.Id);
        var continuesMovementActivation =
            existingActivation is { DeclaredOnly: false } &&
            existingActivation.Action == action &&
            action is PlayerTurnAction.Move or PlayerTurnAction.Blitz or PlayerTurnAction.Pass or PlayerTurnAction.HandOff;
        var standUpMovementCost = isStandingUp &&
            !continuesMovementActivation &&
            !PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.JumpUp)
                ? Math.Min(3, player.Stats.Movement)
                : 0;
        var priorMovementSquares = continuesMovementActivation ? existingActivation!.MovementSquaresUsed : 0;
        var priorGoForItsUsed = continuesMovementActivation ? existingActivation!.GoForItsUsed : 0;
        var remainingMovementAllowance = Math.Max(0, player.Stats.Movement - priorMovementSquares - standUpMovementCost);
        var totalMovementSquares = priorMovementSquares + standUpMovementCost + path.Length;
        var maxGoForIts = PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.Sprint)
            ? SprintGoForItsPerActivation
            : MaxGoForItsPerActivation;
        var goForItsUsed = Math.Max(0, totalMovementSquares - player.Stats.Movement);
        if (goForItsUsed > maxGoForIts)
        {
            var movementDescription = isStandingUp
                ? $"{Math.Max(0, player.Stats.Movement - standUpMovementCost)} squares after standing"
                : $"{player.Stats.Movement} squares";
            throw new InvalidOperationException($"{player.Name} can move {movementDescription} plus {maxGoForIts} go-for-its, not {totalMovementSquares}.");
        }

        var startedAction = BeginPlayerAction(match, ruleset, team, player, action, goForItsUsed, totalMovementSquares);
        if (startedAction.Prevented)
        {
            return startedAction.Match;
        }

        var nextMatch = startedAction.Match;
        if (isStandingUp)
        {
            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(current => current.PlayerId == playerId
                        ? current with { State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                        : current)
                    .ToArray(),
                Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} stands up." }]
            };
        }

        var goForItNumber = priorGoForItsUsed;
        var breakTackleUsed = false;

        for (var stepIndex = 0; stepIndex < path.Length; stepIndex++)
        {
            var currentPlacement = nextMatch.Placements.First(current => current.PlayerId == playerId);
            var currentSquare = currentPlacement.Square!;
            var nextSquare = path[stepIndex];

            if (IsMarkedByOpponent(nextMatch, ruleset, opposingTeam, team.Id, playerId, currentSquare))
            {
                var dodgeRoll = _dice.RollD6();
                var opposingTackleZones = CountOpposingTackleZones(nextMatch, ruleset, opposingTeam, team.Id, playerId, nextSquare);
                var breakTackleBonus = BreakTackleBonus(ruleset, player, breakTackleUsed);
                var divingTackle = FindDivingTackler(nextMatch, ruleset, opposingTeam, currentSquare, nextSquare);
                var prehensileTailModifier = PrehensileTailModifier(nextMatch, ruleset, opposingTeam, playerId, currentSquare);
                var baseDodgeTarget = DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier, breakTackleBonus);
                var divingTackleCanMatter = divingTackle is not null &&
                    RollSucceeds(dodgeRoll, baseDodgeTarget, ruleset.Dice) &&
                    !RollSucceeds(dodgeRoll, DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier + 2, breakTackleBonus), ruleset.Dice);
                if (divingTackle is not null && divingTackleCanMatter)
                {
                    var targetWithDivingTackle = DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier + 2, breakTackleBonus);
                    return nextMatch with
                    {
                        PendingDivingTackle = new PendingDivingTackleChoice
                        {
                            DodgingTeamId = team.Id,
                            TacklerTeamId = opposingTeam!.Id,
                            DodgerPlayerId = player.Id,
                            TacklerPlayerId = divingTackle.PlayerId,
                            DodgerSquare = currentSquare,
                            Destination = nextSquare,
                            Roll = dodgeRoll,
                            TargetWithoutDivingTackle = baseDodgeTarget,
                            TargetWithDivingTackle = targetWithDivingTackle,
                            Context = new PendingRerollContext
                            {
                                MatchBeforeRoll = nextMatch,
                                Action = action,
                                Destination = destination,
                                Path = path,
                                StepIndex = stepIndex,
                                MovementAllowance = remainingMovementAllowance,
                                BlitzDefenderPlayerId = blitzDefenderPlayerId,
                                BreakTackleUsed = breakTackleUsed,
                                ArmBarApplies = ArmBarApplies(nextMatch, ruleset, opposingTeam, playerId, currentSquare, nextSquare)
                            }
                        },
                        Log =
                        [
                            .. nextMatch.Log,
                            new MatchLogEntry { Message = $"{FindTeamPlayer(opposingTeam, divingTackle.PlayerId).Name} may use Diving Tackle against {player.Name}'s dodge from {currentSquare.X},{currentSquare.Y}." }
                        ]
                    };
                }

                var dodgeTarget = baseDodgeTarget;
                var usedBreakTackleThisRoll = breakTackleBonus > 0 && dodgeTarget < DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier);
                if (!RollSucceeds(dodgeRoll, dodgeTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(
                        nextMatch,
                        ruleset,
                        team,
                        player,
                        PendingRerollKind.Dodge,
                        dodgeRoll,
                        dodgeTarget,
                        action,
                        destination,
                        path,
                        stepIndex,
                        remainingMovementAllowance,
                        opposingTeam,
                        breakTackleUsed || usedBreakTackleThisRoll,
                        ArmBarApplies(nextMatch, ruleset, opposingTeam, playerId, currentSquare, nextSquare),
                        blitzDefenderPlayerId: blitzDefenderPlayerId);
                }

                breakTackleUsed = breakTackleUsed || usedBreakTackleThisRoll;
                nextMatch = nextMatch with
                {
                    Log =
                    [
                        .. nextMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} dodges from {currentSquare.X},{currentSquare.Y} to {nextSquare.X},{nextSquare.Y}: rolled {dodgeRoll} vs {dodgeTarget}+ ({opposingTackleZones} opposing tackle zones{(prehensileTailModifier > 0 ? ", Prehensile Tail" : "")}{(usedBreakTackleThisRoll ? ", Break Tackle" : "")}), success." }
                    ]
                };
            }

            var tentacles = ApplyTentacles(nextMatch, ruleset, opposingTeam, player, currentSquare);
            nextMatch = tentacles.Match;
            if (tentacles.Held)
            {
                return nextMatch;
            }

            if (stepIndex >= remainingMovementAllowance)
            {
                goForItNumber++;
                var roll = _dice.RollD6();
                var goForItTarget = GoForItTarget(match.Weather);
                if (!RollSucceeds(roll, goForItTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(
                        nextMatch,
                        ruleset,
                        team,
                        player,
                        PendingRerollKind.GoForIt,
                        roll,
                        goForItTarget,
                        action,
                        destination,
                        path,
                        stepIndex,
                        remainingMovementAllowance,
                        opposingTeam,
                        breakTackleUsed,
                        false,
                        goForItNumber,
                        blitzDefenderPlayerId);
                }

                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} go-for-it {goForItNumber}/{goForItsUsed}: rolled {roll} vs {goForItTarget}+, success." }]
                };
            }

            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(current => current.PlayerId == playerId
                        ? current with { Square = nextSquare }
                        : current)
                    .ToArray()
            };
            nextMatch = ApplyShadowing(nextMatch, ruleset, opposingTeam, player, currentSquare, nextSquare);

            if (nextMatch.Ball.CarrierPlayerId is null && nextMatch.Ball.Square == nextSquare)
            {
                var pickupMatch = ResolvePickup(nextMatch, ruleset, team, player, nextSquare, action, destination, path, stepIndex, remainingMovementAllowance, blitzDefenderPlayerId);
                if (pickupMatch.Ball.CarrierPlayerId == playerId)
                {
                    nextMatch = pickupMatch;
                    continue;
                }

                return pickupMatch;
            }
        }

        var completedMoveMatch = nextMatch with
        {
            Activations = nextMatch.Activations,
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = $"Moved {player.Name} to {destination.X},{destination.Y}." }
            ]
        };

        return IsTouchdown(completedMoveMatch, ruleset, team, playerId, destination)
            ? ScoreTouchdown(completedMoveMatch, ruleset, team)
            : completedMoveMatch;
    }

    private MatchState ResolveFailedGoForIt(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PlayerPlacement placement,
        PitchSquare destination,
        int goForItNumber,
        int roll)
    {
        var injury = ResolveFallInjury(player);
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, player.Name, injury);
        var injuryMatch = apothecary.Match;
        injury = apothecary.Injury;
        var ball = match.Ball;
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"{player.Name} go-for-it {goForItNumber}: rolled {roll}, failed." },
            new() { Message = $"{player.Name} falls at {destination.X},{destination.Y} and is {FormatPitchState(injury.State)}." }
        };
        if (injury.Casualty is not null)
        {
            log.Add(new MatchLogEntry { Message = $"{player.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
        }
        log.AddRange(apothecary.Log);

        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBall(match, ruleset, scatterSquare);
            ball = landing.Ball;
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var fallenMatch = injuryMatch with
        {
            Ball = ball,
            Placements = injuryMatch.Placements
                .Select(current => current.PlayerId == player.Id
                    ? ApplyPitchState(injuryMatch, current, injury.State, OccupiesPitch(injury.State) ? destination : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. injuryMatch.Log, .. log]
        };

        return ApplyTurnover(fallenMatch, ruleset, team.Id);
    }

    private MatchState ResolveFailedDodge(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PitchSquare destination,
        int roll,
        int target,
        bool armBarApplies = false)
    {
        var injury = ResolveFallInjury(player, armBarApplies);
        var placement = FindPlacement(match, player.Id)
            ?? throw new InvalidOperationException("Player is not part of this match.");
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, player.Name, injury);
        var injuryMatch = apothecary.Match;
        injury = apothecary.Injury;
        var ball = match.Ball;
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"{player.Name} dodges to {destination.X},{destination.Y}: rolled {roll} vs {target}+, failed." },
            new() { Message = $"{player.Name} falls at {destination.X},{destination.Y} and is {FormatPitchState(injury.State)}{(armBarApplies ? " after Arm Bar" : "")}." }
        };
        if (injury.Casualty is not null)
        {
            log.Add(new MatchLogEntry { Message = $"{player.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
        }
        log.AddRange(apothecary.Log);

        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBall(match, ruleset, scatterSquare);
            ball = landing.Ball;
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var fallenMatch = injuryMatch with
        {
            Ball = ball,
            Placements = injuryMatch.Placements
                .Select(current => current.PlayerId == player.Id
                    ? ApplyPitchState(injuryMatch, current, injury.State, OccupiesPitch(injury.State) ? destination : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. injuryMatch.Log, .. log]
        };

        return ApplyTurnover(fallenMatch, ruleset, team.Id);
    }

    private MatchState ResolvePickup(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PitchSquare square,
        PlayerTurnAction action,
        PitchSquare destination,
        IReadOnlyList<PitchSquare> path,
        int stepIndex,
        int movementAllowance,
        Guid? blitzDefenderPlayerId = null)
    {
        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.PickupRoll, GameEventStage.BeforeRoll, "no-hands"))
        {
            return ResolveFailedPickup(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} has No Hands and cannot pick up the ball." }
                ]
            }, ruleset, team, player, square, roll: 0, target: 7);
        }

        var opposingTackleZones = CountOpposingTackleZones(match, team.Id, player.Id, square);
        var target = PickupTarget(ruleset, player, opposingTackleZones, match.Weather);
        var roll = _dice.RollD6();

        if (RollSucceeds(roll, target, ruleset.Dice))
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = player.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} picks up the ball on {roll} vs {target}+ ({opposingTackleZones} opposing tackle zones)." }
                ]
            };
        }

        return CreatePendingMovementReroll(
            match,
            ruleset,
            team,
            player,
            PendingRerollKind.Pickup,
            roll,
            target,
            action,
            destination,
            path,
            stepIndex,
            movementAllowance,
            blitzDefenderPlayerId: blitzDefenderPlayerId);
    }

    private MatchState CreatePendingMovementReroll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PendingRerollKind kind,
        int roll,
        int target,
        PlayerTurnAction action,
        PitchSquare destination,
        IReadOnlyList<PitchSquare> path,
        int stepIndex,
        int movementAllowance,
        LeagueTeam? opposingTeam = null,
        bool breakTackleUsed = false,
        bool armBarApplies = false,
        int goForItNumber = 0,
        Guid? blitzDefenderPlayerId = null)
    {
        var skillRerolls = AvailableSkillRerolls(ruleset, player, kind);
        if (kind == PendingRerollKind.Dodge && path.Count > 0)
        {
            var dodgeStart = stepIndex == 0
                ? match.Placements.First(placement => placement.PlayerId == player.Id).Square!
                : path[stepIndex - 1];
            if (opposingTeam is not null &&
                IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, player.Id, dodgeStart, GameEventKind.DodgeRoll, GameEventStage.AfterRoll, SkillEffect.CancelDodgeReroll))
            {
                skillRerolls = skillRerolls
                    .Where(skillId => !SkillCatalog
                        .GetSkillsForHook(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.AfterRoll)
                        .Any(skill => string.Equals(skill.Id, skillId, StringComparison.OrdinalIgnoreCase) &&
                            skill.Effects.Contains(SkillEffect.DodgeReroll)))
                    .ToArray();
            }
        }

        if (!CanUseTeamReroll(match, ruleset, team) && skillRerolls.Count == 0)
        {
            var pendingWithoutOptions = new PendingRerollChoice
            {
                TeamId = team.Id,
                PlayerId = player.Id,
                Kind = kind,
                Roll = roll,
                Target = target,
                TeamRerollAvailable = false,
                SkillRerollIds = [],
                Context = new PendingRerollContext
                {
                    MatchBeforeRoll = match,
                    Action = action,
                    Destination = destination,
                    Path = path.ToArray(),
                    StepIndex = stepIndex,
                    MovementAllowance = movementAllowance,
                    GoForItNumber = goForItNumber,
                    BlitzDefenderPlayerId = blitzDefenderPlayerId,
                    BreakTackleUsed = breakTackleUsed,
                    ArmBarApplies = armBarApplies
                }
            };
            return ResolveDeclinedMovementReroll(match, ruleset, team, player, pendingWithoutOptions);
        }

        return match with
        {
            PendingReroll = new PendingRerollChoice
            {
                TeamId = team.Id,
                PlayerId = player.Id,
                Kind = kind,
                Roll = roll,
                Target = target,
                TeamRerollAvailable = CanUseTeamReroll(match, ruleset, team),
                SkillRerollIds = skillRerolls,
                Context = new PendingRerollContext
                {
                    MatchBeforeRoll = match,
                    Action = action,
                    Destination = destination,
                    Path = path.ToArray(),
                    StepIndex = stepIndex,
                    MovementAllowance = movementAllowance,
                    GoForItNumber = goForItNumber,
                    BlitzDefenderPlayerId = blitzDefenderPlayerId,
                    BreakTackleUsed = breakTackleUsed,
                    ArmBarApplies = armBarApplies
                }
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} failed {FormatRerollKind(kind)} on {roll} vs {target}+. Choose whether to reroll." }
            ]
        };
    }

    private MatchState ResolveDeclinedMovementReroll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PendingRerollChoice pending)
    {
        var square = pending.Context.Path[pending.Context.StepIndex];
        return pending.Kind switch
        {
            PendingRerollKind.Dodge => ResolveFailedDodge(match, ruleset, team, player, square, pending.Roll, pending.Target, pending.Context.ArmBarApplies),
            PendingRerollKind.GoForIt => ResolveFailedGoForIt(
                match,
                ruleset,
                team,
                player,
                match.Placements.First(placement => placement.PlayerId == player.Id),
                square,
                pending.Context.GoForItNumber,
                pending.Roll),
            PendingRerollKind.Pickup => ResolveFailedPickup(match, ruleset, team, player, square, pending.Roll, pending.Target),
            _ => throw new InvalidOperationException("Unknown reroll kind.")
        };
    }

    private MatchState ContinueMovementAfterRerollSuccess(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player player,
        PendingRerollChoice pending,
        int reroll)
    {
        var context = pending.Context;
        var path = context.Path.ToArray();
        var nextMatch = match;
        var stepIndex = context.StepIndex;
        var nextSquare = path[stepIndex];
        var goForItNumber = context.GoForItNumber;

        nextMatch = nextMatch with
        {
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = $"{player.Name} succeeds on the rerolled {FormatRerollKind(pending.Kind)}." }
            ]
        };

        if (pending.Kind == PendingRerollKind.Dodge && stepIndex >= context.MovementAllowance)
        {
            goForItNumber++;
            var goForItRoll = _dice.RollD6();
            var goForItTarget = GoForItTarget(match.Weather);
            if (!RollSucceeds(goForItRoll, goForItTarget, ruleset.Dice))
            {
                    return CreatePendingMovementReroll(
                        nextMatch,
                        ruleset,
                        team,
                        player,
                    PendingRerollKind.GoForIt,
                    goForItRoll,
                    goForItTarget,
                    context.Action,
                        context.Destination,
                        path,
                        stepIndex,
                        context.MovementAllowance,
                        opposingTeam,
                        context.BreakTackleUsed,
                        false,
                        goForItNumber,
                        context.BlitzDefenderPlayerId);
            }

            nextMatch = nextMatch with
            {
                Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} go-for-it {goForItNumber}: rolled {goForItRoll} vs {goForItTarget}+, success." }]
            };
        }

        nextMatch = nextMatch with
        {
            Placements = nextMatch.Placements
                .Select(current => current.PlayerId == player.Id
                    ? current with { Square = nextSquare }
                    : current)
                .ToArray()
        };

        if (pending.Kind != PendingRerollKind.Pickup &&
            nextMatch.Ball.CarrierPlayerId is null &&
            nextMatch.Ball.Square == nextSquare)
        {
            var pickupMatch = ResolvePickup(nextMatch, ruleset, team, player, nextSquare, context.Action, context.Destination, path, stepIndex, context.MovementAllowance, context.BlitzDefenderPlayerId);
            if (pickupMatch.Ball.CarrierPlayerId != player.Id)
            {
                return pickupMatch;
            }

            nextMatch = pickupMatch;
        }
        else if (pending.Kind == PendingRerollKind.Pickup)
        {
            nextMatch = nextMatch with { Ball = new BallState { CarrierPlayerId = player.Id } };
        }

        return ContinueMovementFromStep(nextMatch, ruleset, team, opposingTeam, player, context.Action, context.Destination, path, stepIndex + 1, context.MovementAllowance, goForItNumber, context.BreakTackleUsed, context.BlitzDefenderPlayerId);
    }

    private MatchState ContinueMovementFromStep(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player player,
        PlayerTurnAction action,
        PitchSquare destination,
        IReadOnlyList<PitchSquare> path,
        int startStepIndex,
        int movementAllowance,
        int goForItNumber,
        bool breakTackleUsed,
        Guid? blitzDefenderPlayerId = null)
    {
        var nextMatch = match;
        for (var stepIndex = startStepIndex; stepIndex < path.Count; stepIndex++)
        {
            var currentPlacement = nextMatch.Placements.First(current => current.PlayerId == player.Id);
            var currentSquare = currentPlacement.Square!;
            var nextSquare = path[stepIndex];

            if (IsMarkedByOpponent(nextMatch, ruleset, opposingTeam, team.Id, player.Id, currentSquare))
            {
                var dodgeRoll = _dice.RollD6();
                var opposingTackleZones = CountOpposingTackleZones(nextMatch, ruleset, opposingTeam, team.Id, player.Id, nextSquare);
                var breakTackleBonus = BreakTackleBonus(ruleset, player, breakTackleUsed);
                var divingTackle = FindDivingTackler(nextMatch, ruleset, opposingTeam, currentSquare, nextSquare);
                var prehensileTailModifier = PrehensileTailModifier(nextMatch, ruleset, opposingTeam, player.Id, currentSquare);
                var baseDodgeTarget = DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier, breakTackleBonus);
                var divingTackleCanMatter = divingTackle is not null &&
                    RollSucceeds(dodgeRoll, baseDodgeTarget, ruleset.Dice) &&
                    !RollSucceeds(dodgeRoll, DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier + 2, breakTackleBonus), ruleset.Dice);
                if (divingTackle is not null && divingTackleCanMatter)
                {
                    var targetWithDivingTackle = DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier + 2, breakTackleBonus);
                    return nextMatch with
                    {
                        PendingDivingTackle = new PendingDivingTackleChoice
                        {
                            DodgingTeamId = team.Id,
                            TacklerTeamId = opposingTeam!.Id,
                            DodgerPlayerId = player.Id,
                            TacklerPlayerId = divingTackle.PlayerId,
                            DodgerSquare = currentSquare,
                            Destination = nextSquare,
                            Roll = dodgeRoll,
                            TargetWithoutDivingTackle = baseDodgeTarget,
                            TargetWithDivingTackle = targetWithDivingTackle,
                            Context = new PendingRerollContext
                            {
                                MatchBeforeRoll = nextMatch,
                                Action = action,
                                Destination = destination,
                                Path = path,
                                StepIndex = stepIndex,
                                MovementAllowance = movementAllowance,
                                GoForItNumber = goForItNumber,
                                BlitzDefenderPlayerId = blitzDefenderPlayerId,
                                BreakTackleUsed = breakTackleUsed,
                                ArmBarApplies = ArmBarApplies(nextMatch, ruleset, opposingTeam, player.Id, currentSquare, nextSquare)
                            }
                        },
                        Log =
                        [
                            .. nextMatch.Log,
                            new MatchLogEntry { Message = $"{FindTeamPlayer(opposingTeam, divingTackle.PlayerId).Name} may use Diving Tackle against {player.Name}'s dodge from {currentSquare.X},{currentSquare.Y}." }
                        ]
                    };
                }

                var dodgeTarget = baseDodgeTarget;
                var usedBreakTackleThisRoll = breakTackleBonus > 0 && dodgeTarget < DodgeTarget(ruleset, player, opposingTackleZones + prehensileTailModifier);
                if (!RollSucceeds(dodgeRoll, dodgeTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(
                        nextMatch,
                        ruleset,
                        team,
                        player,
                        PendingRerollKind.Dodge,
                        dodgeRoll,
                        dodgeTarget,
                        action,
                        destination,
                        path,
                        stepIndex,
                        movementAllowance,
                        opposingTeam,
                        breakTackleUsed || usedBreakTackleThisRoll,
                        ArmBarApplies(nextMatch, ruleset, opposingTeam, player.Id, currentSquare, nextSquare),
                        goForItNumber,
                        blitzDefenderPlayerId);
                }

                breakTackleUsed = breakTackleUsed || usedBreakTackleThisRoll;
                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} dodges from {currentSquare.X},{currentSquare.Y} to {nextSquare.X},{nextSquare.Y}: rolled {dodgeRoll} vs {dodgeTarget}+{(prehensileTailModifier > 0 ? " with Prehensile Tail" : "")}{(usedBreakTackleThisRoll ? " with Break Tackle" : "")}, success." }]
                };
            }

            var tentacles = ApplyTentacles(nextMatch, ruleset, opposingTeam, player, currentSquare);
            nextMatch = tentacles.Match;
            if (tentacles.Held)
            {
                return nextMatch;
            }

            if (stepIndex >= movementAllowance)
            {
                goForItNumber++;
                var roll = _dice.RollD6();
                var goForItTarget = GoForItTarget(match.Weather);
                if (!RollSucceeds(roll, goForItTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(nextMatch, ruleset, team, player, PendingRerollKind.GoForIt, roll, goForItTarget, action, destination, path, stepIndex, movementAllowance, opposingTeam, breakTackleUsed, false, goForItNumber, blitzDefenderPlayerId);
                }

                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} go-for-it {goForItNumber}: rolled {roll} vs {goForItTarget}+, success." }]
                };
            }

            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(current => current.PlayerId == player.Id
                        ? current with { Square = nextSquare }
                        : current)
                    .ToArray()
            };
            nextMatch = ApplyShadowing(nextMatch, ruleset, opposingTeam, player, currentSquare, nextSquare);

            if (nextMatch.Ball.CarrierPlayerId is null && nextMatch.Ball.Square == nextSquare)
            {
                var pickupMatch = ResolvePickup(nextMatch, ruleset, team, player, nextSquare, action, destination, path, stepIndex, movementAllowance, blitzDefenderPlayerId);
                if (pickupMatch.Ball.CarrierPlayerId != player.Id)
                {
                    return pickupMatch;
                }

                nextMatch = pickupMatch;
            }
        }

        var completedMoveMatch = nextMatch with
        {
            Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"Moved {player.Name} to {destination.X},{destination.Y}." }]
        };

        if (action == PlayerTurnAction.Blitz && blitzDefenderPlayerId is Guid defenderPlayerId)
        {
            var defenderTeam = opposingTeam
                ?? throw new InvalidOperationException("A blitz reroll continuation requires the defending team.");
            var attackerPlacement = ValidateBlock(completedMoveMatch, team, player.Id, defenderTeam, defenderPlayerId);
            var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
            return ResolveBlock(completedMoveMatch, ruleset, team, player, attackerPlacement, defenderTeam, defender);
        }

        return IsTouchdown(completedMoveMatch, ruleset, team, player.Id, destination)
            ? ScoreTouchdown(completedMoveMatch, ruleset, team)
            : completedMoveMatch;
    }

    private MatchState ResolveFailedPickup(MatchState match, Ruleset ruleset, LeagueTeam team, Player player, PitchSquare square, int roll, int target)
    {
        var bounceSquare = ScatterFrom(ruleset, square);
        var bouncedMatch = BounceBall(
            match with
            {
                Ball = new BallState(),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} fails to pick up the ball on {roll} vs {target}+." },
                    new MatchLogEntry { Message = $"Ball bounces to {bounceSquare.X},{bounceSquare.Y}." }
                ]
            },
            ruleset,
            team,
            bounceSquare);

        return bouncedMatch.Ball.CarrierPlayerId is Guid carrierId && FindPlacement(bouncedMatch, carrierId)?.TeamId == team.Id
            ? bouncedMatch
            : ApplyTurnover(bouncedMatch, ruleset, team.Id);
    }

    private MatchState ApplyShadowing(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Player movingPlayer,
        PitchSquare fromSquare,
        PitchSquare toSquare)
    {
        if (opposingTeam is null)
        {
            return match;
        }

        var shadowerPlacement = match.Placements.FirstOrDefault(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.State == PlayerPitchState.Standing &&
            IsAdjacentToPlacement(placement, fromSquare) &&
            !IsAdjacentToPlacement(placement, toSquare) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.AfterEvent, SkillEffect.Shadowing));
        if (shadowerPlacement is null || match.Placements.Any(placement => placement.PlayerId != shadowerPlacement.PlayerId && PlacementOccupiesSquare(placement, fromSquare) && OccupiesPitch(placement.State)))
        {
            return match;
        }

        var shadower = FindTeamPlayer(opposingTeam, shadowerPlacement.PlayerId);
        var roll = _dice.RollD6();
        var total = roll + shadower.Stats.Movement - movingPlayer.Stats.Movement;
        if (total < 6)
        {
            return match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{shadower.Name} attempts Shadowing: rolled {roll} + MA difference = {total}, failed." }
                ]
            };
        }

        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == shadower.Id
                    ? placement with { Square = fromSquare }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{shadower.Name} uses Shadowing and follows to {fromSquare.X},{fromSquare.Y}." }
            ]
        };
    }

    private static int BreakTackleBonus(Ruleset ruleset, Player player, bool breakTackleUsed)
    {
        if (breakTackleUsed || !PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.BreakTackle))
        {
            return 0;
        }

        return player.Stats.Strength >= 5 ? 2 : 1;
    }

    private static PitchSquare[] BuildMovementPath(PitchSquare start, PitchSquare destination)
    {
        var path = new List<PitchSquare>();
        var currentX = start.X;
        var currentY = start.Y;

        while (currentX != destination.X || currentY != destination.Y)
        {
            currentX += Math.Sign(destination.X - currentX);
            currentY += Math.Sign(destination.Y - currentY);
            path.Add(new PitchSquare(currentX, currentY));
        }

        return path.ToArray();
    }
}
