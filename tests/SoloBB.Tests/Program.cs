using SoloBB.Core.Domain;
using SoloBB.Core.Services;

var root = FindRepositoryRoot();
var store = new JsonGameDataStore();
var ruleset = await store.LoadRulesetAsync(Path.Combine(root, "data", "rulesets", "bb2020-lite.json"));
var rosterSet = await store.LoadRosterSetAsync(Path.Combine(root, "data", "rosters", "core-teams.json"), ruleset);

Assert(ruleset.Id == "bb2020-lite", "ruleset id should load");
Assert(rosterSet.Rosters.Count >= 2, "sample roster set should contain teams");

var leagueService = new LeagueService();
var league = leagueService.CreateLeague("Smoke League", ruleset, [rosterSet]);
var humanRoster = rosterSet.Rosters.Single(roster => roster.Id == "human");

league = leagueService.AddTeam(
    league,
    ruleset,
    "Smoke Humans",
    "Tester",
    humanRoster,
    [
        new("One", "lineman"),
        new("Two", "lineman"),
        new("Three", "lineman"),
        new("Four", "lineman"),
        new("Five", "lineman"),
        new("Six", "lineman"),
        new("Seven", "thrower"),
        new("Eight", "catcher"),
        new("Nine", "blitzer"),
        new("Ten", "blitzer"),
        new("Eleven", "ogre")
    ],
    rerolls: 2);

var leaguePath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-league.json");
await store.SaveLeagueAsync(leaguePath, league);
var loadedLeague = await store.LoadLeagueAsync(leaguePath);

Assert(loadedLeague.Teams.Count == 1, "saved league should round-trip with one team");
Assert(loadedLeague.Teams[0].Players.Count == 11, "team should round-trip with eleven players");

var awayLeague = leagueService.CreateLeague("Away Smoke League", ruleset, [rosterSet]);
awayLeague = leagueService.AddTeam(
    awayLeague,
    ruleset,
    "Smoke Orcs",
    "Tester",
    rosterSet.Rosters.Single(roster => roster.Id == "orc"),
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Orc Lineman {index}", "lineman")),
    rerolls: 2);

var matchService = new MatchService();
var match = matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var matchPath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-match.json");
await store.SaveMatchAsync(matchPath, match);
var loadedMatch = await store.LoadMatchAsync(matchPath);

Assert(loadedMatch.HomeTeamId == loadedLeague.Teams[0].Id, "match home team should round-trip");
Assert(loadedMatch.AwayTeamId == awayLeague.Teams[0].Id, "match away team should round-trip");
Assert(loadedMatch.Phase == MatchPhase.DefenseSetup, "match should start with defense setup");
Assert(loadedMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should set up defense first");
Assert(loadedMatch.HomeTurn == 1 && loadedMatch.AwayTurn == 1, "both teams should start half one on turn one");
Assert(loadedMatch.FirstHalfReceivingTeamId == loadedLeague.Teams[0].Id, "home team should be recorded as the first-half receiving team");
Assert(loadedMatch.Placements.Count == 22, "match should place both teams in reserve");

var awayPlayerToPlace = awayLeague.Teams[0].Players[0];
var defenseSetupMatch = matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(5, 5));
var defensePlacedPlayer = defenseSetupMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(defensePlacedPlayer.State == PlayerPitchState.Standing, "defense player should stand on the pitch");
Assert(defensePlacedPlayer.Square == new PitchSquare(5, 5), "defense player should keep assigned square");

