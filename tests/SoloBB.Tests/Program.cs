using SoloBB.Core.Domain;
using SoloBB.Core.Services;

var root = FindRepositoryRoot();
var store = new JsonGameDataStore();
var ruleset = await store.LoadRulesetAsync(Path.Combine(root, "data", "rulesets", "bb2020-lite.json"));
var rosterSet = await store.LoadRosterSetAsync(Path.Combine(root, "data", "rosters", "core-teams.json"), ruleset);

Assert(ruleset.Id == "bb2020-lite", "ruleset id should load");
Assert(rosterSet.Rosters.Count >= 2, "sample roster set should contain teams");

var leagueService = new LeagueService();
var league = leagueService.CreateLeague("Smoke League", ruleset, [rosterSet], targetTeamCount: 4);
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
Assert(loadedLeague.TargetTeamCount == 4, "saved league should round-trip target team count");
Assert(loadedLeague.Teams[0].Players.Count == 11, "team should round-trip with eleven players");
Assert(loadedLeague.Teams[0].TeamValue == 855_000, "team value should round-trip");

var awayLeague = leagueService.CreateLeague("Away Smoke League", ruleset, [rosterSet]);
awayLeague = leagueService.AddTeam(
    awayLeague,
    ruleset,
    "Smoke Orcs",
    "Tester",
    rosterSet.Rosters.Single(roster => roster.Id == "orc"),
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Orc Lineman {index}", "lineman")),
    rerolls: 2);

var benchLeague = leagueService.CreateLeague("Bench Smoke League", ruleset, [rosterSet]);
benchLeague = leagueService.AddTeam(
    benchLeague,
    ruleset,
    "Smoke Bench",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 12).Select(index => new PlayerDraftPick($"Bench Lineman {index}", "lineman")),
    rerolls: 2);

Assert(benchLeague.Teams[0].Players.Count == 12, "league teams should allow more than eleven players");
Assert(benchLeague.Teams[0].TeamValue == 700_000, "team value should include players and rerolls");

var fullRosterLeague = leagueService.CreateLeague("Full Roster League", ruleset, [rosterSet]);
fullRosterLeague = leagueService.AddTeam(
    fullRosterLeague,
    ruleset,
    "Smoke Full Roster",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 16).Select(index => new PlayerDraftPick($"Full Roster Lineman {index}", "lineman")),
    rerolls: 0);

Assert(fullRosterLeague.Teams[0].Players.Count == 16, "league teams should allow sixteen-player rosters");

var fanFactorLeague = leagueService.CreateLeague("Fan Factor League", ruleset, [rosterSet]);
fanFactorLeague = leagueService.AddTeam(
    fanFactorLeague,
    ruleset,
    "Smoke Fans",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Fan Lineman {index}", "lineman")),
    rerolls: 0,
    fanFactor: 1);

Assert(fanFactorLeague.Teams[0].Treasury == 450_000, "fan factor one should be free");
Assert(fanFactorLeague.Teams[0].TeamValue == 550_000, "team value should include free fan factor correctly");

var paidFanFactorLeague = leagueService.CreateLeague("Paid Fan Factor League", ruleset, [rosterSet]);
paidFanFactorLeague = leagueService.AddTeam(
    paidFanFactorLeague,
    ruleset,
    "Smoke Paid Fans",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Paid Fan Lineman {index}", "lineman")),
    rerolls: 0,
    fanFactor: 2);

Assert(paidFanFactorLeague.Teams[0].Treasury == 440_000, "fan factor above one should cost 10,000 gp per point");
Assert(paidFanFactorLeague.Teams[0].TeamValue == 560_000, "team value should include paid fan factor");

var originalTeamId = fanFactorLeague.Teams[0].Id;
fanFactorLeague = leagueService.UpdateTeam(
    fanFactorLeague,
    ruleset,
    originalTeamId,
    "Smoke Fans Edited",
    "Editor",
    humanRoster,
    Enumerable.Range(1, 12).Select(index => new PlayerDraftPick($"Edited Lineman {index}", "lineman")),
    rerolls: 0,
    fanFactor: 1);

Assert(fanFactorLeague.Teams.Count == 1, "editing a team should replace it rather than add a duplicate");
Assert(fanFactorLeague.Teams[0].Id == originalTeamId, "editing a team should preserve the team id");
Assert(fanFactorLeague.Teams[0].Name == "Smoke Fans Edited", "editing a team should update team details");
Assert(fanFactorLeague.Teams[0].Players.Count == 12, "editing a team should update the roster draft");
Assert(fanFactorLeague.Teams[0].TeamValue == 600_000, "editing a team should update team value");

var scheduledLeague = leagueService.CreateLeague("Scheduled League", ruleset, [rosterSet], targetTeamCount: 4);
for (var teamIndex = 1; teamIndex <= 4; teamIndex++)
{
    scheduledLeague = leagueService.AddTeam(
        scheduledLeague,
        ruleset,
        $"Schedule Team {teamIndex}",
        "Scheduler",
        humanRoster,
        Enumerable.Range(1, 11).Select(playerIndex => new PlayerDraftPick($"Schedule {teamIndex} Lineman {playerIndex}", "lineman")),
        rerolls: 0);
}

scheduledLeague = leagueService.CreateSeason(scheduledLeague);
var scheduledSeason = scheduledLeague.Seasons.Single();
var scheduledWeeks = scheduledSeason.Schedule.GroupBy(match => match.Week).OrderBy(group => group.Key).ToArray();

Assert(scheduledWeeks.Length == 6, "double round-robin should create (teams - 1) * 2 weeks");
Assert(scheduledSeason.Schedule.Count == 12, "four-team double round-robin should create twelve matches");
Assert(scheduledWeeks.All(group => group.Count() == 2), "each week should have two games for four teams");

