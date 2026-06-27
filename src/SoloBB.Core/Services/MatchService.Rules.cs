using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;
using static SoloBB.Core.Services.MatchFormatting;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    private static PlayerPlacement ApplyPitchState(
        MatchState match,
        PlayerPlacement placement,
        PlayerPitchState state,
        PitchSquare? square,
        CasualtyRoll? casualty = null)
    {
        if (state == PlayerPitchState.Stunned)
        {
            var recoveryTurn = GetTeamTurn(match, placement.TeamId) + (placement.TeamId == match.ActiveTeamId && match.Phase != MatchPhase.Kickoff ? 1 : 0);
            return placement with
            {
                Square = square,
                State = state,
                StunnedRecoveryHalf = match.Half,
                StunnedRecoveryTurn = recoveryTurn,
                Casualty = null
            };
        }

        return placement with
        {
            Square = square,
            State = state,
            StunnedRecoveryHalf = null,
            StunnedRecoveryTurn = null,
            Casualty = state is PlayerPitchState.Casualty or PlayerPitchState.Dead ? casualty : null
        };
    }

    private static MatchState ApplyPlayerPitchState(
        MatchState match,
        Guid playerId,
        PlayerPitchState state,
        PitchSquare? square,
        CasualtyRoll? casualty = null)
    {
        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId
                    ? ApplyPitchState(match, current, state, square, casualty)
                    : current)
                .ToArray()
        };
    }

    private PitchSquare[] LegalPushSquares(
        MatchState match,
        Ruleset ruleset,
        PitchSquare attackerSquare,
        PitchSquare defenderSquare,
        Player attacker,
        Player defender,
        PlayerTurnAction attackerAction)
    {
        var attackerCanUseGrab = PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.Grab) &&
            !PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.Frenzy);
        if (attackerCanUseGrab && attackerAction == PlayerTurnAction.Block)
        {
            var grabSquares = AdjacentSquares(defenderSquare)
                .Where(square => IsOnPitch(ruleset, square))
                .Where(square => square != attackerSquare)
                .Where(square => FindPushOccupant(match, square, defender.Id) is null)
                .Distinct()
                .ToArray();

            return grabSquares.Length > 0
                ? grabSquares
                : LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
        }

        if (attackerCanUseGrab ||
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.SideStep))
        {
            return LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
        }

        var sideStepSquares = AdjacentSquares(defenderSquare)
            .Where(square => IsOnPitch(ruleset, square))
            .Where(square => FindPushOccupant(match, square, defender.Id) is null)
            .Distinct()
            .ToArray();

        return sideStepSquares.Length > 0
            ? sideStepSquares
            : LegalPushSquares(match, ruleset, attackerSquare, defenderSquare, defender.Id);
    }

    private static bool PlayerHasHookedEffect(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return SkillHookResolver.PlayerHasHookedEffect(ruleset, player, eventKind, stage, effect);
    }

    private static bool PlayerHasHookedSkillId(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        string skillId)
    {
        return SkillHookResolver.PlayerHasHookedSkillId(ruleset, player, eventKind, stage, skillId);
    }

    private static bool PlayerHasSkillId(Player player, string skillId)
    {
        return SkillCatalog.PlayerHasSkillId(player, skillId);
    }

    private static bool HasAdjacentStandingTeammate(MatchState match, Guid teamId, Guid playerId)
    {
        var placement = FindPlacement(match, playerId);
        if (placement?.Square is null)
        {
            return false;
        }

        return match.Placements.Any(teammate =>
            teammate.TeamId == teamId &&
            teammate.PlayerId != playerId &&
            teammate.State == PlayerPitchState.Standing &&
            PlacementsAreAdjacent(placement, teammate));
    }

    private static bool IsAdjacentToOpponentWithHookedEffect(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam opposingTeam,
        Guid playerId,
        PitchSquare square,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.PlayerId != playerId &&
            placement.State == PlayerPitchState.Standing &&
            IsAdjacentToPlacement(placement, square) &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), eventKind, stage, effect));
    }

    private static bool ArmBarApplies(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Guid playerId,
        PitchSquare currentSquare,
        PitchSquare destination)
    {
        return opposingTeam is not null &&
            (IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, currentSquare, GameEventKind.DodgeRoll, GameEventStage.BeforeResolve, SkillEffect.ArmBar) ||
                IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, destination, GameEventKind.DodgeRoll, GameEventStage.BeforeResolve, SkillEffect.ArmBar));
    }

    private static int PrehensileTailModifier(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Guid playerId,
        PitchSquare currentSquare)
    {
        return opposingTeam is not null &&
            IsAdjacentToOpponentWithHookedEffect(match, ruleset, opposingTeam, playerId, currentSquare, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.PrehensileTail)
                ? 1
                : 0;
    }

    private static int DisturbingPresenceModifier(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return 0;
        }

        return match.Placements.Count(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned &&
            placement.Square is PitchSquare disturbingSquare &&
            Math.Max(Math.Abs(disturbingSquare.X - square.X), Math.Abs(disturbingSquare.Y - square.Y)) <= 3 &&
            (PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence) ||
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence) ||
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.DisturbingPresence)));
    }

    private TentaclesResolution ApplyTentacles(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam? opposingTeam,
        Player mover,
        PitchSquare currentSquare)
    {
        if (opposingTeam is null)
        {
            return new TentaclesResolution(match, false);
        }

        var tentaclePlacement = match.Placements
            .Where(placement =>
                placement.TeamId == opposingTeam.Id &&
                placement.State == PlayerPitchState.Standing &&
                IsAdjacentToPlacement(placement, currentSquare) &&
                PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.BeforeResolve, SkillEffect.Tentacles))
            .FirstOrDefault();

        if (tentaclePlacement is null)
        {
            return new TentaclesResolution(match, false);
        }

        var tentaclePlayer = FindTeamPlayer(opposingTeam, tentaclePlacement.PlayerId);
        var roll = _dice.RollD6();
        var result = roll + tentaclePlayer.Stats.Strength - mover.Stats.Strength;
        if (roll == 1 || result <= 5)
        {
            return new TentaclesResolution(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{tentaclePlayer.Name} uses Tentacles against {mover.Name}: rolled {roll}, result {result}, no effect." }
                ]
            }, false);
        }

        return new TentaclesResolution(match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{tentaclePlayer.Name} uses Tentacles against {mover.Name}: rolled {roll}, result {result}, movement ends." }
            ]
        }, true);
    }

    private static bool ShouldStripBall(Ruleset ruleset, Player attacker, Player defender, bool defenderHasBall, bool knockDown)
    {
        return defenderHasBall &&
            !knockDown &&
            PlayerHasHookedEffect(ruleset, attacker, GameEventKind.Push, GameEventStage.BeforeResolve, SkillEffect.StripBall) &&
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.PickupRoll, GameEventStage.AfterRoll, SkillEffect.PickupReroll) &&
            !PlayerHasHookedEffect(ruleset, defender, GameEventKind.CatchRoll, GameEventStage.AfterRoll, SkillEffect.MonstrousMouth);
    }

    private static bool HasLeaderPlayer(Ruleset ruleset, LeagueTeam team)
    {
        return team.Players.Any(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, SkillEffect.Leader));
    }

    private static bool HasLeaderOnPitch(MatchState match, Ruleset ruleset, LeagueTeam team)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == team.Id &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(team, placement.PlayerId), GameEventKind.ActionStart, GameEventStage.BeforeEvent, SkillEffect.Leader));
    }

    private int Roll2D6()
    {
        return _dice.RollD6() + _dice.RollD6();
    }

    private int RollD3()
    {
        return ((_dice.RollD6() - 1) / 2) + 1;
    }

    // BB2020 FAME: +2 if this team brought at least twice as many fans as the opponent, +1 if it simply
    // brought more, otherwise 0 (the team with fewer or equal fans gains no FAME).
    private static int ComputeFame(int teamFanFactor, int opponentFanFactor)
    {
        if (teamFanFactor >= opponentFanFactor * 2)
        {
            return 2;
        }

        return teamFanFactor > opponentFanFactor ? 1 : 0;
    }

    private int RollIndex(int count)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException("Cannot roll an index for an empty set.");
        }

        return (_dice.RollD6() - 1) % count;
    }

    private DiceRoll2D6 Roll2D6Detailed()
    {
        var first = _dice.RollD6();
        var second = _dice.RollD6();
        return new DiceRoll2D6(first, second);
    }

    private int RollD16()
    {
        return _dice.RollD16();
    }

    private PitchSquare ScatterFrom(Ruleset ruleset, PitchSquare square)
    {
        return ScatterFrom(ruleset, square, distance: 1);
    }

    private PitchSquare ScatterFrom(Ruleset ruleset, PitchSquare square, int distance)
    {
        var direction = _dice.RollD8();
        var next = square;
        for (var step = 0; step < distance; step++)
        {
            next = ScatterDirection(next, direction);
        }

        return next;
    }

    private static PitchSquare ScatterDirection(PitchSquare square, int direction)
    {
        var (dx, dy) = direction switch
        {
            1 => (-1, -1),
            2 => (0, -1),
            3 => (1, -1),
            4 => (-1, 0),
            5 => (1, 0),
            6 => (-1, 1),
            7 => (0, 1),
            _ => (1, 1)
        };

        return new PitchSquare(square.X + dx, square.Y + dy);
    }

    private static PlayerTurnActivation? GetActivation(MatchState match, Guid playerId, Guid teamId)
    {
        return match.Activations.FirstOrDefault(activation =>
            activation.PlayerId == playerId &&
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn);
    }

    private static PlayerPlacement? FindPlacement(MatchState match, Guid playerId)
    {
        return match.Placements.FirstOrDefault(placement => placement.PlayerId == playerId);
    }

    private static PlayerPlacement FindStandingPlacement(MatchState match, Guid playerId, Guid teamId, string role)
    {
        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException($"The {role} is not part of this match.");

        if (placement.TeamId != teamId)
        {
            throw new InvalidOperationException($"The {role} is not assigned to the active team.");
        }

        if (placement.Square is null || placement.State is not PlayerPitchState.Standing)
        {
            throw new InvalidOperationException($"The {role} must be standing on the pitch.");
        }

        return placement;
    }

    private static bool RollSucceeds(int roll, int target, DiceRules diceRules)
    {
        if (diceRules.NaturalOneAlwaysFails && roll == 1)
        {
            return false;
        }

        if (diceRules.NaturalSixAlwaysSucceeds && roll == 6)
        {
            return true;
        }

        return roll >= target;
    }

    private static bool IsReceivingSide(Ruleset ruleset, Guid receivingTeamId, Guid homeTeamId, PitchSquare square)
    {
        return receivingTeamId == homeTeamId
            ? square.X < ruleset.PitchWidth / 2
            : square.X >= ruleset.PitchWidth / 2;
    }

    private static MatchState CreatePendingTouchback(MatchState match, LeagueTeam receivingTeam, IReadOnlyList<MatchLogEntry> log)
    {
        var legalSquares = TouchbackReceiverSquares(match, receivingTeam);
        if (legalSquares.Length == 0)
        {
            throw new InvalidOperationException("Receiving team has no standing player for touchback.");
        }

        var pendingPlayer = receivingTeam.Players.First(player =>
            match.Placements.Any(placement =>
                placement.PlayerId == player.Id &&
                placement.TeamId == receivingTeam.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is not null));

        return match with
        {
            Ball = new BallState(),
            PendingBallPlacement = new PendingBallPlacementChoice
            {
                TeamId = receivingTeam.Id,
                PlayerId = pendingPlayer.Id,
                LegalSquares = legalSquares,
                Reason = "Touchback"
            },
            PendingKickoffEvent = null,
            Log = [.. match.Log, .. log]
        };
    }

    private static PitchSquare[] TouchbackReceiverSquares(MatchState match, LeagueTeam receivingTeam)
    {
        return receivingTeam.Players
            .Select(player => match.Placements.FirstOrDefault(placement =>
                placement.PlayerId == player.Id &&
                placement.TeamId == receivingTeam.Id &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is not null)?.Square)
            .OfType<PitchSquare>()
            .Distinct()
            .ToArray();
    }

    private static bool IsLegalSetupSide(MatchState match, Ruleset ruleset, Guid teamId, PitchSquare square)
    {
        return teamId == match.HomeTeamId
            ? square.X < ruleset.PitchWidth / 2
            : square.X >= ruleset.PitchWidth / 2;
    }

    private static Guid GetOpponentTeamId(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.AwayTeamId : match.HomeTeamId;
    }

    private static WeatherCondition ResolveWeather(int roll)
    {
        return roll switch
        {
            <= 2 => WeatherCondition.SwelteringHeat,
            3 => WeatherCondition.VerySunny,
            <= 10 => WeatherCondition.Nice,
            11 => WeatherCondition.PouringRain,
            _ => WeatherCondition.Blizzard
        };
    }

    private static string FormatWeather(WeatherCondition weather)
    {
        return weather switch
        {
            WeatherCondition.SwelteringHeat => "sweltering heat",
            WeatherCondition.VerySunny => "very sunny",
            WeatherCondition.Nice => "nice",
            WeatherCondition.PouringRain => "pouring rain",
            WeatherCondition.Blizzard => "blizzard",
            _ => weather.ToString().ToLowerInvariant()
        };
    }

    private static IReadOnlyList<PlayerPlacement> CreateInitialPlacements(LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return
        [
            .. homeTeam.Players.Select(player => new PlayerPlacement
            {
                PlayerId = player.Id,
                TeamId = homeTeam.Id,
                State = PlayerPitchState.Reserve,
                IsLarge = player.Stats.IsLarge
            }),
            .. awayTeam.Players.Select(player => new PlayerPlacement
            {
                PlayerId = player.Id,
                TeamId = awayTeam.Id,
                State = PlayerPitchState.Reserve,
                IsLarge = player.Stats.IsLarge
            })
        ];
    }
}
