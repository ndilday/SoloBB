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

    public void Setup(League league, Action<Guid> openTeam, Action<Guid> playGame, Action back)
    {
        Clear();
        _openTeam = openTeam;
        _playGame = playGame;

        AddTitle(league.Name);

        var body = new HBoxContainer();
        AddChild(body);

        var tableColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(560, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.6f
        };
        body.AddChild(tableColumn);
        tableColumn.AddChild(new Label { Text = "League Table" });
        tableColumn.AddChild(BuildLeagueTable(league));

        var scheduleColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(360, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.0f
        };
        body.AddChild(scheduleColumn);
        var currentSeason = league.Seasons.LastOrDefault();
        var currentWeek = currentSeason?.CurrentWeek ?? 1;
        scheduleColumn.AddChild(new Label { Text = $"Week {currentWeek} Games" });
        scheduleColumn.AddChild(BuildWeekSchedule(league, currentSeason, currentWeek));

        AddButton("Back", back);
    }

    private Tree BuildLeagueTable(League league)
    {
        var tree = new Tree
        {
            Columns = 10,
            HideRoot = true,
            CustomMinimumSize = new Vector2(560, 260),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        tree.SetColumnTitle(0, "Team");
        tree.SetColumnTitle(1, "TV");
        tree.SetColumnTitle(2, "CTV");
        tree.SetColumnTitle(3, "W");
        tree.SetColumnTitle(4, "L");
        tree.SetColumnTitle(5, "T");
        tree.SetColumnTitle(6, "PF");
        tree.SetColumnTitle(7, "PA");
        tree.SetColumnTitle(8, "Delta");
        tree.SetColumnTitle(9, "LP");
        tree.ColumnTitlesVisible = true;
        tree.SetColumnExpand(0, true);
        tree.SetColumnCustomMinimumWidth(0, 120);
        for (var column = 1; column < tree.Columns; column++)
        {
            tree.SetColumnExpand(column, false);
            tree.SetColumnCustomMinimumWidth(column, column is 1 or 2 or 8 ? 48 : 32);
            tree.SetColumnTitleAlignment(column, HorizontalAlignment.Right);
        }

        var root = tree.CreateItem();
        foreach (var row in BuildStandings(league))
        {
            var item = tree.CreateItem(root);
            item.SetText(0, row.Team.Name);
            item.SetText(1, FormatTeamValue(row.Team.TeamValue));
            item.SetText(2, FormatTeamValue(row.Team.TeamValue));
            item.SetText(3, row.Wins.ToString());
            item.SetText(4, row.Losses.ToString());
            item.SetText(5, row.Ties.ToString());
            item.SetText(6, row.PointsFor.ToString());
            item.SetText(7, row.PointsAgainst.ToString());
            item.SetText(8, row.PointDelta.ToString());
            item.SetText(9, row.LeaguePoints.ToString());
            item.SetMetadata(0, Variant.From(row.Team.Id.ToString()));
            for (var column = 1; column < tree.Columns; column++)
            {
                item.SetTextAlignment(column, HorizontalAlignment.Right);
            }
        }

        tree.ItemSelected += () => OpenSelectedTeam(tree);
        tree.ItemActivated += () => OpenSelectedTeam(tree);

        return tree;
    }

    private void OpenSelectedTeam(Tree tree)
    {
        var selected = tree.GetSelected();
        if (selected is null)
        {
            return;
        }

        var teamIdText = selected.GetMetadata(0).AsString();
        if (Guid.TryParse(teamIdText, out var teamId))
        {
            _openTeam(teamId);
        }
    }

    private Control BuildWeekSchedule(League league, Season? season, int week)
    {
        var list = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(360, 260),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        if (season is null)
        {
            list.AddChild(new Label { Text = "No season has been scheduled." });
            return list;
        }

        var games = season.Schedule
            .Where(match => match.Week == week)
            .OrderBy(match => TeamName(league, match.HomeTeamId), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (games.Length == 0)
        {
            list.AddChild(new Label { Text = "No games scheduled this week." });
            return list;
        }

        foreach (var game in games)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(new Label
            {
                Text = $"{TeamName(league, game.HomeTeamId)} vs {TeamName(league, game.AwayTeamId)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            });

            var playButton = new Button { Text = "Play!" };
            var gameId = game.Id;
            playButton.Pressed += () => _playGame(gameId);
            row.AddChild(playButton);
            list.AddChild(row);
        }

        return list;
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

    private void AddTitle(string text)
    {
        var title = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        AddChild(title);
    }

    private void AddButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        AddChild(button);
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0}";
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
