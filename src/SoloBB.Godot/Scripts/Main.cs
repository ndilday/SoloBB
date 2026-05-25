using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts;

public partial class Main : Control
{
    private readonly JsonGameDataStore _store = new();
    private readonly LeagueService _leagueService = new();
    private readonly MatchService _matchService = new();
    private readonly Dictionary<string, SpinBox> _positionCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PitchSquare, Button> _pitchButtons = [];
    private readonly List<MatchupTeamOption> _matchupTeams = [];
    private Label _statusLabel = null!;
    private VBoxContainer _stack = null!;
    private LineEdit _teamNameEdit = null!;
    private LineEdit _coachNameEdit = null!;
    private OptionButton _rosterOption = null!;
    private SpinBox _rerollsSpin = null!;
    private SpinBox _fanFactorSpin = null!;
    private GridContainer _positionGrid = null!;
    private Label _summaryLabel = null!;
    private Button _createLeagueButton = null!;
    private OptionButton _homeTeamOption = null!;
    private OptionButton _awayTeamOption = null!;
    private Button _createMatchButton = null!;
    private Label _matchupSummaryLabel = null!;
    private VBoxContainer _pitchSection = null!;
    private Label _pitchSummaryLabel = null!;
    private Button _advancePhaseButton = null!;
    private OptionButton _reservePlayerOption = null!;
    private GridContainer _pitchGrid = null!;
    private Ruleset? _ruleset;
    private RosterSet? _rosterSet;
    private TeamRoster? _selectedRoster;
    private MatchState? _activeMatch;
    private string? _activeMatchPath;
    private LeagueTeam? _activeHomeTeam;
    private LeagueTeam? _activeAwayTeam;
    private Guid? _selectedPitchPlayerId;

