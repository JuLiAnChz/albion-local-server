using System.Globalization;
using System.Xml.Linq;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;

namespace AlbionLocal.Server.Data;

/// <summary>The deliberately bounded opening: first quest, then the tower up to its boat-search objective.
/// Coordinates, radii, time requirements and client ids come from this installation, not screenshot pixels.</summary>
public sealed class OpeningTutorialData
{
    public const string FirstQuest = "MISSIONS_T2_TUTORIAL_TEST_A";
    public const string TowerQuest = "MISSIONS_T2_TUTORIAL_TEST_B";
    public sealed record Task(int Index, string Kind, string Marker, double Seconds, string? TutorialState);
    public sealed record Quest(int Id, string Name, IReadOnlyList<Task> Tasks, IReadOnlyList<string> Variables);
    public required TutorialData Tutorial { get; init; }
    public required ClusterTemplate Map { get; init; }
    public required CriticalMember Start { get; init; }
    public required CriticalMember Crafter { get; init; }
    public required int CrafterType { get; init; }
    public required Quest First { get; init; }
    public required Quest Tower { get; init; }
    public IReadOnlyList<LocationMarkerSpawn> Markers(string name) => Map.LocationMarkers.Where(m => m.LocationId == name).ToArray();

    public static OpeningTutorialData Load(string folder, ClusterStore store)
    {
        var tutorial = TutorialData.Load(GameDataFile.Open(folder, "tutorial.bin").Root());
        var map = store.Get(tutorial.StartCluster);
        var starts = map.Critical.Where(c => c.Type == "startpoint").ToArray();
        var crafters = map.Critical.Where(c => c.Type == "questgiverobject"
            && (string?)c.Component.Attribute("reference") == "WOUNDEDCRAFTER").ToArray();
        if (starts.Length != 1 || crafters.Length != 1 || map.Problems.Count != 0)
            throw new InvalidDataException("Opening tutorial requires one startpoint, one wounded crafter and a resolved map.");
        var types = GameDataFile.Open(folder, "questgiverobjects.bin").Root().Elements("questgiverobject").ToArray();
        var npcType = Array.FindIndex(types, e => (string?)e.Attribute("uniquename") == "WOUNDEDCRAFTER");
        if (npcType < 0 || crafters[0].Guid is not { } guid || guid == Guid.Empty)
            throw new InvalidDataException("Opening tutorial crafter type/GUID is missing.");
        // beu.da resets bog's counter; bog.o3 counts quest and playerfactionquest together, from zero.
        var quests = GameDataFile.Open(folder, "quests.bin").Root().Elements()
            .Where(e => e.Name.LocalName is "quest" or "playerfactionquest").ToArray();
        Quest Read(string name, int limit)
        {
            var id = Array.FindIndex(quests, e => (string?)e.Attribute("uniquename") == name);
            if (id < 0) throw new InvalidDataException($"Missing opening quest {name}.");
            var xml = quests[id];
            var tasks = xml.Element("tasks")?.Elements().Take(limit).Select((e, i) =>
            {
                var mission = e.Name.LocalName == "missionstep" ? e.Elements().Single() : e.Element("missions")?.Elements().Single();
                if (mission is null || mission.Name.LocalName is not ("gotolocation" or "gotoquestgiver"))
                    throw new InvalidDataException($"{name} task {i}: unsupported opening condition.");
                var marker = (string?)mission.Attribute("locationmarkerid") ?? throw new InvalidDataException($"{name}: missing marker.");
                if (!map.LocationMarkers.Any(m => m.LocationId == marker && m.Radius > 0 && float.IsFinite(m.Radius)))
                    throw new InvalidDataException($"{name}: unresolved marker {marker}.");
                var seconds = mission.Attribute("seconds") is { } secondsAttr
                    ? double.Parse(secondsAttr.Value, CultureInfo.InvariantCulture) : 0;
                if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException($"{name}: invalid seconds.");
                var state = (string?)e.Element("preactions")?.Element("settutorialstate")?.Attribute("state");
                if (state is not null && tutorial.Find(state) is null) throw new InvalidDataException($"Unknown tutorial state {state}.");
                return new Task(i, mission.Name.LocalName, marker, seconds, state);
            }).ToArray() ?? [];
            if (tasks.Length != limit) throw new InvalidDataException($"{name}: opening tasks missing.");
            var variables = xml.Element("variables")?.Elements().Select(e => e.Name.LocalName).ToArray() ?? [];
            if (variables.Any(v => v is not ("dynamiccluster" or "dynamicquestgiver")))
                throw new InvalidDataException($"{name}: unsupported opening variables.");
            if (xml.Element("rewards") is not { } rewards || rewards.HasElements)
                throw new InvalidDataException($"{name}: reward definitions differ from this bounded opening.");
            return new Quest(id, name, Array.AsReadOnly(tasks), Array.AsReadOnly(variables));
        }
        var first = Read(FirstQuest, 4);
        var tower = Read(TowerQuest, 3);
        if (first.Tasks[^1].Kind != "gotoquestgiver" || tower.Tasks.Any(t => t.Kind != "gotolocation")
            || (string?)quests[tower.Id].Attribute("previousquests") != FirstQuest)
            throw new InvalidDataException("Opening quest chain differs from the inspected version.");
        return new OpeningTutorialData { Tutorial = tutorial, Map = map, Start = starts[0], Crafter = crafters[0],
            CrafterType = npcType, First = first, Tower = tower };
    }

    public Character Create(string name, ItemStore items, GameData board)
    {
        var c = new Character { Name = name, Cluster = Tutorial.StartCluster, PositionX = Start.Position.X,
            PositionY = Start.Position.Y, Direction = Start.Direction, Progression = board.Board?.FairStart()
                ?? throw new InvalidDataException("Tutorial requires destiny board data."),
            Tutorial = new OpeningTutorialProgress(0, Guid.NewGuid(), DateTime.UtcNow.Ticks) };
        foreach (var itemName in Tutorial.StartingItems)
        {
            var item = items.Find(itemName) ?? throw new InvalidDataException($"Missing tutorial item {itemName}.");
            var slot = Array.FindIndex(Inventory.SlotNames, s => s.Equals(item.Slot, StringComparison.OrdinalIgnoreCase));
            if (slot < 0 || c.Equipment[slot] is not null) throw new InvalidDataException($"Invalid tutorial slot for {itemName}.");
            c.Equipment[slot] = new InventoryItem(itemName);
        }
        return c;
    }
}
