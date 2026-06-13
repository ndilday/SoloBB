using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class LeagueHomeScreen : VBoxContainer
{
    private Action<Guid> _openTeam = _ => { };
    private Action<Guid> _playGame = _ => { };
    private League _league = null!;
    private Tree _standingsTree = null!;
    private VBoxContainer _teamPreview = null!;

    public void Setup(League league, Action<Guid> openTeam, Action<Guid> playGame, Action back)
    {
        Clear();
        AddThemeConstantOverride("separation", 12);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        _league = league;
        _openTeam = openTeam;
        _playGame = playGame;

        var currentSeason = league.Seasons.LastOrDefault();
        var currentWeek = currentSeason?.CurrentWeek ?? 1;

        var headerActions = new HBoxContainer();
        headerActions.AddThemeConstantOverride("separation", 8);
        var backButton = ScreenStyles.StyledButton("Back");
        backButton.Pressed += back;
        headerActions.AddChild(backButton);

        AddChild(ScreenStyles.ScreenHeader(
            "League Hub",
            league.Name,
            currentSeason is null
                ? "Review the competition and prepare its first season."
                : $"{currentSeason.Name} | Week {currentWeek} | Select a team or prepare the next fixture.",
            headerActions));

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        AddChild(body);

        var tableColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.7f
        };
        tableColumn.AddThemeConstantOverride("separation", 12);
        body.AddChild(tableColumn);

        tableColumn.AddChild(ScreenStyles.Panel(
            "Standings",
            BuildLeagueTable(league),
            $"{league.Teams.Count} teams"));

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.85f
        };
        sideColumn.AddThemeConstantOverride("separation", 12);
        body.AddChild(sideColumn);

        var weekGames = currentSeason?.Schedule.Count(match => match.Week == currentWeek) ?? 0;
        var remainingGames = currentSeason?.Schedule.Count(match => match.Week == currentWeek && match.Result is null) ?? 0;
        sideColumn.AddChild(ScreenStyles.Panel(
            "This Week",
            BuildWeekSchedule(league, currentSeason, currentWeek),
            WeekBadge(weekGames, remainingGames),
            remainingGames > 0 ? ScreenStyles.Warning : ScreenStyles.Good));

        _teamPreview = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _teamPreview.AddThemeConstantOverride("separation", 8);
        sideColumn.AddChild(ScreenStyles.Panel("Team Snapshot", _teamPreview, "Select a row"));

        SelectFirstTeam();
    }

    private Tree BuildLeagueTable(League league)
    {
        _standingsTree = new Tree
        {
            Columns = 10,
            HideRoot = true,
            ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(680, 330),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        var titles = new[] { "#", "Team", "TV", "W", "L", "T", "PF", "PA", "+/-", "LP" };
        for (var column = 0; column < titles.Length; column++)
        {
            _standingsTree.SetColumnTitle(column, titles[column]);
            _standingsTree.SetColumnExpand(column, column == 1);
            _standingsTree.SetColumnCustomMinimumWidth(column, column switch
            {
                0 => 34,
                1 => 190,
                2 => 62,
                8 => 48,
                _ => 38
            });

            if (column != 1)
            {
                _standingsTree.SetColumnTitleAlignment(column, HorizontalAlignment.Right);
            }
        }

        var root = _standingsTree.CreateItem();
        var position = 1;
        foreach (var row in BuildStandings(league))
        {
            var item = _standingsTree.CreateItem(root);
            item.SetText(0, position.ToString());
            item.SetText(1, row.Team.Name);
            item.SetText(2, FormatTeamValue(row.Team.TeamValue));
            item.SetText(3, row.Wins.ToString());
            item.SetText(4, row.Losses.ToString());
            item.SetText(5, row.Ties.ToString());
            item.SetText(6, row.PointsFor.ToString());
            item.SetText(7, row.PointsAgainst.ToString());
            item.SetText(8, Signed(row.PointDelta));
            item.SetText(9, row.LeaguePoints.ToString());
            item.SetMetadata(0, Variant.From(row.Team.Id.ToString()));

            for (var column = 0; column < _standingsTree.Columns; column++)
            {
                item.SetTextAlignment(column, column == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
            }

            position++;
        }

        _standingsTree.ItemSelected += UpdateTeamPreview;
        _standingsTree.ItemActivated += OpenSelectedTeam;
        return _standingsTree;
    }

    private Control BuildWeekSchedule(League league, Season? season, int week)
    {
        var list = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(320, 190),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        list.AddThemeConstantOverride("separation", 8);

        if (season is null)
        {
            list.AddChild(ScreenStyles.MutedLabel("No season has been scheduled."));
            return list;
        }

        var games = season.Schedule
            .Where(match => match.Week == week)
            .OrderBy(match => TeamName(league, match.HomeTeamId), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (games.Length == 0)
        {
            list.AddChild(ScreenStyles.MutedLabel("No games are scheduled this week."));
            return list;
        }

        foreach (var game in games)
        {
            list.AddChild(BuildMatchCard(league, game));
        }

        return list;
    }

    private Control BuildMatchCard(League league, ScheduledMatch game)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);

        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", 2);
        copy.AddChild(new Label
        {
            Text = $"{TeamName(league, game.HomeTeamId)} vs {TeamName(league, game.AwayTeamId)}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        if (game.Result is MatchResult result)
        {
            var resultLabel = ScreenStyles.MutedLabel($"Final: {result.HomeScore} - {result.AwayScore}");
            resultLabel.AddThemeColorOverride("font_color", ScreenStyles.Good);
            copy.AddChild(resultLabel);
        }
        else
        {
            copy.AddChild(ScreenStyles.MutedLabel("Pregame setup available"));
        }

        row.AddChild(copy);

        if (game.Result is null)
        {
            var playButton = ScreenStyles.StyledButton("Prepare", primary: true);
            var gameId = game.Id;
            playButton.Pressed += () => _playGame(gameId);
            row.AddChild(playButton);
        }
        else
        {
            row.AddChild(ScreenStyles.Badge("Final", ScreenStyles.Good));
        }

        return ScreenStyles.Inset(row);
    }

    private void SelectFirstTeam()
    {
        var first = _standingsTree.GetRoot()?.GetFirstChild();
        if (first is null)
        {
            _teamPreview.AddChild(ScreenStyles.MutedLabel("No teams have joined this league."));
            return;
        }

        first.Select(0);
        UpdateTeamPreview();
    }

    private void UpdateTeamPreview()
    {
        ClearChildren(_teamPreview);

        var team = SelectedTeam();
        if (team is null)
        {
            _teamPreview.AddChild(ScreenStyles.MutedLabel("Select a standings row to inspect a team."));
            return;
        }

        var name = new Label { Text = team.Name };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", ScreenStyles.Text);
        _teamPreview.AddChild(name);
        _teamPreview.AddChild(ScreenStyles.MutedLabel($"Coach: {team.CoachName}"));

        var details = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("h_separation", 12);
        details.AddThemeConstantOverride("v_separation", 6);
        AddDetail(details, "Team Value", FormatGold(team.TeamValue));
        AddDetail(details, "Treasury", FormatGold(team.Treasury));
        AddDetail(details, "Players", team.Players.Count.ToString());
        AddDetail(details, "Rerolls", team.Rerolls.ToString());
        _teamPreview.AddChild(details);

        var openButton = ScreenStyles.StyledButton("Open Team", primary: true);
        var teamId = team.Id;
        openButton.Pressed += () => _openTeam(teamId);
        _teamPreview.AddChild(openButton);
    }

    private static void AddDetail(GridContainer grid, string label, string value)
    {
        grid.AddChild(ScreenStyles.MutedLabel(label));
        var valueLabel = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        valueLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        grid.AddChild(valueLabel);
    }

    private LeagueTeam? SelectedTeam()
    {
        var selected = _standingsTree.GetSelected();
        if (selected is null)
        {
            return null;
        }

        var teamIdText = selected.GetMetadata(0).AsString();
        return Guid.TryParse(teamIdText, out var teamId)
            ? _league.Teams.FirstOrDefault(team => team.Id == teamId)
            : null;
    }

    private void OpenSelectedTeam()
    {
        if (SelectedTeam() is LeagueTeam team)
        {
            _openTeam(team.Id);
        }
    }

    private static string WeekBadge(int games, int remaining)
    {
        if (games == 0)
        {
            return "No fixtures";
        }

        return remaining == 0 ? "Complete" : $"{remaining} remaining";
    }

    private static IReadOnlyList<StandingsRow> BuildStandings(League league)
    {
        var rows = league.Teams
            .Select(team => new StandingsRow(team))
            .ToDictionary(row => row.Team.Id);

        foreach (var match in league.Seasons.SelectMany(season => season.Schedule).Where(match => match.Result is not null))
        {
            var result = match.Result!;
            ApplyResult(rows[match.HomeTeamId], result.HomeScore, result.AwayScore);
            ApplyResult(rows[match.AwayTeamId], result.AwayScore, result.HomeScore);
        }

        return rows.Values
            .OrderByDescending(row => row.LeaguePoints)
            .ThenByDescending(row => row.PointDelta)
            .ThenByDescending(row => row.PointsFor)
            .ThenBy(row => row.Team.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ApplyResult(StandingsRow row, int pointsFor, int pointsAgainst)
    {
        row.PointsFor += pointsFor;
        row.PointsAgainst += pointsAgainst;
        if (pointsFor > pointsAgainst)
        {
            row.Wins++;
        }
        else if (pointsFor < pointsAgainst)
        {
            row.Losses++;
        }
        else
        {
            row.Ties++;
        }
    }

    private static string TeamName(League league, Guid teamId)
    {
        return league.Teams.FirstOrDefault(team => team.Id == teamId)?.Name ?? "Unknown";
    }

    private void Clear()
    {
        ClearChildren(this);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string Signed(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }

    private static string FormatTeamValue(int value)
    {
        return $"{value / 10_000:N0}";
    }

    private sealed class StandingsRow
    {
        public StandingsRow(LeagueTeam team)
        {
            Team = team;
        }

        public LeagueTeam Team { get; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Ties { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
        public int PointDelta => PointsFor - PointsAgainst;
        public int LeaguePoints => (Wins * 3) + Ties;
    }
}
