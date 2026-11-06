# Co-op Quest Sync 0.4: how to test

What's new since 0.3:

* **Quest admin web page**: while the server runs, open http://localhost:9060/ on the server PC. It shows:
  * every quest, with its journal entries;
  * which stages are shared and which each player has;
  * buttons to give a stage to everyone or reset a quest;
  * a switch for auto sync;
  * a choice of whose progress is shared: everyone's, or one player's.
* **The pop-up on a quick rejoin is fixed**: it was almost certainly the "Tutorial" quest. In the game that quest is the system of help pop-ups (the ones with a Continue button), and 0.3 replayed them. It now stays local to each player and is never shared or replayed.
* **More reliable catch-up**:
  * the plugin waits 4 seconds of gameplay after loading before it replays anything;
  * it checks every quest again every 30 seconds;
  * it no longer depends on timing to start the restore, which could have let a reward slip through twice.
* **"Source" player**: with `/qs source <name>` (or the drop-down on the page), only that player's progress is shared. Everyone else still gets it, but what they do on their own stays in their own game. `/qs source all` goes back to sharing everyone's.
* **Reset a quest**: with the page's "Reset this quest" button, the quest leaves the shared progress and its hidden tokens are taken from everyone, including players who join later. The game can't undo a quest while you're playing, so each player loses it the next time they join.

## Install

1. **Server:** replace the server's `mods\CoopQuestSync` folder with the one in the zip. Keep `coop-quest-sync-state.json`.
2. **Each player:** replace `CoopQuestSync.esp` in the game's `Data` folder with `game-plugin\CoopQuestSync.esp` from the zip.
3. The pop-up add-on hasn't changed, so if you installed it for 0.3 you can skip this step. Otherwise copy `ui\CoopQuestSyncUI` into `%AppData%\ReadyM.Launcher\Oblivion\Mods`.
4. Restart the server. Its console should show `Quest admin page: http://localhost:9060/`.

## Test 1: the web page

1. Join the server, then open http://localhost:9060/ in a browser on the server PC.
2. You should see yourself at the top with "in sync", and below that the quests you or the server have progress in.
3. Click a quest to see its stages. Green marks show what is shared and what each player has.
4. On a stage you don't have yet, click **Give to everyone**. Within a few seconds you should get "Co-op: quest progress synced" in game, and the quest should update.

## Test 2: quick rejoin (the 0.3 pop-up)

1. Do what you did last time: run a quest command, leave, and rejoin quickly without restarting the server.
2. You should get no tutorial or quest pop-up, only "Co-op: quest progress synced".

## Test 3: reset a quest

1. On the page, open "Origin of the Gray Prince" (MS52) and click **Reset this quest**.
2. Leave and rejoin. MS52 should no longer be in your journal, and the page should show no MS52 progress.

## Test 4: two players (when your friend can join)

1. Follow Test 3 in the 0.3 steps: progress made by either of you should reach the other.
2. Choose your own name under "Shared progress comes from". Now when your friend advances a quest on their own, you shouldn't get it. When you advance one, they should.
3. Switch it back to "Everyone".

## Opening the page from another computer

By default only the server PC can open the page. To let others open it, create `config.json` in the server's `mods\CoopQuestSync\server\` folder:

```json
{ "WebBind": "0.0.0.0", "WebPassword": "pick-a-password" }
```

Then open `http://<server PC's IP>:9060/` from the other computer. The page asks for the password, and Windows may ask you to allow the server through the firewall. Without a password, the server won't open the page to other computers.

Other settings you can put in the same file, with their defaults: `"WebPort": 9060` (0 turns the page off), `"AutoSync": true` (used until it's changed from chat or the page), `"UndoReplayRewards": true`.

## What to send me

* Server console lines that start with `[QuestSync]`.
* The `[CoopQuestSyncUI]` lines from the client log if any pop-up gets through.
* Anything on the page that looks wrong or confusing.

## Chat commands

| Command | What it does |
| --- | --- |
| `/qs status` | Shows auto sync, the source player, and each player's state |
| `/qs mine [quest]` | Lists the stages the server sees in your inventory |
| `/qs shared [quest]` | Lists the shared progress |
| `/qs push <quest> <stage>` | Gives yourself the token for one stage |
| `/qs auto on\|off` | Turns automatic sync on or off |
| `/qs source <name>\|all` | Shares only one player's progress, or everyone's |
| `/qs reset` | Resets every shared quest. Players lose those quests when they rejoin |
