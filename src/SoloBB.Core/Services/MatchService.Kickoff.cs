using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState ResolveKickoff(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam, PitchSquare targetSquare, LeagueTeam? kickingTeam = null, bool skipOnTheBall = false)
    {
        if (match.Phase is not MatchPhase.Kickoff)
        {
            throw new InvalidOperationException("Kickoff can only be resolved during the kickoff phase.");
        }

        if (receivingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("The active team must receive the kickoff.");
        }

        var kickingTeamId = GetOpponentTeamId(match, receivingTeam.Id);

        // BB2020 On the Ball: after the kick but before the kick-off event roll, the receiving team may move a
        // player with the skill up to three squares. Raise the window once; it re-enters with skipOnTheBall set.
        if (!skipOnTheBall)
        {
            var onTheBallPlayers = match.Placements
                .Where(placement => placement.TeamId == receivingTeam.Id &&
                    placement.State == PlayerPitchState.Standing &&
                    PlayerHasHookedEffect(ruleset, FindTeamPlayer(receivingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.BeforeEvent, SkillEffect.OnTheBall))
                .Select(placement => placement.PlayerId)
                .ToArray();
            if (onTheBallPlayers.Length > 0)
            {
                return match with
                {
                    PendingOnTheBall = new PendingOnTheBallChoice
                    {
                        Trigger = OnTheBallTrigger.Kickoff,
                        TeamId = receivingTeam.Id,
                        OpposingTeamId = kickingTeamId,
                        EligiblePlayerIds = onTheBallPlayers,
                        KickoffResume = new PendingKickoffResume
                        {
                            TargetSquare = targetSquare,
                            HadKickingTeam = kickingTeam is not null
                        }
                    },
                    Log =
                    [
                        .. match.Log,
                        new MatchLogEntry { Message = "The receiving team may use On the Ball before the kick-off event roll." }
                    ]
                };
            }
        }

        var eventRoll = Roll2D6Detailed();
        var eventResult = ResolveKickoffEvent(match, ruleset, receivingTeam.Id, kickingTeamId, eventRoll.Total);
        var kickoffMatch = eventResult.Match;
        var rawScatterDistance = _dice.RollD6();
        var scatterDistance = kickingTeam is not null && HasKickPlayer(kickoffMatch, ruleset, kickingTeam)
            ? rawScatterDistance / 2
            : rawScatterDistance;
        var scatterSquare = ScatterFrom(ruleset, targetSquare, scatterDistance);
        var log = new List<MatchLogEntry>
        {
            new() { Message = $"Kickoff event roll {eventRoll.Total}: {eventResult.Name}. {eventResult.Message}" },
            new() { Message = kickingTeam is not null && HasKickPlayer(kickoffMatch, ruleset, kickingTeam)
                ? $"Kickoff targeted {targetSquare.X},{targetSquare.Y}; Kick halves scatter from {rawScatterDistance} to {scatterDistance}, landing at {scatterSquare.X},{scatterSquare.Y}."
                : $"Kickoff targeted {targetSquare.X},{targetSquare.Y} and scattered {scatterDistance} square{(scatterDistance == 1 ? "" : "s")} to {scatterSquare.X},{scatterSquare.Y}." }
        };

        if (eventResult.ExtraScatter)
        {
            var gustSquare = ScatterFrom(ruleset, scatterSquare);
            log.Add(new MatchLogEntry { Message = $"Changing weather gust scatters the ball to {gustSquare.X},{gustSquare.Y}." });
            scatterSquare = gustSquare;
        }

        if (eventResult.PendingKind is KickoffEventKind pendingKind &&
            IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            var pending = CreatePendingKickoffEvent(kickoffMatch, ruleset, pendingKind, receivingTeam.Id, kickingTeamId, scatterSquare);
            if (pending is not null)
            {
                return kickoffMatch with
                {
                    Ball = new BallState { Square = scatterSquare },
                    PendingKickoffEvent = pending,
                    Log =
                    [
                        .. kickoffMatch.Log,
                        .. log,
                        new MatchLogEntry { Message = $"{FormatKickoffEventKind(pendingKind)} requires a choice before the ball lands." }
                    ]
                };
            }
        }

        if (!IsOnPitch(ruleset, scatterSquare) || !IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, scatterSquare))
        {
            return CreatePendingTouchback(kickoffMatch, receivingTeam, [.. log, new MatchLogEntry { Message = "Touchback. Choose a receiving player to carry the ball." }]);
        }

        // Transition to the receiving team's turn before the ball lands so the catch happens during their
        // turn; that lets the receiving team spend a team reroll on it, and any resulting pending reroll
        // is captured against the already-transitioned state.
        var preLanding = kickoffMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            DriveState = DriveState.InProgress,
            Activations = [],
            PendingKickoffEvent = null,
            Log =
            [
                .. kickoffMatch.Log,
                .. log,
                new MatchLogEntry { Message = "Kickoff resolved. Offensive player turn begins." }
            ]
        };

        return ResolveKickoffLanding(preLanding, ruleset, receivingTeam, scatterSquare);
    }

    private KickoffEventResult ResolveKickoffEvent(MatchState match, Ruleset ruleset, Guid receivingTeamId, Guid kickingTeamId, int roll)
    {
        return roll switch
        {
            2 => ResolveGetTheRef(match),
            3 => ResolveTimeOut(match, ruleset),
            4 => new KickoffEventResult(match, "Solid Defence", "The kicking team may reposition open players before the ball lands.", PendingKind: KickoffEventKind.SolidDefence),
            5 => new KickoffEventResult(match, "High Kick", "The receiving team may move one open player under the ball.", PendingKind: KickoffEventKind.HighKick),
            6 => ResolveCheeringFansOrBrilliantCoaching(match, receivingTeamId, kickingTeamId, "Cheering Fans", useCheerleaders: true),
            7 => ResolveCheeringFansOrBrilliantCoaching(match, receivingTeamId, kickingTeamId, "Brilliant Coaching", useCheerleaders: false),
            8 => ResolveChangingWeather(match),
            9 => new KickoffEventResult(match, "Quick Snap", "The receiving team may move open players one square before the ball lands.", PendingKind: KickoffEventKind.QuickSnap),
            10 => new KickoffEventResult(match, "Blitz", "The kicking team may move open players one square before the ball lands.", PendingKind: KickoffEventKind.Blitz),
            11 => ResolveOfficiousRef(match, receivingTeamId, kickingTeamId),
            12 => ResolvePitchInvasion(match),
            _ => new KickoffEventResult(match, "Kickoff", "No kickoff event.")
        };
    }

    public MatchState MovePendingKickoffEventPlayer(MatchState match, Ruleset ruleset, Guid playerId, PitchSquare destination)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (!pending.EligiblePlayerIds.Contains(playerId) || pending.MovedPlayerIds.Contains(playerId))
        {
            throw new InvalidOperationException("That player cannot move for this kickoff event.");
        }

        var placement = FindPlacement(match, playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != pending.TeamId || placement.Square is not PitchSquare source || placement.State != PlayerPitchState.Standing)
        {
            throw new InvalidOperationException("Only eligible standing players on the pitch can move for this kickoff event.");
        }

        if (!IsOnPitch(ruleset, destination))
        {
            throw new InvalidOperationException("Kickoff event movement must stay on the pitch.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && PlacementOccupiesSquare(current, destination) && OccupiesPitch(current.State)))
        {
            throw new InvalidOperationException("Kickoff event movement requires an empty destination.");
        }

        if (pending.Kind == KickoffEventKind.SolidDefence)
        {
            if (!IsLegalSetupSide(match, ruleset, pending.TeamId, destination))
            {
                throw new InvalidOperationException("Solid Defence must keep players on the kicking team's setup side.");
            }

            if (IsWideZone(ruleset, destination) && CountTeamPlayersInWideZone(match, ruleset, pending.TeamId, destination, playerId) >= 2)
            {
                throw new InvalidOperationException("Solid Defence cannot place more than two players in the same wide zone.");
            }
        }
        else if (pending.Kind == KickoffEventKind.HighKick)
        {
            if (destination != pending.LandingSquare)
            {
                throw new InvalidOperationException("High Kick can only move the chosen player under the ball.");
            }
        }
        else if (!IsAdjacent(source, destination))
        {
            throw new InvalidOperationException("This kickoff event allows a one-square move.");
        }

        var nextPending = pending with
        {
            MovedPlayerIds = [.. pending.MovedPlayerIds, playerId],
            MovesRemaining = Math.Max(0, pending.MovesRemaining - 1)
        };

        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId ? current with { Square = destination } : current)
                .ToArray(),
            PendingKickoffEvent = nextPending.MovesRemaining == 0 ? nextPending with { RequiresPlayerChoice = false } : nextPending,
            Log =
            [
                .. match.Log,
            new MatchLogEntry { Message = $"{FormatKickoffEventKind(pending.Kind)}: repositioned {PlayerName(playerId)} to {destination.X},{destination.Y}." }
            ]
        };
    }

    public MatchState BlockDuringPendingKickoffBlitz(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam kickingTeam,
        Guid attackerPlayerId,
        LeagueTeam receivingTeam,
        Guid defenderPlayerId)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (pending.Kind != KickoffEventKind.Blitz)
        {
            throw new InvalidOperationException("Only a Blitz kickoff event allows a free block.");
        }

        if (pending.TeamId != kickingTeam.Id || pending.ReceivingTeamId != receivingTeam.Id)
        {
            throw new InvalidOperationException("Kickoff blitz teams do not match the pending event.");
        }

        if (!pending.EligiblePlayerIds.Contains(attackerPlayerId) || pending.MovedPlayerIds.Contains(attackerPlayerId))
        {
            throw new InvalidOperationException("That player cannot act for this kickoff blitz.");
        }

        if (match.PendingBlock is not null || match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending block or push before continuing the kickoff blitz.");
        }

        var attacker = FindTeamPlayer(kickingTeam, attackerPlayerId);
        var defender = FindTeamPlayer(receivingTeam, defenderPlayerId);
        var attackerPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerPlayerId)
            ?? throw new InvalidOperationException("Attacker is not part of this match.");
        var defenderPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderPlayerId)
            ?? throw new InvalidOperationException("Defender is not part of this match.");

        if (attackerPlacement.TeamId != kickingTeam.Id ||
            defenderPlacement.TeamId != receivingTeam.Id ||
            attackerPlacement.Square is null ||
            defenderPlacement.Square is null ||
            attackerPlacement.State != PlayerPitchState.Standing ||
            defenderPlacement.State != PlayerPitchState.Standing ||
            !PlacementsAreAdjacent(attackerPlacement, defenderPlacement))
        {
            throw new InvalidOperationException("Kickoff blitz blocks require adjacent standing opponents.");
        }

        var nextPending = pending with
        {
            MovedPlayerIds = [.. pending.MovedPlayerIds, attackerPlayerId],
            MovesRemaining = Math.Max(0, pending.MovesRemaining - 1)
        };
        var markedMatch = match with
        {
            PendingKickoffEvent = nextPending.MovesRemaining == 0 ? nextPending with { RequiresPlayerChoice = false } : nextPending
        };

        return ResolveBlock(markedMatch, ruleset, kickingTeam, attacker, attackerPlacement, receivingTeam, defender);
    }

    public MatchState CompletePendingKickoffEvent(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam)
    {
        var pending = match.PendingKickoffEvent
            ?? throw new InvalidOperationException("There is no pending kickoff event.");

        if (pending.ReceivingTeamId != receivingTeam.Id)
        {
            throw new InvalidOperationException("The receiving team must resolve the pending kickoff landing.");
        }

        var landingSquare = pending.LandingSquare;
        var baseMatch = match with { PendingKickoffEvent = null };
        if (pending.Kind == KickoffEventKind.SolidDefence)
        {
            ValidateSetupComplete(baseMatch, ruleset, pending.TeamId);
        }

        if (!IsReceivingSide(ruleset, receivingTeam.Id, match.HomeTeamId, landingSquare))
        {
            return CreatePendingTouchback(baseMatch, receivingTeam, [new MatchLogEntry { Message = $"Touchback after {FormatKickoffEventKind(pending.Kind)}. Choose a receiving player to carry the ball." }]);
        }

        var preLanding = baseMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            DriveState = DriveState.InProgress,
            Activations = [],
            Log =
            [
                .. baseMatch.Log,
                new MatchLogEntry { Message = $"{FormatKickoffEventKind(pending.Kind)} complete. Kickoff resolved. Offensive player turn begins." }
            ]
        };

        return ResolveKickoffLanding(preLanding, ruleset, receivingTeam, landingSquare);
    }

    private PendingKickoffEventChoice? CreatePendingKickoffEvent(
        MatchState match,
        Ruleset ruleset,
        KickoffEventKind kind,
        Guid receivingTeamId,
        Guid kickingTeamId,
        PitchSquare landingSquare)
    {
        var teamId = kind is KickoffEventKind.SolidDefence or KickoffEventKind.Blitz
            ? kickingTeamId
            : receivingTeamId;
        var eligible = match.Placements
            .Where(placement =>
                placement.TeamId == teamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                !IsMarkedByOpponent(match, teamId, placement.PlayerId, square))
            .ToArray();

        if (kind == KickoffEventKind.HighKick)
        {
            if (match.Placements.Any(placement => PlacementOccupiesSquare(placement, landingSquare) && OccupiesPitch(placement.State)))
            {
                eligible = eligible.Where(placement => placement.Square == landingSquare).ToArray();
            }

            if (eligible.Length == 0)
            {
                return null;
            }

            return new PendingKickoffEventChoice
            {
                Kind = kind,
                TeamId = teamId,
                ReceivingTeamId = receivingTeamId,
                LandingSquare = landingSquare,
                EligiblePlayerIds = eligible.Select(placement => placement.PlayerId).ToArray(),
                MovedPlayerIds = [],
                MovesRemaining = 1
            };
        }

        if (kind != KickoffEventKind.SolidDefence)
        {
            eligible = eligible
                .Where(placement => placement.Square is not null &&
                    PlacementAdjacentSquares(placement).Any(candidate =>
                        IsOnPitch(ruleset, candidate) &&
                        !match.Placements.Any(current => current.PlayerId != placement.PlayerId && PlacementOccupiesSquare(current, candidate) && OccupiesPitch(current.State))))
                .ToArray();
        }

        if (eligible.Length == 0)
        {
            return null;
        }

        // BB2020: Quick Snap lets every open player move one square. The other repositioning events
        // (Solid Defence, Blitz) only move up to D3+3 players.
        var moves = kind == KickoffEventKind.QuickSnap
            ? eligible.Length
            : Math.Min(eligible.Length, RollD3() + 3);
        return new PendingKickoffEventChoice
        {
            Kind = kind,
            TeamId = teamId,
            ReceivingTeamId = receivingTeamId,
            LandingSquare = landingSquare,
            EligiblePlayerIds = eligible.Select(placement => placement.PlayerId).ToArray(),
            MovedPlayerIds = [],
            MovesRemaining = moves
        };
    }

    private KickoffEventResult ResolveTimeOut(MatchState match, Ruleset ruleset)
    {
        var roll = _dice.RollD6();
        var adjustment = roll <= 3 ? -1 : 1;
        var homeTurn = Math.Clamp(match.HomeTurn + adjustment, 1, ruleset.TurnsPerHalf + 1);
        var awayTurn = Math.Clamp(match.AwayTurn + adjustment, 1, ruleset.TurnsPerHalf + 1);
        var direction = adjustment < 0 ? "back" : "forward";
        return new KickoffEventResult(
            match with { HomeTurn = homeTurn, AwayTurn = awayTurn, Turn = GetTeamTurn(match with { HomeTurn = homeTurn, AwayTurn = awayTurn }, match.ActiveTeamId) },
            "Time-out",
            $"Time-out roll {roll}: both turn markers move {direction} one space.");
    }

    private static KickoffEventResult ResolveGetTheRef(MatchState match)
    {
        return new KickoffEventResult(
            match with
            {
                HomeBribesRemaining = match.HomeBribesRemaining + 1,
                AwayBribesRemaining = match.AwayBribesRemaining + 1
            },
            "Get the Ref",
            "Both teams gain one bribe.");
    }

    private KickoffEventResult ResolveCheeringFansOrBrilliantCoaching(MatchState match, Guid receivingTeamId, Guid kickingTeamId, string name, bool useCheerleaders)
    {
        var receivingStaff = TeamKickoffStaff(match, receivingTeamId, useCheerleaders);
        var kickingStaff = TeamKickoffStaff(match, kickingTeamId, useCheerleaders);
        // BB2020: each coach rolls a D3 and adds their relevant staff (Cheerleaders or Assistant Coaches)
        // plus their team's FAME for this game.
        var receivingRoll = RollD3() + receivingStaff + TeamFame(match, receivingTeamId);
        var kickingRoll = RollD3() + kickingStaff + TeamFame(match, kickingTeamId);
        if (receivingRoll == kickingRoll)
        {
            return new KickoffEventResult(match, name, $"Receiving coach total {receivingRoll}, kicking coach total {kickingRoll}; no bonus reroll.");
        }

        var winnerTeamId = receivingRoll > kickingRoll ? receivingTeamId : kickingTeamId;
        var nextMatch = winnerTeamId == match.HomeTeamId
            ? match with { HomeRerollsRemaining = match.HomeRerollsRemaining + 1 }
            : match with { AwayRerollsRemaining = match.AwayRerollsRemaining + 1 };
        return new KickoffEventResult(nextMatch, name, $"Receiving coach total {receivingRoll}, kicking coach total {kickingRoll}; winner gains a bonus reroll for the drive.");
    }

    // BB2020 Officious Ref (kickoff 11): each coach rolls a D6 and adds their FAME; the coach with the lower
    // total (both, on a tie) randomly selects one of their players on the pitch. A D6 is rolled for each
    // selected player: on a 1 the player is Sent-off, otherwise they are placed Prone and Stunned.
    private KickoffEventResult ResolveOfficiousRef(MatchState match, Guid receivingTeamId, Guid kickingTeamId)
    {
        var receivingTotal = _dice.RollD6() + TeamFame(match, receivingTeamId);
        var kickingTotal = _dice.RollD6() + TeamFame(match, kickingTeamId);

        var affectedTeamIds = new List<Guid>();
        if (receivingTotal <= kickingTotal)
        {
            affectedTeamIds.Add(receivingTeamId);
        }

        if (kickingTotal <= receivingTotal)
        {
            affectedTeamIds.Add(kickingTeamId);
        }

        var current = match;
        var outcomes = new List<string>();
        foreach (var teamId in affectedTeamIds)
        {
            var candidates = current.Placements
                .Where(placement => placement.TeamId == teamId && placement.Square is not null && OccupiesPitch(placement.State))
                .ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            var victim = candidates[RollIndex(candidates.Length)];
            var victimName = PlayerName(victim.PlayerId);
            if (_dice.RollD6() == 1)
            {
                current = SendOffPlayer(current, victim.PlayerId, $"Officious Ref: {victimName} argues with the ref and is sent off.");
                outcomes.Add($"{victimName} is sent off");
            }
            else
            {
                current = current with
                {
                    Placements = current.Placements
                        .Select(placement => placement.PlayerId == victim.PlayerId
                            ? ApplyPitchState(current, placement, PlayerPitchState.Stunned, placement.Square)
                            : placement)
                        .ToArray()
                };
                outcomes.Add($"{victimName} is stunned");
            }
        }

        var summary = outcomes.Count == 0
            ? "No players are on the pitch for the ref to single out."
            : $"{string.Join("; ", outcomes)}.";
        return new KickoffEventResult(current, "Officious Ref", summary);
    }

    private KickoffEventResult ResolvePitchInvasion(MatchState match)
    {
        var stunned = new List<Guid>();
        var nextPlacements = match.Placements
            .Select(placement =>
            {
                if (placement.State != PlayerPitchState.Standing || placement.Square is null)
                {
                    return placement;
                }

                var roll = _dice.RollD6();
                if (roll < 6)
                {
                    return placement;
                }

                stunned.Add(placement.PlayerId);
                return ApplyPitchState(match, placement, PlayerPitchState.Stunned, placement.Square);
            })
            .ToArray();

        return new KickoffEventResult(
            match with { Placements = nextPlacements },
            "Pitch Invasion",
            stunned.Count == 0 ? "The crowd surges, but no players are stunned." : $"The crowd stuns {stunned.Count} player{(stunned.Count == 1 ? "" : "s")}.");
    }

    private KickoffEventResult ResolveChangingWeather(MatchState match)
    {
        var weatherRoll = Roll2D6();
        var weather = ResolveWeather(weatherRoll);
        var nextMatch = match with { Weather = weather };
        var extraScatter = weather == WeatherCondition.Nice;
        var message = extraScatter
            ? $"Weather roll {weatherRoll}: {FormatWeather(weather)}. A gentle gust will scatter the ball one extra square."
            : $"Weather roll {weatherRoll}: {FormatWeather(weather)}.";
        return new KickoffEventResult(nextMatch, "Changing Weather", message, ExtraScatter: extraScatter);
    }


    private MatchState ResolveKickoffLanding(MatchState match, Ruleset ruleset, LeagueTeam receivingTeam, PitchSquare square)
    {
        var catcher = match.Placements.FirstOrDefault(placement =>
            placement.Square == square && placement.State == PlayerPitchState.Standing);
        if (catcher is not null)
        {
            return ResolveBallLanding(match, ruleset, receivingTeam, square);
        }

        var bounceSquare = ScatterFrom(ruleset, square);
        var bounceLog = new MatchLogEntry { Message = $"The kicked ball lands in an empty square and bounces to {bounceSquare.X},{bounceSquare.Y}." };

        if (!IsOnPitch(ruleset, bounceSquare))
        {
            return CreatePendingTouchback(match, receivingTeam,
            [
                .. match.Log,
                bounceLog,
                new MatchLogEntry { Message = "Ball went out of bounds. Touchback. Choose a receiving player to carry the ball." }
            ]);
        }

        var bouncedMatch = match with { Log = [.. match.Log, bounceLog] };
        return ResolveBallLanding(bouncedMatch, ruleset, receivingTeam, bounceSquare);
    }

    private static bool HasKickPlayer(MatchState match, Ruleset ruleset, LeagueTeam kickingTeam)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == kickingTeam.Id &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is not null &&
            PlayerHasHookedEffect(ruleset, FindTeamPlayer(kickingTeam, placement.PlayerId), GameEventKind.Kickoff, GameEventStage.BeforeEvent, SkillEffect.Kick));
    }

    private static string FormatKickoffEventKind(KickoffEventKind kind)
    {
        return kind switch
        {
            KickoffEventKind.SolidDefence => "Solid Defence",
            KickoffEventKind.HighKick => "High Kick",
            KickoffEventKind.QuickSnap => "Quick Snap",
            KickoffEventKind.Blitz => "Blitz",
            _ => kind.ToString()
        };
    }
}
