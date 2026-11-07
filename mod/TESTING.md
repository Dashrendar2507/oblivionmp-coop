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
* **Menus during a conversation.** When the talker opens the barter, persuasion, training, repair or spell screen,
  the watchers' overlay says "Alice is bartering..." and waits for them to come back. The tag over their head says
  so too, and "Picking a lock" while they pick one.
* **Alt+U: follow or leave a conversation.** Pressing Alt+U while you watch someone's conversation closes the overlay
  and unfreezes you, and pressing it again brings you back. "Watchers follow a conversation: When they press Alt+U"
  on the page stops pulling anyone in: a banner at the top says who is talking, for as long as they talk, and you
  choose. `/coop follow` in chat does the same as the key.
* **Back where you left.** When you join, you come back where you left instead of where your local save put you,
  inside buildings too: the server asks your game to load the building (the add-on runs the console's `coc`), then
  puts you on the spot. If that fails, you come back outside, where you last stood outdoors.
* **Updates wait for the end of a conversation**, so a quest doesn't jump while you're talking.
* **Chat messages**: "Alice advanced The Quest: journal text".
* **Rewards handed out in conversation** (gold or items a line gives) go to everyone. Trades don't.
* **Quest items** (keys, letters) one player picks up can go to everyone. Off by default.
* **Death**: you go down instead. A teammate standing next to you presses **Alt+H** to revive you. After 60 s,
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
2. **Nothing to copy for the players.** Both plugins (`CoopQuestSync.esp` and `CoopQuestSyncDialogue.esp`) are in
   the mod's `client` folder. OblivionMP puts them in each player's `Data` folder when they join, and takes them out
   when they leave, which is why they disappear after a disconnect. If you copied them into `Data` yourself before,
   you can delete those copies.
3. **Each player:** open `Plugins.txt` (the same one you edited for 0.2) and make the end of it read:

   ```
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```

   Both plugins now need every plugin that comes with the game (Knights.esp, the DLC*.esp files, AltarESPMain.esp and
   AltarDeluxe.esp) to be loaded. They are on by default. If the game doesn't load our plugins any more, send me
   your `Plugins.txt`.
4. **The UE4SS add-on installs itself.** Your last log showed it never started (UE4SS listed only OblivionMP's own
   mod), which is also why the Ghost ship box was never hidden. The add-on now comes with the mod: on your first
   join, the client copies it to `%AppData%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI` and says "Restart the
   game once". After that restart, UE4SS.log should have lines starting with `[CoopQuestSyncUI]`.
5. Restart the server. Its log should show:
   * `Loaded 287 quests / 2696 stages from the token map`
   * `Game data: 11019 dialogue lines, ... names, 24730 text entries` (the game's text, from `strings.json`)
   * `Quest admin page: http://localhost:9060/`

## Test 1: you alone

0. **Crashes on load** (2026-11-07). The add-on folder is
   `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\`. Two empty files there narrow a crash down (a new
   text document is fine: `off.txt` and `lite.txt` count too). Start the game from scratch after each change.
   * `off`: the add-on does nothing. UE4SS.log must say `switched off`, or the file isn't being seen. Load and join
     5 times.
   * Then replace `off` with `lite`: the add-on runs, but without its regular menu checks. UE4SS.log says `lite`.
     5 more.
   Tell me how many of each crashed, and send UE4SS.log copied right after a crash (the game starts a new one each
   time). Delete the file afterwards.
1. Join. The server log should show `plugin found`, then `restore finished` or `nothing to restore`, then
   `caught up with the party`.
2. Open http://localhost:9060/. Under **Co-op settings** you should see the switches. Click a quest you have
   progress in: stages with effects show small tags such as "Teleport" or "Items".
3. Talk to any NPC. The page should show "talking" next to your name while you are in the conversation. The server
   log should show `<you> is talking to <NPC>; 0 witness(es)`.
4. Talk to a merchant and open Barter. The server log should show `<you> is bartering.`, then `<you> stopped bartering.`
   when you close it, and the page shows "bartering" next to your name meanwhile. Try persuasion the same way, and
   picking a lock (`is picking a lock.`). Opening a chest should not show anything.
5. Rejoin once. Your quests should come back as before, silently.
6. Walk somewhere outdoors, away from where your local save starts you, and quit. Join again: a few seconds after
   you appear, you should be moved back there ("Back where you left."). Then go into a house, stand in a corner and
   quit from inside: next time you should get a loading screen into the house, then land in that corner ("Back
   inside."). `/coop test status` shows the spot the server has.
   Without rejoining: in a house, stand still 3 s, `/coop test pin`, walk out of the house, `/coop test back`.
   If you stay outside, send me UE4SS.log: the add-on writes `ran coc ...` or `could not run coc ...` there.

## Test 1b: alone, with a pretend teammate

The server can play a made-up teammate called "Ghost". Type these in chat:

1. `/coop test status`: what the server knows about you (phase, area, which effect switches apply right now).
2. `/coop test talk`: Ghost "talks" to an NPC for about 40 s. You should see the overlay with a few real lines
   (the NPC and topic; the sentences need the game's text, see below), and you should be unable to move until it
   ends. After the second line, the overlay should say "Ghost is bartering..." for a few seconds, and later
   "Ghost is persuading...".
   Press Alt+U while it runs: the overlay should close and you can move. Press it again: it comes back with the last
   lines. Then try `/coop test talk ask`: you get a banner at the top ("Ghost talks with ... · Alt+U to listen in")
   that stays until the conversation ends, and Alt+U opens the overlay. `/coop follow` in chat does the same as
   Alt+U. `/coop test stop` ends it early. Set "Freeze watchers" to "Nobody" on the page and try again: overlay,
   no freeze.
3. `/coop test effects MS93` lists the stages of a quest that have effects on the player, for example
   `20: teleport/items`. Pick a quest you haven't started and a stage with a teleport.
4. `/coop test stage <quest> <stage>`: Ghost "reached" that stage. You get it as if a teammate did it live, and the
   chat line says which effects you should get. Then turn Live Teleport off on the page, and try another teleport
   stage: you should get the stage but stay where you are.
5. Death: open the console and type `player.modav health -10000`. You should see "You are down...". Either `/coop test revive` (Ghost
   revives you, where you fell), or press Alt+G, or wait 60 s: you come back where you last stood on solid ground,
   a few seconds before you died (Ghost has no body to stand next to). To check the fall case, jump off something
   high: you should come back at the top, not at the bottom.

6. Voices (new, experimental). Watchers now hear the NPC's voice with each line, from nowhere, while listening in
   (setting "Watchers hear the voices" on the page). Where the game keeps its voice sounds isn't known yet, so the
   add-on looks in the likely places and learns the right one from any voice the game plays itself. So:
   1. First talk to any NPC yourself for a line or two (that teaches the add-on where voices live).
   2. `/coop test voice`: plays a random voice line right away. The chat says which line and what it says.
   3. `/coop test talk`: Ghost's lines should now come with their voices, each starting as its text shows. Alt+U
      should stop the voice; the offer banner (`/coop test talk ask`) never plays one.
   If you hear nothing, send UE4SS.log: the lines with `voice` say what the add-on found and what it tried. I can
   fix most of it without you restarting the game, because the add-on now picks up its own updates.

7. Travel to a conversation (new). The banner and the overlay now also say "Alt+Y to join Ghost".
   1. Stand somewhere and `/coop test pin` (Ghost will "stand" there).
   2. Walk away, out of the building too, then `/coop test talk ask`.
   3. Press Alt+Y without listening in: you should be taken to the pinned spot (inside the building first, if it
      was in one). Do it again with `/coop test talk` (listening in): same, and you should stand still on arrival
      if "Freeze watchers" is "Same area".
   4. If you can, pin a spot inside a city (Chorrol, Skingrad...) and travel there from outside, or the other way:
      that's the `cow` route. Send the server log and UE4SS.log either way (lines with `travel`, `coc`, `cow`).

8. Calming fights (new). Save first: this makes someone angry on purpose.
   1. Hit a townsperson (not a guard) until they fight back, then `/coop calm near`: they should stop fighting.
   2. Steal something in front of a guard, or hit them, so they chase you. `/coop calm <your name>`: the guard
      should stop, and your bounty should be 0 (check the journal or a guard's dialogue).
   3. Pick a fight with a few creatures and `/coop calm`: they stop for a moment; hostile creatures will usually
      start again, which is expected.
   Send the server log (lines with `Combat:` and `Calm:`) and UE4SS.log (lines with `StopCombat`, `scaonactor`).

## Test 2: two players (when your friend can join)

Do these standing next to each other, in the same place:

1. **Conversation.** One of you talks to an NPC. The other should:
   * see the overlay ("Listening in · Alice talks with ...") with the NPC's name and topics;
   * be unable to move until the conversation ends;
   * see a "Talking" tag over the talker.
   Then the talker opens Barter: the watcher's overlay should say "... is bartering..." until they close it, and the
   tag over the talker should read "Bartering".
   Then try it with the watcher in another building: they should see the overlay but can still move. The watcher
   presses Alt+Y: they should arrive next to the talker (inside the building), standing still until the talk ends.
2. **Shared progress.** One of you advances a quest. The other should get it within a few seconds, with the
   chat line "X advanced ...". If the stage had a reward, both should get it.
3. **Stage effects.** Find a quest stage that moves you (the page shows a "Teleport" tag). When one of you
   reaches it, the other should be moved too. Then turn Live Teleport off on the page and try another one: the
   other player gets the stage but stays put.
4. **Death.** One of you dies (or `player.modav health -10000` in the console). The dead player should see "You are down...", the other
   gets a chat line. Stand next to them and press Alt+H: they should come back. Next time, wait 60 s or press
   Alt+G: they should come back next to the other player.
5. **Walking around during a conversation.** This one decides the defaults, so please take notes. Set "Freeze
   watchers" to "Nobody", find a few hostile creatures or a busy street, and have one of you talk to an NPC for a
   minute while the other walks around and fights next to them. Then do it again with the talker being the one
   who entered the area first, and again with the other one entering first. Tell me:
   * whether the NPCs and creatures near the walker move and fight normally, or stand still;
   * what the talker sees when their conversation ends: things jumping into place, or nothing odd;
   * anything else that looked wrong.
6. **Fights.** This one tells me what to build next, so please take notes. One of you (not the one who entered
   the area first) attacks a townsperson or a guard, while the other stands nearby. Tell me:
   * whether the NPC fights both of you, or only the attacker;
   * whether the other player's game acts as "in combat" (combat music, can't wait or fast travel);
   * what the server log says (`Combat: ... fights in ..., run by ...'s game`).
   Then try `/coop calm` and the web page's **Calm** button.
7. **Leader only.** Set "Progress counts from" to "Leader only" and the leader to your name. When your friend
   advances a quest on their own, you shouldn't get it. When you do, they should.

## What to send me

* The server log (`[QuestSync]` lines), and the client log if something looks wrong.
* For the overlay: a screenshot.
* Your `Plugins.txt`, so I can check the load order the server assumes (`GamePlugins` in config.json).

## Getting the game's text (for the overlay and the page)

The text now comes from the games themselves:

* **Your game** gives the overlay its text, in your game's language: the add-on looks it up in the running game
  (reading the game's files needs an Oodle library that isn't on your PC). To test it, move `strings.json` out of
  the server folder first (so the server sends no text), then `/coop test talk`. UE4SS.log should show
  `[CoopQuestSyncUI] string tables: ...` and `text: found 5 of 5 key(s)`, and the overlay real sentences. Put
  `strings.json` back after.
* **The server** makes `strings.json` itself from the game on its PC, the first time it starts without one. To test
  that: move your current `strings.json` out of `mods\CoopQuestSync\server` (keep it somewhere), start the server,
  and look for `Wrote ...strings.json: ... of the ... texts the mod uses`.

If either says something else, send me the logs and put your old `strings.json` back. `strings.json` is never in
the zip.

## Known limits

* **Back where you left** needs the pop-up add-on (`ui\`) to go back into a building. Without it, or if the game
  ignores the add-on's console command, you come back outside. Outdoors in another world space (a city, the
  Shivering Isles), the add-on runs `cow` first; without it you only come back when your local save starts you in
  the same world space as your last outdoor spot.
* **The overlay runs behind the talker.** The game reports a line when its result script runs, which is at the end of
  the line, so the watcher reads it after the talker has heard it. There is no voice.
* **The keys** are Alt+U (listen in or leave a conversation), Alt+Y (travel to the talker), Alt+H (revive the teammate next to you) and Alt+G
  (respawn when you are down). The game also sees the letter, so if one of them does something in the game too,
  pick another letter under **Keys** on the page; every player's game switches within a few seconds. K is the
  chat. `/coop follow`, `/coop join`, `/coop revive` and `/coop giveup` in chat do the same as the keys. The client log shows
  `Alt+U (follow) pressed.` when a key gets through.
* **You can still look around while listening in.** The game's freeze stops walking and fighting, but not the
  camera, and I haven't found a way for a mod to lock it. Looking around doesn't affect anyone else.
* **Menus are recognised by the original game's menu numbers.** If the remaster numbers them differently, the
  log won't show `is bartering`. Tell me which menu stayed silent.
* **The NPC's name** comes from the line's own conditions when it names its speaker, otherwise from the NPC closest
  to the talker.
* **Respawning next to someone** only works when they are in the same area (the same interior, or both outside). If
  not, you come back where you last stood on solid ground, at least 6 s before you died, so a fall or a drop out of
  the map doesn't kill you again. If you hadn't been in that area that long, you come back where you entered it.
  A teammate's revive (Alt+H) brings you back where you fell; use Alt+G if your body is somewhere unreachable.
* **Stage scripts that say "player"** still act on each player's own game. The switches decide whether that happens
  for the players who didn't reach the stage themselves.
* **Quest items and dialogue rewards** from DLC plugins need the right load order (see `GamePlugins` below).

## Settings file

(Also in the state file: each player's last outdoor spot, for "Back where you left".)

`config.json` in the server's `mods\CoopQuestSync\server\` folder, all optional:

```json
{
  "WebBind": "127.0.0.1", "WebPassword": "", "WebPort": 9060,
  "AutoSync": true, "UndoReplayRewards": true,
  "GamePlugins": "Oblivion.esm,DLCBattlehornCastle.esp,DLCFrostcrag.esp,DLCHorseArmor.esp,DLCMehrunesRazor.esp,DLCOrrery.esp,DLCShiveringIsles.esp,DLCSpellTomes.esp,DLCThievesDen.esp,DLCVileLair.esp,Knights.esp,AltarESPMain.esp,AltarDeluxe.esp,AltarESPLocal.esp",
  "StringsFile": ""
}
```

`GamePlugins` is the order of the game's own plugins in `Plugins.txt`. It only matters for sharing items and naming
NPCs from the DLCs and the remaster's plugins.
