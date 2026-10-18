namespace AlbionLocal.Server.Data;

/// <summary>
/// The client's own random generator a7u (a7o, 0x18050F710..0x180510400): a plain MT19937, which stage 4b's placement
/// (<see cref="ResourcePlacement"/>) and the dynamic-template roll (<see cref="DynamicTemplates"/>) must reproduce draw for
/// draw (analysis/work/stage4b/SPEC.md §4.2; reference port/placement.py A7u). Not System.Random.
/// </summary>
public sealed class ClientRandom
{
    private const int N = 624;
    private readonly uint[] _mt = new uint[N];
    private int _mti;

    /// <summary>ctor(int) 0x18050FAE0 → a(uint) 0x18050FC50: init_genrand with the seed as an unsigned 32-bit value.</summary>
    public ClientRandom(int seed)
    {
        _mt[0] = unchecked((uint)seed);
        for (var i = 1; i < N; i++)
            _mt[i] = unchecked(1812433253u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i);
        _mti = N;
    }

    /// <summary>Draws so far (diagnostics; the port's rng_draws).</summary>
    public int Draws { get; private set; }

    /// <summary>f() 0x180510100: the standard twist (offsets 397 / -227 / 396) and tempering.</summary>
    public uint NextUInt()
    {
        if (_mti >= N)
        {
            int kk;
            uint y;
            for (kk = 0; kk < N - 397; kk++)
            {
                y = (_mt[kk] & 0x80000000u) | (_mt[kk + 1] & 0x7FFFFFFFu);
                _mt[kk] = _mt[kk + 397] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908B0DFu : 0);
            }
            for (; kk < N - 1; kk++)
            {
                y = (_mt[kk] & 0x80000000u) | (_mt[kk + 1] & 0x7FFFFFFFu);
                _mt[kk] = _mt[kk - 227] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908B0DFu : 0);
            }
            y = (_mt[N - 1] & 0x80000000u) | (_mt[0] & 0x7FFFFFFFu);
            _mt[N - 1] = _mt[396] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908B0DFu : 0);
            _mti = 0;
        }
        var r = _mt[_mti++];
        Draws++;
        r ^= r >> 11;
        r ^= (r << 7) & 0x9D2C5680u;
        r ^= (r << 15) & 0xEFC60000u;
        r ^= r >> 18;
        return r;
    }

    /// <summary>Next(int) 0x18050F760: floor((hi - lo) × u × 2^-32 + lo) over [min(n, 0), max(n, 0)); Next(0) still draws.</summary>
    public int Next(int maxExclusive)
    {
        double lo = Math.Min(maxExclusive, 0), hi = Math.Max(maxExclusive, 0);
        var u = NextUInt();
        return (int)Math.Floor((hi - lo) * u * 2.3283064365386963e-10 + lo);
    }

    /// <summary>NextDouble() 0x18050F710 (interface slot 3): one draw × 2^-32 (32-bit resolution).</summary>
    public double NextDouble() => NextUInt() * 2.3283064365386963e-10;
}

/// <summary>
/// a7m, the client's fixed-point number (4 decimals, raw long × 10000), and its weighted pick a7p.a(IList&lt;a7m&gt;)
/// 0x1809447D0 (SPEC §4.3 step 7), which the dynamic-template roll uses.
/// </summary>
public static class A7m
{
    /// <summary>1.0 raw.</summary>
    public const long One = 10000;

    /// <summary>a7m.e(double) 0x180943590: clamped, round-half-even(v × 10000).</summary>
    public static long FromDouble(double v)
    {
        v = Math.Clamp(v, -922337203685477.0, 922337203685477.0);
        return (long)Math.Round(v * 10000.0, MidpointRounding.ToEven);
    }

    /// <summary>a7m.q(a7m, a7m) 0x180944400: saturating a - b.</summary>
    public static long Subtract(long a, long b)
    {
        var nb = b == long.MinValue ? long.MaxValue : -b;
        var r = unchecked(a + nb);
        if (a > 0 && nb > 0 && r < 0) return long.MaxValue;
        if (a < 0 && nb < 0 && r >= 0) return long.MinValue;
        return r;
    }

    /// <summary>An a7m attribute (s.b(XmlReader, string, a7m)): a decimal string; missing, blank or unreadable = <paramref name="fallback"/>.</summary>
    public static long Parse(string? text, long fallback) =>
        string.IsNullOrWhiteSpace(text) || !double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? fallback : FromDouble(v);

    /// <summary>
    /// a7p.a 0x1809447D0: the total of the positive weights accumulated as a float, target = a7m((float)NextDouble() ×
    /// total), then from the last element down each positive weight is subtracted from a7m(total) until it is ≤ target.
    /// -1 when the list is empty, the total is over 2^31 or nothing is hit.
    /// </summary>
    public static int WeightedIndex(ClientRandom rng, IReadOnlyList<long> weights)
    {
        if (weights.Count == 0) return -1;
        var total = 0f;
        foreach (var w in weights)
            if (w > 0) total += (float)(w / 10000.0);
        if (total > 2147483648f) return -1;
        var d = rng.NextDouble();
        var target = FromDouble((float)d * total);
        var acc = FromDouble(total);
        for (var i = weights.Count - 1; i >= 0; i--)
        {
            if (weights[i] <= 0) continue;
            acc = Subtract(acc, weights[i]);
            if (acc <= target) return i;
        }
        return -1;
    }
}

/// <summary>
/// ces&lt;T&gt; (0x18295E400..0x182962860): a weighted collection sharing one <see cref="ClientRandom"/>. A collection of one
/// is picked without a draw; a pick removes the element (swap with the last) only when the predicate says so.
/// </summary>
public sealed class WeightedPicks<T>(ClientRandom rng) where T : class
{
    private readonly List<T> _items = [];
    private readonly List<long> _weights = [];

    /// <summary>ctor(a7u, IEnumerable&lt;KVP&gt;) 0x18295EF60: weights ≤ 0 are skipped.</summary>
    public WeightedPicks(ClientRandom rng, IEnumerable<(T Item, long Weight)> pairs) : this(rng)
    {
        foreach (var (item, w) in pairs)
            if (w > 0) Add(item, w);
    }

    public int Count => _items.Count;

    /// <summary>Add(T, a7m) 0x18295E700.</summary>
    public void Add(T item, long weight)
    {
        _items.Add(item);
        _weights.Add(weight);
    }

    /// <summary>c(predicate) 0x1829619D0: a weighted pick (no draw for one element); removed when <paramref name="remove"/> says so.</summary>
    public T? Pick(Func<T, bool>? remove = null)
    {
        if (_items.Count == 0) return null;
        if (_items.Count == 1)
        {
            var only = _items[0];
            if (remove?.Invoke(only) == true)
            {
                _items.RemoveAt(0);
                _weights.RemoveAt(0);
            }
            return only;
        }
        var i = A7m.WeightedIndex(rng, _weights);
        if (i < 0) throw new InvalidDataException("weighted pick found nothing (the client throws ArgumentOutOfRange)");
        var item = _items[i];
        if (remove?.Invoke(item) == true)
        {
            var last = _items.Count - 1;
            _items[i] = _items[last];
            _weights[i] = _weights[last];
            _items.RemoveAt(last);
            _weights.RemoveAt(last);
        }
        return item;
    }

    /// <summary>d() 0x182962300: a weighted pick that removes nothing.</summary>
    public T? Peek()
    {
        if (_items.Count == 0) return null;
        if (_items.Count == 1) return _items[0];
        var i = A7m.WeightedIndex(rng, _weights);
        return i < 0 ? null : _items[i];
    }
}
