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
    public IReadOnlyList<string> StarPlayerIds { get; init; } = [];
}

public sealed record PreparedPreGameMatch
{
    public required LeagueTeam HomeTeam { get; init; }
    public required LeagueTeam AwayTeam { get; init; }
    public required MatchInducementPlan Inducements { get; init; }
    public required PreGameSummary Summary { get; init; }
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
}
