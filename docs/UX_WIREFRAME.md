# SoloBB UX Wireframe

This first-pass wireframe treats SoloBB as a tabletop companion: dense enough for league and match management, but calm enough that the next decision is always obvious.

## Design Goals

- Put the pitch and current decision at the center of match play.
- Keep league management scannable, with standings, schedule, and team state visible without drilling.
- Replace long vertical forms with grouped panels and sticky summaries.
- Make invalid states visible near the control that caused them.
- Prefer compact, work-focused UI over a marketing-style game menu.

## App Shell

Use the same application frame for every screen after launch.

```text
+--------------------------------------------------------------------------------+
| SoloBB                                League: Reikland Cup        Save status   |
+----------------------+---------------------------------------------------------+
| Navigation           | Screen content                                           |
|                      |                                                         |
|  League Home         |                                                         |
|  Teams               |                                                         |
|  Schedule            |                                                         |
|  Rules               |                                                         |
|                      |                                                         |
|  New League          |                                                         |
|  Load League         |                                                         |
|                      |                                                         |
|  Back / Exit         |                                                         |
+----------------------+---------------------------------------------------------+
```

Notes:

- The left navigation should be narrow and persistent where possible.
- Save/load status belongs in the top bar, not as loose text at the bottom of each screen.
- Primary actions should live in the screen header or relevant panel footer.

## Main Menu

The main menu should feel like a compact start screen, not a giant vertical button stack.

```text
+--------------------------------------------------------------------------------+
| SoloBB                                                                         |
| Solo and hotseat fantasy football league play                                  |
|                                                                                |
|  [Continue League]     Reikland Cup - Week 3                                   |
|  [New League]          Create a fresh league                                   |
|  [Load League]         Choose a saved league                                   |
|                                                                                |
|  Recent leagues                                                                |
|  +--------------------------------------------------------------------------+  |
|  | Reikland Cup          8 teams     Week 3      Last saved 10:14 AM        |  |
|  | Border Princes Open   4 teams     Drafting    Last saved yesterday       |  |
|  +--------------------------------------------------------------------------+  |
|                                                                                |
|                                                        [Quit]                  |
+--------------------------------------------------------------------------------+
```

## League Home

The league home should answer: who is leading, what happens next, and what needs attention?

```text
+--------------------------------------------------------------------------------+
| Reikland Cup                                      Week 3        [New Match]     |
+--------------------------------------------------------------------------------+
| Summary                                                                        |
|  8 teams        12 games played        4 games this week        Season 1        |
+----------------------------------------------------+---------------------------+
| Standings                                          | This Week                 |
| +----+----------------------+----+----+----+------+ | +-----------------------+ |
| | #  | Team                 | TV | W  | L  | LP   | | Middenheim vs Altdorf  | |
| | 1  | Middenheim Maulers   |105 | 2  | 0  | 6    | | [Pregame] [Play]       | |
| | 2  | Altdorf Griffons     |100 | 1  | 1  | 3    | |                       | |
| | 3  | ...                  |    |    |    |      | | Nuln vs Talabheim     | |
| +----+----------------------+----+----+----+------+ | [Pregame] [Play]       | |
|                                                     | +-----------------------+ |
+----------------------------------------------------+---------------------------+
| Team detail drawer opens when a standings row is selected                       |
+--------------------------------------------------------------------------------+
```

Key changes from current screen:

- Keep standings as the largest element.
- Put week games in action cards with clear `Pregame` and `Play` paths.
- Use row selection to reveal a team preview instead of instantly navigating away on a single click.
- Preserve double-click or explicit `Open Team` for navigation.

## Team Detail

The current team home is mostly a stat list. It should become a roster inspection screen.

```text
+--------------------------------------------------------------------------------+
| Middenheim Maulers                              TV 1,050,000    Treasury 80,000 |
+--------------------------------------------------------------------------------+
| Team Snapshot                                                                  |
| Coach: Hotseat      Rerolls: 3      Fan Factor: 2      Apothecary: 1            |
+--------------------------------------------------+-----------------------------+
| Roster                                           | Staff and Assets            |
| +----+------------------+----------+------------+ | Rerolls        3          |
| | #  | Player           | Position | Status     | | Cheerleaders   0          |
| | 1  | Blitzer 1        | Blitzer  | Ready      | | Assistants     1          |
| | 2  | Lineman 1        | Lineman  | Ready      | | Apothecaries   1          |
| | .. |                  |          |            | | Treasury       80,000 gp  |
| +----+------------------+----------+------------+ |                             |
|                                                  | [Edit Team]                 |
+--------------------------------------------------+-----------------------------+
```

## Team Builder

Team creation has a lot of numeric constraints. The builder should show cost feedback constantly.

```text
+--------------------------------------------------------------------------------+
| Create Team                                                       [Save Team]   |
+--------------------------------------------------------------------------------+
| Identity                                                                       |
| Team Name [Middenheim Maulers        ] Coach [Hotseat              ]           |
| Roster    [Human Nobility        v]                                            |
+--------------------------------------------------+-----------------------------+
| Position Draft                                    | Budget                      |
| +----------+------------+------+-------+--------+ | Starting      1,000,000 gp |
| | Min-Max  | Position   | Cost | Stats | Count  | | Players         770,000 gp |
| | 0-4      | Blitzer    | 90k  | ...   | [- 2 +]| | Rerolls         150,000 gp |
| | 0-12     | Lineman    | 50k  | ...   | [- 9 +]| | Staff            50,000 gp |
| +----------+------------+------+-------+--------+ | Remaining       30,000 gp |
|                                                  |                             |
| Team Assets                                      | Status                      |
| Rerolls [- 3 +]  Fan Factor [- 1 +]              | Ready: 11 players           |
| Assistants [- 1 +] Cheerleaders [- 0 +] Apothecary [x]                         |
+--------------------------------------------------+-----------------------------+
```

