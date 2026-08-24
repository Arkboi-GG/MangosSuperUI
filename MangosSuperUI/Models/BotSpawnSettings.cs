namespace MangosSuperUI.Models;

/// <summary>
/// "BotSpawn" configuration section — limits for the Bot Monitor "Add Bots" spawner
/// (Services/BotSpawnService). Override in server-config.json like every other section.
/// </summary>
public class BotSpawnSettings
{
    /// <summary>
    /// Most bots a single Add Bots batch may request. 0 = unlimited. Whatever this says, a batch
    /// is also refused if it would exhaust the unused-name pool in wwwroot/data (the real ceiling).
    /// </summary>
    public int MaxPerRequest { get; set; } = 4000;
}
