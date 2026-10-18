namespace AlbionLocal.Server.Data;

/// <summary>
/// The client id of a template that the wire carries only as a number (<see cref="ClientNumbering.ExpeditionAgents"/>,
/// <see cref="ClientNumbering.QuestGiverTypes"/>), by its name (case-sensitive, as the cluster data writes it). Immutable.
/// </summary>
public sealed class NameIndex
{
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public NameIndex(IEnumerable<(int Id, string Name)> numbered)
    {
        var names = new List<string>();
        foreach (var (id, name) in numbered)
        {
            names.Add(name);
            _byName.TryAdd(name, id); // a name twice: the first one, as a lookup by name in the client would find
        }
        Names = names;
    }

    /// <summary>The names in id order.</summary>
    public IReadOnlyList<string> Names { get; }

    public int Count => Names.Count;

    /// <summary>The id of <paramref name="name"/>; null = not in the file.</summary>
    public int? IdOf(string name) => _byName.TryGetValue(name, out var id) ? id : null;
}
