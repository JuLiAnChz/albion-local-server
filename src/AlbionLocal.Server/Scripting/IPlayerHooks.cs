using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server.Scripting;

/// <summary>
/// Player hooks (TC PlayerScript, ARCHITECTURE.md §6.7): every class implementing it that <see cref="ScriptRegistry"/>
/// finds gets one instance, called in the order the registry found them. The methods have empty defaults, so a class
/// implements only what it needs. Explicit calls (visible in the debugger), not an event bus; they run in the world
/// thread under World.Sync. An exception is logged and the server goes on. What a hook changes in the player's items is
/// saved after OnLogin, OnLogout and OnItemAdded; a change made anywhere else needs <see cref="Player.SaveItems"/>.
/// More hooks (OnEnterMap, OnKill) come with their systems.
/// </summary>
public interface IPlayerHooks
{
    /// <summary>
    /// The Join is complete (after JoinFinished): the player is on its map and the client has it. <paramref name="first"/>
    /// = the character's first Join (TC firstLogin), as far as this server run can tell without a saved flag: true for a
    /// character created since the server started, on its first Join; false for every other Join (a later login, a
    /// reconnect, a character created in an earlier run).
    /// </summary>
    void OnLogin(Player p, bool first) { }

    /// <summary>
    /// The player leaves the world (logout, disconnect or timeout, or a new Join on the same connection), still on its
    /// map, before its character is saved. A new Join logs the connection's old player out before the new one logs in,
    /// so the hooks see logins and logouts in pairs.
    /// </summary>
    void OnLogout(Player p) { }

    /// <summary>A new item lies in the player's inventory (.item add; later loot, harvest, craft): after its events, before the save.</summary>
    void OnItemAdded(Player p, Item item) { }
}
