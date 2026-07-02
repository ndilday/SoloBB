using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;
using SoloBB.Godot.Scripts.Screens;

namespace SoloBB.Godot.Scripts;

public partial class Main : Control
{
    private const int ForegroundMaxFps = 60;
    private const int BackgroundMaxFps = 15;
    private const int BackgroundSleepUsec = 50000;
    private const int DefaultShellMargin = 24;
    private const int CompactShellMargin = 6;
    private const int MatchShellMargin = 6;
    private const int DefaultMatchStackSeparation = 12;
    private const int CompactMatchStackSeparation = 4;

    private readonly JsonGameDataStore _store = new();
    private readonly LeagueService _leagueService = new();
    private readonly MatchService _matchService = new();
    private readonly PreGameService _preGameService = new();
    private readonly GmAiService _gmAiService = new();

    private MarginContainer _contentMargin = null!;
    private ScrollContainer _shellScroll = null!;
    private VBoxContainer _scrollStack = null!;
    private VBoxContainer _matchStack = null!;
    private Ruleset? _ruleset;
    private RosterSet? _rosterSet;
    private League? _activeLeague;
    private string? _activeLeaguePath;
    private MatchState? _activeMatch;
    private string? _activeMatchPath;
    private LeagueTeam? _activeHomeTeam;
    private LeagueTeam? _activeAwayTeam;
    private Guid? _activeScheduledMatchId;
    private int _targetTeamCount = 2;
    private string _catalogStatus = "Loading rulesets...";
    private Action _teamEditingBack = () => { };

