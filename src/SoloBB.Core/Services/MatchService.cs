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
            HomeRerollsRemaining = homeTeam.Rerolls,
            AwayRerollsRemaining = awayTeam.Rerolls,
            HomeTeamRerolls = homeTeam.Rerolls,
            AwayTeamRerolls = awayTeam.Rerolls,
            Placements = CreateInitialPlacements(homeTeam, awayTeam),
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
                    ? current with { Square = square, State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
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
        var target = CatchTarget(receiver, match.Weather);

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
        var bouncedMatch = ResolveBallLanding(activatedMatch, ruleset, team, scatterSquare);
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
        var receiverPlacement = FindStandingPlacement(match, receiverPlayerId, team.Id, "receiver");
        if (passerPlayerId == receiverPlayerId)
        {
            throw new InvalidOperationException("A player cannot pass to themselves.");
        }

        return PassBall(match, ruleset, team, passerPlayerId, receiverPlacement.Square!, defendingTeam);
    }

    public MatchState PassBall(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid passerPlayerId,
        PitchSquare targetSquare,
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

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (match.Ball.CarrierPlayerId != passerPlayerId)
        {
            throw new InvalidOperationException("The selected player is not carrying the ball.");
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
        var passerPlacement = FindStandingPlacement(match, passerPlayerId, team.Id, "passer");
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
        var passRange = ResolvePassRange(passerPlacement.Square!, targetSquare);
        var passerTackleZones = CountOpposingTackleZones(match, team.Id, passerPlayerId, passerPlacement.Square!);
        var target = PassingTarget(passerPlayer, passRange, match.Weather, passerTackleZones);
        var passRoll = _dice.RollD6();
        var activatedMatch = AddActivation(match, passerPlayerId, team.Id, PlayerTurnAction.Pass, goForItsUsed: 0) with
        {
            Ball = new BallState()
        };

        if (passRoll == 1)
        {
            var bounceSquare = ScatterFrom(ruleset, passerPlacement.Square!);
            var bouncedMatch = ResolveBallLanding(activatedMatch, ruleset, team, bounceSquare);
            var fumbledMatch = bouncedMatch with
            {
                Log =
                [
                    .. bouncedMatch.Log,
                    new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {targetSquare.X},{targetSquare.Y}: {passRange.Name} pass roll {passRoll} vs {target}+, fumbled." },
                    new MatchLogEntry { Message = $"Ball bounces to {bounceSquare.X},{bounceSquare.Y}." }
                ]
            };

            return fumbledMatch.Ball.CarrierPlayerId is Guid fumbleCarrierId && FindPlacement(fumbledMatch, fumbleCarrierId)?.TeamId == team.Id
                ? fumbledMatch
                : ApplyTurnover(fumbledMatch, ruleset, team.Id);
        }

        if (RollSucceeds(passRoll, target, ruleset.Dice))
        {
            var eligibleInterceptors = defendingTeam is null
                ? Array.Empty<PlayerPlacement>()
                : FindEligibleInterceptors(match, defendingTeam.Id, passerPlacement.Square!, targetSquare);
            var accuratePassMatch = activatedMatch with
            {
                Log =
                [
                    .. activatedMatch.Log,
                    new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {PassTargetName(receiverPlayer, targetSquare)}: {passRange.Name} pass roll {passRoll} vs {target}+ ({passerTackleZones} opposing tackle zones), accurate." }
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
                    targetSquare,
                    eligibleInterceptors[0],
                    passRange.Name,
                    passRoll,
                    target);
            }

            return ResolvePassLanding(accuratePassMatch, ruleset, team, passerPlayer, receiverPlayer, targetSquare, passRange.Name, passRoll, target);
        }

        var inaccurateSquare = ScatterFrom(ruleset, targetSquare);
        var inaccurateMatch = ResolveBallLanding(activatedMatch, ruleset, team, inaccurateSquare);
        var failedMatch = inaccurateMatch with
        {
            Log =
            [
                .. inaccurateMatch.Log,
                new MatchLogEntry { Message = $"{passerPlayer.Name} passes to {PassTargetName(receiverPlayer, targetSquare)}: {passRange.Name} pass roll {passRoll} vs {target}+ ({passerTackleZones} opposing tackle zones), inaccurate." },
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
            pending.PassTarget);
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
        int passTarget)
    {
        var interceptor = FindTeamPlayer(defendingTeam, interceptorPlacement.PlayerId);
        var interceptionRoll = _dice.RollD6();
        var interceptorSquare = interceptorPlacement.Square!;
        var interceptionTackleZones = CountOpposingTackleZones(match, defendingTeam.Id, interceptor.Id, interceptorSquare);
        var interceptionTarget = InterceptionTarget(interceptor, match.Weather, interceptionTackleZones);

        if (RollSucceeds(interceptionRoll, interceptionTarget, ruleset.Dice))
        {
            var interceptedMatch = match with
            {
                Ball = new BallState { CarrierPlayerId = interceptor.Id },
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

        return ResolvePassLanding(failedInterceptionMatch, ruleset, passingTeam, passer, receiver, targetSquare, passRangeName, passRoll, passTarget);
    }

    private MatchState ResolvePassLanding(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
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
            var landedMatch = ResolveBallLanding(match, ruleset, team, targetSquare);
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
        var catchRoll = _dice.RollD6();
        var catchTackleZones = CountOpposingTackleZones(match, team.Id, receiver.Id, receiverPlacement.Square!);
        var catchTarget = CatchTarget(receiver, match.Weather, catchTackleZones);

        if (RollSucceeds(catchRoll, catchTarget, ruleset.Dice))
        {
            return match with
            {
                Ball = new BallState { CarrierPlayerId = receiver.Id },
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, catch roll {catchRoll} vs {catchTarget}+ ({catchTackleZones} opposing tackle zones), complete." }
                ]
            };
        }

        var scatterSquare = ScatterFrom(ruleset, receiverPlacement.Square!);
        var bouncedMatch = ResolveBallLanding(match, ruleset, team, scatterSquare);
        var droppedMatch = bouncedMatch with
        {
            Log =
            [
                .. bouncedMatch.Log,
                new MatchLogEntry { Message = $"{passer.Name} passes to {receiver.Name}: {passRangeName} pass roll {passRoll} vs {passTarget}+, catch roll {catchRoll} vs {catchTarget}+ ({catchTackleZones} opposing tackle zones), dropped." },
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

        var eventRoll = Roll2D6Detailed();
        var eventResult = ResolveKickoffEvent(match, eventRoll.Total);
        var kickoffMatch = eventResult.Match;
        var scatterDistance = _dice.RollD6();
        var scatterSquare = ScatterFrom(ruleset, targetSquare, scatterDistance);
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"Kickoff event roll {eventRoll.Total}: {eventResult.Name}. {eventResult.Message}" },
            new() { Message = $"Kickoff targeted {targetSquare.X},{targetSquare.Y} and scattered {scatterDistance} square{(scatterDistance == 1 ? "" : "s")} to {scatterSquare.X},{scatterSquare.Y}." }
        };

        if (eventResult.ExtraScatter)
        {
            var gustSquare = ScatterFrom(ruleset, scatterSquare);
            log.Add(new MatchLogEntry { Message = $"Changing weather gust scatters the ball to {gustSquare.X},{gustSquare.Y}." });
            scatterSquare = gustSquare;
        }

        if (!IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            var touchbackReceiver = FindTouchbackReceiver(kickoffMatch, receivingTeam)
                ?? throw new InvalidOperationException("Receiving team has no standing player for touchback.");

            return kickoffMatch with
            {
                Phase = MatchPhase.OffensivePlayerTurn,
                Ball = new BallState { CarrierPlayerId = touchbackReceiver.Id },
                Activations = [],
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
            Activations = [],
            Log =
            [
                .. bouncedMatch.Log,
                .. log,
                new MatchLogEntry { Message = "Kickoff resolved. Offensive player turn begins." }
            ]
        };
    }

    private KickoffEventResult ResolveKickoffEvent(MatchState match, int roll)
    {
        return roll switch
        {
            2 => new KickoffEventResult(match, "Get the Ref", "Bribe/prayer effects are not implemented yet."),
            3 => new KickoffEventResult(match, "Time-out", "Turn-marker adjustment is not implemented yet."),
            4 => new KickoffEventResult(match, "Solid Defence", "Defensive setup repositioning is not implemented yet."),
            5 => new KickoffEventResult(match, "High Kick", "Free receiver movement under the ball is not implemented yet."),
            6 => new KickoffEventResult(match, "Cheering Fans", "Fan/prayer effects are not implemented yet."),
            7 => new KickoffEventResult(match, "Brilliant Coaching", "Assistant coach/prayer effects are not implemented yet."),
            8 => ResolveChangingWeather(match),
            9 => new KickoffEventResult(match, "Quick Snap", "Offensive free movement is not implemented yet."),
            10 => new KickoffEventResult(match, "Blitz", "Defensive free activation is not implemented yet."),
            11 => new KickoffEventResult(match, "Throw a Rock", "Random player injury from the crowd is not implemented yet."),
            12 => new KickoffEventResult(match, "Pitch Invasion", "Random player knockdown from the crowd is not implemented yet."),
            _ => new KickoffEventResult(match, "Kickoff", "No kickoff event.")
        };
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
        return new KickoffEventResult(nextMatch, "Changing Weather", message, extraScatter);
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

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

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
        var pushedMatch = PushPlayer(match with { PendingPush = null }, ruleset, defender, pending.DefenderSquare, square, pending.KnockDefenderDown);

        return pushedMatch with
        {
            Log =
            [
                .. pushedMatch.Log,
                new MatchLogEntry { Message = $"{pending.ResultMessage} {defender.Name} is pushed to {square.X},{square.Y}." }
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

        if (GetActivation(match, attackerPlayerId, attackerTeam.Id) is not null)
        {
            throw new InvalidOperationException($"{attacker.Name} has already been activated this turn.");
        }

        if (HasUsedBlitz(match, attackerTeam.Id))
        {
            throw new InvalidOperationException($"{attackerTeam.Name} has already used its blitz this turn.");
        }

        var movedMatch = MovePlayerCore(match, ruleset, attackerTeam, attackerPlayerId, destination, PlayerTurnAction.Blitz, defenderPlayerId);
        if (movedMatch.Phase != match.Phase || movedMatch.ActiveTeamId != match.ActiveTeamId || movedMatch.PendingReroll is not null)
        {
            return movedMatch;
        }

        var attackerPlacement = ValidateBlock(movedMatch, attackerTeam, attackerPlayerId, defenderTeam, defenderPlayerId);
        var defender = FindTeamPlayer(defenderTeam, defenderPlayerId);
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

        if (HasUsedFoul(match, foulingTeam.Id))
        {
            throw new InvalidOperationException($"{foulingTeam.Name} has already used its foul this turn.");
        }

        var fouler = FindTeamPlayer(foulingTeam, foulerPlayerId);
        var victim = FindTeamPlayer(victimTeam, victimPlayerId);
        if (GetActivation(match, foulerPlayerId, foulingTeam.Id) is not null)
        {
            throw new InvalidOperationException($"{fouler.Name} has already been activated this turn.");
        }

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
        var activatedMatch = AddActivation(match, foulerPlayerId, foulingTeam.Id, PlayerTurnAction.Foul, goForItsUsed: 0);
        var armorRoll = Roll2D6Detailed();
        var armorTotal = armorRoll.Total + attackAssists - defenseAssists;
        var log = new List<MatchLogEntry>
        {
            new()
            {
                Message = $"{fouler.Name} fouls {victim.Name}: armor {armorRoll.Total} +{attackAssists} -{defenseAssists} = {armorTotal} vs AV {victim.Stats.Armor}+."
            }
        };

        var nextMatch = activatedMatch;
        var sentOff = armorRoll.IsDoubles;
        if (armorTotal > victim.Stats.Armor)
        {
            var injuryRoll = Roll2D6Detailed();
            sentOff = sentOff || injuryRoll.IsDoubles;
            var injury = ResolveInjury(injuryRoll.Total);
            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(placement => placement.PlayerId == victim.Id
                        ? ApplyPitchState(nextMatch, placement, injury.State, OccupiesPitch(injury.State) ? victimSquare : null, injury.Casualty)
                        : placement)
                    .ToArray()
            };
            log.Add(new MatchLogEntry { Message = $"{victim.Name} injury roll {injuryRoll.Total}: {FormatPitchState(injury.State)}." });
            if (injury.Casualty is not null)
            {
                log.Add(new MatchLogEntry { Message = $"{victim.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
            }
        }
        else
        {
            log.Add(new MatchLogEntry { Message = $"{victim.Name}'s armor holds." });
        }

        nextMatch = nextMatch with { Log = [.. nextMatch.Log, .. log] };

        if (!sentOff)
        {
            return nextMatch;
        }

        var sentOffMatch = nextMatch with
        {
            Placements = nextMatch.Placements
                .Select(placement => placement.PlayerId == fouler.Id
                    ? placement with { Square = null, State = PlayerPitchState.SentOff, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{fouler.Name} is sent off for the foul." }]
        };

        return ApplyTurnover(sentOffMatch, ruleset, foulingTeam.Id);
    }

    private MatchState MovePlayerCore(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid playerId,
        PitchSquare destination,
        PlayerTurnAction action,
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

        var existingActivation = GetActivation(match, playerId, team.Id);
        if (existingActivation is not null)
        {
            throw new InvalidOperationException($"{player.Name} has already been activated this turn.");
        }

        var isStandingUp = placement.State == PlayerPitchState.Prone;
        var path = BuildMovementPath(placement.Square!, destination);
        if (path.Length == 0 && !isStandingUp)
        {
            throw new InvalidOperationException("Choose a different square to move to.");
        }

        if (path.Any(square => match.Placements.Any(current => current.PlayerId != playerId && current.Square == square)))
        {
            throw new InvalidOperationException("Movement paths cannot pass through occupied squares.");
        }

        var movementAllowance = isStandingUp
            ? Math.Max(0, player.Stats.Movement - 3)
            : player.Stats.Movement;
        var goForItsUsed = Math.Max(0, path.Length - movementAllowance);
        if (goForItsUsed > MaxGoForItsPerActivation)
        {
            var movementDescription = isStandingUp
                ? $"{movementAllowance} squares after standing"
                : $"{player.Stats.Movement} squares";
            throw new InvalidOperationException($"{player.Name} can move {movementDescription} plus {MaxGoForItsPerActivation} go-for-its, not {path.Length}.");
        }

        var nextMatch = AddActivation(match, playerId, team.Id, action, goForItsUsed);
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

        for (var stepIndex = 0; stepIndex < path.Length; stepIndex++)
        {
            var currentPlacement = nextMatch.Placements.First(current => current.PlayerId == playerId);
            var currentSquare = currentPlacement.Square!;
            var nextSquare = path[stepIndex];

            if (IsMarkedByOpponent(nextMatch, team.Id, playerId, currentSquare))
            {
                var dodgeRoll = _dice.RollD6();
                var opposingTackleZones = CountOpposingTackleZones(nextMatch, team.Id, playerId, nextSquare);
                var dodgeTarget = DodgeTarget(player, opposingTackleZones);
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
                        blitzDefenderPlayerId: blitzDefenderPlayerId);
                }

                nextMatch = nextMatch with
                {
                    Log =
                    [
                        .. nextMatch.Log,
                        new MatchLogEntry { Message = $"{player.Name} dodges from {currentSquare.X},{currentSquare.Y} to {nextSquare.X},{nextSquare.Y}: rolled {dodgeRoll} vs {dodgeTarget}+ ({opposingTackleZones} opposing tackle zones), success." }
                    ]
                };
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

        if (roll == 2)
        {
            var attackerInjuryState = ResolveFallInjury(attacker);
            var defenderInjuryState = ResolveFallInjury(defender);
            var defenderDown = KnockPlayerDown(match, ruleset, defender, defenderPlacement, defenderInjuryState, defenderPlacement.Square!);
            var bothDown = KnockPlayerDown(defenderDown, ruleset, attacker, attackerPlacement, attackerInjuryState, attackerPlacement.Square!);
            return ApplyTurnover(bothDown with
            {
                Log =
                [
                    .. bothDown.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, both players down." }
                ]
            }, ruleset, attackerTeam.Id);
        }

        if (roll <= 4)
        {
            return ResolvePushAfterBlock(
                match,
                ruleset,
                attacker,
                attackerPlacement,
                defender,
                defenderPlacement,
                knockDefenderDown: false,
                $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, pushed back.");
        }

        return ResolvePushAfterBlock(
            match,
            ruleset,
            attacker,
            attackerPlacement,
            defender,
            defenderPlacement,
            knockDefenderDown: true,
            $"{attacker.Name} blocks {defender.Name}: {strengthText}, rolled {rollText}, chose {roll}, defender down.");
    }

    private MatchState ResolvePushAfterBlock(
        MatchState match,
        Ruleset ruleset,
        Player attacker,
        PlayerPlacement attackerPlacement,
        Player defender,
        PlayerPlacement defenderPlacement,
        bool knockDefenderDown,
        string resultMessage)
    {
        var legalSquares = LegalPushSquares(match, ruleset, attackerPlacement.Square!, defenderPlacement.Square!, defender.Id);
        if (legalSquares.Length == 0)
        {
            var resolvedMatch = PushPlayerIntoCrowd(match, ruleset, defenderPlacement);

            return resolvedMatch with
            {
                Log =
                [
                    .. resolvedMatch.Log,
                    new MatchLogEntry { Message = $"{resultMessage} No legal push square is available; {defender.Name} is pushed into the crowd." }
                ]
            };
        }

        if (legalSquares.Length == 1)
        {
            var pushedMatch = PushPlayer(match, ruleset, defender, defenderPlacement.Square!, legalSquares[0], knockDefenderDown);
            return pushedMatch with
            {
                Log =
                [
                    .. pushedMatch.Log,
                    new MatchLogEntry { Message = $"{resultMessage} {defender.Name} is pushed to {legalSquares[0].X},{legalSquares[0].Y}." }
                ]
            };
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
                ResultMessage = resultMessage
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{resultMessage} Choose a push square." }
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
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, assistingSquare));
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square));
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
        if (match.Ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, square);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            nextMatch = match with { Ball = new BallState { Square = landing.Square } };
            log.AddRange(landing.Log.Prepend(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." }));
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

    private MatchState PushPlayer(MatchState match, Ruleset ruleset, Player player, PitchSquare source, PitchSquare destination, bool knockDown)
    {
        return PushPlacement(
            match,
            ruleset,
            player.Id,
            player.Name,
            source,
            destination,
            knockDown,
            () => ResolveFallInjury(player));
    }

    private MatchState PushPlacement(
        MatchState match,
        Ruleset ruleset,
        Guid playerId,
        string playerName,
        PitchSquare source,
        PitchSquare destination,
        bool knockDown,
        Func<InjuryResolution> resolveKnockdownState)
    {
        var placement = FindPlacement(match, playerId)
            ?? throw new InvalidOperationException("Pushed player is not part of this match.");

        var occupant = FindPushOccupant(match, destination, ignoredPlayerId: playerId);
        if (occupant is not null)
        {
            var chainDestination = LegalPushSquares(match, ruleset, source, destination, occupant.PlayerId).FirstOrDefault();
            match = chainDestination is null
                ? PushPlayerIntoCrowd(match, ruleset, occupant)
                : PushPlacement(match, ruleset, occupant.PlayerId, occupant.PlayerId.ToString(), destination, chainDestination, knockDown: false, () => new InjuryResolution(occupant.State));
            placement = FindPlacement(match, playerId)
                ?? throw new InvalidOperationException("Pushed player is not part of this match.");
        }

        var ball = match.Ball;
        var log = new List<MatchLogEntry>();
        if (ball.CarrierPlayerId == playerId && knockDown)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
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

    private MatchState BounceBall(MatchState match, Ruleset ruleset, LeagueTeam originalTeam, PitchSquare square)
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
            }, ruleset, originalTeam, landing.Square);
        }

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
        var target = CatchTarget(receiver, match.Weather);
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

    private MatchState ResolveBallLanding(MatchState match, Ruleset ruleset, LeagueTeam originalTeam, PitchSquare square)
    {
        return BounceBall(match, ruleset, originalTeam, square);
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

        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var fallenMatch = match with
        {
            Ball = ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == player.Id
                    ? ApplyPitchState(match, current, injury.State, OccupiesPitch(injury.State) ? destination : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. match.Log, .. log]
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
        int target)
    {
        var injury = ResolveFallInjury(player);
        var ball = match.Ball;
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"{player.Name} dodges to {destination.X},{destination.Y}: rolled {roll} vs {target}+, failed." },
            new() { Message = $"{player.Name} falls at {destination.X},{destination.Y} and is {FormatPitchState(injury.State)}." }
        };
        if (injury.Casualty is not null)
        {
            log.Add(new MatchLogEntry { Message = $"{player.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
        }

        if (ball.CarrierPlayerId == player.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, destination);
            var landing = ResolveLooseBallLanding(ruleset, scatterSquare);
            ball = new BallState { Square = landing.Square };
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        var fallenMatch = match with
        {
            Ball = ball,
            Placements = match.Placements
                .Select(current => current.PlayerId == player.Id
                    ? ApplyPitchState(match, current, injury.State, OccupiesPitch(injury.State) ? destination : null, injury.Casualty)
                    : current)
                .ToArray(),
            Log = [.. match.Log, .. log]
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
        var opposingTackleZones = CountOpposingTackleZones(match, team.Id, player.Id, square);
        var target = PickupTarget(player, opposingTackleZones, match.Weather);
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

        var reroll = _dice.RollD6();
        var rerolledMatch = useTeamReroll
            ? SpendTeamReroll(baseMatch, team.Id)
            : baseMatch;
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
        int goForItNumber = 0,
        Guid? blitzDefenderPlayerId = null)
    {
        var skillRerolls = AvailableSkillRerolls(player, kind);
        if (!CanUseTeamReroll(match, team.Id) && skillRerolls.Count == 0)
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
                    BlitzDefenderPlayerId = blitzDefenderPlayerId
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
                TeamRerollAvailable = CanUseTeamReroll(match, team.Id),
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
                    BlitzDefenderPlayerId = blitzDefenderPlayerId
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
            PendingRerollKind.Dodge => ResolveFailedDodge(match, ruleset, team, player, square, pending.Roll, pending.Target),
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

        return ContinueMovementFromStep(nextMatch, ruleset, team, opposingTeam, player, context.Action, context.Destination, path, stepIndex + 1, context.MovementAllowance, goForItNumber, context.BlitzDefenderPlayerId);
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
        Guid? blitzDefenderPlayerId = null)
    {
        var nextMatch = match;
        for (var stepIndex = startStepIndex; stepIndex < path.Count; stepIndex++)
        {
            var currentPlacement = nextMatch.Placements.First(current => current.PlayerId == player.Id);
            var currentSquare = currentPlacement.Square!;
            var nextSquare = path[stepIndex];

            if (IsMarkedByOpponent(nextMatch, team.Id, player.Id, currentSquare))
            {
                var dodgeRoll = _dice.RollD6();
                var opposingTackleZones = CountOpposingTackleZones(nextMatch, team.Id, player.Id, nextSquare);
                var dodgeTarget = DodgeTarget(player, opposingTackleZones);
                if (!RollSucceeds(dodgeRoll, dodgeTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(nextMatch, ruleset, team, player, PendingRerollKind.Dodge, dodgeRoll, dodgeTarget, action, destination, path, stepIndex, movementAllowance, goForItNumber, blitzDefenderPlayerId);
                }

                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{player.Name} dodges from {currentSquare.X},{currentSquare.Y} to {nextSquare.X},{nextSquare.Y}: rolled {dodgeRoll} vs {dodgeTarget}+, success." }]
                };
            }

            if (stepIndex >= movementAllowance)
            {
                goForItNumber++;
                var roll = _dice.RollD6();
                var goForItTarget = GoForItTarget(match.Weather);
                if (!RollSucceeds(roll, goForItTarget, ruleset.Dice))
                {
                    return CreatePendingMovementReroll(nextMatch, ruleset, team, player, PendingRerollKind.GoForIt, roll, goForItTarget, action, destination, path, stepIndex, movementAllowance, goForItNumber, blitzDefenderPlayerId);
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

    private static bool CanUseTeamReroll(MatchState match, Guid teamId)
    {
        return TeamRerollsRemaining(match, teamId) > 0 &&
            !match.TeamRerollUses.Any(use =>
                use.TeamId == teamId &&
                use.Half == match.Half &&
                use.Turn == match.Turn);
    }

    private static MatchState SpendTeamReroll(MatchState match, Guid teamId)
    {
        var nextUses = match.TeamRerollUses
            .Append(new TeamRerollUse { TeamId = teamId, Half = match.Half, Turn = match.Turn })
            .ToArray();

        if (teamId == match.HomeTeamId)
        {
            return match with
            {
                HomeRerollsRemaining = Math.Max(0, match.HomeRerollsRemaining - 1),
                TeamRerollUses = nextUses
            };
        }

        return match with
        {
            AwayRerollsRemaining = Math.Max(0, match.AwayRerollsRemaining - 1),
            TeamRerollUses = nextUses
        };
    }

    private static IReadOnlyList<string> AvailableSkillRerolls(Player player, PendingRerollKind kind)
    {
        var skillIds = kind switch
        {
            PendingRerollKind.Dodge => new[] { "dodge" },
            PendingRerollKind.Pickup => new[] { "sure-hands", "sure hands" },
            PendingRerollKind.GoForIt => new[] { "sure-feet", "sure feet" },
            _ => []
        };

        return player.Skills
            .Where(skill => skillIds.Contains(skill, StringComparer.OrdinalIgnoreCase))
            .ToArray();
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
            PendingPush = null,
            PendingInterception = null,
            PendingReroll = null,
            Log = [.. match.Log, new MatchLogEntry { Message = "Full time. Match complete." }]
        };
    }

    private MatchState StartSecondHalfSetup(MatchState match)
    {
        var kickingTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId;
        var recoveredMatch = ResolveKnockoutRecoveries(match);
        var resetPlacements = ResetAvailablePlayersToReserve(recoveredMatch);

        return recoveredMatch with
        {
            Half = 2,
            Turn = 1,
            HomeTurn = 1,
            AwayTurn = 1,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = kickingTeamId,
            Ball = new BallState(),
            HomeRerollsRemaining = recoveredMatch.HomeTeamRerolls,
            AwayRerollsRemaining = recoveredMatch.AwayTeamRerolls,
            TeamRerollUses = recoveredMatch.TeamRerollUses
                .Where(use => use.Half != 2)
                .ToArray(),
            Placements = resetPlacements,
            Activations = [],
            PendingBlock = null,
            PendingPush = null,
            PendingInterception = null,
            PendingReroll = null,
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
        var isHomeScore = scoringTeam.Id == match.HomeTeamId;
        var nextHomeScore = match.HomeScore + (isHomeScore ? 1 : 0);
        var nextAwayScore = match.AwayScore + (isHomeScore ? 0 : 1);
        var recoveredMatch = RecoverStunnedPlayers(match, scoringTeam.Id);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, scoringTeam.Id);
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

        var knockoutRecoveredMatch = ResolveKnockoutRecoveries(consumedTurnMatch);
        var resetPlacements = ResetAvailablePlayersToReserve(knockoutRecoveredMatch);

        return knockoutRecoveredMatch with
        {
            HomeScore = nextHomeScore,
            AwayScore = nextAwayScore,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = scoringTeam.Id,
            Turn = GetTeamTurn(knockoutRecoveredMatch, scoringTeam.Id),
            Ball = new BallState(),
            Placements = resetPlacements,
            Activations = [],
            PendingPush = null,
            PendingReroll = null,
            Log =
            [
                    .. knockoutRecoveredMatch.Log,
                new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." },
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
            if (roll >= 4)
            {
                placements.Add(placement with
                {
                    Square = null,
                    State = PlayerPitchState.Reserve,
                    StunnedRecoveryHalf = null,
                    StunnedRecoveryTurn = null,
                    Casualty = null
                });
                log.Add(new MatchLogEntry { Message = $"Knockout recovery {placement.PlayerId}: rolled {roll}, recovered." });
                continue;
            }

            placements.Add(placement with { Square = null });
            log.Add(new MatchLogEntry { Message = $"Knockout recovery {placement.PlayerId}: rolled {roll}, remains knocked out." });
        }

        return log.Count == 0
            ? match
            : match with
            {
                Placements = placements,
                Log = [.. match.Log, .. log]
            };
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

    private InjuryResolution ResolveFallInjury(Player player)
    {
        var armorRoll = Roll2D6();
        if (armorRoll <= player.Stats.Armor)
        {
            return new InjuryResolution(PlayerPitchState.Prone);
        }

        return ResolveInjury(Roll2D6());
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

        return new PitchSquare(square.X + (dx * distance), square.Y + (dy * distance));
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

    private static int CatchTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static int DodgeTarget(Player player, int opposingTackleZones)
    {
        return Math.Clamp(player.Stats.Agility - 1 + opposingTackleZones, 2, 6);
    }

    private static int PickupTarget(Player player, int opposingTackleZones, WeatherCondition weather)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility - 1 + opposingTackleZones + weatherModifier, 2, 6);
    }

    private static int CountOpposingTackleZones(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Count(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            IsAdjacent(opponentSquare, square));
    }

    private static int InterceptionTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility + 2 + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static int PassingTarget(Player player, PassRange passRange, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather is WeatherCondition.VerySunny or WeatherCondition.Blizzard ? 1 : 0;
        return Math.Clamp(player.Stats.Passing + passRange.TargetModifier + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static string PassTargetName(Player? receiver, PitchSquare targetSquare)
    {
        return receiver is null ? $"{targetSquare.X},{targetSquare.Y}" : receiver.Name;
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

sealed record KickoffEventResult(MatchState Match, string Name, string Message, bool ExtraScatter = false);

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
