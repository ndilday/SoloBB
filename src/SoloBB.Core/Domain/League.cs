namespace SoloBB.Core.Domain;

public sealed record League
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string RulesetId { get; init; }
    public int TargetTeamCount { get; init; } = 2;
    public IReadOnlyList<string> RosterSetIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public LeagueSettings Settings { get; init; } = new();
    public IReadOnlyList<LeagueTeam> Teams { get; init; } = [];
    public IReadOnlyList<Season> Seasons { get; init; } = [];
}

public sealed record LeagueSettings
{
    public bool AllowHotseat { get; init; } = true;
    public bool AllowSoloCoachBothSides { get; init; } = true;
    public bool TrackInjuries { get; init; } = true;
    public bool TrackPlayerAdvancement { get; init; } = true;
}

public sealed record LeagueTeam
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string RosterId { get; init; }
    public string CoachName { get; init; } = "Solo Coach";
    public int Treasury { get; init; }
    public int TeamValue { get; init; }
    public int Rerolls { get; init; }
    public int FanFactor { get; init; }
    public IReadOnlyList<Player> Players { get; init; } = [];
}

public sealed record Player
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string PositionId { get; init; }
    public required PlayerStats Stats { get; init; }
    public int StarPlayerPoints { get; init; }
    public PlayerStatus Status { get; init; } = PlayerStatus.Available;
    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<string> Injuries { get; init; } = [];
}

public enum PlayerStatus
{
    Available,
    KnockedOut,
    Casualty,
    MissNextGame,
    Retired
}

public sealed record Season
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public int CurrentWeek { get; init; } = 1;
    public IReadOnlyList<ScheduledMatch> Schedule { get; init; } = [];
}

public sealed record ScheduledMatch
{
    public required Guid Id { get; init; }
    public int Week { get; init; }
    public required Guid HomeTeamId { get; init; }
    public required Guid AwayTeamId { get; init; }
    public MatchResult? Result { get; init; }
}

public sealed record MatchResult
{
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}
