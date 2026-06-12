using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public void RegisterTeams(LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        _playerNames = homeTeam.Players.Concat(awayTeam.Players)
            .ToDictionary(p => p.Id, p => p.Name);
        _teamsById = new Dictionary<Guid, LeagueTeam>
        {
            [homeTeam.Id] = homeTeam,
            [awayTeam.Id] = awayTeam
        };
    }

    private string PlayerName(Guid playerId) =>
        _playerNames.TryGetValue(playerId, out var name) ? name : playerId.ToString();

    public MatchState CreateHotseatMatch(Ruleset ruleset, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return CreateHotseatMatch(
            ruleset,
            homeTeam,
            awayTeam,
            new TeamInducementPlan { TeamId = homeTeam.Id },
            new TeamInducementPlan { TeamId = awayTeam.Id });
    }

    public MatchState CreateHotseatMatch(
        Ruleset ruleset,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        TeamInducementPlan homeInducements,
        TeamInducementPlan awayInducements)
    {
        if (homeTeam.Id == awayTeam.Id)
        {
            throw new InvalidOperationException("Home and away teams must be different teams.");
        }

        if (homeInducements.TeamId != homeTeam.Id || awayInducements.TeamId != awayTeam.Id)
        {
            throw new InvalidOperationException("Inducement plans must match the selected teams.");
        }

        const int minimumPlayersToField = 3;
        if (homeTeam.Players.Count < minimumPlayersToField || awayTeam.Players.Count < minimumPlayersToField)
        {
            throw new InvalidOperationException($"Both teams must have at least {minimumPlayersToField} players available.");
        }

        var initialMatch = new MatchState
        {
            Id = Guid.NewGuid(),
            RulesetId = ruleset.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ActiveTeamId = awayTeam.Id,
            FirstHalfReceivingTeamId = homeTeam.Id,
            Phase = MatchPhase.DefenseSetup,
            HomeRerollsRemaining = homeTeam.Rerolls,
            AwayRerollsRemaining = awayTeam.Rerolls,
            HomeTeamRerolls = homeTeam.Rerolls,
            AwayTeamRerolls = awayTeam.Rerolls,
            HomeBribesRemaining = homeInducements.Bribes + PreGameService.SelectedInducementCount(homeInducements, "bribe") + SelectedOptionEffectCount(ruleset, homeInducements, "biased-referee", "referee-friendly"),
            AwayBribesRemaining = awayInducements.Bribes + PreGameService.SelectedInducementCount(awayInducements, "bribe") + SelectedOptionEffectCount(ruleset, awayInducements, "biased-referee", "referee-friendly"),
            HomeTreasurySpent = homeInducements.TreasurySpent,
            AwayTreasurySpent = awayInducements.TreasurySpent,
            HomeLeaderRerollAvailable = HasLeaderPlayer(ruleset, homeTeam),
            AwayLeaderRerollAvailable = HasLeaderPlayer(ruleset, awayTeam),
            HomeCheerleaders = homeTeam.Cheerleaders,
            AwayCheerleaders = awayTeam.Cheerleaders,
            HomeAssistantCoaches = homeTeam.AssistantCoaches,
            AwayAssistantCoaches = awayTeam.AssistantCoaches,
            HomeApothecariesRemaining = homeTeam.Apothecaries,
            AwayApothecariesRemaining = awayTeam.Apothecaries,
            HomeBloodweiserKegs = PreGameService.SelectedInducementCount(homeInducements, "bloodweiser-keg") + SelectedOptionEffectCount(ruleset, homeInducements, "infamous-coaching-staff", "staff-recovery"),
            AwayBloodweiserKegs = PreGameService.SelectedInducementCount(awayInducements, "bloodweiser-keg") + SelectedOptionEffectCount(ruleset, awayInducements, "infamous-coaching-staff", "staff-recovery"),
            HomeWeatherMagesRemaining = PreGameService.SelectedInducementCount(homeInducements, "weather-mage"),
            AwayWeatherMagesRemaining = PreGameService.SelectedInducementCount(awayInducements, "weather-mage"),
            HomeSpecialPlaysRemaining = PreGameService.SelectedInducementCount(homeInducements, "special-play"),
            AwaySpecialPlaysRemaining = PreGameService.SelectedInducementCount(awayInducements, "special-play"),
            HomeHasMasterChef = PreGameService.SelectedInducementCount(homeInducements, "halfling-master-chef") > 0,
            AwayHasMasterChef = PreGameService.SelectedInducementCount(awayInducements, "halfling-master-chef") > 0,
            HomeWizardEffect = SelectedOptionEffect(ruleset, homeInducements, "wizard"),
            AwayWizardEffect = SelectedOptionEffect(ruleset, awayInducements, "wizard"),
            HomeWizardsRemaining = PreGameService.SelectedInducementCount(homeInducements, "wizard"),
            AwayWizardsRemaining = PreGameService.SelectedInducementCount(awayInducements, "wizard"),
            HomeBribeRollModifier = SelectedOptionEffectCount(ruleset, homeInducements, "biased-referee", "referee-friendly") - SelectedOptionEffectCount(ruleset, awayInducements, "biased-referee", "referee-intimidating"),
            AwayBribeRollModifier = SelectedOptionEffectCount(ruleset, awayInducements, "biased-referee", "referee-friendly") - SelectedOptionEffectCount(ruleset, homeInducements, "biased-referee", "referee-intimidating"),
            Placements = CreateInitialPlacements(homeTeam, awayTeam),
            SecretWeaponPlayerIds =
            [
                .. homeTeam.Players.Where(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.DriveEnd, GameEventStage.BeforeResolve, SkillEffect.SecretWeapon)).Select(player => player.Id),
                .. awayTeam.Players.Where(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.DriveEnd, GameEventStage.BeforeResolve, SkillEffect.SecretWeapon)).Select(player => player.Id)
            ],
            PickMeUpPlayerIds =
            [
                .. homeTeam.Players.Where(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.DriveEnd, GameEventStage.AfterEvent, SkillEffect.PickMeUp)).Select(player => player.Id),
                .. awayTeam.Players.Where(player => PlayerHasHookedEffect(ruleset, player, GameEventKind.DriveEnd, GameEventStage.AfterEvent, SkillEffect.PickMeUp)).Select(player => player.Id)
            ],
            Log =
            [
                new MatchLogEntry { Message = $"Created hotseat match: {homeTeam.Name} vs {awayTeam.Name}. Defense sets up first." }
            ]
        };

        return ApplyMasterChef(initialMatch);
    }

    public MatchState UseWeatherMage(MatchState match, LeagueTeam team)
    {
        EnsureInducementCanBeUsed(match, team, "Weather Mage");
        if (TeamWeatherMagesRemaining(match, team.Id) <= 0)
        {
            throw new InvalidOperationException($"{team.Name} has no Weather Mage remaining.");
        }

        var roll = Roll2D6();
        var weather = ResolveWeather(roll);
        return SpendWeatherMage(match, team.Id) with
        {
            Weather = weather,
            Log = [.. match.Log, new MatchLogEntry { Message = $"{team.Name} uses a Weather Mage: weather roll {roll}, now {FormatWeather(weather)}." }]
        };
    }

    public MatchState UseSpecialPlay(MatchState match, LeagueTeam team)
    {
        EnsureInducementCanBeUsed(match, team, "Special Play");
        if (TeamSpecialPlaysRemaining(match, team.Id) <= 0)
        {
            throw new InvalidOperationException($"{team.Name} has no Special Play remaining.");
        }

        var roll = _dice.RollD6();
        var spent = SpendSpecialPlay(match, team.Id);
        var result = roll switch
        {
            <= 2 => AddTeamReroll(spent, team.Id),
            3 => AddTeamBribe(spent, team.Id),
            4 => AddTeamApothecary(spent, team.Id),
            5 => AddTeamCheerleader(spent, team.Id),
            _ => AddTeamAssistantCoach(spent, team.Id)
        };
        var benefit = roll switch
        {
            <= 2 => "one team reroll",
            3 => "one bribe",
            4 => "one apothecary",
            5 => "one cheerleader",
            _ => "one assistant coach"
        };

        return result with
        {
            Log = [.. match.Log, new MatchLogEntry { Message = $"{team.Name} reveals a Special Play: rolled {roll}, gaining {benefit} for this match." }]
        };
    }
    public MatchState AdvancePhase(MatchState match, Ruleset? ruleset = null)
    {
        EnsureNoPendingChoices(match);

        var nextPhase = match.Phase;
        var nextActiveTeam = match.ActiveTeamId;
        var message = "";

        switch (match.Phase)
        {
            case MatchPhase.DefenseSetup:
                if (ruleset is not null)
                {
                    ValidateSetupComplete(match, ruleset, match.ActiveTeamId);
                }
                nextPhase = MatchPhase.OffenseSetup;
                nextActiveTeam = GetOpponentTeamId(match, match.ActiveTeamId);
                message = "Defense setup complete. Offense sets up next.";
                break;
            case MatchPhase.OffenseSetup:
                if (ruleset is not null)
                {
                    ValidateSetupComplete(match, ruleset, match.ActiveTeamId);
                }
                nextPhase = MatchPhase.Kickoff;
                message = "Offense setup complete. Ready for kickoff.";
                break;
            case MatchPhase.Kickoff:
                return match;
            case MatchPhase.OffensivePlayerTurn:
                return EndActivePlayerTurn(match, ruleset: null, "Offensive player turn complete. Defensive turn begins.");
            case MatchPhase.EndOfHalf:
                return StartSecondHalfSetup(match);
            case MatchPhase.DefensiveTurn:
            case MatchPhase.Complete:
                return match;
        }

        return match with
        {
            Phase = nextPhase,
            ActiveTeamId = nextActiveTeam,
            Activations = [],
            PendingPush = null,
            PendingBlockReroll = null,
            PendingReroll = null,
            PendingApothecary = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBallPlacement = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    public MatchState AdvanceTurn(MatchState match, Ruleset ruleset)
    {
        EnsureNoPendingChoices(match);

        if (match.Phase is MatchPhase.Complete)
        {
            return match;
        }

        if (match.Phase is MatchPhase.OffensivePlayerTurn)
        {
            return EndActivePlayerTurn(match, ruleset, "Offensive player turn complete. Defensive turn begins.");
        }

        if (match.Phase is not MatchPhase.DefensiveTurn)
        {
            return AdvancePhase(match, ruleset);
        }

        var recoveredMatch = RecoverStunnedPlayers(match, match.ActiveTeamId);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, recoveredMatch.ActiveTeamId);
        if (BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            return AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var nextActiveTeam = GetOpponentTeamId(consumedTurnMatch, consumedTurnMatch.ActiveTeamId);
        var nextMatch = consumedTurnMatch with
        {
            Phase = MatchPhase.OffensivePlayerTurn,
            ActiveTeamId = nextActiveTeam,
            Turn = GetTeamTurn(consumedTurnMatch, nextActiveTeam),
            Activations = [],
            PendingPush = null,
            PendingBlockReroll = null,
            PendingReroll = null,
            PendingApothecary = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBallPlacement = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Advanced to half {consumedTurnMatch.Half}, {FormatTeamTurn(consumedTurnMatch, nextActiveTeam)}, phase {MatchPhase.OffensivePlayerTurn}." }
            ]
        };

        return nextMatch;
    }

    private static void EnsureNoPendingChoices(MatchState match)
    {
        if (match.PendingReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending reroll before advancing the turn.");
        }

        if (match.PendingBlockReroll is not null)
        {
            throw new InvalidOperationException("Resolve the pending block reroll before advancing the turn.");
        }

        if (match.PendingApothecary is not null)
        {
            throw new InvalidOperationException("Resolve the pending apothecary choice before advancing the turn.");
        }

        if (match.PendingStandFirm is not null)
        {
            throw new InvalidOperationException("Resolve the pending Stand Firm choice before advancing the turn.");
        }

        if (match.PendingDivingTackle is not null)
        {
            throw new InvalidOperationException("Resolve the pending Diving Tackle choice before advancing the turn.");
        }

        if (match.PendingFollowUp is not null)
        {
            throw new InvalidOperationException("Resolve the pending follow-up choice before advancing the turn.");
        }

        if (match.PendingBallPlacement is not null)
        {
            throw new InvalidOperationException("Resolve the pending ball placement before advancing the turn.");
        }

        if (match.PendingBombThrow is not null)
        {
            throw new InvalidOperationException("Resolve the pending bomb throw before advancing the turn.");
        }

        if (match.PendingMultipleBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending Multiple Block continuation before advancing the turn.");
        }

        if (match.PendingSendOff is not null)
        {
            throw new InvalidOperationException("Resolve the pending send-off choice before advancing the turn.");
        }

        if (match.PendingBlock is not null)
        {
            throw new InvalidOperationException("Resolve the pending block choice before advancing the turn.");
        }

        if (match.PendingPush is not null)
        {
            throw new InvalidOperationException("Resolve the pending push before advancing the turn.");
        }

        if (match.PendingInterception is not null)
        {
            throw new InvalidOperationException("Resolve the pending interception before advancing the turn.");
        }

        if (match.PendingKickoffEvent is not null)
        {
            throw new InvalidOperationException("Resolve the pending kickoff event before advancing the turn.");
        }
    }
    public MatchState PlacePlayer(MatchState match, Ruleset ruleset, Guid playerId, PitchSquare square)
    {
        if (match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
        {
            throw new InvalidOperationException("Players can only be placed during setup.");
        }

        if (square.X < 0 || square.X >= ruleset.PitchWidth || square.Y < 0 || square.Y >= ruleset.PitchHeight)
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is outside the pitch.");
        }

        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active setup team can place players.");
        }

        if (placement.State is not (PlayerPitchState.Reserve or PlayerPitchState.Standing))
        {
            throw new InvalidOperationException("Only available players can be placed.");
        }

        if (placement.Square is null && CountTeamPlayersOnPitch(match, placement.TeamId) >= ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"A team can set up no more than {ruleset.PlayersPerSide} players.");
        }

        if (!IsLegalSetupSide(match, ruleset, placement.TeamId, square))
        {
            throw new InvalidOperationException("Player must be placed on their team's side of the pitch.");
        }

        if (IsWideZone(ruleset, square) && CountTeamPlayersInWideZone(match, ruleset, placement.TeamId, square, playerId) >= 2)
        {
            throw new InvalidOperationException("A team can place no more than two players in the same wide zone.");
        }

        if (match.Placements.Any(current => current.PlayerId != playerId && current.Square == square))
        {
            throw new InvalidOperationException($"Square {square.X},{square.Y} is already occupied.");
        }

        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId
                    ? current with { Square = square, State = PlayerPitchState.Standing, TackleZonesLost = false, Rooted = false, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : current)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"Placed {PlayerName(playerId)} at {square.X},{square.Y}." }
            ]
        };
    }

    public MatchState ReturnSetupPlayerToReserve(MatchState match, Guid playerId)
    {
        if (match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
        {
            throw new InvalidOperationException("Players can only be returned to reserve during setup.");
        }

        var placement = match.Placements.FirstOrDefault(current => current.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not part of this match.");

        if (placement.TeamId != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active setup team can return players to reserve.");
        }

        if (placement.State is not PlayerPitchState.Standing || placement.Square is null)
        {
            throw new InvalidOperationException("Only set up standing players can be returned to reserve.");
        }

        return match with
        {
            Placements = match.Placements
                .Select(current => current.PlayerId == playerId
                    ? current with { Square = null, State = PlayerPitchState.Reserve, TackleZonesLost = false, Rooted = false, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : current)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"Returned {PlayerName(playerId)} to reserve." }
            ]
        };
    }
    public MatchState DeclarePlayerAction(MatchState match, LeagueTeam team, Guid playerId, PlayerTurnAction action)
    {
        var player = FindTeamPlayer(team, playerId);
        EnsureCanDeclarePlayerAction(match, team, player, action, allowMatchingDeclaration: false);

        return AddActivation(match, playerId, team.Id, action, goForItsUsed: 0, declaredOnly: true) with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} declares {FormatPlayerTurnAction(action)}." }
            ]
        };
    }
    private ActionStart BeginPlayerAction(MatchState match, Ruleset ruleset, LeagueTeam team, Player player, PlayerTurnAction action, int goForItsUsed, int movementSquaresUsed = 0)
    {
        var existingActivation = GetActivation(match, player.Id, team.Id);
        EnsureCanDeclarePlayerAction(match, team, player, action, allowMatchingDeclaration: true);
        var traitCheck = ResolveTraitActionCheck(match, ruleset, team, player, action, existingActivation);
        if (traitCheck.Prevented)
        {
            return traitCheck;
        }

        match = traitCheck.Match;

        if (existingActivation is { DeclaredOnly: true } && existingActivation.Action == action)
        {
            return new ActionStart(match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == player.Id &&
                        activation.TeamId == team.Id &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { GoForItsUsed = goForItsUsed, MovementSquaresUsed = movementSquaresUsed, DeclaredOnly = false }
                            : activation)
                    .ToArray()
            }, Prevented: false);
        }

        if (existingActivation is { Action: PlayerTurnAction.Foul, MayMoveAfterFoul: true } && action == PlayerTurnAction.Move)
        {
            return new ActionStart(match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == player.Id &&
                        activation.TeamId == team.Id &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { GoForItsUsed = goForItsUsed, MovementSquaresUsed = movementSquaresUsed, MayMoveAfterFoul = false }
                            : activation)
                    .ToArray()
            }, Prevented: false);
        }

        if (existingActivation is { DeclaredOnly: false } &&
            existingActivation.Action == action &&
            action is PlayerTurnAction.Move or PlayerTurnAction.Blitz or PlayerTurnAction.Pass or PlayerTurnAction.HandOff)
        {
            return new ActionStart(match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == player.Id &&
                        activation.TeamId == team.Id &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { GoForItsUsed = goForItsUsed, MovementSquaresUsed = movementSquaresUsed }
                            : activation)
                    .ToArray()
            }, Prevented: false);
        }

        return new ActionStart(AddActivation(match, player.Id, team.Id, action, goForItsUsed, declaredOnly: false, movementSquaresUsed: movementSquaresUsed), Prevented: false);
    }

    private static void EnsureCanDeclarePlayerAction(MatchState match, LeagueTeam team, Player player, PlayerTurnAction action, bool allowMatchingDeclaration)
    {
        if (match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException("Player actions can only be declared during a player turn.");
        }

        if (team.Id != match.ActiveTeamId)
        {
            throw new InvalidOperationException("Only the active team can declare a player action during its turn.");
        }

        EnsureNoPendingChoices(match);

        var existingActivation = GetActivation(match, player.Id, team.Id);
        var canUseSneakyGitMove = existingActivation is { Action: PlayerTurnAction.Foul, MayMoveAfterFoul: true } &&
            action == PlayerTurnAction.Move;
        var canContinueMovementActivation = allowMatchingDeclaration &&
            existingActivation is { DeclaredOnly: false } &&
            existingActivation.Action == action &&
            action is PlayerTurnAction.Move or PlayerTurnAction.Blitz or PlayerTurnAction.Pass or PlayerTurnAction.HandOff;
        var matchesExistingDeclaration = existingActivation is { DeclaredOnly: true } &&
            existingActivation.Action == action;
        if (existingActivation is not null && !canUseSneakyGitMove && !canContinueMovementActivation && (!allowMatchingDeclaration || !matchesExistingDeclaration))
        {
            throw new InvalidOperationException($"{player.Name} has already been activated this turn.");
        }

        if (!matchesExistingDeclaration && !canUseSneakyGitMove && !canContinueMovementActivation && HasUsedTeamAction(match, team.Id, action))
        {
            throw new InvalidOperationException($"{team.Name} has already used its {FormatPlayerTurnAction(action).ToLowerInvariant()} this turn.");
        }
    }

    private ActionStart ResolveTraitActionCheck(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        PlayerTurnActivation? existingActivation)
    {
        if (existingActivation is { DeclaredOnly: false })
        {
            return new ActionStart(match, Prevented: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "bone-head"))
        {
            return ResolveReliabilityTrait(match, team, player, action, "Bone-head", target: 2, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "really-stupid"))
        {
            var hasHelper = HasAdjacentStandingTeammate(match, team.Id, player.Id);
            return ResolveReliabilityTrait(match, team, player, action, "Really Stupid", target: hasHelper ? 2 : 4, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "take-root") &&
            FindPlacement(match, player.Id)?.Rooted != true)
        {
            var roll = _dice.RollD6();
            if (roll == 1)
            {
                var rootMessage = $"{player.Name} checks Take Root: rolled 1, is rooted, and cannot move.";
                if (action is not (PlayerTurnAction.Move or PlayerTurnAction.Blitz))
                {
                    return new ActionStart(MarkPlayerTemporaryState(
                        match,
                        player.Id,
                        tackleZonesLost: false,
                        rooted: true,
                        rootMessage), Prevented: false);
                }

                var rootedMatch = MarkPlayerTemporaryState(
                    AddOrResolveActivation(match, player.Id, team.Id, action),
                    player.Id,
                    tackleZonesLost: false,
                    rooted: true,
                    rootMessage);

                return new ActionStart(rootedMatch, Prevented: true);
            }

            return new ActionStart(match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == player.Id
                        ? placement with { TackleZonesLost = false }
                        : placement)
                    .ToArray(),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} checks Take Root: rolled {roll}, action continues." }
                ]
            }, Prevented: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "animal-savagery"))
        {
            var target = action is PlayerTurnAction.Block or PlayerTurnAction.Blitz ? 2 : 4;
            return ResolveAggressionTrait(match, ruleset, team, player, action, "Animal Savagery", target, loseTackleZonesOnFailure: true);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "unchannelled-fury"))
        {
            var target = action is PlayerTurnAction.Block or PlayerTurnAction.Blitz ? 2 : 4;
            return ResolveReliabilityTrait(match, team, player, action, "Unchannelled Fury", target, loseTackleZonesOnFailure: false);
        }

        if (PlayerHasHookedSkillId(ruleset, player, GameEventKind.ActionStart, GameEventStage.BeforeEvent, "bloodlust"))
        {
            return ResolveAggressionTrait(match, ruleset, team, player, action, "Bloodlust", target: 2, loseTackleZonesOnFailure: false);
        }

        return new ActionStart(match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = false }
                    : placement)
                .ToArray()
        }, Prevented: false);
    }

    private ActionStart ResolveReliabilityTrait(
        MatchState match,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        string traitName,
        int target,
        bool loseTackleZonesOnFailure)
    {
        var roll = _dice.RollD6();
        if (roll < target)
        {
            var failedMatch = MarkPlayerTemporaryState(
                AddOrResolveActivation(match, player.Id, team.Id, action),
                player.Id,
                tackleZonesLost: loseTackleZonesOnFailure,
                rooted: null,
                $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action is wasted.");
            return new ActionStart(failedMatch, Prevented: true);
        }

        return new ActionStart(match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = false }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action continues." }
            ]
        }, Prevented: false);
    }

    private ActionStart ResolveAggressionTrait(
        MatchState match,
        Ruleset ruleset,
        LeagueTeam team,
        Player player,
        PlayerTurnAction action,
        string traitName,
        int target,
        bool loseTackleZonesOnFailure)
    {
        var roll = _dice.RollD6();
        if (roll >= target)
        {
            return new ActionStart(match with
            {
                Placements = match.Placements
                    .Select(placement => placement.PlayerId == player.Id
                        ? placement with { TackleZonesLost = false }
                        : placement)
                    .ToArray(),
                Log =
                [
                    .. match.Log,
                    new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, action continues." }
                ]
            }, Prevented: false);
        }

        var activationMatch = AddOrResolveActivation(match, player.Id, team.Id, action);
        var biterPlacement = match.Placements.First(placement => placement.PlayerId == player.Id);
        var biteTargetPlacement = PlacementAdjacentSquares(biterPlacement)
            .Select(square => match.Placements.FirstOrDefault(placement =>
                placement.TeamId == team.Id &&
                placement.PlayerId != player.Id &&
                PlacementOccupiesSquare(placement, square) &&
                placement.State == PlayerPitchState.Standing))
            .FirstOrDefault(placement => placement is not null);

        if (biteTargetPlacement is null)
        {
            var failedMatch = MarkPlayerTemporaryState(
                activationMatch,
                player.Id,
                tackleZonesLost: loseTackleZonesOnFailure,
                rooted: null,
                $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, no adjacent teammate to bite; action is wasted.");
            return new ActionStart(failedMatch, Prevented: true);
        }

        var biteTarget = FindTeamPlayer(team, biteTargetPlacement.PlayerId);
        var bittenMatch = ResolveSpecialArmorAttack(
            activationMatch,
            ruleset,
            team,
            biteTarget,
            biteTargetPlacement,
            armorModifier: 0,
            $"{player.Name} bites {biteTarget.Name} after failing {traitName}");

        return new ActionStart(bittenMatch with
        {
            Placements = bittenMatch.Placements
                .Select(placement => placement.PlayerId == player.Id
                    ? placement with { TackleZonesLost = loseTackleZonesOnFailure }
                    : placement)
                .ToArray(),
            Log =
            [
                .. bittenMatch.Log,
                new MatchLogEntry { Message = $"{player.Name} checks {traitName}: rolled {roll} vs {target}+, bites a teammate and continues." }
            ]
        }, Prevented: bittenMatch.Phase != match.Phase || bittenMatch.ActiveTeamId != match.ActiveTeamId);
    }

    private MatchState AddOrResolveActivation(MatchState match, Guid playerId, Guid teamId, PlayerTurnAction action)
    {
        var existingActivation = GetActivation(match, playerId, teamId);
        if (existingActivation is { DeclaredOnly: true } && existingActivation.Action == action)
        {
            return match with
            {
                Activations = match.Activations
                    .Select(activation =>
                        activation.PlayerId == playerId &&
                        activation.TeamId == teamId &&
                        activation.Half == match.Half &&
                        activation.Turn == match.Turn
                            ? activation with { DeclaredOnly = false }
                            : activation)
                    .ToArray()
            };
        }

        return AddActivation(match, playerId, teamId, action, goForItsUsed: 0, declaredOnly: false);
    }

    private static MatchState MarkPlayerTemporaryState(MatchState match, Guid playerId, bool tackleZonesLost, bool? rooted, string message)
    {
        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.PlayerId == playerId
                    ? placement with
                    {
                        TackleZonesLost = tackleZonesLost,
                        Rooted = rooted ?? placement.Rooted
                    }
                    : placement)
                .ToArray(),
            Log = [.. match.Log, new MatchLogEntry { Message = message }]
        };
    }

    private static bool HasUsedTeamAction(MatchState match, Guid teamId, PlayerTurnAction action)
    {
        return (action is PlayerTurnAction.Blitz or PlayerTurnAction.HandOff or PlayerTurnAction.Pass or PlayerTurnAction.Foul) &&
            match.Activations.Any(activation =>
                activation.TeamId == teamId &&
                activation.Half == match.Half &&
                activation.Turn == match.Turn &&
                activation.Action == action);
    }

    private static string FormatPlayerTurnAction(PlayerTurnAction action)
    {
        return action switch
        {
            PlayerTurnAction.HandOff => "Hand-off",
            _ => action.ToString()
        };
    }

    private MatchState AddActivation(MatchState match, Guid playerId, Guid teamId, PlayerTurnAction action, int goForItsUsed, bool declaredOnly, int movementSquaresUsed = 0)
    {
        return match with
        {
            Activations =
            [
                .. match.Activations,
                new PlayerTurnActivation
                {
                    PlayerId = playerId,
                    TeamId = teamId,
                    Half = match.Half,
                    Turn = match.Turn,
                    GoForItsUsed = goForItsUsed,
                    MovementSquaresUsed = movementSquaresUsed,
                    Action = action,
                    DeclaredOnly = declaredOnly
                }
            ]
        };
    }

    private static int GetBlocksMade(MatchState match, Guid playerId, Guid teamId)
    {
        return GetActivation(match, playerId, teamId)?.BlocksMade ?? 0;
    }

    private static MatchState IncrementActivationBlocksMade(MatchState match, Guid playerId, Guid teamId)
    {
        return match with
        {
            Activations = match.Activations
                .Select(activation =>
                    activation.PlayerId == playerId &&
                    activation.TeamId == teamId &&
                    activation.Half == match.Half &&
                    activation.Turn == match.Turn
                        ? activation with { BlocksMade = activation.BlocksMade + 1 }
                        : activation)
                .ToArray()
        };
    }

    private static MatchState CompleteActivation(MatchState match, Guid playerId, Guid teamId)
    {
        return match with
        {
            Activations = match.Activations
                .Select(activation =>
                    activation.PlayerId == playerId &&
                    activation.TeamId == teamId &&
                    activation.Half == match.Half &&
                    activation.Turn == match.Turn
                        ? activation with { Completed = true }
                        : activation)
                .ToArray()
        };
    }

    /// <summary>
    /// Ends the attacker's activation after a block resolves, unless the attacker is still
    /// standing as part of a Blitz (a blitzing player may keep moving after the block).
    /// </summary>
    private static MatchState CompleteBlockActivationIfDone(MatchState match, Guid attackerPlayerId, Guid attackerTeamId)
    {
        var activation = GetActivation(match, attackerPlayerId, attackerTeamId);
        if (activation is null || activation.Completed)
        {
            return match;
        }

        var placement = FindPlacement(match, attackerPlayerId);
        var stillStanding = placement is { State: PlayerPitchState.Standing, Square: not null };
        if (activation.Action == PlayerTurnAction.Blitz && stillStanding)
        {
            return match;
        }

        return CompleteActivation(match, attackerPlayerId, attackerTeamId);
    }

    private MatchState ApplyTurnover(MatchState match, Ruleset ruleset, Guid turnoverTeamId)
    {
        var nextMatch = match.Phase switch
        {
            MatchPhase.OffensivePlayerTurn => EndActivePlayerTurn(match, ruleset, null),
            MatchPhase.DefensiveTurn => AdvanceTurn(match, ruleset),
            _ => match
        };

        return nextMatch with
        {
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
            Log =
            [
                .. nextMatch.Log,
                new MatchLogEntry { Message = "Turnover." }
            ]
        };
    }
    private MatchState EndActivePlayerTurn(MatchState match, Ruleset? ruleset, string? message)
    {
        var recoveredMatch = RecoverStunnedPlayers(match, match.ActiveTeamId);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, recoveredMatch.ActiveTeamId);
        if (ruleset is not null && BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            return AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var nextActiveTeam = GetOpponentTeamId(consumedTurnMatch, consumedTurnMatch.ActiveTeamId);
        return consumedTurnMatch with
        {
            Phase = MatchPhase.DefensiveTurn,
            ActiveTeamId = nextActiveTeam,
            Turn = GetTeamTurn(consumedTurnMatch, nextActiveTeam),
            Activations = [],
            PendingReroll = null,
            PendingBlockReroll = null,
            PendingPush = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            Log = message is null
                ? consumedTurnMatch.Log
                : [.. consumedTurnMatch.Log, new MatchLogEntry { Message = message }]
        };
    }
    private MatchState AdvanceHalf(MatchState match, Ruleset ruleset)
    {
        if (match.Half == 1)
        {
            return BeginDriveEnd(match, ruleset, new PendingDriveEndContinuation
            {
                NextDefenseTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId,
                StartSecondHalf = true
            });
        }

        return BeginDriveEnd(match, ruleset, new PendingDriveEndContinuation
        {
            NextDefenseTeamId = match.ActiveTeamId,
            CompleteMatch = true
        });
    }

    private MatchState StartSecondHalfSetup(MatchState match)
    {
        return BeginDriveEnd(match, new Ruleset
        {
            Id = match.RulesetId,
            Name = match.RulesetId,
            Version = "",
            Dice = new DiceRules()
        }, new PendingDriveEndContinuation
        {
            NextDefenseTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId,
            StartSecondHalf = true
        });
    }

    private MatchState StartSecondHalfSetupAfterDriveEnd(MatchState match)
    {
        var kickingTeamId = match.FirstHalfReceivingTeamId ?? match.HomeTeamId;
        var recoveredMatch = ResolveKnockoutRecoveries(match);
        var resetPlacements = ResetAvailablePlayersToReserve(recoveredMatch);

        var secondHalfMatch = recoveredMatch with
        {
            Half = 2,
            Drive = recoveredMatch.Drive + 1,
            DriveState = DriveState.Setup,
            Turn = 1,
            HomeTurn = 1,
            AwayTurn = 1,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = kickingTeamId,
            Ball = new BallState(),
            HomeRerollsRemaining = recoveredMatch.HomeTeamRerolls,
            AwayRerollsRemaining = recoveredMatch.AwayTeamRerolls,
            HomeLeaderRerollAvailable = recoveredMatch.HomeLeaderRerollAvailable,
            AwayLeaderRerollAvailable = recoveredMatch.AwayLeaderRerollAvailable,
            TeamRerollUses = recoveredMatch.TeamRerollUses
                .Where(use => use.Half != 2)
                .ToArray(),
            Placements = resetPlacements,
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
            Log = [.. recoveredMatch.Log, new MatchLogEntry { Message = "Second half begins. First-half receiving team kicks off." }]
        };

        return ApplyMasterChef(secondHalfMatch);
    }
    private static MatchState IncrementTeamTurn(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeTurn = match.HomeTurn + 1 }
            : match with { AwayTurn = match.AwayTurn + 1 };
    }

    private static bool BothTeamsFinishedHalf(MatchState match, Ruleset ruleset)
    {
        return match.HomeTurn > ruleset.TurnsPerHalf && match.AwayTurn > ruleset.TurnsPerHalf;
    }

    private static int GetTeamTurn(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeTurn : match.AwayTurn;
    }

    private static string FormatTeamTurn(MatchState match, Guid teamId)
    {
        var side = teamId == match.HomeTeamId ? "home" : "away";
        return $"{side} turn {GetTeamTurn(match, teamId)}";
    }
    private MatchState ScoreTouchdown(MatchState match, Ruleset ruleset, LeagueTeam scoringTeam)
    {
        var scorerId = match.Ball.CarrierPlayerId;
        var isHomeScore = scoringTeam.Id == match.HomeTeamId;
        var nextHomeScore = match.HomeScore + (isHomeScore ? 1 : 0);
        var nextAwayScore = match.AwayScore + (isHomeScore ? 0 : 1);
        var recoveredMatch = RecoverStunnedPlayers(match, scoringTeam.Id);
        var consumedTurnMatch = IncrementTeamTurn(recoveredMatch, scoringTeam.Id);
        if (BothTeamsFinishedHalf(consumedTurnMatch, ruleset))
        {
            consumedTurnMatch = AdvanceHalf(consumedTurnMatch, ruleset);
        }

        var scorer = scorerId.HasValue ? FindTeamPlayer(scoringTeam, scorerId.Value) : null;
        if (consumedTurnMatch.Phase is MatchPhase.DefenseSetup or MatchPhase.Complete)
        {
            return consumedTurnMatch with
            {
                HomeScore = nextHomeScore,
                AwayScore = nextAwayScore,
                PlayerAwards = scorerId.HasValue
                    ? AddPlayerAward(consumedTurnMatch, scoringTeam.Id, scorerId.Value, MatchPlayerAwardKind.Touchdown, 3, teamName: scoringTeam.Name, playerName: scorer?.Name)
                    : consumedTurnMatch.PlayerAwards,
                Log =
                [
                    .. consumedTurnMatch.Log,
                    new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." }
                ]
            };
        }

        return BeginDriveEnd(consumedTurnMatch with
        {
            HomeScore = nextHomeScore,
            AwayScore = nextAwayScore,
            PlayerAwards = scorerId.HasValue
                ? AddPlayerAward(consumedTurnMatch, scoringTeam.Id, scorerId.Value, MatchPlayerAwardKind.Touchdown, 3, teamName: scoringTeam.Name, playerName: scorer?.Name)
                : consumedTurnMatch.PlayerAwards,
            Log =
            [
                .. consumedTurnMatch.Log,
                new MatchLogEntry { Message = $"Touchdown for {scoringTeam.Name}. Score {nextHomeScore}-{nextAwayScore}." }
            ]
        }, ruleset, new PendingDriveEndContinuation { NextDefenseTeamId = scoringTeam.Id });
    }

    private MatchState StartNextDriveSetup(MatchState match, Guid nextDefenseTeamId)
    {
        var knockoutRecoveredMatch = ResolveKnockoutRecoveries(match);
        var resetPlacements = ResetAvailablePlayersToReserve(knockoutRecoveredMatch);

        return knockoutRecoveredMatch with
        {
            Drive = knockoutRecoveredMatch.Drive + 1,
            DriveState = DriveState.Setup,
            Phase = MatchPhase.DefenseSetup,
            ActiveTeamId = nextDefenseTeamId,
            Turn = GetTeamTurn(knockoutRecoveredMatch, nextDefenseTeamId),
            Ball = new BallState(),
            Placements = resetPlacements,
            Activations = [],
            PendingPush = null,
            PendingReroll = null,
            PendingStandFirm = null,
            PendingDivingTackle = null,
            PendingFollowUp = null,
            PendingBombThrow = null,
            PendingMultipleBlock = null,
            PendingSendOff = null,
            PendingKickoffEvent = null,
            Log =
            [
                .. knockoutRecoveredMatch.Log,
                new MatchLogEntry { Message = "New drive begins with defense placement." }
            ]
        };
    }

    private static PlayerPlacement[] ResetAvailablePlayersToReserve(MatchState match)
    {
        return match.Placements
            .Select(placement => placement.State is PlayerPitchState.Casualty or PlayerPitchState.Dead or PlayerPitchState.SentOff or PlayerPitchState.KnockedOut
                ? placement
                : placement with { Square = null, State = PlayerPitchState.Reserve, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null })
            .ToArray();
    }

    private static void ValidateSetupComplete(MatchState match, Ruleset ruleset, Guid teamId)
    {
        var availableCount = match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.State is PlayerPitchState.Reserve or PlayerPitchState.Standing);
        var requiredPlayers = Math.Min(ruleset.PlayersPerSide, availableCount);
        var placed = match.Placements
            .Where(placement =>
                placement.TeamId == teamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is not null)
            .ToArray();

        if (placed.Length != requiredPlayers)
        {
            throw new InvalidOperationException($"{requiredPlayers} available players must be set up before kickoff.");
        }

        if (placed.Length > ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"No more than {ruleset.PlayersPerSide} players may be set up.");
        }

        if (placed.Any(placement => placement.Square is PitchSquare square && !IsLegalSetupSide(match, ruleset, teamId, square)))
        {
            throw new InvalidOperationException("All players must be set up on their team's side of the pitch.");
        }

        var linePlayers = placed.Count(placement =>
            placement.Square is PitchSquare square &&
            IsLineOfScrimmage(ruleset, teamId, match.HomeTeamId, square) &&
            !IsWideZone(ruleset, square));
        if (linePlayers < 3)
        {
            throw new InvalidOperationException("At least three players must be set up on the line of scrimmage outside the wide zones.");
        }

        var wideZoneViolation = placed
            .Where(placement => placement.Square is PitchSquare square && IsWideZone(ruleset, square))
            .GroupBy(placement => placement.Square!.Y < 4 ? "top" : "bottom")
            .Any(group => group.Count() > 2);
        if (wideZoneViolation)
        {
            throw new InvalidOperationException("A team can set up no more than two players in each wide zone.");
        }
    }
    private MatchState ResolveKnockoutRecoveries(MatchState match)
    {
        var placements = new List<PlayerPlacement>(match.Placements.Count);
        var log = new List<MatchLogEntry>();

        foreach (var placement in match.Placements)
        {
            if (placement.State != PlayerPitchState.KnockedOut)
            {
                placements.Add(placement);
                continue;
            }

            var roll = _dice.RollD6();
            var recoveryBonus = TeamBloodweiserKegs(match, placement.TeamId) + (HasActivePickMeUp(match, placement.TeamId) ? 1 : 0);
            var target = Math.Max(2, 4 - recoveryBonus);
            if (roll >= target)
            {
                placements.Add(placement with
                {
                    Square = null,
                    State = PlayerPitchState.Reserve,
                    StunnedRecoveryHalf = null,
                    StunnedRecoveryTurn = null,
                    Casualty = null
                });
                log.Add(new MatchLogEntry { Message = $"Knockout recovery {PlayerName(placement.PlayerId)}: rolled {roll} vs {target}+, recovered." });
                continue;
            }

            placements.Add(placement with { Square = null });
            log.Add(new MatchLogEntry { Message = $"Knockout recovery {PlayerName(placement.PlayerId)}: rolled {roll} vs {target}+, remains knocked out." });
        }

        return log.Count == 0
            ? match
            : match with
            {
                Placements = placements,
                Log = [.. match.Log, .. log]
            };
    }

    private static bool HasActivePickMeUp(MatchState match, Guid teamId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId == teamId &&
            match.PickMeUpPlayerIds.Contains(placement.PlayerId) &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve);
    }

    private MatchState ApplyMasterChef(MatchState match)
    {
        var nextMatch = match;
        if (match.HomeHasMasterChef)
        {
            nextMatch = ResolveMasterChef(nextMatch, match.HomeTeamId);
        }

        if (match.AwayHasMasterChef)
        {
            nextMatch = ResolveMasterChef(nextMatch, match.AwayTeamId);
        }

        return nextMatch;
    }

    private MatchState ResolveMasterChef(MatchState match, Guid teamId)
    {
        var successes = Enumerable.Range(0, 3).Count(_ => _dice.RollD6() >= 4);
        var opponentId = GetOpponentTeamId(match, teamId);
        var availableToSteal = TeamRerollsRemaining(match, opponentId);
        var stolen = Math.Min(successes, availableToSteal);
        var nextMatch = SetTeamRerollsRemaining(match, teamId, TeamRerollsRemaining(match, teamId) + stolen);
        nextMatch = SetTeamRerollsRemaining(nextMatch, opponentId, availableToSteal - stolen);
        return nextMatch with
        {
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{(teamId == match.HomeTeamId ? "Home" : "Away")} Master Chef rolls {successes} success{(successes == 1 ? "" : "es")} and steals {stolen} reroll{(stolen == 1 ? "" : "s")} for half {match.Half}." }
            ]
        };
    }

    private static void EnsureInducementCanBeUsed(MatchState match, LeagueTeam team, string name)
    {
        EnsureNoPendingChoices(match);
        if (match.ActiveTeamId != team.Id || match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            throw new InvalidOperationException($"{name} can only be used at the start of that team's turn.");
        }

        if (match.Activations.Count > 0)
        {
            throw new InvalidOperationException($"{name} must be used before activating a player.");
        }
    }

    private static int TeamBloodweiserKegs(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeBloodweiserKegs : match.AwayBloodweiserKegs;
    }

    private static int TeamWeatherMagesRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeWeatherMagesRemaining : match.AwayWeatherMagesRemaining;
    }

    private static int TeamSpecialPlaysRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeSpecialPlaysRemaining : match.AwaySpecialPlaysRemaining;
    }

    private static MatchState SpendWeatherMage(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeWeatherMagesRemaining = Math.Max(0, match.HomeWeatherMagesRemaining - 1) }
            : match with { AwayWeatherMagesRemaining = Math.Max(0, match.AwayWeatherMagesRemaining - 1) };
    }

    private static MatchState SpendSpecialPlay(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeSpecialPlaysRemaining = Math.Max(0, match.HomeSpecialPlaysRemaining - 1) }
            : match with { AwaySpecialPlaysRemaining = Math.Max(0, match.AwaySpecialPlaysRemaining - 1) };
    }

    private static MatchState SetTeamRerollsRemaining(MatchState match, Guid teamId, int value)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeRerollsRemaining = Math.Max(0, value) }
            : match with { AwayRerollsRemaining = Math.Max(0, value) };
    }

    private static MatchState AddTeamReroll(MatchState match, Guid teamId)
    {
        return SetTeamRerollsRemaining(match, teamId, TeamRerollsRemaining(match, teamId) + 1);
    }

    private static MatchState AddTeamBribe(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeBribesRemaining = match.HomeBribesRemaining + 1 }
            : match with { AwayBribesRemaining = match.AwayBribesRemaining + 1 };
    }

    private static MatchState AddTeamApothecary(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeApothecariesRemaining = match.HomeApothecariesRemaining + 1 }
            : match with { AwayApothecariesRemaining = match.AwayApothecariesRemaining + 1 };
    }

    private static MatchState AddTeamCheerleader(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeCheerleaders = match.HomeCheerleaders + 1 }
            : match with { AwayCheerleaders = match.AwayCheerleaders + 1 };
    }

    private static MatchState AddTeamAssistantCoach(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeAssistantCoaches = match.HomeAssistantCoaches + 1 }
            : match with { AwayAssistantCoaches = match.AwayAssistantCoaches + 1 };
    }

    private static int SelectedOptionEffectCount(Ruleset ruleset, TeamInducementPlan plan, string inducementId, string effect)
    {
        var definition = ruleset.Inducements.First(inducement => string.Equals(inducement.Id, inducementId, StringComparison.OrdinalIgnoreCase));
        return plan.Inducements
            .Where(selected => string.Equals(selected.InducementId, inducementId, StringComparison.OrdinalIgnoreCase))
            .Where(selected => definition.Options.Any(option =>
                string.Equals(option.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(option.Effect, effect, StringComparison.OrdinalIgnoreCase)))
            .Sum(selected => selected.Count);
    }

    private static string SelectedOptionEffect(Ruleset ruleset, TeamInducementPlan plan, string inducementId)
    {
        var definition = ruleset.Inducements.First(inducement => string.Equals(inducement.Id, inducementId, StringComparison.OrdinalIgnoreCase));
        var selected = plan.Inducements.FirstOrDefault(current => string.Equals(current.InducementId, inducementId, StringComparison.OrdinalIgnoreCase));
        return selected is null
            ? ""
            : definition.Options.FirstOrDefault(option => string.Equals(option.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))?.Effect ?? "";
    }

    private static MatchState RecoverStunnedPlayers(MatchState match, Guid teamId)
    {
        var stunnedCount = match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.State == PlayerPitchState.Stunned &&
            placement.StunnedRecoveryHalf == match.Half &&
            placement.StunnedRecoveryTurn <= GetTeamTurn(match, teamId));

        if (stunnedCount == 0)
        {
            return match;
        }

        return match with
        {
            Placements = match.Placements
                .Select(placement => placement.TeamId == teamId && placement.State == PlayerPitchState.Stunned
                    && placement.StunnedRecoveryHalf == match.Half
                    && placement.StunnedRecoveryTurn <= GetTeamTurn(match, teamId)
                        ? placement with { State = PlayerPitchState.Prone, StunnedRecoveryHalf = null, StunnedRecoveryTurn = null, Casualty = null }
                    : placement)
                .ToArray(),
            Log =
            [
                .. match.Log,
                new MatchLogEntry { Message = $"{stunnedCount} stunned player{(stunnedCount == 1 ? "" : "s")} recovered to prone." }
            ]
        };
    }
    private static bool IsTouchdown(MatchState match, Ruleset ruleset, LeagueTeam team, Guid playerId, PitchSquare square)
    {
        if (match.Ball.CarrierPlayerId != playerId)
        {
            return false;
        }

        return team.Id == match.HomeTeamId
            ? square.X == ruleset.PitchWidth - 1
            : square.X == 0;
    }
}
