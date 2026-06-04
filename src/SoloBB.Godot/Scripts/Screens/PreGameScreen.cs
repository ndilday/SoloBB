using Godot;
using System;
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

        if (!summary.StarPlayersSupported)
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
            awayTreasurySpent: (int)_awayTreasurySpin.Value);
    }

    private void UpdatePlanStatus()
    {
        try
        {
            var plan = BuildPlan();
            _homeBudgetLabel.Text = BudgetText(_homeTeam, plan.Home);
            _awayBudgetLabel.Text = BudgetText(_awayTeam, plan.Away);
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

    private static string BudgetText(LeagueTeam team, TeamInducementPlan plan)
    {
        var cost = plan.Bribes * PreGameService.BribeCost;
        return $"{team.Name}: {plan.Bribes} bribe(s), cost {FormatGold(cost)}, budget {FormatGold(plan.PettyCash + plan.TreasurySpent)}.";
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
}
