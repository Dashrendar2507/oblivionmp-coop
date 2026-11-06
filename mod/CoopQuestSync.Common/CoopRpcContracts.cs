using ReadyM.Api.Multiplayer;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Common;

/// <summary>
/// Messages between the server and each player's game. JSON travels as a string, so the page in the client can take
/// it as is.
/// </summary>
[RpcContracts]
public static partial class CoopRpcContracts
{
    /// <summary>
    /// Someone else started a conversation. json: { talker, npc, frozen }. The witness overlay opens and shows it.
    /// </summary>
    [ServerToClient] public static partial void ConversationStarted(string json);

    /// <summary>One line of that conversation. json: { topic, lines: [..], speaker }.</summary>
    [ServerToClient] public static partial void ConversationLine(string json);

    /// <summary>
    /// The talker opened or closed a menu from the conversation. json: { activity } ("bartering", "persuading"...,
    /// or null when they are back at the topic list).
    /// </summary>
    [ServerToClient] public static partial void ConversationActivity(string json);

    /// <summary>The conversation is over: the overlay closes.</summary>
    [ServerToClient] public static partial void ConversationEnded();

    /// <summary>
    /// Every player's party status, for the tags over their heads. json: [{ name, talking, downed, activity }].
    /// </summary>
    [ServerToClient] public static partial void PartyStatus(string json);

    /// <summary>The co-op keys, Alt + these letters. json: { follow, revive, respawn }.</summary>
    [ServerToClient] public static partial void Keys(string json);

    /// <summary>
    /// Go into this interior (its editor id): the game's console runs <c>coc &lt;cell&gt;</c> through the UE4SS
    /// add-on. Used to bring a player back inside the building they left from.
    /// </summary>
    [ServerToClient] public static partial void GoToCell(string cell);

    /// <summary>Test: play one voice line now ("imperial_m ms26_ms26filecharges_000363be_1").</summary>
    [ServerToClient] public static partial void PlayVoice(string voice);

    /// <summary>A short message for the middle of the screen.</summary>
    [ServerToClient] public static partial void Notice(string text);

    /// <summary>The player was revived, or is respawned: the game brings them back to life.</summary>
    [ServerToClient] public static partial void Resurrect();

    /// <summary>The player pressed the revive key: the server looks for a downed player close by.</summary>
    [ClientToServer] public static partial void ReviveNearest();

    /// <summary>The follow key: leave the conversation the player is watching, or listen in on one they were offered.</summary>
    [ClientToServer] public static partial void ToggleFollow();

    /// <summary>A downed player gives up waiting and respawns next to the party.</summary>
    [ClientToServer] public static partial void GiveUp();
}
