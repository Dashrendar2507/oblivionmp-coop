using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// /coop test: lets one player try the co-op features alone, with a made-up teammate ("Ghost").
///   /coop test talk [ask]     - Ghost talks to an NPC ("ask": you are offered it, follow key to listen in): you get the overlay (and the freeze, per the settings), with a
///                               pause while Ghost barters and another while Ghost persuades
///   /coop test stop           - end that conversation now
///   /coop test stage Q N      - Ghost reached stage N of quest Q: you get it live, with the live effect switches
///   /coop test effects Q      - list the stages of quest Q that have effects, and which
///   /coop test revive         - Ghost revives you while you are down (die first: console "kill")
///   /coop test voice [voice line] - play a voice line now (a random one, or e.g. "imperial_m ms26_ms26filecharges_000363be_1")
///   /coop test pin / back     - remember where you stand, then go back there as a join would (into a building too).
///                               During /coop test talk, the join key travels to the pinned spot, as if Ghost stood there
/// /coop calm [player | all | near]: stop fights that went wrong (your area; a player's area and bounty; every area;
/// the NPC closest to you). See CombatService.
///   /coop test goto           - /coop goto's checks and cooldown, then the trip to the pinned spot, as if Ghost stood there
///   /coop test askme          - Ghost asks to come to you (as if you had typed /coop goto lock): Alt+Y or /coop accept answers
///   /coop test status         - what the server knows about you: phase, area, talking, menu, frozen, switches
///   /coop test asghost on|off - the server treats you like Ghost, a player who isn't an admin, so you can see the
///                               admin commands refused without a second player (open to everyone)
/// /coop goto [name | lock | unlock] and /coop accept [name]: travel to a teammate at any time (see <see cref="TeleportService"/>).
/// /coop calm and /coop test are for admins only (see <see cref="CommandAccess"/>); /coop follow, join, revive, giveup,
/// goto and accept are for everyone.
/// </summary>
[Service]
public sealed partial class TestCommands(IChat chat, IEntities entities, QuestTokenSync sync, GameData data,
    ConversationRelay talks, PartyService party, ReturnOnJoin returns, CombatService combat, CoopRpc rpc, CommandAccess access,
    TeleportService teleport)
{
    private const string Ghost = "Ghost";
    private const string Usage = "/coop follow | join | revive | giveup | goto [name | lock | unlock] | accept [name], /coop calm [player | all | near], or /coop test talk [ask] | stop | stage <quest> <stage> | effects <quest> | revive | voice | pin | back | goto | askme | status | asghost on|off";

    private void Start() => chat.AddCommand("coop", Usage, Run);

    private void Run(IChat c, PlayerId sender, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && parts[0].Equals("calm", StringComparison.OrdinalIgnoreCase))
        {
            // Stops fights and can clear a bounty, so it is for admins.
            if (!access.Allow(sender, "/coop calm"))
                return;
            var result = combat.Calm(sender, string.Join(' ', parts.Skip(1)));
            Reply(sender, result);
            return;
        }
        // Travel to a teammate at any time (open to everyone, see TeleportService).
        if (parts.Length >= 1 && parts[0].ToLowerInvariant() is "goto" or "accept")
        {
            var rest = string.Join(' ', parts.Skip(1));
            if (parts[0].Equals("goto", StringComparison.OrdinalIgnoreCase))
                teleport.Goto(sender, rest);
            else
                teleport.Accept(sender, rest);
            return;
        }
        // Same as the keys, for when a key doesn't get through.
        if (parts.Length == 1)
            switch (parts[0].ToLowerInvariant())
            {
                case "follow": talks.ToggleFollow(sender); return;
                case "join": talks.Join(sender); return;
                case "revive": party.ReviveNearest(sender); return;
                case "giveup": party.GiveUp(sender); return;
            }
        if (parts.Length < 2 || !parts[0].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            Reply(sender, Usage);
            return;
        }
        // Open to everyone: it can only take rights away from yourself, and "off" must work while you have none.
        if (parts[1].Equals("asghost", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 3 && parts[2] is "on" or "off")
                Reply(sender, access.SetActAsGhost(sender, parts[2] == "on"));
            else
                Reply(sender, "/coop test asghost on|off");
            return;
        }
        // follow, revive and giveup above are for everyone; the test tools are for admins.
        if (!access.Allow(sender, "/coop test"))
            return;
        if (!entities.TryLookup(sender, out MainCharacter me))
            return;
        switch (parts[1].ToLowerInvariant())
        {
            case "talk":
                if (sync.Settings.ConversationOverlay == "off")
                    Reply(sender, "\"Show others' conversations\" is off on the page; turn it on first.");
                else
                {
                    var ask = parts.Length > 2 && parts[2].Equals("ask", StringComparison.OrdinalIgnoreCase);
                    talks.StartGhost(me, Ghost, ask);
                    Reply(sender, "Ghost is talking to an NPC for about 40 s, and stops to barter and to persuade on the way. "
                        + (ask ? $"Press {sync.Settings.Key("follow")} to listen in, and again to leave. " : $"{sync.Settings.Key("follow")} leaves, and again comes back. ")
                        + "/coop test stop ends it.");
                }
                break;

            case "stop":
                Reply(sender, talks.StopGhost(sender) ? "Conversation ended." : "No test conversation is running.");
                break;

            case "stage" when parts.Length == 4 && int.TryParse(parts[3], out var stageNo):
            {
                if (!sync.Map.TryNormalizeQuest(parts[2], out var quest) || !sync.Map.TryGetToken(new QuestStage(quest, stageNo), out _))
                {
                    Reply(sender, $"No such stage: {parts[2]} {parts[3]}. Use the quest id from the web page, like MS93.");
                    break;
                }
                var stage = new QuestStage(quest, stageNo);
                if (!sync.Players.TryGetValue(sender, out var view) || view.Phase != QuestTokenSync.Phase.Ready || view.CatchingUp)
                {
                    Reply(sender, "Wait until the server shows you as \"in sync\" on the page, then try again.");
                    break;
                }
                if (view.Stages.Contains(stage))
                {
                    Reply(sender, $"You already have {stage}. Pick a stage you don't have yet.");
                    break;
                }
                var effects = sync.Map.Quest(quest)?.StageInfos.FirstOrDefault(s => s.Stage == stageNo)?.Effects ?? [];
                sync.PushOne(me, stage);
                var on = effects.Where(e => sync.Settings.Allowed("live", e)).ToList();
                var off = effects.Except(on).ToList();
                Reply(sender, $"{Ghost} advanced {data.QuestName(quest)} to stage {stageNo}."
                    + (effects.Count == 0 ? " This stage has no effects on the player."
                        : $" Effects you should get: {(on.Count > 0 ? string.Join(", ", on) : "none")}. Switched off: {(off.Count > 0 ? string.Join(", ", off) : "none")}."));
                break;
            }

            case "effects" when parts.Length == 3:
            {
                if (!sync.Map.TryNormalizeQuest(parts[2], out var quest) || sync.Map.Quest(quest) is not { } info)
                {
                    Reply(sender, $"Unknown quest {parts[2]}.");
                    break;
                }
                var with = info.StageInfos.Where(s => s.Effects.Count > 0).Select(s => $"{s.Stage}: {string.Join("/", s.Effects)}").ToList();
                Reply(sender, with.Count == 0 ? $"{quest}: no stage has effects on the player." : $"{quest}: " + string.Join("  ", with));
                break;
            }

            case "revive":
                Reply(sender, party.TestRevive(sender, Ghost) ? "Ghost revived you." : "You are not down. Die first (console: kill), with \"When a player dies\" set to revive.");
                break;

            case "voice":
            {
                string? voice = null;
                string? text = null;
                if (parts.Length >= 4)
                    voice = $"{parts[2]} {parts[3]}".ToLowerInvariant();
                else if (data.SampleLines(1) is [var line])
                {
                    var speaker = data.VoiceOfName(line.Speaker);
                    var spoken = GameData.VoiceLine(line, line.Lines[0]);
                    if (speaker is not null && spoken is not null)
                    {
                        voice = $"{speaker} {spoken}";
                        text = $"{data.Name(line.Speaker)}: \"{data.Text(line.Lines[0])}\"";
                    }
                }
                if (voice is null || !System.Text.RegularExpressions.Regex.IsMatch(voice, "^[a-z0-9_]{1,40} [a-z0-9_]{1,120}$"))
                {
                    Reply(sender, "Usage: /coop test voice, or /coop test voice imperial_m ms26_ms26filecharges_000363be_1");
                    break;
                }
                rpc.SendPlayVoice(sender, voice);
                Reply(sender, $"Playing {voice}{(text is null ? "" : " - " + text)}. If you hear nothing, send UE4SS.log.");
                break;
            }

            case "pin":
                Reply(sender, returns.TestPin(sender));
                break;

            case "back":
                Reply(sender, returns.TestBack(sender));
                break;

            case "goto":
                Reply(sender, teleport.TestGoto(sender));
                break;

            case "askme":
                Reply(sender, teleport.TestAskMe(sender));
                break;

            case "status":
            {
                if (!sync.Players.TryGetValue(sender, out var view))
                    break;
                var phase = view.Phase != QuestTokenSync.Phase.Ready ? "rejoin" : view.CatchingUp ? "join" : "live";
                var allowed = CoopSettings.Categories.Where(cat => sync.Settings.Allowed(phase, cat));
                Reply(sender, $"Phase {view.Phase}{(view.CatchingUp ? " (catching up)" : "")}, area {(view.Area is { } area ? GameData.AreaKey(area) : "unknown")}, "
                    + $"talking {view.InDialogue}, menu {talks.Activity(sender) ?? "none"}, in combat {view.InCombat}, frozen {view.WantFrozen}. "
                    + $"Effect switches now ({phase}): {string.Join(", ", allowed)}. Dialogue text loaded: {(data.HasText ? "yes" : "no")}. "
                    + returns.Describe(sender) + " " + teleport.Describe(sender) + " " + combat.Describe());
                break;
            }

            default:
                Reply(sender, Usage);
                break;
        }
    }

    private void Reply(PlayerId player, string text) => chat.SendTo(player, text, ChatLineKind.CommandReply);
}
