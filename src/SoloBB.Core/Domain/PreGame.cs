namespace SoloBB.Core.Domain;

public sealed record MatchInducementPlan
{
    public required TeamInducementPlan Home { get; init; }
    public required TeamInducementPlan Away { get; init; }
}

public sealed record TeamInducementPlan
{
    public required Guid TeamId { get; init; }
    public int PettyCash { get; init; }
    public int TreasurySpent { get; init; }
    public int Bribes { get; init; }
    public IReadOnlyList<SelectedInducement> Inducements { get; init; } = [];
    public IReadOnlyList<string> StarPlayerIds { get; init; } = [];
}

public sealed record SelectedInducement
{
    public required string InducementId { get; init; }
    public string OptionId { get; init; } = "";
    public int Count { get; init; }
}

public sealed record PreparedPreGameMatch
{
    public required LeagueTeam HomeTeam { get; init; }
    public required LeagueTeam AwayTeam { get; init; }
    public required MatchInducementPlan Inducements { get; init; }
    public required PreGameSummary Summary { get; init; }
    public IReadOnlyList<ActivePrayer> Prayers { get; init; } = [];
}

public sealed record PreGameSummary
{
    public required TeamPreGameSummary Home { get; init; }
    public required TeamPreGameSummary Away { get; init; }
    public int BribeCost { get; init; }
    public bool StarPlayersSupported { get; init; }
}

public sealed record TeamPreGameSummary
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public int TeamValue { get; init; }
    public int Treasury { get; init; }
    public int PettyCash { get; init; }
    public int JourneymenNeeded { get; init; }
    public int MaximumBribesFromPettyCash { get; init; }
    public IReadOnlyList<string> SpecialRules { get; init; } = [];
    public IReadOnlyList<string> RosterRestrictions { get; init; } = [];
    public IReadOnlyList<AvailableInducementSummary> AvailableInducements { get; init; } = [];
    public IReadOnlyList<EligibleStarPlayerSummary> EligibleStarPlayers { get; init; } = [];
}

public sealed record AvailableInducementSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Cost { get; init; }
    public int MaxCount { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }
    public bool MatchEffectImplemented { get; init; }
    public IReadOnlyList<InducementOptionSummary> PickerOptions { get; init; } = [];
}

public sealed record InducementOptionSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Cost { get; init; }
    public required string Effect { get; init; }
    public required string Description { get; init; }
    public string PositionId { get; init; } = "";
    public PlayerStats? Stats { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
}

public sealed record EligibleStarPlayerSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Cost { get; init; }
    public required PlayerStats Stats { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<string> MatchedSpecialRules { get; init; } = [];
}
