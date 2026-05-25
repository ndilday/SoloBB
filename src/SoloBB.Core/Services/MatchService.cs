using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class MatchService
{
    private const int MaxGoForItsPerActivation = 3;
    private readonly IDiceRoller _dice;

    public MatchService(IDiceRoller? dice = null)
    {
        _dice = dice ?? new RandomDiceRoller();
    }

    public MatchState CreateHotseatMatch(Ruleset ruleset, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        if (homeTeam.Id == awayTeam.Id)
        {
            throw new InvalidOperationException("Home and away teams must be different teams.");
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
            Placements = CreateInitialPlacements(homeTeam, awayTeam),
            Log =
            [
                new MatchLogEntry { Message = $"Created hotseat match: {homeTeam.Name} vs {awayTeam.Name}. Defense sets up first." }
            ]
        };
    }

    public MatchState AdvancePhase(MatchState match)
    {
        var nextPhase = match.Phase;
        var nextActiveTeam = match.ActiveTeamId;
        var message = "";

        switch (match.Phase)
        {
            case MatchPhase.DefenseSetup:
                nextPhase = MatchPhase.OffenseSetup;
                nextActiveTeam = GetOpponentTeamId(match, match.ActiveTeamId);
                message = "Defense setup complete. Offense sets up next.";
                break;
            case MatchPhase.OffenseSetup:
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
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    public MatchState AdvanceTurn(MatchState match, Ruleset ruleset)
    {
        if (match.Phase is MatchPhase.Complete)
        {
            return match;
        }

        if (match.Phase is not MatchPhase.DefensiveTurn)
        {
            return AdvancePhase(match);
        }

        var consumedTurnMatch = IncrementTeamTurn(match, match.ActiveTeamId);
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
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Advanced to half {consumedTurnMatch.Half}, {FormatTeamTurn(consumedTurnMatch, nextActiveTeam)}, phase {MatchPhase.OffensivePlayerTurn}." }
            ]
        };

        return nextMatch;
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
                    ? current with { Square = square, State = PlayerPitchState.Standing }
                    : current)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"Placed player {playerId} at {square.X},{square.Y}." }
            ]
        };
    }

    public MatchState MovePlayer(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare destination)
    {
        return MovePlayerCore(match, ruleset, team, playerId, destination, PlayerTurnAction.Move);
    }

    public MatchState HandOffBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid carrierPlayerId,
        Guid receiverPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only hand off during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can hand off during its turn.");
        }

        if (match.Ball.CarrierPlayerId != carrierPlayerId)
        {
            throw new InvalidOperationException("The selected player is not carrying the ball.");
        }

        if (carrierPlayerId == receiverPlayerId)
        {
            throw new InvalidOperationException("A player cannot hand off to themselves.");
        }

        if (HasUsedHandOff(match, team.Id))
        {
            throw new InvalidOperationException($"{team.Name} has already used its handoff this turn.");
        }

        var carrier = FindTeamPlayer(team, carrierPlayerId);
        var receiver = FindTeamPlayer(team, receiverPlayerId);
        var carrierPlacement = FindStandingPlacement(match, carrierPlayerId, team.Id, "carrier");
        var receiverPlacement = FindStandingPlacement(match, receiverPlayerId, team.Id, "receiver");

        if (!IsAdjacent(carrierPlacement.Square!, receiverPlacement.Square!))
        {
            throw new InvalidOperationException("Hand offs require adjacent players.");
        }

        var activatedMatch = AddActivation(match, carrierPlayerId, team.Id, PlayerTurnAction.HandOff, goForItsUsed: 0);
        var catchRoll = _dice.RollD6();
        var target = CatchTarget(receiver);

        if (catchRoll >= target)
        {
            return activatedMatch with
            {
                Ball = new BallState { CarrierPlayerId = receiverPlayerId },
                Log =
                [
                    .. activatedMatch.Log,
                    new MatchLogEntry { Message = $"{carrier.Name} hands off to {receiver.Name}: catch roll {catchRoll}+ vs {target}+, success." }
                ]
            };
        }

        var scatterSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var bouncedMatch = BounceBall(activatedMatch, ruleset, team, scatterSquare);
        var failedMatch = bouncedMatch with
        {
            Log =
            [
                .. bouncedMatch.Log,
                new MatchLogEntry { Message = $"{carrier.Name} hands off to {receiver.Name}: catch roll {catchRoll} vs {target}+, failed." },
                new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." }
            ]
        };

        return failedMatch.Ball.CarrierPlayerId is Guid carrierId && FindPlacement(failedMatch, carrierId)?.TeamId == team.Id
            ? failedMatch
            : ApplyTurnover(failedMatch, ruleset, team.Id);
    }

    public MatchState PassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        Guid receiverPlayerId,
        LeagueTeam? defendingTeam = null)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only pass during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can pass during its turn.");
        }

        if (match.Ball.CarrierPlayerId != passerPlayerId)
        {
            throw new InvalidOperationException("The selected player is not carrying the ball.");
        }

        if (passerPlayerId == receiverPlayerId)
        {
            throw new InvalidOperationException("A player cannot pass to themselves.");
        }

        if (GetActivation(match, passerPlayerId, team.Id) is not null)
        {
            var passer = FindTeamPlayer(team, passerPlayerId);
            throw new InvalidOperationException($"{passer.Name} has already been activated this turn.");
        }

        if (HasUsedPass(match, team.Id))
        {
            throw new InvalidOperationException($"{team.Name} has already used its pass this turn.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before taking another action.");
        }

        var passerPlayer = FindTeamPlayer(team, passerPlayerId);
        var receiverPlayer = FindTeamPlayer(team, receiverPlayerId);
        var passerPlacement = FindStandingPlacement(match, passerPlayerId, team.Id, "passer");
        var receiverPlacement = FindStandingPlacement(match, receiverPlayerId, team.Id, "receiver");
        var passRange = ResolvePassRange(passerPlacement.Square!, receiverPlacement.Square!);
        var target = PassingTarget(passerPlayer, passRange);
        var passRoll = _dice.RollD6();
        var activatedMatch = AddActivation(match, passerPlayerId, team.Id, PlayerTurnAction.Pass, goForItsUsed: 0) with
        {
            Ball = new BallState()
        };

        if (RollSucceeds(passRoll, target, ruleset.Dice))
        {
            var eligibleInterceptors = defendingTeam is null
                ? Array.Empty<PlayerPlacement>()
                : FindEligibleInterceptors(match, defendingTeam.Id, passerPlacement.Square!, receiverPlacement.Square!);
            var accuratePassMatch = activatedMatch with
            {
                Log =
                [
                    .. activatedMatch.Log,
                    new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {receiverPlayer.Name}: {passRange.Name} pass roll {passRoll} vs {target}+, accurate." }
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
                        ReceiverPlayerId = receiverPlayerId,
                        EligiblePlayerIds = eligibleInterceptors.Select(placement => placement.PlayerId).ToArray(),
                        PassRoll = passRoll,
                        PassTarget = target,
                        PassRangeName = passRange.Name
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
                    receiverPlacement,
                    eligibleInterceptors[0],
                    passRange.Name,
                    passRoll,
                    target);
            }

            return ResolvePassCatch(accuratePassMatch, ruleset, team, passerPlayer, receiverPlayer, receiverPlacement, passRange.Name, passRoll, target);
        }

        var inaccurateSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var inaccurateMatch = BounceBall(activatedMatch, ruleset, team, inaccurateSquare);
        var failedMatch = inaccurateMatch with
        {
            Log =
            [
                .. inaccurateMatch.Log,
                new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {receiverPlayer.Name}: {passRange.Name} pass roll {passRoll} vs {target}+, inaccurate." },
                new MatchLogEntry { Message = $"Ball scatters to {inaccurateSquare.X},{inaccurateSquare.Y}." }
            ]
        };

        return failedMatch.Ball.CarrierPlayerId is Guid recoveredCarrierId && FindPlacement(failedMatch, recoveredCarrierId)?.TeamId == team.Id
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

        var receiver = FindTeamPlayer(passingTeam, pending.ReceiverPlayerId);
        var passer = FindTeamPlayer(passingTeam, pending.PasserPlayerId);
        var receiverPlacement = FindStandingPlacement(match, pending.ReceiverPlayerId, passingTeam.Id, "receiver");
        var interceptorPlacement = FindStandingPlacement(match, interceptorPlayerId, defendingTeam.Id, "interceptor");

        return ResolveInterceptionAttempt(
            match with { PendingInterception = null },
            ruleset,
            passingTeam,
            defendingTeam,
            passer,
            receiver,
            receiverPlacement,
            interceptorPlacement,
            pending.PassRangeName,
            pending.PassRoll,
            pending.PassTarget);
    }

    private MatchState ResolveInterceptionAttempt(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam passingTeam,
        LeagueTeam defendingTeam,
        Player passer,
        Player receiver,
        PlayerPlacement receiverPlacement,
        PlayerPlacement interceptorPlacement,
        string passRangeName,
        int passRoll,
        int passTarget)
    {
        var interceptor = FindTeamPlayer(defendingTeam, interceptorPlacement.PlayerId);
        var interceptionRoll = _dice.RollD6();
        var interceptionTarget = InterceptionTarget(interceptor);

        if (RollSucceeds(interceptionRoll, interceptionTarget, ruleset.Dice))
        {
            var interceptedMatch = match with
            {
                Ball = new BallState { CarrierPlayerId = interceptor.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{interceptor.Name} intercepts the {passRangeName} pass on {interceptionRoll} vs {interceptionTarget}+." }
                ]
            };

            return ApplyTurnover(interceptedMatch, ruleset, passingTeam.Id);
        }

        var failedInterceptionMatch = match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{interceptor.Name} attempts to intercept the {passRangeName} pass: rolled {interceptionRoll} vs {interceptionTarget}+, failed." }
            ]
        };

        return ResolvePassCatch(failedInterceptionMatch, ruleset, passingTeam, passer, receiver, receiverPlacement, passRangeName, passRoll, passTarget);
    }

    private MatchState ResolvePassCatch(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player passer,
        Player receiver,
        PlayerPlacement receiverPlacement,
        string passRangeName,
        int passRoll,
        int passTarget)
    {
        var catchRoll = _dice.RollD6();
        var catchTarget = CatchTarget(receiver);

        if (RollSucceeds(catchRoll, catchTarget, ruleset.Dice))
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, catch roll {catchRoll} vs {catchTarget}+, complete." }
                ]
            };
        }

        var scatterSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var bouncedMatch = BounceBall(match, ruleset, team, scatterSquare);
        var droppedMatch = bouncedMatch with
        {
            Log =
            [
                .. bouncedMatch.Log,
                new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, catch roll {catchRoll} vs {catchTarget}+, dropped." },
                new MatchLogEntry { Message = $"Ball bounces to {scatterSquare.X},{scatterSquare.Y}." }
            ]
        };

        return droppedMatch.Ball.CarrierPlayerId is Guid carrierId && FindPlacement(droppedMatch, carrierId)?.TeamId == team.Id
            ? droppedMatch
            : ApplyTurnover(droppedMatch, ruleset, team.Id);
    }

    public MatchState ResolveKickoff(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam, PitchSquare targetSquare)
    {
        if (match.Phase is not MatchPhase.Kickoff)
        {
            throw new InvalidOperationException("Kickoff can only be resolved during the kickoff phase.");
        }

        if (receivingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("The active team must receive the kickoff.");
        }

        var scatterSquare = ScatterFrom(ruleset, targetSquare);
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"Kickoff targeted {targetSquare.X},{targetSquare.Y} and scattered to {scatterSquare.X},{scatterSquare.Y}." }
        };

        if (!IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            var touchbackReceiver = FindTouchbackReceiver(match, receivingTeam)
                ?? throw new InvalidOperationException("Receiving team has no standing player for touchback.");

            return match with
            {
                Phase = MatchPhase.OffensivePlayerTurn,
                Ball = new BallState { CarrierPlayerId = touchbackReceiver.Id },
                Activations = [],
                Log =
                [
                    .. match.Log,
                    .. log,
                    new MatchLogEntry { Message = $"Touchback. {touchbackReceiver.Name} receives the ball." }
                ]
            };
        }

        var bouncedMatch = BounceBall(match, ruleset, receivingTeam, scatterSquare);
        return bouncedMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            Activations = [],
            Log =
            [
                .. bouncedMatch.Log,
                .. log,
                new MatchLogEntry { Message = "Kickoff resolved. Offensive player turn begins." }
            ]
        };
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
        var attackerPlacement = ValidateBlock(match, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);

        if (GetActivation(match, attackerPlayerId, attackerTeam.Id) is not null)
        {
            throw new InvalidOperationException($"{attacker.Name} has already been activated this turn.");
        }

        var activatedMatch = AddActivation(match, attackerPlayerId, attackerTeam.Id, PlayerTurnAction.Block, goForItsUsed: 0);
        return ResolveBlock(activatedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
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
            defender,
            defenderPlacement,
            strength,
            pending.Rolls,
            roll);
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
        if (GetActivation(match, attackerPlayerId, attackerTeam.Id) is not null)
        {
            throw new InvalidOperationException($"{attacker.Name} has already been activated this turn.");
        }

        if (HasUsedBlitz(match, attackerTeam.Id))
        {
            throw new InvalidOperationException($"{attackerTeam.Name} has already used its blitz this turn.");
        }

        var movedMatch = MovePlayerCore(match, ruleset, attackerTeam, attackerPlayerId, destination, PlayerTurnAction.Blitz);
        if (movedMatch.Phase != match.Phase || movedMatch.ActiveTeamId != match.ActiveTeamId)
        {
            return movedMatch;
        }

        var attackerPlacement = ValidateBlock(movedMatch, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
        return ResolveBlock(movedMatch, ruleset, attackerTeam, attacker, attackerPlacement, defenderTeam, defender);
    }

    private MatchState MovePlayerCore(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare destination,
        PlayerTurnAction action)
    {
        if (match.Phase is MatchPhase.Complete)
        {
            throw new InvalidOperationException("Players cannot move after the match is complete.");
        }

        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only move during a player turn.");
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

        if (placement.Square is null || placement.State is not PlayerPitchState.Standing)
        {
            throw new InvalidOperationException("Only standing players on the pitch can move.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && current.Square == destination))
        {
            throw new InvalidOperationException($"Square {destination.X},{destination.Y} is already occupied.");
        }

        if (GetActivation(match, playerId, team.Id) is not null)
        {
            throw new InvalidOperationException($"{player.Name} has already been activated this turn.");
        }

        var distance = Math.Max(Math.Abs(destination.X - placement.Square.X), Math.Abs(destination.Y - placement.Square.Y));
        var goForItsUsed = Math.Max(0, distance - player.Stats.Movement);
        if (goForItsUsed > MaxGoForItsPerActivation)
        {
            throw new InvalidOperationException($"{player.Name} can move {player.Stats.Movement} squares plus {MaxGoForItsPerActivation} go-for-its, not {distance}.");
        }

        var nextMatch = AddActivation(match, playerId, team.Id, action, goForItsUsed);

        for (var goForIt = 1; goForIt <= goForItsUsed; goForIt++)
        {
            var roll = _dice.RollD6();
            if (roll == 1)
            {
                return ResolveFailedGoForIt(nextMatch, ruleset, team, player, placement, destination, goForIt, roll);
            }

            nextMatch = nextMatch with
            {
                Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} go-for-it {goForIt}/{goForItsUsed}: rolled {roll}, success." }]
            };
        }

        var movedMatch = nextMatch with
        {
            Placements = nextMatch.Placements
                .Select(current => current.PlayerId == playerId
                    ? current with { Square = destination }
                    : current)
                .ToArray(),
            Activations = nextMatch.Activations,
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = $"Moved {player.Name} to {destination.X},{destination.Y}." }
            ]
        };

        return IsTouchdown(movedMatch, ruleset, team, playerId, destination)
            ? ScoreTouchdown(movedMatch, ruleset, team)
            : movedMatch;
    }

    private MatchState ResolveBlock(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        Player defender)
    {
        var defenderPlacement = match.Placements.First(placement => placement.PlayerId == defender.Id);
        var strength = ResolveBlockStrength(match, attackerTeam, attackerPlacement, defenderTeam, defenderPlacement, attacker, defender);
        var rolls = Enumerable.Range(0, strength.Dice).Select(_ => _dice.RollD6()).ToArray();
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
                    DefenderStrength = strength.DefenderStrength
                },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: ST {strength.AttackerStrength}-{strength.DefenderStrength}, rolled {string.Join(", ", rolls)}. Choose a block die." }
                ]
            };
        }

        return ResolveChosenBlockDie(match, ruleset, attackerTeam, attacker, attackerPlacement, defender, defenderPlacement, strength, rolls, rolls[0]);
    }

    private MatchState ResolveChosenBlockDie(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Player attacker,
        PlayerPlacement attackerPlacement,
        Player defender,
        PlayerPlacement defenderPlacement,
        BlockStrength strength,
        IReadOnlyList<int> rolls,
        int roll)
    {
        var rollText = string.Join(", ", rolls);
        var strengthText = $"ST {strength.AttackerStrength}-{strength.DefenderStrength}, {strength.Dice} die{(strength.Dice == 1 ? "" : "s")}";

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

        if (roll <= 3)
        {
            return match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, pushed back." }
                ]
            };
        }

        var defenderInjuryState = ResolveFallInjury(defender);
        var result = KnockPlayerDown(match, ruleset, defender, defenderPlacement, defenderInjuryState, defenderPlacement.Square!);
        return result with
        {
            Log =
            [
                .. result.Log,
                new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, defender down." }
            ]
        };
    }

    private BlockStrength ResolveBlockStrength(
        MatchState match,
        LeagueTeam attackerTeam,
        PlayerPlacement attackerPlacement,
        LeagueTeam defenderTeam,
        PlayerPlacement defenderPlacement,
        Player attacker,
        Player defender)
    {
        var attackerAssists = CountAssists(match, attackerTeam.Id, defenderPlacement.PlayerId, defenderPlacement.Square!, attackerPlacement.PlayerId);
        var defenderAssists = CountAssists(match, defenderTeam.Id, attackerPlacement.PlayerId, attackerPlacement.Square!, defenderPlacement.PlayerId);
        var attackerStrength = attacker.Stats.Strength + attackerAssists;
        var defenderStrength = defender.Stats.Strength + defenderAssists;
        var dice = ResolveBlockDice(attackerStrength, defenderStrength);

        return new BlockStrength(attackerStrength, defenderStrength, dice);
    }

    private int CountAssists(MatchState match, Guid assistingTeamId, Guid opposedPlayerId, PitchSquare targetSquare, Guid primaryPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == assistingTeamId &&
            placement.PlayerId != primaryPlayerId &&
            placement.PlayerId != opposedPlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, targetSquare) &&
            !IsMarkedByOpponent(match, assistingTeamId, placement.PlayerId, square, opposedPlayerId));
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid assistingTeamId, Guid assistingPlayerId, PitchSquare assistingSquare, Guid ignoredOpponentId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != assistingTeamId &&
            placement.PlayerId != ignoredOpponentId &&
            placement.PlayerId != assistingPlayerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, assistingSquare));
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

        var distance = Math.Max(
            Math.Abs(attackerPlacement.Square.X - defenderPlacement.Square.X),
            Math.Abs(attackerPlacement.Square.Y - defenderPlacement.Square.Y));
        if (distance != 1)
        {
            throw new InvalidOperationException("Blocks require adjacent players.");
        }

        return attackerPlacement;
    }

    private MatchState KnockPlayerDown(MatchState match, Ruleset ruleset, Player player, PlayerPlacement placement, PlayerPitchState injuryState, PitchSquare square)
    {
        var ball = match.Ball;
        var log = new List<MatchLogEntry>();
        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, square);
            ball = new BallState { Square = scatterSquare };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
        }

        return match with
        {
            Ball = ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == player.Id
                    ? current with { Square = square, State = injuryState }
                    : current)
                .ToArray(),
            Log = [.. match.Log, .. log]
        };
    }

    private MatchState BounceBall(MatchState match, Ruleset ruleset, LeagueTeam originalTeam, PitchSquare square)
    {
        var receiverPlacement = match.Placements.FirstOrDefault(placement =>
            placement.Square == square &&
            placement.State == PlayerPitchState.Standing);

        if (receiverPlacement is null)
        {
            return match with { Ball = new BallState { Square = square } };
        }

        if (receiverPlacement.TeamId != originalTeam.Id)
        {
            return match with { Ball = new BallState { Square = square } };
        }

        var receiver = FindTeamPlayer(originalTeam, receiverPlacement.PlayerId);
        var target = CatchTarget(receiver);
        var catchRoll = _dice.RollD6();

        if (catchRoll >= target)
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{receiver.Name} catches the bouncing ball on {catchRoll} vs {target}+." }
                ]
            };
        }

        var nextSquare = ScatterFrom(ruleset, square);
        var nextMatch = match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{receiver.Name} fails to catch the bouncing ball on {catchRoll} vs {target}+." }
            ]
        };

        return BounceBall(nextMatch, ruleset, originalTeam, nextSquare);
    }

    private MatchState AddActivation(MatchState match, Guid playerId, Guid teamId, PlayerTurnAction action, int goForItsUsed)
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
                    Action = action
                }
            ]
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
        var injuryState = ResolveFallInjury(player);
        var ball = match.Ball;
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"{player.Name} go-for-it {goForItNumber}: rolled {roll}, failed." },
            new() { Message = $"{player.Name} falls at {destination.X},{destination.Y} and is {FormatPitchState(injuryState)}." }
        };

        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            ball = new BallState { Square = scatterSquare };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
        }

        var fallenMatch = match with
        {
            Ball = ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == player.Id
                    ? current with { Square = destination, State = injuryState }
                    : current)
                .ToArray(),
            Log = [.. match.Log, .. log]
        };

        return ApplyTurnover(fallenMatch, ruleset, team.Id);
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
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = "Turnover." }
            ]
        };
    }

    private MatchState EndActivePlayerTurn(MatchState match, Ruleset? ruleset, string? message)
    {
        var consumedTurnMatch = IncrementTeamTurn(match, match.ActiveTeamId);
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
            Log = message is null
                ? consumedTurnMatch.Log
                : [.. consumedTurnMatch.Log, new MatchLogEntry { Message = message }]
        };
    }

    private MatchState AdvanceHalf(MatchState match, Ruleset ruleset)
    {
        if (match.Half == 1)
        {
            return StartSecondHalfSetup(match);
        }

        return match with
        {
            Phase = MatchPhase.Complete,
            Turn = ruleset.TurnsPerHalf,
            Activations = [],
            PendingBlock = null,
            PendingInterception = null,
            Log = [.. match.Log, new MatchLogEntry { Message = "Full time. Match complete." }]
        };
    }

    private MatchState StartSecondHalfSetup(MatchState match)
    {
        var kickingTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId;
        var resetPlacements = ResetAvailablePlayersToReserve(match);

        return match with
        {
            Half = 2,
            Turn = 1,
            HomeTurn = 1,
            AwayTurn = 1,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = kickingTeamId,
            Ball = new BallState(),
            Placements = resetPlacements,
            Activations = [],
            PendingBlock = null,
            PendingInterception = null,
            Log = [.. match.Log, new MatchLogEntry { Message = "Second half begins. First-half receiving team kicks off." }]
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
        var isHomeScore = scoringTeam.Id == match.HomeTeamId;
        var nextHomeScore = match.HomeScore + (isHomeScore ? 1 : 0);
        var nextAwayScore = match.AwayScore + (isHomeScore ? 0 : 1);
        var consumedTurnMatch = IncrementTeamTurn(match, scoringTeam.Id);
        if (BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            consumedTurnMatch = AdvanceHalf(consumedTurnMatch, ruleset);
        }

        if (consumedTurnMatch.Phase is MatchPhase.DefenseSetup or MatchPhase.Complete)
        {
            return consumedTurnMatch with
            {
                HomeScore = nextHomeScore,
                AwayScore = nextAwayScore,
                Log =
                [
                    .. consumedTurnMatch.Log,
                    new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." }
                ]
            };
        }

        var resetPlacements = ResetAvailablePlayersToReserve(consumedTurnMatch);

        return consumedTurnMatch with
        {
            HomeScore = nextHomeScore,
            AwayScore = nextAwayScore,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = scoringTeam.Id,
            Turn = GetTeamTurn(consumedTurnMatch, scoringTeam.Id),
            Ball = new BallState(),
            Placements = resetPlacements,
            Activations = [],
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." },
                new MatchLogEntry { Message = "New drive begins with defense placement." }
            ]
        };
    }

    private static PlayerPlacement[] ResetAvailablePlayersToReserve(MatchState match)
    {
        return match.Placements
            .Select(placement => placement.State is PlayerPitchState.Casualty or PlayerPitchState.SentOff
                ? placement
                : placement with { Square = null, State = PlayerPitchState.Reserve })
            .ToArray();
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

    private PlayerPitchState ResolveFallInjury(Player player)
    {
        var armorRoll = Roll2D6();
        if (armorRoll <= player.Stats.Armor)
        {
            return PlayerPitchState.Prone;
        }

        var injuryRoll = Roll2D6();
        return injuryRoll switch
        {
            >= 12 => PlayerPitchState.Casualty,
            >= 10 => PlayerPitchState.KnockedOut,
            >= 8 => PlayerPitchState.Stunned,
            _ => PlayerPitchState.Prone
        };
    }

    private int Roll2D6()
    {
        return _dice.RollD6() + _dice.RollD6();
    }

    private PitchSquare ScatterFrom(Ruleset ruleset, PitchSquare square)
    {
        var direction = _dice.RollD8();
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

        return new PitchSquare(
            Math.Clamp(square.X + dx, 0, ruleset.PitchWidth - 1),
            Math.Clamp(square.Y + dy, 0, ruleset.PitchHeight - 1));
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

    private static int CatchTarget(Player player)
    {
        return Math.Clamp(player.Stats.Agility, 2, 6);
    }

    private static int InterceptionTarget(Player player)
    {
        return Math.Clamp(player.Stats.Agility + 2, 2, 6);
    }

    private static int PassingTarget(Player player, PassRange passRange)
    {
        return Math.Max(2, player.Stats.Passing + passRange.TargetModifier);
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

public interface IDiceRoller
{
    int RollD6();
    int RollD8();
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
}
