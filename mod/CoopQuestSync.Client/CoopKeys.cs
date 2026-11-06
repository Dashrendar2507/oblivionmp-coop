using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.Sdk.Common.Input;

namespace CoopQuestSync.Client;

/// <summary>
/// The co-op keys (Alt + a letter): listen in or leave a conversation, revive the teammate next to you, respawn when
/// you are down. The letters come from the server (web page), so a key the game also uses can be moved.
///
/// The binds are polled, not "unhandled by UI": the game's own input takes the keys it uses before an unhandled bind
/// would see them. A polled bind also lets the game see the key, hence keys the game doesn't use.
/// </summary>
public sealed class CoopKeys(IInput input, Func<ClientRpc> rpc, ILogger logger)
{
    public static readonly Dictionary<string, string> Defaults = new() { ["follow"] = "L", ["revive"] = "H", ["respawn"] = "G" };

    private readonly Dictionary<string, (string Key, IDisposable? Bind)> _binds = new();
    private readonly Dictionary<string, DateTime> _lastPress = new();

    /// <summary>"Alt+L", for the overlay and the tags.</summary>
    public string Name(string action) => "Alt+" + (_binds.TryGetValue(action, out var b) ? b.Key : Defaults[action]);

    public void Start() => Apply(Defaults);

    /// <summary>The server's keys. json: { follow, revive, respawn }.</summary>
    public void Update(string json)
    {
        var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        Apply(keys);
    }

    private void Apply(Dictionary<string, string> keys)
    {
        foreach (var (action, letter) in keys)
        {
            if (!Defaults.ContainsKey(action) || _binds.TryGetValue(action, out var had) && had.Key == letter)
                continue;
            if (!Enum.TryParse<Key>(letter, true, out var key))
            {
                logger.LogWarning("[QuestSync] Unknown key {Key} for {Action}; keeping the old one.", letter, action);
                continue;
            }
            if (_binds.TryGetValue(action, out var old))
                old.Bind?.Dispose();
            var name = $"Alt+{letter} ({action})";
            var bind = input.RegisterKeyBind(ModifierKeys.Alt, key, () => Pressed(action, name), KeySource.Polled);
            _binds[action] = (letter, bind as IDisposable);
            logger.LogInformation("[QuestSync] {Name} bound.", name);
        }
    }

    /// <summary>A polled bind fires for as long as the keys are down: one press per half second, none while the chat
    /// or a menu has the keyboard.</summary>
    private void Pressed(string action, string name)
    {
        var now = DateTime.UtcNow;
        var repeat = _lastPress.TryGetValue(action, out var last) && now - last < TimeSpan.FromSeconds(0.5);
        _lastPress[action] = now;
        if (repeat || !input.CanApplyInput())
            return;
        logger.LogInformation("[QuestSync] {Key} pressed.", name);
        switch (action)
        {
            case "follow": rpc().SendToggleFollow(); break;
            case "revive": rpc().SendReviveNearest(); break;
            case "respawn": rpc().SendGiveUp(); break;
        }
    }
}
