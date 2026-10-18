namespace AlbionLocal.Server.Entities;

/// <summary>
/// One bar of a unit (health, energy, mount health) as the client holds it (ARCHITECTURE.md §4.1): current value,
/// maximum, regeneration cap, regeneration per second and the GameTimeStamp the value was taken at, the five fields of
/// Join keys 11-15 / 16-20 / 21-25 (cx2.ai7: a44.n(cur, max, regenCap), a44.n(stamp, regenPerSecond, cur)) and of each
/// part of RegenerationPlayerComboChanged (event 96). Regeneration is lazy: nothing ticks, <see cref="At"/> computes
/// value(t) = min(cap, value + regen * (t - stamp)); the server sends the bar again only when that line breaks (damage,
/// healing, a new maximum or regeneration).
/// </summary>
public struct ResourceBar
{
    /// <summary>The value at <see cref="Stamp"/>.</summary>
    public float Value;

    public float Max;

    /// <summary>Share of <see cref="Max"/> regeneration stops at; 0 = 100%, as the client reads it (not sent then, as official).</summary>
    public float RegenCap;

    public float RegenPerSecond;

    /// <summary>GameTimeStamp (UTC ticks) of <see cref="Value"/>.</summary>
    public long Stamp;

    private readonly float Limit => Max * (RegenCap == 0 ? 1 : RegenCap);

    /// <summary>The value at GameTimeStamp <paramref name="now"/>: regenerated from <see cref="Stamp"/> up to the cap, never below <see cref="Value"/>.</summary>
    public readonly float At(long now) =>
        Math.Max(Value, Math.Min(Limit, Value + RegenPerSecond * (now - Stamp) / TimeSpan.TicksPerSecond));

    /// <summary>A new value (clamped to 0..<see cref="Max"/>) taken at <paramref name="now"/>.</summary>
    public void Set(float value, long now)
    {
        Value = Math.Clamp(value, 0, Max);
        Stamp = now;
    }

    /// <summary>A new maximum and regeneration, the bar full at <paramref name="now"/>.</summary>
    public void Fill(float max, float regenPerSecond, long now)
    {
        Max = max;
        RegenPerSecond = regenPerSecond;
        Set(max, now);
    }
}
