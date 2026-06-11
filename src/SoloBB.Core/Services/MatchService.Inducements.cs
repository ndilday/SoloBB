using SoloBB.Core.Domain;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    public MatchState UseWizard(MatchState match, Ruleset ruleset, LeagueTeam team, PitchSquare targetSquare)
    {
        EnsureInducementCanBeUsed(match, team, "Wizard");
        if (TeamWizardsRemaining(match, team.Id) <= 0)
        {
            throw new InvalidOperationException($"{team.Name} has no wizard spell remaining.");
        }

        if (!IsOnPitch(ruleset, targetSquare))
        {
            throw new InvalidOperationException("Wizard target must be on the pitch.");
        }

        var effect = TeamWizardEffect(match, team.Id);
        var spent = SpendWizard(match, team.Id);
        return effect switch
        {
            "wizard-fireball" => ResolveFireball(spent, ruleset, team, targetSquare),
            "wizard-lightning" => ResolveLightning(spent, ruleset, team, targetSquare),
            _ => throw new InvalidOperationException($"{team.Name} has no supported wizard effect.")
        };
    }

    private MatchState ResolveFireball(MatchState match, Ruleset ruleset, LeagueTeam team, PitchSquare targetSquare)
    {
        var victims = match.Placements
            .Where(placement => placement.Square is PitchSquare square &&
                Math.Abs(square.X - targetSquare.X) <= 1 &&
                Math.Abs(square.Y - targetSquare.Y) <= 1 &&
                placement.State == PlayerPitchState.Standing)
            .ToArray();
        var nextMatch = match;
        var knockedDown = 0;
        foreach (var victim in victims)
        {
            var roll = _dice.RollD6();
            if (roll < 4)
            {
                continue;
            }

            var victimTeam = RegisteredTeam(victim.TeamId)
                ?? throw new InvalidOperationException("Wizard targets require registered match teams.");
            var player = FindTeamPlayer(victimTeam, victim.PlayerId);
            nextMatch = KnockPlayerDown(nextMatch, ruleset, player, victim, ResolveFallInjury(player), victim.Square!);
            knockedDown++;
        }

        return nextMatch with
        {
            Log = [.. nextMatch.Log, new MatchLogEntry { Message = $"{team.Name}'s Fireball hits the area at {targetSquare.X},{targetSquare.Y} and knocks down {knockedDown} player{(knockedDown == 1 ? "" : "s")}." }]
        };
    }

    private MatchState ResolveLightning(MatchState match, Ruleset ruleset, LeagueTeam team, PitchSquare targetSquare)
    {
        var victim = match.Placements.FirstOrDefault(placement =>
            placement.TeamId != team.Id &&
            placement.Square == targetSquare &&
            placement.State == PlayerPitchState.Standing)
            ?? throw new InvalidOperationException("Lightning must target a standing opposing player.");
        var roll = _dice.RollD6();
        if (roll < 2)
        {
            return match with
            {
                Log = [.. match.Log, new MatchLogEntry { Message = $"{team.Name}'s Lightning targets {targetSquare.X},{targetSquare.Y}: rolled {roll}, missed." }]
            };
        }

        var victimTeam = RegisteredTeam(victim.TeamId)
            ?? throw new InvalidOperationException("Wizard targets require registered match teams.");
        var player = FindTeamPlayer(victimTeam, victim.PlayerId);
        var struck = KnockPlayerDown(match, ruleset, player, victim, ResolveFallInjury(player), targetSquare);
        return struck with
        {
            Log = [.. struck.Log, new MatchLogEntry { Message = $"{team.Name}'s Lightning targets {player.Name}: rolled {roll}, hit." }]
        };
    }

    private static int TeamWizardsRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeWizardsRemaining : match.AwayWizardsRemaining;
    }

    private static string TeamWizardEffect(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeWizardEffect : match.AwayWizardEffect;
    }

    private static MatchState SpendWizard(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId
            ? match with { HomeWizardsRemaining = Math.Max(0, match.HomeWizardsRemaining - 1) }
            : match with { AwayWizardsRemaining = Math.Max(0, match.AwayWizardsRemaining - 1) };
    }
}