var offenseSetupMatch = matchService.AdvancePhase(defenseSetupMatch);
Assert(offenseSetupMatch.Phase == MatchPhase.OffenseSetup, "defense setup should advance to offense setup");
Assert(offenseSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should set up offense");

var playerToPlace = loadedLeague.Teams[0].Players[0];
var placedMatch = matchService.PlacePlayer(offenseSetupMatch, ruleset, playerToPlace.Id, new(0, 0));
var placedPlayer = placedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(placedPlayer.State == PlayerPitchState.Standing, "offense player should stand on the pitch");
Assert(placedPlayer.Square == new PitchSquare(0, 0), "offense player should keep assigned square");

var kickoffMatch = matchService.AdvancePhase(placedMatch);
Assert(kickoffMatch.Phase == MatchPhase.Kickoff, "offense setup should advance to kickoff");
Assert(matchService.AdvancePhase(kickoffMatch).Phase == MatchPhase.Kickoff, "generic phase advance should not skip unresolved kickoff");

var kickoffService = new MatchService(new FixedDiceRoller(d8: [5]));
var offensiveTurnMatch = kickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(offensiveTurnMatch.Phase == MatchPhase.OffensivePlayerTurn, "kickoff should advance to offensive player turn");
Assert(offensiveTurnMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should have the offensive turn");
Assert(offensiveTurnMatch.Ball.Square == new PitchSquare(3, 2), "kickoff landing on empty square should leave loose ball");

var caughtKickoffService = new MatchService(new FixedDiceRoller(d6: [4], d8: [4]));
var caughtKickoffMatch = caughtKickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(0, 0));

Assert(caughtKickoffMatch.Ball.CarrierPlayerId == playerToPlace.Id, "kickoff landing on receiver should allow a catch");

var touchbackService = new MatchService(new FixedDiceRoller(d8: [5]));
var touchbackMatch = touchbackService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(ruleset.PitchWidth / 2, 0));

Assert(touchbackMatch.Ball.CarrierPlayerId == playerToPlace.Id, "kickoff outside receiving half should award touchback to receiving player");

var movedMatch = matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0));
var movedPlayer = movedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(movedPlayer.Square == new PitchSquare(3, 0), "moved player should keep destination square");
Assert(movedMatch.Activations.Count == 1, "movement should activate the player");

var touchdownReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(ruleset.PitchWidth - 2, 0), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var scoredMatch = matchService.MovePlayer(touchdownReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(ruleset.PitchWidth - 1, 0));

Assert(scoredMatch.HomeScore == 1, "home ball carrier should score in away end zone");
Assert(scoredMatch.AwayScore == 0, "away score should not change on home touchdown");
Assert(scoredMatch.Phase == MatchPhase.DefenseSetup, "touchdown should reset to defense placement");
Assert(scoredMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "scoring team should set up defense for the next drive");
Assert(scoredMatch.Ball.CarrierPlayerId is null && scoredMatch.Ball.Square is null, "touchdown should clear the ball");
Assert(scoredMatch.Placements.Any(placement => placement.TeamId == loadedLeague.Teams[0].Id && placement.State == PlayerPitchState.Reserve), "touchdown should reset available players to reserve");

var handOffReceiver = loadedLeague.Teams[0].Players[1];
var handOffReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == handOffReceiver.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var handOffService = new MatchService(new FixedDiceRoller(d6: [4]));
var handOffMatch = handOffService.HandOffBall(handOffReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);

Assert(handOffMatch.Ball.CarrierPlayerId == handOffReceiver.Id, "successful handoff should transfer the ball");
Assert(handOffMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.HandOff, "handoff should record an activation");

var failedHandOffService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedHandOffMatch = failedHandOffService.HandOffBall(handOffReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);

Assert(failedHandOffMatch.Phase == MatchPhase.DefensiveTurn, "failed offensive handoff should cause a turnover");
Assert(failedHandOffMatch.Ball.CarrierPlayerId is null, "failed handoff should leave the ball loose");
Assert(failedHandOffMatch.Ball.Square == new PitchSquare(3, 1), "failed handoff should scatter from the receiver");