    public override void _Ready()
    {
        ApplyForegroundPerformanceMode();
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("111816"), ScreenStyles.PanelBorder, 2));
        _contentMargin = GetNode<MarginContainer>("Panel/Margin");
        _shellScroll = GetNode<ScrollContainer>("Panel/Margin/Scroll");
        _scrollStack = GetNode<VBoxContainer>("Panel/Margin/Scroll/Stack");
        _matchStack = GetNode<VBoxContainer>("Panel/Margin/MatchStack");
        _scrollStack.SizeFlagsVertical = SizeFlags.Fill;
        _matchStack.SizeFlagsVertical = SizeFlags.ExpandFill;
        ShowMainMenu();
        _ = LoadCatalogAsync();
    }

    public override void _Notification(int what)
    {
        switch (what)
        {
            case (int)NotificationApplicationFocusIn:
                ApplyForegroundPerformanceMode();
                break;
            case (int)NotificationApplicationFocusOut:
                ApplyBackgroundPerformanceMode();
                break;
        }
    }

    private static void ApplyForegroundPerformanceMode()
    {
        Engine.MaxFps = ForegroundMaxFps;
        OS.LowProcessorUsageMode = false;
    }

    private static void ApplyBackgroundPerformanceMode()
    {
        Engine.MaxFps = BackgroundMaxFps;
        OS.LowProcessorUsageModeSleepUsec = BackgroundSleepUsec;
        OS.LowProcessorUsageMode = true;
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            _ruleset = await _store.LoadRulesetAsync(ProjectPath("data/rulesets/bb2020-lite.json"));
            var rosterSets = await _store.LoadRosterSetsAsync(ProjectPath("data/rosters"), _ruleset);
            _rosterSet = rosterSets.FirstOrDefault()
                ?? throw new InvalidDataException("No roster sets are available for the loaded ruleset.");

            _catalogStatus = $"Loaded {_ruleset.Name} with {rosterSets.Sum(set => set.Rosters.Count)} rosters.";
            if (CurrentScreen is MainMenuScreen menu)
            {
                menu.SetStatus(_catalogStatus);
            }
        }
        catch (Exception ex)
        {
            _catalogStatus = $"Data load failed: {ex.Message}";
            if (CurrentScreen is MainMenuScreen menu)
            {
                menu.SetStatus(_catalogStatus);
            }
        }
    }

    private Control? CurrentScreen =>
        _matchStack.Visible && _matchStack.GetChildCount() > 0
            ? _matchStack.GetChild<Control>(0)
            : _scrollStack.GetChildCount() > 0
                ? _scrollStack.GetChild<Control>(0)
                : null;

    private void ShowMainMenu()
    {
        var screen = ShowScreen<MainMenuScreen>("res://scenes/screens/main_menu_screen.tscn");
        screen.Setup(
            newLeague: ShowLeagueSetupForNewLeague,
            loadLeague: async () => await ShowLoadLeagueScreenAsync(),
            quit: () => GetTree().Quit(),
            status: _catalogStatus);
    }

    private async Task ShowLoadLeagueScreenAsync()
    {
        var screen = ShowScreen<LoadLeagueScreen>("res://scenes/screens/load_league_screen.tscn");
        var leagues = await _store.LoadLeaguesAsync(ProjectPath("user://leagues"));
        screen.Setup(
            leagues: leagues,
            loadLeague: league => LoadSelectedLeague(league, screen),
            deleteLeague: async league => await DeleteLeagueAsync(league, screen),
            back: ShowMainMenu);
    }

    private void LoadSelectedLeague(League league, LoadLeagueScreen screen)
    {
        try
        {
            _activeLeague = league;
            _activeLeaguePath = ProjectPath($"user://leagues/{Slugify(league.Name)}-{league.Id:N}.json");
            _targetTeamCount = Math.Max(Math.Max(league.TargetTeamCount, league.Teams.Count), 2);
            ShowLeagueHomeScreen();
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Load failed: {ex.Message}");
        }
    }

    private async Task DeleteLeagueAsync(League league, LoadLeagueScreen screen)
    {
        try
        {
            var path = ProjectPath($"user://leagues/{Slugify(league.Name)}-{league.Id:N}.json");
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            var leagues = await _store.LoadLeaguesAsync(ProjectPath("user://leagues"));
            screen.Setup(
                leagues: leagues,
                loadLeague: l => LoadSelectedLeague(l, screen),
                deleteLeague: async l => await DeleteLeagueAsync(l, screen),
                back: ShowMainMenu);
            screen.SetStatus($"Deleted '{league.Name}'.");
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Delete failed: {ex.Message}");
        }
    }

    private void ShowLeagueSetupForNewLeague()
    {
        if (_ruleset is null || _rosterSet is null)
        {
            ShowMainMenu();
            if (CurrentScreen is MainMenuScreen menu)
            {
                menu.SetStatus("Catalog data is still loading.");
            }

            return;
        }

        _targetTeamCount = Math.Max(2, _targetTeamCount + (_targetTeamCount % 2));
        _activeLeague = _leagueService.CreateLeague("Solo Hotseat League", _ruleset, [_rosterSet], targetTeamCount: _targetTeamCount);
        _activeLeaguePath = null;
        ShowLeagueTeamsScreen();
    }

    private void CommitLeagueSetup(string leagueName, int teamCount)
    {
        if (_activeLeague is null)
        {
            return;
        }

        _targetTeamCount = teamCount;
        var trimmedName = leagueName.Trim();
        _activeLeague = _activeLeague with
        {
            Name = string.IsNullOrEmpty(trimmedName) ? _activeLeague.Name : trimmedName,
            TargetTeamCount = teamCount
        };
    }

    private void ShowLeagueTeamsScreen()
    {
        if (_activeLeague is null)
        {
            ShowMainMenu();
            return;
        }

        var screen = ShowScreen<LeagueTeamsScreen>("res://scenes/screens/league_teams_screen.tscn");
        screen.Setup(
            league: _activeLeague,
            rosterSet: _rosterSet,
            commit: CommitLeagueSetup,
            createTeam: ShowTeamCreationScreen,
            createAiTeam: async rosterId => await AddAiTeamAsync(rosterId, screen),
            startLeague: async () => await CreateLeagueSeasonAsync(screen),
            editTeam: ShowTeamEditingScreen,
            deleteTeam: async teamId => await DeleteTeamAsync(teamId, screen),
            back: ShowMainMenu);
    }

    private void ShowTeamCreationScreen()
    {
        if (_activeLeague is null)
        {
            ShowMainMenu();
            return;
        }

        if (_ruleset is null || _rosterSet is null)
        {
            ShowMainMenu();
            return;
        }

        var screen = ShowScreen<TeamCreationScreen>("res://scenes/screens/team_creation_screen.tscn");
        screen.Setup(
            ruleset: _ruleset,
            rosterSet: _rosterSet,
            defaultTeamName: $"Team {_activeLeague.Teams.Count + 1}",
            saveTeam: async request => await SaveTeamAsync(request, screen),
            back: ShowLeagueTeamsScreen);
    }

    private async Task AddAiTeamAsync(string? rosterId, LeagueTeamsScreen screen)
    {
        try
        {
            if (_ruleset is null || _rosterSet is null || _activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            if (_activeLeague.Teams.Count >= _targetTeamCount)
            {
                throw new InvalidOperationException("This league is already full.");
            }

            var roster = rosterId is null
                ? _gmAiService.PickRandomRoster(_rosterSet)
                : _rosterSet.Rosters.FirstOrDefault(current => string.Equals(current.Id, rosterId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Roster '{rosterId}' is not available.");

            var plan = _gmAiService.PlanInitialTeam(
                _ruleset,
                roster,
                personality: _gmAiService.RollPersonality(),
                teamName: _gmAiService.GenerateTeamName(_activeLeague.Teams.Select(team => team.Name)));
            _activeLeague = _gmAiService.AddPlannedTeam(_activeLeague, _ruleset, plan);

            await SaveActiveLeagueAsync();
            ShowLeagueTeamsScreen();
            if (CurrentScreen is LeagueTeamsScreen leagueTeamsScreen)
            {
                var savedTeam = _activeLeague.Teams.Last();
                leagueTeamsScreen.SetStatus($"AI GM {savedTeam.CoachName} ({plan.Personality.Describe()}) drafted '{savedTeam.Name}' ({roster.Name}) with {savedTeam.Players.Count} players.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"AI team creation failed: {ex.Message}");
        }
    }

    private async Task CreateLeagueSeasonAsync(LeagueTeamsScreen screen)
    {
        try
        {
            if (_activeLeague is null)
            {
                return;
            }

            _activeLeague = _leagueService.CreateSeason(_activeLeague);
            await SaveActiveLeagueAsync();
            ShowLeagueHomeScreen();
        }
        catch (Exception ex)
        {
            screen.SetStatus($"League creation failed: {ex.Message}");
        }
    }

    private void ShowLeagueHomeScreen()
    {
        if (_activeLeague is null)
        {
            ShowMainMenu();
            return;
        }

        var screen = ShowScreen<LeagueHomeScreen>("res://scenes/screens/league_home_screen.tscn");
        screen.Setup(
            league: _activeLeague,
            openTeam: ShowLeagueTeamEditingScreen,
            playGame: ShowPreGameScreen,
            back: _activeLeague.Seasons.Any() ? ShowMainMenu : ShowLeagueTeamsScreen);
    }

    private void ShowPreGameScreen(Guid scheduledMatchId)
    {
        if (_activeLeague is null || _ruleset is null || _rosterSet is null)
        {
            return;
        }

        var scheduledMatch = _activeLeague.Seasons
            .SelectMany(season => season.Schedule)
            .FirstOrDefault(match => match.Id == scheduledMatchId);
        if (scheduledMatch is null)
        {
            return;
        }

        var screen = ShowScreen<PreGameScreen>("res://scenes/screens/pre_game_screen.tscn");
        screen.Setup(
            ruleset: _ruleset,
            rosterSet: _rosterSet,
            league: _activeLeague,
            scheduledMatch: scheduledMatch,
            done: async inducements => await StartScheduledMatchAsync(scheduledMatch, inducements),
            back: ShowLeagueHomeScreen);
    }

    private async Task StartScheduledMatchAsync(ScheduledMatch scheduledMatch, MatchInducementPlan? inducements = null)
    {
        if (_ruleset is null || _rosterSet is null || _activeLeague is null)
        {
            return;
        }

        var homeTeam = _activeLeague.Teams.First(team => team.Id == scheduledMatch.HomeTeamId);
        var awayTeam = _activeLeague.Teams.First(team => team.Id == scheduledMatch.AwayTeamId);
        var preGame = _preGameService.PrepareMatch(_ruleset, _rosterSet, homeTeam, awayTeam, inducements);
        _activeScheduledMatchId = scheduledMatch.Id;
        _activeMatch = _matchService.CreateHotseatMatch(_ruleset, preGame.HomeTeam, preGame.AwayTeam, preGame.Inducements.Home, preGame.Inducements.Away, preGame.Prayers);
        _activeHomeTeam = preGame.HomeTeam;
        _activeAwayTeam = preGame.AwayTeam;
        _activeMatchPath = ProjectPath($"user://matches/{Slugify(homeTeam.Name)}-vs-{Slugify(awayTeam.Name)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
        await SaveActiveMatchAsync(_activeMatch);
        ShowMatchScreen();
    }

    private void ShowMatchScreen()
    {
        if (_ruleset is null || _activeMatch is null || _activeHomeTeam is null || _activeAwayTeam is null)
        {
            ShowLeagueHomeScreen();
            return;
        }

        var screen = ShowScreen<MatchScreen>("res://scenes/screens/match_screen.tscn");
        screen.Setup(
            ruleset: _ruleset,
            match: _activeMatch,
            homeTeam: _activeHomeTeam,
            awayTeam: _activeAwayTeam,
            saveMatch: SaveActiveMatchAsync,
            back: ShowLeagueHomeScreen);
    }

    private void ShowTeamEditingScreen(Guid teamId)
    {
        ShowTeamEditingScreen(teamId, ShowLeagueTeamsScreen);
    }

    private void ShowLeagueTeamEditingScreen(Guid teamId)
    {
        ShowTeamEditingScreen(teamId, ShowLeagueHomeScreen);
    }

    private void ShowTeamEditingScreen(Guid teamId, Action back)
    {
        if (_activeLeague is null)
        {
            ShowMainMenu();
            return;
        }

        if (_ruleset is null || _rosterSet is null)
        {
            ShowMainMenu();
            return;
        }

        var team = _activeLeague.Teams.FirstOrDefault(current => current.Id == teamId);
        if (team is null)
        {
            back();
            return;
        }

        _teamEditingBack = back;

        var screen = ShowScreen<TeamCreationScreen>(
            "res://scenes/screens/team_creation_screen.tscn",
            usesCompactShell: true,
            fillsViewport: true);

        if (team.IsAiControlled)
        {
            // AI-managed teams are inspectable but not editable; none of the mutation callbacks
            // are wired so the screen renders as a read-only report.
            screen.Setup(
                ruleset: _ruleset,
                rosterSet: _rosterSet,
                defaultTeamName: team.Name,
                saveTeam: _ => Task.CompletedTask,
                back: back,
                league: _activeLeague,
                latestSeasonName: _activeLeague.Seasons.LastOrDefault()?.Name ?? "",
                editingTeam: team,
                record: _leagueService.GetTeamRecord(_activeLeague, team.Id),
                viewOnly: true);
            return;
        }

        screen.Setup(
            ruleset: _ruleset,
            rosterSet: _rosterSet,
            defaultTeamName: team.Name,
            saveTeam: async request => await SaveTeamAsync(request, screen),
            back: back,
            league: _activeLeague,
            latestSeasonName: _activeLeague.Seasons.LastOrDefault()?.Name ?? "",
            editingTeam: team,
            record: _leagueService.GetTeamRecord(_activeLeague, team.Id),
            saveManagement: async request => await SaveTeamManagementAsync(request, screen),
            renamePlayer: async (playerId, playerName) => await RenamePlayerAsync(team.Id, playerId, playerName, screen),
            purchaseSelectedSkill: async (playerId, skillId) => await PurchaseSelectedSkillAsync(team.Id, playerId, skillId, screen),
            purchaseRandomSkill: async (playerId, secondary) => await PurchaseRandomSkillAsync(team.Id, playerId, secondary, screen),
            rollCharacteristic: playerId => RollCharacteristicAsync(team.Id, playerId, screen),
            applyCharacteristic: async (playerId, roll, characteristic) => await ApplyCharacteristicAsync(team.Id, playerId, roll, characteristic, screen),
            applyCharacteristicSkill: async (playerId, skillId) => await ApplyCharacteristicSkillAsync(team.Id, playerId, skillId, screen),
            movePlayer: async (playerId, up) => await MovePlayerAsync(team.Id, playerId, up, screen),
            hirePlayer: async (positionId, playerName) => await HirePlayerAsync(team.Id, positionId, playerName, screen));
    }

    private async Task SaveTeamAsync(TeamDraftRequest request, TeamCreationScreen screen)
    {
        try
        {
            if (_ruleset is null || _activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            if (_activeLeague.Teams.Count >= _targetTeamCount)
            {
                throw new InvalidOperationException("This league is already full.");
            }

            _activeLeague = _leagueService.AddTeam(
                _activeLeague,
                _ruleset,
                request.TeamName,
                request.CoachName,
                request.Roster,
                request.Draft,
                rerolls: request.Rerolls,
                dedicatedFans: request.DedicatedFans,
                cheerleaders: request.Cheerleaders,
                assistantCoaches: request.AssistantCoaches,
                apothecaries: request.Apothecaries);

            await SaveActiveLeagueAsync();
            ShowLeagueTeamsScreen();
            if (CurrentScreen is LeagueTeamsScreen leagueTeamsScreen)
            {
                var savedTeam = _activeLeague.Teams.Last();
                leagueTeamsScreen.SetStatus($"Saved '{savedTeam.Name}' with {savedTeam.Players.Count} players.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Team save failed: {ex.Message}");
        }
    }

    private async Task SaveTeamManagementAsync(TeamManagementRequest request, TeamCreationScreen screen)
    {
        try
        {
            if (_ruleset is null || _activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            _activeLeague = _leagueService.UpdateTeamManagement(
                _activeLeague,
                _ruleset,
                request.Roster,
                request.TeamId,
                request.TeamName,
                request.CoachName,
                request.Rerolls,
                request.DedicatedFans,
                request.Cheerleaders,
                request.AssistantCoaches,
                request.Apothecaries);

            await SaveActiveLeagueAsync();
            _teamEditingBack();
            if (CurrentScreen is LeagueTeamsScreen leagueTeamsScreen)
            {
                var savedTeam = _activeLeague.Teams.First(team => team.Id == request.TeamId);
                leagueTeamsScreen.SetStatus($"Saved team settings for '{savedTeam.Name}'.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Team save failed: {ex.Message}");
        }
    }

    private async Task RenamePlayerAsync(Guid teamId, Guid playerId, string playerName, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            _activeLeague = _leagueService.RenamePlayer(_activeLeague, teamId, playerId, playerName);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus($"Renamed player to '{playerName.Trim()}'.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Rename failed: {ex.Message}");
        }
    }

    private async Task PurchaseSelectedSkillAsync(Guid teamId, Guid playerId, string skillId, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            _activeLeague = _leagueService.PurchaseSelectedSkillAdvancement(_activeLeague, _ruleset, roster, teamId, playerId, skillId);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus("Purchased selected skill advancement.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Skill purchase failed: {ex.Message}");
        }
    }

    private async Task PurchaseRandomSkillAsync(Guid teamId, Guid playerId, bool secondary, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            _activeLeague = _leagueService.PurchaseRandomSkillAdvancement(_activeLeague, _ruleset, roster, teamId, playerId, secondary);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus($"Purchased random {(secondary ? "secondary" : "primary")} skill advancement.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Random skill purchase failed: {ex.Message}");
        }
    }

    private async Task MovePlayerAsync(Guid teamId, Guid playerId, bool up, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            _activeLeague = _leagueService.MovePlayer(_activeLeague, teamId, playerId, up);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus(up ? "Moved player up." : "Moved player down.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Reordering players failed: {ex.Message}");
        }
    }

    private async Task HirePlayerAsync(Guid teamId, string positionId, string playerName, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            var previousPlayerIds = team.Players.Select(player => player.Id).ToHashSet();

            _activeLeague = _leagueService.HirePlayer(_activeLeague, _ruleset, roster, teamId, positionId, playerName);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);

            var updatedTeam = _activeLeague.Teams.First(current => current.Id == teamId);
            var hiredPlayer = updatedTeam.Players.FirstOrDefault(player => !previousPlayerIds.Contains(player.Id));
            if (CurrentScreen is TeamCreationScreen teamScreen && hiredPlayer is not null)
            {
                teamScreen.SelectPlayerById(hiredPlayer.Id);
                teamScreen.SetStatus($"Hired {hiredPlayer.Name}.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Hire failed: {ex.Message}");
        }
    }

    private async Task<CharacteristicAdvancementRoll> RollCharacteristicAsync(Guid teamId, Guid playerId, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            return _leagueService.RollCharacteristicAdvancement(_activeLeague, _ruleset, roster, teamId, playerId);
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Characteristic roll failed: {ex.Message}");
            return new CharacteristicAdvancementRoll(0, 0, Array.Empty<PlayerCharacteristic>());
        }
    }

    private async Task ApplyCharacteristicAsync(Guid teamId, Guid playerId, int roll, PlayerCharacteristic characteristic, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            _activeLeague = _leagueService.ApplyCharacteristicAdvancement(_activeLeague, _ruleset, roster, teamId, playerId, roll, characteristic);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus($"Improved {characteristic} via characteristic advancement.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Characteristic improvement failed: {ex.Message}");
        }
    }

    private async Task ApplyCharacteristicSkillAsync(Guid teamId, Guid playerId, string skillId, TeamCreationScreen screen)
    {
        try
        {
            if (_activeLeague is null || _ruleset is null || _rosterSet is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            var team = _activeLeague.Teams.First(current => current.Id == teamId);
            var roster = _rosterSet.Rosters.First(current => string.Equals(current.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
            _activeLeague = _leagueService.ApplyCharacteristicSecondarySkill(_activeLeague, _ruleset, roster, teamId, playerId, skillId);
            await SaveActiveLeagueAsync();
            ShowTeamEditingScreen(teamId, _teamEditingBack);
            if (CurrentScreen is TeamCreationScreen teamScreen)
            {
                teamScreen.SelectPlayerById(playerId);
                teamScreen.SetStatus("Took a Chosen Secondary skill in place of a characteristic.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Secondary skill choice failed: {ex.Message}");
        }
    }


    private async Task DeleteTeamAsync(Guid teamId, LeagueTeamsScreen screen)
    {
        try
        {
            if (_activeLeague is null)
            {
                return;
            }

            var team = _activeLeague.Teams.FirstOrDefault(current => current.Id == teamId)
                ?? throw new InvalidOperationException("Select a team to delete.");

            _activeLeague = _activeLeague with
            {
                Teams = _activeLeague.Teams.Where(current => current.Id != team.Id).ToArray()
            };

            await SaveActiveLeagueAsync();
            ShowLeagueTeamsScreen();
            if (CurrentScreen is LeagueTeamsScreen leagueTeamsScreen)
            {
                leagueTeamsScreen.SetStatus($"Deleted '{team.Name}'.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Delete failed: {ex.Message}");
        }
    }

    private Task SaveActiveLeagueAsync()
    {
        if (_activeLeague is null)
        {
            return Task.CompletedTask;
        }

        _activeLeaguePath ??= ProjectPath($"user://leagues/{Slugify(_activeLeague.Name)}-{_activeLeague.Id:N}.json");
        return _store.SaveLeagueAsync(_activeLeaguePath, _activeLeague);
    }

    private async Task SaveActiveMatchAsync(MatchState match)
    {
        _activeMatch = match;
        _activeMatchPath ??= ProjectPath($"user://matches/active-{match.Id}.json");
        await _store.SaveMatchAsync(_activeMatchPath, match);

        if (_activeLeague is not null &&
            _ruleset is not null &&
            _activeScheduledMatchId is Guid scheduledMatchId &&
            match.Phase == MatchPhase.Complete)
        {
            _activeLeague = _leagueService.CompleteScheduledMatch(_activeLeague, _ruleset, scheduledMatchId, match, _rosterSet);
            if (_rosterSet is not null)
            {
                // AI GMs manage their rosters as soon as the result is in: spend SPP, rehire, and
                // restock team assets before the next fixture.
                _activeLeague = _gmAiService.RunPostMatchDevelopment(
                    _activeLeague,
                    _ruleset,
                    _rosterSet,
                    [match.HomeTeamId, match.AwayTeamId]);
            }

            await SaveActiveLeagueAsync();
        }
    }

    private T ShowScreen<T>(
        string scenePath,
        bool usesCompactShell = false,
        bool fillsViewport = false) where T : Control
    {
        var scene = GD.Load<PackedScene>(scenePath);
        var screen = scene.Instantiate<T>();
        var isMatchScreen = screen is MatchScreen;
        fillsViewport = fillsViewport || isMatchScreen || screen is PreGameScreen;
        ClearScreenHost(_scrollStack);
        ClearScreenHost(_matchStack);
        ConfigureScreenChrome(isMatchScreen, usesCompactShell);

        _shellScroll.Visible = !fillsViewport;
        _matchStack.Visible = fillsViewport;
        _shellScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _shellScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _shellScroll.ScrollVertical = 0;
        _shellScroll.ScrollHorizontal = 0;

        screen.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        screen.SizeFlagsVertical = fillsViewport ? SizeFlags.ExpandFill : SizeFlags.Fill;
        (fillsViewport ? _matchStack : _scrollStack).AddChild(screen);
        return screen;
    }

    private void ConfigureScreenChrome(bool isMatchScreen, bool usesCompactShell = false)
    {
        var margin = isMatchScreen
            ? MatchShellMargin
            : usesCompactShell
                ? CompactShellMargin
                : DefaultShellMargin;
        _contentMargin.AddThemeConstantOverride("margin_left", margin);
        _contentMargin.AddThemeConstantOverride("margin_top", margin);
        _contentMargin.AddThemeConstantOverride("margin_right", margin);
        _contentMargin.AddThemeConstantOverride("margin_bottom", margin);
        _matchStack.AddThemeConstantOverride("separation", isMatchScreen ? CompactMatchStackSeparation : DefaultMatchStackSeparation);
    }

    private static void ClearScreenHost(Node host)
    {
        foreach (var child in host.GetChildren())
        {
            host.RemoveChild(child);
            child.QueueFree();
        }
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
}
