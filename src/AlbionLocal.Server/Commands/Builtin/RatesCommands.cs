using System.Globalization;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The rate modifiers (<see cref="Rates"/>, config section Rates): .rates.</summary>
public static class RatesCommands
{
    /// <summary>
    /// Without <paramref name="name"/>: the rates in force. With a name and <paramref name="value"/> (Admin): sets that rate
    /// for the world (<see cref="World.Rates"/>), from the next quantity it computes on (a respawn or regrowth already
    /// scheduled keeps its time); <paramref name="save"/> "save" also writes the rates that differ from the config's to
    /// data/rates.json, which the next start reads over the config. ".rates reset [save]" goes back to the config's rates
    /// (save: the file is deleted).
    /// </summary>
    [Command("rates", Level = AccountLevel.GameMaster, Help = "shows the rates (fame, harvest, silver, loot, respawn, …); Admin: .rates <name> <value> [save], .rates reset [save]")]
    public static void Show(CommandContext ctx, string? name = null, string? value = null, string? save = null)
    {
        var world = ctx.World;
        if (name is null)
        {
            ctx.Reply($"rates: {world.Rates.Describe()}");
            return;
        }
        if (ctx.Level < AccountLevel.Admin) throw new CommandError("changing the rates needs Admin");
        var reset = name.Equals("reset", StringComparison.OrdinalIgnoreCase);
        if (reset) (save, value) = (value, null);
        if (save is not null && !save.Equals("save", StringComparison.OrdinalIgnoreCase))
            throw new CommandError($"'{save}': expected save or nothing");
        Rates rates;
        if (reset) rates = world.Config.Rates;
        else
        {
            if (Rates.Canonical(name) is not { } key) throw new CommandError($"'{name}' is not a rate ({string.Join(", ", Rates.Names)})");
            if (value is null) throw new CommandError($".rates {key} <value> [save]; now {world.Rates.Get(key)!.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new CommandError($"value: '{value}' is not a number");
            try { rates = world.Rates.With(key, v); }
            catch (ArgumentException e) { throw new CommandError(e.Message); }
        }
        world.Rates = rates;
        var saved = "";
        if (save is not null)
        {
            var path = Rates.FilePath(world.Config.SavesPath);
            try { rates.Save(path, world.Config.Rates); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new CommandError($"set, but not saved to {path}: {e.Message}"); }
            saved = rates == world.Config.Rates ? $"; {path} removed" : $"; saved to {path}";
        }
        ctx.Reply($"rates: {rates.Describe()}{saved}");
    }
}
