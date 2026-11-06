# Phase 0 probe: how to test

This test answers two questions before we build the real quest sync:

1. When the game itself adds an item to a player (a console command or a script), does the server see it?
2. When the server adds an item to a player, does it show up in the game?

The real mod hides quest progress in invisible items, so both answers need to be "yes".

## Install

1. Copy the `CoopQuestSync.Probe` folder into your server's `mods/` folder, next to `OblivionMp.Sdk` and `OblivionMP.Chat`.
2. Restart the server. Its console should print `[QuestSyncProbe] Server half loaded.`
3. Join the server from the launcher.

## Test 1: game to server

1. In game, open the console (the `~` key) and type: `player.additem 0000000A 3` (3 lockpicks).
2. Close the console and wait 5 seconds.
3. In the server console, look for a line like `[QuestSyncProbe] ...: 0000000A x.. -> x..` or `NEW 0000000A`.
4. In chat, type `/qsprobe count 0000000A`. The server answers with the number of lockpicks it sees.

## Test 2: server to game

1. In chat, type `/qsprobe give 0000000A 2`.
2. Open your inventory: you should have 2 more lockpicks.
3. In chat, type `/qsprobe take 0000000A 2`, and check they are gone.

## Test 3: does quest progress survive a reconnect?

1. Pick up any quest in game the normal way (talk to someone, read a note) so it appears in your journal.
2. Disconnect, then join again.
3. Check whether the quest is still in your journal, at the same step.

## Send back

- What you saw for each test.
- The server console log (or its log file), and the client log. Lines starting with `[QuestSyncProbe]` are the ones I need.
