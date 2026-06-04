using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class LeagueService
{
    private const int MaximumRosterPlayers = 16;
    private const int FanFactorCost = 10_000;
    private const int CheerleaderCost = 10_000;
    private const int AssistantCoachCost = 10_000;
    private const int ApothecaryCost = 50_000;
    private const int BaseWinnings = 30_000;
    private const int TouchdownWinnings = 10_000;
    private const int WinBonusWinnings = 10_000;
    private const int TieBonusWinnings = 5_000;
    private const int FanFactorWinnings = 5_000;

    public League CreateLeague(string name, Ruleset ruleset, IEnumerable<RosterSet> rosterSets, int targetTeamCount = 2)
    {
        var rosterSetIds = rosterSets.Select(set => set.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (rosterSetIds.Length == 0)
        {
            throw new InvalidOperationException("At least one roster set is required to create a league.");
        }

        if (targetTeamCount < 2)
        {
            throw new InvalidOperationException("A league must have at least two teams.");
        }

        if (targetTeamCount % 2 != 0)
        {
            throw new InvalidOperationException("League scheduling currently requires an even number of teams.");
        }

        return new League
        {
            Id = Guid.NewGuid(),
            Name = RequireText(name, "League name is required."),
            RulesetId = ruleset.Id,
            TargetTeamCount = targetTeamCount,
            RosterSetIds = rosterSetIds,
            Settings = new LeagueSettings()
        };
    }

    public League AddTeam(
        League league,
        Ruleset ruleset,
        string teamName,
        string coachName,
        TeamRoster roster,
        IEnumerable<PlayerDraftPick> draft,
        int rerolls = 0,
        int fanFactor = 1,
        int cheerleaders = 0,
        int assistantCoaches = 0,
        int apothecaries = 0)
    {
        if (!league.RosterSetIds.Any())
        {
            throw new InvalidOperationException("League has no roster sets configured.");
        }

        var players = draft.Select(pick => CreatePlayer(roster, pick)).ToArray();
        ValidateDraft(roster, players);

        if (players.Length < ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; ruleset requires at least {ruleset.PlayersPerSide}.");
        }

        if (players.Length > MaximumRosterPlayers)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; rosters can include no more than {MaximumRosterPlayers}.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        if (fanFactor < 1)
        {
            throw new InvalidOperationException("Fan factor must be at least 1.");
        }

        ValidateStaff(cheerleaders, assistantCoaches, apothecaries);

        var team = BuildTeam(Guid.NewGuid(), ruleset, teamName, coachName, roster, players, rerolls, fanFactor, cheerleaders, assistantCoaches, apothecaries);

        return league with { Teams = [.. league.Teams, team] };
    }

    public League CreateSeason(League league, string seasonName = "Season 1")
    {
        if (league.Teams.Count != league.TargetTeamCount)
        {
            throw new InvalidOperationException($"League has {league.Teams.Count} teams; expected {league.TargetTeamCount}.");
        }

        if (league.Teams.Count < 2 || league.Teams.Count % 2 != 0)
        {
            throw new InvalidOperationException("League scheduling requires an even number of teams.");
        }

        if (league.Seasons.Any())
        {
            return league;
        }

        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = seasonName,
            CurrentWeek = 1,
            Schedule = CreateDoubleRoundRobinSchedule(league.Teams)
        };

        return league with { Seasons = [season] };
    }

    public League UpdateTeam(
        League league,
        Ruleset ruleset,
        Guid teamId,
        string teamName,
        string coachName,
        TeamRoster roster,
        IEnumerable<PlayerDraftPick> draft,
        int rerolls = 0,
        int fanFactor = 1,
        int cheerleaders = 0,
        int assistantCoaches = 0,
        int apothecaries = 0)
    {
        if (!league.Teams.Any(team => team.Id == teamId))
        {
            throw new InvalidOperationException("Team is not part of this league.");
        }

        var players = draft.Select(pick => CreatePlayer(roster, pick)).ToArray();
        ValidateDraft(roster, players);

        if (players.Length < ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; ruleset requires at least {ruleset.PlayersPerSide}.");
        }

        if (players.Length > MaximumRosterPlayers)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; rosters can include no more than {MaximumRosterPlayers}.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        if (fanFactor < 1)
        {
            throw new InvalidOperationException("Fan factor must be at least 1.");
        }

        ValidateStaff(cheerleaders, assistantCoaches, apothecaries);

        var updatedTeam = BuildTeam(teamId, ruleset, teamName, coachName, roster, players, rerolls, fanFactor, cheerleaders, assistantCoaches, apothecaries);

        return league with
        {
            Teams = league.Teams
                .Select(team => team.Id == teamId ? updatedTeam : team)
                .ToArray()
        };
    }

    public League ApplyMatchCasualties(League league, MatchState match)
    {
        var casualtyPlacements = match.Placements
            .Where(placement => placement.Casualty is not null || placement.State == PlayerPitchState.Dead)
            .ToArray();
        if (casualtyPlacements.Length == 0)
        {
            return league;
        }

        var teams = league.Teams
            .Select(team => ApplyTeamCasualties(team, casualtyPlacements.Where(placement => placement.TeamId == team.Id).ToArray()))
            .ToArray();

        teams = ApplyPlagueRidden(teams, casualtyPlacements);

        return league with { Teams = teams };
    }

    public League CompleteScheduledMatch(League league, Guid scheduledMatchId, MatchState match)
    {
        if (match.Phase != MatchPhase.Complete)
        {
            throw new InvalidOperationException("Only completed matches can be applied to a league.");
        }

        var season = league.Seasons.FirstOrDefault(current => current.Schedule.Any(scheduled => scheduled.Id == scheduledMatchId))
            ?? throw new InvalidOperationException("Scheduled match is not part of this league.");
        var scheduledMatch = season.Schedule.First(scheduled => scheduled.Id == scheduledMatchId);
        if (scheduledMatch.Result is not null)
        {
            return league;
        }

        if (scheduledMatch.HomeTeamId != match.HomeTeamId || scheduledMatch.AwayTeamId != match.AwayTeamId)
        {
            throw new InvalidOperationException("Completed match teams do not match the scheduled match.");
        }

        var homeTeam = league.Teams.FirstOrDefault(team => team.Id == match.HomeTeamId)
            ?? throw new InvalidOperationException("Home team is not part of this league.");
        var awayTeam = league.Teams.FirstOrDefault(team => team.Id == match.AwayTeamId)
            ?? throw new InvalidOperationException("Away team is not part of this league.");
        var awards = AppendMostValuablePlayerAwards(EnrichPlayerAwards(match.PlayerAwards, match, homeTeam, awayTeam), homeTeam, awayTeam);
        var homeWinnings = CalculateWinnings(homeTeam, match.HomeScore, match.AwayScore);
        var awayWinnings = CalculateWinnings(awayTeam, match.AwayScore, match.HomeScore);
        var result = new MatchResult
        {
            HomeScore = match.HomeScore,
            AwayScore = match.AwayScore,
            HomeWinnings = homeWinnings,
            AwayWinnings = awayWinnings,
            PlayerAwards = awards
        };

        var withResult = league with
        {
            Seasons = league.Seasons
                .Select(currentSeason => currentSeason.Id == season.Id
                    ? currentSeason with
                    {
                        Schedule = currentSeason.Schedule
                            .Select(scheduled => scheduled.Id == scheduledMatchId ? scheduled with { Result = result } : scheduled)
                            .ToArray()
                    }
                    : currentSeason)
                .ToArray()
        };

        var afterCasualties = ApplyMatchCasualties(ReleaseMissNextGamePlayers(withResult, match), match);
        var updatedTeams = afterCasualties.Teams
            .Select(team => team.Id == match.HomeTeamId
                ? ApplyPostMatchTeamUpdates(team, awards, homeWinnings, match.HomeTreasurySpent, match.HomeScore, match.AwayScore)
                : team.Id == match.AwayTeamId
                    ? ApplyPostMatchTeamUpdates(team, awards, awayWinnings, match.AwayTreasurySpent, match.AwayScore, match.HomeScore)
                    : team)
            .ToArray();

        return AdvanceSeasonWeek(afterCasualties with { Teams = updatedTeams }, season.Id);
    }

    public League PurchaseSelectedSkillAdvancement(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, Guid playerId, string skillId)
    {
        var skill = ruleset.Skills.FirstOrDefault(current => string.Equals(current.Id, skillId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Ruleset does not define skill '{skillId}'.");
        return PurchaseSkillAdvancement(league, ruleset, roster, teamId, playerId, skill);
    }

    public League PurchaseRandomSkillAdvancement(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, Guid playerId, bool secondary = false)
    {
        var team = league.Teams.FirstOrDefault(current => current.Id == teamId)
            ?? throw new InvalidOperationException("Team is not part of this league.");
        var player = team.Players.FirstOrDefault(current => current.Id == playerId)
            ?? throw new InvalidOperationException("Player is not part of this team.");
        var position = FindPosition(roster, player.PositionId);
        var categories = secondary ? position.SecondarySkillCategories : position.PrimarySkillCategories;
        var skill = ruleset.Skills
            .Where(current => categories.Contains(current.Category, StringComparer.OrdinalIgnoreCase))
            .Where(current => !player.Skills.Contains(current.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(current => current.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No eligible random skill is available.");

        return PurchaseSkillAdvancement(league, ruleset, roster, teamId, playerId, skill);
    }

    private static LeagueTeam BuildTeam(
        Guid teamId,
        Ruleset ruleset,
        string teamName,
        string coachName,
        TeamRoster roster,
        IReadOnlyList<Player> players,
        int rerolls,
        int fanFactor,
        int cheerleaders,
        int assistantCoaches,
        int apothecaries)
    {
        var playerCost = players.Sum(player => FindPosition(roster, player.PositionId).Cost);
        var rerollCost = rerolls * roster.RerollCost;
        var fanFactorCost = Math.Max(0, fanFactor - 1) * FanFactorCost;
        var staffCost = (cheerleaders * CheerleaderCost) + (assistantCoaches * AssistantCoachCost) + (apothecaries * ApothecaryCost);
        var totalCost = playerCost + rerollCost + fanFactorCost + staffCost;

        if (totalCost > ruleset.StartingTreasury)
        {
            throw new InvalidOperationException($"Team cost {totalCost} exceeds starting treasury {ruleset.StartingTreasury}.");
        }

        return new LeagueTeam
        {
            Id = teamId,
            Name = RequireText(teamName, "Team name is required."),
            CoachName = string.IsNullOrWhiteSpace(coachName) ? "Solo Coach" : coachName,
            RosterId = roster.Id,
            Treasury = ruleset.StartingTreasury - totalCost,
            TeamValue = totalCost,
            Rerolls = rerolls,
            FanFactor = fanFactor,
            Cheerleaders = cheerleaders,
            AssistantCoaches = assistantCoaches,
            Apothecaries = apothecaries,
            Players = players
        };
    }

    private static League PurchaseSkillAdvancement(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, Guid playerId, SkillDefinition skill)
    {
        var team = league.Teams.FirstOrDefault(current => current.Id == teamId)
            ?? throw new InvalidOperationException("Team is not part of this league.");
        if (!string.Equals(team.RosterId, roster.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Roster does not match the selected team.");
        }

        var player = team.Players.FirstOrDefault(current => current.Id == playerId)
            ?? throw new InvalidOperationException("Player is not part of this team.");
        if (player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{player.Name} already has {skill.Name}.");
        }

        var position = FindPosition(roster, player.PositionId);
        var isPrimary = position.PrimarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase);
        var isSecondary = position.SecondarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase);
        if (!isPrimary && !isSecondary)
        {
            throw new InvalidOperationException($"{skill.Name} is not an eligible advancement for {player.Name}.");
        }

        var cost = AdvancementCost(ruleset, position, player);
        if (player.StarPlayerPoints < cost)
        {
            throw new InvalidOperationException($"{player.Name} needs {cost} SPP for the next advancement.");
        }

        var teamValueIncrease = isPrimary ? 20_000 : 40_000;
        return league with
        {
            Teams = league.Teams
                .Select(current => current.Id == team.Id
                    ? current with
                    {
                        TeamValue = current.TeamValue + teamValueIncrease,
                        Players = current.Players
                            .Select(currentPlayer => currentPlayer.Id == player.Id
                                ? currentPlayer with
                                {
                                    StarPlayerPoints = currentPlayer.StarPlayerPoints - cost,
                                    Skills = [.. currentPlayer.Skills, skill.Id]
                                }
                                : currentPlayer)
                            .ToArray()
                    }
                    : current)
                .ToArray()
        };
    }

    private static int AdvancementCost(Ruleset ruleset, PositionTemplate position, Player player)
    {
        var startingSkillIds = position.StartingSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var advancementsTaken = player.Skills.Count(skill => !startingSkillIds.Contains(skill));
        var thresholdKey = advancementsTaken switch
        {
            0 => "first",
            1 => "second",
            2 => "third",
            _ => "fourth"
        };

        return ruleset.AdvancementThresholds.TryGetValue(thresholdKey, out var cost)
            ? cost
            : throw new InvalidOperationException($"Ruleset does not define the '{thresholdKey}' advancement threshold.");
    }

    private static League ReleaseMissNextGamePlayers(League league, MatchState match)
    {
        return league with
        {
            Teams = league.Teams
                .Select(team => team.Id == match.HomeTeamId || team.Id == match.AwayTeamId
                    ? team with
                    {
                        Players = team.Players
                            .Select(player => player.Status == PlayerStatus.MissNextGame
                                ? player with { Status = PlayerStatus.Available }
                                : player)
                            .ToArray()
                    }
                    : team)
                .ToArray()
        };
    }

    private static LeagueTeam ApplyPostMatchTeamUpdates(
        LeagueTeam team,
        IReadOnlyList<MatchPlayerAward> awards,
        int winnings,
        int treasurySpent,
        int scoreFor,
        int scoreAgainst)
    {
        var sppByPlayer = awards
            .Where(award => award.TeamId == team.Id)
            .GroupBy(award => award.PlayerId)
            .ToDictionary(group => group.Key, group => group.Sum(award => award.StarPlayerPoints));

        return team with
        {
            Treasury = Math.Max(0, team.Treasury - treasurySpent) + winnings,
            FanFactor = AdjustFanFactor(team.FanFactor, scoreFor, scoreAgainst),
            Players = team.Players
                .Select(player => sppByPlayer.TryGetValue(player.Id, out var spp)
                    ? player with { StarPlayerPoints = player.StarPlayerPoints + spp }
                    : player)
                .ToArray()
        };
    }

    private static int CalculateWinnings(LeagueTeam team, int scoreFor, int scoreAgainst)
    {
        var resultBonus = scoreFor > scoreAgainst
            ? WinBonusWinnings
            : scoreFor == scoreAgainst
                ? TieBonusWinnings
                : 0;
        return BaseWinnings + (scoreFor * TouchdownWinnings) + resultBonus + (team.FanFactor * FanFactorWinnings);
    }

    private static int AdjustFanFactor(int fanFactor, int scoreFor, int scoreAgainst)
    {
        if (scoreFor > scoreAgainst)
        {
            return Math.Min(6, fanFactor + 1);
        }

        if (scoreFor < scoreAgainst)
        {
            return Math.Max(1, fanFactor - 1);
        }

        return fanFactor;
    }

    private static MatchPlayerAward[] AppendMostValuablePlayerAwards(
        IReadOnlyList<MatchPlayerAward> awards,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam)
    {
        return
        [
            .. awards,
            .. CreateMostValuablePlayerAward(homeTeam),
            .. CreateMostValuablePlayerAward(awayTeam)
        ];
    }

    private static MatchPlayerAward[] CreateMostValuablePlayerAward(LeagueTeam team)
    {
        var player = team.Players.FirstOrDefault(player =>
            (player.Status is PlayerStatus.Available or PlayerStatus.KnockedOut or PlayerStatus.Casualty or PlayerStatus.MissNextGame) &&
            !player.Injuries.Contains("journeyman", StringComparer.OrdinalIgnoreCase));
        return player is null
            ? []
            :
            [
                new MatchPlayerAward
                {
                    TeamId = team.Id,
                    PlayerId = player.Id,
                    Kind = MatchPlayerAwardKind.MostValuablePlayer,
                    StarPlayerPoints = 4,
                    TeamName = team.Name,
                    PlayerName = player.Name
                }
            ];
    }

    private static MatchPlayerAward[] EnrichPlayerAwards(IReadOnlyList<MatchPlayerAward> awards, MatchState match, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return awards.Select(award =>
        {
            var team = TeamById(homeTeam, awayTeam, award.TeamId);
            var player = team?.Players.FirstOrDefault(current => current.Id == award.PlayerId);
            var victimTeam = award.VictimTeamId is Guid victimTeamId
                ? TeamById(homeTeam, awayTeam, victimTeamId)
                : FindTeamContainingPlayer(homeTeam, awayTeam, award.VictimPlayerId);
            var victim = award.VictimPlayerId is Guid victimPlayerId
                ? victimTeam?.Players.FirstOrDefault(current => current.Id == victimPlayerId)
                : null;
            var victimPlacement = award.VictimPlayerId is Guid placementPlayerId
                ? match.Placements.FirstOrDefault(placement => placement.PlayerId == placementPlayerId)
                : null;

            return award with
            {
                TeamName = award.TeamName ?? team?.Name,
                PlayerName = award.PlayerName ?? player?.Name,
                VictimTeamId = award.VictimTeamId ?? victimTeam?.Id,
                VictimTeamName = award.VictimTeamName ?? victimTeam?.Name,
                VictimPlayerName = award.VictimPlayerName ?? victim?.Name,
                CasualtyResult = award.CasualtyResult ?? victimPlacement?.Casualty?.Result
            };
        }).ToArray();
    }

    private static LeagueTeam? TeamById(LeagueTeam homeTeam, LeagueTeam awayTeam, Guid teamId)
    {
        if (homeTeam.Id == teamId)
        {
            return homeTeam;
        }

        return awayTeam.Id == teamId ? awayTeam : null;
    }

    private static LeagueTeam? FindTeamContainingPlayer(LeagueTeam homeTeam, LeagueTeam awayTeam, Guid? playerId)
    {
        if (playerId is null)
        {
            return null;
        }

        if (homeTeam.Players.Any(player => player.Id == playerId))
        {
            return homeTeam;
        }

        return awayTeam.Players.Any(player => player.Id == playerId) ? awayTeam : null;
    }

    private static League AdvanceSeasonWeek(League league, Guid seasonId)
    {
        return league with
        {
            Seasons = league.Seasons
                .Select(season => season.Id == seasonId ? AdvanceSeasonWeek(season) : season)
                .ToArray()
        };
    }

    private static Season AdvanceSeasonWeek(Season season)
    {
        var currentWeekMatches = season.Schedule.Where(match => match.Week == season.CurrentWeek).ToArray();
        if (currentWeekMatches.Length == 0 || currentWeekMatches.Any(match => match.Result is null))
        {
            return season;
        }

        var nextWeek = season.Schedule
            .Where(match => match.Week > season.CurrentWeek && match.Result is null)
            .Select(match => match.Week)
            .DefaultIfEmpty(season.CurrentWeek)
            .Min();

        return season with { CurrentWeek = nextWeek };
    }

    private static LeagueTeam ApplyTeamCasualties(LeagueTeam team, IReadOnlyList<PlayerPlacement> casualties)
    {
        if (casualties.Count == 0)
        {
            return team;
        }

        return team with
        {
            Players = team.Players
                .Select(player =>
                {
                    var casualty = casualties.FirstOrDefault(placement => placement.PlayerId == player.Id);
                    if (casualty is null)
                    {
                        return player;
                    }

                    return ApplyPlayerCasualty(player, casualty.Casualty?.Result ?? CasualtyResult.Dead);
                })
                .ToArray()
        };
    }

    private static Player ApplyPlayerCasualty(Player player, CasualtyResult result)
    {
        return result switch
        {
            CasualtyResult.BadlyHurt => player with
            {
                Status = PlayerStatus.Available,
                Injuries = [.. player.Injuries, "badly-hurt"]
            },
            CasualtyResult.SeriouslyHurt => player with
            {
                Status = PlayerStatus.MissNextGame,
                Injuries = [.. player.Injuries, "seriously-hurt"]
            },
            CasualtyResult.SeriousInjury => player with
            {
                Status = PlayerStatus.MissNextGame,
                Injuries = [.. player.Injuries, "serious-injury"]
            },
            CasualtyResult.LastingInjury => ApplyLastingInjury(player),
            CasualtyResult.Dead => player with
            {
                Status = PlayerStatus.Dead,
                Injuries = [.. player.Injuries, "dead"]
            },
            _ => player
        };
    }

    private static Player ApplyLastingInjury(Player player)
    {
        var armorInjuries = player.Injuries.Count(injury => string.Equals(injury, "lasting-injury-av", StringComparison.OrdinalIgnoreCase));
        var injuryCode = armorInjuries % 2 == 0 ? "lasting-injury-ma" : "lasting-injury-av";
        return player with
        {
            Status = PlayerStatus.MissNextGame,
            Stats = string.Equals(injuryCode, "lasting-injury-ma", StringComparison.OrdinalIgnoreCase)
                ? player.Stats with { Movement = Math.Max(1, player.Stats.Movement - 1) }
                : player.Stats with { Armor = Math.Max(2, player.Stats.Armor - 1) },
            Injuries = [.. player.Injuries, injuryCode]
        };
    }

    private static LeagueTeam[] ApplyPlagueRidden(IReadOnlyList<LeagueTeam> teams, IReadOnlyList<PlayerPlacement> casualties)
    {
        var nextTeams = teams.ToArray();
        foreach (var deadPlacement in casualties.Where(placement => (placement.Casualty?.Result ?? (placement.State == PlayerPitchState.Dead ? CasualtyResult.Dead : CasualtyResult.BadlyHurt)) == CasualtyResult.Dead))
        {
            var deadTeam = teams.FirstOrDefault(team => team.Id == deadPlacement.TeamId);
            var deadPlayer = deadTeam?.Players.FirstOrDefault(player => player.Id == deadPlacement.PlayerId);
            if (deadPlayer is null)
            {
                continue;
            }

            for (var index = 0; index < nextTeams.Length; index++)
            {
                var team = nextTeams[index];
                if (team.Id == deadPlacement.TeamId ||
                    team.Players.Count >= MaximumRosterPlayers ||
                    !team.Players.Any(player => player.Skills.Any(skill => string.Equals(skill, "plague-ridden", StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                var plaguePlayer = new Player
                {
                    Id = Guid.NewGuid(),
                    Name = $"Plague Ridden {deadPlayer.Name}",
                    PositionId = "plague-ridden",
                    Stats = deadPlayer.Stats,
                    Skills = ["decay", "regeneration"],
                    Injuries = ["created-by-plague-ridden"]
                };
                nextTeams[index] = team with { Players = [.. team.Players, plaguePlayer] };
                break;
            }
        }

        return nextTeams;
    }

    private static void ValidateStaff(int cheerleaders, int assistantCoaches, int apothecaries)
    {
        if (cheerleaders < 0)
        {
            throw new InvalidOperationException("Cheerleaders cannot be negative.");
        }

        if (assistantCoaches < 0)
        {
            throw new InvalidOperationException("Assistant coaches cannot be negative.");
        }

        if (apothecaries is < 0 or > 1)
        {
            throw new InvalidOperationException("Apothecaries must be between 0 and 1.");
        }
    }

    private static ScheduledMatch[] CreateDoubleRoundRobinSchedule(IReadOnlyList<LeagueTeam> teams)
    {
        var firstHalf = CreateSingleRoundRobinPairings(teams);
        var secondHalfOrder = RotateRounds(firstHalf, Math.Max(1, firstHalf.Length / 2));
        var matches = new List<ScheduledMatch>();

        for (var roundIndex = 0; roundIndex < firstHalf.Length; roundIndex++)
        {
            foreach (var (homeTeamId, awayTeamId) in firstHalf[roundIndex])
            {
                matches.Add(new ScheduledMatch
                {
                    Id = Guid.NewGuid(),
                    Week = roundIndex + 1,
                    HomeTeamId = homeTeamId,
                    AwayTeamId = awayTeamId
                });
            }
        }

        for (var roundIndex = 0; roundIndex < secondHalfOrder.Length; roundIndex++)
        {
            foreach (var (homeTeamId, awayTeamId) in secondHalfOrder[roundIndex])
            {
                matches.Add(new ScheduledMatch
                {
                    Id = Guid.NewGuid(),
                    Week = firstHalf.Length + roundIndex + 1,
                    HomeTeamId = awayTeamId,
                    AwayTeamId = homeTeamId
                });
            }
        }

        return matches.ToArray();
    }

    private static (Guid HomeTeamId, Guid AwayTeamId)[][] CreateSingleRoundRobinPairings(IReadOnlyList<LeagueTeam> teams)
    {
        var rotatingTeams = teams.Select(team => team.Id).ToList();
        var rounds = new List<(Guid HomeTeamId, Guid AwayTeamId)[]>();
        var teamCount = rotatingTeams.Count;

        for (var round = 0; round < teamCount - 1; round++)
        {
            var pairings = new List<(Guid HomeTeamId, Guid AwayTeamId)>();
            for (var index = 0; index < teamCount / 2; index++)
            {
                var first = rotatingTeams[index];
                var second = rotatingTeams[teamCount - 1 - index];
                pairings.Add((round + index) % 2 == 0 ? (first, second) : (second, first));
            }

            rounds.Add(pairings.ToArray());

            var moved = rotatingTeams[^1];
            rotatingTeams.RemoveAt(teamCount - 1);
            rotatingTeams.Insert(1, moved);
        }

        return rounds.ToArray();
    }

    private static T[] RotateRounds<T>(IReadOnlyList<T> rounds, int offset)
    {
        return rounds
            .Skip(offset)
            .Concat(rounds.Take(offset))
            .ToArray();
    }

    private static Player CreatePlayer(TeamRoster roster, PlayerDraftPick pick)
    {
        var position = FindPosition(roster, pick.PositionId);
        return new Player
        {
            Id = Guid.NewGuid(),
            Name = RequireText(pick.Name, "Player name is required."),
            PositionId = position.Id,
            Stats = position.Stats,
            Skills = position.StartingSkills
        };
    }

    private static PositionTemplate FindPosition(TeamRoster roster, string positionId)
    {
        return roster.Positions.FirstOrDefault(position => string.Equals(position.Id, positionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Roster '{roster.Id}' does not contain position '{positionId}'.");
    }

    private static void ValidateDraft(TeamRoster roster, IReadOnlyList<Player> players)
    {
        var counts = players
            .GroupBy(player => player.PositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var position in roster.Positions)
        {
            counts.TryGetValue(position.Id, out var count);
            if (count < position.Min || count > position.Max)
            {
                throw new InvalidOperationException(
                    $"Draft has {count} '{position.Name}' players; roster requires {position.Min}-{position.Max}.");
            }
        }
    }

    private static string RequireText(string value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
    }
}

public sealed record PlayerDraftPick(string Name, string PositionId);
