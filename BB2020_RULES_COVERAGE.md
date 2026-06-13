# BB2020 Rules Coverage

Coverage states:

- `data only`: represented in ruleset/roster data, but not used by engine behavior.
- `partially implemented`: behavior exists, but known timing, choice, campaign, or interaction gaps remain.
- `implemented`: intended behavior exists in engine code for the current `bb2020-lite` scope.
- `tested`: covered by the executable smoke harness in `tests/SoloBB.Tests`.

## Core Data and League

| Area | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Ruleset loading and validation | tested | Smoke section: ruleset catalog and data loading | Loads `bb2020-lite` skills, traits, dice assumptions, inducement data, advancement thresholds, and data-only coverage markers. |
| Roster loading and validation | tested | Smoke section: ruleset catalog and data loading | Current data includes an expanded 20+ roster catalog, roster special-rule metadata, roster restriction metadata, and starter star-player data. |
| Team drafting | tested | Smoke section: league creation, roster validation, and persistence | Covers roster limits, team value, treasury, rerolls, fan factor, staff, and apothecaries. Players receive jersey numbers (auto-assigned 1..N in draft order, reorderable via `MovePlayer`), which drive ordering on the match roster; match-only journeymen/inducement players are numbered after the roster. |
| League creation | tested | Smoke section: league creation, roster validation, and persistence | Supports even-team leagues. |
| Season scheduling | tested | Smoke section: league creation, roster validation, and persistence | Supports double round-robin schedules. |
| League standings display | tested | Smoke section: post-match campaign lifecycle | UI computes standings from stored results; completed scheduled matches now record results and advance the current week when a week is complete. |
| Inducements | partially implemented | Smoke section: match creation, setup, and persistence | Fixed-price effects are active for temporary staff, rerolls, bribes, apothecaries, Weather Mage, Bloodweiser Kegs, Special Plays, Riotous Rookies, and Master Chef. Picker-backed `bb2020-lite` options now cover roster-derived mercenaries, famous coaching staff, Fireball/Lightning wizards, and friendly/intimidating referees, including mixed option purchases and in-match wizard targeting. Star players are supported. Exact published named inducement catalogs and fuller tabletop timing remain pending; unsupported inducements are rejected instead of consuming treasury. |
| Journeymen | tested | Smoke section: match creation, setup, and persistence | Adds match-only Loner journeymen to reach the ruleset player count without changing league rosters. Pre-game Current Team Value excludes unavailable players at their base-plus-advancement value and includes automatic journeymen for petty cash and spending order. |
| Post-match sequence | tested | Smoke section: post-match campaign lifecycle | Records scheduled results, applies casualty roster updates, clears old Miss Next Game status, applies winnings/treasury spend, MVPs, SPP awards, and Dedicated Fans changes. The MVP is now randomly selected (BB2020) from eligible non-journeyman players via the dice roller. Winnings now follow BB2020: each team's game Fan Factor (Dedicated Fans + D3) is rolled at kickoff and stored on the match, the two are summed into Fan Attendance, and each coach receives `(Fan Attendance / 2 + touchdowns scored) * 10,000`. The Dedicated Fans characteristic is updated by the BB2020 dice rule (win: D6 ≥ current → +1 to a max of 6; loss: D6 < current → −1 to a min of 1; draw: no change). |
| End-of-season redraft | partially implemented | Smoke section: post-match campaign lifecycle | `LeagueService.CalculateRedraftBudget` implements the BB2020 "Raise Funds" step: 1,000,000 + Treasury + 20,000 per fixture played + 20,000 per win + 10,000 per draw, capped at 1,300,000 (excess lost), computed from the most recent season's results. `RedraftTeam` rebuilds a team within that budget — retained players are re-hired at their current value plus a 20,000 agent fee (preserving skills, injuries, characteristic reductions, and unspent SPP, while clearing transient match status), new players are drafted at position cost, rerolls/staff are re-purchased, and Dedicated Fans carry over; the leftover becomes the new Treasury. `StartNewSeason` appends a fresh double round-robin. Simplifications: the agent fee is a flat one-season 20,000 (per-player seasons-played is not tracked), and the Rest & Relaxation step (niggling-injury/hatred rolls) and a redraft UI remain pending. |
| FAME / Fan Factor | tested | Smoke section: kickoff, weather, and kickoff events | At kickoff each team rolls its game Fan Factor (Dedicated Fans + D3); FAME is derived per BB2020 (+1 for more fans than the opponent, +2 for at least twice as many, otherwise 0) and added to the Cheering Fans and Brilliant Coaching kickoff contests. The persistent characteristic is stored as `DedicatedFans` on `LeagueTeam`; legacy save files using the old `fanFactor` key are migrated on load by `JsonGameDataStore`. |
| Prayers to Nuffle | partially implemented | Smoke section: match creation, setup, and persistence | The underdog rolls on the BB2020 D16 Prayers to Nuffle table once per full 50,000 gp of Current Team Value difference, re-rolling duplicates. Player-affecting prayers are baked into the match-only teams and recorded on `MatchState.Prayers`: Stiletto (Stab), Knuckle Dusters (Mighty Blow), Blessed Statue of Nuffle (Pro), Intensive Training (a primary skill), Iron Man (AV +1), Greasy Cleats (opponent MA −1), and Bad Habits (D3 opponents gain Loner). The remaining prayers (Treacherous Trapdoor, Friends with the Ref, Moles under the Pitch, Perfect Passing, Fan Interaction, Necessary Violence, Fouling Frenzy, Throw a Rock, Under Scrutiny) are recorded and logged but their effects are not yet modelled. Simplifications: the roll count uses the pre-inducement CTV difference, "choose" selections are deterministic (lowest jersey number), and "this drive"/"this half" prayers currently last the whole match. |
| Player advancement | partially implemented | Smoke section: post-match campaign lifecycle | Skill and characteristic advancement purchases spend SPP and increase team value. Costs follow the BB2020 by-type model (random/chosen × primary/secondary: 3/6/6/12 SPP with +10k/+20k/+20k/+40k value) rather than the old BB2016 escalating ladder. Random skill selection rolls 2D6 and picks at random via the dice roller, including the doubles rule that expands selection to any of the player's primary/secondary categories; a doubles pick is priced by the table that was rolled (random primary/secondary) rather than by the chosen skill's category, so both SPP cost and player value stay correct. Characteristic improvements (18 SPP) roll a D16 on the BB2020 table to decide which characteristics may be raised, apply the change in the correct direction (MA/AV/ST up, AG/PA target down) up to each maximum, and add the matching value (AV +10k, MA/PA +20k, AG +40k, ST +80k). The BB2020 hard cap is enforced: at most six career advancements per player, and any single characteristic may be improved at most twice (tracked via `Player.CharacteristicImprovements`, which records the characteristic raised by each improvement). `LeagueService.PlayerTitle` derives the BB2020 player title from the advancement count (Rookie/Experienced/Veteran/Emerging Star/Star/Super Star/Legend). The advancement UI is still pending. |

