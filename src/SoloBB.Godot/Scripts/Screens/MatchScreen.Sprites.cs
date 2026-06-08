using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private void LoadSpriteAssets()
    {
        _atlasCache.Clear();
        _humanSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/human_team_32.png");
        _orcSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/orc_team_32.png");
        _dwarfSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/dwarf_team_32.png");
        _shamblingUndeadSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/shambling_undead_team_32.png");
        _highElfSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/high_elf_team_32.png");
        _amazonSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/amazon_team_32.png");
        _darkElfSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/dark_elf_team_32.png");
        _pitchObjectSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_objects_32.png");
        _blockDiceSheet = GD.Load<Texture2D>("res://assets/sprites/block_dice_32.png");
        _pitchTileSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_tiles_32.png");
        _pitchFieldSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_field_base_32.png");
        _pitchMarkingSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_field_markings_32.png");
    }

    private Texture2D? AtlasCell(Texture2D? sheet, string key, int column, int row)
    {
        if (sheet is null)
        {
            return null;
        }

        if (_atlasCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var atlas = new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(column * 32, row * 32, 32, 32)
        };
        _atlasCache[key] = atlas;
        return atlas;
    }

    private Texture2D? PlayerSprite(LeagueTeam team, Player player, PlayerPlacement? placement)
    {
        var prone = placement?.State is PlayerPitchState.Prone or PlayerPitchState.Stunned;
        var row = prone ? 1 : 0;
        if (string.Equals(team.RosterId, "orc", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "thrower" => 0,
                "blitzer" => 1,
                "big-un" => 2,
                _ => 3
            };
            return AtlasCell(_orcSpriteSheet, $"orc:{column}:{row}", column, row);
        }

        if (string.Equals(team.RosterId, "dwarf", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "runner" => 0,
                "blitzer" => 1,
                "troll-slayer" => 2,
                _ => 3
            };
            return AtlasCell(_dwarfSpriteSheet, $"dwarf:{column}:{row}", column, row);
        }

        if (string.Equals(team.RosterId, "shambling-undead", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "zombie" => 1,
                "ghoul" => 2,
                "wight" => 3,
                "mummy" => 4,
                _ => 0
            };
            return AtlasCell(_shamblingUndeadSpriteSheet, $"shambling-undead:{column}:{row}", column, row);
        }

        if (string.Equals(team.RosterId, "high-elf", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "catcher" => 0,
                "thrower" => 1,
                "blitzer" => 2,
                _ => 3
            };
            return AtlasCell(_highElfSpriteSheet, $"high-elf:{column}:{row}", column, row);
        }

        if (string.Equals(team.RosterId, "amazon", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "catcher" => 0,
                "thrower" => 1,
                "blitzer" => 2,
                _ => 3
            };
            return AtlasCell(_amazonSpriteSheet, $"amazon:{column}:{row}", column, row);
        }

        if (string.Equals(team.RosterId, "dark-elf", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "runner" => 0,
                "witch-elf" => 1,
                "blitzer" => 2,
                _ => 3
            };
            return AtlasCell(_darkElfSpriteSheet, $"dark-elf:{column}:{row}", column, row);
        }

        var humanColumn = player.PositionId switch
        {
            "catcher" => 0,
            "thrower" => 1,
            "blitzer" => 2,
            _ => 3
        };
        return AtlasCell(_humanSpriteSheet, $"human:{humanColumn}:{row}", humanColumn, row);
    }

    private Texture2D? BallSprite(int frame)
    {
        var column = Math.Clamp(frame, 0, 5);
        return AtlasCell(_pitchObjectSheet, $"object:ball:{column}", column, 0);
    }

    private Texture2D? StunnedSprite(int frame)
    {
        var column = Math.Clamp(frame, 0, 5);
        return AtlasCell(_pitchObjectSheet, $"object:stunned:{column}", column, 1);
    }

    private Texture2D? BlockDieSprite(int roll)
    {
        var column = Math.Clamp(roll, 1, 6) - 1;
        return AtlasCell(_blockDiceSheet, $"block-die:{column}", column, 0);
    }

    private Texture2D? PitchTileTexture(PitchSquare square, bool canUse, string? pathMarker)
    {
        return AtlasCell(_pitchFieldSheet, $"field:{square.X}:{square.Y}", square.X, square.Y);
    }

    private Texture2D? PitchMarkingTexture(PitchSquare square)
    {
        return AtlasCell(_pitchMarkingSheet, $"marking:{square.X}:{square.Y}", square.X, square.Y);
    }

    private Texture2D? PitchHighlightTexture(bool canUse, string? pathMarker)
    {
        if (pathMarker is not null)
        {
            if (pathMarker.StartsWith('!'))
            {
                return AtlasCell(_pitchTileSheet, "overlay:rush", 2, 3);
            }

            return pathMarker switch
            {
                ">" => AtlasCell(_pitchTileSheet, "overlay:risk", 2, 3),
                "B" or "P" or "L" => AtlasCell(_pitchTileSheet, "overlay:target", 5, 3),
                "o" => AtlasCell(_pitchTileSheet, "overlay:ball-target", 4, 3),
                "." or "X" => AtlasCell(_pitchTileSheet, "overlay:path", 3, 3),
                _ => AtlasCell(_pitchTileSheet, "overlay:selected", 0, 3)
            };
        }

        return canUse ? AtlasCell(_pitchTileSheet, "overlay:legal", 1, 3) : null;
    }
}
