using System.Numerics;
using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server;

/// <summary>
/// The visibility grid of a map (SPEC stage 2 §5; ARCHITECTURE.md §4.3): square cells of <see cref="CellSize"/> (10)
/// aligned to the cluster origin of the .cluster file, cell = (floor((x − origin.x) / size), floor((z − origin.z) / size)).
/// The window of a player is every cell within a Chebyshev cell distance (<see cref="Distance"/>) of its own, so its edges
/// lie 40-50 m from the player and are not symmetric about it; fitted on Lymhurst and exact on 6,811 captured
/// object/position pairs of 1000, 1001, 1002 and 1207, where a centred square was not. Objects do not count their size.
/// Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class CellGrid(Vector2 origin, float cellSize)
{
    private readonly Dictionary<(int X, int Z), List<WorldObject>> _cells = new();

    /// <summary>The cluster origin (0000: (-305, -305)).</summary>
    public Vector2 Origin { get; } = origin;

    public float CellSize { get; } = cellSize;

    /// <summary>The cell of <paramref name="position"/> (computed in double: tile positions sit on exact cell borders, x = -45 in Thetford).</summary>
    public (int X, int Z) CellOf(Vector2 position) =>
        ((int)Math.Floor(((double)position.X - Origin.X) / CellSize), (int)Math.Floor(((double)position.Y - Origin.Y) / CellSize));

    /// <summary>Chebyshev distance of two cells: max(|dcx|, |dcz|).</summary>
    public static int Distance((int X, int Z) a, (int X, int Z) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));

    /// <summary>Puts <paramref name="o"/> into the cell of its position and records the cell on it.</summary>
    public void Add(WorldObject o)
    {
        o.Cell = CellOf(o.Position);
        if (!_cells.TryGetValue(o.Cell, out var list)) _cells[o.Cell] = list = [];
        list.Add(o);
    }

    /// <summary>Takes <paramref name="o"/> out of its cell (a mob despawned, stage 5).</summary>
    public void Remove(WorldObject o)
    {
        if (_cells.TryGetValue(o.Cell, out var list) && list.Remove(o) && list.Count == 0) _cells.Remove(o.Cell);
    }

    /// <summary>The objects whose cell is at most <paramref name="range"/> cells from <paramref name="cell"/>, cell by cell.</summary>
    public IEnumerable<WorldObject> Around((int X, int Z) cell, int range)
    {
        for (var x = cell.X - range; x <= cell.X + range; x++)
            for (var z = cell.Z - range; z <= cell.Z + range; z++)
                if (_cells.TryGetValue((x, z), out var list))
                    foreach (var o in list) yield return o;
    }
}
