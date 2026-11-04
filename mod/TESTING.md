# Co-op Quest Sync 0.5: how to test

What's new since 0.4:

* **Stage effects reach everyone, with switches.** Every vanilla stage script that does something to the player
  is patched so each effect is tagged Teleport, Combat, Items, Status, Faction ranks or Cut scene (446 stages in
  181 quests). The player who reaches a stage always gets everything. For everyone else, the web page has a switch
  per effect and per moment:
  * **Live**: someone advanced the quest while you play. All on by default. Live Items is the reward rule.
  * **On join**: what the party did while you were away. Items, Status and Ranks on by default, so nobody gets
    teleported or thrown into a fight when they log in.
  * **On rejoin**: your own stages, which the game forgets and the plugin sets again every time you join. Only
    Ranks is on by default.
  * Before 0.5, stages with a teleport or cut scene were never given to other players at all. Now every stage is.
* **Knights of the Nine, the DLCs and the remaster's own changes.** The plugin is now built from all the game's
  plugins you uploaded, so it covers Knights and the DLC quests (287 quests) and doesn't undo the remaster's fixes.
* **Anyone's progress counts** (default), or **leader only**, on the page. The leader is also who you respawn next to.
* **Shared conversations.** When someone talks to an NPC, everyone else sees an overlay styled like the dialogue
  screen: the NPC's name, the topic, the line. Watchers can't click anything. Players in the same area are frozen
  until the conversation ends, unless they are in combat. See "Known limits" for the text.
* **Updates wait for the end of a conversation**, so a quest doesn't jump while you're talking.
* **Chat messages**: "Alice advanced The Quest: journal text".
* **Rewards handed out in conversation** (gold or items a line gives) go to everyone. Trades don't.
* **Quest items** (keys, letters) one player picks up can go to everyone. Off by default.
* **Death**: you go down instead. A teammate standing next to you presses **Alt+R** to revive you. After 60 s,
  or when you press **Alt+G**, you come back next to the leader. If the leader is the one down, you come back next to
  the first other player by name.
* **Tags over heads**: "Talking" and "Down".
* **Level scaling and difficulty** for the whole session, on the page.
* Not possible: turning friendly fire off. OblivionMP doesn't tell mods who hit whom.

Every one of these is a switch under **Co-op settings** on http://localhost:9060/. Settings are saved with the
shared progress, so a restart keeps them.

## Install

1. **Server:** replace the server's `mods\CoopQuestSync` folder with the one in the zip. Keep
   `coop-quest-sync-state.json`: your shared progress carries over.
2. **Each player:** copy both files from `game-plugin\` in the zip into the game's `Data` folder, replacing the old
   `CoopQuestSync.esp`:
   * `CoopQuestSync.esp` (quests, as before)
   * `CoopQuestSyncDialogue.esp` (new: shared conversations)
3. **Each player:** open `Plugins.txt` (the same one you edited for 0.2) and make the end of it read:

   ```
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```

   Both plugins now need every plugin that comes with the game (Knights.esp, the DLC*.esp files, AltarESPMain.esp and
   AltarDeluxe.esp) to be loaded. They are on by default. If the game doesn't load our plugins any more, send me
   your `Plugins.txt`.
4. The pop-up add-on in `ui\` hasn't changed since 0.3.
5. Restart the server. Its log should show:
   * `Loaded 287 quests / 2696 stages from the token map`
   * `Game data: 11019 dialogue lines, ... names, 0 text entries (no strings.json: ...)`
   * `Quest admin page: http://localhost:9060/`

## Test 1: you alone

1. Join. The server log should show `plugin found`, then `restore finished` or `nothing to restore`, then
   `caught up with the party`.
2. Open http://localhost:9060/. Under **Co-op settings** you should see the switches. Click a quest you have
   progress in: stages with effects show small tags such as "Teleport" or "Items".
3. Talk to any NPC. The page should show "talking" next to your name while you are in the conversation. The server
   log should show `<you> is talking to <NPC>; 0 witness(es)`.
4. Rejoin once. Your quests should come back as before, silently.

## Test 1b: alone, with a pretend teammate

The server can play a made-up teammate called "Ghost". Type these in chat:

