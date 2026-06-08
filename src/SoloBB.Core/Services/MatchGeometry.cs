using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class MatchGeometry
{
    public static bool IsAdjacent(PitchSquare first, PitchSquare second)
    {
        return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)) == 1;
    }

    public static IEnumerable<PitchSquare> AdjacentSquares(PitchSquare square)
    {
        for (var y = square.Y - 1; y <= square.Y + 1; y++)
        {
            for (var x = square.X - 1; x <= square.X + 1; x++)
            {
                if (x == square.X && y == square.Y)
                {
                    continue;
                }

                yield return new PitchSquare(x, y);
            }
        }
    }

    public static bool IsOnPitch(Ruleset ruleset, PitchSquare square)
    {
        return square.X >= 0 && square.X < ruleset.PitchWidth && square.Y >= 0 && square.Y < ruleset.PitchHeight;
    }

    public static bool IsWideZone(Ruleset ruleset, PitchSquare square)
    {
        return square.Y < 4 || square.Y >= ruleset.PitchHeight - 4;
    }

    public static bool IsSameWideZone(Ruleset ruleset, PitchSquare first, PitchSquare second)
    {
        return (first.Y < 4 && second.Y < 4) ||
            (first.Y >= ruleset.PitchHeight - 4 && second.Y >= ruleset.PitchHeight - 4);
    }

    public static bool IsLineOfScrimmage(Ruleset ruleset, Guid teamId, Guid homeTeamId, PitchSquare square)
    {
        var midline = ruleset.PitchWidth / 2;
        return teamId == homeTeamId ? square.X == midline - 1 : square.X == midline;
    }

    public static bool IsOnPassingLane(PitchSquare square, PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        var lineX = receiverSquare.X - passerSquare.X;
        var lineY = receiverSquare.Y - passerSquare.Y;
        var squareX = square.X - passerSquare.X;
        var squareY = square.Y - passerSquare.Y;
        var lengthSquared = (lineX * lineX) + (lineY * lineY);

        if (lengthSquared == 0)
        {
            return false;
        }

        var projection = ((squareX * lineX) + (squareY * lineY)) / (double)lengthSquared;
        if (projection <= 0 || projection >= 1)
        {
            return false;
        }

        var closestX = passerSquare.X + (projection * lineX);
        var closestY = passerSquare.Y + (projection * lineY);
        var distanceX = square.X - closestX;
        var distanceY = square.Y - closestY;
        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY)) <= 0.5;
    }

    public static IReadOnlyList<PitchSquare> BuildMovementPath(PitchSquare start, PitchSquare destination)
    {
        var path = new List<PitchSquare>();
        var currentX = start.X;
        var currentY = start.Y;

        while (currentX != destination.X || currentY != destination.Y)
        {
            currentX += Math.Sign(destination.X - currentX);
            currentY += Math.Sign(destination.Y - currentY);
            path.Add(new PitchSquare(currentX, currentY));
        }

        return path;
    }

    public static PassRange? ResolvePassRange(PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        var distance = Math.Max(
            Math.Abs(passerSquare.X - receiverSquare.X),
            Math.Abs(passerSquare.Y - receiverSquare.Y));

        return distance switch
        {
            <= 3 => new PassRange("quick", 0),
            <= 6 => new PassRange("short", 1),
            <= 9 => new PassRange("long", 2),
            <= 13 => new PassRange("long bomb", 3),
            _ => null
        };
    }

    public static bool OccupiesPitch(PlayerPitchState state)
    {
        return state is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned;
    }

    public static IEnumerable<PitchSquare> OccupiedSquares(PitchSquare anchor, bool isLarge)
    {
        yield return anchor;
        if (isLarge)
        {
            yield return new PitchSquare(anchor.X + 1, anchor.Y);
            yield return new PitchSquare(anchor.X, anchor.Y + 1);
            yield return new PitchSquare(anchor.X + 1, anchor.Y + 1);
        }
    }

    public static bool PlacementOccupiesSquare(PlayerPlacement placement, PitchSquare square)
    {
        return placement.Square is not null &&
               OccupiedSquares(placement.Square, placement.IsLarge).Contains(square);
    }

    public static bool IsAdjacentToPlacement(PlayerPlacement placement, PitchSquare square)
    {
        if (placement.Square is null)
        {
            return false;
        }

        var footprint = OccupiedSquares(placement.Square, placement.IsLarge).ToHashSet();
        return !footprint.Contains(square) && footprint.Any(s => IsAdjacent(s, square));
    }

    public static bool PlacementsAreAdjacent(PlayerPlacement p1, PlayerPlacement p2)
    {
        if (p1.Square is null || p2.Square is null)
        {
            return false;
        }

        var footprint2 = OccupiedSquares(p2.Square, p2.IsLarge).ToHashSet();
        return OccupiedSquares(p1.Square, p1.IsLarge).Any(s1 => footprint2.Any(s2 => IsAdjacent(s1, s2)));
    }

    public static IEnumerable<PitchSquare> PlacementAdjacentSquares(PlayerPlacement placement)
    {
        if (placement.Square is null)
        {
            yield break;
        }

        var footprint = OccupiedSquares(placement.Square, placement.IsLarge).ToHashSet();
        foreach (var square in footprint.SelectMany(AdjacentSquares).Distinct().Where(s => !footprint.Contains(s)))
        {
            yield return square;
        }
    }

    public static bool HasActiveTackleZone(PlayerPlacement placement)
    {
        return placement.State == PlayerPitchState.Standing && !placement.TackleZonesLost;
    }
}