var scheduledPairs = scheduledSeason.Schedule
    .GroupBy(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order()))
    .ToArray();

Assert(scheduledPairs.Length == 6, "each team pair should appear once as a pair");
Assert(scheduledPairs.All(group => group.Count() == 2), "each team pair should play twice");
Assert(scheduledPairs.All(group => group.Select(match => match.HomeTeamId).Distinct().Count() == 2), "each pair should swap home and away");

foreach (var teamId in scheduledLeague.Teams.Select(team => team.Id))
{
    var opponentsByWeek = scheduledSeason.Schedule
        .Where(match => match.HomeTeamId == teamId || match.AwayTeamId == teamId)
        .OrderBy(match => match.Week)
        .Select(match => match.HomeTeamId == teamId ? match.AwayTeamId : match.HomeTeamId)
        .ToArray();

    Assert(!opponentsByWeek.Zip(opponentsByWeek.Skip(1), (current, next) => current == next).Any(repeated => repeated), "teams should not play the same opponent twice in a row");
}

var firstHalfSequence = scheduledWeeks.Take(3).Select(group => string.Join(",", group.Select(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order())).Order())).ToArray();
var secondHalfSequence = scheduledWeeks.Skip(3).Select(group => string.Join(",", group.Select(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order())).Order())).ToArray();

Assert(!firstHalfSequence.SequenceEqual(secondHalfSequence), "second half schedule should not repeat the first-half sequence in the same order");

var matchService = new MatchService();
var match = matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var benchMatch = matchService.CreateHotseatMatch(ruleset, benchLeague.Teams[0], awayLeague.Teams[0]);
var depletedTeam = benchLeague.Teams[0] with { Players = benchLeague.Teams[0].Players.Take(3).ToArray() };
var depletedMatch = matchService.CreateHotseatMatch(ruleset, depletedTeam, awayLeague.Teams[0]);
var matchPath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-match.json");
await store.SaveMatchAsync(matchPath, match);
var loadedMatch = await store.LoadMatchAsync(matchPath);

Assert(benchMatch.Placements.Count == 23, "matches should accept teams with bench players");
Assert(depletedMatch.Placements.Count == 14, "matches should accept teams with the three-player minimum");
Assert(loadedMatch.HomeTeamId == loadedLeague.Teams[0].Id, "match home team should round-trip");
Assert(loadedMatch.AwayTeamId == awayLeague.Teams[0].Id, "match away team should round-trip");
Assert(loadedMatch.Phase == MatchPhase.DefenseSetup, "match should start with defense setup");
Assert(loadedMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should set up defense first");
Assert(loadedMatch.HomeTurn == 1 && loadedMatch.AwayTurn == 1, "both teams should start half one on turn one");
Assert(loadedMatch.FirstHalfReceivingTeamId == loadedLeague.Teams[0].Id, "home team should be recorded as the first-half receiving team");
Assert(loadedMatch.Placements.Count == 22, "match should place both teams in reserve");

var awayPlayerToPlace = awayLeague.Teams[0].Players[0];
var incompleteDefenseSetupMatch = matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(20, 5));
AssertThrows(
    () => matchService.AdvancePhase(incompleteDefenseSetupMatch, ruleset),
    "defense setup should require a complete legal formation before advancing");

var defenseSetupMatch = SetupTeam(matchService, loadedMatch, ruleset, awayLeague.Teams[0], [
    new(20, 5),
    new(13, 4),
    new(13, 5),
    new(13, 6),
    new(20, 4),
    new(20, 6),
    new(20, 7),
    new(20, 8),
    new(20, 9),
    new(20, 10),
    new(20, 11)
]);
var defensePlacedPlayer = defenseSetupMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(defensePlacedPlayer.State == PlayerPitchState.Standing, "defense player should stand on the pitch");
Assert(defensePlacedPlayer.Square == new PitchSquare(20, 5), "defense player should keep assigned square");

var knockedOutSetupMatch = loadedMatch with
{
    Placements = loadedMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { State = PlayerPitchState.KnockedOut }
            : placement)
        .ToArray()
};
AssertThrows(
    () => matchService.PlacePlayer(knockedOutSetupMatch, ruleset, awayPlayerToPlace.Id, new(20, 5)),
    "knocked out players should not be placeable during kickoff setup");

var offenseSetupMatch = matchService.AdvancePhase(defenseSetupMatch, ruleset);
Assert(offenseSetupMatch.Phase == MatchPhase.OffenseSetup, "defense setup should advance to offense setup");
Assert(offenseSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should set up offense");

var playerToPlace = loadedLeague.Teams[0].Players[0];
var noLineSetupMatch = SetupTeam(matchService, offenseSetupMatch, ruleset, loadedLeague.Teams[0], [
    new(0, 0),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10),
    new(1, 11),
    new(2, 4),
    new(2, 5)
]);
AssertThrows(
    () => matchService.AdvancePhase(noLineSetupMatch, ruleset),
    "offense setup should require three players on the line of scrimmage");

var placedMatch = SetupTeam(matchService, offenseSetupMatch, ruleset, loadedLeague.Teams[0], [
    new(0, 0),
    new(12, 4),
    new(12, 5),
    new(12, 6),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10)
]);
var placedPlayer = placedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(placedPlayer.State == PlayerPitchState.Standing, "offense player should stand on the pitch");
Assert(placedPlayer.Square == new PitchSquare(0, 0), "offense player should keep assigned square");

