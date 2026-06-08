using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;
using static SoloBB.Core.Services.RollTargets;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
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

        if (!PlacementsAreAdjacent(carrierPlacement, receiverPlacement))
        {
            throw new InvalidOperationException("Hand offs require adjacent players.");
        }

        var handOffAction = BeginPlayerAction(match, ruleset, team, carrier, PlayerTurnAction.HandOff, goForItsUsed: 0);
        if (handOffAction.Prevented)
        {
            return handOffAction.Match;
        }

        // Handing off ends the carrier's activation regardless of whether the catch succeeds.
        var activatedMatch = CompleteActivation(handOffAction.Match, carrierPlayerId, team.Id);
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
        bool isHailMary,
        int? forcedPassRoll = null)
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
                : ResolvePassRange(passerPlacement.Square!, targetSquare) ?? throw new InvalidOperationException("The receiver is out of passing range.");
        if (isDumpOff && (ResolvePassRange(passerPlacement.Square!, targetSquare)?.Name ?? "quick") != "quick")
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

        var passAttempt = RollPass(ruleset, passerPlayer, target, usePassSkillReroll, forcedPassRoll);
        var passerHasPassReroll = PlayerHasHookedEffect(ruleset, passerPlayer, GameEventKind.PassRoll, GameEventStage.AfterRoll, SkillEffect.PassReroll);
        if (forcedPassRoll is null &&
            !passAttempt.Success &&
            !usePassSkillReroll &&
            (passAttempt.Roll == 1 || passerHasPassReroll) &&
            (CanUseTeamReroll(passAction.Match, ruleset, team) || passerHasPassReroll))
        {
            return CreatePendingPassReroll(
                passAction.Match,
                ruleset,
                team,
                passerPlayer,
                passAttempt.Roll,
                target,
                targetSquare,
                defendingTeam,
                useCloudBurster,
                isDumpOff,
                isHailMary);
        }
        var passRoll = passAttempt.FinalRoll;
        var activatedMatch = (isDumpOff
            ? match
            : passAction.Match) with
        {
            Ball = new BallState()
        };
        // Throwing the ball ends the passer's activation regardless of the outcome.
        activatedMatch = CompleteActivation(activatedMatch, passerPlayerId, team.Id);

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

    private MatchState CreatePendingPassReroll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        int roll,
        int target,
        PitchSquare targetSquare,
        LeagueTeam? defendingTeam,
        bool useCloudBurster,
        bool isDumpOff,
        bool isHailMary)
    {
        var skillRerolls = AvailableSkillRerolls(ruleset, player, PendingRerollKind.Pass);
        if (!CanUseTeamReroll(match, ruleset, team) && skillRerolls.Count == 0)
        {
            return PassBallCore(match, ruleset, team, player.Id, targetSquare, defendingTeam, usePassSkillReroll: false, useCloudBurster, isDumpOff, isHailMary, forcedPassRoll: roll);
        }

        return match with
        {
            PendingReroll = new PendingRerollChoice
            {
                TeamId = team.Id,
                PlayerId = player.Id,
                Kind = PendingRerollKind.Pass,
                Roll = roll,
                Target = target,
                TeamRerollAvailable = CanUseTeamReroll(match, ruleset, team),
                SkillRerollIds = skillRerolls,
                Context = new PendingRerollContext
                {
                    MatchBeforeRoll = match,
                    Action = PlayerTurnAction.Pass,
                    Destination = targetSquare,
                    Path = [],
                    StepIndex = 0,
                    MovementAllowance = 0,
                    UseCloudBurster = useCloudBurster,
                    IsDumpOff = isDumpOff,
                    IsHailMary = isHailMary
                }
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} failed pass on {roll} vs {target}+. Choose whether to reroll." }
            ]
        };
    }

    private static PitchSquare[] SafePairOfHandsSquares(MatchState match, Ruleset ruleset, Player player, PitchSquare source, bool isLarge = false)
    {
        if (!PlayerHasHookedEffect(ruleset, player, GameEventKind.BallScatter, GameEventStage.BeforeEvent, SkillEffect.SafePairOfHands))
        {
            return [];
        }

        var footprint = OccupiedSquares(source, isLarge).ToHashSet();
        return footprint.SelectMany(AdjacentSquares)
            .Distinct()
            .Where(candidate => !footprint.Contains(candidate))
            .Where(candidate => IsOnPitch(ruleset, candidate))
            .Where(candidate =>
                FindPushOccupant(match, candidate, player.Id) is null &&
                match.Ball.Square != candidate)
            .ToArray();
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

    private PassAttempt RollPass(Ruleset ruleset, Player player, int target, bool usePassSkillReroll, int? forcedRoll = null)
    {
        var roll = forcedRoll ?? _dice.RollD6();
        int? reroll = null;
        var finalRoll = roll;
        if (forcedRoll is null &&
            usePassSkillReroll &&
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

    private PlayerPlacement? FindDivingCatchReceiver(MatchState match, Ruleset ruleset, LeagueTeam team, PitchSquare targetSquare)
    {
        if (match.Placements.Any(placement => PlacementOccupiesSquare(placement, targetSquare) && OccupiesPitch(placement.State)))
        {
            return null;
        }

        return match.Placements
            .Where(placement =>
                placement.TeamId == team.Id &&
                placement.State == PlayerPitchState.Standing &&
                IsAdjacentToPlacement(placement, targetSquare))
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
            IsAdjacentToPlacement(placement, currentSquare) &&
            !IsAdjacentToPlacement(placement, nextSquare) &&
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

}
