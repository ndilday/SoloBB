using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class MatchService
{
    private const int MaxGoForItsPerActivation = 3;
    private const int SprintGoForItsPerActivation = 4;
    private readonly IDiceRoller _dice;

    public MatchService(IDiceRoller? dice = null)
    {
        _dice = dice ?? new RandomDiceRoller();
    }

    public MatchState CreateHotseatMatch(Ruleset ruleset, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return CreateHotseatMatch(
            ruleset,
            homeTeam,
            awayTeam,
            new TeamInducementPlan { TeamId = homeTeam.Id },
            new TeamInducementPlan { TeamId = awayTeam.Id });
    }

    public MatchState CreateHotseatMatch(
        Ruleset ruleset,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        TeamInducementPlan homeInducements,
        TeamInducementPlan awayInducements)
    {
        if (homeTeam.Id == awayTeam.Id)
        {
            throw new InvalidOperationException("Home and away teams must be different teams.");
        }

        if (homeInducements.TeamId != homeTeam.Id || awayInducements.TeamId != awayTeam.Id)
        {
            throw new InvalidOperationException("Inducement plans must match the selected teams.");
        }

        const int minimumPlayersToField = 3;
        if (homeTeam.Players.Count < minimumPlayersToField || awayTeam.Players.Count < minimumPlayersToField)
        {
            throw new InvalidOperationException($"Both teams must have at least {minimumPlayersToField} players available.");
        }

        return new MatchState
        {
            Id = Guid.NewGuid(),
            RulesetId = ruleset.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ActiveTeamId = awayTeam.Id,
            FirstHalfReceivingTeamId = homeTeam.Id,
            Phase = MatchPhase.DefenseSetup,
            HomeRerollsRemaining = homeTeam.Rerolls,
            AwayRerollsRemaining = awayTeam.Rerolls,
            HomeTeamRerolls = homeTeam.Rerolls,
            AwayTeamRerolls = awayTeam.Rerolls,
            HomeBribesRemaining = homeInducements.Bribes,
            AwayBribesRemaining = awayInducements.Bribes,
            HomeTreasurySpent = homeInducements.TreasurySpent,
            AwayTreasurySpent = awayInducements.TreasurySpent,
            HomeLeaderRerollAvailable = HasLeaderPlayer(ruleset, homeTeam),
            AwayLeaderRerollAvailable = HasLeaderPlayer(ruleset, awayTeam),
            HomeCheerleaders = homeTeam.Cheerleaders,
            AwayCheerleaders = awayTeam.Cheerleaders,
            HomeAssistantCoaches = homeTeam.AssistantCoaches,
            AwayAssistantCoaches = awayTeam.AssistantCoaches,
            HomeApothecariesRemaining = homeTeam.Apothecaries,
            AwayApothecariesRemaining = awayTeam.Apothecaries,
            Placements = CreateInitialPlacements(homeTeam, awayTeam),
            SecretWeaponPlayerIds =
            [
                .. homeTeam.Players.Where(player => PlayerHasSkillId(player, "secret-weapon")).Select(player => player.Id),
                .. awayTeam.Players.Where(player => PlayerHasSkillId(player, "secret-weapon")).Select(player => player.Id)
            ],
            PickMeUpPlayerIds =
            [
                .. homeTeam.Players.Where(player => PlayerHasSkillId(player, "pick-me-up")).Select(player => player.Id),
                .. awayTeam.Players.Where(player => PlayerHasSkillId(player, "pick-me-up")).Select(player => player.Id)
            ],
            Log =
            [
                new MatchLogEntry { Message = $"Created hotseat match: {homeTeam.Name} vs {awayTeam.Name}. Defense sets up first." }
            ]
        };
    }

    public MatchState AdvancePhase(MatchState match, Ruleset? ruleset = null)
    {
        EnsureNoPendingChoices(match);

        var nextPhase = match.Phase;
        var nextActiveTeam = match.ActiveTeamId;
        var message = "";

        switch (match.Phase)
        {
            case MatchPhase.DefenseSetup:
                if (ruleset is not null)
                {
                    ValidateSetupComplete(match, ruleset, match.ActiveTeamId);
                }
                nextPhase = MatchPhase.OffenseSetup;
                nextActiveTeam = GetOpponentTeamId(match, match.ActiveTeamId);
                message = "Defense setup complete. Offense sets up next.";
                break;
            case MatchPhase.OffenseSetup:
                if (ruleset is not null)
                {
                    ValidateSetupComplete(match, ruleset, match.ActiveTeamId);
                }
                nextPhase = MatchPhase.Kickoff;
                message = "Offense setup complete. Ready for kickoff.";
                break;
            case MatchPhase.Kickoff:
                return match;
            case MatchPhase.OffensivePlayerTurn:
                return EndActivePlayerTurn(match, ruleset: null, "Offensive player turn complete. Defensive turn begins.");
            case MatchPhase.EndOfHalf:
                return StartSecondHalfSetup(match);
            case MatchPhase.DefensiveTurn:
            case MatchPhase.Complete:
                return match;
        }

        return match with
        {
            Phase = nextPhase,
            ActiveTeamId = nextActiveTeam,
            Activations = [],
            PendingPush = null,
            PendingReroll = null,
            PendingApothecary = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBallPlacement = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    public MatchState AdvanceTurn(MatchState match, Ruleset ruleset)
    {
        EnsureNoPendingChoices(match);

        if (match.Phase is MatchPhase.Complete)
        {
            return match;
        }

        if (match.Phase is MatchPhase.OffensivePlayerTurn)
        {
            return EndActivePlayerTurn(match, ruleset, "Offensive player turn complete. Defensive turn begins.");
        }

        if (match.Phase is not MatchPhase.DefensiveTurn)
        {
            return AdvancePhase(match, ruleset);
        }

        var recoveredMatch = RecoverStunnedPlayers(match, match.ActiveTeamId);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, recoveredMatch.ActiveTeamId);
        if (BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            return AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var nextActiveTeam = GetOpponentTeamId(consumedTurnMatch, consumedTurnMatch.ActiveTeamId);
        var nextMatch = consumedTurnMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            ActiveTeamId = nextActiveTeam,
            Turn = GetTeamTurn(consumedTurnMatch, nextActiveTeam),
            Activations = [],
            PendingPush = null,
            PendingReroll = null,
            PendingApothecary = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBallPlacement = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Advanced to half {consumedTurnMatch.Half}, {FormatTeamTurn(consumedTurnMatch, nextActiveTeam)}, phase {MatchPhase.OffensivePlayerTurn}." }
            ]
        };

        return nextMatch;
    }

    private static void EnsureNoPendingChoices(MatchState match)
    {
        if (match.PendingReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending reroll before advancing the turn.");
        }

        if (match.PendingApothecary is not null)
        {
            throw new InvalidOperationException("Resolve the pending apothecary choice before advancing the turn.");
        }

        if (match.PendingStandFirm is not null)
        {
            throw new InvalidOperationException("Resolve the pending Stand Firm choice before advancing the turn.");
        }

        if (match.PendingDivingTackle is not null)
        {
            throw new InvalidOperationException("Resolve the pending Diving Tackle choice before advancing the turn.");
        }

        if (match.PendingFollowUp is not null)
        {
            throw new InvalidOperationException("Resolve the pending follow-up choice before advancing the turn.");
        }

        if (match.PendingBallPlacement is not null)
        {
            throw new InvalidOperationException("Resolve the pending ball placement before advancing the turn.");
        }

        if (match.PendingBombThrow is not null)
        {
            throw new InvalidOperationException("Resolve the pending bomb throw before advancing the turn.");
        }

        if (match.PendingMultipleBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending Multiple Block continuation before advancing the turn.");
        }

        if (match.PendingSendOff is not null)
        {
            throw new InvalidOperationException("Resolve the pending send-off choice before advancing the turn.");
        }

        if (match.PendingBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending block choice before advancing the turn.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before advancing the turn.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before advancing the turn.");
        }

        if (match.PendingKickoffEvent is not null)
        {
            throw new InvalidOperationException("Resolve the pending kickoff event before advancing the turn.");
        }
    }

    public MatchState PlacePlayer(MatchState match, Ruleset ruleset, Guid playerId, PitchSquare square)
    {
        if (match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
        {
            throw new InvalidOperationException("Players can only be placed during setup.");
        }

        if (square.X < 0 || square.X >= ruleset.PitchWidth || square.Y < 0 || square.Y >= ruleset.PitchHeight)
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is outside the pitch.");
        }

        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active setup team can place players.");
        }

        if (placement.State is not (PlayerPitchState.Reserve or PlayerPitchState.Standing))
        {
            throw new InvalidOperationException("Only available players can be placed.");
        }

        if (placement.Square is null && CountTeamPlayersOnPitch(match, placement.TeamId) >= ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"A team can set up no more than {ruleset.PlayersPerSide} players.");
        }

        if (!IsLegalSetupSide(match, ruleset, placement.TeamId, square))
        {
            throw new InvalidOperationException("Player must be placed on their team's side of the pitch.");
        }

        if (IsWideZone(ruleset, square) && CountTeamPlayersInWideZone(match, ruleset, placement.TeamId, square, playerId) >= 2)
        {
            throw new InvalidOperationException("A team can place no more than two players in the same wide zone.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && current.Square == square))
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is already occupied.");
        }

        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId
                    ? current with { Square = square, State = PlayerPitchState.Standing, TackleZonesLost = false, Rooted = false, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : current)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"Placed player {playerId} at {square.X},{square.Y}." }
            ]
        };
    }

    public MatchState MovePlayer(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination, LeagueTeam? opposingTeam = null)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.Move, opposingTeam);
    }

    public MatchState DeclarePlayerAction(MatchState match, LeagueTeam team, Guid playerId, PlayerTurnAction action)
    {
        var player = FindTeamPlayer(team, playerId);
        EnsureCanDeclarePlayerAction(match, team, player, action, allowMatchingDeclaration: false);

        return AddActivation(match, playerId, team.Id, action, goForItsUsed: 0, declaredOnly: true) with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} declares {FormatPlayerTurnAction(action)}." }
            ]
        };
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

        if (match.Placements.Any(current => current.PlayerId != playerId && current.Square == destination && OccupiesPitch(current.State)))
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

    public MatchState HandOffBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid carrierPlayerId,
        Guid receiverPlayerId,
        LeagueTeam? opposingTeam = null)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only hand off during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can hand off during its turn.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (match.Ball.CarrierPlayerId != carrierPlayerId)
        {
            throw new InvalidOperationException("The selected player is not carrying the ball.");
        }

        if (carrierPlayerId == receiverPlayerId)
        {
            throw new InvalidOperationException("A player cannot hand off to themselves.");
        }

        var carrier = FindTeamPlayer(team, carrierPlayerId);
        var receiver = FindTeamPlayer(team, receiverPlayerId);
        var carrierPlacement = FindStandingPlacement(match, carrierPlayerId, team.Id, "carrier");
        var receiverPlacement = FindStandingPlacement(match, receiverPlayerId, team.Id, "receiver");

        if (!IsAdjacent(carrierPlacement.Square!, receiverPlacement.Square!))
        {
            throw new InvalidOperationException("Hand offs require adjacent players.");
        }

        var handOffAction = BeginPlayerAction(match, ruleset, team, carrier, PlayerTurnAction.HandOff, goForItsUsed: 0);
        if (handOffAction.Prevented)
        {
            return handOffAction.Match;
        }

        var activatedMatch = handOffAction.Match;
        var handOffTackleZones = PlayerHasHookedEffect(ruleset, receiver, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
            ? 0
            : CountOpposingTackleZones(match, team.Id, receiver.Id, receiverPlacement.Square!);
        var disturbingPresence = DisturbingPresenceModifier(match, ruleset, opposingTeam, receiverPlacement.Square!);
        var target = CatchTarget(ruleset, receiver, match.Weather, handOffTackleZones, disturbingPresence);
        var catchAttempt = RollCatch(ruleset, receiver, target);

        if (catchAttempt.Success)
        {
            return activatedMatch with
            {
                Ball = new BallState { CarrierPlayerId = receiverPlayerId },
                Log =
                [
                    .. activatedMatch.Log,
                    new MatchLogEntry { Message = $"{carrier.Name} hands off to {receiver.Name}: {FormatCatchAttempt(catchAttempt, target)}, success." }
                ]
            };
        }

        var scatterSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var bouncedMatch = ResolveBallLanding(activatedMatch, ruleset, team, scatterSquare, opposingTeam: opposingTeam);
        var failedMatch = bouncedMatch with
        {
            Log =
            [
                .. bouncedMatch.Log,
                new MatchLogEntry { Message = $"{carrier.Name} hands off to {receiver.Name}: {FormatCatchAttempt(catchAttempt, target)}, failed." },
                new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." }
            ]
        };

        return failedMatch.Ball.CarrierPlayerId is Guid carrierId && FindPlacement(failedMatch, carrierId)?.TeamId == team.Id
            ? failedMatch
            : ApplyTurnover(failedMatch, ruleset, team.Id);
    }

    public MatchState UseFumblerooskie(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare vacatedSquare)
    {
        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can use Fumblerooskie during its turn.");
        }

        if (match.Ball.CarrierPlayerId != playerId)
        {
            throw new InvalidOperationException("Only the ball carrier can use Fumblerooskie.");
        }

        var player = FindTeamPlayer(team, playerId);
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.AfterEvent, SkillEffect.Fumblerooskie))
        {
            throw new InvalidOperationException($"{player.Name} does not have Fumblerooskie.");
        }

        var placement = FindStandingPlacement(match, playerId, team.Id, "player");
        if (!IsOnPitch(ruleset, vacatedSquare) || vacatedSquare == placement.Square)
        {
            throw new InvalidOperationException("Fumblerooskie must place the ball in a square the player vacated during movement.");
        }

        var activation = GetActivation(match, playerId, team.Id)
            ?? throw new InvalidOperationException("Fumblerooskie can only be used during this player's Move or Blitz action.");
        if (activation.Action is not (PlayerTurnAction.Move or PlayerTurnAction.Blitz))
        {
            throw new InvalidOperationException("Fumblerooskie can only be used during a Move or Blitz action.");
        }

        return match with
        {
            Ball = new BallState { Square = vacatedSquare },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} uses Fumblerooskie and drops the ball at {vacatedSquare.X},{vacatedSquare.Y}." }
            ]
        };
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

        if (path.Any(square => match.Placements.Any(current => current.PlayerId != playerId && current.Square == square && OccupiesPitch(current.State))))
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

    public MatchState PassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        Guid receiverPlayerId,
        LeagueTeam? defendingTeam = null,
        bool usePassSkillReroll = false,
        bool useCloudBurster = false)
    {
        var receiverPlacement = FindStandingPlacement(match, receiverPlayerId, team.Id, "receiver");
        if (passerPlayerId == receiverPlayerId)
        {
            throw new InvalidOperationException("A player cannot pass to themselves.");
        }

        return PassBall(match, ruleset, team, passerPlayerId, receiverPlacement.Square!, defendingTeam, usePassSkillReroll, useCloudBurster);
    }

    public MatchState PassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? defendingTeam = null,
        bool usePassSkillReroll = false,
        bool useCloudBurster = false)
    {
        return PassBallCore(match, ruleset, team, passerPlayerId, targetSquare, defendingTeam, usePassSkillReroll, useCloudBurster, isDumpOff: false, isHailMary: false);
    }

    public MatchState HailMaryPassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        PitchSquare targetSquare,
        bool usePassSkillReroll = false)
    {
        return PassBallCore(match, ruleset, team, passerPlayerId, targetSquare, defendingTeam: null, usePassSkillReroll, useCloudBurster: false, isDumpOff: false, isHailMary: true);
    }

    public MatchState DumpOffPassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? defendingTeam = null,
        bool usePassSkillReroll = false,
        bool useCloudBurster = false)
    {
        return PassBallCore(match, ruleset, team, passerPlayerId, targetSquare, defendingTeam, usePassSkillReroll, useCloudBurster, isDumpOff: true, isHailMary: false);
    }

    private MatchState PassBallCore(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? defendingTeam,
        bool usePassSkillReroll,
        bool useCloudBurster,
        bool isDumpOff,
        bool isHailMary)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only pass during a player turn.");
        }

        if (!isDumpOff && team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can pass during its turn.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (match.Ball.CarrierPlayerId != passerPlayerId)
        {
            throw new InvalidOperationException("The selected player is not carrying the ball.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before taking another action.");
        }

        var passerPlayer = FindTeamPlayer(team, passerPlayerId);
        var passerPlacement = FindStandingPlacement(match, passerPlayerId, team.Id, "passer");
        if (isDumpOff &&
            !PlayerHasHookedEffect(ruleset, passerPlayer, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.DumpOff))
        {
            throw new InvalidOperationException($"{passerPlayer.Name} does not have Dump-Off.");
        }

        if (isHailMary)
        {
            if (!PlayerHasHookedEffect(ruleset, passerPlayer, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.HailMaryPass))
            {
                throw new InvalidOperationException($"{passerPlayer.Name} does not have Hail Mary Pass.");
            }

            if (match.Weather == WeatherCondition.Blizzard)
            {
                throw new InvalidOperationException("Hail Mary Pass cannot be used in a blizzard.");
            }
        }

        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException("Pass target must be on the pitch.");
        }

        var targetPlacement = match.Placements.FirstOrDefault(placement =>
            placement.Square == targetSquare &&
            placement.State == PlayerPitchState.Standing);
        if (targetPlacement?.PlayerId == passerPlayerId)
        {
            throw new InvalidOperationException("A player cannot pass to themselves.");
        }

        var receiverPlayer = targetPlacement?.TeamId == team.Id
            ? FindTeamPlayer(team, targetPlacement.PlayerId)
            : null;
        var passRange = isHailMary
            ? new PassRange("hail mary", 0)
            : isDumpOff
                ? new PassRange("quick", 0)
                : ResolvePassRange(passerPlacement.Square!, targetSquare);
        if (isDumpOff && ResolvePassRange(passerPlacement.Square!, targetSquare).Name != "quick")
        {
            throw new InvalidOperationException("Dump-Off can only make a Quick Pass.");
        }

        var passerTackleZones = PlayerHasHookedEffect(ruleset, passerPlayer, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
            ? 0
            : CountOpposingTackleZones(match, team.Id, passerPlayerId, passerPlacement.Square!);
        var passerDisturbingPresence = DisturbingPresenceModifier(match, ruleset, defendingTeam, passerPlacement.Square!);
        var target = PassingTarget(ruleset, passerPlayer, passRange, match.Weather, passerTackleZones, passerDisturbingPresence);
        var passAction = isDumpOff
            ? new ActionStart(match, Prevented: false)
            : BeginPlayerAction(match, ruleset, team, passerPlayer, PlayerTurnAction.Pass, goForItsUsed: 0);
        if (passAction.Prevented)
        {
            return passAction.Match;
        }

        var passAttempt = RollPass(ruleset, passerPlayer, target, usePassSkillReroll);
        var passRoll = passAttempt.FinalRoll;
        var activatedMatch = (isDumpOff
            ? match
            : passAction.Match) with
        {
            Ball = new BallState()
        };

        if (passAttempt.Fumbled)
        {
            if (passAttempt.SafePassPreventedFumble)
            {
                return activatedMatch with
                {
                    Ball = new BallState { CarrierPlayerId = passerPlayerId },
                    Log =
                    [
                        .. activatedMatch.Log,
                        new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {targetSquare.X},{targetSquare.Y}: {passRange.Name} {FormatPassAttempt(passAttempt, target)}, Safe Pass prevents the fumble." }
                    ]
                };
            }

            var bounceSquare = ScatterFrom(ruleset, passerPlacement.Square!);
            var bouncedMatch = ResolveBallLanding(activatedMatch, ruleset, team, bounceSquare, opposingTeam: defendingTeam);
            var fumbledMatch = bouncedMatch with
            {
                Log =
                [
                    .. bouncedMatch.Log,
                    new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {targetSquare.X},{targetSquare.Y}: {passRange.Name} {FormatPassAttempt(passAttempt, target)}, fumbled." },
                    new MatchLogEntry { Message = $"Ball bounces to {bounceSquare.X},{bounceSquare.Y}." }
                ]
            };

            return fumbledMatch.Ball.CarrierPlayerId is Guid fumbleCarrierId && FindPlacement(fumbledMatch, fumbleCarrierId)?.TeamId == team.Id
                ? fumbledMatch
                : isDumpOff
                    ? fumbledMatch
                    : ApplyTurnover(fumbledMatch, ruleset, team.Id);
        }

        if (passAttempt.Success && !isHailMary)
        {
            var eligibleInterceptors = defendingTeam is null
                ? Array.Empty<PlayerPlacement>()
                : FindEligibleInterceptors(match, defendingTeam.Id, passerPlacement.Square!, targetSquare);
            var accuratePassMatch = activatedMatch with
            {
                Log =
                [
                    .. activatedMatch.Log,
                    new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {PassTargetName(receiverPlayer, targetSquare)}: {passRange.Name} {FormatPassAttempt(passAttempt, target)} ({passerTackleZones} opposing tackle zones), accurate." }
                ]
            };

            if (eligibleInterceptors.Length > 1)
            {
                return accuratePassMatch with
                {
                    PendingInterception = new PendingInterceptionChoice
                    {
                        PassingTeamId = team.Id,
                        DefendingTeamId = defendingTeam!.Id,
                        PasserPlayerId = passerPlayerId,
                        ReceiverPlayerId = receiverPlayer?.Id,
                        TargetSquare = targetSquare,
                        EligiblePlayerIds = eligibleInterceptors.Select(placement => placement.PlayerId).ToArray(),
                        PassRoll = passRoll,
                        PassTarget = target,
                        PassRangeName = passRange.Name,
                        UseCloudBurster = useCloudBurster
                    },
                    Log =
                    [
                        .. accuratePassMatch.Log,
                        new MatchLogEntry { Message = $"Choose one of {eligibleInterceptors.Length} eligible defenders to attempt an interception." }
                    ]
                };
            }

            if (eligibleInterceptors.Length == 1)
            {
                return ResolveInterceptionAttempt(
                    accuratePassMatch,
                    ruleset,
                    team,
                    defendingTeam!,
                    passerPlayer,
                    receiverPlayer,
                    targetSquare,
                    eligibleInterceptors[0],
                    passRange.Name,
                    passRoll,
                    target,
                    useCloudBurster);
            }

            return ResolvePassLanding(accuratePassMatch, ruleset, team, defendingTeam, passerPlayer, receiverPlayer, targetSquare, passRange.Name, passRoll, target);
        }

        var inaccurateSquare = ScatterFrom(ruleset, targetSquare);
        var inaccurateMatch = ResolveBallLanding(activatedMatch, ruleset, team, inaccurateSquare, opposingTeam: defendingTeam);
        var failedMatch = inaccurateMatch with
        {
            Log =
            [
                .. inaccurateMatch.Log,
                new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {PassTargetName(receiverPlayer, targetSquare)}: {passRange.Name} {FormatPassAttempt(passAttempt, target)} ({passerTackleZones} opposing tackle zones), inaccurate." },
                new MatchLogEntry { Message = $"Ball scatters to {inaccurateSquare.X},{inaccurateSquare.Y}." }
            ]
        };

        return isDumpOff || failedMatch.Ball.CarrierPlayerId is Guid recoveredCarrierId && FindPlacement(failedMatch, recoveredCarrierId)?.TeamId == team.Id
            ? failedMatch
            : ApplyTurnover(failedMatch, ruleset, team.Id);
    }

    public MatchState ChooseInterceptor(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam passingTeam,
        LeagueTeam defendingTeam,
        Guid interceptorPlayerId)
    {
        var pending = match.PendingInterception
            ?? throw new InvalidOperationException("There is no pending interception choice.");

        if (pending.PassingTeamId != passingTeam.Id || pending.DefendingTeamId != defendingTeam.Id)
        {
            throw new InvalidOperationException("Pending interception teams do not match the selected teams.");
        }

        if (!pending.EligiblePlayerIds.Contains(interceptorPlayerId))
        {
            throw new InvalidOperationException("Selected player is not eligible to intercept this pass.");
        }

        var receiver = pending.ReceiverPlayerId is Guid receiverPlayerId
            ? FindTeamPlayer(passingTeam, receiverPlayerId)
            : null;
        var passer = FindTeamPlayer(passingTeam, pending.PasserPlayerId);
        var interceptorPlacement = FindStandingPlacement(match, interceptorPlayerId, defendingTeam.Id, "interceptor");

        return ResolveInterceptionAttempt(
            match with { PendingInterception = null },
            ruleset,
            passingTeam,
            defendingTeam,
            passer,
            receiver,
            pending.TargetSquare,
            interceptorPlacement,
            pending.PassRangeName,
            pending.PassRoll,
            pending.PassTarget,
            pending.UseCloudBurster);
    }

    private MatchState ResolveInterceptionAttempt(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam passingTeam,
        LeagueTeam defendingTeam,
        Player passer,
        Player? receiver,
        PitchSquare targetSquare,
        PlayerPlacement interceptorPlacement,
        string passRangeName,
        int passRoll,
        int passTarget,
        bool useCloudBurster)
    {
        var interceptor = FindTeamPlayer(defendingTeam, interceptorPlacement.PlayerId);
        var interceptionRoll = _dice.RollD6();
        var interceptorSquare = interceptorPlacement.Square!;
        var interceptionTackleZones = PlayerHasHookedEffect(ruleset, interceptor, GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
            ? 0
            : CountOpposingTackleZones(match, defendingTeam.Id, interceptor.Id, interceptorSquare);
        var cloudBursterApplies = useCloudBurster &&
            PlayerHasHookedEffect(ruleset, passer, GameEventKind.InterceptionRoll, GameEventStage.AfterRoll, SkillEffect.CloudBurster) &&
            !PlayerHasHookedEffect(ruleset, interceptor, GameEventKind.InterceptionRoll, GameEventStage.AfterRoll, SkillEffect.VeryLongLegs) &&
            IsLongPass(passRangeName);
        var interceptionDisturbingPresence = DisturbingPresenceModifier(match, ruleset, passingTeam, interceptorSquare);
        var interceptionTarget = InterceptionTarget(ruleset, interceptor, match.Weather, interceptionTackleZones, interceptionDisturbingPresence);

        if (RollSucceeds(interceptionRoll, interceptionTarget, ruleset.Dice))
        {
            if (cloudBursterApplies)
            {
                var cloudBursterReroll = _dice.RollD6();
                if (!RollSucceeds(cloudBursterReroll, interceptionTarget, ruleset.Dice))
                {
                    var cloudFailedMatch = match with
                    {
                        Log =
                        [
                            .. match.Log,
                            new MatchLogEntry { Message = $"{passer.Name} uses Cloud Burster: {interceptor.Name}'s successful interference is rerolled to {cloudBursterReroll} vs {interceptionTarget}+, failed." }
                        ]
                    };

                    return ResolvePassLanding(cloudFailedMatch, ruleset, passingTeam, defendingTeam, passer, receiver, targetSquare, passRangeName, passRoll, passTarget);
                }

                match = match with
                {
                    Log =
                    [
                        .. match.Log,
                        new MatchLogEntry { Message = $"{passer.Name} uses Cloud Burster: {interceptor.Name}'s successful interference is rerolled to {cloudBursterReroll} vs {interceptionTarget}+, still successful." }
                    ]
                };
            }

            var interceptedMatch = match with
            {
                Ball = new BallState { CarrierPlayerId = interceptor.Id },
                PlayerAwards = AddPlayerAward(match, defendingTeam.Id, interceptor.Id, MatchPlayerAwardKind.Interception, 2, teamName: defendingTeam.Name, playerName: interceptor.Name),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{interceptor.Name} intercepts the {passRangeName} pass on {interceptionRoll} vs {interceptionTarget}+ ({interceptionTackleZones} opposing tackle zones)." }
                ]
            };

            return ApplyTurnover(interceptedMatch, ruleset, passingTeam.Id);
        }

        var failedInterceptionMatch = match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{interceptor.Name} attempts to intercept the {passRangeName} pass: rolled {interceptionRoll} vs {interceptionTarget}+ ({interceptionTackleZones} opposing tackle zones), failed." }
            ]
        };

        return ResolvePassLanding(failedInterceptionMatch, ruleset, passingTeam, defendingTeam, passer, receiver, targetSquare, passRangeName, passRoll, passTarget);
    }

    private MatchState ResolvePassLanding(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player passer,
        Player? intendedReceiver,
        PitchSquare targetSquare,
        string passRangeName,
        int passRoll,
        int passTarget)
    {
        var receiverPlacement = match.Placements.FirstOrDefault(placement =>
            placement.Square == targetSquare &&
            placement.TeamId == team.Id &&
            placement.State == PlayerPitchState.Standing);
        if (receiverPlacement is null)
        {
            var landedMatch = ResolveBallLanding(match, ruleset, team, targetSquare, allowDivingCatch: false, opposingTeam: opposingTeam);
            return landedMatch.Ball.CarrierPlayerId is Guid recoveredCarrierId && FindPlacement(landedMatch, recoveredCarrierId)?.TeamId == team.Id
                ? landedMatch
                : ApplyTurnover(landedMatch with
                {
                    Log =
                    [
                        .. landedMatch.Log,
                        new MatchLogEntry { Message = $"{passer.Name}'s accurate pass lands at {targetSquare.X},{targetSquare.Y} with no friendly catch." }
                    ]
                }, ruleset, team.Id);
        }

        var receiver = FindTeamPlayer(team, receiverPlacement.PlayerId);
        var catchTackleZones = PlayerHasHookedEffect(ruleset, receiver, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
            ? 0
            : CountOpposingTackleZones(match, team.Id, receiver.Id, receiverPlacement.Square!);
        var catchDisturbingPresence = DisturbingPresenceModifier(match, ruleset, opposingTeam, receiverPlacement.Square!);
        var catchTarget = CatchTarget(ruleset, receiver, match.Weather, catchTackleZones, catchDisturbingPresence);
        var catchAttempt = RollCatch(ruleset, receiver, catchTarget);

        if (catchAttempt.Success)
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                PlayerAwards = AddPlayerAward(match, team.Id, passer.Id, MatchPlayerAwardKind.Completion, 1, teamName: team.Name, playerName: passer.Name),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, {FormatCatchAttempt(catchAttempt, catchTarget)} ({catchTackleZones} opposing tackle zones), complete." }
                ]
            };
        }

        var scatterSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var bouncedMatch = ResolveBallLanding(match, ruleset, team, scatterSquare, opposingTeam: opposingTeam);
        var droppedMatch = bouncedMatch with
        {
            Log =
            [
                .. bouncedMatch.Log,
                new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, {FormatCatchAttempt(catchAttempt, catchTarget)} ({catchTackleZones} opposing tackle zones), dropped." },
                new MatchLogEntry { Message = $"Ball bounces to {scatterSquare.X},{scatterSquare.Y}." }
            ]
        };

        return droppedMatch.Ball.CarrierPlayerId is Guid carrierId && FindPlacement(droppedMatch, carrierId)?.TeamId == team.Id
            ? droppedMatch
            : ApplyTurnover(droppedMatch, ruleset, team.Id);
    }

    public MatchState ResolveKickoff(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam, PitchSquare targetSquare, LeagueTeam? kickingTeam = null)
    {
        if (match.Phase is not MatchPhase.Kickoff)
        {
            throw new InvalidOperationException("Kickoff can only be resolved during the kickoff phase.");
        }

        if (receivingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("The active team must receive the kickoff.");
        }

        var eventRoll = Roll2D6Detailed();
        var kickingTeamId = GetOpponentTeamId(match, receivingTeam.Id);
        var eventResult = ResolveKickoffEvent(match, ruleset, receivingTeam.Id, kickingTeamId, eventRoll.Total);
        var kickoffMatch = eventResult.Match;
        var rawScatterDistance = _dice.RollD6();
        var scatterDistance = kickingTeam is not null && HasKickPlayer(kickoffMatch, ruleset, kickingTeam)
            ? rawScatterDistance / 2
            : rawScatterDistance;
        var scatterSquare = ScatterFrom(ruleset, targetSquare, scatterDistance);
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"Kickoff event roll {eventRoll.Total}: {eventResult.Name}. {eventResult.Message}" },
            new() { Message = kickingTeam is not null && HasKickPlayer(kickoffMatch, ruleset, kickingTeam)
                ? $"Kickoff targeted {targetSquare.X},{targetSquare.Y}; Kick halves scatter from {rawScatterDistance} to {scatterDistance}, landing at {scatterSquare.X},{scatterSquare.Y}."
                : $"Kickoff targeted {targetSquare.X},{targetSquare.Y} and scattered {scatterDistance} square{(scatterDistance == 1 ? "" : "s")} to {scatterSquare.X},{scatterSquare.Y}." }
        };

        if (eventResult.ExtraScatter)
        {
            var gustSquare = ScatterFrom(ruleset, scatterSquare);
            log.Add(new MatchLogEntry { Message = $"Changing weather gust scatters the ball to {gustSquare.X},{gustSquare.Y}." });
            scatterSquare = gustSquare;
        }

        if (eventResult.PendingKind is KickoffEventKind pendingKind &&
            IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            var pending = CreatePendingKickoffEvent(kickoffMatch, ruleset, pendingKind, receivingTeam.Id, kickingTeamId, scatterSquare);
            if (pending is not null)
            {
                return kickoffMatch with
                {
                    Ball = new BallState { Square = scatterSquare },
                    PendingKickoffEvent = pending,
                    Log =
                    [
                        .. kickoffMatch.Log,
                        .. log,
                        new MatchLogEntry { Message = $"{FormatKickoffEventKind(pendingKind)} requires a choice before the ball lands." }
                    ]
                };
            }
        }

        if (!IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            var touchbackReceiver = FindTouchbackReceiver(kickoffMatch, receivingTeam)
                ?? throw new InvalidOperationException("Receiving team has no standing player for touchback.");

            return kickoffMatch with
            {
                Phase = MatchPhase.OffensivePlayerTurn,
                DriveState = DriveState.InProgress,
                Ball = new BallState { CarrierPlayerId = touchbackReceiver.Id },
                Activations = [],
                PendingKickoffEvent = null,
                Log =
                [
                    .. kickoffMatch.Log,
                    .. log,
                    new MatchLogEntry { Message = $"Touchback. {touchbackReceiver.Name} receives the ball." }
                ]
            };
        }

        var bouncedMatch = ResolveBallLanding(kickoffMatch, ruleset, receivingTeam, scatterSquare);
        return bouncedMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            DriveState = DriveState.InProgress,
            Activations = [],
            PendingKickoffEvent = null,
            Log =
            [
                .. bouncedMatch.Log,
                .. log,
                new MatchLogEntry { Message = "Kickoff resolved. Offensive player turn begins." }
            ]
        };
    }

    private KickoffEventResult ResolveKickoffEvent(MatchState match, Ruleset ruleset, Guid receivingTeamId, Guid kickingTeamId, int roll)
    {
        return roll switch
        {
            2 => ResolveGetTheRef(match),
            3 => ResolveTimeOut(match, ruleset),
            4 => new KickoffEventResult(match, "Solid Defence", "The kicking team may reposition open players before the ball lands.", PendingKind: KickoffEventKind.SolidDefence),
            5 => new KickoffEventResult(match, "High Kick", "The receiving team may move one open player under the ball.", PendingKind: KickoffEventKind.HighKick),
            6 => ResolveCheeringFansOrBrilliantCoaching(match, receivingTeamId, kickingTeamId, "Cheering Fans", useCheerleaders: true),
            7 => ResolveCheeringFansOrBrilliantCoaching(match, receivingTeamId, kickingTeamId, "Brilliant Coaching", useCheerleaders: false),
            8 => ResolveChangingWeather(match),
            9 => new KickoffEventResult(match, "Quick Snap", "The receiving team may move open players one square before the ball lands.", PendingKind: KickoffEventKind.QuickSnap),
            10 => new KickoffEventResult(match, "Blitz", "The kicking team may move open players one square before the ball lands.", PendingKind: KickoffEventKind.Blitz),
            11 => ResolveThrowARock(match),
            12 => ResolvePitchInvasion(match),
            _ => new KickoffEventResult(match, "Kickoff", "No kickoff event.")
        };
    }

    public MatchState MovePendingKickoffEventPlayer(MatchState match, Ruleset ruleset, Guid playerId, PitchSquare destination)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (!pending.EligiblePlayerIds.Contains(playerId) || pending.MovedPlayerIds.Contains(playerId))
        {
            throw new InvalidOperationException("That player cannot move for this kickoff event.");
        }

        var placement = FindPlacement(match, playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != pending.TeamId || placement.Square is not PitchSquare source || placement.State != PlayerPitchState.Standing)
        {
            throw new InvalidOperationException("Only eligible standing players on the pitch can move for this kickoff event.");
        }

        if (!IsOnPitch(ruleset, destination))
        {
            throw new InvalidOperationException("Kickoff event movement must stay on the pitch.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && current.Square == destination && OccupiesPitch(current.State)))
        {
            throw new InvalidOperationException("Kickoff event movement requires an empty destination.");
        }

        if (pending.Kind == KickoffEventKind.SolidDefence)
        {
            if (!IsLegalSetupSide(match, ruleset, pending.TeamId, destination))
            {
                throw new InvalidOperationException("Solid Defence must keep players on the kicking team's setup side.");
            }

            if (IsWideZone(ruleset, destination) && CountTeamPlayersInWideZone(match, ruleset, pending.TeamId, destination, playerId) >= 2)
            {
                throw new InvalidOperationException("Solid Defence cannot place more than two players in the same wide zone.");
            }
        }
        else if (pending.Kind == KickoffEventKind.HighKick)
        {
            if (destination != pending.LandingSquare)
            {
                throw new InvalidOperationException("High Kick can only move the chosen player under the ball.");
            }
        }
        else if (!IsAdjacent(source, destination))
        {
            throw new InvalidOperationException("This kickoff event allows a one-square move.");
        }

        var nextPending = pending with
        {
            MovedPlayerIds = [.. pending.MovedPlayerIds, playerId],
            MovesRemaining = Math.Max(0, pending.MovesRemaining - 1)
        };

        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId ? current with { Square = destination } : current)
                .ToArray(),
            PendingKickoffEvent = nextPending.MovesRemaining == 0 ? nextPending with { RequiresPlayerChoice = false } : nextPending,
            Log =
            [
                .. match.Log,
            new MatchLogEntry { Message = $"{FormatKickoffEventKind(pending.Kind)}: repositioned {playerId} to {destination.X},{destination.Y}." }
            ]
        };
    }

    public MatchState BlockDuringPendingKickoffBlitz(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam kickingTeam,
        Guid attackerPlayerId,
        LeagueTeam receivingTeam,
        Guid defenderPlayerId)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (pending.Kind != KickoffEventKind.Blitz)
        {
            throw new InvalidOperationException("Only a Blitz kickoff event allows a free block.");
        }

        if (pending.TeamId != kickingTeam.Id || pending.ReceivingTeamId != receivingTeam.Id)
        {
            throw new InvalidOperationException("Kickoff blitz teams do not match the pending event.");
        }

        if (!pending.EligiblePlayerIds.Contains(attackerPlayerId) || pending.MovedPlayerIds.Contains(attackerPlayerId))
        {
            throw new InvalidOperationException("That player cannot act for this kickoff blitz.");
        }

        if (match.PendingBlock is not null || match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending block or push before continuing the kickoff blitz.");
        }

        var attacker = FindTeamPlayer(kickingTeam, attackerPlayerId);
        var defender = FindTeamPlayer(receivingTeam, defenderPlayerId);
        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId)
            ?? throw new InvalidOperationException("Attacker is not part of this match.");
        var defenderPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderPlayerId)
            ?? throw new InvalidOperationException("Defender is not part of this match.");

        if (attackerPlacement.TeamId != kickingTeam.Id ||
            defenderPlacement.TeamId != receivingTeam.Id ||
            attackerPlacement.Square is not PitchSquare attackerSquare ||
            defenderPlacement.Square is not PitchSquare defenderSquare ||
            attackerPlacement.State != PlayerPitchState.Standing ||
            defenderPlacement.State != PlayerPitchState.Standing ||
            !IsAdjacent(attackerSquare, defenderSquare))
        {
            throw new InvalidOperationException("Kickoff blitz blocks require adjacent standing opponents.");
        }

        var nextPending = pending with
        {
            MovedPlayerIds = [.. pending.MovedPlayerIds, attackerPlayerId],
            MovesRemaining = Math.Max(0, pending.MovesRemaining - 1)
        };
        var markedMatch = match with
        {
            PendingKickoffEvent = nextPending.MovesRemaining == 0 ? nextPending with { RequiresPlayerChoice = false } : nextPending
        };

        return ResolveBlock(markedMatch, ruleset, kickingTeam, attacker, attackerPlacement, receivingTeam, defender);
    }

    public MatchState CompletePendingKickoffEvent(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (pending.ReceivingTeamId != receivingTeam.Id)
        {
            throw new InvalidOperationException("The receiving team must resolve the pending kickoff landing.");
        }

        var landingSquare = pending.LandingSquare;
        var baseMatch = match with { PendingKickoffEvent = null };
        if (pending.Kind == KickoffEventKind.SolidDefence)
        {
            ValidateSetupComplete(baseMatch, ruleset, pending.TeamId);
        }

        if (!IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, landingSquare))
        {
            var touchbackReceiver = FindTouchbackReceiver(baseMatch, receivingTeam)
                ?? throw new InvalidOperationException("Receiving team has no standing player for touchback.");

            return baseMatch with
            {
                Phase = MatchPhase.OffensivePlayerTurn,
                Ball = new BallState { CarrierPlayerId = touchbackReceiver.Id },
                Activations = [],
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"Touchback after {FormatKickoffEventKind(pending.Kind)}. {touchbackReceiver.Name} receives the ball." }
                ]
            };
        }

        var landedMatch = ResolveBallLanding(baseMatch, ruleset, receivingTeam, landingSquare);
        return landedMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            Activations = [],
            Log =
            [
                .. landedMatch.Log,
                new MatchLogEntry { Message = $"{FormatKickoffEventKind(pending.Kind)} complete. Kickoff resolved. Offensive player turn begins." }
            ]
        };
    }

    private PendingKickoffEventChoice? CreatePendingKickoffEvent(
        MatchState match,
        Ruleset ruleset,
        KickoffEventKind kind,
        Guid receivingTeamId,
        Guid kickingTeamId,
        PitchSquare landingSquare)
    {
        var teamId = kind is KickoffEventKind.SolidDefence or KickoffEventKind.Blitz
            ? kickingTeamId
            : receivingTeamId;
        var eligible = match.Placements
            .Where(placement =>
                placement.TeamId == teamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                !IsMarkedByOpponent(match, teamId, placement.PlayerId, square))
            .ToArray();

        if (kind == KickoffEventKind.HighKick)
        {
            if (match.Placements.Any(placement => placement.Square == landingSquare && OccupiesPitch(placement.State)))
            {
                eligible = eligible.Where(placement => placement.Square == landingSquare).ToArray();
            }

            if (eligible.Length == 0)
            {
                return null;
            }

            return new PendingKickoffEventChoice
            {
                Kind = kind,
                TeamId = teamId,
                ReceivingTeamId = receivingTeamId,
                LandingSquare = landingSquare,
                EligiblePlayerIds = eligible.Select(placement => placement.PlayerId).ToArray(),
                MovedPlayerIds = [],
                MovesRemaining = 1
            };
        }

        if (kind != KickoffEventKind.SolidDefence)
        {
            eligible = eligible
                .Where(placement => placement.Square is PitchSquare square &&
                    AdjacentSquares(square).Any(candidate =>
                        IsOnPitch(ruleset, candidate) &&
                        !match.Placements.Any(current => current.PlayerId != placement.PlayerId && current.Square == candidate && OccupiesPitch(current.State))))
                .ToArray();
        }

        if (eligible.Length == 0)
        {
            return null;
        }

        var moves = Math.Min(eligible.Length, RollD3() + 3);
        return new PendingKickoffEventChoice
        {
            Kind = kind,
            TeamId = teamId,
            ReceivingTeamId = receivingTeamId,
            LandingSquare = landingSquare,
            EligiblePlayerIds = eligible.Select(placement => placement.PlayerId).ToArray(),
            MovedPlayerIds = [],
            MovesRemaining = moves
        };
    }

    private KickoffEventResult ResolveTimeOut(MatchState match, Ruleset ruleset)
    {
        var roll = _dice.RollD6();
        var adjustment = roll <= 3 ? -1 : 1;
        var homeTurn = Math.Clamp(match.HomeTurn + adjustment, 1, ruleset.TurnsPerHalf + 1);
        var awayTurn = Math.Clamp(match.AwayTurn + adjustment, 1, ruleset.TurnsPerHalf + 1);
        var direction = adjustment < 0 ? "back" : "forward";
        return new KickoffEventResult(
            match with { HomeTurn = homeTurn, AwayTurn = awayTurn, Turn = GetTeamTurn(match with { HomeTurn = homeTurn, AwayTurn = awayTurn }, match.ActiveTeamId) },
            "Time-out",
            $"Time-out roll {roll}: both turn markers move {direction} one space.");
    }

    private static KickoffEventResult ResolveGetTheRef(MatchState match)
    {
        return new KickoffEventResult(
            match with
            {
                HomeBribesRemaining = match.HomeBribesRemaining + 1,
                AwayBribesRemaining = match.AwayBribesRemaining + 1
            },
            "Get the Ref",
            "Both teams gain one bribe.");
    }

    private KickoffEventResult ResolveCheeringFansOrBrilliantCoaching(MatchState match, Guid receivingTeamId, Guid kickingTeamId, string name, bool useCheerleaders)
    {
        var receivingStaff = TeamKickoffStaff(match, receivingTeamId, useCheerleaders);
        var kickingStaff = TeamKickoffStaff(match, kickingTeamId, useCheerleaders);
        var receivingRoll = RollD3() + receivingStaff;
        var kickingRoll = RollD3() + kickingStaff;
        if (receivingRoll == kickingRoll)
        {
            return new KickoffEventResult(match, name, $"Receiving coach total {receivingRoll}, kicking coach total {kickingRoll}; no bonus reroll.");
        }

        var winnerTeamId = receivingRoll > kickingRoll ? receivingTeamId : kickingTeamId;
        var nextMatch = winnerTeamId == match.HomeTeamId
            ? match with { HomeRerollsRemaining = match.HomeRerollsRemaining + 1 }
            : match with { AwayRerollsRemaining = match.AwayRerollsRemaining + 1 };
        return new KickoffEventResult(nextMatch, name, $"Receiving coach total {receivingRoll}, kicking coach total {kickingRoll}; winner gains a bonus reroll for the drive.");
    }

    private KickoffEventResult ResolveThrowARock(MatchState match)
    {
        var candidates = match.Placements
            .Where(placement => placement.State == PlayerPitchState.Standing && placement.Square is not null)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new KickoffEventResult(match, "Throw a Rock", "No standing players are on the pitch.");
        }

        var victim = candidates[RollIndex(candidates.Length)];
        var injury = ResolveInjury(Roll2D6());
        var apothecary = CreatePendingApothecaryIfAvailable(match, victim, victim.PlayerId.ToString(), injury);
        injury = apothecary.Injury;
        var nextMatch = apothecary.Match with
        {
            Placements = apothecary.Match.Placements
                .Select(placement => placement.PlayerId == victim.PlayerId
                    ? ApplyPitchState(apothecary.Match, placement, injury.State, OccupiesPitch(injury.State) ? placement.Square : null, injury.Casualty)
                    : placement)
                .ToArray()
        };
        var casualtyText = injury.Casualty is null ? "" : $" Casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}.";
        var apothecaryText = apothecary.Log.Count == 0 ? "" : $" {apothecary.Log[0].Message}";
        return new KickoffEventResult(nextMatch, "Throw a Rock", $"{victim.PlayerId} is hit by a rock and is {FormatPitchState(injury.State)}.{casualtyText}{apothecaryText}");
    }

    private KickoffEventResult ResolvePitchInvasion(MatchState match)
    {
        var stunned = new List<Guid>();
        var nextPlacements = match.Placements
            .Select(placement =>
            {
                if (placement.State != PlayerPitchState.Standing || placement.Square is null)
                {
                    return placement;
                }

                var roll = _dice.RollD6();
                if (roll < 6)
                {
                    return placement;
                }

                stunned.Add(placement.PlayerId);
                return ApplyPitchState(match, placement, PlayerPitchState.Stunned, placement.Square);
            })
            .ToArray();

        return new KickoffEventResult(
            match with { Placements = nextPlacements },
            "Pitch Invasion",
            stunned.Count == 0 ? "The crowd surges, but no players are stunned." : $"The crowd stuns {stunned.Count} player{(stunned.Count == 1 ? "" : "s")}.");
    }

    private KickoffEventResult ResolveChangingWeather(MatchState match)
    {
        var weatherRoll = Roll2D6();
        var weather = ResolveWeather(weatherRoll);
        var nextMatch = match with { Weather = weather };
        var extraScatter = weather == WeatherCondition.Nice;
        var message = extraScatter
            ? $"Weather roll {weatherRoll}: {FormatWeather(weather)}. A gentle gust will scatter the ball one extra square."
            : $"Weather roll {weatherRoll}: {FormatWeather(weather)}.";
        return new KickoffEventResult(nextMatch, "Changing Weather", message, ExtraScatter: extraScatter);
    }

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
            if (attackerPlacement.Square is null || !IsAdjacent(attackerPlacement.Square, defenderPlacement.Square!))
            {
                throw new InvalidOperationException("Blocks require adjacent players.");
            }

            var jumpUpRoll = _dice.RollD6();
            var jumpUpTarget = Math.Clamp(attacker.Stats.Agility + 1, 2, 6);
            var jumpUpAction = BeginPlayerAction(match, ruleset, attackerTeam, attacker, PlayerTurnAction.Block, goForItsUsed: 0);
            if (jumpUpAction.Prevented)
            {
                return jumpUpAction.Match;
            }

            var activatedJumpUpMatch = jumpUpAction.Match;
            if (!RollSucceeds(jumpUpRoll, jumpUpTarget, ruleset.Dice))
            {
                return activatedJumpUpMatch with
                {
                    Log =
                    [
                        .. activatedJumpUpMatch.Log,
                        new MatchLogEntry { Message = $"{attacker.Name} attempts a Jump Up block: rolled {jumpUpRoll} vs {jumpUpTarget}+, failed and remains prone." }
                    ]
                };
            }

            match = activatedJumpUpMatch with
            {
                Placements = activatedJumpUpMatch.Placements
                    .Select(placement => placement.PlayerId == attackerPlayerId
                        ? placement with { State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                        : placement)
                    .ToArray(),
                Log =
                [
                    .. activatedJumpUpMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} attempts a Jump Up block: rolled {jumpUpRoll} vs {jumpUpTarget}+, success." }
                ]
            };
            attackerPlacement = match.Placements.First(placement => placement.PlayerId == attackerPlayerId);
            var jumpUpFoulAppearance = ResolveFoulAppearance(match, ruleset, attacker, defender);
            if (jumpUpFoulAppearance.BlockPrevented)
            {
                return jumpUpFoulAppearance.Match;
            }

            return ResolveBlock(match, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
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
            pending.PreventFollowUp);
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

        var defender = FindTeamPlayer(defenderTeam, pending.DefenderPlayerId);
        var attacker = FindTeamPlayer(attackerTeam, pending.AttackerPlayerId);
        var stripBall = ShouldStripBall(ruleset, attacker, defender, match.Ball.CarrierPlayerId == defender.Id, pending.KnockDefenderDown);
        var pushedMatch = PushPlayer(match with { PendingPush = null }, ruleset, defender, pending.DefenderSquare, square, pending.KnockDefenderDown, () => ResolveBlockInjury(ruleset, attacker, defender), stripBall);
        var loggedMatch = pushedMatch with
        {
            Log =
            [
                .. pushedMatch.Log,
                new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} is pushed to {square.X},{square.Y}." }
            ]
        };

        return CompleteBlockPush(loggedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, pending.DefenderSquare, pending.KnockDefenderDown, pending.PreventFollowUp);
    }

    public MatchState ResolvePendingFollowUp(
        MatchState match,
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
        var baseMatch = match with { PendingFollowUp = null };
        if (!useFollowUp)
        {
            return baseMatch with
            {
                Log =
                [
                    .. baseMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} does not follow up." }
                ]
            };
        }

        return MoveAttackerToFollowUpSquare(baseMatch, attacker, pending.FollowUpSquare);
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
            var crowdMatch = PushPlayerIntoCrowd(baseMatch, ruleset, defenderPlacement);
            var loggedMatch = crowdMatch with
            {
                Log =
                [
                    .. crowdMatch.Log,
                    new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} declines Stand Firm. No legal push square is available; {defender.Name} is pushed into the crowd." }
                ]
            };

            return CompleteBlockPush(loggedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, pending.DefenderSquare, pending.KnockDefenderDown, pending.PreventFollowUp);
        }

        if (pending.LegalSquares.Count == 1)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, baseMatch.Ball.CarrierPlayerId == defender.Id, pending.KnockDefenderDown);
            var pushedMatch = PushPlayer(baseMatch, ruleset, defender, pending.DefenderSquare, pending.LegalSquares[0], pending.KnockDefenderDown, () => ResolveBlockInjury(ruleset, attacker, defender), stripBall);
            var loggedMatch = pushedMatch with
            {
                Log =
                [
                    .. pushedMatch.Log,
                    new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} declines Stand Firm and is pushed to {pending.LegalSquares[0].X},{pending.LegalSquares[0].Y}." }
                ]
            };

            return CompleteBlockPush(loggedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, pending.DefenderSquare, pending.KnockDefenderDown, pending.PreventFollowUp);
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

    public MatchState FoulPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Guid foulerPlayerId,
        LeagueTeam victimTeam,
        Guid victimPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only foul during a player turn.");
        }

        if (foulingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can foul during its turn.");
        }

        if (foulingTeam.Id == victimTeam.Id)
        {
            throw new InvalidOperationException("A player cannot foul a teammate.");
        }

        if (match.PendingBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending block choice before taking another action.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before taking another action.");
        }

        if (match.PendingReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending reroll before taking another action.");
        }

        var fouler = FindTeamPlayer(foulingTeam, foulerPlayerId);
        var victim = FindTeamPlayer(victimTeam, victimPlayerId);
        var foulerPlacement = FindStandingPlacement(match, foulerPlayerId, foulingTeam.Id, "fouler");
        var victimPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == victimPlayerId)
            ?? throw new InvalidOperationException("Victim is not part of this match.");

        if (victimPlacement.TeamId != victimTeam.Id)
        {
            throw new InvalidOperationException("Victim is assigned to the wrong team.");
        }

        if (victimPlacement.Square is not PitchSquare victimSquare ||
            victimPlacement.State is not (PlayerPitchState.Prone or PlayerPitchState.Stunned))
        {
            throw new InvalidOperationException("Only prone or stunned players on the pitch can be fouled.");
        }

        if (!IsAdjacent(foulerPlacement.Square!, victimSquare))
        {
            throw new InvalidOperationException("Fouls require adjacent players.");
        }

        var attackAssists = CountFoulAssists(match, foulingTeam.Id, victimPlayerId, victimSquare, foulerPlayerId);
        var defenseAssists = CountFoulAssists(match, victimTeam.Id, victimPlayerId, victimSquare, foulerPlayerId);
        var foulAction = BeginPlayerAction(match, ruleset, foulingTeam, fouler, PlayerTurnAction.Foul, goForItsUsed: 0);
        if (foulAction.Prevented)
        {
            return foulAction.Match;
        }

        return ResolveFoulAfterActivation(foulAction.Match, ruleset, foulingTeam, fouler, foulerPlacement, victimTeam, victim, victimPlacement, attackAssists, defenseAssists, allowSneakyGitMove: true);
    }

    public MatchState PileDriverPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Guid foulerPlayerId,
        LeagueTeam victimTeam,
        Guid victimPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only use Pile Driver during a player turn.");
        }

        if (foulingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can use Pile Driver during its turn.");
        }

        EnsureNoPendingChoices(match);

        var fouler = FindTeamPlayer(foulingTeam, foulerPlayerId);
        if (!PlayerHasHookedEffect(ruleset, fouler, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.PileDriver))
        {
            throw new InvalidOperationException($"{fouler.Name} does not have Pile Driver.");
        }

        var activation = GetActivation(match, fouler.Id, foulingTeam.Id);
        if (activation is null || activation.BlocksMade == 0 || activation.Action is not (PlayerTurnAction.Block or PlayerTurnAction.Blitz))
        {
            throw new InvalidOperationException("Pile Driver can only be used after this player knocks an opponent down with a block.");
        }

        var victim = FindTeamPlayer(victimTeam, victimPlayerId);
        var foulerPlacement = FindStandingPlacement(match, foulerPlayerId, foulingTeam.Id, "fouler");
        var victimPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == victimPlayerId)
            ?? throw new InvalidOperationException("Victim is not part of this match.");

        if (victimPlacement.TeamId != victimTeam.Id)
        {
            throw new InvalidOperationException("Victim is assigned to the wrong team.");
        }

        if (victimPlacement.Square is not PitchSquare victimSquare ||
            victimPlacement.State is not (PlayerPitchState.Prone or PlayerPitchState.Stunned))
        {
            throw new InvalidOperationException("Only prone or stunned players on the pitch can be fouled.");
        }

        if (!IsAdjacent(foulerPlacement.Square!, victimSquare))
        {
            throw new InvalidOperationException("Pile Driver requires an adjacent prone or stunned opponent.");
        }

        var attackAssists = CountFoulAssists(match, foulingTeam.Id, victimPlayerId, victimSquare, foulerPlayerId);
        var defenseAssists = CountFoulAssists(match, victimTeam.Id, victimPlayerId, victimSquare, foulerPlayerId);
        var proneMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == fouler.Id
                    ? placement with { State = PlayerPitchState.Prone, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = $"{fouler.Name} uses Pile Driver and is placed prone." }]
        };

        return ResolveFoulAfterActivation(proneMatch, ruleset, foulingTeam, fouler, foulerPlacement, victimTeam, victim, victimPlacement, attackAssists, defenseAssists, allowSneakyGitMove: false);
    }

    public MatchState StabPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "stab", "Stab");
        if (context.Prevented)
        {
            return context.Match;
        }

        return ResolveSpecialArmorAttack(context.Match, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name} stabs {context.Target.Name}");
    }

    public MatchState ChainsawPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "chainsaw", "Chainsaw");
        if (context.Prevented)
        {
            return context.Match;
        }

        var startRoll = _dice.RollD6();
        if (startRoll == 1)
        {
            var selfHit = ResolveSpecialArmorAttack(context.Match, ruleset, attackerTeam, context.Actor, context.ActorPlacement, armorModifier: 3, $"{context.Actor.Name}'s chainsaw kicks back");
            return selfHit with
            {
                Log = [.. selfHit.Log, new MatchLogEntry { Message = $"{context.Actor.Name} starts the chainsaw: rolled 1, it kicks back." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} starts the chainsaw: rolled {startRoll}, attack continues." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 3, $"{context.Actor.Name} attacks {context.Target.Name} with a chainsaw");
    }

    public MatchState ProjectileVomitPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "projectile-vomit", "Projectile Vomit");
        if (context.Prevented)
        {
            return context.Match;
        }

        var vomitRoll = _dice.RollD6();
        if (vomitRoll == 1)
        {
            var selfHit = ResolveSpecialArmorAttack(context.Match, ruleset, attackerTeam, context.Actor, context.ActorPlacement, armorModifier: 0, $"{context.Actor.Name}'s Projectile Vomit hits themselves");
            return selfHit with
            {
                Log = [.. selfHit.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Projectile Vomit: rolled 1, the vomit hits themselves." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Projectile Vomit: rolled {vomitRoll}, target is hit." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name} vomits on {context.Target.Name}");
    }

    public MatchState HypnoticGazePlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "hypnotic-gaze", "Hypnotic Gaze");
        if (context.Prevented)
        {
            return context.Match;
        }

        var roll = _dice.RollD6();
        var target = Math.Clamp(context.Actor.Stats.Agility, 2, 6);
        if (!RollSucceeds(roll, target, ruleset.Dice))
        {
            return context.Match with
            {
                Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Hypnotic Gaze on {context.Target.Name}: rolled {roll} vs {target}+, failed." }]
            };
        }

        return context.Match with
        {
            Placements = context.Match.Placements
                .Select(placement => placement.PlayerId == context.Target.Id
                    ? placement with { TackleZonesLost = true }
                    : placement)
                .ToArray(),
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Hypnotic Gaze on {context.Target.Name}: rolled {roll} vs {target}+, tackle zone removed." }]
        };
    }

    public MatchState BreatheFirePlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "breathe-fire", "Breathe Fire");
        if (context.Prevented)
        {
            return context.Match;
        }

        var roll = _dice.RollD6();
        if (roll == 1)
        {
            return context.Match with
            {
                Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} breathes fire at {context.Target.Name}: rolled 1, no effect." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} breathes fire at {context.Target.Name}: rolled {roll}, target is hit." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name}'s fire hits {context.Target.Name}");
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
            current.Square == destination &&
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

    public MatchState ThrowBomb(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam throwingTeam,
        Guid bomberPlayerId,
        PitchSquare targetSquare,
        LeagueTeam opposingTeam)
    {
        var bomber = FindTeamPlayer(throwingTeam, bomberPlayerId);
        if (!PlayerHasHookedEffect(ruleset, bomber, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.Bombardier))
        {
            throw new InvalidOperationException($"{bomber.Name} does not have Bombardier.");
        }

        var placement = ValidateSpecialActor(match, throwingTeam, bomber, requireStanding: true);
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"Square {targetSquare.X},{targetSquare.Y} is outside the pitch.");
        }

        var action = BeginPlayerAction(match, ruleset, throwingTeam, bomber, PlayerTurnAction.Special, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var passRange = ResolvePassRange(placement.Square!, targetSquare);
        var passTarget = PassingTarget(ruleset, bomber, passRange, action.Match.Weather);
        var passRoll = _dice.RollD6();
        var landingSquare = RollSucceeds(passRoll, passTarget, ruleset.Dice)
            ? targetSquare
            : ScatterFrom(ruleset, targetSquare);
        var nextMatch = action.Match with
        {
            Log =
            [
                .. action.Match.Log,
                new MatchLogEntry { Message = $"{bomber.Name} throws a bomb to {targetSquare.X},{targetSquare.Y}: rolled {passRoll} vs {passTarget}+{(landingSquare == targetSquare ? "." : $", scatters to {landingSquare.X},{landingSquare.Y}.")}" }
            ]
        };

        return ResolveBombLanding(nextMatch, ruleset, throwingTeam, opposingTeam, landingSquare);
    }

    public MatchState ThrowPendingBomb(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam throwingTeam,
        LeagueTeam opposingTeam,
        PitchSquare targetSquare)
    {
        var pending = match.PendingBombThrow
            ?? throw new InvalidOperationException("There is no pending bomb throw.");

        if (pending.ThrowingTeamId != throwingTeam.Id || pending.OpposingTeamId != opposingTeam.Id)
        {
            throw new InvalidOperationException("Pending bomb throw belongs to different teams.");
        }

        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"Square {targetSquare.X},{targetSquare.Y} is outside the pitch.");
        }

        var thrower = FindTeamPlayer(throwingTeam, pending.ThrowerPlayerId);
        var throwerPlacement = FindStandingPlacement(match, thrower.Id, throwingTeam.Id, "bomb thrower");
        var passRange = ResolvePassRange(throwerPlacement.Square!, targetSquare);
        var passTarget = PassingTarget(ruleset, thrower, passRange, match.Weather);
        var passRoll = _dice.RollD6();
        var landingSquare = RollSucceeds(passRoll, passTarget, ruleset.Dice)
            ? targetSquare
            : ScatterFrom(ruleset, targetSquare);
        var thrownMatch = match with
        {
            PendingBombThrow = null,
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{thrower.Name} throws the caught bomb to {targetSquare.X},{targetSquare.Y}: rolled {passRoll} vs {passTarget}+{(landingSquare == targetSquare ? "." : $", scatters to {landingSquare.X},{landingSquare.Y}.")}" }
            ]
        };

        return ResolveBombLanding(thrownMatch, ruleset, throwingTeam, opposingTeam, landingSquare);
    }

    public MatchState ThrowTeamMate(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid throwerPlayerId,
        Guid thrownPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? opposingTeam = null)
    {
        var context = ValidateTeamMateLaunch(match, ruleset, team, throwerPlayerId, thrownPlayerId, targetSquare, "throw-team-mate", "Throw Team-Mate");
        var action = BeginPlayerAction(match, ruleset, team, context.Actor, PlayerTurnAction.Pass, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var nextMatch = action.Match;
        if (PlayerHasHookedEffect(ruleset, context.Actor, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.AlwaysHungry))
        {
            var hungryRoll = _dice.RollD6();
            if (hungryRoll == 1)
            {
                var reroll = _dice.RollD6();
                if (reroll == 1)
                {
                    var eatenMatch = RemoveLaunchedPlayer(nextMatch, context.Launched.Id, PlayerPitchState.Casualty);
                    var loggedEaten = eatenMatch with
                    {
                        Log =
                        [
                            .. eatenMatch.Log,
                            new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled 1 then 1, {context.Launched.Name} is eaten." }
                        ]
                    };

                    return match.Ball.CarrierPlayerId == context.Launched.Id
                        ? ApplyTurnover(loggedEaten with { Ball = new BallState() }, ruleset, team.Id)
                        : loggedEaten;
                }

                nextMatch = nextMatch with
                {
                    Log =
                    [
                        .. nextMatch.Log,
                        new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled 1 then {reroll}, throw continues." }
                    ]
                };
            }
            else
            {
                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled {hungryRoll}, throw continues." }]
                };
            }
        }

        var passRange = ResolvePassRange(context.ActorPlacement.Square!, targetSquare);
        var throwModifier = PlayerHasHookedEffect(ruleset, context.Actor, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.StrongArm) ? -1 : 0;
        var passTarget = Math.Clamp(PassingTarget(ruleset, context.Actor, passRange, nextMatch.Weather) + throwModifier + 1, 2, 6);
        var passRoll = _dice.RollD6();
        var accurate = RollSucceeds(passRoll, passTarget, ruleset.Dice);
        var landingSquare = accurate
            ? targetSquare
            : ScatterLaunchedPlayer(ruleset, targetSquare, context.Launched, inaccurateDistance: 3);

        nextMatch = nextMatch with
        {
            Placements = nextMatch.Placements
                .Select(placement => placement.PlayerId == context.Launched.Id
                    ? placement with { Square = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = $"{context.Actor.Name} throws {context.Launched.Name} to {targetSquare.X},{targetSquare.Y}: {passRange.Name} rolled {passRoll} vs {passTarget}+{(accurate ? ", accurate." : $", inaccurate to {landingSquare.X},{landingSquare.Y}.")}" }
            ]
        };

        return ResolveLaunchedPlayerLanding(nextMatch, ruleset, team, opposingTeam, context.Launched, landingSquare, "thrown");
    }

    public MatchState KickTeamMate(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid kickerPlayerId,
        Guid kickedPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? opposingTeam = null)
    {
        var context = ValidateTeamMateLaunch(match, ruleset, team, kickerPlayerId, kickedPlayerId, targetSquare, "kick-team-mate", "Kick Team-Mate");
        var action = BeginPlayerAction(match, ruleset, team, context.Actor, PlayerTurnAction.Special, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var kickRoll = _dice.RollD6();
        var landingSquare = kickRoll == 1
            ? ScatterLaunchedPlayer(ruleset, targetSquare, context.Launched, inaccurateDistance: 3)
            : targetSquare;
        var nextMatch = action.Match with
        {
            Placements = action.Match.Placements
                .Select(placement => placement.PlayerId == context.Launched.Id
                    ? placement with { Square = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. action.Match.Log,
                new MatchLogEntry { Message = $"{context.Actor.Name} kicks {context.Launched.Name} to {targetSquare.X},{targetSquare.Y}: rolled {kickRoll}{(kickRoll == 1 ? $", scatters to {landingSquare.X},{landingSquare.Y}." : ", on target.")}" }
            ]
        };

        return ResolveLaunchedPlayerLanding(nextMatch, ruleset, team, opposingTeam, context.Launched, landingSquare, "kicked");
    }

    private MatchState ResolveFoulAfterActivation(
        MatchState activatedMatch,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Player fouler,
        PlayerPlacement foulerPlacement,
        LeagueTeam victimTeam,
        Player victim,
        PlayerPlacement victimPlacement,
        int attackAssists,
        int defenseAssists,
        bool allowSneakyGitMove)
    {
        var armorRoll = Roll2D6Detailed();
        var hasDirtyPlayer = PlayerHasHookedEffect(ruleset, fouler, GameEventKind.ArmorRoll, GameEventStage.AfterRoll, SkillEffect.DirtyPlayer);
        var hasSneakyGit = PlayerHasHookedEffect(ruleset, fouler, GameEventKind.ArmorRoll, GameEventStage.AfterRoll, SkillEffect.SneakyGit);
        var victimSquare = victimPlacement.Square
            ?? throw new InvalidOperationException("Victim must be on the pitch.");
        var armorTotalWithoutSkill = armorRoll.Total + attackAssists - defenseAssists;
        var dirtyPlayerArmorBonus = hasDirtyPlayer &&
            !PlayerHasHookedEffect(ruleset, victim, GameEventKind.ArmorRoll, GameEventStage.BeforeResolve, SkillEffect.IronHardSkin) &&
            armorTotalWithoutSkill <= victim.Stats.Armor &&
            armorTotalWithoutSkill + 1 > victim.Stats.Armor
                ? 1
                : 0;
        var armorTotal = armorTotalWithoutSkill + dirtyPlayerArmorBonus;
        var log = new List<MatchLogEntry>
        {
            new()
            {
                Message = dirtyPlayerArmorBonus > 0
                    ? $"{fouler.Name} fouls {victim.Name}: armor {armorRoll.Total} +{attackAssists} -{defenseAssists} +1 Dirty Player = {armorTotal} vs AV {victim.Stats.Armor}+."
                    : $"{fouler.Name} fouls {victim.Name}: armor {armorRoll.Total} +{attackAssists} -{defenseAssists} = {armorTotal} vs AV {victim.Stats.Armor}+."
            }
        };

        var nextMatch = activatedMatch;
        var armorBroken = armorTotal > victim.Stats.Armor;
        var sentOff = armorRoll.IsDoubles && !hasSneakyGit;
        if (armorBroken)
        {
            var injuryRoll = Roll2D6Detailed();
            sentOff = sentOff || injuryRoll.IsDoubles;
            var dirtyPlayerInjuryBonus = hasDirtyPlayer && dirtyPlayerArmorBonus == 0 ? 1 : 0;
            var injuryTotal = injuryRoll.Total + dirtyPlayerInjuryBonus;
            var injury = ResolveInjury(ruleset, victim, injuryTotal);
            var apothecary = CreatePendingApothecaryIfAvailable(nextMatch, victimPlacement, victim.Name, injury);
            nextMatch = apothecary.Match;
            injury = apothecary.Injury;
            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(placement => placement.PlayerId == victim.Id
                        ? ApplyPitchState(nextMatch, placement, injury.State, OccupiesPitch(injury.State) ? victimSquare : null, injury.Casualty)
                        : placement)
                    .ToArray()
            };
            log.Add(new MatchLogEntry
            {
                Message = dirtyPlayerInjuryBonus > 0
                    ? $"{victim.Name} injury roll {injuryRoll.Total} +1 Dirty Player = {injuryTotal}: {FormatPitchState(injury.State)}."
                    : $"{victim.Name} injury roll {injuryRoll.Total}: {FormatPitchState(injury.State)}."
            });
            if (injury.Casualty is not null)
            {
                log.Add(new MatchLogEntry { Message = $"{victim.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
            }
            log.AddRange(apothecary.Log);
        }
        else
        {
            log.Add(new MatchLogEntry { Message = $"{victim.Name}'s armor holds." });
        }

        nextMatch = nextMatch with { Log = [.. nextMatch.Log, .. log] };

        if (!sentOff)
        {
            if (hasSneakyGit && allowSneakyGitMove)
            {
                nextMatch = nextMatch with
                {
                    Activations = nextMatch.Activations
                        .Select(activation =>
                            activation.PlayerId == fouler.Id &&
                            activation.TeamId == foulingTeam.Id &&
                            activation.Half == nextMatch.Half &&
                            activation.Turn == nextMatch.Turn
                                ? activation with { MayMoveAfterFoul = true }
                                : activation)
                        .ToArray(),
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{fouler.Name} may continue moving with Sneaky Git." }]
                };
            }

            return nextMatch;
        }

        if (TeamBribesRemaining(nextMatch, foulingTeam.Id) > 0)
        {
            return nextMatch with
            {
                PendingSendOff = new PendingSendOffChoice
                {
                    TeamId = foulingTeam.Id,
                    PlayerId = fouler.Id,
                    Reason = "foul",
                    BribeAvailable = true
                },
                Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{fouler.Name} may be sent off for the foul. Choose whether to use a bribe." }]
            };
        }

        return ApplyTurnover(SendOffPlayer(nextMatch, fouler.Id, $"{fouler.Name} is sent off for the foul."), ruleset, foulingTeam.Id);
    }

    public MatchState ResolvePendingSendOff(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        bool useBribe)
    {
        var pending = match.PendingSendOff
            ?? throw new InvalidOperationException("There is no pending send-off choice.");

        if (pending.TeamId != team.Id)
        {
            throw new InvalidOperationException("Pending send-off belongs to another team.");
        }

        var player = FindTeamPlayer(team, pending.PlayerId);
        var baseMatch = match with { PendingSendOff = null };
        if (useBribe)
        {
            if (!pending.BribeAvailable || TeamBribesRemaining(baseMatch, team.Id) <= 0)
            {
                throw new InvalidOperationException($"{team.Name} has no bribe available.");
            }

            var bribeRoll = _dice.RollD6();
            var bribedMatch = SpendBribe(baseMatch, team.Id);
            if (bribeRoll >= 2)
            {
                var keptMatch = bribedMatch with
                {
                    Log = [.. bribedMatch.Log, new MatchLogEntry { Message = $"{team.Name} uses a bribe for {player.Name}: rolled {bribeRoll}, send-off prevented." }]
                };

                return pending.DriveEnd is null
                    ? keptMatch
                    : ContinueDriveEnd(keptMatch, ruleset, pending.DriveEnd with { ResolvedPlayerIds = [.. pending.DriveEnd.ResolvedPlayerIds, player.Id] });
            }

            baseMatch = bribedMatch with
            {
                Log = [.. bribedMatch.Log, new MatchLogEntry { Message = $"{team.Name} uses a bribe for {player.Name}: rolled {bribeRoll}, bribe failed." }]
            };
        }
        else if (pending.BribeAvailable)
        {
            baseMatch = baseMatch with
            {
                Log = [.. baseMatch.Log, new MatchLogEntry { Message = $"{team.Name} declines to use a bribe for {player.Name}." }]
            };
        }

        var sentOffMatch = SendOffPlayer(baseMatch, player.Id, $"{player.Name} is sent off for {pending.Reason}.");
        if (pending.DriveEnd is not null)
        {
            return ContinueDriveEnd(sentOffMatch, ruleset, pending.DriveEnd);
        }

        return ApplyTurnover(sentOffMatch, ruleset, team.Id);
    }

    private MatchState BeginDriveEnd(MatchState match, Ruleset ruleset, PendingDriveEndContinuation continuation)
    {
        return ContinueDriveEnd(match with { DriveState = DriveState.Ending }, ruleset, continuation);
    }

    private MatchState ContinueDriveEnd(MatchState match, Ruleset ruleset, PendingDriveEndContinuation continuation)
    {
        var nextSecretWeapon = match.Placements.FirstOrDefault(placement =>
            !continuation.ResolvedPlayerIds.Contains(placement.PlayerId) &&
            match.SecretWeaponPlayerIds.Contains(placement.PlayerId) &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve &&
            placement.Square is not null);

        if (nextSecretWeapon is not null)
        {
            var teamId = nextSecretWeapon.TeamId;
            var remainingSecretWeapons = match.Placements.Count(placement =>
                !continuation.ResolvedPlayerIds.Contains(placement.PlayerId) &&
                placement.PlayerId != nextSecretWeapon.PlayerId &&
                match.SecretWeaponPlayerIds.Contains(placement.PlayerId) &&
                placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve &&
                placement.Square is not null);
            if (TeamBribesRemaining(match, teamId) > 0)
            {
                return match with
                {
                    PendingSendOff = new PendingSendOffChoice
                    {
                        TeamId = teamId,
                        PlayerId = nextSecretWeapon.PlayerId,
                        Reason = "Secret Weapon",
                        BribeAvailable = true,
                        DriveEnd = continuation
                    },
                    Log = [.. match.Log, new MatchLogEntry { Message = $"{nextSecretWeapon.PlayerId} must be sent off for Secret Weapon. Choose whether to use a bribe; {remainingSecretWeapons} more Secret Weapon send-off{(remainingSecretWeapons == 1 ? "" : "s")} remain after this." }]
                };
            }

            var sentOff = SendOffPlayer(match, nextSecretWeapon.PlayerId, $"{nextSecretWeapon.PlayerId} is sent off for Secret Weapon.");
            return ContinueDriveEnd(sentOff, ruleset, continuation with { ResolvedPlayerIds = [.. continuation.ResolvedPlayerIds, nextSecretWeapon.PlayerId] });
        }

        if (continuation.CompleteMatch)
        {
            return match with
            {
                Phase = MatchPhase.Complete,
                DriveState = DriveState.Complete,
                Turn = ruleset.TurnsPerHalf,
                Activations = [],
                PendingBlock = null,
                PendingPush = null,
                PendingInterception = null,
                PendingReroll = null,
                PendingStandFirm = null,
                PendingDivingTackle = null,
                PendingFollowUp = null,
                PendingBombThrow = null,
                PendingMultipleBlock = null,
                PendingSendOff = null,
                PendingKickoffEvent = null,
                Log = [.. match.Log, new MatchLogEntry { Message = "Full time. Match complete." }]
            };
        }

        return continuation.StartSecondHalf
            ? StartSecondHalfSetupAfterDriveEnd(match)
            : StartNextDriveSetup(match, continuation.NextDefenseTeamId);
    }

    private SpecialActionContext BeginSpecialTargetAction(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam actorTeam,
        Guid actorPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId,
        string skillId,
        string actionName)
    {
        if (actorTeam.Id == targetTeam.Id)
        {
            throw new InvalidOperationException($"{actionName} must target an opposing player.");
        }

        var actor = FindTeamPlayer(actorTeam, actorPlayerId);
        if (!PlayerHasSkillId(actor, skillId))
        {
            throw new InvalidOperationException($"{actor.Name} does not have {actionName}.");
        }

        var actorPlacement = ValidateSpecialActor(match, actorTeam, actor, requireStanding: true);
        var target = FindTeamPlayer(targetTeam, targetPlayerId);
        var targetPlacement = FindStandingPlacement(match, targetPlayerId, targetTeam.Id, "target");
        if (!IsAdjacent(actorPlacement.Square!, targetPlacement.Square!))
        {
            throw new InvalidOperationException($"{actionName} requires adjacent players.");
        }

        var action = BeginPlayerAction(match, ruleset, actorTeam, actor, PlayerTurnAction.Special, goForItsUsed: 0);
        return new SpecialActionContext(action.Match, actor, actorPlacement, target, targetPlacement, action.Prevented);
    }

    private PlayerPlacement ValidateSpecialActor(MatchState match, LeagueTeam team, Player player, bool requireStanding)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Special actions can only be used during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can use a special action during its turn.");
        }

        EnsureNoPendingChoices(match);

        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == player.Id)
            ?? throw new InvalidOperationException("Player is not part of this match.");
        if (placement.TeamId != team.Id)
        {
            throw new InvalidOperationException("Player is assigned to the wrong team.");
        }

        if (placement.Square is null || (requireStanding && placement.State != PlayerPitchState.Standing))
        {
            throw new InvalidOperationException("The player must be standing on the pitch.");
        }

        return placement;
    }

    private MatchState ResolveSpecialArmorAttack(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam targetTeam,
        Player target,
        PlayerPlacement targetPlacement,
        int armorModifier,
        string description)
    {
        var targetSquare = targetPlacement.Square
            ?? throw new InvalidOperationException("Target must be on the pitch.");
        var armorRoll = Roll2D6Detailed();
        var total = armorRoll.Total + armorModifier;
        var armorText = armorModifier == 0
            ? $"{armorRoll.Total}"
            : $"{armorRoll.Total} +{armorModifier} = {total}";
        if (total <= target.Stats.Armor)
        {
            return match with
            {
                Log = [.. match.Log, new MatchLogEntry { Message = $"{description}: armor {armorText} vs AV {target.Stats.Armor}+, no effect." }]
            };
        }

        var injuryRoll = Roll2D6Detailed();
        var injury = ResolveInjury(ruleset, target, injuryRoll.Total);
        var knockedMatch = KnockPlayerDown(match, ruleset, target, targetPlacement, injury, targetSquare);
        var result = knockedMatch with
        {
            Log =
            [
                .. knockedMatch.Log,
                new MatchLogEntry { Message = $"{description}: armor {armorText} vs AV {target.Stats.Armor}+, broken." },
                new MatchLogEntry { Message = $"{target.Name} injury roll {injuryRoll.Total}: {FormatPitchState(injury.State)}." }
            ]
        };

        return targetPlacement.TeamId == match.ActiveTeamId && match.Ball.CarrierPlayerId == target.Id
            ? ApplyTurnover(result, ruleset, targetTeam.Id)
            : result;
    }

    private MatchState ResolveBombLanding(MatchState match, Ruleset ruleset, LeagueTeam throwingTeam, LeagueTeam opposingTeam, PitchSquare landingSquare)
    {
        var catcherPlacement = match.Placements.FirstOrDefault(current =>
            current.Square == landingSquare &&
            current.State == PlayerPitchState.Standing &&
            (current.TeamId == throwingTeam.Id || current.TeamId == opposingTeam.Id));
        if (catcherPlacement is null)
        {
            return ResolveBombExplosion(match, ruleset, throwingTeam, opposingTeam, landingSquare);
        }

        var catcherTeam = catcherPlacement.TeamId == throwingTeam.Id ? throwingTeam : opposingTeam;
        var otherTeam = catcherPlacement.TeamId == throwingTeam.Id ? opposingTeam : throwingTeam;
        var catcher = FindTeamPlayer(catcherTeam, catcherPlacement.PlayerId);
        var catchRoll = _dice.RollD6();
        var catchTarget = CatchTarget(ruleset, catcher, match.Weather, CountOpposingTackleZones(match, catcherTeam.Id, catcher.Id, landingSquare));
        if (!RollSucceeds(catchRoll, catchTarget, ruleset.Dice))
        {
            return ResolveBombExplosion(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{catcher.Name} attempts to catch the bomb: rolled {catchRoll} vs {catchTarget}+, failed and it explodes." }
                ]
            }, ruleset, throwingTeam, opposingTeam, landingSquare);
        }

        return match with
        {
            PendingBombThrow = new PendingBombThrowChoice
            {
                ThrowingTeamId = catcherTeam.Id,
                OpposingTeamId = otherTeam.Id,
                ThrowerPlayerId = catcher.Id,
                BombSquare = landingSquare
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{catcher.Name} catches the bomb: rolled {catchRoll} vs {catchTarget}+. Choose a square to throw it back." }
            ]
        };
    }

    private MatchState ResolveBombExplosion(MatchState match, Ruleset ruleset, LeagueTeam throwingTeam, LeagueTeam opposingTeam, PitchSquare center)
    {
        var affectedSquares = AdjacentSquares(center).Append(center).Distinct().ToArray();
        var nextMatch = match with
        {
            Log = [.. match.Log, new MatchLogEntry { Message = $"Bomb explodes at {center.X},{center.Y}." }]
        };

        foreach (var placement in affectedSquares
            .SelectMany(square => nextMatch.Placements.Where(current => current.Square == square && OccupiesPitch(current.State)))
            .ToArray())
        {
            var team = placement.TeamId == throwingTeam.Id ? throwingTeam : opposingTeam;
            var player = FindTeamPlayer(team, placement.PlayerId);
            nextMatch = ResolveSpecialArmorAttack(nextMatch, ruleset, team, player, placement, armorModifier: 0, $"Bomb blast hits {player.Name}");
        }

        return nextMatch;
    }

    private TeamMateLaunchContext ValidateTeamMateLaunch(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid actorPlayerId,
        Guid launchedPlayerId,
        PitchSquare targetSquare,
        string requiredSkillId,
        string actionName)
    {
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"{actionName} target must be on the pitch.");
        }

        var actor = FindTeamPlayer(team, actorPlayerId);
        if (!PlayerHasLaunchActionEffect(ruleset, actor, requiredSkillId))
        {
            throw new InvalidOperationException($"{actor.Name} does not have {actionName}.");
        }

        var launched = FindTeamPlayer(team, launchedPlayerId);
        if (!PlayerHasHookedEffect(ruleset, launched, GameEventKind.PassRoll, GameEventStage.BeforeEvent, SkillEffect.RightStuff))
        {
            throw new InvalidOperationException($"{launched.Name} does not have Right Stuff.");
        }

        if (actor.Id == launched.Id)
        {
            throw new InvalidOperationException($"{actionName} requires two different players.");
        }

        var actorPlacement = ValidateSpecialActor(match, team, actor, requireStanding: true);
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"{actionName} target must be on the pitch.");
        }

        var launchedPlacement = FindStandingPlacement(match, launchedPlayerId, team.Id, "launched player");
        if (!IsAdjacent(actorPlacement.Square!, launchedPlacement.Square!))
        {
            throw new InvalidOperationException($"{actionName} requires an adjacent Right Stuff team-mate.");
        }

        return new TeamMateLaunchContext(actor, actorPlacement, launched, launchedPlacement);
    }

    private PitchSquare ScatterLaunchedPlayer(Ruleset ruleset, PitchSquare targetSquare, Player launchedPlayer, int inaccurateDistance)
    {
        if (!PlayerHasHookedEffect(ruleset, launchedPlayer, GameEventKind.BallScatter, GameEventStage.BeforeResolve, SkillEffect.Swoop))
        {
            return ScatterFrom(ruleset, targetSquare, inaccurateDistance);
        }

        var direction = _dice.RollD8();
        var swoopDistance = Math.Max(1, _dice.RollD6() / 2);
        var next = targetSquare;
        for (var step = 0; step < swoopDistance; step++)
        {
            next = ScatterDirection(next, direction);
        }

        return next;
    }

    private MatchState ResolveLaunchedPlayerLanding(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player launchedPlayer,
        PitchSquare landingSquare,
        string launchKind,
        int collisionDepth = 0)
    {
        if (!IsOnPitch(ruleset, landingSquare))
        {
            var injury = ResolveFallInjury(launchedPlayer);
            var crowdState = injury.State;
            var outMatch = match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == launchedPlayer.Id
                        ? ApplyPitchState(match, placement, crowdState, OccupiesPitch(crowdState) ? null : null, injury.Casualty)
                        : placement)
                    .ToArray(),
                Ball = match.Ball.CarrierPlayerId == launchedPlayer.Id ? new BallState() : match.Ball,
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} is {launchKind} out of bounds and crashes." }
                ]
            };

            return ApplyTurnover(outMatch, ruleset, team.Id);
        }

        var occupant = match.Placements.FirstOrDefault(placement =>
            placement.PlayerId != launchedPlayer.Id &&
            placement.Square == landingSquare &&
            OccupiesPitch(placement.State));
        if (occupant is not null)
        {
            var occupantPlayer = FindCollisionPlayer(team, opposingTeam, occupant);
            var knockedMatch = KnockPlayerDown(match, ruleset, occupantPlayer, occupant, ResolveFallInjury(occupantPlayer), landingSquare);
            var collisionMatch = knockedMatch with
            {
                Log =
                [
                    .. knockedMatch.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} crashes into {occupantPlayer.Name} at {landingSquare.X},{landingSquare.Y}; {occupantPlayer.Name} is knocked down." }
                ]
            };
            var nextLandingSquare = ScatterFrom(ruleset, landingSquare);
            var scatteredMatch = collisionMatch with
            {
                Log =
                [
                    .. collisionMatch.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} scatters from the occupied square to {nextLandingSquare.X},{nextLandingSquare.Y}." }
                ]
            };

            if (collisionDepth >= 7)
            {
                var injury = ResolveFallInjury(launchedPlayer);
                var crashMatch = KnockLaunchedPlayerDown(scatteredMatch, ruleset, team, launchedPlayer, nextLandingSquare, injury, $"{launchedPlayer.Name}'s collision chain cannot be resolved and they crash.");
                return ApplyTurnover(crashMatch, ruleset, team.Id);
            }

            return ResolveLaunchedPlayerLanding(scatteredMatch, ruleset, team, opposingTeam, launchedPlayer, nextLandingSquare, launchKind, collisionDepth + 1);
        }

        var target = LandingTarget(ruleset, launchedPlayer, CountOpposingTackleZones(match, team.Id, launchedPlayer.Id, landingSquare));
        var roll = _dice.RollD6();
        if (!RollSucceeds(roll, target, ruleset.Dice))
        {
            var injury = ResolveFallInjury(launchedPlayer);
            var crashMatch = KnockLaunchedPlayerDown(match, ruleset, team, launchedPlayer, landingSquare, injury, $"{launchedPlayer.Name} attempts to land after being {launchKind}: rolled {roll} vs {target}+, failed.");
            return ApplyTurnover(crashMatch, ruleset, team.Id);
        }

        var landedMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == launchedPlayer.Id
                    ? placement with { Square = landingSquare, State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{launchedPlayer.Name} lands after being {launchKind}: rolled {roll} vs {target}+, success at {landingSquare.X},{landingSquare.Y}." }
            ]
        };

        return IsTouchdown(landedMatch, ruleset, team, launchedPlayer.Id, landingSquare)
            ? ScoreTouchdown(landedMatch, ruleset, team)
            : landedMatch;
    }

    private static Player FindCollisionPlayer(LeagueTeam team, LeagueTeam? opposingTeam, PlayerPlacement placement)
    {
        if (placement.TeamId == team.Id)
        {
            return FindTeamPlayer(team, placement.PlayerId);
        }

        if (opposingTeam is not null && placement.TeamId == opposingTeam.Id)
        {
            return FindTeamPlayer(opposingTeam, placement.PlayerId);
        }

        throw new InvalidOperationException("An opposing team is required to resolve occupied launch collisions.");
    }

    private MatchState KnockLaunchedPlayerDown(MatchState match, Ruleset ruleset, LeagueTeam team, Player launchedPlayer, PitchSquare landingSquare, InjuryResolution injury, string message)
    {
        var placement = match.Placements.First(placement => placement.PlayerId == launchedPlayer.Id);
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, launchedPlayer.Name, injury);
        var nextMatch = apothecary.Match;
        injury = apothecary.Injury;
        var ball = nextMatch.Ball;
        var log = new List<MatchLogEntry> { new() { Message = message } };
        if (ball.CarrierPlayerId == launchedPlayer.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, landingSquare);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        log.AddRange(apothecary.Log);

        return nextMatch with
        {
            Ball = ball,
            Placements = nextMatch.Placements
                .Select(current => current.PlayerId == launchedPlayer.Id
                    ? ApplyPitchState(nextMatch, current, injury.State, OccupiesPitch(injury.State) ? landingSquare : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. nextMatch.Log, .. log]
        };
    }

    private static MatchState RemoveLaunchedPlayer(MatchState match, Guid playerId, PlayerPitchState state)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == playerId
                    ? placement with { Square = null, State = state, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null }
                    : placement)
                .ToArray()
        };
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

        var movementAllowance = isStandingUp && !PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.JumpUp)
            ? Math.Max(0, player.Stats.Movement - 3)
            : player.Stats.Movement;
        var maxGoForIts = PlayerHasHookedEffect(ruleset, player, GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.Sprint)
            ? SprintGoForItsPerActivation
            : MaxGoForItsPerActivation;
        var goForItsUsed = Math.Max(0, path.Length - movementAllowance);
        if (goForItsUsed > maxGoForIts)
        {
            var movementDescription = isStandingUp
                ? $"{movementAllowance} squares after standing"
                : $"{player.Stats.Movement} squares";
            throw new InvalidOperationException($"{player.Name} can move {movementDescription} plus {maxGoForIts} go-for-its, not {path.Length}.");
        }

        var startedAction = BeginPlayerAction(match, ruleset, team, player, action, goForItsUsed);
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

        var goForItNumber = 0;
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
                                MovementAllowance = movementAllowance,
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
                        movementAllowance,
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

            if (stepIndex >= movementAllowance)
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
                        movementAllowance,
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
                var pickupMatch = ResolvePickup(nextMatch, ruleset, team, player, nextSquare, action, destination, path, stepIndex, movementAllowance, blitzDefenderPlayerId);
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

    private MatchState ResolveBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        Player defender,
        int defenderStrengthBonus = 0,
        bool preventFollowUp = false)
    {
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == defender.Id);
        var strength = ResolveBlockStrength(match, ruleset, attackerTeam, attackerPlacement, defenderTeam, defenderPlacement, attacker, defender, defenderStrengthBonus);
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
                    new MatchLogEntry { Message = $"{attacker.Name} uses Brawler: one Both Down die is rerolled to {brawlerRoll}." }
                ]
            };
        }

        if (rolls.Length > 1)
        {
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
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: ST {strength.AttackerStrength}-{strength.DefenderStrength}, rolled {string.Join(", ", rolls)}. Choose a block die." }
                ]
            };
        }

        return ResolveChosenBlockDie(match, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender, defenderPlacement, strength, rolls, rolls[0], preventFollowUp);
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
        bool preventFollowUp = false)
    {
        var rollText = string.Join(", ", rolls);
        var strengthText = $"ST {strength.AttackerStrength}-{strength.DefenderStrength}, {strength.Dice} die{(strength.Dice == 1 ? "" : "s")}";
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
                var ball = match.Ball;
                var wrestleLog = new List<MatchLogEntry>();
                if (ball.CarrierPlayerId == attacker.Id || ball.CarrierPlayerId == defender.Id)
                {
                    var dropSquare = ball.CarrierPlayerId == attacker.Id ? attackerPlacement.Square! : defenderPlacement.Square!;
                    var scatterSquare = ScatterFrom(ruleset, dropSquare);
                    var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
                    ball = new BallState { Square = landing.Square };
                    wrestleLog.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
                    wrestleLog.AddRange(landing.Log);
                }

                var wrestledMatch = match with
                {
                    Ball = ball,
                    Placements = match.Placements
                        .Select(placement =>
                            placement.PlayerId == attacker.Id || placement.PlayerId == defender.Id
                                ? ApplyPitchState(match, placement, PlayerPitchState.Prone, placement.Square)
                                : placement)
                        .ToArray(),
                    Log =
                    [
                        .. match.Log,
                        new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, Wrestle places both players prone." },
                        .. wrestleLog
                    ]
                };

                return match.Ball.CarrierPlayerId == attacker.Id
                    ? ApplyTurnover(wrestledMatch, ruleset, attackerTeam.Id)
                    : wrestledMatch;
            }

            var attackerHasBlock = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.BothDownProtection);
            var defenderHasBlock = PlayerHasHookedEffect(ruleset, defender, GameEventKind.BlockRoll, GameEventStage.BeforeResolve, SkillEffect.BothDownProtection);
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
                ? resolvedMatch
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
            var resolvedMatch = PushPlayerIntoCrowd(match, ruleset, defenderPlacement);
            var loggedMatch = resolvedMatch with
            {
                Log =
                [
                    .. resolvedMatch.Log,
                    new MatchLogEntry { Message = $"{resultMessage} No legal push square is available; {defender.Name} is pushed into the crowd." }
                ]
            };

            return CompleteBlockPush(loggedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, defenderPlacement.Square!, knockDefenderDown, preventFollowUp);
        }

        if (legalSquares.Length == 1)
        {
            var stripBall = ShouldStripBall(ruleset, attacker, defender, match.Ball.CarrierPlayerId == defender.Id, knockDefenderDown);
            var pushedMatch = PushPlayer(match, ruleset, defender, defenderPlacement.Square!, legalSquares[0], knockDefenderDown, () => ResolveBlockInjury(ruleset, attacker, defender), stripBall);
            var loggedMatch = pushedMatch with
            {
                Log =
                [
                    .. pushedMatch.Log,
                    new MatchLogEntry { Message = $"{resultMessage} {defender.Name} is pushed to {legalSquares[0].X},{legalSquares[0].Y}." }
                ]
            };

            return CompleteBlockPush(loggedMatch, ruleset, attackerTeam, attacker, defenderTeam, defender, defenderPlacement.Square!, knockDefenderDown, preventFollowUp);
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
        bool knockDefenderDown,
        bool preventFollowUp = false)
    {
        var blocksMadeBeforePush = GetBlocksMade(match, attacker.Id, attackerTeam.Id);
        var awardedMatch = AwardCasualtyIfCaused(match, attackerTeam, attacker, defenderTeam, defender.Id);
        var countedMatch = IncrementActivationBlocksMade(awardedMatch, attacker.Id, attackerTeam.Id);
        if (preventFollowUp)
        {
            return countedMatch with
            {
                Log =
                [
                    .. countedMatch.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} cannot follow up while using Multiple Block." }
                ]
            };
        }

        if (!CanFollowUp(countedMatch, attacker.Id, attackerTeam.Id, followUpSquare))
        {
            return countedMatch;
        }

        if (PlayerHasHookedEffect(ruleset, defender, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Fend))
        {
            return countedMatch with
            {
                Log =
                [
                    .. countedMatch.Log,
                    new MatchLogEntry { Message = $"{defender.Name} uses Fend; {attacker.Name} cannot follow up." }
                ]
            };
        }

        var mustFrenzy = !knockDefenderDown &&
            blocksMadeBeforePush == 0 &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Frenzy);
        if (mustFrenzy)
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

            var attackerPlacement = followedMatch.Placements.First(placement => placement.PlayerId == attacker.Id);
            var defenderPlacement = followedMatch.Placements.FirstOrDefault(placement => placement.PlayerId == defender.Id);
            if (defenderPlacement?.Square is null ||
                defenderPlacement.State != PlayerPitchState.Standing ||
                attackerPlacement.Square is null ||
                !IsAdjacent(attackerPlacement.Square, defenderPlacement.Square))
            {
                return followedMatch with
                {
                    Log =
                    [
                        .. followedMatch.Log,
                        new MatchLogEntry { Message = $"{attacker.Name}'s Frenzy cannot continue because {defender.Name} is no longer adjacent and standing." }
                    ]
                };
            }

            return ResolveBlock(followedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
        }

        if (countedMatch.PendingApothecary is not null || countedMatch.PendingBallPlacement is not null)
        {
            return countedMatch;
        }

        return countedMatch with
        {
            PendingFollowUp = new PendingFollowUpChoice
            {
                AttackerTeamId = attackerTeam.Id,
                DefenderTeamId = defenderTeam.Id,
                AttackerPlayerId = attacker.Id,
                DefenderPlayerId = defender.Id,
                FollowUpSquare = followUpSquare
            },
            Log =
            [
                .. countedMatch.Log,
                new MatchLogEntry { Message = $"{attacker.Name} may follow up to {followUpSquare.X},{followUpSquare.Y}." }
            ]
        };
    }

    private static bool CanFollowUp(MatchState match, Guid attackerPlayerId, Guid attackerTeamId, PitchSquare followUpSquare)
    {
        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId);
        return attackerPlacement is { TeamId: var teamId, State: PlayerPitchState.Standing, Square: not null } &&
            teamId == attackerTeamId &&
            match.Placements.All(placement =>
                placement.PlayerId == attackerPlayerId ||
                placement.Square != followUpSquare ||
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
        int defenderStrengthBonus = 0)
    {
        var attackerAssists = CountAssists(match, ruleset, attackerTeam, defenderTeam, defenderPlacement.PlayerId, defenderPlacement.Square!, attackerPlacement.PlayerId);
        var defenderAssists = CountAssists(match, ruleset, defenderTeam, attackerTeam, attackerPlacement.PlayerId, attackerPlacement.Square!, defenderPlacement.PlayerId);
        var attackerAction = GetActivation(match, attacker.Id, attackerTeam.Id)?.Action ?? PlayerTurnAction.Block;
        var attackerBaseStrength = attacker.Stats.Strength + (attackerAction == PlayerTurnAction.Blitz && PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeRoll, SkillEffect.Horns) ? 1 : 0);
        if (attackerBaseStrength < defender.Stats.Strength &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.BlockRoll, GameEventStage.BeforeRoll, SkillEffect.Dauntless))
        {
            var dauntlessRoll = _dice.RollD6();
            if (dauntlessRoll + attackerBaseStrength > defender.Stats.Strength)
            {
                attackerBaseStrength = defender.Stats.Strength;
            }
        }

        var attackerStrength = attackerBaseStrength + attackerAssists;
        var defenderStrength = defender.Stats.Strength + defenderAssists + defenderStrengthBonus;
        var dice = ResolveBlockDice(attackerStrength, defenderStrength);

        return new BlockStrength(attackerStrength, defenderStrength, dice);
    }

    private int CountAssists(MatchState match, Ruleset ruleset, LeagueTeam assistingTeam, LeagueTeam opposingTeam, Guid opposedPlayerId, PitchSquare targetSquare, Guid primaryPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == assistingTeam.Id &&
            placement.PlayerId != primaryPlayerId &&
            placement.PlayerId != opposedPlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, targetSquare) &&
            (!IsMarkedByOpponent(match, assistingTeam.Id, placement.PlayerId, square, opposedPlayerId) ||
                (PlayerHasHookedEffect(ruleset, FindTeamPlayer(assistingTeam, placement.PlayerId), GameEventKind.BlockRoll, GameEventStage.ModifyTarget, SkillEffect.GuardAssist) &&
                    match.ActiveTeamId != opposingTeam.Id &&
                    !IsMarkedByOpponentWithHookedEffect(match, ruleset, assistingTeam.Id, opposingTeam, placement.PlayerId, square, opposedPlayerId, GameEventKind.BlockRoll, GameEventStage.ModifyTarget, SkillEffect.Defensive))));
    }

    private int CountFoulAssists(
        MatchState match,
        Guid assistingTeamId,
        Guid victimPlayerId,
        PitchSquare victimSquare,
        Guid foulerPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == assistingTeamId &&
            placement.PlayerId != foulerPlayerId &&
            placement.PlayerId != victimPlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, victimSquare) &&
            !IsMarkedByOpponent(match, assistingTeamId, placement.PlayerId, square, victimPlayerId));
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid assistingTeamId, Guid assistingPlayerId, PitchSquare assistingSquare, Guid ignoredOpponentId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != assistingTeamId &&
            placement.PlayerId != ignoredOpponentId &&
            placement.PlayerId != assistingPlayerId &&
            HasActiveTackleZone(placement) &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, assistingSquare));
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            HasActiveTackleZone(placement) &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square));
    }

    private static bool IsMarkedByOpponent(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, Guid teamId, Guid playerId, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return IsMarkedByOpponent(match, teamId, playerId, square);
        }

        return match.Placements.Any(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            HasActiveTackleZone(placement) &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square) &&
            !PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.ModifyTarget, SkillEffect.Titchy));
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
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), eventKind, stage, effect));
    }

    private static int ResolveBlockDice(int attackerStrength, int defenderStrength)
    {
        var high = Math.Max(attackerStrength, defenderStrength);
        var low = Math.Max(1, Math.Min(attackerStrength, defenderStrength));
        return high >= low * 2 ? 3 : high > low ? 2 : 1;
    }

    private static bool OccupiesPitch(PlayerPitchState state)
    {
        return state is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned;
    }

    private static bool HasActiveTackleZone(PlayerPlacement placement)
    {
        return placement.State == PlayerPitchState.Standing && !placement.TackleZonesLost;
    }

    private static PlayerPlacement ApplyPitchState(
        MatchState match,
        PlayerPlacement placement,
        PlayerPitchState state,
        PitchSquare? square,
        CasualtyRoll? casualty = null)
    {
        if (state == PlayerPitchState.Stunned)
        {
            var recoveryTurn = GetTeamTurn(match, placement.TeamId) + (placement.TeamId == match.ActiveTeamId ? 1 : 0);
            return placement with
            {
                Square = square,
                State = state,
                StunnedRecoveryHalf = match.Half,
                StunnedRecoveryTurn = recoveryTurn,
                Casualty = null
            };
        }

        return placement with
        {
            Square = square,
            State = state,
            StunnedRecoveryHalf = null,
            StunnedRecoveryTurn = null,
            Casualty = state is PlayerPitchState.Casualty or PlayerPitchState.Dead ? casualty : null
        };
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

        var distance = Math.Max(
            Math.Abs(attackerPlacement.Square.X - defenderPlacement.Square.X),
            Math.Abs(attackerPlacement.Square.Y - defenderPlacement.Square.Y));
        if (distance != 1)
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
        log.AddRange(apothecary.Log);

        if (match.Ball.CarrierPlayerId == player.Id)
        {
            var safeSquares = SafePairOfHandsSquares(match, ruleset, player, square);
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
                var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
                nextMatch = nextMatch with { Ball = new BallState { Square = landing.Square } };
                log.AddRange(landing.Log.Prepend(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." }));
            }
        }

        return nextMatch with
        {
            Placements = nextMatch.Placements
                .Select(current => current.PlayerId == player.Id
                    ? ApplyPitchState(nextMatch, current, injury.State, OccupiesPitch(injury.State) ? square : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. nextMatch.Log, .. log]
        };
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

        var ball = match.Ball;
        var log = new List<MatchLogEntry>();
        if (ball.CarrierPlayerId == playerId && (knockDown || stripBall))
        {
            var safeSquares = player is null ? [] : SafePairOfHandsSquares(match, ruleset, player, destination);
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
                var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
                ball = new BallState { Square = landing.Square };
                log.Add(new MatchLogEntry { Message = stripBall && !knockDown ? $"Strip Ball knocks the ball loose to {scatterSquare.X},{scatterSquare.Y}." : $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
                log.AddRange(landing.Log);
            }
        }
        else if (ball.CarrierPlayerId is null && ball.Square == destination)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball is pushed from {destination.X},{destination.Y} to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var injury = knockDown ? resolveKnockdownState() : new InjuryResolution(PlayerPitchState.Standing);
        var nextState = injury.State;
        if (!knockDown)
        {
            nextState = placement.State;
            injury = new InjuryResolution(nextState);
        }

        return match with
        {
            Ball = ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId
                    ? ApplyPitchState(match, current, nextState, OccupiesPitch(nextState) ? destination : null, injury.Casualty)
                    : current)
                .ToArray(),
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

        return onPitchCandidates
            .Where(square => CanResolvePushDestination(match, ruleset, defenderSquare, square, defenderPlayerId))
            .ToArray();
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
            placement.Square == square &&
            OccupiesPitch(placement.State));
    }

    private MatchState PushPlayerIntoCrowd(MatchState match, Ruleset ruleset, PlayerPlacement placement)
    {
        var injuryState = ResolveInjury(Roll2D6());
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, placement.PlayerId.ToString(), injuryState);
        match = apothecary.Match;
        injuryState = apothecary.Injury;
        var crowdState = injuryState.State is PlayerPitchState.KnockedOut or PlayerPitchState.Casualty or PlayerPitchState.Dead
            ? injuryState.State
            : PlayerPitchState.Reserve;
        var ball = match.Ball;
        var log = new List<MatchLogEntry>();
        if (ball.CarrierPlayerId == placement.PlayerId && placement.Square is PitchSquare square)
        {
            var scatterSquare = ScatterFrom(ruleset, square);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball scatters in from the crowd to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var crowdLog = new List<MatchLogEntry>
        {
            new() { Message = $"{placement.PlayerId} is pushed into the crowd: {FormatPitchState(crowdState)}." }
        };
        if (injuryState.Casualty is not null)
        {
            crowdLog.Add(new MatchLogEntry { Message = $"{placement.PlayerId} casualty roll {injuryState.Casualty.Roll}: {FormatCasualtyResult(injuryState.Casualty.Result)}." });
        }
        crowdLog.AddRange(apothecary.Log);
        crowdLog.AddRange(log);

        return match with
        {
            Ball = ball,
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
                .ToArray(),
            Log =
            [
                .. match.Log,
                .. crowdLog
            ]
        };
    }

    private MatchState BounceBall(MatchState match, Ruleset ruleset, LeagueTeam originalTeam, PitchSquare square, bool allowDivingCatch = true, LeagueTeam? opposingTeam = null)
    {
        if (!IsOnPitch(ruleset, square))
        {
            var landing = ResolveThrowIn(ruleset, square);
            return BounceBall(match with
            {
                Log =
                [
                    .. match.Log,
                    .. landing.Log
                ]
            }, ruleset, originalTeam, landing.Square, allowDivingCatch: true, opposingTeam);
        }

        var receiverPlacement = match.Placements.FirstOrDefault(placement =>
            placement.Square == square &&
            placement.State == PlayerPitchState.Standing);

        if (receiverPlacement is null)
        {
            if (allowDivingCatch)
            {
                var divingCatchPlacement = FindDivingCatchReceiver(match, ruleset, originalTeam, square);
                if (divingCatchPlacement is not null)
                {
                    var divingReceiver = FindTeamPlayer(originalTeam, divingCatchPlacement.PlayerId);
                    var divingTackleZones = PlayerHasHookedEffect(ruleset, divingReceiver, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
                        ? 0
                        : CountOpposingTackleZones(match, originalTeam.Id, divingReceiver.Id, square);
                    var divingDisturbingPresence = DisturbingPresenceModifier(match, ruleset, opposingTeam, square);
                    var divingTarget = CatchTarget(ruleset, divingReceiver, match.Weather, divingTackleZones, divingDisturbingPresence);
                    var divingAttempt = RollCatch(ruleset, divingReceiver, divingTarget);
                    var movedMatch = match with
                    {
                        Placements = match.Placements
                            .Select(placement => placement.PlayerId == divingReceiver.Id
                                ? placement with { Square = square }
                                : placement)
                            .ToArray()
                    };

                    if (divingAttempt.Success)
                    {
                        return movedMatch with
                        {
                            Ball = new BallState { CarrierPlayerId = divingReceiver.Id },
                            Log =
                            [
                                .. movedMatch.Log,
                                new MatchLogEntry { Message = $"{divingReceiver.Name} uses Diving Catch at {square.X},{square.Y}: {FormatCatchAttempt(divingAttempt, divingTarget)}, success." }
                            ]
                        };
                    }

                    var divingNextSquare = ScatterFrom(ruleset, square);
                    return BounceBall(movedMatch with
                    {
                        Log =
                        [
                            .. movedMatch.Log,
                            new MatchLogEntry { Message = $"{divingReceiver.Name} uses Diving Catch at {square.X},{square.Y}: {FormatCatchAttempt(divingAttempt, divingTarget)}, failed." }
                        ]
                    }, ruleset, originalTeam, divingNextSquare, opposingTeam: opposingTeam);
                }
            }

            return match with { Ball = new BallState { Square = square } };
        }

        if (receiverPlacement.TeamId != originalTeam.Id)
        {
            return match with { Ball = new BallState { Square = square } };
        }

        var receiver = FindTeamPlayer(originalTeam, receiverPlacement.PlayerId);
        var receiverTackleZones = PlayerHasHookedEffect(ruleset, receiver, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.NervesOfSteel)
            ? 0
            : CountOpposingTackleZones(match, originalTeam.Id, receiver.Id, receiverPlacement.Square!);
        var receiverDisturbingPresence = DisturbingPresenceModifier(match, ruleset, opposingTeam, receiverPlacement.Square!);
        var target = CatchTarget(ruleset, receiver, match.Weather, receiverTackleZones, receiverDisturbingPresence);
        var catchAttempt = RollCatch(ruleset, receiver, target);

        if (catchAttempt.Success)
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{receiver.Name} catches the bouncing ball: {FormatCatchAttempt(catchAttempt, target)}." }
                ]
            };
        }

        var nextSquare = ScatterFrom(ruleset, square);
        var nextMatch = match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{receiver.Name} fails to catch the bouncing ball: {FormatCatchAttempt(catchAttempt, target)}." }
            ]
        };

        return BounceBall(nextMatch, ruleset, originalTeam, nextSquare, opposingTeam: opposingTeam);
    }

    private MatchState ResolveBallLanding(MatchState match, Ruleset ruleset, LeagueTeam originalTeam, PitchSquare square, bool allowDivingCatch = true, LeagueTeam? opposingTeam = null)
    {
        return BounceBall(match, ruleset, originalTeam, square, allowDivingCatch, opposingTeam);
    }

    private BallLanding ResolveLooseBallLanding(Ruleset ruleset, PitchSquare square)
    {
        if (IsOnPitch(ruleset, square))
        {
            return new BallLanding(square, []);
        }

        return ResolveThrowIn(ruleset, square);
    }

    private BallLanding ResolveThrowIn(Ruleset ruleset, PitchSquare outOfBoundsSquare)
    {
        var start = new PitchSquare(
            Math.Clamp(outOfBoundsSquare.X, 0, ruleset.PitchWidth - 1),
            Math.Clamp(outOfBoundsSquare.Y, 0, ruleset.PitchHeight - 1));
        var directionRoll = _dice.RollD6();
        var distance = Roll2D6();
        var leftOrRight = outOfBoundsSquare.X < 0 || outOfBoundsSquare.X >= ruleset.PitchWidth;
        int dx;
        int dy;

        if (leftOrRight)
        {
            dx = outOfBoundsSquare.X < 0 ? 1 : -1;
            dy = directionRoll <= 2 ? -1 : directionRoll <= 4 ? 0 : 1;
        }
        else
        {
            dx = directionRoll <= 2 ? -1 : directionRoll <= 4 ? 0 : 1;
            dy = outOfBoundsSquare.Y < 0 ? 1 : -1;
        }

        var landing = new PitchSquare(
            Math.Clamp(start.X + (dx * distance), 0, ruleset.PitchWidth - 1),
            Math.Clamp(start.Y + (dy * distance), 0, ruleset.PitchHeight - 1));

        return new BallLanding(
            landing,
            [
                new MatchLogEntry { Message = $"Ball went out of bounds at {outOfBoundsSquare.X},{outOfBoundsSquare.Y}. Throw-in roll {directionRoll}, distance {distance}, lands at {landing.X},{landing.Y}." }
            ]);
    }

    private ActionStart BeginPlayerAction(MatchState match, Ruleset ruleset, LeagueTeam team, Player player, PlayerTurnAction action, int goForItsUsed)
    {
        var existingActivation = GetActivation(match, player.Id, team.Id);
        EnsureCanDeclarePlayerAction(match, team, player, action, allowMatchingDeclaration: true);
        var traitCheck = ResolveTraitActionCheck(match, ruleset, team, player, action, existingActivation);
        if (traitCheck.Prevented)
        {
            return traitCheck;
        }

        match = traitCheck.Match;

        if (existingActivation is { DeclaredOnly: true } && existingActivation.Action == action)
        {
            return new ActionStart(match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == player.Id &&
                        activation.TeamId == team.Id &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { GoForItsUsed = goForItsUsed, DeclaredOnly = false }
                            : activation)
                    .ToArray()
            }, Prevented: false);
        }

        if (existingActivation is { Action: PlayerTurnAction.Foul, MayMoveAfterFoul: true } && action == PlayerTurnAction.Move)
        {
            return new ActionStart(match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == player.Id &&
                        activation.TeamId == team.Id &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { GoForItsUsed = goForItsUsed, MayMoveAfterFoul = false }
                            : activation)
                    .ToArray()
            }, Prevented: false);
        }

        return new ActionStart(AddActivation(match, player.Id, team.Id, action, goForItsUsed, declaredOnly: false), Prevented: false);
    }

    private static void EnsureCanDeclarePlayerAction(MatchState match, LeagueTeam team, Player player, PlayerTurnAction action, bool allowMatchingDeclaration)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Player actions can only be declared during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can declare a player action during its turn.");
        }

        EnsureNoPendingChoices(match);

        var existingActivation = GetActivation(match, player.Id, team.Id);
        var canUseSneakyGitMove = existingActivation is { Action: PlayerTurnAction.Foul, MayMoveAfterFoul: true } &&
            action == PlayerTurnAction.Move;
        var matchesExistingDeclaration = existingActivation is { DeclaredOnly: true } &&
            existingActivation.Action == action;
        if (existingActivation is not null && !canUseSneakyGitMove && (!allowMatchingDeclaration || !matchesExistingDeclaration))
        {
            throw new InvalidOperationException($"{player.Name} has already been activated this turn.");
        }

        if (!matchesExistingDeclaration && !canUseSneakyGitMove && HasUsedTeamAction(match, team.Id, action))
        {
            throw new InvalidOperationException($"{team.Name} has already used its {FormatPlayerTurnAction(action).ToLowerInvariant()} this turn.");
        }
    }

    private ActionStart ResolveTraitActionCheck(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        PlayerTurnActivation? existingActivation)
    {
        if (existingActivation is { DeclaredOnly: false })
        {
            return new ActionStart(match, Prevented: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "bone-head"))
        {
            return ResolveReliabilityTrait(match, team, player, action, "Bone-head", target: 2, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "really-stupid"))
        {
            var hasHelper = HasAdjacentStandingTeammate(match, team.Id, player.Id);
            return ResolveReliabilityTrait(match, team, player, action, "Really Stupid", target: hasHelper ? 2 : 4, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "take-root") &&
            FindPlacement(match, player.Id)?.Rooted != true)
        {
            var roll = _dice.RollD6();
            if (roll == 1)
            {
                var rootMessage = $"{player.Name} checks Take Root: rolled 1, is rooted, and cannot move.";
                if (action is not (PlayerTurnAction.Move or PlayerTurnAction.Blitz))
                {
                    return new ActionStart(MarkPlayerTemporaryState(
                        match,
                        player.Id,
                        tackleZonesLost: false,
                        rooted: true,
                        rootMessage), Prevented: false);
                }

                var rootedMatch = MarkPlayerTemporaryState(
                    AddOrResolveActivation(match, player.Id, team.Id, action),
                    player.Id,
                    tackleZonesLost: false,
                    rooted: true,
                    rootMessage);

                return new ActionStart(rootedMatch, Prevented: true);
            }

            return new ActionStart(match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == player.Id
                        ? placement with { TackleZonesLost = false }
                        : placement)
                    .ToArray(),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} checks Take Root: rolled {roll}, action continues." }
                ]
            }, Prevented: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "animal-savagery"))
        {
            var target = action is PlayerTurnAction.Block or PlayerTurnAction.Blitz ? 2 : 4;
            return ResolveAggressionTrait(match, ruleset, team, player, action, "Animal Savagery", target, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "unchannelled-fury"))
        {
            var target = action is PlayerTurnAction.Block or PlayerTurnAction.Blitz ? 2 : 4;
            return ResolveReliabilityTrait(match, team, player, action, "Unchannelled Fury", target, loseTackleZonesOnFailure: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "bloodlust"))
        {
            return ResolveAggressionTrait(match, ruleset, team, player, action, "Bloodlust", target: 2, loseTackleZonesOnFailure: false);
        }

        return new ActionStart(match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = false }
                    : placement)
                .ToArray()
        }, Prevented: false);
    }

    private ActionStart ResolveReliabilityTrait(
        MatchState match,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        string traitName,
        int target,
        bool loseTackleZonesOnFailure)
    {
        var roll = _dice.RollD6();
        if (roll < target)
        {
            var failedMatch = MarkPlayerTemporaryState(
                AddOrResolveActivation(match, player.Id, team.Id, action),
                player.Id,
                tackleZonesLost: loseTackleZonesOnFailure,
                rooted: null,
                $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action is wasted.");
            return new ActionStart(failedMatch, Prevented: true);
        }

        return new ActionStart(match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = false }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action continues." }
            ]
        }, Prevented: false);
    }

    private ActionStart ResolveAggressionTrait(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        string traitName,
        int target,
        bool loseTackleZonesOnFailure)
    {
        var roll = _dice.RollD6();
        if (roll >= target)
        {
            return new ActionStart(match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == player.Id
                        ? placement with { TackleZonesLost = false }
                        : placement)
                    .ToArray(),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action continues." }
                ]
            }, Prevented: false);
        }

        var activationMatch = AddOrResolveActivation(match, player.Id, team.Id, action);
        var biteTargetPlacement = AdjacentSquares(match.Placements.First(placement => placement.PlayerId == player.Id).Square!)
            .Select(square => match.Placements.FirstOrDefault(placement =>
                placement.TeamId == team.Id &&
                placement.PlayerId != player.Id &&
                placement.Square == square &&
                placement.State == PlayerPitchState.Standing))
            .FirstOrDefault(placement => placement is not null);

        if (biteTargetPlacement is null)
        {
            var failedMatch = MarkPlayerTemporaryState(
                activationMatch,
                player.Id,
                tackleZonesLost: loseTackleZonesOnFailure,
                rooted: null,
                $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, no adjacent teammate to bite; action is wasted.");
            return new ActionStart(failedMatch, Prevented: true);
        }

        var biteTarget = FindTeamPlayer(team, biteTargetPlacement.PlayerId);
        var bittenMatch = ResolveSpecialArmorAttack(
            activationMatch,
            ruleset,
            team,
            biteTarget,
            biteTargetPlacement,
            armorModifier: 0,
            $"{player.Name} bites {biteTarget.Name} after failing {traitName}");

        return new ActionStart(bittenMatch with
        {
            Placements = bittenMatch.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = loseTackleZonesOnFailure }
                    : placement)
                .ToArray(),
            Log =
            [
                .. bittenMatch.Log,
                new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, bites a teammate and continues." }
            ]
        }, Prevented: bittenMatch.Phase != match.Phase || bittenMatch.ActiveTeamId != match.ActiveTeamId);
    }

    private MatchState AddOrResolveActivation(MatchState match, Guid playerId, Guid teamId, PlayerTurnAction action)
    {
        var existingActivation = GetActivation(match, playerId, teamId);
        if (existingActivation is { DeclaredOnly: true } && existingActivation.Action == action)
        {
            return match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == playerId &&
                        activation.TeamId == teamId &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { DeclaredOnly = false }
                            : activation)
                    .ToArray()
            };
        }

        return AddActivation(match, playerId, teamId, action, goForItsUsed: 0, declaredOnly: false);
    }

    private static MatchState MarkPlayerTemporaryState(MatchState match, Guid playerId, bool tackleZonesLost, bool? rooted, string message)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == playerId
                    ? placement with
                    {
                        TackleZonesLost = tackleZonesLost,
                        Rooted = rooted ?? placement.Rooted
                    }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    private static bool HasUsedTeamAction(MatchState match, Guid teamId, PlayerTurnAction action)
    {
        return (action is PlayerTurnAction.Blitz or PlayerTurnAction.HandOff or PlayerTurnAction.Pass or PlayerTurnAction.Foul) &&
            match.Activations.Any(activation =>
                activation.TeamId == teamId &&
                activation.Half == match.Half &&
                activation.Turn == match.Turn &&
                activation.Action == action);
    }

    private static string FormatPlayerTurnAction(PlayerTurnAction action)
    {
        return action switch
        {
            PlayerTurnAction.HandOff => "Hand-off",
            _ => action.ToString()
        };
    }

    private MatchState AddActivation(MatchState match, Guid playerId, Guid teamId, PlayerTurnAction action, int goForItsUsed, bool declaredOnly)
    {
        return match with
        {
            Activations =
            [
                .. match.Activations,
                new PlayerTurnActivation
                {
                    PlayerId = playerId,
                    TeamId = teamId,
                    Half = match.Half,
                    Turn = match.Turn,
                    GoForItsUsed = goForItsUsed,
                    Action = action,
                    DeclaredOnly = declaredOnly
                }
            ]
        };
    }

    private static MatchState UpdateActivationGoForIts(MatchState match, Guid playerId, Guid teamId, int goForItsUsed)
    {
        return match with
        {
            Activations = match.Activations
                .Select(activation =>
                    activation.PlayerId == playerId &&
                    activation.TeamId == teamId &&
                    activation.Half == match.Half &&
                    activation.Turn == match.Turn
                        ? activation with { GoForItsUsed = goForItsUsed }
                        : activation)
                .ToArray()
        };
    }

    private static int GetBlocksMade(MatchState match, Guid playerId, Guid teamId)
    {
        return GetActivation(match, playerId, teamId)?.BlocksMade ?? 0;
    }

    private static MatchState IncrementActivationBlocksMade(MatchState match, Guid playerId, Guid teamId)
    {
        return match with
        {
            Activations = match.Activations
                .Select(activation =>
                    activation.PlayerId == playerId &&
                    activation.TeamId == teamId &&
                    activation.Half == match.Half &&
                    activation.Turn == match.Turn
                        ? activation with { BlocksMade = activation.BlocksMade + 1 }
                        : activation)
                .ToArray()
        };
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
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
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
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
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

    public MatchState ResolvePendingReroll(
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

    private MatchState ApplyTurnover(MatchState match, Ruleset ruleset, Guid turnoverTeamId)
    {
        var nextMatch = match.Phase switch
        {
            MatchPhase.OffensivePlayerTurn => EndActivePlayerTurn(match, ruleset, null),
            MatchPhase.DefensiveTurn => AdvanceTurn(match, ruleset),
            _ => match
        };

        return nextMatch with
        {
            PendingBlock = null,
            PendingPush = null,
            PendingInterception = null,
            PendingReroll = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = "Turnover." }
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

    private static int TeamKickoffStaff(MatchState match, Guid teamId, bool cheerleaders)
    {
        if (teamId == match.HomeTeamId)
        {
            return cheerleaders ? match.HomeCheerleaders : match.HomeAssistantCoaches;
        }

        return cheerleaders ? match.AwayCheerleaders : match.AwayAssistantCoaches;
    }

    private static IReadOnlyList<string> AvailableSkillRerolls(Ruleset ruleset, Player player, PendingRerollKind kind)
    {
        var (eventKind, effect) = kind switch
        {
            PendingRerollKind.Dodge => (GameEventKind.DodgeRoll, SkillEffect.DodgeReroll),
            PendingRerollKind.Pickup => (GameEventKind.PickupRoll, SkillEffect.PickupReroll),
            PendingRerollKind.GoForIt => (GameEventKind.GoForItRoll, SkillEffect.GoForItReroll),
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

    private PitchSquare[] LegalPushSquares(
        MatchState match,
        Ruleset ruleset,
        PitchSquare attackerSquare,
        PitchSquare defenderSquare,
        Player attacker,
        Player defender,
        PlayerTurnAction attackerAction)
    {
        var attackerCanUseGrab = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.Grab) &&
            !PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Frenzy);
        if (attackerCanUseGrab && attackerAction == PlayerTurnAction.Block)
        {
            var grabSquares = AdjacentSquares(defenderSquare)
                .Where(square => IsOnPitch(ruleset, square))
                .Where(square => square != attackerSquare)
                .Where(square => FindPushOccupant(match, square, defender.Id) is null)
                .Distinct()
                .ToArray();

            return grabSquares.Length > 0
                ? grabSquares
                : LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
        }

        if (attackerCanUseGrab ||
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.SideStep))
        {
            return LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
        }

        var sideStepSquares = AdjacentSquares(defenderSquare)
            .Where(square => IsOnPitch(ruleset, square))
            .Where(square => FindPushOccupant(match, square, defender.Id) is null)
            .Distinct()
            .ToArray();

        return sideStepSquares.Length > 0
            ? sideStepSquares
            : LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
    }

    private static bool PlayerHasHookedEffect(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return SkillHookResolver.PlayerHasHookedEffect(ruleset, player, eventKind, stage, effect);
    }

    private static bool PlayerHasHookedSkillId(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        string skillId)
    {
        return SkillHookResolver.PlayerHasHookedSkillId(ruleset, player, eventKind, stage, skillId);
    }

    private static bool PlayerHasLaunchActionEffect(Ruleset ruleset, Player player, string requiredSkillId)
    {
        var requiredEffect = requiredSkillId switch
        {
            "throw-team-mate" => SkillEffect.ThrowTeamMate,
            "kick-team-mate" => SkillEffect.KickTeamMate,
            _ => throw new InvalidOperationException($"Unknown launch skill '{requiredSkillId}'.")
        };

        return PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.BeforeEvent, requiredEffect);
    }

    private static GameEventKind PendingRerollEventKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.Dodge => GameEventKind.DodgeRoll,
            PendingRerollKind.Pickup => GameEventKind.PickupRoll,
            PendingRerollKind.GoForIt => GameEventKind.GoForItRoll,
            _ => throw new InvalidOperationException("Unknown reroll kind.")
        };
    }

    private static bool PlayerHasSkillId(Player player, string skillId)
    {
        return SkillCatalog.PlayerHasSkillId(player, skillId);
    }

    private static bool HasAdjacentStandingTeammate(MatchState match, Guid teamId, Guid playerId)
    {
        var placement = FindPlacement(match, playerId);
        if (placement?.Square is not PitchSquare square)
        {
            return false;
        }

        return match.Placements.Any(teammate =>
            teammate.TeamId == teamId &&
            teammate.PlayerId != playerId &&
            teammate.State == PlayerPitchState.Standing &&
            teammate.Square is PitchSquare teammateSquare &&
            IsAdjacent(square, teammateSquare));
    }

    private static bool IsAdjacentToOpponentWithHookedEffect(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam opposingTeam,
        Guid playerId,
        PitchSquare square,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.PlayerId != playerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), eventKind, stage, effect));
    }

    private static bool ArmBarApplies(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Guid playerId,
        PitchSquare currentSquare,
        PitchSquare destination)
    {
        return opposingTeam is not null &&
            (IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, currentSquare, GameEventKind.DodgeRoll, GameEventStage.BeforeResolve, SkillEffect.ArmBar) ||
                IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, destination, GameEventKind.DodgeRoll, GameEventStage.BeforeResolve, SkillEffect.ArmBar));
    }

    private static int PrehensileTailModifier(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Guid playerId,
        PitchSquare currentSquare)
    {
        return opposingTeam is not null &&
            IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, currentSquare, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.PrehensileTail)
                ? 1
                : 0;
    }

    private static int DisturbingPresenceModifier(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return 0;
        }

        return match.Placements.Count(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned &&
            placement.Square is PitchSquare disturbingSquare &&
            Math.Max(Math.Abs(disturbingSquare.X - square.X), Math.Abs(disturbingSquare.Y - square.Y)) <= 3 &&
            (PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence) ||
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence) ||
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence)));
    }

    private TentaclesResolution ApplyTentacles(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Player mover,
        PitchSquare currentSquare)
    {
        if (opposingTeam is null)
        {
            return new TentaclesResolution(match, false);
        }

        var tentaclePlacement = match.Placements
            .Where(placement =>
                placement.TeamId == opposingTeam.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                IsAdjacent(square, currentSquare) &&
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.BeforeResolve, SkillEffect.Tentacles))
            .FirstOrDefault();

        if (tentaclePlacement is null)
        {
            return new TentaclesResolution(match, false);
        }

        var tentaclePlayer = FindTeamPlayer(opposingTeam, tentaclePlacement.PlayerId);
        var roll = _dice.RollD6();
        var result = roll + tentaclePlayer.Stats.Strength - mover.Stats.Strength;
        if (roll == 1 || result <= 5)
        {
            return new TentaclesResolution(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{tentaclePlayer.Name} uses Tentacles against {mover.Name}: rolled {roll}, result {result}, no effect." }
                ]
            }, false);
        }

        return new TentaclesResolution(match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{tentaclePlayer.Name} uses Tentacles against {mover.Name}: rolled {roll}, result {result}, movement ends." }
            ]
        }, true);
    }

    private static bool ShouldStripBall(Ruleset ruleset, Player attacker, Player defender, bool defenderHasBall, bool knockDown)
    {
        return defenderHasBall &&
            !knockDown &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.StripBall) &&
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.PickupRoll, GameEventStage.AfterRoll, SkillEffect.PickupReroll) &&
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.CatchRoll, GameEventStage.AfterRoll, SkillEffect.MonstrousMouth);
    }

    private FoulAppearanceResolution ResolveFoulAppearance(MatchState match, Ruleset ruleset, Player attacker, Player defender)
    {
        if (!PlayerHasHookedEffect(ruleset, defender, GameEventKind.BlockRoll, GameEventStage.BeforeEvent, SkillEffect.FoulAppearance))
        {
            return new FoulAppearanceResolution(match, false);
        }

        var roll = _dice.RollD6();
        if (roll != 1)
        {
            return new FoulAppearanceResolution(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} checks Foul Appearance against {defender.Name}: rolled {roll}, action continues." }
                ]
            }, false);
        }

        return new FoulAppearanceResolution(match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} checks Foul Appearance against {defender.Name}: rolled 1, action wasted." }
            ]
        }, true);
    }

    private static bool HasKickPlayer(MatchState match, Ruleset ruleset, LeagueTeam kickingTeam)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == kickingTeam.Id &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is not null &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(kickingTeam, placement.PlayerId), GameEventKind.BallScatter, GameEventStage.BeforeEvent, SkillEffect.Kick));
    }

    private static bool HasLeaderPlayer(Ruleset ruleset, LeagueTeam team)
    {
        return team.Players.Any(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, SkillEffect.Leader));
    }

    private static bool HasLeaderOnPitch(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == team.Id &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(team, placement.PlayerId), GameEventKind.ActionStart, GameEventStage.BeforeEvent, SkillEffect.Leader));
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
            placement.Square is PitchSquare shadowerSquare &&
            IsAdjacent(shadowerSquare, fromSquare) &&
            !IsAdjacent(shadowerSquare, toSquare) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.AfterEvent, SkillEffect.Shadowing));
        if (shadowerPlacement is null || match.Placements.Any(placement => placement.PlayerId != shadowerPlacement.PlayerId && placement.Square == fromSquare && OccupiesPitch(placement.State)))
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

    private static string FormatRerollKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.GoForIt => "go-for-it",
            _ => kind.ToString().ToLowerInvariant()
        };
    }

    private MatchState EndActivePlayerTurn(MatchState match, Ruleset? ruleset, string? message)
    {
        var recoveredMatch = RecoverStunnedPlayers(match, match.ActiveTeamId);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, recoveredMatch.ActiveTeamId);
        if (ruleset is not null && BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            return AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var nextActiveTeam = GetOpponentTeamId(consumedTurnMatch, consumedTurnMatch.ActiveTeamId);
        return consumedTurnMatch with
        {
            Phase = MatchPhase.DefensiveTurn,
            ActiveTeamId = nextActiveTeam,
            Turn = GetTeamTurn(consumedTurnMatch, nextActiveTeam),
            Activations = [],
            PendingReroll = null,
            PendingPush = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            Log = message is null
                ? consumedTurnMatch.Log
                : [.. consumedTurnMatch.Log, new MatchLogEntry { Message = message }]
        };
    }

    private MatchState AdvanceHalf(MatchState match, Ruleset ruleset)
    {
        if (match.Half == 1)
        {
            return BeginDriveEnd(match, ruleset, new PendingDriveEndContinuation
            {
                NextDefenseTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId,
                StartSecondHalf = true
            });
        }

        return BeginDriveEnd(match, ruleset, new PendingDriveEndContinuation
        {
            NextDefenseTeamId = match.ActiveTeamId,
            CompleteMatch = true
        });
    }

    private MatchState StartSecondHalfSetup(MatchState match)
    {
        return BeginDriveEnd(match, new Ruleset
        {
            Id = match.RulesetId,
            Name = match.RulesetId,
            Version = "",
            Dice = new DiceRules()
        }, new PendingDriveEndContinuation
        {
            NextDefenseTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId,
            StartSecondHalf = true
        });
    }

    private MatchState StartSecondHalfSetupAfterDriveEnd(MatchState match)
    {
        var kickingTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId;
        var recoveredMatch = ResolveKnockoutRecoveries(match);
        var resetPlacements = ResetAvailablePlayersToReserve(recoveredMatch);

        return recoveredMatch with
        {
            Half = 2,
            Drive = recoveredMatch.Drive + 1,
            DriveState = DriveState.Setup,
            Turn = 1,
            HomeTurn = 1,
            AwayTurn = 1,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = kickingTeamId,
            Ball = new BallState(),
            HomeRerollsRemaining = recoveredMatch.HomeTeamRerolls,
            AwayRerollsRemaining = recoveredMatch.AwayTeamRerolls,
            HomeLeaderRerollAvailable = recoveredMatch.HomeLeaderRerollAvailable,
            AwayLeaderRerollAvailable = recoveredMatch.AwayLeaderRerollAvailable,
            TeamRerollUses = recoveredMatch.TeamRerollUses
                .Where(use => use.Half != 2)
                .ToArray(),
            Placements = resetPlacements,
            Activations = [],
            PendingBlock = null,
            PendingPush = null,
            PendingInterception = null,
            PendingReroll = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log = [.. recoveredMatch.Log, new MatchLogEntry { Message = "Second half begins. First-half receiving team kicks off." }]
        };
    }

    private static MatchState IncrementTeamTurn(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeTurn = match.HomeTurn + 1 }
            : match with { AwayTurn = match.AwayTurn + 1 };
    }

    private static bool BothTeamsFinishedHalf(MatchState match, Ruleset ruleset)
    {
        return match.HomeTurn > ruleset.TurnsPerHalf && match.AwayTurn > ruleset.TurnsPerHalf;
    }

    private static int GetTeamTurn(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeTurn : match.AwayTurn;
    }

    private static string FormatTeamTurn(MatchState match, Guid teamId)
    {
        var side = teamId == match.HomeTeamId ? "home" : "away";
        return $"{side} turn {GetTeamTurn(match, teamId)}";
    }

    private MatchState ScoreTouchdown(MatchState match, Ruleset ruleset, LeagueTeam scoringTeam)
    {
        var scorerId = match.Ball.CarrierPlayerId;
        var isHomeScore = scoringTeam.Id == match.HomeTeamId;
        var nextHomeScore = match.HomeScore + (isHomeScore ? 1 : 0);
        var nextAwayScore = match.AwayScore + (isHomeScore ? 0 : 1);
        var recoveredMatch = RecoverStunnedPlayers(match, scoringTeam.Id);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, scoringTeam.Id);
        if (BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            consumedTurnMatch = AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var scorer = scorerId.HasValue ? FindTeamPlayer(scoringTeam, scorerId.Value) : null;
        if (consumedTurnMatch.Phase is MatchPhase.DefenseSetup or MatchPhase.Complete)
        {
            return consumedTurnMatch with
            {
                HomeScore = nextHomeScore,
                AwayScore = nextAwayScore,
                PlayerAwards = scorerId.HasValue
                    ? AddPlayerAward(consumedTurnMatch, scoringTeam.Id, scorerId.Value, MatchPlayerAwardKind.Touchdown, 3, teamName: scoringTeam.Name, playerName: scorer?.Name)
                    : consumedTurnMatch.PlayerAwards,
                Log =
                [
                    .. consumedTurnMatch.Log,
                    new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." }
                ]
            };
        }

        return BeginDriveEnd(consumedTurnMatch with
        {
            HomeScore = nextHomeScore,
            AwayScore = nextAwayScore,
            PlayerAwards = scorerId.HasValue
                ? AddPlayerAward(consumedTurnMatch, scoringTeam.Id, scorerId.Value, MatchPlayerAwardKind.Touchdown, 3, teamName: scoringTeam.Name, playerName: scorer?.Name)
                : consumedTurnMatch.PlayerAwards,
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." }
            ]
        }, ruleset, new PendingDriveEndContinuation { NextDefenseTeamId = scoringTeam.Id });
    }

    private MatchState StartNextDriveSetup(MatchState match, Guid nextDefenseTeamId)
    {
        var knockoutRecoveredMatch = ResolveKnockoutRecoveries(match);
        var resetPlacements = ResetAvailablePlayersToReserve(knockoutRecoveredMatch);

        return knockoutRecoveredMatch with
        {
            Drive = knockoutRecoveredMatch.Drive + 1,
            DriveState = DriveState.Setup,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = nextDefenseTeamId,
            Turn = GetTeamTurn(knockoutRecoveredMatch, nextDefenseTeamId),
            Ball = new BallState(),
            Placements = resetPlacements,
            Activations = [],
            PendingPush = null,
            PendingReroll = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log =
            [
                .. knockoutRecoveredMatch.Log,
                new MatchLogEntry { Message = "New drive begins with defense placement." }
            ]
        };
    }

    private static PlayerPlacement[] ResetAvailablePlayersToReserve(MatchState match)
    {
        return match.Placements
            .Select(placement => placement.State is PlayerPitchState.Casualty or PlayerPitchState.Dead or PlayerPitchState.SentOff or PlayerPitchState.KnockedOut
                ? placement
                : placement with { Square = null, State = PlayerPitchState.Reserve, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null })
            .ToArray();
    }

    private static void ValidateSetupComplete(MatchState match, Ruleset ruleset, Guid teamId)
    {
        var availableCount = match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.State is PlayerPitchState.Reserve or PlayerPitchState.Standing);
        var requiredPlayers = Math.Min(ruleset.PlayersPerSide, availableCount);
        var placed = match.Placements
            .Where(placement =>
                placement.TeamId == teamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is not null)
            .ToArray();

        if (placed.Length != requiredPlayers)
        {
            throw new InvalidOperationException($"{requiredPlayers} available players must be set up before kickoff.");
        }

        if (placed.Length > ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"No more than {ruleset.PlayersPerSide} players may be set up.");
        }

        if (placed.Any(placement => placement.Square is PitchSquare square && !IsLegalSetupSide(match, ruleset, teamId, square)))
        {
            throw new InvalidOperationException("All players must be set up on their team's side of the pitch.");
        }

        var linePlayers = placed.Count(placement =>
            placement.Square is PitchSquare square &&
            IsLineOfScrimmage(ruleset, teamId, match.HomeTeamId, square) &&
            !IsWideZone(ruleset, square));
        if (linePlayers < 3)
        {
            throw new InvalidOperationException("At least three players must be set up on the line of scrimmage outside the wide zones.");
        }

        var wideZoneViolation = placed
            .Where(placement => placement.Square is PitchSquare square && IsWideZone(ruleset, square))
            .GroupBy(placement => placement.Square!.Y < 4 ? "top" : "bottom")
            .Any(group => group.Count() > 2);
        if (wideZoneViolation)
        {
            throw new InvalidOperationException("A team can set up no more than two players in each wide zone.");
        }
    }

    private MatchState ResolveKnockoutRecoveries(MatchState match)
    {
        var placements = new List<PlayerPlacement>(match.Placements.Count);
        var log = new List<MatchLogEntry>();

        foreach (var placement in match.Placements)
        {
            if (placement.State != PlayerPitchState.KnockedOut)
            {
                placements.Add(placement);
                continue;
            }

            var roll = _dice.RollD6();
            var target = HasActivePickMeUp(match, placement.TeamId) ? 3 : 4;
            if (roll >= target)
            {
                placements.Add(placement with
                {
                    Square = null,
                    State = PlayerPitchState.Reserve,
                    StunnedRecoveryHalf = null,
                    StunnedRecoveryTurn = null,
                    Casualty = null
                });
                log.Add(new MatchLogEntry { Message = $"Knockout recovery {placement.PlayerId}: rolled {roll} vs {target}+, recovered." });
                continue;
            }

            placements.Add(placement with { Square = null });
            log.Add(new MatchLogEntry { Message = $"Knockout recovery {placement.PlayerId}: rolled {roll} vs {target}+, remains knocked out." });
        }

        return log.Count == 0
            ? match
            : match with
            {
                Placements = placements,
                Log = [.. match.Log, .. log]
            };
    }

    private static bool HasActivePickMeUp(MatchState match, Guid teamId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == teamId &&
            match.PickMeUpPlayerIds.Contains(placement.PlayerId) &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve);
    }

    private static MatchState RecoverStunnedPlayers(MatchState match, Guid teamId)
    {
        var stunnedCount = match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.State == PlayerPitchState.Stunned &&
            placement.StunnedRecoveryHalf == match.Half &&
            placement.StunnedRecoveryTurn <= GetTeamTurn(match, teamId));

        if (stunnedCount == 0)
        {
            return match;
        }

        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.TeamId == teamId && placement.State == PlayerPitchState.Stunned
                    && placement.StunnedRecoveryHalf == match.Half
                    && placement.StunnedRecoveryTurn <= GetTeamTurn(match, teamId)
                        ? placement with { State = PlayerPitchState.Prone, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{stunnedCount} stunned player{(stunnedCount == 1 ? "" : "s")} recovered to prone." }
            ]
        };
    }

    private static bool IsTouchdown(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare square)
    {
        if (match.Ball.CarrierPlayerId != playerId)
        {
            return false;
        }

        return team.Id == match.HomeTeamId
            ? square.X == ruleset.PitchWidth - 1
            : square.X == 0;
    }

    private InjuryResolution ResolveFallInjury(Player player, bool armBarApplies = false)
    {
        var armorRoll = Roll2D6();
        if (armorRoll <= player.Stats.Armor)
        {
            if (!armBarApplies || armorRoll + 1 <= player.Stats.Armor)
            {
                return new InjuryResolution(PlayerPitchState.Prone);
            }

            return ResolveInjury(Roll2D6());
        }

        var injuryRoll = Roll2D6();
        return ResolveInjury(armBarApplies ? injuryRoll + 1 : injuryRoll);
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
                return new InjuryResolution(PlayerPitchState.Prone);
            }

            return ResolveInjury(ruleset, defender, Roll2D6());
        }

        var injuryRoll = Roll2D6();
        return ResolveInjury(ruleset, defender, hasMightyBlowInjury ? injuryRoll + 1 : injuryRoll);
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

    private static PitchSquare[] SafePairOfHandsSquares(MatchState match, Ruleset ruleset, Player player, PitchSquare source)
    {
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.BallScatter, GameEventStage.BeforeEvent, SkillEffect.SafePairOfHands))
        {
            return [];
        }

        return AdjacentSquares(source)
            .Where(candidate => IsOnPitch(ruleset, candidate))
            .Where(candidate =>
                FindPushOccupant(match, candidate, player.Id) is null &&
                match.Ball.Square != candidate)
            .ToArray();
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

    private int Roll2D6()
    {
        return _dice.RollD6() + _dice.RollD6();
    }

    private int RollD3()
    {
        return ((_dice.RollD6() - 1) / 2) + 1;
    }

    private int RollIndex(int count)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException("Cannot roll an index for an empty set.");
        }

        return (_dice.RollD6() - 1) % count;
    }

    private DiceRoll2D6 Roll2D6Detailed()
    {
        var first = _dice.RollD6();
        var second = _dice.RollD6();
        return new DiceRoll2D6(first, second);
    }

    private int RollD16()
    {
        return _dice.RollD16();
    }

    private PitchSquare ScatterFrom(Ruleset ruleset, PitchSquare square)
    {
        return ScatterFrom(ruleset, square, distance: 1);
    }

    private PitchSquare ScatterFrom(Ruleset ruleset, PitchSquare square, int distance)
    {
        var direction = _dice.RollD8();
        var next = square;
        for (var step = 0; step < distance; step++)
        {
            next = ScatterDirection(next, direction);
        }

        return next;
    }

    private static PitchSquare ScatterDirection(PitchSquare square, int direction)
    {
        var (dx, dy) = direction switch
        {
            1 => (-1, -1),
            2 => (0, -1),
            3 => (1, -1),
            4 => (-1, 0),
            5 => (1, 0),
            6 => (-1, 1),
            7 => (0, 1),
            _ => (1, 1)
        };

        return new PitchSquare(square.X + dx, square.Y + dy);
    }

    private static PlayerTurnActivation? GetActivation(MatchState match, Guid playerId, Guid teamId)
    {
        return match.Activations.FirstOrDefault(activation =>
            activation.PlayerId == playerId &&
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn);
    }

    private static PlayerPlacement? FindPlacement(MatchState match, Guid playerId)
    {
        return match.Placements.FirstOrDefault(placement => placement.PlayerId == playerId);
    }


    private static bool HasUsedBlitz(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Blitz);
    }

    private static bool HasUsedHandOff(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.HandOff);
    }

    private static bool HasUsedPass(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Pass);
    }

    private static bool HasUsedFoul(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Foul);
    }

    private static Player FindTeamPlayer(LeagueTeam team, Guid playerId)
    {
        return team.Players.FirstOrDefault(player => player.Id == playerId)
            ?? throw new InvalidOperationException($"Team '{team.Name}' does not contain player '{playerId}'.");
    }

    private static PlayerPlacement FindStandingPlacement(MatchState match, Guid playerId, Guid teamId, string role)
    {
        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException($"The {role} is not part of this match.");

        if (placement.TeamId != teamId)
        {
            throw new InvalidOperationException($"The {role} is not assigned to the active team.");
        }

        if (placement.Square is null || placement.State is not PlayerPitchState.Standing)
        {
            throw new InvalidOperationException($"The {role} must be standing on the pitch.");
        }

        return placement;
    }

    private static int CatchTarget(Ruleset ruleset, Player player, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        var extraArmsModifier = PlayerHasHookedEffect(ruleset, player, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        return Math.Clamp(player.Stats.Agility + weatherModifier + opposingTackleZones + disturbingPresence + extraArmsModifier, 2, 6);
    }

    private CatchAttempt RollCatch(Ruleset ruleset, Player player, int target)
    {
        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.CatchRoll, GameEventStage.BeforeRoll, "no-hands"))
        {
            return new CatchAttempt(0, null, false);
        }

        var roll = _dice.RollD6();
        if (RollSucceeds(roll, target, ruleset.Dice))
        {
            return new CatchAttempt(roll, null, true);
        }

        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.CatchRoll, GameEventStage.AfterRoll, SkillEffect.CatchReroll) &&
            !PlayerHasHookedEffect(ruleset, player, GameEventKind.CatchRoll, GameEventStage.AfterRoll, SkillEffect.MonstrousMouth))
        {
            return new CatchAttempt(roll, null, false);
        }

        var reroll = _dice.RollD6();
        return new CatchAttempt(roll, reroll, RollSucceeds(reroll, target, ruleset.Dice));
    }

    private static string FormatCatchAttempt(CatchAttempt attempt, int target)
    {
        if (attempt.Roll == 0)
        {
            return "No Hands prevents the catch";
        }

        return attempt.Reroll is int reroll
            ? $"catch roll {attempt.Roll} rerolled with Catch to {reroll} vs {target}+"
            : $"catch roll {attempt.Roll} vs {target}+";
    }

    private PassAttempt RollPass(Ruleset ruleset, Player player, int target, bool usePassSkillReroll)
    {
        var roll = _dice.RollD6();
        int? reroll = null;
        var finalRoll = roll;
        if (usePassSkillReroll &&
            !RollSucceeds(roll, target, ruleset.Dice) &&
            PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.AfterRoll, SkillEffect.PassReroll))
        {
            reroll = _dice.RollD6();
            finalRoll = reroll.Value;
        }

        var fumbled = finalRoll == 1;
        var safePassPreventedFumble = fumbled && PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.AfterRoll, SkillEffect.SafePass);

        return new PassAttempt(
            roll,
            reroll,
            finalRoll,
            RollSucceeds(finalRoll, target, ruleset.Dice),
            fumbled,
            safePassPreventedFumble);
    }

    private static string FormatPassAttempt(PassAttempt attempt, int target)
    {
        var text = attempt.Reroll is int reroll
            ? $"pass roll {attempt.Roll} rerolled with Pass to {reroll} vs {target}+"
            : $"pass roll {attempt.Roll} vs {target}+";

        return attempt.SafePassPreventedFumble
            ? $"{text}; Safe Pass"
            : text;
    }

    private static int DodgeTarget(Ruleset ruleset, Player player, int opposingTackleZones, int skillBonus = 0)
    {
        var twoHeadsBonus = PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.TwoHeads) ? 1 : 0;
        var titchyBonus = PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.Titchy) ? 1 : 0;
        var effectiveTackleZones = PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.Stunty) ? 0 : opposingTackleZones;
        return Math.Clamp(player.Stats.Agility - 1 + effectiveTackleZones - skillBonus - twoHeadsBonus - titchyBonus, 2, 6);
    }

    private static int PickupTarget(Ruleset ruleset, Player player, int opposingTackleZones, WeatherCondition weather)
    {
        var hasBigHand = PlayerHasHookedEffect(ruleset, player, GameEventKind.PickupRoll, GameEventStage.ModifyTarget, SkillEffect.BigHand);
        var markedModifier = hasBigHand ? 0 : opposingTackleZones;
        var weatherModifier = weather == WeatherCondition.PouringRain && !hasBigHand ? 1 : 0;
        var extraArmsModifier = PlayerHasHookedEffect(ruleset, player, GameEventKind.PickupRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        return Math.Clamp(player.Stats.Agility - 1 + markedModifier + weatherModifier + extraArmsModifier, 2, 6);
    }

    private static int LandingTarget(Ruleset ruleset, Player player, int opposingTackleZones)
    {
        return Math.Clamp(player.Stats.Agility + opposingTackleZones, 2, 6);
    }

    private static int CountOpposingTackleZones(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Count(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            HasActiveTackleZone(placement) &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square));
    }

    private static int CountOpposingTackleZones(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, Guid teamId, Guid playerId, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return CountOpposingTackleZones(match, teamId, playerId, square);
        }

        return match.Placements.Count(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            HasActiveTackleZone(placement) &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square) &&
            !PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.ModifyTarget, SkillEffect.Titchy));
    }

    private static int InterceptionTarget(Ruleset ruleset, Player player, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        var extraArmsModifier = PlayerHasHookedEffect(ruleset, player, GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        var veryLongLegsModifier = PlayerHasHookedEffect(ruleset, player, GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.VeryLongLegs) ? -2 : 0;
        return Math.Clamp(player.Stats.Agility + 2 + weatherModifier + opposingTackleZones + disturbingPresence + extraArmsModifier + veryLongLegsModifier, 2, 6);
    }

    private PlayerPlacement? FindDivingCatchReceiver(MatchState match, Ruleset ruleset, LeagueTeam team, PitchSquare targetSquare)
    {
        if (match.Placements.Any(placement => placement.Square == targetSquare && OccupiesPitch(placement.State)))
        {
            return null;
        }

        return match.Placements
            .Where(placement =>
                placement.TeamId == team.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                IsAdjacent(square, targetSquare))
            .Select(placement => new
            {
                Placement = placement,
                Player = FindTeamPlayer(team, placement.PlayerId)
            })
            .FirstOrDefault(candidate => PlayerHasHookedEffect(ruleset, candidate.Player, GameEventKind.CatchRoll, GameEventStage.BeforeEvent, SkillEffect.DivingCatch))
            ?.Placement;
    }

    private static PlayerPlacement? FindDivingTackler(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        PitchSquare currentSquare,
        PitchSquare nextSquare)
    {
        if (opposingTeam is null)
        {
            return null;
        }

        return match.Placements.FirstOrDefault(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, currentSquare) &&
            !IsAdjacent(square, nextSquare) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.DodgeRoll, GameEventStage.AfterRoll, SkillEffect.DivingTackle));
    }

    private static MatchState ApplyDivingTackle(MatchState match, PlayerPlacement tackler, PitchSquare dodgerSquare, string tacklerName)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == tackler.PlayerId
                    ? placement with { State = PlayerPitchState.Prone }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{tacklerName} uses Diving Tackle against a dodge from {dodgerSquare.X},{dodgerSquare.Y}." }
            ]
        };
    }

    private static int PassingTarget(Ruleset ruleset, Player player, PassRange passRange, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather is WeatherCondition.VerySunny or WeatherCondition.Blizzard ? 1 : 0;
        var skillModifier = 0;
        if (PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.Accurate) && passRange.Name is "quick" or "short")
        {
            skillModifier--;
        }
        else if (PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.Cannoneer) && IsLongPass(passRange.Name))
        {
            skillModifier--;
        }

        return Math.Clamp(player.Stats.Passing + passRange.TargetModifier + weatherModifier + opposingTackleZones + disturbingPresence + skillModifier, 2, 6);
    }

    private static string PassTargetName(Player? receiver, PitchSquare targetSquare)
    {
        return receiver is null ? $"{targetSquare.X},{targetSquare.Y}" : receiver.Name;
    }

    private static bool IsLongPass(string passRangeName)
    {
        return passRangeName is "long" or "long bomb";
    }

    private static bool CrossesMidline(Ruleset ruleset, PitchSquare start, PitchSquare destination)
    {
        var midline = ruleset.PitchWidth / 2;
        return start.X < midline && destination.X >= midline ||
            start.X >= midline && destination.X < midline;
    }

    private static int GoForItTarget(WeatherCondition weather)
    {
        return weather == WeatherCondition.Blizzard ? 3 : 2;
    }

    private static WeatherCondition ResolveWeather(int roll)
    {
        return roll switch
        {
            <= 2 => WeatherCondition.SwelteringHeat,
            3 => WeatherCondition.VerySunny,
            <= 10 => WeatherCondition.Nice,
            11 => WeatherCondition.PouringRain,
            _ => WeatherCondition.Blizzard
        };
    }

    private static string FormatWeather(WeatherCondition weather)
    {
        return weather switch
        {
            WeatherCondition.SwelteringHeat => "sweltering heat",
            WeatherCondition.VerySunny => "very sunny",
            WeatherCondition.Nice => "nice",
            WeatherCondition.PouringRain => "pouring rain",
            WeatherCondition.Blizzard => "blizzard",
            _ => weather.ToString().ToLowerInvariant()
        };
    }

    private static PassRange ResolvePassRange(PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        var distance = Math.Max(
            Math.Abs(passerSquare.X - receiverSquare.X),
            Math.Abs(passerSquare.Y - receiverSquare.Y));

        return distance switch
        {
            <= 3 => new PassRange("quick", 0),
            <= 6 => new PassRange("short", 1),
            <= 9 => new PassRange("long", 2),
            <= 13 => new PassRange("long bomb", 3),
            _ => throw new InvalidOperationException("The receiver is out of passing range.")
        };
    }

    private static PlayerPlacement[] FindEligibleInterceptors(
        MatchState match,
        Guid defendingTeamId,
        PitchSquare passerSquare,
        PitchSquare receiverSquare)
    {
        return match.Placements
            .Where(placement =>
                placement.TeamId == defendingTeamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                square != passerSquare &&
                square != receiverSquare &&
                IsOnPassingLane(square, passerSquare, receiverSquare))
            .OrderBy(placement => placement.Square!.X)
            .ThenBy(placement => placement.Square!.Y)
            .ToArray();
    }

    private static bool IsOnPassingLane(PitchSquare square, PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        var lineX = receiverSquare.X - passerSquare.X;
        var lineY = receiverSquare.Y - passerSquare.Y;
        var squareX = square.X - passerSquare.X;
        var squareY = square.Y - passerSquare.Y;
        var lengthSquared = (lineX * lineX) + (lineY * lineY);

        if (lengthSquared == 0)
        {
            return false;
        }

        var projection = ((squareX * lineX) + (squareY * lineY)) / (double)lengthSquared;
        if (projection <= 0 || projection >= 1)
        {
            return false;
        }

        var closestX = passerSquare.X + (projection * lineX);
        var closestY = passerSquare.Y + (projection * lineY);
        var distanceX = square.X - closestX;
        var distanceY = square.Y - closestY;
        var distance = Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));

        return distance <= 0.5;
    }

    private static bool RollSucceeds(int roll, int target, DiceRules diceRules)
    {
        if (diceRules.NaturalOneAlwaysFails && roll == 1)
        {
            return false;
        }

        if (diceRules.NaturalSixAlwaysSucceeds && roll == 6)
        {
            return true;
        }

        return roll >= target;
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

    private static bool IsReceivingSide(Ruleset ruleset, Guid receivingTeamId, Guid homeTeamId, PitchSquare square)
    {
        return receivingTeamId == homeTeamId
            ? square.X < ruleset.PitchWidth / 2
            : square.X >= ruleset.PitchWidth / 2;
    }

    private static Player? FindTouchbackReceiver(MatchState match, LeagueTeam receivingTeam)
    {
        return receivingTeam.Players.FirstOrDefault(player =>
            match.Placements.Any(placement =>
                placement.PlayerId == player.Id &&
                placement.TeamId == receivingTeam.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is not null));
    }

    private static bool IsOnPitch(Ruleset ruleset, PitchSquare square)
    {
        return square.X >= 0 && square.X < ruleset.PitchWidth && square.Y >= 0 && square.Y < ruleset.PitchHeight;
    }

    private static bool IsLegalSetupSide(MatchState match, Ruleset ruleset, Guid teamId, PitchSquare square)
    {
        return teamId == match.HomeTeamId
            ? square.X < ruleset.PitchWidth / 2
            : square.X >= ruleset.PitchWidth / 2;
    }

    private static bool IsLineOfScrimmage(Ruleset ruleset, Guid teamId, Guid homeTeamId, PitchSquare square)
    {
        return teamId == homeTeamId
            ? square.X == (ruleset.PitchWidth / 2) - 1
            : square.X == ruleset.PitchWidth / 2;
    }

    private static bool IsWideZone(Ruleset ruleset, PitchSquare square)
    {
        return square.Y < 4 || square.Y >= ruleset.PitchHeight - 4;
    }

    private static int CountTeamPlayersInWideZone(MatchState match, Ruleset ruleset, Guid teamId, PitchSquare square, Guid ignoredPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.PlayerId != ignoredPlayerId &&
            placement.TeamId == teamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare placedSquare &&
            IsSameWideZone(ruleset, square, placedSquare));
    }

    private static int CountTeamPlayersOnPitch(MatchState match, Guid teamId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is not null);
    }

    private static bool IsSameWideZone(Ruleset ruleset, PitchSquare first, PitchSquare second)
    {
        return (first.Y < 4 && second.Y < 4) ||
            (first.Y >= ruleset.PitchHeight - 4 && second.Y >= ruleset.PitchHeight - 4);
    }

    private static Guid GetOpponentTeamId(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.AwayTeamId : match.HomeTeamId;
    }

    private static string FormatPitchState(PlayerPitchState state)
    {
        return state.ToString().ToLowerInvariant();
    }

    private static bool IsAdjacent(PitchSquare first, PitchSquare second)
    {
        return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)) == 1;
    }

    private static IEnumerable<PitchSquare> AdjacentSquares(PitchSquare square)
    {
        for (var y = square.Y - 1; y <= square.Y + 1; y++)
        {
            for (var x = square.X - 1; x <= square.X + 1; x++)
            {
                if (x == square.X && y == square.Y)
                {
                    continue;
                }

                yield return new PitchSquare(x, y);
            }
        }
    }

    private static string FormatKickoffEventKind(KickoffEventKind kind)
    {
        return kind switch
        {
            KickoffEventKind.SolidDefence => "Solid Defence",
            KickoffEventKind.HighKick => "High Kick",
            KickoffEventKind.QuickSnap => "Quick Snap",
            KickoffEventKind.Blitz => "Blitz",
            _ => kind.ToString()
        };
    }

    private static IReadOnlyList<PlayerPlacement> CreateInitialPlacements(LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return
        [
            .. homeTeam.Players.Select(player => new PlayerPlacement
            {
                PlayerId = player.Id,
                TeamId = homeTeam.Id,
                State = PlayerPitchState.Reserve
            }),
            .. awayTeam.Players.Select(player => new PlayerPlacement
            {
                PlayerId = player.Id,
                TeamId = awayTeam.Id,
                State = PlayerPitchState.Reserve
            })
        ];
    }
}

