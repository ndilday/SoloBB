# SoloBB

SoloBB is a Godot/C# foundation for solo and hotseat fantasy football league play.

The current implementation is deliberately split into two layers:

- `src/SoloBB.Core`: engine-agnostic ruleset, roster, league, match, validation, and JSON persistence code.
- `src/SoloBB.Godot`: Godot UI shell scripts for loading data and creating a sample league.

## Run

Open this folder in Godot 4.x .NET and run `scenes/Main.tscn`.

The first screen loads the sample ruleset and roster set from `data/`, then can create a sample league under Godot's `user://leagues` save folder.

## Data Model

Rulesets live in `data/rulesets/*.json` and define pitch size, turn structure, treasury, dice assumptions, skill definitions, and advancement thresholds.

Roster sets live in `data/rosters/*.json` and target a ruleset by id. Each roster defines position limits, costs, stats, starting skills, and skill category access.

Leagues are saved as JSON through `JsonGameDataStore.SaveLeagueAsync`, so PBEM or online play can later reuse the same serialized state without depending on Godot scenes.

## Smoke Check

```powershell
dotnet run --project tests/SoloBB.Tests/SoloBB.Tests.csproj
```