    public override void _Ready()
    {
        _stack = GetNode<VBoxContainer>("Panel/Margin/Scroll/Stack");
        _statusLabel = GetNode<Label>("%StatusLabel");
        _createLeagueButton = GetNode<Button>("%CreateLeagueButton");
        _createLeagueButton.Text = "Save Team";
        _createLeagueButton.Disabled = true;
        _createLeagueButton.Pressed += async () => await SaveTeamAsync();
        BuildTeamBuilder();
        BuildMatchupBuilder();
        BuildPitchDisplay();
        _ = LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            _ruleset = await _store.LoadRulesetAsync(ProjectPath("data/rulesets/bb2020-lite.json"));
            var rosterSets = await _store.LoadRosterSetsAsync(ProjectPath("data/rosters"), _ruleset);
            _rosterSet = rosterSets.FirstOrDefault()
                ?? throw new InvalidDataException("No roster sets are available for the loaded ruleset.");

            PopulateRosterOptions();
            _statusLabel.Text = $"Loaded {_ruleset.Name} with {rosterSets.Sum(set => set.Rosters.Count)} rosters.";
            _createLeagueButton.Disabled = false;
            UpdateDraftSummary();
            await RefreshMatchupTeamsAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Data load failed: {ex.Message}";
        }
    }

    private void BuildTeamBuilder()
    {
        _teamNameEdit = AddLineEdit("TeamNameEdit", "Team name", "Reikland Rehearsal");
        _coachNameEdit = AddLineEdit("CoachNameEdit", "Coach name", "Hotseat");

        _rosterOption = new OptionButton { Name = "RosterOption" };
        _rosterOption.ItemSelected += _ => SelectRosterFromOption();
        _stack.AddChild(_rosterOption);

        var economyGrid = new GridContainer
        {
            Name = "EconomyGrid",
            Columns = 2
        };
        _stack.AddChild(economyGrid);

        economyGrid.AddChild(new Label { Text = "Rerolls" });
        _rerollsSpin = CreateSpinBox("RerollsSpin", 0, 8, 2);
        economyGrid.AddChild(_rerollsSpin);

        economyGrid.AddChild(new Label { Text = "Fan factor" });
        _fanFactorSpin = CreateSpinBox("FanFactorSpin", 0, 9, 0);
        economyGrid.AddChild(_fanFactorSpin);

        _positionGrid = new GridContainer
        {
            Name = "PositionGrid",
            Columns = 5
        };
        _stack.AddChild(_positionGrid);

        _summaryLabel = new Label
        {
            Name = "SummaryLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _stack.AddChild(_summaryLabel);
    }

    private void BuildMatchupBuilder()
    {
        _stack.AddChild(new HSeparator());
        var matchupTitle = new Label
        {
            Text = "Matchup",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        matchupTitle.AddThemeFontSizeOverride("font_size", 24);
        _stack.AddChild(matchupTitle);

        var matchupGrid = new GridContainer
        {
            Name = "MatchupGrid",
            Columns = 2
        };
        _stack.AddChild(matchupGrid);

        matchupGrid.AddChild(new Label { Text = "Home" });
        _homeTeamOption = new OptionButton { Name = "HomeTeamOption" };
        _homeTeamOption.ItemSelected += _ => UpdateMatchupSummary();
        matchupGrid.AddChild(_homeTeamOption);

        matchupGrid.AddChild(new Label { Text = "Away" });
        _awayTeamOption = new OptionButton { Name = "AwayTeamOption" };
        _awayTeamOption.ItemSelected += _ => UpdateMatchupSummary();
        matchupGrid.AddChild(_awayTeamOption);

        _matchupSummaryLabel = new Label
        {
            Name = "MatchupSummaryLabel",
            Text = "Save at least two teams to create a matchup.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _stack.AddChild(_matchupSummaryLabel);

        _createMatchButton = new Button
        {
            Name = "CreateMatchButton",
            Text = "Create Match",
            Disabled = true
        };
        _createMatchButton.Pressed += async () => await CreateMatchAsync();
        _stack.AddChild(_createMatchButton);
    }

    private LineEdit AddLineEdit(string name, string placeholder, string text)
    {
        var edit = new LineEdit
        {
            Name = name,
            PlaceholderText = placeholder,
            Text = text
        };
        edit.TextChanged += _ => UpdateDraftSummary();
        _stack.AddChild(edit);
        return edit;
    }

    private SpinBox CreateSpinBox(string name, double min, double max, double value)
    {
        var spinBox = new SpinBox
        {
            Name = name,
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false
        };
        spinBox.ValueChanged += _ => UpdateDraftSummary();
        return spinBox;
    }

    private void PopulateRosterOptions()
    {
        _rosterOption.Clear();
        if (_rosterSet is null)
        {
            return;
        }

        for (var i = 0; i < _rosterSet.Rosters.Count; i++)
        {
            _rosterOption.AddItem(_rosterSet.Rosters[i].Name, i);
        }

        _rosterOption.Selected = 0;
        SelectRosterFromOption();
    }

    private void SelectRosterFromOption()
    {
        if (_rosterSet is null || _rosterOption.Selected < 0)
        {
            return;
        }

        _selectedRoster = _rosterSet.Rosters[_rosterOption.Selected];
        _rerollsSpin.MaxValue = _ruleset?.RerollCap ?? 8;
        BuildPositionRows();
        UpdateDraftSummary();
    }

    private void BuildPositionRows()
    {
        foreach (var child in _positionGrid.GetChildren())
        {
            child.QueueFree();
        }

        _positionCounts.Clear();
        if (_selectedRoster is null)
        {
            return;
        }

        AddPositionHeader("Position");
        AddPositionHeader("Cost");
        AddPositionHeader("Stats");
        AddPositionHeader("Skills");
        AddPositionHeader("Count");

        foreach (var position in _selectedRoster.Positions)
        {
            _positionGrid.AddChild(new Label { Text = position.Name });
            _positionGrid.AddChild(new Label { Text = FormatGold(position.Cost) });
            _positionGrid.AddChild(new Label { Text = FormatStats(position.Stats) });
            _positionGrid.AddChild(new Label { Text = position.StartingSkills.Count == 0 ? "-" : string.Join(", ", position.StartingSkills) });

            var count = CreateSpinBox($"{position.Id}Count", position.Min, position.Max, position.Id == "lineman" ? 11 : position.Min);
            _positionCounts[position.Id] = count;
            _positionGrid.AddChild(count);
        }
    }

    private void AddPositionHeader(string text)
    {
        _positionGrid.AddChild(new Label
        {
            Text = text,
            ThemeTypeVariation = "HeaderSmall"
        });
    }

    private async Task SaveTeamAsync()
    {
        try
        {
            if (_ruleset is null || _rosterSet is null || _selectedRoster is null)
            {
                throw new InvalidOperationException("Catalog data is not loaded.");
            }

            var league = _leagueService.CreateLeague("Solo Hotseat League", _ruleset, [_rosterSet]);
            var draft = CreateDraft(_selectedRoster);
            league = _leagueService.AddTeam(
                league,
                _ruleset,
                _teamNameEdit.Text,
                _coachNameEdit.Text,
                _selectedRoster,
                draft,
                rerolls: (int)_rerollsSpin.Value,
                fanFactor: (int)_fanFactorSpin.Value);

            var saveName = Slugify(league.Teams[0].Name);
            await _store.SaveLeagueAsync(ProjectPath($"user://leagues/{saveName}.json"), league);
            _statusLabel.Text = $"Saved '{league.Teams[0].Name}' with {league.Teams[0].Players.Count} players.";
            await RefreshMatchupTeamsAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Team save failed: {ex.Message}";
        }
    }

    private async Task RefreshMatchupTeamsAsync()
    {
        _matchupTeams.Clear();
        _homeTeamOption.Clear();
        _awayTeamOption.Clear();

        var leagues = await _store.LoadLeaguesAsync(ProjectPath("user://leagues"));
        foreach (var league in leagues.Where(league => string.Equals(league.RulesetId, _ruleset?.Id, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var team in league.Teams)
            {
                _matchupTeams.Add(new MatchupTeamOption(league, team));
            }
        }

        for (var i = 0; i < _matchupTeams.Count; i++)
        {
            var option = _matchupTeams[i];
            var text = $"{option.Team.Name} ({option.Team.CoachName})";
            _homeTeamOption.AddItem(text, i);
            _awayTeamOption.AddItem(text, i);
        }

        if (_matchupTeams.Count > 1)
        {
            _homeTeamOption.Selected = 0;
            _awayTeamOption.Selected = 1;
        }

        UpdateMatchupSummary();
    }

    private async Task CreateMatchAsync()
    {
        try
        {
            if (_ruleset is null)
            {
                throw new InvalidOperationException("Catalog data is not loaded.");
            }

            var home = GetSelectedMatchupTeam(_homeTeamOption);
            var away = GetSelectedMatchupTeam(_awayTeamOption);
            var match = _matchService.CreateHotseatMatch(_ruleset, home, away);
            var saveName = $"{Slugify(home.Name)}-vs-{Slugify(away.Name)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            var matchPath = ProjectPath($"user://matches/{saveName}.json");

            await _store.SaveMatchAsync(matchPath, match);
            _activeMatch = match;
            _activeMatchPath = matchPath;
            _activeHomeTeam = home;
            _activeAwayTeam = away;
            ShowPitchDisplay();
            _statusLabel.Text = $"Created match: {home.Name} vs {away.Name}.";
            _matchupSummaryLabel.Text = $"Saved match with {match.Placements.Count} players in reserve.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Match creation failed: {ex.Message}";
        }
    }

    private LeagueTeam GetSelectedMatchupTeam(OptionButton option)
    {
        if (option.Selected < 0 || option.Selected >= _matchupTeams.Count)
        {
            throw new InvalidOperationException("Select a team for both sides.");
        }

        return _matchupTeams[option.Selected].Team;
    }

    private void UpdateMatchupSummary()
    {
        if (_matchupSummaryLabel is null || _createMatchButton is null)
        {
            return;
        }

        if (_matchupTeams.Count < 2)
        {
            _matchupSummaryLabel.Text = $"Save {2 - _matchupTeams.Count} more team{(_matchupTeams.Count == 1 ? "" : "s")} to create a matchup.";
            _createMatchButton.Disabled = true;
            return;
        }

        var home = _homeTeamOption.Selected >= 0 && _homeTeamOption.Selected < _matchupTeams.Count
            ? _matchupTeams[_homeTeamOption.Selected].Team
            : null;
        var away = _awayTeamOption.Selected >= 0 && _awayTeamOption.Selected < _matchupTeams.Count
            ? _matchupTeams[_awayTeamOption.Selected].Team
            : null;
        var isReady = home is not null && away is not null && home.Id != away.Id;

        _matchupSummaryLabel.Text = isReady && home is not null && away is not null
            ? $"{home.Name} vs {away.Name}. Both teams are ready for setup."
            : "Choose two different teams.";
        _createMatchButton.Disabled = !isReady;
    }

    private void BuildPitchDisplay()
    {
        _stack.AddChild(new HSeparator());
        _pitchSection = new VBoxContainer
        {
            Name = "PitchSection",
            Visible = false
        };
        _stack.AddChild(_pitchSection);

        var pitchTitle = new Label
        {
            Text = "Pitch Setup",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        pitchTitle.AddThemeFontSizeOverride("font_size", 24);
        _pitchSection.AddChild(pitchTitle);

        _pitchSummaryLabel = new Label
        {
            Name = "PitchSummaryLabel",
            Text = "Create a match to show the pitch.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _pitchSection.AddChild(_pitchSummaryLabel);

        _advancePhaseButton = new Button
        {
            Name = "AdvancePhaseButton",
            Text = "Advance Phase"
        };
        _advancePhaseButton.Pressed += async () => await AdvanceMatchPhaseAsync();
        _pitchSection.AddChild(_advancePhaseButton);

        _reservePlayerOption = new OptionButton { Name = "ReservePlayerOption" };
        _pitchSection.AddChild(_reservePlayerOption);

        _pitchGrid = new GridContainer
        {
            Name = "PitchGrid",
            Columns = 26
        };
        _pitchSection.AddChild(_pitchGrid);
    }

    private void ShowPitchDisplay()
    {
        if (_ruleset is null || _activeMatch is null)
        {
            return;
        }

        _pitchSection.Visible = true;
        BuildPitchGrid(_ruleset);
        PopulateReservePlayerOptions();
        RefreshPitchDisplay();
    }

    private void BuildPitchGrid(Ruleset ruleset)
    {
        if (_pitchButtons.Count == ruleset.PitchWidth * ruleset.PitchHeight)
        {
            return;
        }

        foreach (var child in _pitchGrid.GetChildren())
        {
            child.QueueFree();
        }

        _pitchButtons.Clear();
        _pitchGrid.Columns = ruleset.PitchWidth;
        for (var y = 0; y < ruleset.PitchHeight; y++)
        {
            for (var x = 0; x < ruleset.PitchWidth; x++)
            {
                var square = new PitchSquare(x, y);
                var button = new Button
                {
                    Text = "",
                    CustomMinimumSize = new Vector2(24, 24),
                    TooltipText = $"{x + 1},{y + 1}"
                };
                button.Pressed += async () => await HandlePitchSquareAsync(square);
                _pitchButtons[square] = button;
                _pitchGrid.AddChild(button);
            }
        }
    }

    private void PopulateReservePlayerOptions()
    {
        _reservePlayerOption.Clear();
        if (_activeMatch is null)
        {
            return;
        }

        var reservePlacements = _activeMatch.Placements
            .Where(placement => placement.State == PlayerPitchState.Reserve)
            .Where(placement => _activeMatch.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup) || placement.TeamId == _activeMatch.ActiveTeamId)
            .OrderBy(placement => placement.TeamId == _activeMatch.HomeTeamId ? 0 : 1)
            .ThenBy(placement => FindPlayer(placement.PlayerId)?.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var i = 0; i < reservePlacements.Length; i++)
        {
            var placement = reservePlacements[i];
            var player = FindPlayer(placement.PlayerId);
            var team = FindTeam(placement.TeamId);
            _reservePlayerOption.AddItem($"{(team?.Id == _activeMatch.HomeTeamId ? "Home" : "Away")}: {player?.Name ?? "Unknown"}", i);
            _reservePlayerOption.SetItemMetadata(i, Variant.From(placement.PlayerId.ToString()));
        }
    }

    private async Task AdvanceMatchPhaseAsync()
    {
        try
        {
            if (_activeMatch is null)
            {
                throw new InvalidOperationException("Create a match before advancing phases.");
            }

            var previousPhase = _activeMatch.Phase;
            _activeMatch = _matchService.AdvancePhase(_activeMatch);
            if (_activeMatch.Phase == previousPhase)
            {
                _activeMatch = _ruleset is null ? _activeMatch : _matchService.AdvanceTurn(_activeMatch, _ruleset);
            }

            _selectedPitchPlayerId = null;
            await SaveActiveMatchAsync();
            PopulateReservePlayerOptions();
            RefreshPitchDisplay();
            _statusLabel.Text = $"Advanced to {FormatPhase(_activeMatch.Phase)}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Advance failed: {ex.Message}";
        }
    }

    private async Task HandlePitchSquareAsync(PitchSquare square)
    {
        try
        {
            if (_ruleset is null || _activeMatch is null)
            {
                throw new InvalidOperationException("Create a match before using the pitch.");
            }

            var occupiedPlacement = FindPlacementAt(square);
            if (occupiedPlacement is not null)
            {
                if (_activeMatch.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
                {
                    _statusLabel.Text = "Movement starts after kickoff.";
                    return;
                }

                _selectedPitchPlayerId = occupiedPlacement.PlayerId;
                RefreshPitchDisplay();
                var selectedPlayer = FindPlayer(occupiedPlacement.PlayerId);
                _statusLabel.Text = $"Selected {selectedPlayer?.Name ?? "player"} for movement.";
                return;
            }

            if (_selectedPitchPlayerId is not null)
            {
                await MoveSelectedPlayerAsync(square);
                return;
            }

            if (_reservePlayerOption.Selected < 0)
            {
                throw new InvalidOperationException("Select a reserve player first.");
            }

            var playerId = Guid.Parse(_reservePlayerOption.GetItemMetadata(_reservePlayerOption.Selected).AsString());
            _activeMatch = _matchService.PlacePlayer(_activeMatch, _ruleset, playerId, square);
            await SaveActiveMatchAsync();
            PopulateReservePlayerOptions();
            RefreshPitchDisplay();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Placement failed: {ex.Message}";
        }
    }

    private async Task MoveSelectedPlayerAsync(PitchSquare destination)
    {
        if (_ruleset is null || _activeMatch is null || _selectedPitchPlayerId is null)
        {
            return;
        }

        var placement = _activeMatch.Placements.FirstOrDefault(current => current.PlayerId == _selectedPitchPlayerId.Value)
            ?? throw new InvalidOperationException("Selected player is not part of this match.");
        var team = FindTeam(placement.TeamId)
            ?? throw new InvalidOperationException("Could not find the selected player's team.");

        _activeMatch = _matchService.MovePlayer(_activeMatch, _ruleset, team, _selectedPitchPlayerId.Value, destination);
        await SaveActiveMatchAsync();
        var player = FindPlayer(_selectedPitchPlayerId.Value);
        _selectedPitchPlayerId = null;
        PopulateReservePlayerOptions();
        RefreshPitchDisplay();
        _statusLabel.Text = $"Moved {player?.Name ?? "player"}.";
    }

    private void RefreshPitchDisplay()
    {
        if (_ruleset is null || _activeMatch is null)
        {
            return;
        }

        foreach (var button in _pitchButtons.Values)
        {
            button.Text = "";
            button.Disabled = false;
            button.TooltipText = "";
        }

        foreach (var placement in _activeMatch.Placements.Where(placement => placement.Square is not null))
        {
            var square = placement.Square!;
            if (!_pitchButtons.TryGetValue(square, out var button))
            {
                continue;
            }

            var player = FindPlayer(placement.PlayerId);
            var isHome = placement.TeamId == _activeMatch.HomeTeamId;
            var isSelected = _selectedPitchPlayerId == placement.PlayerId;
            var isBallCarrier = _activeMatch.Ball.CarrierPlayerId == placement.PlayerId;
            button.Text = isBallCarrier ? (isHome ? "HB" : "AB") : isSelected ? "*" : isHome ? "H" : "A";
            button.TooltipText = $"{player?.Name ?? "Unknown"} ({(isHome ? "Home" : "Away")})";
        }

        if (_activeMatch.Ball.Square is PitchSquare ballSquare && _pitchButtons.TryGetValue(ballSquare, out var ballButton))
        {
            ballButton.Text = "B";
            ballButton.TooltipText = "Ball";
        }

        var placed = _activeMatch.Placements.Count(placement => placement.Square is not null);
        var homePlaced = _activeMatch.Placements.Count(placement => placement.TeamId == _activeMatch.HomeTeamId && placement.Square is not null);
        var awayPlaced = _activeMatch.Placements.Count(placement => placement.TeamId == _activeMatch.AwayTeamId && placement.Square is not null);
        var selectedPlayer = _selectedPitchPlayerId is Guid selectedId ? FindPlayer(selectedId) : null;
        var phaseText = $"{FormatPhase(_activeMatch.Phase)}: {FindTeam(_activeMatch.ActiveTeamId)?.Name ?? "No team"} active.";
        var scoreText = $"Score {_activeHomeTeam?.Name} {_activeMatch.HomeScore}-{_activeMatch.AwayScore} {_activeAwayTeam?.Name}.";
        var turnText = $"Half {_activeMatch.Half}, turns {_activeHomeTeam?.Name} {_activeMatch.HomeTurn}/{_ruleset.TurnsPerHalf}, {_activeAwayTeam?.Name} {_activeMatch.AwayTurn}/{_ruleset.TurnsPerHalf}.";
        var ballText = FormatBallState(_activeMatch);
        var actionText = _activeMatch.Phase switch
        {
            MatchPhase.DefenseSetup => "Place defensive reserves.",
            MatchPhase.OffenseSetup => "Place offensive reserves.",
            MatchPhase.Kickoff => "Advance to begin the offensive turn.",
            MatchPhase.OffensivePlayerTurn => selectedPlayer is null ? "Click a placed offensive player to move." : $"Moving {selectedPlayer.Name}: click an empty square.",
            MatchPhase.DefensiveTurn => selectedPlayer is null ? "Click a placed defensive player to move." : $"Moving {selectedPlayer.Name}: click an empty square.",
            MatchPhase.EndOfHalf => "Advance to begin next setup.",
            MatchPhase.Complete => "Match complete.",
            _ => ""
        };
        _advancePhaseButton.Disabled = _activeMatch.Phase is MatchPhase.Complete;
        _pitchSummaryLabel.Text = $"{scoreText} {turnText} {phaseText} {ballText} {_activeHomeTeam?.Name} {homePlaced}/{_ruleset.PlayersPerSide} vs {_activeAwayTeam?.Name} {awayPlaced}/{_ruleset.PlayersPerSide}. {placed} players placed. {actionText}";
    }

    private PlayerPlacement? FindPlacementAt(PitchSquare square)
    {
        return _activeMatch?.Placements.FirstOrDefault(placement => placement.Square == square);
    }

    private Task SaveActiveMatchAsync()
    {
        if (_activeMatch is null)
        {
            return Task.CompletedTask;
        }

        return _store.SaveMatchAsync(_activeMatchPath ?? ProjectPath($"user://matches/active-{_activeMatch.Id}.json"), _activeMatch);
    }

    private LeagueTeam? FindTeam(Guid teamId)
    {
        if (_activeHomeTeam?.Id == teamId)
        {
            return _activeHomeTeam;
        }

        if (_activeAwayTeam?.Id == teamId)
        {
            return _activeAwayTeam;
        }

        return null;
    }

    private Player? FindPlayer(Guid playerId)
    {
        return _activeHomeTeam?.Players.Concat(_activeAwayTeam?.Players ?? []).FirstOrDefault(player => player.Id == playerId);
    }

    private IReadOnlyList<PlayerDraftPick> CreateDraft(TeamRoster roster)
    {
        var draft = new List<PlayerDraftPick>();
        foreach (var position in roster.Positions)
        {
            var count = _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0;
            for (var i = 1; i <= count; i++)
            {
                draft.Add(new PlayerDraftPick($"{position.Name} {i}", position.Id));
            }
        }

        return draft;
    }

    private void UpdateDraftSummary()
    {
        if (_ruleset is null || _selectedRoster is null || _summaryLabel is null)
        {
            return;
        }

        var playerCount = 0;
        var playerCost = 0;
        foreach (var position in _selectedRoster.Positions)
        {
            var count = _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0;
            playerCount += count;
            playerCost += count * position.Cost;
        }

        var rerollCost = (int)_rerollsSpin.Value * _selectedRoster.RerollCost;
        var totalCost = playerCost + rerollCost;
        var treasury = _ruleset.StartingTreasury - totalCost;
        var isReady = playerCount == _ruleset.PlayersPerSide && treasury >= 0;
        var status = isReady ? "Ready" : "Needs work";

        _summaryLabel.Text = $"{status}: {playerCount}/{_ruleset.PlayersPerSide} players, cost {FormatGold(totalCost)}, treasury {FormatGold(treasury)}.";
        _createLeagueButton.Disabled = !isReady;
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"MA {stats.Movement} ST {stats.Strength} AG {stats.Agility}+ PA {stats.Passing}+ AV {stats.Armor}+";
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }

    private static string FormatPhase(MatchPhase phase)
    {
        return phase switch
        {
            MatchPhase.DefenseSetup => "Defense Placement",
            MatchPhase.OffenseSetup => "Offense Placement",
            MatchPhase.Kickoff => "Kickoff",
            MatchPhase.OffensivePlayerTurn => "Offensive Player Turn",
            MatchPhase.DefensiveTurn => "Defensive Turn",
            MatchPhase.EndOfHalf => "End of Half",
            MatchPhase.Complete => "Complete",
            _ => phase.ToString()
        };
    }

    private string FormatBallState(MatchState match)
    {
        if (match.Ball.CarrierPlayerId is Guid carrierId)
        {
            return $"Ball: {FindPlayer(carrierId)?.Name ?? "carried"}.";
        }

        if (match.Ball.Square is PitchSquare square)
        {
            return $"Ball: {square.X + 1},{square.Y + 1}.";
        }

        return "Ball: not in play.";
    }

    private static string Slugify(string value)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        return string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ProjectPath(string path)
    {
        return ProjectSettings.GlobalizePath(path.StartsWith("user://", StringComparison.Ordinal) ? path : $"res://{path}");
    }

    private sealed record MatchupTeamOption(League League, LeagueTeam Team);
}