var bounceReceiver = loadedLeague.Teams[0].Players[2];
var friendlyBounceMatch = handOffReadyMatch with
{
    Placements = handOffReadyMatch.Placements
        .Select(placement => placement.PlayerId == bounceReceiver.Id
            ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var friendlyBounceService = new MatchService(new FixedDiceRoller(d6: [1, 4], d8: [5]));
var friendlyBounceResult = friendlyBounceService.HandOffBall(friendlyBounceMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);

Assert(friendlyBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "friendly catch on a handoff bounce should avoid turnover");
Assert(friendlyBounceResult.Ball.CarrierPlayerId == bounceReceiver.Id, "friendly bounce catch should recover the ball");

var chainBounceService = new MatchService(new FixedDiceRoller(d6: [1, 1, 4], d8: [5, 4]));
var chainBounceResult = chainBounceService.HandOffBall(friendlyBounceMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);

Assert(chainBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "eventual friendly catch after chained bounces should avoid turnover");
Assert(chainBounceResult.Ball.CarrierPlayerId == handOffReceiver.Id, "ball can bounce back to original receiver and be caught");

var passerPlayer = loadedLeague.Teams[0].Players[6];
var passReceiver = loadedLeague.Teams[0].Players[7];
var passReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = passerPlayer.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == passerPlayer.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == passReceiver.Id
                ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var passService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var completedPassMatch = passService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(completedPassMatch.Ball.CarrierPlayerId == passReceiver.Id, "completed pass should transfer the ball to the receiver");
Assert(completedPassMatch.Activations.Single(activation => activation.PlayerId == passerPlayer.Id).Action == PlayerTurnAction.Pass, "pass should record a pass activation");

var failedPassService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedPassMatch = failedPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(failedPassMatch.Phase == MatchPhase.DefensiveTurn, "inaccurate pass to empty space should cause a turnover");
Assert(failedPassMatch.Ball.CarrierPlayerId is null, "inaccurate pass should leave the ball loose if not recovered");
Assert(failedPassMatch.Ball.Square == new PitchSquare(5, 1), "inaccurate pass should scatter from the receiver");

var droppedPassService = new MatchService(new FixedDiceRoller(d6: [2, 1], d8: [5]));
var droppedPassMatch = droppedPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(droppedPassMatch.Phase == MatchPhase.DefensiveTurn, "dropped completed pass should cause a turnover if not recovered");
Assert(droppedPassMatch.Ball.Square == new PitchSquare(5, 1), "dropped pass should bounce from the receiver");

var passBounceReceiver = loadedLeague.Teams[0].Players[1];
var friendlyPassBounceMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == passBounceReceiver.Id
            ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var friendlyPassBounceService = new MatchService(new FixedDiceRoller(d6: [1, 4], d8: [5]));
var friendlyPassBounceResult = friendlyPassBounceService.PassBall(friendlyPassBounceMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(friendlyPassBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "friendly catch on an inaccurate pass scatter should avoid turnover");
Assert(friendlyPassBounceResult.Ball.CarrierPlayerId == passBounceReceiver.Id, "friendly player should be able to recover a scattered pass");

var interceptionMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var interceptionService = new MatchService(new FixedDiceRoller(d6: [2, 6]));
var interceptedPassMatch = interceptionService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(interceptedPassMatch.Phase == MatchPhase.DefensiveTurn, "successful interception should cause a turnover");
Assert(interceptedPassMatch.ActiveTeamId == awayLeague.Teams[0].Id, "intercepting team should become active after turnover");
Assert(interceptedPassMatch.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "interceptor should carry the ball");

var secondInterceptor = awayLeague.Teams[0].Players[1];
var multiInterceptionMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == secondInterceptor.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var pendingInterceptionService = new MatchService(new FixedDiceRoller(d6: [2, 1, 3]));
var pendingInterceptionMatch = pendingInterceptionService.PassBall(multiInterceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(pendingInterceptionMatch.PendingInterception?.EligiblePlayerIds.SequenceEqual([awayPlayerToPlace.Id, secondInterceptor.Id]) == true, "multiple eligible interceptors should require a defensive choice");
Assert(pendingInterceptionMatch.Ball.CarrierPlayerId is null && pendingInterceptionMatch.Ball.Square is null, "pending interception should keep the ball in flight");

var completedAfterFailedInterception = pendingInterceptionService.ChooseInterceptor(pendingInterceptionMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], secondInterceptor.Id);

Assert(completedAfterFailedInterception.PendingInterception is null, "choosing an interceptor should clear the pending choice");
Assert(completedAfterFailedInterception.Ball.CarrierPlayerId == passReceiver.Id, "failed interception should allow the receiver to catch the pass");
Assert(completedAfterFailedInterception.Phase == MatchPhase.OffensivePlayerTurn, "failed interception and completed catch should not cause a turnover");

var blockReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var blockService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var blockMatch = blockService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
var blockedPlayer = blockMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(blockedPlayer.State == PlayerPitchState.Prone, "successful block should knock defender down");
Assert(blockMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Block, "block should activate the attacker");

var badBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var badBlockMatch = badBlockService.BlockPlayer(
    blockReadyMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    awayLeague.Teams[0],
    awayPlayerToPlace.Id);
var badBlockAttacker = badBlockMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(badBlockMatch.Phase == MatchPhase.DefensiveTurn, "attacker-down block should cause a turnover");
Assert(badBlockAttacker.State == PlayerPitchState.Casualty, "attacker-down block should resolve injury");
Assert(badBlockMatch.Ball.Square == new PitchSquare(2, 1), "attacker-down ball carrier should scatter the ball");

var blitzReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var blitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var blitzMatch = blitzService.BlitzPlayer(blitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(2, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
var blitzActivation = blitzMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(blitzActivation.Action == PlayerTurnAction.Blitz, "blitz should record a blitz activation");
Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "blitz should move the attacker");
Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "blitz should resolve the block");

var assistingPlayer = loadedLeague.Teams[0].Players[1];
var assistedBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == assistingPlayer.Id
                ? placement with { Square = new PitchSquare(2, 2), State = PlayerPitchState.Standing }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                    : placement)
        .ToArray()
};
var assistedBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 1, 1]));
var assistedPendingBlock = assistedBlockService.BlockPlayer(assistedBlockMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(assistedPendingBlock.PendingBlock?.Rolls.SequenceEqual([1, 6]) == true, "multi-die assisted block should wait for player choice");

var assistedBlockResult = assistedBlockService.ChooseBlockDie(assistedPendingBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], roll: 6);

