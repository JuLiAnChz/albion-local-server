using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// Positions and rotations of the cluster data (SPEC stage 2 §2 "Transform"): "x y z" with y as the height, world 2D =
/// (x, z), yaw about the height axis with x' = x·cosθ + z·sinθ and z' = −x·sinθ + z·cosθ (the sign the Lymhurst trees in
/// 90°/270° compounds confirm, and the one that gives the Thetford tree counts of §7). Same rules as
/// analysis/work/stage2/data/tools/clusterdump.py (v3, yaw_of, rot2, Xform).
/// </summary>
internal static class TileMath
{
    /// <summary>"x y z"; missing parts are 0 (a 2D "x z" gives z = 0, as v3 does). A number that does not parse throws <see cref="InvalidDataException"/>.</summary>
    public static (double X, double Y, double Z) V3(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (0, 0, 0);
        var p = Numbers(text);
        return (p.Length > 0 ? p[0] : 0, p.Length > 1 ? p[1] : 0, p.Length > 2 ? p[2] : 0);
    }

    /// <summary>A 2D "x z" pair (world.xml positions, exit spawnpos, sizes); null for a missing attribute.</summary>
    public static Vector2? V2(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var p = Numbers(text);
        return new Vector2(p.Length > 0 ? (float)p[0] : 0, p.Length > 1 ? (float)p[1] : 0);
    }

    /// <summary>A float attribute; missing = <paramref name="fallback"/>.</summary>
    public static float Float(string? text, float fallback = 0) =>
        string.IsNullOrWhiteSpace(text) ? fallback : (float)Numbers(text)[0];

    /// <summary>An int attribute; missing or not a number = <paramref name="fallback"/>.</summary>
    public static int Int(string? text, int fallback = 0) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    private static double[] Numbers(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new InvalidDataException($"'{text}' is not a list of numbers");
        return values;
    }

    /// <summary>
    /// The yaw of a tile, compound or critical tile: roty, else rot ("x y z" Euler angles: the y one; a single number:
    /// that one), else 0 (yaw_of).
    /// </summary>
    public static double YawOf(XElement e)
    {
        if (e.Attribute("roty")?.Value is { } roty) return V3(roty).X;
        if (e.Attribute("rot")?.Value is not { } rot) return 0;
        var r = V3(rot);
        return rot.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 3 ? r.Y : r.X;
    }

    /// <summary>0 ≤ result &lt; 360, as Python's % 360.</summary>
    public static double Mod360(double degrees) => (degrees % 360 + 360) % 360;

    /// <summary>
    /// Rotates (x, z) by <paramref name="degrees"/>. The right angles (every templateinstance rot, most roty) are exact,
    /// so positions like (-25, 38) stay free of 1e-15 residue.
    /// </summary>
    public static (double X, double Z) Rotate(double x, double z, double degrees)
    {
        var (c, s) = Mod360(degrees) switch
        {
            0.0 => (1.0, 0.0),
            90.0 => (0.0, 1.0),
            180.0 => (-1.0, 0.0),
            270.0 => (0.0, -1.0),
            var d => (Math.Cos(d * Math.PI / 180), Math.Sin(d * Math.PI / 180)),
        };
        return (x * c + z * s, -x * s + z * c);
    }
}

/// <summary>
/// Where a template, compound or tile sits in its parent: position (y = height) and yaw (clusterdump.py Xform).
/// <see cref="Apply"/> places a local point, <see cref="Child"/> the frame of something placed inside.
/// </summary>
internal readonly record struct Placement(double X, double Y, double Z, double Yaw)
{
    public static readonly Placement Identity = new(0, 0, 0, 0);

    public (double X, double Y, double Z) Apply(double x, double y, double z)
    {
        var (rx, rz) = TileMath.Rotate(x, z, Yaw);
        return (X + rx, Y + y, Z + rz);
    }

    public Placement Child(double x, double y, double z, double yaw)
    {
        var (wx, wy, wz) = Apply(x, y, z);
        return new Placement(wx, wy, wz, TileMath.Mod360(Yaw + yaw));
    }
}