var kickoffMatch = matchService.AdvancePhase(placedMatch, ruleset);
Assert(kickoffMatch.Phase == MatchPhase.Kickoff, "offense setup should advance to kickoff");
Assert(matchService.AdvancePhase(kickoffMatch, ruleset).Phase == MatchPhase.Kickoff, "generic phase advance should not skip unresolved kickoff");

var kickoffService = new MatchService(new FixedDiceRoller(d6: [3, 4, 1], d8: [5]));
var offensiveTurnMatch = kickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(offensiveTurnMatch.Phase == MatchPhase.OffensivePlayerTurn, "kickoff should advance to offensive player turn");
Assert(offensiveTurnMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should have the offensive turn");
Assert(offensiveTurnMatch.Ball.Square == new PitchSquare(3, 2), "kickoff landing on empty square should leave loose ball");

var longKickoffScatterService = new MatchService(new FixedDiceRoller(d6: [3, 4, 3], d8: [5]));
var longKickoffScatterMatch = longKickoffScatterService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));

Assert(longKickoffScatterMatch.Ball.Square == new PitchSquare(5, 2), "kickoff scatter should move d6 squares in the d8 direction");

var caughtKickoffService = new MatchService(new FixedDiceRoller(d6: [3, 4, 1, 4], d8: [1]));
var caughtKickoffMatch = caughtKickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(1, 1));

Assert(caughtKickoffMatch.Ball.CarrierPlayerId == playerToPlace.Id, "kickoff landing on receiver should allow a catch");

var touchbackService = new MatchService(new FixedDiceRoller(d6: [3, 4, 1], d8: [5]));
var touchbackMatch = touchbackService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(ruleset.PitchWidth / 2, 0));

Assert(touchbackMatch.Ball.CarrierPlayerId == playerToPlace.Id, "kickoff outside receiving half should award touchback to receiving player");

var changingWeatherKickoffService = new MatchService(new FixedDiceRoller(d6: [4, 4, 3, 3, 1], d8: [5, 5]));
var changingWeatherKickoffMatch = changingWeatherKickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));

Assert(changingWeatherKickoffMatch.Weather == WeatherCondition.Nice, "changing weather kickoff event should update match weather");
Assert(changingWeatherKickoffMatch.Ball.Square == new PitchSquare(4, 2), "nice weather changing-weather event should add an extra gust scatter");
Assert(changingWeatherKickoffMatch.Log.Any(entry => entry.Message.Contains("Kickoff event roll 8", StringComparison.Ordinal)), "kickoff should log the table result");

var movedMatch = matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0));
var movedPlayer = movedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(movedPlayer.Square == new PitchSquare(3, 0), "moved player should keep destination square");
Assert(movedMatch.Activations.Count == 1, "movement should activate the player");

var pickupService = new MatchService(new FixedDiceRoller(d6: [2]));
var pickupMatch = pickupService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(pickupMatch.Ball.CarrierPlayerId == playerToPlace.Id, "moving over a loose ball should pick it up on a successful roll");
Assert(pickupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(3, 0), "successful pickup should allow movement to continue");

var rainPickupService = new MatchService(new FixedDiceRoller(d6: [2]));
var rainPickupMatch = rainPickupService.MovePlayer(
    offensiveTurnMatch with
    {
        Weather = WeatherCondition.PouringRain,
        Ball = new BallState { Square = new PitchSquare(2, 0) }
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(rainPickupMatch.PendingReroll?.Kind == PendingRerollKind.Pickup, "pouring rain should make a normal 2+ pickup need 3+");
Assert(rainPickupMatch.PendingReroll?.Target == 3, "pouring rain pickup target should be one worse");

var failedPickupService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedPickupMatch = failedPickupService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));
Assert(failedPickupMatch.PendingReroll?.Kind == PendingRerollKind.Pickup, "failed pickup should offer a pending reroll before resolving failure");
failedPickupMatch = failedPickupService.ResolvePendingReroll(failedPickupMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(failedPickupMatch.Phase == MatchPhase.DefensiveTurn, "failed pickup should cause a turnover if the moving team does not recover the bounce");
Assert(failedPickupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 0), "failed pickup should stop movement on the pickup square");
Assert(failedPickupMatch.Ball.Square == new PitchSquare(3, 0), "failed pickup should bounce the ball from the pickup square");

var outOfBoundsPickupService = new MatchService(new FixedDiceRoller(d6: [1, 3, 3, 3], d8: [1]));
var outOfBoundsPickupMatch = outOfBoundsPickupService.MovePlayer(
    offensiveTurnMatch with
    {
        Ball = new BallState { Square = new PitchSquare(0, 0) },
        Placements = offensiveTurnMatch.Placements
            .Select(placement => placement.PlayerId == playerToPlace.Id
                ? placement with { Square = new PitchSquare(1, 0), State = PlayerPitchState.Standing }
                : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(0, 0));
outOfBoundsPickupMatch = outOfBoundsPickupService.ResolvePendingReroll(outOfBoundsPickupMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(outOfBoundsPickupMatch.Ball.Square == new PitchSquare(6, 0), "out-of-bounds ball scatter should be thrown back in instead of clamped to the edge");

var noRerollPickupService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var noRerollPickupMatch = noRerollPickupService.MovePlayer(
    offensiveTurnMatch with { HomeRerollsRemaining = 0, Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(noRerollPickupMatch.PendingReroll is null, "failed pickup with no available rerolls should not create a pending reroll");
Assert(noRerollPickupMatch.Phase == MatchPhase.DefensiveTurn, "failed pickup with no available rerolls should resolve immediately");

var pickupRerollService = new MatchService(new FixedDiceRoller(d6: [1, 2]));
var pickupRerollPendingMatch = pickupRerollService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));
var pickupRerollMatch = pickupRerollService.ResolvePendingReroll(pickupRerollPendingMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: true);

Assert(pickupRerollMatch.PendingReroll is null, "successful team reroll should clear pending pickup reroll");
Assert(pickupRerollMatch.Ball.CarrierPlayerId == playerToPlace.Id, "successful pickup reroll should recover the ball");
Assert(pickupRerollMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls - 1, "team reroll should reduce remaining rerolls");

var touchdownReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(ruleset.PitchWidth - 2, 0), State = PlayerPitchState.Standing }
            : placement.PlayerId == loadedLeague.Teams[0].Players[1].Id
                ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                    : placement)
        .ToArray()
};
var touchdownService = new MatchService(new FixedDiceRoller(d6: [4, 3]));
var scoredMatch = touchdownService.MovePlayer(touchdownReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(ruleset.PitchWidth - 1, 0));