Assert(assistedBlockResult.PendingBlock is null, "choosing a block die should clear pending block choice");
Assert(assistedBlockResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "chosen favorable block die should knock defender down");

var weakBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[7].Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayLeague.Teams[0].Players[3].Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var weakBlockService = new MatchService(new FixedDiceRoller(d6: [6, 1, 6, 6, 6]));
var weakPendingBlock = weakBlockService.BlockPlayer(weakBlockMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[7].Id, awayLeague.Teams[0], awayLeague.Teams[0].Players[3].Id);

Assert(weakPendingBlock.PendingBlock?.Rolls.SequenceEqual([6, 1]) == true, "unfavorable multi-die block should still wait for player choice");

var weakBlockResult = weakBlockService.ChooseBlockDie(weakPendingBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], roll: 6);

Assert(weakBlockResult.Placements.Single(placement => placement.PlayerId == awayLeague.Teams[0].Players[3].Id).State == PlayerPitchState.Casualty, "chosen high block die should knock defender down even when defender had strength advantage");

var goForItService = new MatchService(new FixedDiceRoller(d6: [2]));
var goForItMatch = goForItService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(7, 0));
var goForItActivation = goForItMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(goForItActivation.GoForItsUsed == 1, "movement past MA should spend go-for-its");
Assert(goForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(7, 0), "successful go-for-it should move the player");

var failedGoForItService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var failedGoForItMatch = failedGoForItService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
var failedGoForItPlayer = failedGoForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(failedGoForItMatch.Phase == MatchPhase.DefensiveTurn, "failed offensive go-for-it should cause a turnover to defensive turn");
Assert(failedGoForItMatch.ActiveTeamId == awayLeague.Teams[0].Id, "failed offensive go-for-it should activate defense");
Assert(failedGoForItPlayer.State == PlayerPitchState.Casualty, "failed go-for-it should resolve injury");
Assert(failedGoForItMatch.Ball.CarrierPlayerId is null, "failed ball carrier go-for-it should drop the ball");
Assert(failedGoForItMatch.Ball.Square == new PitchSquare(8, 0), "failed ball carrier go-for-it should scatter the ball");

