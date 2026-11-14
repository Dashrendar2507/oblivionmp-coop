using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Events;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Events;

namespace CoopQuestSync.Server;

/// <summary>
/// Players can't hurt each other (setting FriendlyFire off). OblivionMP 1.1 reports every change of health to the server
/// as a HealthChangedEvent, with the attacker, before applying it; refusing it takes the change back. So a loss of
/// health that one player's character caused to another player's is refused. Healing a teammate still works, and
/// NPCs, creatures and summons hurt everyone as before.
/// </summary>
[Service]
public sealed partial class FriendlyFire(QuestTokenSync sync, ILogger logger)
{
    /// <summary>One log line per attacker and victim this often, not one per hit.</summary>
    private static readonly TimeSpan LogEvery = TimeSpan.FromSeconds(30);

    private readonly Dictionary<(PlayerId, PlayerId), DateTime> _logged = new();

    [EventHandler(Stage.Validate)]
    public void Check(in HealthChangedEvent e, EventContext ctx)
    {
        if (sync.Settings.FriendlyFire || e.Amount >= 0 || e.Attacker is not { } attacker)
            return;
        if (!attacker.TryAs(out MainCharacter from) || !e.Victim.TryAs(out MainCharacter to) || from.PlayerId == to.PlayerId)
            return;
        ctx.Cancel();
        var now = DateTime.UtcNow;
        if (_logged.TryGetValue((from.PlayerId, to.PlayerId), out var last) && now - last < LogEvery)
            return;
        _logged[(from.PlayerId, to.PlayerId)] = now;
        logger.LogInformation("[QuestSync] {Attacker} hit {Victim} for {Amount:0.#}: refused (friendly fire is off).",
            sync.Nickname(from.PlayerId), sync.Nickname(to.PlayerId), -e.Amount);
    }
}
