# SoloBB Sprite Sheets

These are first-pass pitch sprites for UX validation. The `_32.png` files are packed into exact 32 px cells for Godot import. The larger `.png` files are transparent source sheets, and the `_raw_chroma.png` files are the original chroma-key generations kept for reprocessing.

Large players use separate `_64.png` sheets. Each sheet is `64x128`, with a `64x64` standing frame above a `64x64` prone frame. On the pitch these frames render at twice the width and height of a standard player while remaining anchored to the player's logical square.

| Sheet | Standing Row | Prone Row |
| --- | --- | --- |
| `human_ogre_64.png` | Human Ogre | Human Ogre prone |
| `orc_untrained_troll_64.png` | Orc Untrained Troll | Orc Untrained Troll prone |
| `dwarf_deathroller_64.png` | Dwarf Deathroller | Dwarf Deathroller disabled/prone |
| `chaos_chosen_minotaur_64.png` | Chaos Chosen Minotaur | Chaos Chosen Minotaur prone |
| `lizardmen_kroxigor_64.png` | Lizardmen Kroxigor | Lizardmen Kroxigor prone |

## `human_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Catcher | Catcher prone |
| 1 | Thrower | Thrower prone |
| 2 | Blitzer | Blitzer prone |
| 3 | Lineman | Lineman prone |

## `orc_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Thrower | Thrower prone |
| 1 | Blitzer | Blitzer prone |
| 2 | Big un | Big un prone |
| 3 | Lineman | Lineman prone |

## `dwarf_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Runner | Runner prone |
| 1 | Blitzer | Blitzer prone |
| 2 | Troll Slayer | Troll Slayer prone |
| 3 | Blocker Lineman | Blocker Lineman prone |

## `shambling_undead_team_32.png`

Size: `160x64`
Cell size: `32x32`
Layout: `5 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Skeleton Lineman | Skeleton Lineman prone |
| 1 | Zombie Lineman | Zombie Lineman prone |
| 2 | Ghoul Runner | Ghoul Runner prone |
| 3 | Wight Blitzer | Wight Blitzer prone |
| 4 | Mummy | Mummy prone |

## `high_elf_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Catcher | Catcher prone |
| 1 | Thrower | Thrower prone |
| 2 | Blitzer | Blitzer prone |
| 3 | Lineman | Lineman prone |

## `amazon_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Eagle Warrior | Eagle Warrior prone |
| 1 | Thrower | Thrower prone |
| 2 | Piranha Warrior | Piranha Warrior prone |
| 3 | Linewoman | Linewoman prone |

## `dark_elf_team_32.png`

Size: `128x64`
Cell size: `32x32`
Layout: `4 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Runner | Runner prone |
| 1 | Witch Elf | Witch Elf prone |
| 2 | Blitzer | Blitzer prone |
| 3 | Lineman | Lineman prone |

## `chaos_chosen_team_32.png`

Size: `64x64`
Cell size: `32x32`
Layout: `2 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Beastman Runner | Beastman Runner prone |
| 1 | Chosen Blocker | Chosen Blocker prone |

## `lizardmen_team_32.png`

Size: `96x64`
Cell size: `32x32`
Layout: `3 columns x 2 rows`

| Cell | Standing Row | Prone Row |
| --- | --- | --- |
| 0 | Skink Runner | Skink Runner prone |
| 1 | Chameleon Skink | Chameleon Skink prone |
| 2 | Saurus Blocker | Saurus Blocker prone |

## `pitch_objects_32.png`

Size: `192x128`
Cell size: `32x32`
Layout: `6 columns x 4 rows`

| Row | Contents |
| --- | --- |
| 0 | Ball bounce frames 0-5 |
| 1 | Stunned-star frames 0-5 |
| 2 | Ball-at-rest variants in cells 0-1 |
| 3 | Scatter/chalk puff frames in cells 0-1 |

The remaining empty cells are intentionally transparent.

## `pitch_tiles_32.png`

Size: `256x128`
Cell size: `32x32`
Layout: `8 columns x 4 rows`

| Row | Cell | Contents |
| --- | --- | --- |
| 0 | 0-3 | Grass variants |
| 0 | 4 | Home end-zone tile |
| 0 | 5 | Away end-zone tile |
| 0 | 6 | Worn/scuffed grass |
| 0 | 7 | Dark dugout/disabled grass |
| 1 | 0 | Center/goal line through tile center |
| 1 | 1 | Chalk line on left edge |
| 1 | 2 | Chalk line on right edge |
| 1 | 3-6 | 2x2 center insignia quadrants |
| 1 | 7 | Small center insignia tile |
| 2 | 0 | Legal square highlight tile |
| 2 | 1 | Selected square highlight tile |
| 2 | 2 | Risk square highlight tile |
| 2 | 3 | Target/block highlight tile |
| 2 | 4 | Home-tinted state tile |
| 2 | 5 | Away-tinted state tile |
| 2 | 6 | Ball-square highlight tile |
| 2 | 7 | Disabled/unavailable square tile |
| 3 | 0 | Transparent selected outline overlay |
| 3 | 1 | Transparent legal deployment outline overlay |
| 3 | 2 | Transparent risk outline overlay |
| 3 | 3 | Transparent dotted path overlay |
| 3 | 4 | Transparent ball target ring overlay |
| 3 | 5 | Transparent target/block outline overlay |
| 3 | 6 | Transparent vertical chalk line overlay |
| 3 | 7 | Transparent horizontal chalk line overlay |

## `pitch_field_32.png`

Size: `832x480`
Cell size: `32x32`
Layout: `26 columns x 15 rows`

This is the composited preview of the split pitch art.

## `pitch_field_base_32.png`

Size: `832x480`
Cell size: `32x32`
Layout: `26 columns x 15 rows`

Base grass and end-zone art used by the match screen.

## `pitch_field_markings_32.png`

Size: `832x480`
Cell size: `32x32`
Layout: `26 columns x 15 rows`

Transparent chalk-marking layer used by the match screen. It is drawn above legal-placement highlights so markings remain readable:

- One-column home and away end zones.
- Goal lines at the boundary between each end zone and the field.
- Center line at the half-way boundary.
- Wide-zone separator lines that stop before the end zones.
- A center-field chalk crest.

The match screen slices the base and marking images into 32 px square regions at runtime, then draws transparent highlight/player/ball layers between and above them.
