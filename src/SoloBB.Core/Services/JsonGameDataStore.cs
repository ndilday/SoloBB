using System.Text.Json;
using System.Text.Json.Serialization;
using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class JsonGameDataStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<Ruleset> LoadRulesetAsync(string path, CancellationToken cancellationToken = default)
    {
        var ruleset = await LoadAsync<Ruleset>(path, cancellationToken);
        new RulesetValidator().Validate(ruleset);
        return ruleset;
    }

    public async Task<RosterSet> LoadRosterSetAsync(string path, Ruleset ruleset, CancellationToken cancellationToken = default)
    {
        var rosterSet = await LoadAsync<RosterSet>(path, cancellationToken);
        new RosterSetValidator().Validate(rosterSet, ruleset);
        return rosterSet;
    }

    public Task<League> LoadLeagueAsync(string path, CancellationToken cancellationToken = default)
    {
        return LoadAsync<League>(path, cancellationToken);
    }

    public Task SaveLeagueAsync(string path, League league, CancellationToken cancellationToken = default)
    {
        return SaveAsync(path, league, cancellationToken);
    }

    public Task<MatchState> LoadMatchAsync(string path, CancellationToken cancellationToken = default)
    {
        return LoadAsync<MatchState>(path, cancellationToken);
    }

    public Task SaveMatchAsync(string path, MatchState match, CancellationToken cancellationToken = default)
    {
        return SaveAsync(path, match, cancellationToken);
    }

    public async Task<IReadOnlyList<Ruleset>> LoadRulesetsAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var rulesets = new List<Ruleset>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            rulesets.Add(await LoadRulesetAsync(path, cancellationToken));
        }

        return rulesets;
    }

    public async Task<IReadOnlyList<RosterSet>> LoadRosterSetsAsync(string directory, Ruleset ruleset, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var rosterSets = new List<RosterSet>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var rosterSet = await LoadAsync<RosterSet>(path, cancellationToken);
            if (string.Equals(rosterSet.RulesetId, ruleset.Id, StringComparison.OrdinalIgnoreCase))
            {
                new RosterSetValidator().Validate(rosterSet, ruleset);
                rosterSets.Add(rosterSet);
            }
        }

        return rosterSets;
    }

    public async Task<IReadOnlyList<League>> LoadLeaguesAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var leagues = new List<League>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            leagues.Add(await LoadLeagueAsync(path, cancellationToken));
        }

        return leagues;
    }

    private async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException($"Could not read '{path}'.");
    }

    private async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
    }
}
