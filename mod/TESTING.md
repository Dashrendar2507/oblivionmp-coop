# Co-op Quest Sync 0.3: how to test

What's new since 0.2:

* **Every quest is covered**: all 263 Oblivion.esm quests that have stages. Character creation and the E3 demo quest are left out.
* **Auto sync is on**: a stage one player reaches is sent to every other player within a few seconds, and a player who joins gets everything they missed.
* **Silent catch-up**: a small UE4SS add-on, `CoopQuestSyncUI`, hides pop-ups while the plugin replays stages. You see one line, "Co-op: quest progress synced", instead of a pile of quest updates.
* **No double rewards**: when you rejoin, the plugin replays your own stages, which would hand out their rewards again. The server now puts your inventory back the way it was before the replay.
* **No teleports or cut scenes on join**: 14 stages that teleport you, play a video, open a menu or empty your inventory are synced but never replayed.

## Install (each player)

1. **Game plugin.** Copy `game-plugin\CoopQuestSync.esp` from the zip into the game's `OblivionRemastered\Content\Dev\ObvData\Data\` folder, replacing the old one.
   * Nicolas: your `Plugins.txt` already has the line.
   * Your friend: add `CoopQuestSync.esp` as the last line of `Plugins.txt` in the same folder.
2. **Pop-up add-on.**
   1. Press Win+R, paste `%AppData%\ReadyM.Launcher\Oblivion\Mods`, and press Enter.
   2. Copy the `ui\CoopQuestSyncUI` folder from the zip into that folder. You should end up with `...\Mods\CoopQuestSyncUI\Scripts\main.lua` and `...\Mods\CoopQuestSyncUI\enabled.txt`.

## Install (server, Nicolas only)

1. Replace the server's `mods\CoopQuestSync` folder with the one in the zip.
2. Leave `coop-quest-sync-state.json` in the server folder where it is. It holds the shared progress from the last test (MS93 and MS52).
3. Restart the server. Its console should show `Loaded 263 quests / 2438 stages ... Auto sync is ON`.

## Test 1: rejoin on your own

1. Join alone.
2. The server log should show:
   * `plugin found in load order slot XX. Starting handshake.`
   * `removed old test token` a few times (left over from 0.2).
   * Then `nothing to restore` or `restore finished`.
   * Then `pushed 2 shared stage(s)`.
3. In game you should see "Co-op: quest progress synced", but not a separate pop-up for each quest. MS93 and MS52 should be back in your journal.

## Test 2: no double rewards

1. Check how much gold you have.
2. In the console, type `setstage MS16 15`. That stage of a small side quest gives 50 gold.
3. Check your gold again. It should have gone up by 50.
4. Leave and rejoin. Your gold should be the same as before you left, not higher.
5. The server log should show `restoring ... stage(s) after joining`, then `undo replay reward 0000000F x50` (0000000F is gold), then `restore finished`.

## Test 3: two players

1. Your friend joins while you're in game.
2. They should get your progress without a pile of pop-ups: they see "Co-op: quest progress synced", and MS93, MS52 and MS16 are in their journal. They also get the 50 gold once, because MS16 15 is new for them.
3. Type `setstage MS93 20`. Within a few seconds your friend should see "Co-op: quest progress synced", and MS93 should move on in their journal.
4. Your friend advances any quest the normal way, by talking to someone or picking something up. You should get it too.
5. Type `/qs status` in chat. Both players should be listed as `Ready`.

## What to send me

* Server console lines that start with `[QuestSync]`.
* From each player's client log (the UE4SS log you sent before), the lines that start with `[CoopQuestSyncUI]`. They list every pop-up the add-on saw, and whether it hid it.
* Anything odd: pop-ups that got through, "Co-op quest token" showing up anywhere, teleports, stutter or crashes.

## If something goes wrong

* `/qs auto off` stops automatic sync. `/qs push <quest> <stage>` still works.
* To start the shared progress over, stop the server and delete `coop-quest-sync-state.json`, or type `/qs reset`.
* To turn off undoing rewards, put a `config.json` in the server's `mods\CoopQuestSync\server\` folder containing `{ "UndoReplayRewards": false }`.

## Chat commands

| Command | What it does |
| --- | --- |
| `/qs status` | Shows whether auto sync is on, and each player's state (Handshake, Restoring, Ready) |
| `/qs mine [quest]` | Lists the stages the server sees in your inventory |
| `/qs shared [quest]` | Lists the shared progress |
| `/qs push <quest> <stage>` | Gives yourself the token for one stage |
| `/qs auto on\|off` | Turns automatic sync between players on or off |
| `/qs reset` | Clears the shared progress |
