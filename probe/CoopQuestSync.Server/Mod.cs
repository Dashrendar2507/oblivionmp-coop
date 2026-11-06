using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

[ModEntry]
public sealed partial class Mod(ILogger logger)
{
    private void Init()
    {
        logger.LogInformation("[QuestSyncProbe] Server half loaded. Watching player inventories for plugin items.");
    }
}
