using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class PreGameScreen : VBoxContainer
{
    private readonly PreGameService _preGameService = new();

    private Ruleset _ruleset = null!;
    private RosterSet _rosterSet = null!;
    private LeagueTeam _homeTeam = null!;
    private LeagueTeam _awayTeam = null!;
    private Func<MatchInducementPlan, Task> _done = _ => Task.CompletedTask;
    private SpinBox _homeBribesSpin = null!;
    private SpinBox _awayBribesSpin = null!;
    private SpinBox _homeTreasurySpin = null!;
    private SpinBox _awayTreasurySpin = null!;
    private Label _homeBudgetLabel = null!;
    private Label _awayBudgetLabel = null!;
    private TeamPreGameSummary _homeSummary = null!;
    private TeamPreGameSummary _awaySummary = null!;
    private readonly Dictionary<string, CheckBox> _homeStarChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _awayStarChecks = new(StringComparer.OrdinalIgnoreCase);
    private Label _statusLabel = null!;
    private Button _doneButton = null!;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        League league,
        ScheduledMatch scheduledMatch,
        Func<MatchInducementPlan, Task> done,
        Action back)
    {
        Clear();

        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _done = done;
        _homeTeam = FindTeam(league, scheduledMatch.HomeTeamId);
        _awayTeam = FindTeam(league, scheduledMatch.AwayTeamId);
        var summary = _preGameService.BuildSummary(_ruleset, _rosterSet, _homeTeam, _awayTeam);
        _homeSummary = summary.Home;
        _awaySummary = summary.Away;
        _homeStarChecks.Clear();
        _awayStarChecks.Clear();

        AddTitle("Pre-Game");
        AddChild(new Label { Text = $"Week {scheduledMatch.Week}" });
        AddChild(new Label { Text = $"{_homeTeam.Name} vs {_awayTeam.Name}" });

        var grid = new GridContainer { Columns = 7 };
        AddChild(grid);
        AddHeader(grid, "Team");
        AddHeader(grid, "TV");
        AddHeader(grid, "Treasury");
        AddHeader(grid, "Petty Cash");
        AddHeader(grid, "Journeymen");
        AddHeader(grid, "Bribes");
        AddHeader(grid, "Treasury Spend");

        AddTeamRow(grid, summary.Home, out _homeBribesSpin, out _homeTreasurySpin);
        AddTeamRow(grid, summary.Away, out _awayBribesSpin, out _awayTreasurySpin);

        _homeBudgetLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_homeBudgetLabel);
        _awayBudgetLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_awayBudgetLabel);

        AddRosterMetadata(summary.Home);
        AddRosterMetadata(summary.Away);

        if (summary.StarPlayersSupported)
        {
            AddTeamStarSection(summary.Home, _homeStarChecks);
            AddTeamStarSection(summary.Away, _awayStarChecks);
        }
        else
        {
            AddChild(new Label
            {
                Text = "Star Players: roster data unavailable",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
        }

        _doneButton = new Button { Text = "Done" };
        _doneButton.Pressed += async () => await DoneAsync();
        AddChild(_doneButton);

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        AddChild(backButton);

        _statusLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_statusLabel);

        UpdatePlanStatus();
    }

    private void AddTeamRow(GridContainer grid, TeamPreGameSummary summary, out SpinBox bribesSpin, out SpinBox treasurySpin)
    {
        grid.AddChild(new Label { Text = summary.TeamName });
        grid.AddChild(new Label { Text = FormatGold(summary.TeamValue) });
        grid.AddChild(new Label { Text = FormatGold(summary.Treasury) });
        grid.AddChild(new Label { Text = FormatGold(summary.PettyCash) });
        grid.AddChild(new Label { Text = summary.JourneymenNeeded.ToString() });

        bribesSpin = CreateSpinBox(0, (summary.PettyCash + summary.Treasury) / PreGameService.BribeCost, 0, 1);
        grid.AddChild(bribesSpin);

        treasurySpin = CreateSpinBox(0, summary.Treasury, 0, 10_000);
        grid.AddChild(treasurySpin);
    }

    private async Task DoneAsync()
    {
        try
        {
            await _done(BuildPlan());
        }
        catch (Exception ex)
        {
            SetStatus($"Pre-game setup failed: {ex.Message}");
        }
    }

    private MatchInducementPlan BuildPlan()
    {
        return _preGameService.CreatePlan(
            _ruleset,
            _homeTeam,
            _awayTeam,
            homeBribes: (int)_homeBribesSpin.Value,
            awayBribes: (int)_awayBribesSpin.Value,
            homeTreasurySpent: (int)_homeTreasurySpin.Value,
            awayTreasurySpent: (int)_awayTreasurySpin.Value,
            homeStarPlayerIds: SelectedStarIds(_homeStarChecks),
            awayStarPlayerIds: SelectedStarIds(_awayStarChecks));
    }

    private void UpdatePlanStatus()
    {
        try
        {
            var plan = BuildPlan();
            _homeBudgetLabel.Text = BudgetText(_homeTeam, _homeSummary, plan.Home);
            _awayBudgetLabel.Text = BudgetText(_awayTeam, _awaySummary, plan.Away);
            UpdateStarAffordability(_homeSummary, plan.Home, _homeStarChecks);
            UpdateStarAffordability(_awaySummary, plan.Away, _awayStarChecks);
            _doneButton.Disabled = false;
            SetStatus("Ready.");
        }
        catch (Exception ex)
        {
            _doneButton.Disabled = true;
            SetStatus(ex.Message);
        }
    }

    private void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private static string BudgetText(LeagueTeam team, TeamPreGameSummary summary, TeamInducementPlan plan)
    {
        var bribeCost = plan.Bribes * PreGameService.BribeCost;
        var starCost = SelectedStarCost(summary, plan);
        var totalCost = bribeCost + starCost;
        var budget = plan.PettyCash + plan.TreasurySpent;
        return $"{team.Name}: budget {FormatGold(budget)} ({FormatGold(plan.PettyCash)} petty cash + {FormatGold(plan.TreasurySpent)} treasury), selected {FormatGold(totalCost)}, remaining {FormatGold(budget - totalCost)}. Bribes: {plan.Bribes} ({FormatGold(bribeCost)}). Stars: {plan.StarPlayerIds.Count} ({FormatGold(starCost)}).";
    }

    private void AddRosterMetadata(TeamPreGameSummary summary)
    {
        var details = summary.SpecialRules.Count == 0
            ? "Special rules: none"
            : $"Special rules: {string.Join(", ", summary.SpecialRules)}";
        if (summary.RosterRestrictions.Count > 0)
        {
            details += $". Restrictions: {string.Join(", ", summary.RosterRestrictions)}";
        }

        AddChild(new Label
        {
            Text = $"{summary.TeamName} roster: {details}.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
    }

    private void AddTeamStarSection(TeamPreGameSummary summary, Dictionary<string, CheckBox> checks)
    {
        AddChild(new Label
        {
            Text = $"{summary.TeamName} eligible Star Players",
            ThemeTypeVariation = "HeaderSmall"
        });

        if (summary.EligibleStarPlayers.Count == 0)
        {
            AddChild(new Label { Text = "No eligible Star Players for this roster.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            return;
        }

        var grid = new GridContainer { Columns = 6 };
        AddChild(grid);
        AddHeader(grid, "Select");
        AddHeader(grid, "Star");
        AddHeader(grid, "Cost");
        AddHeader(grid, "Stats");
        AddHeader(grid, "Skills");
        AddHeader(grid, "Eligibility");

        foreach (var star in summary.EligibleStarPlayers)
        {
            var check = new CheckBox();
            check.Toggled += _ => UpdatePlanStatus();
            checks[star.Id] = check;
            grid.AddChild(check);
            grid.AddChild(new Label { Text = star.Name });
            grid.AddChild(new Label { Text = FormatGold(star.Cost) });
            grid.AddChild(new Label { Text = FormatStats(star.Stats) });
            grid.AddChild(new Label { Text = star.Skills.Count == 0 ? "-" : string.Join(", ", star.Skills), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            grid.AddChild(new Label { Text = string.Join(", ", star.MatchedSpecialRules), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
    }

    private void UpdateStarAffordability(TeamPreGameSummary summary, TeamInducementPlan plan, Dictionary<string, CheckBox> checks)
    {
        var selectedCost = SelectedStarCost(summary, plan);
        var budgetBeforeStars = plan.PettyCash + plan.TreasurySpent - (plan.Bribes * PreGameService.BribeCost);
        foreach (var star in summary.EligibleStarPlayers)
        {
            if (!checks.TryGetValue(star.Id, out var check))
            {
                continue;
            }

            var selected = plan.StarPlayerIds.Contains(star.Id, StringComparer.OrdinalIgnoreCase);
            check.Disabled = !selected && selectedCost + star.Cost > budgetBeforeStars;
        }
    }

    private static IReadOnlyList<string> SelectedStarIds(Dictionary<string, CheckBox> checks)
    {
        return checks
            .Where(pair => pair.Value.ButtonPressed)
            .Select(pair => pair.Key)
            .ToArray();
    }

    private static int SelectedStarCost(TeamPreGameSummary summary, TeamInducementPlan plan)
    {
        return summary.EligibleStarPlayers
            .Where(star => plan.StarPlayerIds.Contains(star.Id, StringComparer.OrdinalIgnoreCase))
            .Sum(star => star.Cost);
    }

    private SpinBox CreateSpinBox(double min, double max, double value, double step)
    {
        var spinBox = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = step,
            AllowGreater = false,
            AllowLesser = false
        };
        spinBox.ValueChanged += _ => UpdatePlanStatus();
        return spinBox;
    }

    private static LeagueTeam FindTeam(League league, Guid teamId)
    {
        return league.Teams.FirstOrDefault(team => team.Id == teamId)
            ?? throw new InvalidOperationException("Scheduled match team is not part of this league.");
    }

    private static void AddHeader(GridContainer grid, string text)
    {
        grid.AddChild(new Label
        {
            Text = text,
            ThemeTypeVariation = "HeaderSmall"
        });
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

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"MA {stats.Movement} ST {stats.Strength} AG {stats.Agility}+ PA {stats.Passing}+ AV {stats.Armor}+";
    }
}