## Match Flow

| Area | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Hotseat match creation | tested | Smoke section: match creation, setup, and persistence | Creates placements, active teams, rerolls, leader availability, staff, and apothecaries. |
| Match persistence | tested | Smoke section: match creation, setup, and persistence | Match state round-trips through JSON. |
| Setup formation rules | tested | Smoke section: match creation, setup, and persistence | Covers side-of-pitch, line-of-scrimmage, wide-zone, player-count, and unavailable-player guards. |
| Kickoff scatter and touchbacks | tested | Smoke section: kickoff, weather, and kickoff events | Includes Kick skill scatter reduction and a team/skill reroll on a dropped kickoff catch. |
| Kickoff table | partially implemented | Smoke section: kickoff, weather, and kickoff events | Tests Get the Ref, Time-out, Solid Defence, High Kick, Cheering Fans, Brilliant Coaching, Changing Weather, Quick Snap, Blitz, Officious Ref, and Pitch Invasion. Slot 11 is the BB2020 Officious Ref (coaches roll D6 + FAME; the lower coach's randomly chosen on-pitch player is sent off on a 1, otherwise placed prone and stunned) — the previous BB2016 "Throw a Rock" has been removed. Officious Ref send-offs currently apply directly without offering a bribe interrupt. Quick Snap now lets every open player move one square (BB2020) rather than D3+3; Solid Defence and Blitz keep the D3+3 limit. |
| Kickoff Blitz event | partially implemented | Smoke section: kickoff, weather, and kickoff events | Event state and blocks exist; full timing should be reviewed against tabletop rules. |
| Weather | tested | Smoke section: kickoff, weather, and kickoff events | Weather affects passing, catching, pickup, and rush targets where currently implemented. |
| Explicit action declaration | tested | Smoke sections: movement, hand-offs/passing, and blocking | `DeclarePlayerAction` records declaration-only activations, reserves once-per-turn actions, and blocks new declarations while pending choices exist. |
| Turn and half advancement | tested | Smoke section: turn advancement, halftime, and full time | Covers offensive/defensive turn advancement, halftime, full time, reroll refresh, and KO recovery. |
| Drive lifecycle | tested | Smoke sections: kickoff, movement/scoring, and turn advancement | Explicit drive number/state, kickoff in-progress state, touchdown setup reset, halftime, and full-time transitions. |

## Ball, Movement, and Actions

| Area | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Movement | tested | Smoke section: movement, ball pickup, and scoring | Includes activations, standing up, and occupied/out-of-range guards. |
| Rush / go-for-it | tested | Smoke section: fouls, rushes, dodges, and movement skills | Includes weather target, failure, injury, turnover, and reroll choices. |
| Dodge | tested | Smoke section: fouls, rushes, dodges, and movement skills | Includes tackle zones, Dodge reroll, Tackle cancellation, and failed-dodge injury/turnover. |
| Pickup | tested | Smoke section: movement, ball pickup, and scoring | Includes Sure Hands, Big Hand, Extra Arms, weather, tackle zones, and failures. |
| Ball landing and bouncing | tested | Smoke sections: movement/scoring and hand-offs/passing | Covers chained bounces and friendly recovery avoiding turnovers. |
| Touchdowns | tested | Smoke section: movement, ball pickup, and scoring | Scores and resets to setup. |
| Hand-offs | tested | Smoke section: hand-offs, passing, and interference | Includes catches, failed handoffs, team/skill reroll on a dropped catch, turnovers, and bouncing. |
| Passing | tested | Smoke section: hand-offs, passing, and interference | Includes pass ranges, weather, Pass reroll, team/skill reroll on a dropped catch, accurate/inaccurate/fumbled passes, Safe Pass, and empty target squares. |
| Interference/interceptions | tested | Smoke section: hand-offs, passing, and interference | Includes multiple eligible interceptors and Cloud Burster. A successful interference now follows BB2020: the deflecting player earns a Deflection (1 SPP), then may catch the loose ball for an Interception (2 SPP) or leave it bouncing on a miss. |
| Hail Mary Pass | tested | Smoke section: hand-offs, passing, and interference | Simplified special pass behavior is covered. |
| Dump-Off | tested | Smoke section: hand-offs, passing, and interference | Exists as an explicit method; full block-interrupt timing should be reviewed. |
| Running Pass | tested | Smoke section: hand-offs, passing, and interference | Allows movement continuation after passing. |
| On the Ball | tested | Smoke section: hand-offs, passing, and interference | Includes movement helper; full kickoff/pass timing should be reviewed. |
| Fumblerooskie | tested | Smoke section: hand-offs, passing, and interference | Places the ball on a vacated square without bounce/turnover. |

## Blocking, Fouling, Injury

| Area | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Block dice and strength | tested | Smoke section: blocking, pushes, armor, and injuries | Includes assists, Guard, Defensive, Dauntless, Horns, unfavorable dice, and block die choice. |
| Pushes | tested | Smoke section: blocking, pushes, armor, and injuries | Includes pending push choices, Side Step, Stand Firm, Grab, chain pushes, and crowd pushes. |
| Follow-up | tested | Smoke section: blocking, pushes, armor, and injuries | Optional follow-up choice exists after pushes; Fend prevents it; Frenzy forces it into a second block when legal. |
| Armor/injury/casualty | tested | Smoke section: blocking, pushes, armor, and injuries | Includes Mighty Blow, Claws, Iron Hard Skin, Thick Skull, crowd injury, casualty storage, roster casualty application, Miss Next Game, death, and lasting stat injuries. |
| Apothecary | tested | Smoke section: blocking, pushes, armor, and injuries | Pending apothecary choice and rerolled casualty choice exist. |
| Fouling | tested | Smoke section: fouls, rushes, dodges, and movement skills | Includes assists, Dirty Player, Sneaky Git, doubles send-off, pending bribe choice, injury, and turnover. |
| Secret weapons | tested | Movement/scoring section | Drive-end send-off and bribe choice coverage after touchdowns. |

## Standard Skills

| Skill or Group | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Block | tested | Blocking section | Both Down protection. |
| Wrestle | tested | Blocking section | Both players prone and ball drop behavior. |
| Dodge | tested | Movement skills section | Reroll offered and Tackle cancellation covered. |
| Tackle | tested | Movement skills section | Cancels Dodge reroll. |
| Sure Hands | tested | Movement/scoring section | Pickup reroll and Strip Ball immunity. |
| Catch | tested | Hand-offs/passing section | Catch reroll behavior, including a team/skill reroll on dropped pass, hand-off, and bouncing-ball catches. |
| Pass | tested | Hand-offs/passing section | Optional pass reroll. |
| Sure Feet | tested | Movement skills section | Rush reroll behavior. |
| Sprint | tested | Movement skills section | Extra rush allowance. |
| Leap | tested | Movement skills section | Leap movement and failure behavior. |
| Jump Up | tested | Movement skills section | Standing movement and block-from-prone behavior, including a team/Pro reroll on a failed Jump Up roll. |
| Diving Catch | tested | Hand-offs/passing section | Nearby accurate-pass catch behavior. |
| Diving Tackle | partially implemented | Movement skills section | Behavior exists; coach-choice timing needs review. |
| Safe Pair of Hands | tested | Blocking section | Creates a legal ball-placement choice when the carrier is knocked down. |
| Side Step | tested | Blocking section | Push choice behavior. |
| Stand Firm | tested | Blocking section | Pending choice behavior. |
| Grab | tested | Blocking section | Push control and Side Step interaction. |
| Guard | tested | Blocking section | Marked assist behavior. |
| Defensive | tested | Blocking section | Cancels opposing Guard assists. |
| Dauntless | tested | Blocking section | Strength challenge behavior, including a team/Pro reroll on a failed Dauntless roll before the block dice. |
| Horns | tested | Blocking section | Blitz strength modifier. |
| Juggernaut | tested | Blocking section | Blitz block-result interaction. |
| Brawler | tested | Blocking section | Both Down reroll behavior. |
| Break Tackle | tested | Movement skills section | Strength-based dodge modifier behavior. |
| Mighty Blow | tested | Blocking section | Armor/injury pressure. |
| Claws | tested | Blocking section | Armor interaction. |
| Iron Hard Skin | tested | Blocking section | Claws cancellation. |
| Thick Skull | tested | Blocking/fouls sections | KO-to-stunned behavior. |
| Dirty Player | tested | Fouls section | Foul armor/injury modifier. |
| Sneaky Git | tested | Fouls section | Armor-only doubles send-off protection and movement continuation after a non-send-off foul. |
| Fend | tested | Blocking section | Prevents follow-up after pushes. |
| Frenzy | tested | Blocking section | Disables Grab, forces follow-up after the first standing push, and immediately resolves the second block when legal. |
| Shadowing | tested | Movement skills section | Follow behavior covered. |
| Strip Ball | tested | Blocking section | Ball-loosening and Sure Hands immunity. |
| Pro | tested | Movement skills section | Conditional skill reroll is offered and resolved. |
| Multiple Block | tested | Blocking section | Blocks two adjacent defenders with +2 defender strength, continuation state, and no follow-up. |
| Pile Driver | tested | Blocking/fouls sections | Post-block foul surface places the blocker prone and uses foul send-off handling. |
| Strong Arm | tested | Throw and kick team-mate section | Improves Throw Team-Mate target numbers. |

## Passing and Mutation Skills

| Skill | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Accurate | tested | Hand-offs/passing section | Quick/short pass modifier. |
| Cannoneer | tested | Hand-offs/passing section | Long pass modifier. |
| Cloud Burster | tested | Hand-offs/passing section | Optional interference reroll. |
| Dump-Off | tested | Hand-offs/passing section | Implemented as explicit helper. |
| Hail Mary Pass | tested | Hand-offs/passing section | Special pass helper. |
| Leader | tested | Match creation and reroll behavior | Leader reroll availability is modeled. |
| Nerves of Steel | tested | Hand-offs/passing section | Ignores tackle-zone modifiers on pass/catch/interference where implemented. |
| Big Hand | tested | Movement/scoring section | Pickup modifier behavior. |
| Extra Arms | tested | Movement/scoring and hand-offs/passing sections | Pickup/catch/interference modifiers. |
| Monstrous Mouth | tested | Hand-offs/passing section | Catch reroll behavior. |
| Disturbing Presence | tested | Hand-offs/passing section | Pass/catch/interference modifier. |
| Foul Appearance | tested | Blocking section | Prevents block on failed roll. |
| Prehensile Tail | tested | Movement skills section | Dodge-away penalty. |
| Tentacles | tested | Movement skills section | Holds adjacent mover. |
| Two Heads | tested | Movement skills section | Dodge modifier. |
| Very Long Legs | tested | Movement skills and passing sections | Leap/interference modifier behavior. |

## Traits and Special Actions

| Trait or Group | State | Test Coverage | Notes |
| --- | --- | --- | --- |
| Bone-head | tested | Movement skills section | Failed checks waste the action and remove tackle zones until the player successfully starts a later action or is reset. |
| Loner | tested | Movement skills section | Failed Loner checks prevent team-reroll use without spending the reroll. |
| Throw Team-Mate / Right Stuff | tested | Throw and kick team-mate section | Right Stuff validation, launch, landing, crash, turnover, and touchdown behavior, including team/Pro rerolls on the throw and landing rolls. |
| Always Hungry / Swoop / Kick Team-Mate | tested | Throw and kick team-mate section | Always Hungry casualty, Swoop scatter, and Kick Team-Mate launch/landing behavior, including a reroll on the kick accuracy roll. |
| Really Stupid / Take Root / Animal Savagery / Unchannelled Fury / Bloodlust | partially implemented | Movement skills section | Reliability checks are implemented and tested. Animal Savagery and Bloodlust currently use simplified action-wasted behavior rather than full teammate-injury/bite resolution. |
| No Hands | tested | Movement skills section | Prevents pickup and catch attempts; failed pickup bounces the ball. |
| Stunty / Titchy | partially implemented | Movement skills section | Dodge modifiers are implemented and tested. Full Titchy no-tackle-zone behavior and full Stunty injury nuance should still be reviewed. |
| Regeneration / Decay / Plague Ridden / Pick-me-up | tested | Blocking/injuries and movement/scoring sections | Regeneration, Decay, Pick-me-up recovery, and simplified Plague Ridden roster replacement are covered. |
| Chainsaw / Stab / Bombardier / Projectile Vomit / Breathe Fire | partially implemented | Special actions section | Explicit special-action APIs with armor/injury, bomb catch/explosion, activation, and ball-drop coverage, including team/Pro rerolls on the bomb throw and a friendly bomb catch. Secret Weapon send-offs and deeper weapon timing remain for drive/UI work. |
| Ball and Chain / Hypnotic Gaze / Swarming | partially implemented | Special actions section | Ball and Chain random movement/blocking and Hypnotic Gaze tackle-zone removal (with a reroll on the gaze roll) are covered. Swarming remains data-only. |
