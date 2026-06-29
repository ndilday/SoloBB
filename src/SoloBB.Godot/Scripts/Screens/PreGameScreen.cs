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
    private static readonly Color ScreenInk = new("f3ead6");
    private static readonly Color MutedInk = new("bfb39c");
    private static readonly Color PanelBg = new("211b17");
    private static readonly Color PanelBgAlt = new("2b241e");
    private static readonly Color FieldBg = new("182b20");
    private static readonly Color Gold = new("d8a43f");
    private static readonly Color HomeRed = new("7c333a");
    private static readonly Color AwayBlue = new("27506e");
    private static readonly Color Line = new("6f5a3f");
    private static readonly Color LockedRed = new("a45a48");

    private readonly PreGameService _preGameService = new();
    private readonly Dictionary<Guid, TeamDraftState> _teamStates = [];

    private Ruleset _ruleset = null!;
    private RosterSet _rosterSet = null!;
    private LeagueTeam _homeTeam = null!;
    private LeagueTeam _awayTeam = null!;
    private LeagueTeam _firstTeam = null!;
    private LeagueTeam _secondTeam = null!;
    private TeamPreGameSummary _homeSummary = null!;
    private TeamPreGameSummary _awaySummary = null!;
    private Func<MatchInducementPlan, Task> _done = _ => Task.CompletedTask;
    private Action _back = () => { };
    private int _step;
    private Label _statusLabel = null!;
    private Button _continueButton = null!;
    private ScrollContainer? _contentScroll;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        League league,
        ScheduledMatch scheduledMatch,
        Func<MatchInducementPlan, Task> done,
        Action back)
    {
        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _done = done;
        _back = back;
        _homeTeam = FindTeam(league, scheduledMatch.HomeTeamId);
        _awayTeam = FindTeam(league, scheduledMatch.AwayTeamId);
        _step = 0;

        var summary = _preGameService.BuildSummary(_ruleset, _rosterSet, _homeTeam, _awayTeam);
        _homeSummary = summary.Home;
        _awaySummary = summary.Away;
        (_firstTeam, _secondTeam) = OrderTeamsForInducements(_homeTeam, _awayTeam, _homeSummary, _awaySummary);
        _teamStates.Clear();
        _teamStates[_homeTeam.Id] = new TeamDraftState(_homeTeam, _homeSummary);
        _teamStates[_awayTeam.Id] = new TeamDraftState(_awayTeam, _awaySummary);

        AddThemeConstantOverride("separation", 14);
        AddThemeStyleboxOverride("panel", FlatStyle(FieldBg, Line, 2));
        Render(scheduledMatch.Week, preserveScroll: false);
    }

    private void Render(int week, bool preserveScroll = true)
    {
        var scrollVertical = preserveScroll && _contentScroll is not null && GodotObject.IsInstanceValid(_contentScroll)
            ? _contentScroll.ScrollVertical
            : 0;
        Clear();

        var activeTeam = _step == 0 ? _firstTeam : _secondTeam;
        var activeState = _teamStates[activeTeam.Id];
        var plan = TryBuildPlan(out var planError);

        AddChild(BuildHeader(week));

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _contentScroll = scroll;
        AddChild(scroll);

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.Fill
        };
        body.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(body);

        var mainColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        mainColumn.AddThemeConstantOverride("separation", 12);
        body.AddChild(mainColumn);

        mainColumn.AddChild(BuildTeamSummaryPanel(activeState, plan));
        mainColumn.AddChild(BuildTreasuryPanel(activeState, week));
        mainColumn.AddChild(BuildMarketPanel(activeState, plan, week));
        mainColumn.AddChild(BuildStarsPanel(activeState, plan, week));

        body.AddChild(BuildLedgerPanel(activeState, plan, planError));
        AddChild(BuildFooter(week, planError));

        UpdateStatus(planError);
        CallDeferred(nameof(RestoreScrollPosition), scroll, scrollVertical);
    }

    private Control BuildHeader(int week)
    {
        var panel = Panel(PanelBg, Line, 2);
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        var titleStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(titleStack);

        var crumb = Label($"Week {week} - Pre-Game Inducements", 12, Gold);
        titleStack.AddChild(crumb);

        var title = Label($"{_firstTeam.Name} spend, then {_secondTeam.Name} spend", 26, ScreenInk);
        title.AddThemeFontSizeOverride("font_size", 26);
        titleStack.AddChild(title);

        var context = Label($"{RoleText(_firstTeam)}: {_firstTeam.Name}  |  {RoleText(_secondTeam)}: {_secondTeam.Name}", 13, MutedInk);
        titleStack.AddChild(context);

        var stepStack = new VBoxContainer();
        row.AddChild(stepStack);
        stepStack.AddChild(Pill(_step == 0 ? "Screen 1 of 2" : "Screen 2 of 2", Gold, new Color("16100a")));

        return panel;
    }

    private Control BuildTeamSummaryPanel(TeamDraftState state, MatchInducementPlan? plan)
    {
        var panel = Panel(state.Team.Id == _homeTeam.Id ? HomeRed.Darkened(0.25f) : AwayBlue.Darkened(0.22f), Line, 2);
        var stack = Stack(panel, 10);

        var heading = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddChild(Label(state.Team.Name, 24, ScreenInk));
        var role = Pill(RoleText(state.Team), state.Team.Id == _homeTeam.Id ? HomeRed : AwayBlue, ScreenInk);
        role.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        heading.AddChild(role);
        stack.AddChild(heading);

        var numbers = new GridContainer
        {
            Columns = 4,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        numbers.AddThemeConstantOverride("h_separation", 8);
        numbers.AddThemeConstantOverride("v_separation", 8);
        stack.AddChild(numbers);

        var teamPlan = plan is null ? null : PlanForTeam(plan, state.Team.Id);
        AddStat(numbers, "Current Team Value", FormatGold(state.Summary.TeamValue));
        AddStat(numbers, "Treasury", FormatGold(state.Team.Treasury));
        AddStat(numbers, "Petty Cash", FormatGold(teamPlan?.PettyCash ?? state.Summary.PettyCash), Gold);
        AddStat(numbers, "Journeymen", state.Summary.JourneymenNeeded.ToString());

        var rules = state.Summary.SpecialRules.Count == 0 ? "none" : string.Join(", ", state.Summary.SpecialRules);
        stack.AddChild(Label($"Roster rules: {rules}", 13, MutedInk, autowrap: true));
        return panel;
    }

    private Control BuildTreasuryPanel(TeamDraftState state, int week)
    {
        var panel = Panel(PanelBgAlt, Line);
        var stack = Stack(panel, 8);
        stack.AddChild(Label("Treasury Budget", 18, ScreenInk));

        var note = state.Team.Id == _firstTeam.Id
            ? $"Anything {state.Team.Name} spends from treasury increases {_secondTeam.Name}'s petty cash on screen 2."
            : $"{state.Team.Name} can add its own treasury after receiving recalculated petty cash.";
        stack.AddChild(Label(note, 13, MutedInk, autowrap: true));

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        stack.AddChild(row);
        row.AddChild(Label("Treasury spend", 13, MutedInk));
        var spin = CreateSpinBox(0, state.Team.Treasury, state.TreasurySpent, 10_000);
        spin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        spin.ValueChanged += value =>
        {
            state.TreasurySpent = (int)value;
            Render(week);
        };
        row.AddChild(spin);

        return panel;
    }

    private Control BuildMarketPanel(TeamDraftState state, MatchInducementPlan? plan, int week)
    {
        var panel = Panel(PanelBg, Line, 2);
        var stack = Stack(panel, 10);
        stack.AddChild(Label("Inducement Market", 20, ScreenInk));

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        stack.AddChild(grid);

        foreach (var inducement in state.Summary.AvailableInducements.Where(inducement => !string.Equals(inducement.Id, "star-player", StringComparison.OrdinalIgnoreCase)))
        {
            grid.AddChild(BuildInducementCard(state, inducement, plan, week));
        }

        return panel;
    }

    private Control BuildInducementCard(TeamDraftState state, AvailableInducementSummary inducement, MatchInducementPlan? plan, int week)
    {
        if (inducement.PickerOptions.Count > 0)
        {
            return BuildPickerInducementCard(state, inducement, plan, week);
        }

        var selectable = inducement.MatchEffectImplemented;
        var panel = Panel(selectable ? PanelBgAlt : new Color("1c1b19"), selectable ? new Color("514532") : new Color("34302a"));
        panel.CustomMinimumSize = new Vector2(0, 96);
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 4);
        row.AddChild(details);

        details.AddChild(Label(inducement.Name, 15, selectable ? ScreenInk : MutedInk, autowrap: true));
        details.AddChild(Label($"{FormatInducementCost(inducement)} - {FormatLimit(inducement.MaxCount)}", 12, Gold));
        details.AddChild(Label(inducement.Description, 12, MutedInk, autowrap: true));

        var controls = new VBoxContainer();
        row.AddChild(controls);
        var currentCount = state.CountFor(inducement.Id);
        var affordableMax = selectable ? AffordableInducementMax(state, inducement, plan) : 0;
        var spinMax = selectable ? Math.Max(currentCount, affordableMax) : 0;
        var spin = CreateSpinBox(0, spinMax, currentCount, 1);
        spin.CustomMinimumSize = new Vector2(88, 0);
        spin.TooltipText = selectable
            ? $"Maximum now: {affordableMax} based on remaining budget."
            : "This inducement does not yet have a match effect.";
        spin.ValueChanged += value =>
        {
            state.InducementCounts[inducement.Id] = (int)value;
            Render(week);
        };
        controls.AddChild(spin);

        return panel;
    }

    private Control BuildPickerInducementCard(TeamDraftState state, AvailableInducementSummary inducement, MatchInducementPlan? plan, int week)
    {
        var panel = Panel(PanelBgAlt, new Color("514532"));
        var stack = Stack(panel, 5);
        stack.AddChild(Label(inducement.Name, 15, ScreenInk, autowrap: true));
        var selectedCount = state.PickerCountFor(inducement.Id);
        stack.AddChild(Label($"Choose options - {selectedCount}/{inducement.MaxCount} selected", 12, Gold));

        foreach (var option in inducement.PickerOptions)
        {
            var optionPanel = Panel(new Color("241e19"), new Color("44382b"));
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 8);
            optionPanel.AddChild(row);

            var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            details.AddThemeConstantOverride("separation", 3);
            row.AddChild(details);
            details.AddChild(Label($"{option.Name} - {FormatGold(option.Cost)}", 13, ScreenInk, autowrap: true));
            var optionDetail = option.Stats is null
                ? option.Description
                : $"{option.Description} {FormatStats(option.Stats)} Skills: {(option.Skills.Count == 0 ? "-" : string.Join(", ", option.Skills))}";
            details.AddChild(Label(optionDetail, 11, MutedInk, autowrap: true));

            var affordableMax = AffordablePickerOptionMax(state, inducement, option, plan);
            var currentCount = state.CountFor(inducement.Id, option.Id);
            var spin = CreateSpinBox(0, Math.Max(currentCount, affordableMax), currentCount, 1);
            spin.CustomMinimumSize = new Vector2(78, 0);
            spin.TooltipText = $"Maximum now: {affordableMax} based on the family limit and remaining budget.";
            spin.ValueChanged += value =>
            {
                state.SetPickerCount(inducement.Id, option.Id, (int)value);
                Render(week);
            };
            row.AddChild(spin);
            stack.AddChild(optionPanel);
        }

        return panel;
    }

    private Control BuildStarsPanel(TeamDraftState state, MatchInducementPlan? plan, int week)
    {
        var panel = Panel(PanelBgAlt, Line);
        var stack = Stack(panel, 8);
        stack.AddChild(Label("Players and Named Help", 18, ScreenInk));
        stack.AddChild(Label("Star players can be selected here. Mercenaries, famous coaching staff, wizards, and biased referees are shown in the market as picker-backed items because their price varies.", 13, MutedInk, autowrap: true));
        stack.AddChild(Label($"Star Players selected: {state.StarPlayerIds.Count}/{StarPlayerMaximum()}", 12, Gold));

        if (state.Summary.EligibleStarPlayers.Count == 0)
        {
            stack.AddChild(Label("No eligible Star Players for this roster.", 13, MutedInk));
            return panel;
        }

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        stack.AddChild(grid);

        foreach (var star in state.Summary.EligibleStarPlayers)
        {
            grid.AddChild(BuildStarCard(state, star, plan, week));
        }

        return panel;
    }

    private Control BuildStarCard(TeamDraftState state, EligibleStarPlayerSummary star, MatchInducementPlan? plan, int week)
    {
        var selected = state.StarPlayerIds.Contains(star.Id);
        var affordable = plan is null || selected || StarCanBeAdded(state, star, plan);
        var claimedByOpponent = _teamStates.Values.Any(other => other.Team.Id != state.Team.Id && other.StarPlayerIds.Contains(star.Id));
        var limitReached = !selected && state.StarPlayerIds.Count >= StarPlayerMaximum();
        var available = selected || affordable && !claimedByOpponent && !limitReached;
        var panel = Panel(available ? PanelBg : new Color("1c1b19"), available ? new Color("514532") : new Color("34302a"));
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        var check = new CheckBox
        {
            ButtonPressed = selected,
            Disabled = !available,
            TooltipText = selected
                ? "Deselect this Star Player."
                : claimedByOpponent
                    ? "The opposing team has already selected this Star Player."
                    : limitReached
                        ? $"A team can select at most {StarPlayerMaximum()} Star Players."
                        : affordable
                            ? "Select this Star Player."
                            : "Not enough budget remaining."
        };
        check.Toggled += toggled =>
        {
            if (toggled)
            {
                state.StarPlayerIds.Add(star.Id);
            }
            else
            {
                state.StarPlayerIds.Remove(star.Id);
            }

            Render(week);
        };
        row.AddChild(check);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 4);
        row.AddChild(text);
        text.AddChild(Label(star.Name, 15, available ? ScreenInk : MutedInk));
        text.AddChild(Label($"{FormatGold(star.Cost)} - {FormatStats(star.Stats)}", 12, Gold, autowrap: true));
        text.AddChild(Label(star.Skills.Count == 0 ? "Skills: -" : $"Skills: {string.Join(", ", star.Skills)}", 12, MutedInk, autowrap: true));
        text.AddChild(Label($"Eligible: {string.Join(", ", star.MatchedSpecialRules)}", 12, MutedInk, autowrap: true));
        if (!selected && !available)
        {
            var reason = claimedByOpponent
                ? "Selected by the opposing team"
                : limitReached
                    ? "Team Star Player limit reached"
                    : "Insufficient budget";
            text.AddChild(Label(reason, 12, LockedRed, autowrap: true));
        }

        return panel;
    }

    private Control BuildLedgerPanel(TeamDraftState activeState, MatchInducementPlan? plan, string? planError)
    {
        var panel = Panel(PanelBg, Gold, 2);
        panel.CustomMinimumSize = new Vector2(310, 0);
        var stack = Stack(panel, 10);
        stack.AddChild(Label("Inducement Ledger", 20, ScreenInk));

        AddLedgerTeam(stack, _teamStates[_firstTeam.Id], plan);
        AddLedgerTeam(stack, _teamStates[_secondTeam.Id], plan);

        if (_step == 0)
        {
            var first = _teamStates[_firstTeam.Id];
            var tvGap = Math.Abs(_homeSummary.TeamValue - _awaySummary.TeamValue);
            stack.AddChild(Separator());
            stack.AddChild(Label("Handoff Preview", 16, ScreenInk));
            stack.AddChild(LedgerRow("TV difference", FormatGold(tvGap)));
            stack.AddChild(LedgerRow($"{_firstTeam.Name} treasury spend", FormatGold(first.TreasurySpent)));
            stack.AddChild(LedgerRow($"{_secondTeam.Name} petty cash next", FormatGold(tvGap + first.TreasurySpent), Gold));
        }

        if (!string.IsNullOrWhiteSpace(planError))
        {
            stack.AddChild(Separator());
            stack.AddChild(Label(planError, 13, LockedRed, autowrap: true));
        }

        return panel;
    }

    private void AddLedgerTeam(VBoxContainer stack, TeamDraftState state, MatchInducementPlan? plan)
    {
        var teamPlan = plan is null ? null : PlanForTeam(plan, state.Team.Id);
        var budget = (teamPlan?.PettyCash ?? state.Summary.PettyCash) + state.TreasurySpent;
        var selected = SelectedInducementCost(state) + SelectedStarCost(state);
        stack.AddChild(Separator());
        stack.AddChild(Label(state.Team.Name, 16, ScreenInk));
        stack.AddChild(LedgerRow("Petty cash", FormatGold(teamPlan?.PettyCash ?? state.Summary.PettyCash)));
        stack.AddChild(LedgerRow("Own treasury", FormatGold(state.TreasurySpent)));
        stack.AddChild(LedgerRow("Selected", FormatGold(selected)));
        stack.AddChild(LedgerRow("Remaining", FormatGold(budget - selected), budget - selected >= 0 ? Gold : LockedRed));
    }

    private Control BuildFooter(int week, string? planError)
    {
        var panel = Panel(PanelBg, Line, 2);
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        var backButton = StyledButton(_step == 0 ? "Back" : $"Back to {_firstTeam.Name}");
        backButton.Pressed += () =>
        {
            if (_step == 0)
            {
                _back();
            }
            else
            {
                _step = 0;
                Render(week, preserveScroll: false);
            }
        };
        row.AddChild(backButton);

        _statusLabel = Label("", 13, MutedInk, autowrap: true);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_statusLabel);

        _continueButton = StyledButton(_step == 0 ? $"Continue to {_secondTeam.Name}" : "Confirm Pre-Game", primary: true);
        _continueButton.Disabled = !string.IsNullOrWhiteSpace(planError);
        _continueButton.Pressed += async () =>
        {
            if (_step == 0)
            {
                _step = 1;
                Render(week, preserveScroll: false);
            }
            else
            {
                await DoneAsync();
            }
        };
        row.AddChild(_continueButton);

        return panel;
    }

    private async Task DoneAsync()
    {
        try
        {
            await _done(BuildPlan());
        }
        catch (Exception ex)
        {
            SetStatus($"Pre-game setup failed: {ex.Message}", LockedRed);
        }
    }

    private MatchInducementPlan BuildPlan()
    {
        var home = _teamStates[_homeTeam.Id];
        var away = _teamStates[_awayTeam.Id];
        return _preGameService.CreatePlan(
            _ruleset,
            _rosterSet,
            _homeTeam,
            _awayTeam,
            homeBribes: 0,
            awayBribes: 0,
            homeTreasurySpent: home.TreasurySpent,
            awayTreasurySpent: away.TreasurySpent,
            homeInducements: SelectedInducements(home),
            awayInducements: SelectedInducements(away),
            homeStarPlayerIds: home.StarPlayerIds.ToArray(),
            awayStarPlayerIds: away.StarPlayerIds.ToArray());
    }

    private MatchInducementPlan? TryBuildPlan(out string? error)
    {
        var plan = BuildPlan();
        try
        {
            _preGameService.PrepareMatch(_ruleset, _rosterSet, _homeTeam, _awayTeam, plan);
            error = null;
            return plan;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return plan;
        }
    }

    private void UpdateStatus(string? planError)
    {
        if (string.IsNullOrWhiteSpace(planError))
        {
            SetStatus(_step == 0
                ? $"{_firstTeam.Name} is ready to declare treasury-funded inducements."
                : $"{_secondTeam.Name} is spending with recalculated petty cash.");
        }
        else
        {
            SetStatus(planError, LockedRed);
        }
    }

    private void SetStatus(string status, Color? color = null)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = status;
        _statusLabel.AddThemeColorOverride("font_color", color ?? MutedInk);
    }

    private static IReadOnlyList<SelectedInducement> SelectedInducements(TeamDraftState state)
    {
        var selected = state.InducementCounts
            .Where(pair => pair.Value > 0)
            .Select(pair => new SelectedInducement { InducementId = pair.Key, Count = pair.Value })
            .ToList();

        foreach (var inducement in state.Summary.AvailableInducements.Where(current => current.PickerOptions.Count > 0))
        {
            selected.AddRange(inducement.PickerOptions
                .Select(option => new SelectedInducement
                {
                    InducementId = inducement.Id,
                    OptionId = option.Id,
                    Count = state.CountFor(inducement.Id, option.Id)
                })
                .Where(selection => selection.Count > 0));
        }

        return selected;
    }

    private bool StarCanBeAdded(TeamDraftState state, EligibleStarPlayerSummary star, MatchInducementPlan plan)
    {
        var teamPlan = PlanForTeam(plan, state.Team.Id);
        var budget = teamPlan.PettyCash + teamPlan.TreasurySpent;
        var selected = SelectedInducementCost(state) + SelectedStarCost(state);
        return selected + star.Cost <= budget;
    }

    private static int AffordableInducementMax(TeamDraftState state, AvailableInducementSummary inducement, MatchInducementPlan? plan)
    {
        if (inducement.Cost <= 0)
        {
            return 0;
        }

        var currentCount = state.CountFor(inducement.Id);
        var budget = plan is null
            ? state.Summary.PettyCash + state.TreasurySpent
            : PlanForTeam(plan, state.Team.Id).PettyCash + state.TreasurySpent;
        var selectedOther = SelectedInducementCost(state) - (currentCount * inducement.Cost) + SelectedStarCost(state);
        var remainingForThisItem = Math.Max(0, budget - selectedOther);
        return Math.Min(inducement.MaxCount, remainingForThisItem / inducement.Cost);
    }

    private static int AffordablePickerOptionMax(
        TeamDraftState state,
        AvailableInducementSummary inducement,
        InducementOptionSummary option,
        MatchInducementPlan? plan)
    {
        if (option.Cost <= 0)
        {
            return 0;
        }

        var currentCount = state.CountFor(inducement.Id, option.Id);
        var otherFamilyCount = state.PickerCountFor(inducement.Id) - currentCount;
        var familyMaximum = Math.Max(0, inducement.MaxCount - otherFamilyCount);
        var budget = plan is null
            ? state.Summary.PettyCash + state.TreasurySpent
            : PlanForTeam(plan, state.Team.Id).PettyCash + state.TreasurySpent;
        var selectedOther = SelectedInducementCost(state) - (currentCount * option.Cost) + SelectedStarCost(state);
        var affordableMaximum = Math.Max(0, budget - selectedOther) / option.Cost;
        return Math.Min(familyMaximum, affordableMaximum);
    }

    private static TeamInducementPlan PlanForTeam(MatchInducementPlan plan, Guid teamId)
    {
        return plan.Home.TeamId == teamId ? plan.Home : plan.Away;
    }

    private static int SelectedInducementCost(TeamDraftState state)
    {
        var fixedCost = state.InducementCounts.Sum(pair =>
        {
            var definition = state.Summary.AvailableInducements.FirstOrDefault(inducement => string.Equals(inducement.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
            return (definition?.Cost ?? 0) * pair.Value;
        });
        var pickerCost = state.Summary.AvailableInducements.Sum(inducement =>
            inducement.PickerOptions.Sum(option => option.Cost * state.CountFor(inducement.Id, option.Id)));
        return fixedCost + pickerCost;
    }

    private static int SelectedStarCost(TeamDraftState state)
    {
        return state.Summary.EligibleStarPlayers
            .Where(star => state.StarPlayerIds.Contains(star.Id))
            .Sum(star => star.Cost);
    }

    private static (LeagueTeam First, LeagueTeam Second) OrderTeamsForInducements(
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        TeamPreGameSummary homeSummary,
        TeamPreGameSummary awaySummary)
    {
        return homeSummary.TeamValue >= awaySummary.TeamValue
            ? (homeTeam, awayTeam)
            : (awayTeam, homeTeam);
    }

    private string RoleText(LeagueTeam team)
    {
        if (_homeSummary.TeamValue == _awaySummary.TeamValue)
        {
            return team.Id == _firstTeam.Id ? "First declaration" : "Second declaration";
        }

        return team.Id == _firstTeam.Id ? "Higher TV" : "Lower TV";
    }

    private int StarPlayerMaximum()
    {
        return _ruleset.Inducements
            .First(inducement => string.Equals(inducement.Id, "star-player", StringComparison.OrdinalIgnoreCase))
            .MaxCount;
    }

    private static LeagueTeam FindTeam(League league, Guid teamId)
    {
        return league.Teams.FirstOrDefault(team => team.Id == teamId)
            ?? throw new InvalidOperationException("Scheduled match team is not part of this league.");
    }

    private static void AddStat(GridContainer grid, string label, string value, Color? valueColor = null)
    {
        var panel = Panel(new Color("171412"), new Color("4b3e2f"));
        panel.CustomMinimumSize = new Vector2(0, 72);
        var stack = Stack(panel, 3);
        stack.AddChild(Label(label, 11, MutedInk));
        stack.AddChild(Label(value, 17, valueColor ?? ScreenInk));
        grid.AddChild(panel);
    }

    private static Label Label(string text, int fontSize, Color color, bool autowrap = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = autowrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Control LedgerRow(string label, string value, Color? valueColor = null)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(Label(label, 13, MutedInk));
        var valueLabel = Label(value, 13, valueColor ?? ScreenInk);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(valueLabel);
        return row;
    }

    private static Label Pill(string text, Color background, Color foreground)
    {
        var label = Label(text, 12, foreground);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeStyleboxOverride("normal", FlatStyle(background, background.Lightened(0.15f)));
        return label;
    }

    private static Button StyledButton(string text, bool primary = false)
    {
        var button = new Button { Text = text };
        var background = primary ? Gold : PanelBgAlt;
        var foreground = primary ? new Color("16100a") : ScreenInk;
        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", foreground.Lightened(0.08f));
        button.AddThemeStyleboxOverride("normal", FlatStyle(background, primary ? new Color("f0ca65") : Line, primary ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", FlatStyle(background.Lightened(0.12f), Gold, 2));
        button.AddThemeStyleboxOverride("pressed", FlatStyle(background.Darkened(0.12f), Gold, 2));
        button.AddThemeStyleboxOverride("disabled", FlatStyle(new Color("24211d"), new Color("3a342c")));
        return button;
    }

    private static PanelContainer Panel(Color background, Color border, int borderWidth = 1)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(background, border, borderWidth));
        return panel;
    }

    private static VBoxContainer Stack(PanelContainer panel, int separation)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", separation);
        margin.AddChild(stack);
        return stack;
    }

    private static HSeparator Separator()
    {
        var separator = new HSeparator();
        separator.AddThemeStyleboxOverride("separator", FlatStyle(Line, Line));
        return separator;
    }

    private static StyleBoxFlat FlatStyle(Color background, Color? border = null, int borderWidth = 1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border ?? background.Darkened(0.25f),
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = 8,
            ContentMarginTop = 6,
            ContentMarginRight = 8,
            ContentMarginBottom = 6,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusBottomLeft = 4
        };
        return style;
    }

    private SpinBox CreateSpinBox(double min, double max, double value, double step)
    {
        var spinBox = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = Math.Clamp(value, min, max),
            Step = step,
            AllowGreater = false,
            AllowLesser = false
        };
        return spinBox;
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }

    private static string FormatInducementCost(AvailableInducementSummary inducement)
    {
        return inducement.Cost == 0 ? "Price varies" : $"{FormatGold(inducement.Cost)} each";
    }

    private static string FormatLimit(int maxCount)
    {
        return maxCount >= 99 ? "Unlimited" : $"0-{maxCount}";
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"MA {stats.Movement} ST {stats.Strength} AG {stats.Agility}+ PA {stats.Passing}+ AV {stats.Armor}+";
    }

    private void Clear()
    {
        _contentScroll = null;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static void RestoreScrollPosition(ScrollContainer scroll, int scrollVertical)
    {
        if (!GodotObject.IsInstanceValid(scroll))
        {
            return;
        }

        scroll.ScrollVertical = scrollVertical;
    }

    private sealed class TeamDraftState(LeagueTeam team, TeamPreGameSummary summary)
    {
        public LeagueTeam Team { get; } = team;
        public TeamPreGameSummary Summary { get; } = summary;
        public int TreasurySpent { get; set; }
        public Dictionary<string, int> InducementCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, int>> PickerCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> StarPlayerIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int CountFor(string inducementId)
        {
            return InducementCounts.TryGetValue(inducementId, out var count) ? count : 0;
        }

        public int CountFor(string inducementId, string optionId)
        {
            return PickerCounts.TryGetValue(inducementId, out var options) && options.TryGetValue(optionId, out var count)
                ? count
                : 0;
        }

        public int PickerCountFor(string inducementId)
        {
            return PickerCounts.TryGetValue(inducementId, out var options) ? options.Values.Sum() : 0;
        }

        public void SetPickerCount(string inducementId, string optionId, int count)
        {
            if (!PickerCounts.TryGetValue(inducementId, out var options))
            {
                options = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                PickerCounts[inducementId] = options;
            }

            if (count <= 0)
            {
                options.Remove(optionId);
                return;
            }

            options[optionId] = count;
        }
    }
}