1. `/coop test status`: what the server knows about you (phase, area, which effect switches apply right now).
2. `/coop test talk`: Ghost "talks" to an NPC for about 25 s. You should see the overlay with a few real lines
   (the NPC and topic; the sentences need the game's text, see below), and you should be unable to move until it
   ends. `/coop test stop` ends it early. Set "Freeze watchers" to "Nobody" on the page and try again: overlay, no freeze.
3. `/coop test effects MS93` lists the stages of a quest that have effects on the player, for example
   `20: teleport/items`. Pick a quest you haven't started and a stage with a teleport.
4. `/coop test stage <quest> <stage>`: Ghost "reached" that stage. You get it as if a teammate did it live, and the
   chat line says which effects you should get. Then turn Live Teleport off on the page, and try another teleport
   stage: you should get the stage but stay where you are.
5. Death: open the console and type `kill`. You should see "You are down...". Either `/coop test revive` (Ghost
   revives you, where you fell), or press Alt+G, or wait 60 s: you come back where you last stood on solid ground,
   a few seconds before you died (Ghost has no body to stand next to). To check the fall case, jump off something
   high: you should come back at the top, not at the bottom.

## Test 2: two players (when your friend can join)

Do these standing next to each other, in the same place:

1. **Conversation.** One of you talks to an NPC. The other should:
   * see the overlay ("Listening in · Alice talks with ...") with the NPC's name and topics;
   * be unable to move until the conversation ends;
   * see a "Talking" tag over the talker.
   Then try it with the watcher in another building: they should see the overlay but can still move.
2. **Shared progress.** One of you advances a quest. The other should get it within a few seconds, with the
   chat line "X advanced ...". If the stage had a reward, both should get it.
3. **Stage effects.** Find a quest stage that moves you (the page shows a "Teleport" tag). When one of you
   reaches it, the other should be moved too. Then turn Live Teleport off on the page and try another one: the
   other player gets the stage but stays put.
4. **Death.** One of you dies (or `kill` in the console). The dead player should see "You are down...", the other
   gets a chat line. Stand next to them and press Alt+R: they should come back. Next time, wait 60 s or press
   Alt+G: they should come back next to the other player.
5. **Leader only.** Set "Progress counts from" to "Leader only" and the leader to your name. When your friend
   advances a quest on their own, you shouldn't get it. When you do, they should.

## What to send me

* The server log (`[QuestSync]` lines), and the client log if something looks wrong.
* For the overlay: a screenshot.
* Your `Plugins.txt`, so I can check the load order the server assumes (`GamePlugins` in config.json).

## Getting the game's text (for the overlay and the page)

The remaster doesn't keep names, dialogue or journal text in the plugins, only keys like
`LOC_FN_YsabelAndronicus`. Without the text, the overlay shows the NPC and the topic but not the words of the line,
and the page shows quest ids. The text is in the game's localization file:

1. Download FModel (https://fmodel.app) and point it at the game's
   `OblivionRemastered\Content\Paks` folder.
2. Find `Localization/Game/en/Game.locres` (search for `.locres`), right-click it and choose **Export Raw Data**.
3. Upload that file here. I'll turn it into `strings.json`, which goes next to the server DLL. The overlay and the
   page will then show the real names, lines and journal entries.

## Known limits

* **The overlay runs behind the talker.** The game reports a line when its result script runs, which is at the end of
  the line, so the watcher reads it after the talker has heard it. There is no voice.
* **The NPC's name** comes from the line's own conditions when it names its speaker, otherwise from the NPC closest
  to the talker.
* **Respawning next to someone** only works when they are in the same area (the same interior, or both outside). If
  not, you come back where you last stood on solid ground, at least 6 s before you died, so a fall or a drop out of
  the map doesn't kill you again. If you hadn't been in that area that long, you come back where you entered it.
  A teammate's revive (Alt+R) brings you back where you fell; use Alt+G if your body is somewhere unreachable.
* **Stage scripts that say "player"** still act on each player's own game. The switches decide whether that happens
  for the players who didn't reach the stage themselves.
* **Quest items and dialogue rewards** from DLC plugins need the right load order (see `GamePlugins` below).

## Settings file

`config.json` in the server's `mods\CoopQuestSync\server\` folder, all optional:

```json
{
  "WebBind": "127.0.0.1", "WebPassword": "", "WebPort": 9060,
  "AutoSync": true, "UndoReplayRewards": true,
  "GamePlugins": "Oblivion.esm,DLCBattlehornCastle.esp,DLCFrostcrag.esp,DLCHorseArmor.esp,DLCMehrunesRazor.esp,DLCOrrery.esp,DLCShiveringIsles.esp,DLCSpellTomes.esp,DLCThievesDen.esp,DLCVileLair.esp,Knights.esp,AltarESPMain.esp,AltarDeluxe.esp",
  "StringsFile": ""
}
```

`GamePlugins` is the order of the game's own plugins in `Plugins.txt`. It only matters for sharing items and naming
NPCs from the DLCs and the remaster's plugins.
