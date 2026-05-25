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
    private readonly JsonGameDataStore _store = new();
    private readonly LeagueService _leagueService = new();
    private readonly MatchService _matchService = new();

    private VBoxContainer _stack = null!;
    private Ruleset? _ruleset;
    private RosterSet? _rosterSet;
    private League? _activeLeague;
    private string? _activeLeaguePath;
    private MatchState? _activeMatch;
    private string? _activeMatchPath;
    private LeagueTeam? _activeHomeTeam;
    private LeagueTeam? _activeAwayTeam;
    private int _targetTeamCount = 2;
    private string _catalogStatus = "Loading rulesets...";

    public override void _Ready()
    {
        _stack = GetNode<VBoxContainer>("Panel/Margin/Scroll/Stack");
        ShowMainMenu();
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

    private Control? CurrentScreen => _stack.GetChildCount() > 0 ? _stack.GetChild<Control>(0) : null;

    private void ShowMainMenu()
    {
        var screen = ShowScreen<MainMenuScreen>("res://scenes/screens/main_menu_screen.tscn");
        screen.Setup(
            newLeague: ShowNewLeagueScreen,
            loadLeague: async () => await LoadMostRecentLeagueAsync(screen),
            quit: () => GetTree().Quit(),
            status: _catalogStatus);
    }

    private void ShowNewLeagueScreen()
    {
        var screen = ShowScreen<NewLeagueScreen>("res://scenes/screens/new_league_screen.tscn");
        screen.Setup(
            defaultTeamCount: _targetTeamCount,
            createLeague: StartNewLeague,
            back: ShowMainMenu);
    }

    private void StartNewLeague(string leagueName, int teamCount)
    {
        if (_ruleset is null || _rosterSet is null)
        {
            if (CurrentScreen is NewLeagueScreen screen)
            {
                screen.SetStatus("Catalog data is still loading.");
            }

            return;
        }

        _targetTeamCount = teamCount;
        _activeLeague = _leagueService.CreateLeague(leagueName, _ruleset, [_rosterSet], targetTeamCount: teamCount);
        _activeLeaguePath = ProjectPath($"user://leagues/{Slugify(_activeLeague.Name)}-{_activeLeague.Id:N}.json");
        ShowLeagueTeamsScreen();
    }

    private void ShowLeagueTeamsScreen()
    {
        if (_activeLeague is null)
        {
            ShowNewLeagueScreen();
            return;
        }

        var screen = ShowScreen<LeagueTeamsScreen>("res://scenes/screens/league_teams_screen.tscn");
        screen.Setup(
            league: _activeLeague,
            targetTeamCount: _targetTeamCount,
            createTeam: ShowTeamCreationScreen,
            createLeague: async () => await CreateLeagueSeasonAsync(screen),
            editTeam: ShowTeamEditingScreen,
            deleteTeam: async teamId => await DeleteTeamAsync(teamId, screen),
            back: ShowNewLeagueScreen);
    }

    private void ShowTeamCreationScreen()
    {
        if (_activeLeague is null)
        {
            ShowNewLeagueScreen();
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
            openTeam: ShowTeamHomeScreen,
            playGame: ShowPreGameScreen,
            back: _activeLeague.Seasons.Any() ? ShowMainMenu : ShowLeagueTeamsScreen);
    }

    private void ShowPreGameScreen(Guid scheduledMatchId)
    {
        if (_activeLeague is null)
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
            league: _activeLeague,
            scheduledMatch: scheduledMatch,
            done: async () => await StartScheduledMatchAsync(scheduledMatch),
            back: ShowLeagueHomeScreen);
    }

    private async Task StartScheduledMatchAsync(ScheduledMatch scheduledMatch)
    {
        if (_ruleset is null || _activeLeague is null)
        {
            return;
        }

        var homeTeam = _activeLeague.Teams.First(team => team.Id == scheduledMatch.HomeTeamId);
        var awayTeam = _activeLeague.Teams.First(team => team.Id == scheduledMatch.AwayTeamId);
        _activeMatch = _matchService.CreateHotseatMatch(_ruleset, homeTeam, awayTeam);
        _activeHomeTeam = homeTeam;
        _activeAwayTeam = awayTeam;
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

    private void ShowTeamHomeScreen(Guid teamId)
    {
        if (_activeLeague is null)
        {
            return;
        }

        var team = _activeLeague.Teams.FirstOrDefault(current => current.Id == teamId);
        if (team is null)
        {
            return;
        }

        var screen = ShowScreen<TeamHomeScreen>("res://scenes/screens/team_home_screen.tscn");
        screen.Setup(team, ShowLeagueHomeScreen);
    }

    private void ShowTeamEditingScreen(Guid teamId)
    {
        if (_activeLeague is null)
        {
            ShowNewLeagueScreen();
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
            ShowLeagueTeamsScreen();
            return;
        }

        var screen = ShowScreen<TeamCreationScreen>("res://scenes/screens/team_creation_screen.tscn");
        screen.Setup(
            ruleset: _ruleset,
            rosterSet: _rosterSet,
            defaultTeamName: team.Name,
            saveTeam: async request => await SaveTeamAsync(request, screen),
            back: ShowLeagueTeamsScreen,
            editingTeam: team);
    }

    private async Task SaveTeamAsync(TeamDraftRequest request, TeamCreationScreen screen)
    {
        try
        {
            if (_ruleset is null || _activeLeague is null)
            {
                throw new InvalidOperationException("League data is not ready.");
            }

            if (request.ExistingTeamId is null && _activeLeague.Teams.Count >= _targetTeamCount)
            {
                throw new InvalidOperationException("This league is already full.");
            }

            _activeLeague = request.ExistingTeamId is Guid existingTeamId
                ? _leagueService.UpdateTeam(
                    _activeLeague,
                    _ruleset,
                    existingTeamId,
                    request.TeamName,
                    request.CoachName,
                    request.Roster,
                    request.Draft,
                    rerolls: request.Rerolls,
                    fanFactor: request.FanFactor)
                : _leagueService.AddTeam(
                    _activeLeague,
                    _ruleset,
                    request.TeamName,
                    request.CoachName,
                    request.Roster,
                    request.Draft,
                    rerolls: request.Rerolls,
                    fanFactor: request.FanFactor);

            await SaveActiveLeagueAsync();
            ShowLeagueTeamsScreen();
            if (CurrentScreen is LeagueTeamsScreen leagueTeamsScreen)
            {
                var savedTeam = request.ExistingTeamId is Guid savedTeamId
                    ? _activeLeague.Teams.First(team => team.Id == savedTeamId)
                    : _activeLeague.Teams.Last();
                leagueTeamsScreen.SetStatus($"Saved '{savedTeam.Name}' with {savedTeam.Players.Count} players.");
            }
        }
        catch (Exception ex)
        {
            screen.SetStatus($"Team save failed: {ex.Message}");
        }
    }

    private async Task LoadMostRecentLeagueAsync(MainMenuScreen screen)
    {
        try
        {
            var leagues = await _store.LoadLeaguesAsync(ProjectPath("user://leagues"));
            var league = leagues.LastOrDefault()
                ?? throw new InvalidOperationException("No saved leagues were found.");

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
            screen.Setup(
                league: _activeLeague,
                targetTeamCount: _targetTeamCount,
                createTeam: ShowTeamCreationScreen,
                createLeague: async () => await CreateLeagueSeasonAsync(screen),
                editTeam: ShowTeamEditingScreen,
                deleteTeam: async nextTeamId => await DeleteTeamAsync(nextTeamId, screen),
                back: ShowNewLeagueScreen);
            screen.SetStatus($"Deleted '{team.Name}'.");
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

    private Task SaveActiveMatchAsync(MatchState match)
    {
        _activeMatch = match;
        _activeMatchPath ??= ProjectPath($"user://matches/active-{match.Id}.json");
        return _store.SaveMatchAsync(_activeMatchPath, match);
    }

    private T ShowScreen<T>(string scenePath) where T : Control
    {
        foreach (var child in _stack.GetChildren())
        {
            _stack.RemoveChild(child);
            child.QueueFree();
        }

        var scene = GD.Load<PackedScene>(scenePath);
        var screen = scene.Instantiate<T>();
        screen.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        screen.SizeFlagsVertical = SizeFlags.ExpandFill;
        _stack.AddChild(screen);
        return screen;
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
