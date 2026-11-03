# Co-op Quest Sync 0.2 (token bridge spike): how to test

This build proves the whole bridge on two harmless side quests before we switch it on for every quest:

* **MS93** "The Ghost Ship of Anvil" (stages 10, 20, 100)
* **MS52** "Origin of the Gray Prince" (stages 10, 20, 30, 100, 115)

It has two parts:

* **The game plugin** `CoopQuestSync.esp`. When a stage is done in your game, it puts a hidden "token" item in your inventory. When a token for a stage appears that your game hasn't done, it sets that stage.
* **The server mod** `CoopQuestSync`. It reads the tokens, keeps the shared progress, and gives tokens to other players.

## Install

1. **Remove the old probe.** Delete `CoopQuestSync.Probe` from the server's `mods/` folder. The new mod includes the same `/qsprobe` command, and the two would clash.
2. **Server mod.** Copy the `CoopQuestSync` folder from the zip into the server's `mods/` folder.
3. **Game plugin.**
   1. In Steam, right-click Oblivion Remastered and choose Manage > Browse local files.
   2. Open `OblivionRemastered\Content\Dev\ObvData\Data\`.
   3. Copy `game-plugin\CoopQuestSync.esp` from the zip into that folder.
   4. In the same folder, open `Plugins.txt` in Notepad. Add `CoopQuestSync.esp` on a new line at the very end, then save.
4. Restart the server. Its console should show:
   `[QuestSync] Loaded 2 quests / 8 stages from the token map. Auto sync is OFF.`

## Test 1: the plugin loads

1. Join the server. Remember to use the launcher's Join button, which gives you a fresh ticket.
2. Within about 5 seconds, the server console should show
   `[QuestSync] PlayerId[1]: plugin found in load order slot XX`.
3. If it shows `no CoopQuestSync beacon in inventory yet` and nothing better after a minute, the game didn't load the plugin. Stop here and send me your `Plugins.txt` and the client log.

## Test 2: game to server

1. Open the console with the `~` key and type `setstage MS93 10`.
2. Your journal should get "The Ghost Ship of Anvil".
3. Within a few seconds, the server console should show
   `[QuestSync] PlayerId[1]: has MS93 10 (new)` and `Shared progress: MS93 10`.

## Test 3: server to game

1. Open chat with `K` and type `/qs push MS52 10`.
2. Chat should answer `Gave you the token for MS52 10...`.
3. Within a few seconds, the game should show a quest update, and "Origin of the Gray Prince" should be in your journal.

## Test 4: progress comes back after reconnecting

1. Leave the server, then join again from the launcher.
2. Normally OblivionMP forgets vanilla quest progress (we saw this in the last test). This time, both quests should come back into your journal within about 10 seconds of joining.
3. To check from chat, type `/qs mine`. It should list `MS52 (1)  MS93 (1)`.

## Things to look out for

Please note any of these:

* Did you see "Co-op quest sync added" pop up?
* Can you see any item called "Co-op quest sync" in your inventory? It should be hidden.
* Did the game stutter, crash or freeze at any point?

## Optional: with a second player

You can only run this if someone else (or a second PC) can join.

1. Both players join. One of you types `/qs auto on`.
2. Player A types `setstage MS93 20` in the console.
3. Player B should get the MS93 update within a few seconds.

## What to send me

* The server console lines that start with `[QuestSync]`.
* Which tests passed, and anything from the list above.

## Chat commands

| Command | What it does |
| --- | --- |
| `/qs status` | Shows whether auto sync is on, and which players the server sees the plugin for |
| `/qs mine [quest]` | Lists the stages the server sees in your inventory |
| `/qs shared [quest]` | Lists the shared progress |
| `/qs push <quest> <stage>` | Gives yourself the token for one stage |
| `/qs auto on\|off` | Turns automatic sync between players on or off |
| `/qs reset` | Clears the shared progress |