var defensiveTurnMatch = matchService.AdvancePhase(movedMatch);
Assert(defensiveTurnMatch.Phase == MatchPhase.DefensiveTurn, "offensive player turn should advance to defensive turn");
Assert(defensiveTurnMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should have the defensive turn");
Assert(defensiveTurnMatch.HomeTurn == 2 && defensiveTurnMatch.AwayTurn == 1, "ending the offensive turn should consume home turn one");
Assert(defensiveTurnMatch.Turn == 1, "defensive turn should use the active team's turn counter");

var defensiveMoveMatch = matchService.MovePlayer(defensiveTurnMatch, ruleset, awayLeague.Teams[0], awayPlayerToPlace.Id, new(6, 5));
var defensiveMovedPlayer = defensiveMoveMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);
Assert(defensiveMovedPlayer.Square == new PitchSquare(6, 5), "defensive player should move during defensive turn");

var nextOffensiveTurnMatch = matchService.AdvanceTurn(defensiveMoveMatch, ruleset);
Assert(nextOffensiveTurnMatch.Phase == MatchPhase.OffensivePlayerTurn, "defensive turn should advance to next offensive player turn");
Assert(nextOffensiveTurnMatch.Turn == 2, "turn should increment after defensive turn");
Assert(nextOffensiveTurnMatch.HomeTurn == 2 && nextOffensiveTurnMatch.AwayTurn == 2, "both teams should be on turn two after each has acted once");
Assert(nextOffensiveTurnMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "offense should regain the next player turn");

var lastFirstHalfDefensiveTurn = offensiveTurnMatch with
{
    Half = 1,
    HomeTurn = ruleset.TurnsPerHalf + 1,
    AwayTurn = ruleset.TurnsPerHalf,
    Phase = MatchPhase.DefensiveTurn,
    ActiveTeamId = awayLeague.Teams[0].Id,
    FirstHalfReceivingTeamId = loadedLeague.Teams[0].Id
};
var secondHalfSetupMatch = matchService.AdvanceTurn(lastFirstHalfDefensiveTurn, ruleset);

Assert(secondHalfSetupMatch.Half == 2, "both teams finishing eight turns should advance to the second half");
Assert(secondHalfSetupMatch.HomeTurn == 1 && secondHalfSetupMatch.AwayTurn == 1, "second half should reset both team turn counters");
Assert(secondHalfSetupMatch.Phase == MatchPhase.DefenseSetup, "second half should begin with defense placement");
Assert(secondHalfSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "first-half receiving team should kick off to start the second half");
Assert(secondHalfSetupMatch.Ball.CarrierPlayerId is null && secondHalfSetupMatch.Ball.Square is null, "halftime should clear the ball");

var lastSecondHalfDefensiveTurn = offensiveTurnMatch with
{
    Half = 2,
    HomeTurn = ruleset.TurnsPerHalf + 1,
    AwayTurn = ruleset.TurnsPerHalf,
    Phase = MatchPhase.DefensiveTurn,
    ActiveTeamId = awayLeague.Teams[0].Id
};
var fullTimeMatch = matchService.AdvanceTurn(lastSecondHalfDefensiveTurn, ruleset);

Assert(fullTimeMatch.Phase == MatchPhase.Complete, "both teams finishing eight second-half turns should complete the match");

AssertThrows(
    () => leagueService.AddTeam(
        league,
        ruleset,
        "Too Few",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 10).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: 2),
    "drafts below players-per-side should fail");

AssertThrows(
    () => leagueService.AddTeam(
        league,
        ruleset,
        "Too Many Rerolls",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: ruleset.RerollCap + 1),
    "drafts above reroll cap should fail");

AssertThrows(
    () => matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0]),
    "matches should require two different teams");

