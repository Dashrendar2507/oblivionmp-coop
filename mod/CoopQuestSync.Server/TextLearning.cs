using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReadyM.Api.Idents;
using ReadyM.SDK.Server.Events;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Gets the game's text (quest and NPC names, journal entries, dialogue) from the players' own games, so the server
/// has it without reading the game's files, which the remaster keeps Oodle-compressed (the Steam version ships no
/// oo2core dll, and the mod never downloads anything).
///
/// Each player's client says which language their game runs in. For every language the server is missing text in,
/// it asks one player of that language at a time for a batch of keys; their CoopQuestSyncUI add-on looks them up in
/// the running game and the client sends the text back. The server keeps it per language, in
/// coop-quest-sync-strings.&lt;language&gt;.json next to the state file, so it's only asked once per language. A host's
/// own coop-quest-sync-strings.json (made by hand, in config Language) still comes first for that language.
///
/// Only keys the server asked for are taken, the first answer for a key wins, and nothing a player sends ever
/// replaces the host's file. Config LearnTextFromPlayers (on) turns it off.
/// </summary>
[Service]
public sealed partial class TextLearning(IServerEvents events, QuestTokenSync sync, GameData data, CoopRpc rpc,
    QuestSyncConfig config, ILogger logger)
{
    /// <summary>Keys per request. The add-on looks up 100 per game tick (4 a second), so a batch takes about half a second.</summary>
    private const int BatchSize = 200;
    /// <summary>A player who hasn't answered a batch by then is skipped for the rest of their session.</summary>
    private const float AnswerSeconds = 45f;
    /// <summary>Save the learned text every this many batches (and when a language is complete, and at shutdown).</summary>
    private const int SaveEveryBatches = 20;

    private sealed record Ask(PlayerId Player, string Language, HashSet<string> Keys, float SentAt);

    private readonly Dictionary<string, Ask> _asking = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<PlayerId> _gaveUpOn = new();
    private readonly Dictionary<PlayerId, (int Count, float At)> _pinged = new();
    private readonly Dictionary<string, int> _batches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastPercent = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _complete = new(StringComparer.OrdinalIgnoreCase);
    private float _clock;
    private float _elapsed;

    private void Start()
    {
        rpc.TextLanguageReported += OnLanguage;
        rpc.TextFoundReceived += OnFound;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
    }

    private void Stop()
    {
        rpc.TextLanguageReported -= OnLanguage;
        rpc.TextFoundReceived -= OnFound;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        data.SaveLearned();
    }

    private void OnPlayerDisconnected(Player gone, Guid _, LiteNetLib.DisconnectReason __)
    {
        var player = gone.PlayerId;
        _gaveUpOn.Remove(player);
        _pinged.Remove(player);
        foreach (var (language, ask) in _asking.ToList())
            if (ask.Player == player)
                _asking.Remove(language);
        data.SaveLearned();
    }

    private void OnLanguage(PlayerId player, string language)
    {
        language = language.Trim();
        if (!GameData.IsLanguage(language))
        {
            logger.LogInformation("[QuestSync] {Player}'s game reported the language \"{Language}\", which isn't one the game uses.",
                sync.Nickname(player), language.Length > 20 ? language[..20] : language);
            return;
        }
        var view = sync.View(player);
        if (view.Language == language)
            return;
        view.Language = language;
        logger.LogInformation("[QuestSync] {Player}'s game runs in {Language}. The server has {Missing} of the mod's {Wanted} texts still to learn in it.",
            sync.Nickname(player), language, data.MissingText(language).Count, data.UsedKeys.Count);
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < 1f)
            return;
        _elapsed = 0;
        if (!config.LearnTextFromPlayers)
            return;

        foreach (var (language, ask) in _asking.ToList())
            if (_clock - ask.SentAt > AnswerSeconds)
            {
                _asking.Remove(language);
                _gaveUpOn.Add(ask.Player);
                logger.LogWarning("[QuestSync] {Player}'s game didn't send the text asked for in {Seconds} s (is the CoopQuestSyncUI add-on running?). " +
                    "Asking another player of that language, if any.", sync.Nickname(ask.Player), (int)AnswerSeconds);
            }

        foreach (var (player, view) in sync.Players)
        {
            // Only once they are in the game and caught up: the join is the busiest moment for their game.
            if (view.Phase != QuestTokenSync.Phase.Ready || view.CatchingUp || _gaveUpOn.Contains(player))
                continue;
            if (view.Language is null)
            {
                // Ask which language their game runs in (an empty request), a few times.
                var (count, at) = _pinged.GetValueOrDefault(player);
                if (count < 3 && (count == 0 || _clock - at > 30f))
                {
                    _pinged[player] = (count + 1, _clock);
                    rpc.SendTextWanted(player, "");
                }
                continue;
            }
            var language = view.Language;
            if (_asking.Keys.Any(l => GameData.SameLanguage(l, language)) || _asking.Values.Any(a => a.Player == player))
                continue;
            var missing = data.MissingText(language);
            if (missing.Count == 0)
            {
                if (_complete.Add(language) && _batches.ContainsKey(language))
                {
                    data.SaveLearned();
                    var (answered, found) = data.Learned(language);
                    logger.LogInformation("[QuestSync] The game's text in {Language} is complete: {Found} of {Answered} texts, kept in {Path}.",
                        language, found, answered, GameData.LearnedPath(language));
                }
                continue;
            }
            _complete.Remove(language);
            var batch = missing.Take(BatchSize).ToList();
            if (!_batches.ContainsKey(language))
                logger.LogInformation("[QuestSync] Learning the game's text in {Language} from {Player}'s game: {Missing} texts, {Batch} at a time.",
                    language, sync.Nickname(player), missing.Count, BatchSize);
            _asking[language] = new Ask(player, language, new HashSet<string>(batch, StringComparer.Ordinal), _clock);
            rpc.SendTextWanted(player, string.Join('\n', batch));
        }
    }

    private void OnFound(PlayerId player, string json)
    {
        var ask = _asking.Values.FirstOrDefault(a => a.Player == player);
        if (ask is null)
            return;
        _asking.Remove(ask.Language);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var language = root.TryGetProperty("language", out var l) ? l.GetString() : null;
            if (language is null || !GameData.SameLanguage(language, ask.Language))
            {
                logger.LogWarning("[QuestSync] {Player}'s game answered in {Language}, but {Asked} was asked; ignored.",
                    sync.Nickname(player), language ?? "no language", ask.Language);
                return;
            }
            var entries = new List<KeyValuePair<string, string>>();
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object)
                foreach (var p in text.EnumerateObject())
                    if (ask.Keys.Contains(p.Name) && p.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = p.Value.GetString() ?? "";
                        entries.Add(new(p.Name, value.Length > 8000 ? value[..8000] : value));
                    }
            if (entries.Count == 0)
            {
                // Their game couldn't look anything up (no add-on, or it doesn't answer): try someone else.
                _gaveUpOn.Add(player);
                logger.LogWarning("[QuestSync] {Player}'s game couldn't look up the game's text{Why}. Asking another player of {Language}, if any.",
                    sync.Nickname(player), root.TryGetProperty("error", out var e) ? $" ({e.GetString()})" : "", ask.Language);
                return;
            }
            data.Learn(ask.Language, entries);
            _elapsed = 1f; // the next batch on the next tick
            var batches = _batches[ask.Language] = _batches.GetValueOrDefault(ask.Language) + 1;
            if (batches % SaveEveryBatches == 0)
                data.SaveLearned();
            var wanted = data.UsedKeys.Count;
            var left = data.MissingText(ask.Language).Count;
            var percent = wanted == 0 ? 100 : (wanted - left) * 100 / wanted;
            if (percent / 10 != _lastPercent.GetValueOrDefault(ask.Language, -1) / 10 && left > 0)
                logger.LogInformation("[QuestSync] The game's text in {Language}: {Percent}% ({Left} still to learn).", ask.Language, percent, left);
            _lastPercent[ask.Language] = percent;
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't read the text {Player}'s game sent: {Error}", sync.Nickname(player), e.Message);
        }
    }
}
