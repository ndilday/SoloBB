using System.Text.Json;
using System.Text.Json.Nodes;
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

    public async Task<League> LoadLeagueAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Could not read '{path}'.");
        MigrateLeagueNode(node);
        return node.Deserialize<League>(_jsonOptions)
            ?? throw new InvalidDataException($"Could not read '{path}'.");
    }

    // Saved leagues from before the BB2020 fan-factor work stored the persistent characteristic under the
    // camelCase key "fanFactor". It is now "dedicatedFans" (per-game Fan Factor is rolled at kickoff and is
    // not persisted on the team). Rewrite the legacy key on load so older save files keep working.
    private static void MigrateLeagueNode(JsonNode node)
    {
        if (node["teams"] is not JsonArray teams)
        {
            return;
        }

        foreach (var team in teams)
        {
            if (team is not JsonObject teamObject ||
                !teamObject.TryGetPropertyValue("fanFactor", out var legacyValue))
            {
                continue;
            }

            if (!teamObject.ContainsKey("dedicatedFans") && legacyValue is not null)
            {
                teamObject["dedicatedFans"] = legacyValue.DeepClone();
            }

            teamObject.Remove("fanFactor");
        }
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