Interaction notes:

- Replace wide stat grids with compact rows and expanded tooltips for full skill text.
- The budget panel should remain visible while editing.
- Disable unaffordable increments at the stepper level, but also explain why in the status area.

## Pregame

Pregame is a comparison and shopping flow. It should look like two mirrored team panels plus a central budget summary.

```text
+--------------------------------------------------------------------------------+
| Pregame: Middenheim Maulers vs Altdorf Griffons                  [Start Match]  |
+--------------------------------------------------------------------------------+
| Week 3                                                                         |
+----------------------------------+----------------+----------------------------+
| Middenheim Maulers               | Match Budget   | Altdorf Griffons           |
| TV 1,050,000                     | Petty cash     | TV 960,000                 |
| Treasury 80,000                  | Home: 0        | Treasury 120,000           |
| Journeymen 0                     | Away: 90,000   | Journeymen 1               |
|                                  | Remaining      |                            |
| Bribes [- 0 +]                   | Home: 80,000   | Bribes [- 1 +]             |
| Treasury spend [0          ]     | Away: 20,000   | Treasury spend [20,000]    |
+----------------------------------+----------------+----------------------------+
| Star Players                                                                  |
| +-----+----------------------+---------+---------------+----------------------+ |
| | Use | Star                 | Cost    | Eligible Team | Skills               | |
| | [ ] | Griff Oberwald       | 280,000 | Away          | Block, Dodge, ...    | |
| +-----+----------------------+---------+---------------+----------------------+ |
|                                                                                |
| [Back]                                                                         |
+--------------------------------------------------------------------------------+
```

## Match Screen

The match screen should be optimized for repeated turn-by-turn use. The pitch stays central, the current choice is prominent, and advanced context sits nearby without taking over.

```text
+--------------------------------------------------------------------------------+
| Middenheim Maulers  1       Half 1 - Turn 4       0  Altdorf Griffons          |
| RR 2  Bribes 0  KO 1        Weather: Nice         RR 1  Bribes 1  KO 0         |
+--------------------------------------------------------------------------------+
| Current Decision                                                               |
| Select destination for Blitzer 2. Dodge 3+, pickup 2+.                         |
| [Confirm Move] [Cancel]                          Last roll: Dodge 4 - success  |
+-------------------------+--------------------------------------+---------------+
| Active Roster           | Pitch                                | Event Log     |
| [H1] Thrower Ready      | +----------------------------------+ | 4. Blitzer... |
| [H2] Blitzer Selected   | |                                  | | 3. Ball scat. |
| [H3] Lineman Used       | |          26 x 15 pitch grid       | | 2. Kickoff... |
| [H4] Lineman Prone      | |          larger square size       | | 1. Weather... |
|                         | |                                  | |               |
| Player Inspector        | +----------------------------------+ |               |
| Blitzer 2               | Legend: selected, legal, risky, ball|               |
| MA 7 ST 3 AG 3+ PA 4+   |                                      |               |
| Skills: Block           |                                      |               |
+-------------------------+--------------------------------------+---------------+
| Action Bar                                                                     |
| [Move] [Block] [Blitz] [Pass] [Foul] [TTM] [KTM]       [End Activation] [Back] |
+--------------------------------------------------------------------------------+
```

Important match UX behavior:

- Rename the title from `Match Setup` to the real match state, such as `Kickoff`, `Setup`, or `Turn 4`.
- Put pending choices in the `Current Decision` band, not scattered across the footer.
- Keep the roster panel left because selecting players is frequent.
- Put a compact event log right so the user can audit what just happened.
- Move rare choice groups such as apothecary, Stand Firm, Diving Tackle, and send-off into the decision band when active.
- Use an action bar for mode toggles. Selected modes should remain visibly pressed.
- Add a small legend under the pitch for color meanings.

## Responsive Layout

Desktop target:

```text
Left roster: 220-280 px
Pitch: grows, centered, square size 24-32 px
Right log: 220-280 px
Top decision band: full width
Bottom action bar: full width
```

Narrow target:

```text
+--------------------------------------+
| Score / turn                         |
| Current decision                     |
| Tabs: Pitch | Roster | Log           |
|                                      |
| Active tab content                   |
|                                      |
| Action bar wraps into two rows       |
+--------------------------------------+
```

## Visual Direction

- Background: muted charcoal green, not pure black.
- Pitch: saturated grass green with clear white grid lines and team-colored end zones.
- Panels: dark neutral surfaces with subtle borders.
- Team colors: home blue, away red, selected amber, risk orange, illegal muted gray.
- Typography: compact labels, tabular numbers for standings and currency.
- Buttons: primary action filled, secondary actions outlined or neutral.

## First Implementation Slice

The highest-value UX slice is the match screen shell:

1. Change the match layout to `top HUD -> current decision -> three-column body -> action bar`.
2. Move `_summaryLabel` and pending choice controls into the current decision band.
3. Add a right-side event log panel.
4. Keep the existing pitch button implementation, but increase square size slightly and center the pitch.
5. Convert pass, TTM, and KTM buttons into visibly toggled mode buttons.

That slice improves the most-used screen without requiring a rewrite of the core match logic.
