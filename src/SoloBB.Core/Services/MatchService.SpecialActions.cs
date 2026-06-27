using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;
using static SoloBB.Core.Services.RollTargets;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState FoulPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Guid foulerPlayerId,
        LeagueTeam victimTeam,
        Guid victimPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only foul during a player turn.");
        }

        if (foulingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can foul during its turn.");
        }

        if (foulingTeam.Id == victimTeam.Id)
        {
            throw new InvalidOperationException("A player cannot foul a teammate.");
        }

        if (match.PendingBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending block choice before taking another action.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before taking another action.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before taking another action.");
        }

        if (match.PendingReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending reroll before taking another action.");
        }

        var fouler = FindTeamPlayer(foulingTeam, foulerPlayerId);
        var victim = FindTeamPlayer(victimTeam, victimPlayerId);
        var foulerPlacement = FindStandingPlacement(match, foulerPlayerId, foulingTeam.Id, "fouler");
        var victimPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == victimPlayerId)
            ?? throw new InvalidOperationException("Victim is not part of this match.");

        if (victimPlacement.TeamId != victimTeam.Id)
        {
            throw new InvalidOperationException("Victim is assigned to the wrong team.");
        }

        if (victimPlacement.Square is null ||
            victimPlacement.State is not (PlayerPitchState.Prone or PlayerPitchState.Stunned))
        {
            throw new InvalidOperationException("Only prone or stunned players on the pitch can be fouled.");
        }

        if (!PlacementsAreAdjacent(foulerPlacement, victimPlacement))
        {
            throw new InvalidOperationException("Fouls require adjacent players.");
        }

        var attackAssists = CountFoulAssists(match, foulingTeam.Id, victimPlacement, foulerPlayerId);
        var defenseAssists = CountFoulAssists(match, victimTeam.Id, victimPlacement, foulerPlayerId);
        var foulAction = BeginPlayerAction(match, ruleset, foulingTeam, fouler, PlayerTurnAction.Foul, goForItsUsed: 0);
        if (foulAction.Prevented)
        {
            return foulAction.Match;
        }

        return ResolveFoulAfterActivation(foulAction.Match, ruleset, foulingTeam, fouler, foulerPlacement, victimTeam, victim, victimPlacement, attackAssists, defenseAssists, allowSneakyGitMove: true);
    }

    public MatchState PileDriverPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Guid foulerPlayerId,
        LeagueTeam victimTeam,
        Guid victimPlayerId)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Players can only use Pile Driver during a player turn.");
        }

        if (foulingTeam.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can use Pile Driver during its turn.");
        }

        EnsureNoPendingChoices(match);

        var fouler = FindTeamPlayer(foulingTeam, foulerPlayerId);
        if (!PlayerHasHookedEffect(ruleset, fouler, GameEventKind.Push, GameEventStage.AfterEvent, SkillEffect.PileDriver))
        {
            throw new InvalidOperationException($"{fouler.Name} does not have Pile Driver.");
        }

        var activation = GetActivation(match, fouler.Id, foulingTeam.Id);
        if (activation is null || activation.BlocksMade == 0 || activation.Action is not (PlayerTurnAction.Block or PlayerTurnAction.Blitz))
        {
            throw new InvalidOperationException("Pile Driver can only be used after this player knocks an opponent down with a block.");
        }

        var victim = FindTeamPlayer(victimTeam, victimPlayerId);
        var foulerPlacement = FindStandingPlacement(match, foulerPlayerId, foulingTeam.Id, "fouler");
        var victimPlacement = match.Placements.FirstOrDefault(placement => placement.PlayerId == victimPlayerId)
            ?? throw new InvalidOperationException("Victim is not part of this match.");

        if (victimPlacement.TeamId != victimTeam.Id)
        {
            throw new InvalidOperationException("Victim is assigned to the wrong team.");
        }

        if (victimPlacement.Square is null ||
            victimPlacement.State is not (PlayerPitchState.Prone or PlayerPitchState.Stunned))
        {
            throw new InvalidOperationException("Only prone or stunned players on the pitch can be fouled.");
        }

        if (!PlacementsAreAdjacent(foulerPlacement, victimPlacement))
        {
            throw new InvalidOperationException("Pile Driver requires an adjacent prone or stunned opponent.");
        }

        var attackAssists = CountFoulAssists(match, foulingTeam.Id, victimPlacement, foulerPlayerId);
        var defenseAssists = CountFoulAssists(match, victimTeam.Id, victimPlacement, foulerPlayerId);
        var proneMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == fouler.Id
                    ? placement with { State = PlayerPitchState.Prone, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = $"{fouler.Name} uses Pile Driver and is placed prone." }]
        };

        return ResolveFoulAfterActivation(proneMatch, ruleset, foulingTeam, fouler, foulerPlacement, victimTeam, victim, victimPlacement, attackAssists, defenseAssists, allowSneakyGitMove: false);
    }

    public MatchState StabPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "stab", "Stab");
        if (context.Prevented)
        {
            return context.Match;
        }

        return ResolveSpecialArmorAttack(context.Match, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name} stabs {context.Target.Name}");
    }

    public MatchState ChainsawPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "chainsaw", "Chainsaw");
        if (context.Prevented)
        {
            return context.Match;
        }

        var startRoll = _dice.RollD6();
        if (startRoll == 1)
        {
            var selfHit = ResolveSpecialArmorAttack(context.Match, ruleset, attackerTeam, context.Actor, context.ActorPlacement, armorModifier: 3, $"{context.Actor.Name}'s chainsaw kicks back");
            return selfHit with
            {
                Log = [.. selfHit.Log, new MatchLogEntry { Message = $"{context.Actor.Name} starts the chainsaw: rolled 1, it kicks back." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} starts the chainsaw: rolled {startRoll}, attack continues." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 3, $"{context.Actor.Name} attacks {context.Target.Name} with a chainsaw");
    }

    public MatchState ProjectileVomitPlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "projectile-vomit", "Projectile Vomit");
        if (context.Prevented)
        {
            return context.Match;
        }

        var vomitRoll = _dice.RollD6();
        if (vomitRoll == 1)
        {
            var selfHit = ResolveSpecialArmorAttack(context.Match, ruleset, attackerTeam, context.Actor, context.ActorPlacement, armorModifier: 0, $"{context.Actor.Name}'s Projectile Vomit hits themselves");
            return selfHit with
            {
                Log = [.. selfHit.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Projectile Vomit: rolled 1, the vomit hits themselves." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} uses Projectile Vomit: rolled {vomitRoll}, target is hit." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name} vomits on {context.Target.Name}");
    }

    public MatchState HypnoticGazePlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "hypnotic-gaze", "Hypnotic Gaze");
        if (context.Prevented)
        {
            return context.Match;
        }

        return ResolveHypnoticGazeRoll(context.Match, ruleset, attackerTeam, context.Actor, targetTeam, targetPlayerId);
    }

    private MatchState ResolveHypnoticGazeRoll(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam actorTeam,
        Player actor,
        LeagueTeam targetTeam,
        Guid targetPlayerId,
        int? forcedRoll = null)
    {
        var targetPlayer = FindTeamPlayer(targetTeam, targetPlayerId);
        var roll = forcedRoll ?? _dice.RollD6();
        var target = Math.Clamp(actor.Stats.Agility, 2, 6);
        if (!RollSucceeds(roll, target, ruleset.Dice))
        {
            if (forcedRoll is null && ActionRerollOffered(match, ruleset, actorTeam, actor, GameEventKind.SpecialAction))
            {
                var square = FindPlacement(match, targetPlayerId)?.Square ?? new PitchSquare(0, 0);
                return CreatePendingActionReroll(
                    match, ruleset, actorTeam, actor, PendingRerollKind.HypnoticGaze, GameEventKind.SpecialAction, roll, target,
                    ActionRerollContext(match, square) with { SecondaryPlayerId = targetPlayerId },
                    $"{actor.Name} uses Hypnotic Gaze on {targetPlayer.Name}: rolled {roll} vs {target}+, failed. Choose whether to reroll.");
            }

            return match with
            {
                Log = [.. match.Log, new MatchLogEntry { Message = $"{actor.Name} uses Hypnotic Gaze on {targetPlayer.Name}: rolled {roll} vs {target}+, failed." }]
            };
        }

        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == targetPlayerId
                    ? placement with { TackleZonesLost = true }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = $"{actor.Name} uses Hypnotic Gaze on {targetPlayer.Name}: rolled {roll} vs {target}+, tackle zone removed." }]
        };
    }

    public MatchState BreatheFirePlayer(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam attackerTeam,
        Guid attackerPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId)
    {
        var context = BeginSpecialTargetAction(match, ruleset, attackerTeam, attackerPlayerId, targetTeam, targetPlayerId, "breathe-fire", "Breathe Fire");
        if (context.Prevented)
        {
            return context.Match;
        }

        var roll = _dice.RollD6();
        if (roll == 1)
        {
            return context.Match with
            {
                Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} breathes fire at {context.Target.Name}: rolled 1, no effect." }]
            };
        }

        return ResolveSpecialArmorAttack(context.Match with
        {
            Log = [.. context.Match.Log, new MatchLogEntry { Message = $"{context.Actor.Name} breathes fire at {context.Target.Name}: rolled {roll}, target is hit." }]
        }, ruleset, targetTeam, context.Target, context.TargetPlacement, armorModifier: 0, $"{context.Actor.Name}'s fire hits {context.Target.Name}");
    }

    public MatchState ThrowBomb(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam throwingTeam,
        Guid bomberPlayerId,
        PitchSquare targetSquare,
        LeagueTeam opposingTeam)
    {
        var bomber = FindTeamPlayer(throwingTeam, bomberPlayerId);
        if (!PlayerHasHookedEffect(ruleset, bomber, GameEventKind.BombThrow, GameEventStage.BeforeEvent, SkillEffect.Bombardier))
        {
            throw new InvalidOperationException($"{bomber.Name} does not have Bombardier.");
        }

        var placement = ValidateSpecialActor(match, throwingTeam, bomber, requireStanding: true);
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"Square {targetSquare.X},{targetSquare.Y} is outside the pitch.");
        }

        var action = BeginPlayerAction(match, ruleset, throwingTeam, bomber, PlayerTurnAction.Special, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        return ResolveBombThrow(action.Match, ruleset, throwingTeam, opposingTeam, bomber, targetSquare, thrownBack: false);
    }

    public MatchState ThrowPendingBomb(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam throwingTeam,
        LeagueTeam opposingTeam,
        PitchSquare targetSquare)
    {
        var pending = match.PendingBombThrow
            ?? throw new InvalidOperationException("There is no pending bomb throw.");

        if (pending.ThrowingTeamId != throwingTeam.Id || pending.OpposingTeamId != opposingTeam.Id)
        {
            throw new InvalidOperationException("Pending bomb throw belongs to different teams.");
        }

        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"Square {targetSquare.X},{targetSquare.Y} is outside the pitch.");
        }

        var thrower = FindTeamPlayer(throwingTeam, pending.ThrowerPlayerId);
        return ResolveBombThrow(match with { PendingBombThrow = null }, ruleset, throwingTeam, opposingTeam, thrower, targetSquare, thrownBack: true);
    }

    /// <summary>
    /// Resolves a bomb throw (Bombardier or a caught bomb thrown back) as a Pass-style test, offering a
    /// team/Pro reroll on a miss before the bomb scatters and lands.
    /// </summary>
    private MatchState ResolveBombThrow(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam throwingTeam,
        LeagueTeam opposingTeam,
        Player thrower,
        PitchSquare targetSquare,
        bool thrownBack,
        int? forcedRoll = null)
    {
        var throwerPlacement = FindStandingPlacement(match, thrower.Id, throwingTeam.Id, "bomb thrower");
        var passRange = ResolvePassRange(throwerPlacement.Square!, targetSquare) ?? throw new InvalidOperationException("The target is out of passing range.");
        var passTarget = PassingTarget(ruleset, thrower, passRange, match.Weather);
        var passRoll = forcedRoll ?? _dice.RollD6();
        var accurate = RollSucceeds(passRoll, passTarget, ruleset.Dice);
        var bombText = thrownBack ? "the caught bomb" : "a bomb";

        if (!accurate && forcedRoll is null &&
            throwingTeam.Id == match.ActiveTeamId &&
            ActionRerollOffered(match, ruleset, throwingTeam, thrower, GameEventKind.BombThrow))
        {
            return CreatePendingActionReroll(
                match, ruleset, throwingTeam, thrower, PendingRerollKind.BombThrow, GameEventKind.BombThrow, passRoll, passTarget,
                ActionRerollContext(match, targetSquare) with { BombThrownBack = thrownBack },
                $"{thrower.Name} throws {bombText} to {targetSquare.X},{targetSquare.Y}: rolled {passRoll} vs {passTarget}+, off target. Choose whether to reroll.");
        }

        var landingSquare = accurate ? targetSquare : ScatterFrom(ruleset, targetSquare);
        var nextMatch = match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{thrower.Name} throws {bombText} to {targetSquare.X},{targetSquare.Y}: rolled {passRoll} vs {passTarget}+{(accurate ? "." : $", scatters to {landingSquare.X},{landingSquare.Y}.")}" }
            ]
        };

        return ResolveBombLanding(nextMatch, ruleset, throwingTeam, opposingTeam, landingSquare);
    }

    public MatchState ThrowTeamMate(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid throwerPlayerId,
        Guid thrownPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? opposingTeam = null)
    {
        var context = ValidateTeamMateLaunch(match, ruleset, team, throwerPlayerId, thrownPlayerId, targetSquare, "throw-team-mate", "Throw Team-Mate");
        var action = BeginPlayerAction(match, ruleset, team, context.Actor, PlayerTurnAction.Pass, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        var nextMatch = action.Match;
        if (PlayerHasHookedEffect(ruleset, context.Actor, GameEventKind.ThrowTeamMate, GameEventStage.BeforeEvent, SkillEffect.AlwaysHungry))
        {
            var hungryRoll = _dice.RollD6();
            if (hungryRoll == 1)
            {
                var reroll = _dice.RollD6();
                if (reroll == 1)
                {
                    var eatenMatch = RemoveLaunchedPlayer(nextMatch, context.Launched.Id, PlayerPitchState.Casualty);
                    var loggedEaten = eatenMatch with
                    {
                        Log =
                        [
                            .. eatenMatch.Log,
                            new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled 1 then 1, {context.Launched.Name} is eaten." }
                        ]
                    };

                    return match.Ball.CarrierPlayerId == context.Launched.Id
                        ? ApplyTurnover(loggedEaten with { Ball = new BallState() }, ruleset, team.Id)
                        : loggedEaten;
                }

                nextMatch = nextMatch with
                {
                    Log =
                    [
                        .. nextMatch.Log,
                        new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled 1 then {reroll}, throw continues." }
                    ]
                };
            }
            else
            {
                nextMatch = nextMatch with
                {
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{context.Actor.Name} checks Always Hungry: rolled {hungryRoll}, throw continues." }]
                };
            }
        }

        return ResolveTeamMateThrow(nextMatch, ruleset, team, opposingTeam, context.Actor, context.Launched, targetSquare, "thrown");
    }

    public MatchState KickTeamMate(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid kickerPlayerId,
        Guid kickedPlayerId,
        PitchSquare targetSquare,
        LeagueTeam? opposingTeam = null)
    {
        var context = ValidateTeamMateLaunch(match, ruleset, team, kickerPlayerId, kickedPlayerId, targetSquare, "kick-team-mate", "Kick Team-Mate");
        var action = BeginPlayerAction(match, ruleset, team, context.Actor, PlayerTurnAction.Special, goForItsUsed: 0);
        if (action.Prevented)
        {
            return action.Match;
        }

        return ResolveTeamMateThrow(action.Match, ruleset, team, opposingTeam, context.Actor, context.Launched, targetSquare, "kicked");
    }

    /// <summary>
    /// Resolves the accuracy roll for a launched team-mate (a Pass-style test when thrown, a 1-scatters
    /// roll when kicked), offering a team/Pro reroll on a miss, then resolves where the player lands.
    /// </summary>
    private MatchState ResolveTeamMateThrow(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player actor,
        Player launched,
        PitchSquare targetSquare,
        string launchKind,
        int? forcedRoll = null)
    {
        var isThrow = launchKind == "thrown";
        var actorPlacement = FindStandingPlacement(match, actor.Id, team.Id, "launcher");
        PassRange? passRange = null;
        int rollTarget;
        if (isThrow)
        {
            passRange = ResolvePassRange(actorPlacement.Square!, targetSquare) ?? throw new InvalidOperationException("The target is out of passing range.");
            var throwModifier = PlayerHasHookedEffect(ruleset, actor, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.StrongArm) ? -1 : 0;
            rollTarget = Math.Clamp(PassingTarget(ruleset, actor, passRange, match.Weather) + throwModifier + 1, 2, 6);
        }
        else
        {
            // A kicked team-mate only scatters on a 1, i.e. it succeeds on a 2+.
            rollTarget = 2;
        }

        var roll = forcedRoll ?? _dice.RollD6();
        var accurate = isThrow ? RollSucceeds(roll, rollTarget, ruleset.Dice) : roll != 1;

        if (!accurate && forcedRoll is null && ActionRerollOffered(match, ruleset, team, actor, GameEventKind.ThrowTeamMate))
        {
            var verb = isThrow ? "throws" : "kicks";
            var rollText = isThrow ? $"rolled {roll} vs {rollTarget}+" : $"rolled {roll}";
            return CreatePendingActionReroll(
                match, ruleset, team, actor,
                isThrow ? PendingRerollKind.ThrowTeamMate : PendingRerollKind.KickTeamMate,
                GameEventKind.ThrowTeamMate, roll, rollTarget,
                ActionRerollContext(match, targetSquare) with { SecondaryPlayerId = launched.Id, LaunchKind = launchKind },
                $"{actor.Name} {verb} {launched.Name} to {targetSquare.X},{targetSquare.Y}: {rollText}, inaccurate. Choose whether to reroll.");
        }

        var landingSquare = accurate
            ? targetSquare
            : ScatterLaunchedPlayer(ruleset, targetSquare, launched, inaccurateDistance: 3);
        var message = isThrow
            ? $"{actor.Name} throws {launched.Name} to {targetSquare.X},{targetSquare.Y}: {passRange!.Name} rolled {roll} vs {rollTarget}+{(accurate ? ", accurate." : $", inaccurate to {landingSquare.X},{landingSquare.Y}.")}"
            : $"{actor.Name} kicks {launched.Name} to {targetSquare.X},{targetSquare.Y}: rolled {roll}{(accurate ? ", on target." : $", scatters to {landingSquare.X},{landingSquare.Y}.")}";

        var nextMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == launched.Id
                    ? placement with { Square = null }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };

        return ResolveLaunchedPlayerLanding(nextMatch, ruleset, team, opposingTeam, launched, landingSquare, launchKind);
    }

    private MatchState ResolveFoulAfterActivation(
        MatchState activatedMatch,
        Ruleset ruleset,
        LeagueTeam foulingTeam,
        Player fouler,
        PlayerPlacement foulerPlacement,
        LeagueTeam victimTeam,
        Player victim,
        PlayerPlacement victimPlacement,
        int attackAssists,
        int defenseAssists,
        bool allowSneakyGitMove)
    {
        var armorRoll = Roll2D6Detailed();
        var hasDirtyPlayer = PlayerHasHookedEffect(ruleset, fouler, GameEventKind.ArmorRoll, GameEventStage.AfterRoll, SkillEffect.DirtyPlayer);
        var hasSneakyGit = PlayerHasHookedEffect(ruleset, fouler, GameEventKind.ArmorRoll, GameEventStage.AfterRoll, SkillEffect.SneakyGit);
        var victimSquare = victimPlacement.Square
            ?? throw new InvalidOperationException("Victim must be on the pitch.");
        var armorTotalWithoutSkill = armorRoll.Total + attackAssists - defenseAssists;
        var dirtyPlayerArmorBonus = hasDirtyPlayer &&
            !PlayerHasHookedEffect(ruleset, victim, GameEventKind.ArmorRoll, GameEventStage.BeforeResolve, SkillEffect.IronHardSkin) &&
            armorTotalWithoutSkill <= victim.Stats.Armor &&
            armorTotalWithoutSkill + 1 > victim.Stats.Armor
                ? 1
                : 0;
        var armorTotal = armorTotalWithoutSkill + dirtyPlayerArmorBonus;
        var log = new List<MatchLogEntry>
        {
            new()
            {
                Message = dirtyPlayerArmorBonus > 0
                    ? $"{fouler.Name} fouls {victim.Name}: armor {armorRoll.Total} +{attackAssists} -{defenseAssists} +1 Dirty Player = {armorTotal} vs AV {victim.Stats.Armor}+."
                    : $"{fouler.Name} fouls {victim.Name}: armor {armorRoll.Total} +{attackAssists} -{defenseAssists} = {armorTotal} vs AV {victim.Stats.Armor}+."
            }
        };

        var nextMatch = activatedMatch;
        var armorBroken = armorTotal > victim.Stats.Armor;
        var sentOff = armorRoll.IsDoubles && !hasSneakyGit;
        if (armorBroken)
        {
            var injuryRoll = Roll2D6Detailed();
            sentOff = sentOff || injuryRoll.IsDoubles;
            var dirtyPlayerInjuryBonus = hasDirtyPlayer && dirtyPlayerArmorBonus == 0 ? 1 : 0;
            var injuryTotal = injuryRoll.Total + dirtyPlayerInjuryBonus;
            var injury = ResolveInjury(ruleset, victim, injuryTotal);
            var apothecary = CreatePendingApothecaryIfAvailable(nextMatch, victimPlacement, victim.Name, injury);
            nextMatch = apothecary.Match;
            injury = apothecary.Injury;
            nextMatch = nextMatch with
            {
                Placements = nextMatch.Placements
                    .Select(placement => placement.PlayerId == victim.Id
                        ? ApplyPitchState(nextMatch, placement, injury.State, OccupiesPitch(injury.State) ? victimSquare : null, injury.Casualty)
                        : placement)
                    .ToArray()
            };
            log.Add(new MatchLogEntry
            {
                Message = dirtyPlayerInjuryBonus > 0
                    ? $"{victim.Name} injury roll {injuryRoll.Total} +1 Dirty Player = {injuryTotal}: {FormatPitchState(injury.State)}."
                    : $"{victim.Name} injury roll {injuryRoll.Total}: {FormatPitchState(injury.State)}."
            });
            if (injury.Casualty is not null)
            {
                log.Add(new MatchLogEntry { Message = $"{victim.Name} casualty roll {injury.Casualty.Roll}: {FormatCasualtyResult(injury.Casualty.Result)}." });
            }
            log.AddRange(apothecary.Log);
        }
        else
        {
            log.Add(new MatchLogEntry { Message = $"{victim.Name}'s armor holds." });
        }

        nextMatch = nextMatch with { Log = [.. nextMatch.Log, .. log] };

        if (!sentOff)
        {
            if (hasSneakyGit && allowSneakyGitMove)
            {
                nextMatch = nextMatch with
                {
                    Activations = nextMatch.Activations
                        .Select(activation =>
                            activation.PlayerId == fouler.Id &&
                            activation.TeamId == foulingTeam.Id &&
                            activation.Half == nextMatch.Half &&
                            activation.Turn == nextMatch.Turn
                                ? activation with { MayMoveAfterFoul = true }
                                : activation)
                        .ToArray(),
                    Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{fouler.Name} may continue moving with Sneaky Git." }]
                };
            }

            return nextMatch;
        }

        if (TeamBribesRemaining(nextMatch, foulingTeam.Id) > 0)
        {
            return nextMatch with
            {
                PendingSendOff = new PendingSendOffChoice
                {
                    TeamId = foulingTeam.Id,
                    PlayerId = fouler.Id,
                    Reason = "foul",
                    BribeAvailable = true
                },
                Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{fouler.Name} may be sent off for the foul. Choose whether to use a bribe." }]
            };
        }

        return ApplyTurnover(SendOffPlayer(nextMatch, fouler.Id, $"{fouler.Name} is sent off for the foul."), ruleset, foulingTeam.Id);
    }

    public MatchState ResolvePendingSendOff(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        bool useBribe)
    {
        var pending = match.PendingSendOff
            ?? throw new InvalidOperationException("There is no pending send-off choice.");

        if (pending.TeamId != team.Id)
        {
            throw new InvalidOperationException("Pending send-off belongs to another team.");
        }

        var player = FindTeamPlayer(team, pending.PlayerId);
        var baseMatch = match with { PendingSendOff = null };
        if (useBribe)
        {
            if (!pending.BribeAvailable || TeamBribesRemaining(baseMatch, team.Id) <= 0)
            {
                throw new InvalidOperationException($"{team.Name} has no bribe available.");
            }

            var bribeRoll = _dice.RollD6();
            var bribeModifier = team.Id == baseMatch.HomeTeamId ? baseMatch.HomeBribeRollModifier : baseMatch.AwayBribeRollModifier;
            var modifiedBribeRoll = Math.Clamp(bribeRoll + bribeModifier, 1, 6);
            var bribedMatch = SpendBribe(baseMatch, team.Id);
            if (modifiedBribeRoll >= 2)
            {
                var keptMatch = bribedMatch with
                {
                    Log = [.. bribedMatch.Log, new MatchLogEntry { Message = $"{team.Name} uses a bribe for {player.Name}: rolled {bribeRoll}{FormatBribeModifier(bribeModifier)}, send-off prevented." }]
                };

                return pending.DriveEnd is null
                    ? keptMatch
                    : ContinueDriveEnd(keptMatch, ruleset, pending.DriveEnd with { ResolvedPlayerIds = [.. pending.DriveEnd.ResolvedPlayerIds, player.Id] });
            }

            baseMatch = bribedMatch with
            {
                Log = [.. bribedMatch.Log, new MatchLogEntry { Message = $"{team.Name} uses a bribe for {player.Name}: rolled {bribeRoll}{FormatBribeModifier(bribeModifier)}, bribe failed." }]
            };
        }
        else if (pending.BribeAvailable)
        {
            baseMatch = baseMatch with
            {
                Log = [.. baseMatch.Log, new MatchLogEntry { Message = $"{team.Name} declines to use a bribe for {player.Name}." }]
            };
        }

        var sentOffMatch = SendOffPlayer(baseMatch, player.Id, $"{player.Name} is sent off for {pending.Reason}.");
        if (pending.DriveEnd is not null)
        {
            return ContinueDriveEnd(sentOffMatch, ruleset, pending.DriveEnd);
        }

        return ApplyTurnover(sentOffMatch, ruleset, team.Id);
    }

    private static string FormatBribeModifier(int modifier)
    {
        return modifier == 0 ? "" : modifier > 0 ? $" + {modifier}" : $" - {Math.Abs(modifier)}";
    }

    private MatchState BeginDriveEnd(MatchState match, Ruleset ruleset, PendingDriveEndContinuation continuation)
    {
        return ContinueDriveEnd(match with { DriveState = DriveState.Ending }, ruleset, continuation);
    }

    private MatchState ContinueDriveEnd(MatchState match, Ruleset ruleset, PendingDriveEndContinuation continuation)
    {
        var nextSecretWeapon = match.Placements.FirstOrDefault(placement =>
            !continuation.ResolvedPlayerIds.Contains(placement.PlayerId) &&
            match.SecretWeaponPlayerIds.Contains(placement.PlayerId) &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve &&
            placement.Square is not null);

        if (nextSecretWeapon is not null)
        {
            var teamId = nextSecretWeapon.TeamId;
            var remainingSecretWeapons = match.Placements.Count(placement =>
                !continuation.ResolvedPlayerIds.Contains(placement.PlayerId) &&
                placement.PlayerId != nextSecretWeapon.PlayerId &&
                match.SecretWeaponPlayerIds.Contains(placement.PlayerId) &&
                placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve &&
                placement.Square is not null);
            if (TeamBribesRemaining(match, teamId) > 0)
            {
                return match with
                {
                    PendingSendOff = new PendingSendOffChoice
                    {
                        TeamId = teamId,
                        PlayerId = nextSecretWeapon.PlayerId,
                        Reason = "Secret Weapon",
                        BribeAvailable = true,
                        DriveEnd = continuation
                    },
                    Log = [.. match.Log, new MatchLogEntry { Message = $"{PlayerName(nextSecretWeapon.PlayerId)} must be sent off for Secret Weapon. Choose whether to use a bribe; {remainingSecretWeapons} more Secret Weapon send-off{(remainingSecretWeapons == 1 ? "" : "s")} remain after this." }]
                };
            }

            var sentOff = SendOffPlayer(match, nextSecretWeapon.PlayerId, $"{PlayerName(nextSecretWeapon.PlayerId)} is sent off for Secret Weapon.");
            return ContinueDriveEnd(sentOff, ruleset, continuation with { ResolvedPlayerIds = [.. continuation.ResolvedPlayerIds, nextSecretWeapon.PlayerId] });
        }

        if (continuation.CompleteMatch)
        {
            return match with
            {
                Phase = MatchPhase.Complete,
                DriveState = DriveState.Complete,
                Turn = ruleset.TurnsPerHalf,
                Activations = [],
                PendingBlock = null,
                PendingBlockReroll = null,
                PendingPush = null,
                PendingInterception = null,
                PendingReroll = null,
                PendingStandFirm = null,
                PendingDivingTackle = null,
                PendingFollowUp = null,
                PendingBombThrow = null,
                PendingMultipleBlock = null,
                PendingSendOff = null,
                PendingKickoffEvent = null,
                Log = [.. match.Log, new MatchLogEntry { Message = "Full time. Match complete." }]
            };
        }

        return continuation.StartSecondHalf
            ? StartSecondHalfSetupAfterDriveEnd(match)
            : StartNextDriveSetup(match, continuation.NextDefenseTeamId);
    }

    private SpecialActionContext BeginSpecialTargetAction(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam actorTeam,
        Guid actorPlayerId,
        LeagueTeam targetTeam,
        Guid targetPlayerId,
        string skillId,
        string actionName)
    {
        if (actorTeam.Id == targetTeam.Id)
        {
            throw new InvalidOperationException($"{actionName} must target an opposing player.");
        }

        var actor = FindTeamPlayer(actorTeam, actorPlayerId);
        if (!PlayerHasSpecialActionEffect(ruleset, actor, skillId))
        {
            throw new InvalidOperationException($"{actor.Name} does not have {actionName}.");
        }

        var actorPlacement = ValidateSpecialActor(match, actorTeam, actor, requireStanding: true);
        var target = FindTeamPlayer(targetTeam, targetPlayerId);
        var targetPlacement = FindStandingPlacement(match, targetPlayerId, targetTeam.Id, "target");
        if (!PlacementsAreAdjacent(actorPlacement, targetPlacement))
        {
            throw new InvalidOperationException($"{actionName} requires adjacent players.");
        }

        var action = BeginPlayerAction(match, ruleset, actorTeam, actor, PlayerTurnAction.Special, goForItsUsed: 0);
        return new SpecialActionContext(action.Match, actor, actorPlacement, target, targetPlacement, action.Prevented);
    }

    private PlayerPlacement ValidateSpecialActor(MatchState match, LeagueTeam team, Player player, bool requireStanding)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Special actions can only be used during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can use a special action during its turn.");
        }

        EnsureNoPendingChoices(match);

        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == player.Id)
            ?? throw new InvalidOperationException("Player is not part of this match.");
        if (placement.TeamId != team.Id)
        {
            throw new InvalidOperationException("Player is assigned to the wrong team.");
        }

        if (placement.Square is null || (requireStanding && placement.State != PlayerPitchState.Standing))
        {
            throw new InvalidOperationException("The player must be standing on the pitch.");
        }

        return placement;
    }

    private MatchState ResolveSpecialArmorAttack(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam targetTeam,
        Player target,
        PlayerPlacement targetPlacement,
        int armorModifier,
        string description)
    {
        var targetSquare = targetPlacement.Square
            ?? throw new InvalidOperationException("Target must be on the pitch.");
        var armorRoll = Roll2D6Detailed();
        var total = armorRoll.Total + armorModifier;
        var armorText = armorModifier == 0
            ? $"{armorRoll.Total}"
            : $"{armorRoll.Total} +{armorModifier} = {total}";
        if (total <= target.Stats.Armor)
        {
            return match with
            {
                Log = [.. match.Log, new MatchLogEntry { Message = $"{description}: armor {armorText} vs AV {target.Stats.Armor}+, no effect." }]
            };
        }

        var injuryRoll = Roll2D6Detailed();
        var injury = ResolveInjury(ruleset, target, injuryRoll.Total);
        var knockedMatch = KnockPlayerDown(match, ruleset, target, targetPlacement, injury, targetSquare);
        var result = knockedMatch with
        {
            Log =
            [
                .. knockedMatch.Log,
                new MatchLogEntry { Message = $"{description}: armor {armorText} vs AV {target.Stats.Armor}+, broken." },
                new MatchLogEntry { Message = $"{target.Name} injury roll {injuryRoll.Total}: {FormatPitchState(injury.State)}." }
            ]
        };

        return targetPlacement.TeamId == match.ActiveTeamId && match.Ball.CarrierPlayerId == target.Id
            ? ApplyTurnover(result, ruleset, targetTeam.Id)
            : result;
    }

    private MatchState ResolveBombLanding(MatchState match, Ruleset ruleset, LeagueTeam throwingTeam, LeagueTeam opposingTeam, PitchSquare landingSquare, int? forcedCatchRoll = null)
    {
        var catcherPlacement = match.Placements.FirstOrDefault(current =>
            current.Square == landingSquare &&
            current.State == PlayerPitchState.Standing &&
            (current.TeamId == throwingTeam.Id || current.TeamId == opposingTeam.Id));
        if (catcherPlacement is null)
        {
            return ResolveBombExplosion(match, ruleset, throwingTeam, opposingTeam, landingSquare);
        }

        var catcherTeam = catcherPlacement.TeamId == throwingTeam.Id ? throwingTeam : opposingTeam;
        var otherTeam = catcherPlacement.TeamId == throwingTeam.Id ? opposingTeam : throwingTeam;
        var catcher = FindTeamPlayer(catcherTeam, catcherPlacement.PlayerId);
        var catchRoll = forcedCatchRoll ?? _dice.RollD6();
        var catchTarget = CatchTarget(ruleset, catcher, match.Weather, CountOpposingTackleZones(match, catcherTeam.Id, catcher.Id, landingSquare));
        if (!RollSucceeds(catchRoll, catchTarget, ruleset.Dice))
        {
            // The catching team may reroll the catch, but only during their own turn (a team reroll can't
            // be used on the opponent's turn). The throwing team is the active team here.
            if (forcedCatchRoll is null &&
                catcherTeam.Id == match.ActiveTeamId &&
                ActionRerollOffered(match, ruleset, catcherTeam, catcher, GameEventKind.BombThrow))
            {
                return CreatePendingActionReroll(
                    match, ruleset, catcherTeam, catcher, PendingRerollKind.BombCatch, GameEventKind.BombThrow, catchRoll, catchTarget,
                    ActionRerollContext(match, landingSquare) with { BounceOriginalTeamId = throwingTeam.Id, BounceOpposingTeamId = opposingTeam.Id },
                    $"{catcher.Name} attempts to catch the bomb: rolled {catchRoll} vs {catchTarget}+, failed. Choose whether to reroll.");
            }

            return ResolveBombExplosion(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{catcher.Name} attempts to catch the bomb: rolled {catchRoll} vs {catchTarget}+, failed and it explodes." }
                ]
            }, ruleset, throwingTeam, opposingTeam, landingSquare);
        }

        return match with
        {
            PendingBombThrow = new PendingBombThrowChoice
            {
                ThrowingTeamId = catcherTeam.Id,
                OpposingTeamId = otherTeam.Id,
                ThrowerPlayerId = catcher.Id,
                BombSquare = landingSquare
            },
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{catcher.Name} catches the bomb: rolled {catchRoll} vs {catchTarget}+. Choose a square to throw it back." }
            ]
        };
    }

    private MatchState ResolveBombExplosion(MatchState match, Ruleset ruleset, LeagueTeam throwingTeam, LeagueTeam opposingTeam, PitchSquare center)
    {
        var affectedSquares = AdjacentSquares(center).Append(center).Distinct().ToArray();
        var nextMatch = match with
        {
            Log = [.. match.Log, new MatchLogEntry { Message = $"Bomb explodes at {center.X},{center.Y}." }]
        };

        foreach (var placement in affectedSquares
            .SelectMany(square => nextMatch.Placements.Where(current => PlacementOccupiesSquare(current, square) && OccupiesPitch(current.State)))
            .ToArray())
        {
            var team = placement.TeamId == throwingTeam.Id ? throwingTeam : opposingTeam;
            var player = FindTeamPlayer(team, placement.PlayerId);
            nextMatch = ResolveSpecialArmorAttack(nextMatch, ruleset, team, player, placement, armorModifier: 0, $"Bomb blast hits {player.Name}");
        }

        return nextMatch;
    }

    private TeamMateLaunchContext ValidateTeamMateLaunch(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Guid actorPlayerId,
        Guid launchedPlayerId,
        PitchSquare targetSquare,
        string requiredSkillId,
        string actionName)
    {
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"{actionName} target must be on the pitch.");
        }

        var actor = FindTeamPlayer(team, actorPlayerId);
        if (!PlayerHasLaunchActionEffect(ruleset, actor, requiredSkillId))
        {
            throw new InvalidOperationException($"{actor.Name} does not have {actionName}.");
        }

        var launched = FindTeamPlayer(team, launchedPlayerId);
        var launchEventKind = LaunchEventKind(requiredSkillId);
        if (!PlayerHasHookedEffect(ruleset, launched, launchEventKind, GameEventStage.BeforeEvent, SkillEffect.RightStuff))
        {
            throw new InvalidOperationException($"{launched.Name} does not have Right Stuff.");
        }

        if (actor.Id == launched.Id)
        {
            throw new InvalidOperationException($"{actionName} requires two different players.");
        }

        var actorPlacement = ValidateSpecialActor(match, team, actor, requireStanding: true);
        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException($"{actionName} target must be on the pitch.");
        }

        var launchedPlacement = FindStandingPlacement(match, launchedPlayerId, team.Id, "launched player");
        if (!PlacementsAreAdjacent(actorPlacement, launchedPlacement))
        {
            throw new InvalidOperationException($"{actionName} requires an adjacent Right Stuff team-mate.");
        }

        return new TeamMateLaunchContext(actor, actorPlacement, launched, launchedPlacement);
    }

    private PitchSquare ScatterLaunchedPlayer(Ruleset ruleset, PitchSquare targetSquare, Player launchedPlayer, int inaccurateDistance)
    {
        if (!PlayerHasHookedEffect(ruleset, launchedPlayer, GameEventKind.BallScatter, GameEventStage.BeforeResolve, SkillEffect.Swoop))
        {
            return ScatterFrom(ruleset, targetSquare, inaccurateDistance);
        }

        var direction = _dice.RollD8();
        var swoopDistance = Math.Max(1, _dice.RollD6() / 2);
        var next = targetSquare;
        for (var step = 0; step < swoopDistance; step++)
        {
            next = ScatterDirection(next, direction);
        }

        return next;
    }

    private MatchState ResolveLaunchedPlayerLanding(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        LeagueTeam? opposingTeam,
        Player launchedPlayer,
        PitchSquare landingSquare,
        string launchKind,
        int collisionDepth = 0,
        int? forcedRoll = null)
    {
        if (!IsOnPitch(ruleset, landingSquare))
        {
            var injury = ResolveFallInjury(launchedPlayer);
            var crowdState = injury.State;
            var outMatch = match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == launchedPlayer.Id
                        ? ApplyPitchState(match, placement, crowdState, OccupiesPitch(crowdState) ? null : null, injury.Casualty)
                        : placement)
                    .ToArray(),
                Ball = match.Ball.CarrierPlayerId == launchedPlayer.Id ? new BallState() : match.Ball,
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} is {launchKind} out of bounds and crashes." }
                ]
            };

            return ApplyTurnover(outMatch, ruleset, team.Id);
        }

        var occupant = match.Placements.FirstOrDefault(placement =>
            placement.PlayerId != launchedPlayer.Id &&
            PlacementOccupiesSquare(placement, landingSquare) &&
            OccupiesPitch(placement.State));
        if (occupant is not null)
        {
            var occupantPlayer = FindCollisionPlayer(team, opposingTeam, occupant);
            var knockedMatch = KnockPlayerDown(match, ruleset, occupantPlayer, occupant, ResolveFallInjury(occupantPlayer), landingSquare);
            var collisionMatch = knockedMatch with
            {
                Log =
                [
                    .. knockedMatch.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} crashes into {occupantPlayer.Name} at {landingSquare.X},{landingSquare.Y}; {occupantPlayer.Name} is knocked down." }
                ]
            };
            var nextLandingSquare = ScatterFrom(ruleset, landingSquare);
            var scatteredMatch = collisionMatch with
            {
                Log =
                [
                    .. collisionMatch.Log,
                    new MatchLogEntry { Message = $"{launchedPlayer.Name} scatters from the occupied square to {nextLandingSquare.X},{nextLandingSquare.Y}." }
                ]
            };

            if (collisionDepth >= 7)
            {
                var injury = ResolveFallInjury(launchedPlayer);
                var crashMatch = KnockLaunchedPlayerDown(scatteredMatch, ruleset, team, launchedPlayer, nextLandingSquare, injury, $"{launchedPlayer.Name}'s collision chain cannot be resolved and they crash.");
                return ApplyTurnover(crashMatch, ruleset, team.Id);
            }

            return ResolveLaunchedPlayerLanding(scatteredMatch, ruleset, team, opposingTeam, launchedPlayer, nextLandingSquare, launchKind, collisionDepth + 1);
        }

        var target = LandingTarget(ruleset, launchedPlayer, CountOpposingTackleZones(match, team.Id, launchedPlayer.Id, landingSquare));
        var roll = forcedRoll ?? _dice.RollD6();
        if (!RollSucceeds(roll, target, ruleset.Dice))
        {
            if (forcedRoll is null && ActionRerollOffered(match, ruleset, team, launchedPlayer, GameEventKind.SpecialAction))
            {
                return CreatePendingActionReroll(
                    match, ruleset, team, launchedPlayer, PendingRerollKind.Landing, GameEventKind.SpecialAction, roll, target,
                    ActionRerollContext(match, landingSquare) with { LaunchKind = launchKind, CollisionDepth = collisionDepth },
                    $"{launchedPlayer.Name} attempts to land after being {launchKind}: rolled {roll} vs {target}+, failed. Choose whether to reroll.");
            }

            var injury = ResolveFallInjury(launchedPlayer);
            var crashMatch = KnockLaunchedPlayerDown(match, ruleset, team, launchedPlayer, landingSquare, injury, $"{launchedPlayer.Name} attempts to land after being {launchKind}: rolled {roll} vs {target}+, failed.");
            return ApplyTurnover(crashMatch, ruleset, team.Id);
        }

        var landedMatch = match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == launchedPlayer.Id
                    ? placement with { Square = landingSquare, State = PlayerPitchState.Standing, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{launchedPlayer.Name} lands after being {launchKind}: rolled {roll} vs {target}+, success at {landingSquare.X},{landingSquare.Y}." }
            ]
        };

        return IsTouchdown(landedMatch, ruleset, team, launchedPlayer.Id, landingSquare)
            ? ScoreTouchdown(landedMatch, ruleset, team)
            : landedMatch;
    }

    private static Player FindCollisionPlayer(LeagueTeam team, LeagueTeam? opposingTeam, PlayerPlacement placement)
    {
        if (placement.TeamId == team.Id)
        {
            return FindTeamPlayer(team, placement.PlayerId);
        }

        if (opposingTeam is not null && placement.TeamId == opposingTeam.Id)
        {
            return FindTeamPlayer(opposingTeam, placement.PlayerId);
        }

        throw new InvalidOperationException("An opposing team is required to resolve occupied launch collisions.");
    }

    private MatchState KnockLaunchedPlayerDown(MatchState match, Ruleset ruleset, LeagueTeam team, Player launchedPlayer, PitchSquare landingSquare, InjuryResolution injury, string message)
    {
        var placement = match.Placements.First(placement => placement.PlayerId == launchedPlayer.Id);
        var apothecary = CreatePendingApothecaryIfAvailable(match, placement, launchedPlayer.Name, injury);
        var nextMatch = apothecary.Match;
        injury = apothecary.Injury;
        var ball = nextMatch.Ball;
        var log = new List<MatchLogEntry> { new() { Message = message } };
        log.AddRange(injury.Log ?? []);
        nextMatch = ApplyPlayerPitchState(nextMatch, launchedPlayer.Id, injury.State, OccupiesPitch(injury.State) ? landingSquare : null, injury.Casualty);

        if (ball.CarrierPlayerId == launchedPlayer.Id)
        {
            var scatterSquare = ScatterFrom(ruleset, landingSquare);
            var preLandingLog = nextMatch.Log;
            var landing = ResolveLooseBall(nextMatch, ruleset, scatterSquare);
            nextMatch = landing.Match with { Log = preLandingLog };
            ball = nextMatch.Ball;
            log.Add(new MatchLogEntry { Message = $"Ball scatters to {scatterSquare.X},{scatterSquare.Y}." });
            log.AddRange(landing.Log);
        }

        log.AddRange(apothecary.Log);

        return nextMatch with
        {
            Ball = ball,
            Log = [.. nextMatch.Log, .. log]
        };
    }

    private static MatchState RemoveLaunchedPlayer(MatchState match, Guid playerId, PlayerPitchState state)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == playerId
                    ? placement with { Square = null, State = state, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null }
                    : placement)
                .ToArray()
        };
    }

    private static bool PlayerHasLaunchActionEffect(Ruleset ruleset, Player player, string requiredSkillId)
    {
        var requiredEffect = requiredSkillId switch
        {
            "throw-team-mate" => SkillEffect.ThrowTeamMate,
            "kick-team-mate" => SkillEffect.KickTeamMate,
            _ => throw new InvalidOperationException($"Unknown launch skill '{requiredSkillId}'.")
        };

        return PlayerHasHookedEffect(ruleset, player, LaunchEventKind(requiredSkillId), GameEventStage.BeforeEvent, requiredEffect);
    }

    private static bool PlayerHasSpecialActionEffect(Ruleset ruleset, Player player, string skillId)
    {
        var requiredEffect = skillId switch
        {
            "breathe-fire" => SkillEffect.BreatheFire,
            "chainsaw" => SkillEffect.Chainsaw,
            "hypnotic-gaze" => SkillEffect.HypnoticGaze,
            "projectile-vomit" => SkillEffect.ProjectileVomit,
            "stab" => SkillEffect.Stab,
            _ => throw new InvalidOperationException($"Unknown special action skill '{skillId}'.")
        };

        return PlayerHasHookedEffect(ruleset, player, GameEventKind.SpecialAction, GameEventStage.BeforeEvent, requiredEffect);
    }

    private static GameEventKind LaunchEventKind(string requiredSkillId)
    {
        return requiredSkillId switch
        {
            "throw-team-mate" => GameEventKind.ThrowTeamMate,
            "kick-team-mate" => GameEventKind.KickTeamMate,
            _ => throw new InvalidOperationException($"Unknown launch skill '{requiredSkillId}'.")
        };
    }

    private FoulAppearanceResolution ResolveFoulAppearance(MatchState match, Ruleset ruleset, Player attacker, Player defender)
    {
        if (!PlayerHasHookedEffect(ruleset, defender, GameEventKind.BlockRoll, GameEventStage.BeforeEvent, SkillEffect.FoulAppearance))
        {
            return new FoulAppearanceResolution(match, false);
        }

        var roll = _dice.RollD6();
        if (roll != 1)
        {
            return new FoulAppearanceResolution(match with
            {
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{attacker.Name} checks Foul Appearance against {defender.Name}: rolled {roll}, action continues." }
                ]
            }, false);
        }

        return new FoulAppearanceResolution(match with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{attacker.Name} checks Foul Appearance against {defender.Name}: rolled 1, action wasted." }
            ]
        }, true);
    }
}
