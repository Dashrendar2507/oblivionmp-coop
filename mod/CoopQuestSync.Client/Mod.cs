using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

[ModEntry]
public sealed partial class Mod(ILogger logger)
{
    private void Init()
    {
        logger.LogInformation("[QuestSync] Client half loaded.");
    }
}