Assert(scoredMatch.HomeScore == 1, "home ball carrier should score in away end zone");
Assert(scoredMatch.AwayScore == 0, "away score should not change on home touchdown");
Assert(scoredMatch.Phase == MatchPhase.DefenseSetup, "touchdown should reset to defense placement");
Assert(scoredMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "scoring team should set up defense for the next drive");
Assert(scoredMatch.Ball.CarrierPlayerId is null && scoredMatch.Ball.Square is null, "touchdown should clear the ball");
Assert(scoredMatch.Placements.Any(placement => placement.TeamId == loadedLeague.Teams[0].Id && placement.State == PlayerPitchState.Reserve), "touchdown should reset available players to reserve");
Assert(scoredMatch.Placements.Single(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id).State == PlayerPitchState.Reserve, "touchdown should recover knocked out players on 4+");
Assert(scoredMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.KnockedOut, "touchdown should leave failed knockout recoveries knocked out");

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

Assert(failedPassMatch.Phase == MatchPhase.DefensiveTurn, "fumbled pass should cause a turnover");
Assert(failedPassMatch.Ball.CarrierPlayerId is null, "fumbled pass should leave the ball loose if not recovered");
Assert(failedPassMatch.Ball.Square == new PitchSquare(2, 1), "fumbled pass should bounce from the passer");

var emptyTargetPassService = new MatchService(new FixedDiceRoller(d6: [2]));
var emptyTargetPassMatch = emptyTargetPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, new PitchSquare(4, 2));

Assert(emptyTargetPassMatch.Phase == MatchPhase.DefensiveTurn, "accurate pass to an empty square should cause a turnover if not recovered");
Assert(emptyTargetPassMatch.Ball.Square == new PitchSquare(4, 2), "accurate pass to an empty square should land on the target square");

var sunnyPassService = new MatchService(new FixedDiceRoller(d6: [2], d8: [5]));
var sunnyPassMatch = sunnyPassService.PassBall(
    passReadyMatch with { Weather = WeatherCondition.VerySunny },
    ruleset,
    loadedLeague.Teams[0],
    passerPlayer.Id,
    passReceiver.Id);

Assert(sunnyPassMatch.Phase == MatchPhase.DefensiveTurn, "very sunny weather should make a normal 2+ pass need 3+");
Assert(sunnyPassMatch.Ball.Square == new PitchSquare(5, 1), "failed sunny pass should scatter from the receiver");

var markedPasserMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(1, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedPasserService = new MatchService(new FixedDiceRoller(d6: [2], d8: [5]));
var markedPasserResult = markedPasserService.PassBall(markedPasserMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(markedPasserResult.Phase == MatchPhase.DefensiveTurn, "opposing tackle zones on the passer should make passing harder");
Assert(markedPasserResult.Ball.Square == new PitchSquare(5, 1), "marked passer inaccurate pass should scatter from the target square");

var droppedPassService = new MatchService(new FixedDiceRoller(d6: [2, 1], d8: [5]));
var droppedPassMatch = droppedPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(droppedPassMatch.Phase == MatchPhase.DefensiveTurn, "dropped completed pass should cause a turnover if not recovered");
Assert(droppedPassMatch.Ball.Square == new PitchSquare(5, 1), "dropped pass should bounce from the receiver");

var markedReceiverMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(4, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedReceiverService = new MatchService(new FixedDiceRoller(d6: [2, 3], d8: [5]));
var markedReceiverResult = markedReceiverService.PassBall(markedReceiverMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(markedReceiverResult.Phase == MatchPhase.DefensiveTurn, "opposing tackle zones on the receiver should make catching harder");
Assert(markedReceiverResult.Ball.Square == new PitchSquare(5, 1), "marked receiver dropped pass should bounce from the receiver");

var passBounceReceiver = loadedLeague.Teams[0].Players[1];
var friendlyPassBounceMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == passBounceReceiver.Id
            ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var friendlyPassBounceService = new MatchService(new FixedDiceRoller(d6: [2, 4], d8: [5]));
var friendlyPassBounceResult = friendlyPassBounceService.PassBall(friendlyPassBounceMatch with { Weather = WeatherCondition.VerySunny }, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

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
var interceptionService = new MatchService(new FixedDiceRoller(d6: [3, 6]));
var interceptedPassMatch = interceptionService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(interceptedPassMatch.Phase == MatchPhase.DefensiveTurn, "successful interception should cause a turnover");
Assert(interceptedPassMatch.ActiveTeamId == awayLeague.Teams[0].Id, "intercepting team should become active after turnover");
Assert(interceptedPassMatch.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "interceptor should carry the ball");

var markedInterceptionService = new MatchService(new FixedDiceRoller(d6: [3, 5, 4]));
var markedInterceptionResult = markedInterceptionService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(markedInterceptionResult.Ball.CarrierPlayerId == passReceiver.Id, "opposing tackle zones on the interceptor should make interception harder");

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
var pendingInterceptionService = new MatchService(new FixedDiceRoller(d6: [3, 1, 4]));
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

Assert(blockMatch.PendingPush?.KnockDefenderDown == true, "successful block should ask for a push square before knocking the defender down");
blockMatch = blockService.ChoosePushSquare(blockMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
var blockedPlayer = blockMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(blockedPlayer.State == PlayerPitchState.Prone, "successful block should knock defender down after the push");
Assert(blockedPlayer.Square == new PitchSquare(3, 1), "successful block should push the defender before knockdown");
Assert(blockMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Block, "block should activate the attacker");

var pushBlockService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingPushBlock = pushBlockService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(pendingPushBlock.PendingPush?.KnockDefenderDown == false, "push result should ask for a push square without knockdown");
AssertThrows(
    () => pushBlockService.AdvanceTurn(pendingPushBlock, ruleset),
    "pending push should block turn advancement");
var pushedBlock = pushBlockService.ChoosePushSquare(pendingPushBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
var pushedDefender = pushedBlock.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(pushedDefender.State == PlayerPitchState.Standing, "push result should leave the defender standing");
Assert(pushedDefender.Square == new PitchSquare(3, 1), "push result should move the defender to the chosen square");

var chainPushedPlayer = awayLeague.Teams[0].Players[1];
var secondChainPushedPlayer = awayLeague.Teams[0].Players[2];
var thirdChainPushedPlayer = awayLeague.Teams[0].Players[3];
var chainPushMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == chainPushedPlayer.Id
                    ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                    : placement.PlayerId == secondChainPushedPlayer.Id
                        ? placement with { Square = new PitchSquare(3, 0), State = PlayerPitchState.Standing }
                        : placement.PlayerId == thirdChainPushedPlayer.Id
                            ? placement with { Square = new PitchSquare(3, 2), State = PlayerPitchState.Standing }
                    : placement)
        .ToArray()
};
var chainPushService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingChainPush = chainPushService.BlockPlayer(chainPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(pendingChainPush.PendingPush?.LegalSquares.Contains(new PitchSquare(3, 1)) == true, "occupied push squares should be legal only when no unoccupied on-pitch push square exists");
var chainPushResult = chainPushService.ChoosePushSquare(pendingChainPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));

Assert(chainPushResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(3, 1), "push into an occupied square should move the original pushed player there");
Assert(chainPushResult.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(4, 0), "push into an occupied square should chain-push the occupying player");

var fourthChainPushedPlayer = awayLeague.Teams[0].Players[4];
var cascadePushMatch = chainPushMatch with
{
    Placements = chainPushMatch.Placements
        .Select(placement => placement.PlayerId == fourthChainPushedPlayer.Id
            ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var cascadePushService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingCascadePush = cascadePushService.BlockPlayer(cascadePushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
var cascadePushResult = cascadePushService.ChoosePushSquare(pendingCascadePush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));

Assert(cascadePushResult.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(4, 0), "cascade chain push should force the second pushed player into an unoccupied square before occupied options");
Assert(cascadePushResult.Placements.Single(placement => placement.PlayerId == fourthChainPushedPlayer.Id).Square == new PitchSquare(4, 1), "cascade chain push should not push a third player while the second player has an unoccupied destination");

var emptyPreferredPushMatch = chainPushMatch with
{
    Placements = chainPushMatch.Placements
        .Select(placement => placement.PlayerId == secondChainPushedPlayer.Id
            ? placement with { Square = null, State = PlayerPitchState.Reserve }
            : placement)
        .ToArray()
};
var emptyPreferredPushService = new MatchService(new FixedDiceRoller(d6: [3]));
var emptyPreferredPush = emptyPreferredPushService.BlockPlayer(emptyPreferredPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(emptyPreferredPush.PendingPush is null, "single unoccupied legal push square should resolve automatically instead of offering occupied chain-push squares");
Assert(emptyPreferredPush.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(3, 0), "unoccupied push square must be chosen before occupied chain-push squares");
Assert(emptyPreferredPush.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(3, 1), "occupied chain-push square should not be used while an unoccupied push square exists");

var crowdPushMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var crowdPushService = new MatchService(new FixedDiceRoller(d6: [3, 1, 2]));
var crowdPushResult = crowdPushService.BlockPlayer(crowdPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
var crowdedPlayer = crowdPushResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(crowdedPlayer.Square is null, "sideline push with no legal on-pitch destination should push the player off the pitch");
Assert(crowdedPlayer.State == PlayerPitchState.Reserve, "crowd push with no lasting injury should put the player in reserve");

var bothDownService = new MatchService(new FixedDiceRoller(d6: [2, 1, 1, 1, 1], d8: [5]));
var bothDownMatch = bothDownService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(bothDownMatch.Phase == MatchPhase.DefensiveTurn, "both-down block should cause a turnover");
Assert(bothDownMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "both-down block should knock the attacker down");
Assert(bothDownMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "both-down block should knock the defender down");

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
Assert(badBlockAttacker.State == PlayerPitchState.Casualty, "attacker-down injury roll of 10+ should injure the player");
Assert(badBlockAttacker.Casualty?.Roll == 1 && badBlockAttacker.Casualty.Result == CasualtyResult.BadlyHurt, "injury roll of 10+ should immediately roll on the casualty table");
Assert(badBlockMatch.Ball.Square == new PitchSquare(2, 1), "attacker-down ball carrier should scatter the ball");

var deathBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d16: [16]));
var deathBlockMatch = deathBlockService.BlockPlayer(
    blockReadyMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    awayLeague.Teams[0],
    awayPlayerToPlace.Id);
var deadAttacker = deathBlockMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(deadAttacker.State == PlayerPitchState.Dead, "dead should come from the casualty table rather than the injury roll");
Assert(deadAttacker.Casualty?.Result == CasualtyResult.Dead, "casualty roll of 15-16 should be dead");

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
Assert(blitzMatch.PendingPush?.KnockDefenderDown == true, "blitz should ask for a push square before block knockdown");
blitzMatch = blitzService.ChoosePushSquare(blitzMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(4, 1));

Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "blitz should move the attacker");
Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "blitz should resolve the block");

var failedMoveBlitzMatch = blitzReadyMatch with
{
    Placements = blitzReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(10, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var failedMoveBlitzService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedMoveBlitzResult = failedMoveBlitzService.BlitzPlayer(failedMoveBlitzMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(9, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(failedMoveBlitzResult.PendingReroll?.Kind == PendingRerollKind.GoForIt, "failed blitz movement should pause before resolving the failed movement roll");
Assert(failedMoveBlitzResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Blitz, "failed blitz movement should still spend the blitz activation");
Assert(failedMoveBlitzResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Standing, "failed blitz movement should not resolve the block");

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
Assert(assistedBlockResult.PendingPush is not null, "chosen favorable block die should ask for a push square");
assistedBlockResult = assistedBlockService.ChoosePushSquare(assistedBlockResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
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

weakBlockResult = weakBlockService.ChoosePushSquare(weakBlockResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(weakBlockResult.Placements.Single(placement => placement.PlayerId == awayLeague.Teams[0].Players[3].Id).State == PlayerPitchState.Casualty, "chosen high block die with injury roll of 10+ should injure the defender");
Assert(weakBlockResult.Placements.Single(placement => placement.PlayerId == awayLeague.Teams[0].Players[3].Id).Casualty?.Result == CasualtyResult.BadlyHurt, "casualty details should be stored on injured players");

var foulReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Prone }
                : placement)
        .ToArray()
};
var foulService = new MatchService(new FixedDiceRoller(d6: [5, 6, 3, 4]));
var foulMatch = foulService.FoulPlayer(foulReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(foulMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Foul, "foul should activate the fouler");
Assert(foulMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "foul armor break should resolve injury against the victim");
Assert(foulMatch.Phase == MatchPhase.OffensivePlayerTurn, "foul without doubles should not cause a turnover");

AssertThrows(
    () => foulService.FoulPlayer(foulMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "foul should be limited to once per team turn");

var sentOffFoulService = new MatchService(new FixedDiceRoller(d6: [6, 6, 4, 5]));
var sentOffFoulMatch = sentOffFoulService.FoulPlayer(foulReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(sentOffFoulMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.SentOff, "doubles on a foul should send off the fouler");
Assert(sentOffFoulMatch.Phase == MatchPhase.DefensiveTurn, "send-off on a foul should cause a turnover");

var goForItService = new MatchService(new FixedDiceRoller(d6: [2]));
var goForItMatch = goForItService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(7, 0));
var goForItActivation = goForItMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(goForItActivation.GoForItsUsed == 1, "movement past MA should spend go-for-its");
Assert(goForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(7, 0), "successful go-for-it should move the player");

var blizzardGoForItService = new MatchService(new FixedDiceRoller(d6: [2]));
var blizzardGoForItMatch = blizzardGoForItService.MovePlayer(
    offensiveTurnMatch with { Weather = WeatherCondition.Blizzard },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));

Assert(blizzardGoForItMatch.PendingReroll?.Kind == PendingRerollKind.GoForIt, "blizzard should make a normal 2+ go-for-it need 3+");
Assert(blizzardGoForItMatch.PendingReroll?.Target == 3, "blizzard go-for-it target should be 3+");

var proneMoveReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 0), State = PlayerPitchState.Prone }
            : placement)
        .ToArray()
};
var standOnlyMatch = matchService.MovePlayer(proneMoveReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 0));
var stoodPlayer = standOnlyMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(stoodPlayer.State == PlayerPitchState.Standing, "move action should allow a prone player to stand up");
Assert(stoodPlayer.Square == new PitchSquare(1, 0), "standing up without moving should keep the player in place");
Assert(standOnlyMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Move, "standing up should spend a move activation");

var proneMoveService = new MatchService(new FixedDiceRoller(d6: [2]));
var stoodAndMovedMatch = proneMoveService.MovePlayer(proneMoveReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(5, 0));
var stoodAndMovedActivation = stoodAndMovedMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(stoodAndMovedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(5, 0), "standing up should still allow movement with reduced allowance");
Assert(stoodAndMovedActivation.GoForItsUsed == 1, "standing up should reduce movement allowance before go-for-its");

var proneBlitzReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Prone }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var proneBlitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var proneBlitzMatch = proneBlitzService.BlitzPlayer(proneBlitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(proneBlitzMatch.PendingPush?.KnockDefenderDown == true, "prone adjacent blitz should stand up and resolve the block");
Assert(proneBlitzMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Standing, "prone blitz should stand the attacker up");

var failedGoForItService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var failedGoForItMatch = failedGoForItService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
Assert(failedGoForItMatch.PendingReroll?.Kind == PendingRerollKind.GoForIt, "failed go-for-it should offer a pending reroll before resolving failure");
failedGoForItMatch = failedGoForItService.ResolvePendingReroll(failedGoForItMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
var failedGoForItPlayer = failedGoForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(failedGoForItMatch.Phase == MatchPhase.DefensiveTurn, "failed offensive go-for-it should cause a turnover to defensive turn");
Assert(failedGoForItMatch.ActiveTeamId == awayLeague.Teams[0].Id, "failed offensive go-for-it should activate defense");
Assert(failedGoForItMatch.PendingBlock is null && failedGoForItMatch.PendingPush is null && failedGoForItMatch.PendingInterception is null && failedGoForItMatch.PendingReroll is null, "turnover cleanup should clear pending choices");
Assert(failedGoForItPlayer.State == PlayerPitchState.Casualty, "failed go-for-it injury roll of 10+ should injure the player");
Assert(failedGoForItMatch.Ball.CarrierPlayerId is null, "failed ball carrier go-for-it should drop the ball");
Assert(failedGoForItMatch.Ball.Square == new PitchSquare(8, 0), "failed ball carrier go-for-it should scatter the ball");

var dodgeReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var dodgeService = new MatchService(new FixedDiceRoller(d6: [3]));
var dodgedMatch = dodgeService.MovePlayer(dodgeReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 2));

Assert(dodgedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "successful dodge should move the player");
Assert(dodgedMatch.Phase == MatchPhase.OffensivePlayerTurn, "successful dodge should not cause a turnover");

var twoTackleZoneDodgeMatch = dodgeReadyMatch with
{
    Placements = dodgeReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(1, 3), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedDodgeService = new MatchService(new FixedDiceRoller(d6: [3, 6, 6], d8: [5]));
var markedDodgeMatch = markedDodgeService.MovePlayer(twoTackleZoneDodgeMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 2));
Assert(markedDodgeMatch.PendingReroll?.Kind == PendingRerollKind.Dodge, "failed marked dodge should offer a pending reroll");
markedDodgeMatch = markedDodgeService.ResolvePendingReroll(markedDodgeMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(markedDodgeMatch.Phase == MatchPhase.DefensiveTurn, "dodging into two opposing tackle zones should need worse than a 3+");
Assert(markedDodgeMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State != PlayerPitchState.Standing, "failed marked dodge should knock the player down");

var failedDodgeService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var failedDodgeMatch = failedDodgeService.MovePlayer(
    dodgeReadyMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(1, 2));
Assert(failedDodgeMatch.PendingReroll?.Kind == PendingRerollKind.Dodge, "failed dodge should offer a pending reroll before resolving failure");
failedDodgeMatch = failedDodgeService.ResolvePendingReroll(failedDodgeMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
var failedDodgePlayer = failedDodgeMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(failedDodgeMatch.Phase == MatchPhase.DefensiveTurn, "failed dodge should cause a turnover");
Assert(failedDodgePlayer.State == PlayerPitchState.Casualty, "failed dodge injury roll of 10+ should injure the player");
Assert(failedDodgePlayer.Square is null, "injured player from failed dodge should be removed from the pitch");
Assert(failedDodgeMatch.Ball.Square == new PitchSquare(2, 2), "failed dodge by ball carrier should scatter the ball");

var defensiveTurnMatch = matchService.AdvancePhase(movedMatch);
Assert(defensiveTurnMatch.Phase == MatchPhase.DefensiveTurn, "offensive player turn should advance to defensive turn");
Assert(defensiveTurnMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should have the defensive turn");
Assert(defensiveTurnMatch.HomeTurn == 2 && defensiveTurnMatch.AwayTurn == 1, "ending the offensive turn should consume home turn one");
Assert(defensiveTurnMatch.Turn == 1, "defensive turn should use the active team's turn counter");

var rulesetAwareDefensiveTurnMatch = matchService.AdvanceTurn(movedMatch, ruleset);
Assert(rulesetAwareDefensiveTurnMatch.Phase == MatchPhase.DefensiveTurn, "ruleset-aware turn control should end the offensive player turn");
Assert(rulesetAwareDefensiveTurnMatch.HomeTurn == 2 && rulesetAwareDefensiveTurnMatch.AwayTurn == 1, "ruleset-aware offensive turn end should consume the active team's turn");

var stunnedHomeTurnMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { State = PlayerPitchState.Stunned, StunnedRecoveryHalf = 1, StunnedRecoveryTurn = 2 }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { State = PlayerPitchState.Stunned, StunnedRecoveryHalf = 1, StunnedRecoveryTurn = 1 }
                : placement)
        .ToArray()
};
var stunnedRecoveryMatch = matchService.AdvanceTurn(stunnedHomeTurnMatch, ruleset);

Assert(stunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Stunned, "a player stunned during their own turn should not recover at the end of that same turn");
Assert(stunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "ending a team turn should not recover the opposing team's stunned players");

var nextHomeTurnWithStunnedPlayer = stunnedRecoveryMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = loadedLeague.Teams[0].Id,
    Turn = 2
};
var stunnedRecoveredAfterFullTurn = matchService.AdvanceTurn(nextHomeTurnWithStunnedPlayer, ruleset);
Assert(stunnedRecoveredAfterFullTurn.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "a stunned player should recover to prone after spending their next own team turn stunned");

var awayStunnedRecoveryMatch = matchService.AdvanceTurn(stunnedRecoveryMatch, ruleset);
Assert(awayStunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "a player stunned during the opponent turn should recover after their upcoming own turn");

var defensiveMoveMatch = matchService.MovePlayer(defensiveTurnMatch, ruleset, awayLeague.Teams[0], awayPlayerToPlace.Id, new(19, 5));
var defensiveMovedPlayer = defensiveMoveMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);
Assert(defensiveMovedPlayer.Square == new PitchSquare(19, 5), "defensive player should move during defensive turn");

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
var knockoutHalftimeMatch = lastFirstHalfDefensiveTurn with
{
    HomeRerollsRemaining = 0,
    AwayRerollsRemaining = 0,
    Placements = lastFirstHalfDefensiveTurn.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                : placement)
        .ToArray()
};
var halftimeService = new MatchService(new FixedDiceRoller(d6: [4, 2]));
var secondHalfSetupMatch = halftimeService.AdvanceTurn(knockoutHalftimeMatch, ruleset);

Assert(secondHalfSetupMatch.Half == 2, "both teams finishing eight turns should advance to the second half");
Assert(secondHalfSetupMatch.HomeTurn == 1 && secondHalfSetupMatch.AwayTurn == 1, "second half should reset both team turn counters");
Assert(secondHalfSetupMatch.Phase == MatchPhase.DefenseSetup, "second half should begin with defense placement");
Assert(secondHalfSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "first-half receiving team should kick off to start the second half");
Assert(secondHalfSetupMatch.Ball.CarrierPlayerId is null && secondHalfSetupMatch.Ball.Square is null, "halftime should clear the ball");
Assert(secondHalfSetupMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls && secondHalfSetupMatch.AwayRerollsRemaining == awayLeague.Teams[0].Rerolls, "halftime should refresh both teams' rerolls");
Assert(secondHalfSetupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Reserve, "halftime should recover knocked out players on 4+");
Assert(secondHalfSetupMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.KnockedOut, "halftime should leave failed knockout recoveries knocked out");

var lastFirstHalfOffensiveTurn = offensiveTurnMatch with
{
    Half = 1,
    HomeTurn = ruleset.TurnsPerHalf,
    AwayTurn = ruleset.TurnsPerHalf + 1,
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = loadedLeague.Teams[0].Id,
    FirstHalfReceivingTeamId = loadedLeague.Teams[0].Id
};
var secondHalfFromOffensiveEnd = matchService.AdvanceTurn(lastFirstHalfOffensiveTurn, ruleset);

Assert(secondHalfFromOffensiveEnd.Half == 2, "ruleset-aware offensive turn end should advance the half when both teams are done");
Assert(secondHalfFromOffensiveEnd.Phase == MatchPhase.DefenseSetup, "ruleset-aware offensive turn end should begin second-half setup when the half ends");

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
        "Too Many Players",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 17).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: 0),
    "drafts above sixteen players should fail");

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
    () => leagueService.CreateLeague("Too Small", ruleset, [rosterSet], targetTeamCount: 1),
    "leagues should require at least two teams");

