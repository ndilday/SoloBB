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
    private async Task AnimateMovementAsync(MatchState beforeMatch, MatchState afterMatch, Guid playerId, IReadOnlyList<PitchSquare> path)
    {
        if (path.Count == 0)
        {
            return;
        }

        _isAnimating = true;
        try
        {
            var finalSquare = afterMatch.Placements.FirstOrDefault(placement => placement.PlayerId == playerId)?.Square;
            foreach (var square in path)
            {
                _match = beforeMatch with
                {
                    Placements = beforeMatch.Placements
                        .Select(placement => placement.PlayerId == playerId
                            ? placement with { Square = square }
                            : placement)
                        .ToArray()
                };
                RefreshPitch();
                await Task.Delay(70);
                if (finalSquare == square)
                {
                    break;
                }
            }
        }
        finally
        {
            _isAnimating = false;
            _match = afterMatch;
        }
    }

    private async Task AnimateBallAsync(MatchState beforeMatch, MatchState afterMatch, int logStart)
    {
        var path = BallAnimationPath(beforeMatch, afterMatch, logStart);
        if (path.Count == 0)
        {
            _animationBallSquare = null;
            return;
        }

        var savedMatch = _match;
        _match = afterMatch;
        foreach (var square in path)
        {
            _animationBallSquare = square;
            RefreshPitch();
            await Task.Delay(90);
        }

        _animationBallSquare = null;
        _match = savedMatch;
    }

    private IReadOnlyList<PitchSquare> BallAnimationPath(MatchState beforeMatch, MatchState afterMatch, int logStart)
    {
        var squares = new List<PitchSquare>();
        var start = BallDisplaySquare(beforeMatch);
        if (start is not null)
        {
            squares.Add(start);
        }

        foreach (var entry in afterMatch.Log.Skip(logStart))
        {
            if (!MentionsBall(entry.Message))
            {
                continue;
            }

            foreach (var square in ExtractPitchSquares(entry.Message))
            {
                if (IsOnPitch(square))
                {
                    squares.Add(square);
                }
            }
        }

        var end = BallDisplaySquare(afterMatch);
        if (end is not null)
        {
            squares.Add(end);
        }

        return squares
            .Where(square => IsOnPitch(square))
            .Aggregate(new List<PitchSquare>(), (path, square) =>
            {
                if (path.Count == 0 || path[^1] != square)
                {
                    path.Add(square);
                }

                return path;
            })
            .Skip(start is null ? 0 : 1)
            .ToArray();
    }

    private static bool MentionsBall(string message)
    {
        return message.Contains("ball", StringComparison.OrdinalIgnoreCase);
    }

    private PitchSquare? BallDisplaySquare(MatchState match)
    {
        if (match.Ball.Square is PitchSquare ballSquare)
        {
            return ballSquare;
        }

        return match.Ball.CarrierPlayerId is Guid carrierId
            ? match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId)?.Square
            : null;
    }

    private static IEnumerable<PitchSquare> ExtractPitchSquares(string message)
    {
        foreach (Match match in Regex.Matches(message, @"-?\d+,-?\d+"))
        {
            var parts = match.Value.Split(',');
            if (int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            {
                yield return new PitchSquare(x, y);
            }
        }
    }
}