public sealed record BlockStrength(int AttackerStrength, int DefenderStrength, int Dice);

public sealed record PassRange(string Name, int TargetModifier);

public sealed record DiceRoll2D6(int First, int Second)
{
    public int Total => First + Second;
    public bool IsDoubles => First == Second;
}

public sealed record BallLanding(PitchSquare Square, IReadOnlyList<MatchLogEntry> Log);

sealed record InjuryResolution(PlayerPitchState State, CasualtyRoll? Casualty = null);

sealed record CatchAttempt(int Roll, int? Reroll, bool Success);

sealed record PassAttempt(int Roll, int? Reroll, int FinalRoll, bool Success, bool Fumbled, bool SafePassPreventedFumble);

sealed record TentaclesResolution(MatchState Match, bool Held);

sealed record FoulAppearanceResolution(MatchState Match, bool BlockPrevented);

sealed record ApothecaryResolution(MatchState Match, InjuryResolution Injury, IReadOnlyList<MatchLogEntry> Log);

sealed record KickoffEventResult(MatchState Match, string Name, string Message, KickoffEventKind? PendingKind = null, bool ExtraScatter = false);

sealed record ActionStart(MatchState Match, bool Prevented);

sealed record SpecialActionContext(
    MatchState Match,
    Player Actor,
    PlayerPlacement ActorPlacement,
    Player Target,
    PlayerPlacement TargetPlacement,
    bool Prevented);

sealed record TeamMateLaunchContext(
    Player Actor,
    PlayerPlacement ActorPlacement,
    Player Launched,
    PlayerPlacement LaunchedPlacement);

public interface IDiceRoller
{
    int RollD6();
    int RollD8();
    int RollD16();
}

public sealed class RandomDiceRoller : IDiceRoller
{
    private readonly Random _random = new();

    public int RollD6()
    {
        return _random.Next(1, 7);
    }

    public int RollD8()
    {
        return _random.Next(1, 9);
    }

    public int RollD16()
    {
        return _random.Next(1, 17);
    }
}