AssertThrows(
    () => leagueService.CreateLeague("Odd League", ruleset, [rosterSet], targetTeamCount: 3),
    "league scheduling should require an even number of teams");

AssertThrows(
    () => matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0]),
    "matches should require two different teams");

AssertThrows(
    () => matchService.CreateHotseatMatch(ruleset, depletedTeam with { Players = depletedTeam.Players.Take(2).ToArray() }, awayLeague.Teams[0]),
    "matches should require at least three players");

AssertThrows(
    () => matchService.PlacePlayer(defenseSetupMatch, ruleset, awayLeague.Teams[0].Players[1].Id, new(20, 5)),
    "placement should reject occupied squares");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(5, 5)),
    "defense placement should reject the wrong side of the pitch");

var wideZoneLimitMatch = matchService.PlacePlayer(
    matchService.PlacePlayer(loadedMatch, ruleset, awayLeague.Teams[0].Players[0].Id, new(13, 0)),
    ruleset,
    awayLeague.Teams[0].Players[1].Id,
    new(14, 0));

AssertThrows(
    () => matchService.PlacePlayer(wideZoneLimitMatch, ruleset, awayLeague.Teams[0].Players[2].Id, new(15, 1)),
    "setup should reject more than two players in the same wide zone");

var benchDefenseSetup = SetupTeam(matchService, benchMatch, ruleset, awayLeague.Teams[0], [
    new(20, 5),
    new(13, 4),
    new(13, 5),
    new(13, 6),
    new(20, 4),
    new(20, 6),
    new(20, 7),
    new(20, 8),
    new(20, 9),
    new(20, 10),
    new(20, 11)
]);
var benchOffenseSetup = matchService.AdvancePhase(benchDefenseSetup, ruleset);
var elevenBenchPlayersSetup = SetupTeam(matchService, benchOffenseSetup, ruleset, benchLeague.Teams[0], [
    new(0, 0),
    new(12, 4),
    new(12, 5),
    new(12, 6),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10)
]);
AssertThrows(
    () => matchService.PlacePlayer(elevenBenchPlayersSetup, ruleset, benchLeague.Teams[0].Players[11].Id, new(2, 11)),
    "setup should reject placing a twelfth player");

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
    () => matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(20, 5)),
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
        new(19, 5)),
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

static MatchState SetupTeam(MatchService matchService, MatchState match, Ruleset ruleset, LeagueTeam team, IReadOnlyList<PitchSquare> squares)
{
    var next = match;
    for (var index = 0; index < squares.Count; index++)
    {
        next = matchService.PlacePlayer(next, ruleset, team.Players[index].Id, squares[index]);
    }

    return next;
}

public sealed class FixedDiceRoller : IDiceRoller
{
    private readonly Queue<int> _d6;
    private readonly Queue<int> _d8;
    private readonly Queue<int> _d16;

    public FixedDiceRoller(IEnumerable<int>? d6 = null, IEnumerable<int>? d8 = null, IEnumerable<int>? d16 = null)
    {
        _d6 = new Queue<int>(d6 ?? [6]);
        _d8 = new Queue<int>(d8 ?? [1]);
        _d16 = new Queue<int>(d16 ?? [1]);
    }

    public int RollD6()
    {
        return _d6.Count > 0 ? _d6.Dequeue() : 6;
    }

    public int RollD8()
    {
        return _d8.Count > 0 ? _d8.Dequeue() : 1;
    }

    public int RollD16()
    {
        return _d16.Count > 0 ? _d16.Dequeue() : 1;
    }
}
