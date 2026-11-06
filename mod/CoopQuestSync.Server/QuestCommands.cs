using System.Text;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// /qs chat commands:
///   /qs status                - auto sync on/off, players and the plugin slot the server found for each
///   /qs mine [quest]          - the stages the server sees in your inventory (one quest, or a summary)
///   /qs shared [quest]        - the shared progress
///   /qs push &lt;quest&gt; &lt;stage&gt;  - give yourself the token for one stage; the plugin then sets it in your game
///   /qs auto on|off           - turn automatic sync between players on or off
///   /qs source &lt;name&gt;|all     - share only one player's progress, or everyone's
///   /qs reset                 - reset every shared quest (players lose them when they rejoin)
///   /qs whoami                - your ReadyM id, and whether you are an admin
/// push, auto, source and reset are for admins only (see <see cref="CommandAccess"/>).
/// </summary>
[Service]
public sealed partial class QuestCommands(IChat chat, IEntities entities, QuestTokenSync sync, CommandAccess access)
{
    private const string Usage = "/qs status | mine [quest] | shared [quest] | push <quest> <stage> | auto on|off | source <name>|all | reset | whoami";

    private void Start() => chat.AddCommand("qs", Usage, Run);

    private void Run(IChat c, PlayerId sender, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sub = parts.FirstOrDefault()?.ToLowerInvariant();
        if (sub is "push" or "auto" or "source" or "reset" && !access.Allow(sender, "/qs " + sub))
            return;
        switch (sub)
        {
            case "whoami":
                Reply(sender, access.Describe(sender));
                break;

            case "status":
                Reply(sender, $"Auto sync is {(sync.AutoSync ? "ON" : "OFF")}. Source: {sync.SourcePlayer ?? "everyone"}. Shared progress: {sync.Shared.Count} quest(s), {sync.Shared.Sum(kv => kv.Value.Count)} stage(s).");
                foreach (var (id, view) in sync.Players)
                    Reply(sender, view.ModIndex is { } slot
                        ? $"{sync.Nickname(id)}: plugin in slot {slot:X2}, {view.Phase}, {view.Stages.Count} stage token(s)"
                        : $"{sync.Nickname(id)}: plugin not seen yet");
                break;

            case "mine":
                if (!sync.Players.TryGetValue(sender, out var me) || me.ModIndex is null)
                {
                    Reply(sender, "The server has not seen the plugin in your inventory yet.");
                    break;
                }
                ReplyStages(sender, me.Stages.GroupBy(s => s.Quest).ToDictionary(g => g.Key, g => g.Select(s => s.Stage)), parts.ElementAtOrDefault(1));
                break;

            case "shared":
                ReplyStages(sender, sync.Shared.ToDictionary(kv => kv.Key, kv => (IEnumerable<int>)kv.Value), parts.ElementAtOrDefault(1));
                break;

            case "push" when parts.Length == 3 && int.TryParse(parts[2], out var stageNo):
                if (!sync.Map.TryNormalizeQuest(parts[1], out var quest) || !sync.Map.TryGetToken(new QuestStage(quest, stageNo), out _))
                {
                    Reply(sender, $"The plugin has no token for {parts[1]} {parts[2]}. Use the quest's editor id, like MS93, and one of its stages.");
                    break;
                }
                if (!entities.TryLookup(sender, out MainCharacter main) || !sync.PushOne(main, new QuestStage(quest, stageNo)))
                {
                    Reply(sender, "Could not give you the token: the server has not seen the plugin in your inventory yet, or is still restoring your progress.");
                    break;
                }
                Reply(sender, $"Gave you the token for {quest} {stageNo}. The plugin should set that stage within a few seconds.");
                break;

            case "auto" when parts.Length == 2 && parts[1] is "on" or "off":
                sync.SetAuto(parts[1] == "on");
                Reply(sender, $"Auto sync is now {(sync.AutoSync ? "ON" : "OFF")}.");
                break;

            case "source" when parts.Length >= 2:
                var who = string.Join(' ', parts.Skip(1));
                sync.SetSource(who.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : who);
                Reply(sender, sync.SourcePlayer is null
                    ? "Everyone's progress is shared."
                    : $"Only {sync.SourcePlayer}'s progress is shared now. Other players still get it, but what they do on their own stays in their game.");
                break;

            case "reset":
                sync.ClearShared();
                Reply(sender, "All shared quests reset. Each player loses them the next time they join.");
                break;

            default:
                Reply(sender, Usage);
                break;
        }
    }

    private void ReplyStages(PlayerId to, Dictionary<string, IEnumerable<int>> stages, string? quest)
    {
        if (quest is not null)
        {
            var hit = stages.FirstOrDefault(kv => kv.Key.Equals(quest, StringComparison.OrdinalIgnoreCase));
            Reply(to, hit.Key is null ? $"No stages for {quest}." : $"{hit.Key}: {string.Join(", ", hit.Value.Order())}");
            return;
        }
        if (stages.Count == 0)
        {
            Reply(to, "No stages yet.");
            return;
        }
        var line = new StringBuilder();
        foreach (var (q, s) in stages.OrderBy(kv => kv.Key))
        {
            var part = $"{q} ({s.Count()})  ";
            if (line.Length + part.Length > 200)
            {
                Reply(to, line.ToString());
                line.Clear();
            }
            line.Append(part);
        }
        Reply(to, line.ToString());
    }

    private void Reply(PlayerId player, string text) => chat.SendTo(player, text, ChatLineKind.CommandReply);
}