AssertThrows(
    () => matchService.PlacePlayer(defenseSetupMatch, ruleset, awayLeague.Teams[0].Players[1].Id, new(5, 5)),
    "placement should reject occupied squares");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(-1, 0)),
    "placement should reject squares outside the pitch");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, playerToPlace.Id, new(0, 0)),
    "placement should reject inactive setup teams");

AssertThrows(
    () => matchService.MovePlayer(placedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0)),
    "movement should reject setup phases");

AssertThrows(
    () => matchService.BlockPlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blocking should reject non-adjacent players");

AssertThrows(
    () => matchService.HandOffBall(
        handOffReadyMatch with
        {
            Placements = handOffReadyMatch.Placements
                .Select(placement => placement.PlayerId == handOffReceiver.Id
                    ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
                    : placement)
                .ToArray()
        },
        ruleset,
        loadedLeague.Teams[0],
        playerToPlace.Id,
        handOffReceiver.Id),
    "handoff should require adjacent players");

AssertThrows(
    () => matchService.HandOffBall(handOffMatch, ruleset, loadedLeague.Teams[0], handOffReceiver.Id, loadedLeague.Teams[0].Players[2].Id),
    "handoff should be limited to once per turn");

AssertThrows(
    () => passService.PassBall(completedPassMatch, ruleset, loadedLeague.Teams[0], passReceiver.Id, loadedLeague.Teams[0].Players[1].Id),
    "pass should be limited to once per turn");

AssertThrows(
    () => matchService.PassBall(
        passReadyMatch with
        {
            Placements = passReadyMatch.Placements
                .Select(placement => placement.PlayerId == passReceiver.Id
                    ? placement with { Square = new PitchSquare(20, 1), State = PlayerPitchState.Standing }
                    : placement)
                .ToArray()
        },
        ruleset,
        loadedLeague.Teams[0],
        passerPlayer.Id,
        passReceiver.Id),
    "pass should reject receivers beyond long bomb range");

AssertThrows(
    () => pendingInterceptionService.ChooseInterceptor(pendingInterceptionMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], awayLeague.Teams[0].Players[2].Id),
    "interception choice should reject ineligible defenders");

AssertThrows(
    () => matchService.BlockPlayer(movedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blocking should reject a second activation in the same turn");

AssertThrows(
    () => matchService.BlitzPlayer(blitzMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, new(2, 2), awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blitz should be limited to once per turn");

AssertThrows(
    () => matchService.MovePlayer(movedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(4, 0)),
    "movement should reject a second activation in the same turn");

AssertThrows(
    () => matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(10, 0)),
    "movement should reject destinations beyond movement plus go-for-it allowance");

AssertThrows(
    () => matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(5, 5)),
    "movement should reject occupied destinations");

AssertThrows(
    () => matchService.MovePlayer(loadedMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, new(1, 1)),
    "movement should reject reserve players");

AssertThrows(
    () => matchService.MovePlayer(
        offensiveTurnMatch,
        ruleset,
        awayLeague.Teams[0],
        awayPlayerToPlace.Id,
        new(6, 5)),
    "movement should reject inactive teams during a turn");

AssertThrows(
    () => matchService.MovePlayer(
        defensiveTurnMatch,
        ruleset,
        loadedLeague.Teams[0],
        playerToPlace.Id,
        new(4, 0)),
    "movement should reject offensive team during defensive turn");

Console.WriteLine("SoloBB smoke checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (File.Exists(Path.Combine(current, "project.godot")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate repository root.");
}

public sealed class FixedDiceRoller : IDiceRoller
{
    private readonly Queue<int> _d6;
    private readonly Queue<int> _d8;

    public FixedDiceRoller(IEnumerable<int>? d6 = null, IEnumerable<int>? d8 = null)
    {
        _d6 = new Queue<int>(d6 ?? [6]);
        _d8 = new Queue<int>(d8 ?? [1]);
    }

    public int RollD6()
    {
        return _d6.Count > 0 ? _d6.Dequeue() : 6;
    }

    public int RollD8()
    {
        return _d8.Count > 0 ? _d8.Dequeue() : 1;
    }
}
