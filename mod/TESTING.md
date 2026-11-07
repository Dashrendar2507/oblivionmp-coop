# Co-op Quest Sync 0.5.1: how to test

This is the full test plan for what is on `main` today (0.5.1). What changed in each version is in
[CHANGELOG.md](../CHANGELOG.md); the keys, commands and settings are in [docs/USING.md](../docs/USING.md).

Every file below says where it is: **Server** (the computer that runs `server.exe`) or **PC** (the computer you play
on). When you host and play on the same computer, both are on it, but they are still different folders.

## Where things are

| What | Where |
| --- | --- |
| Server folder | **Server:** the folder with `server.exe`, for example `C:\Users\<you>\Desktop\oblivion-server-win64-1.0.1\`. Below it's written `<server folder>`. |
| The mod on the server | **Server:** `<server folder>\mods\CoopQuestSync\` (`manifest.json`, `client\`, `server\`). Replaced on every update. |
| Shared progress and settings | **Server:** `<server folder>\coop-quest-sync-state.json`. Keep it across updates. |
| The game's text for the server | **Server:** `<server folder>\coop-quest-sync-strings.json`. Never share it, never put it in a zip. |
| Optional settings file | **Server:** `<server folder>\mods\CoopQuestSync\server\config.json` |
| ReadyM admin list | **Server:** `<server folder>\data\web.db` (read only, by the mod) |
| Server log | **Server:** the server console window, the newest log file in `<server folder>`, or the admin panel's **Terminal** tab (http://localhost:9050). The mod's lines start with `[QuestSync]`. |
| The add-on that runs | **PC:** `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` (paste it into the Explorer address bar). The copy in the server's `client\addon\` folder is only what gets installed. |
| UE4SS.log | **PC:** `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\OblivionRemastered\Binaries\Win64\UE4SS.log` (or Steam, right-click the game, **Manage**, **Browse local files**). A new one each game start: copy it before you start the game again. |
| Plugins.txt | **PC:** `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\OblivionRemastered\Content\Dev\ObvData\Data\Plugins.txt` |

## Install or update

1. **Server:** stop the server. Delete `<server folder>\mods\CoopQuestSync\` and copy in the `CoopQuestSync` folder
   from the zip, so you get `<server folder>\mods\CoopQuestSync\manifest.json`. Don't touch
   `<server folder>\coop-quest-sync-state.json` or `<server folder>\coop-quest-sync-strings.json`.
2. **PC:** the end of `Plugins.txt` must read:

   ```
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```

   Nothing to copy: the launcher puts both plugins in the game's `Data` folder when you join and takes them out
   when you leave.
3. **PC:** if `off` or `lite` (or `off.txt`) is in `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\`,
   delete it.
4. **Server:** start the server. Its log should show:
   * `Loaded 287 quests / 2696 stages from the token map`
   * `Game data: 11019 dialogue lines, ... names, 24730 text entries` (0 text entries is Test 0 below)
   * `Quest admin page: http://localhost:9060/`

## Test 0: the server's own text (strings file)

The server shows quest and NPC names on its web page and in chat from `<server folder>\coop-quest-sync-strings.json`.
When that file is missing, it tries to make it from the game installed on the same computer. This test checks
each way it can get the file, and what the log says when it can't.

**What to expect:** on a standard Steam install the server **can't** make the file by itself. The remaster's text
is Oodle-compressed, reading it needs `oo2core_9_win64.dll`, and the game doesn't ship that file (your UE4SS.log of
2026-11-07 says so: `there's no oo2core_9_win64.dll on this computer`). The mod never downloads it. That's why you
had to put `strings.json` in by hand. So step B is expected to fail, and the test checks that it fails cleanly and
says why.

**A. The copy you put in by hand is kept.**

1. **Server:** check that `<server folder>\coop-quest-sync-strings.json` exists. If it does, skip to step 3.
2. **Server:** if it doesn't, put your `strings.json` (the one you placed by hand) in
   `<server folder>\mods\CoopQuestSync\server\` and start the server. The log should show
   `Copied the game's text to <server folder>\coop-quest-sync-strings.json, where updates of the mod keep it.`
3. **Server:** the log shows `The game's text: <server folder>\coop-quest-sync-strings.json` and
   `Game data: ... 24730 text entries`. On http://localhost:9060/, MS93 shows its name, not `MS93`.

**B. The server tries to make its own.**

1. **Server:** stop the server. Move `<server folder>\coop-quest-sync-strings.json` to your desktop, and move any
   `strings.json` out of `<server folder>\mods\CoopQuestSync\server\` too. Keep both safe.
2. **Server:** start the server. Look for these `[QuestSync]` lines, in this order:
   * `OblivionRemastered-Windows.pak: .pak version 11, 241371 files, compression Oodle, with a directory index.`
     (it found the game);
   * `Reading the game's text from OblivionRemastered-Windows.pak (OblivionRemastered/Content/Localization/Game/en/Game.locres, Oodle).`
   * `Couldn't read the game's text from OblivionRemastered-Windows.pak: the game's text is Oodle-compressed and
     there's no oo2core_9_win64.dll on this computer. ...`
   * `Game data: ... 0 text entries (no coop-quest-sync-strings.json or strings.json: names and lines show as keys).`
3. Check:
   * **Server:** no `coop-quest-sync-strings.json` was made in `<server folder>`.
   * The server still starts, http://localhost:9060/ still opens, and quests show names worked out from their ids
     (for example "Ysabel Andronicus" from `LOC_FN_YsabelAndronicus`).
   * **Server:** nothing new appeared next to `server.exe` or in `mods\CoopQuestSync\` (nothing was downloaded).
4. If instead you see `Wrote <server folder>\coop-quest-sync-strings.json: ... of the ... texts the mod uses`, the
   server **did** make its own file: please send me the log, that's news. Other lines you might see:
   * `Oblivion Remastered isn't installed on this computer (or set GameFolder in config.json) ...`: it didn't find
     the game through Steam. Put `{ "GameFolder": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Oblivion Remastered" }`
     in `<server folder>\mods\CoopQuestSync\server\config.json` and try again.
   * `GameFolder ... doesn't hold Oblivion Remastered.`: that path is wrong.
   * `No Localization/Game/en/Game.locres in the game's .pak files`: the language in config.json isn't one the
     game has.
5. **Server:** stop the server, move `coop-quest-sync-strings.json` back into `<server folder>`, start the server,
   and check `The game's text: ...` again.

**C. The text survives an update.**

1. **Server:** stop the server, delete `<server folder>\mods\CoopQuestSync\` and copy the zip's `CoopQuestSync`
   folder in again.
2. **Server:** start the server. The log shows `The game's text: <server folder>\coop-quest-sync-strings.json`, and
   the page still shows real names.

**D. Players don't need the server's text.**

1. **Server:** stop the server, move `<server folder>\coop-quest-sync-strings.json` to your desktop, start the
   server.
2. **PC:** join, then type `/coop test talk`. The overlay should show real sentences, looked up in your own game.
   **PC** UE4SS.log should show `[CoopQuestSyncUI] string tables: ...` and `text: found 5 of 5 key(s)`.
3. **Server:** put `coop-quest-sync-strings.json` back and restart the server.

Send me the server log from step B either way.

## Test 1: admin-only commands

`/qs push`, `/qs auto`, `/qs source`, `/qs reset`, `/coop calm`, `/qsprobe` and `/coop test` are for admins.
`/qs status`, `/qs mine`, `/qs shared`, `/qs whoami`, `/coop follow`, `/coop join`, `/coop revive` and
`/coop giveup` are for everyone. Admins are the ReadyM admin panel's admins (http://localhost:9050, **Admins**) with
the **players.manage** permission (superusers always), read from **Server:** `<server folder>\data\web.db`, plus
anyone added with **Make admin** on the co-op page. Players are recognised by their ReadyM id, not their nickname.

1. **Server:** start the server. Its log shows `ReadyM panel admins: read Admins(...)` (it found the list) or
   `ReadyM panel admins can't be used: ...` (and why). Send me that line.
2. Join, and type `/qs whoami`: your ReadyM id and "You are an admin (listed in the ReadyM admin panel)".
3. `/qs auto off`, then `/qs auto on`: both work.
4. On http://localhost:9060/, your player shows **admin (ReadyM panel)** in green, and under
   **Co-op settings > Commands**, **ReadyM panel admins** says **Working**.
5. `/coop test asghost on`: the server now treats you like Ghost, who isn't an admin (only you, until you turn it
   off or the server restarts).
   * `/qs auto off`, `/qs reset` and `/coop test status` are refused with "Only server admins can use ...".
   * `/qs status`, `/qs whoami` and `/coop follow` still work. `/qs whoami` says you are testing as Ghost.
   * The page shows your player as **testing as Ghost (not admin)**.
   * `/coop test asghost off` gives your rights back, and `/qs auto on` works again.
6. With a second player who isn't an admin: their `/qs reset` is refused, and **Make admin** next to them on the
   page lets them in.

## Test 2: you alone

1. **Add-on and crashes.** Join once. If the add-on changed, a note in the top left corner asks you to restart the
   game: quit. Start again, and load and join a few times. The note should be gone, and **PC** UE4SS.log should say
   `hooked HUD notifications` before `coop.lua loaded`. If the game crashes, send UE4SS.log copied right after the
   crash.
2. Join. The server log shows `plugin found`, then `restore finished` or `nothing to restore`, then
   `caught up with the party`.
3. **Esc after the catch-up.** Right after the catch-up, press Esc: the pause menu should open. If a "Quest Added"
   window flashed up, **PC** UE4SS.log says `closed the Quest Added pop-up`. If you had to close one by hand, or Esc
   does nothing, send UE4SS.log.
4. On http://localhost:9060/, **Co-op settings** shows the switches. Click a quest you have progress in: stages with
   effects show tags such as "Teleport" or "Items".
5. Talk to any NPC. The page shows "talking" next to your name, and the server log
   `<you> is talking to <NPC>; 0 witness(es)`.
6. Talk to a merchant and open Barter. The server log shows `<you> is bartering.`, then `<you> stopped bartering.`.
   Try persuasion the same way, and picking a lock (`is picking a lock.`). Opening a chest shows nothing.
7. **Back where you left.** Walk somewhere outdoors, away from where your local save starts you, and quit. Join
   again: you should be moved back there ("Back where you left."). Then go into a house, stand in a corner and quit
   from inside: next time you get a loading screen into the house, then land in that corner ("Back inside.").
   From the moment you're in the world until you stand in your spot, the screen stays black with
   "Taking you back to where you left...", with no glimpse of the game between the fades (**PC** UE4SS.log:
   `ran coc ...`, then `world back after the move`). `/coop test status` shows the spot the server has.
   Without rejoining: in a house, stand still 3 s, `/coop test pin`, walk out, **close the chat**, then
   `/coop test back` (a load with the chat open leaves OblivionMP's chat stuck).

## Test 3: alone, with a pretend teammate

The server can play a made-up teammate called "Ghost":

1. `/coop test status`: your phase, area, and which effect switches apply right now.
2. `/coop test talk`: Ghost "talks" to an NPC for about 40 s. You see the overlay with real lines and can't move
   until it ends. After the second line it says "Ghost is bartering..." for a few seconds, later
   "Ghost is persuading...". Alt+U closes the overlay and frees you; Alt+U again brings it back.
   `/coop test talk ask`: a banner at the top ("Ghost talks with ... · Alt+U to listen in") until it ends; Alt+U
   opens the overlay. `/coop test stop` ends it early. With "Freeze watchers" set to "Nobody" on the page: overlay,
   no freeze.
3. **Voices.** Talk to any NPC yourself for a line or two first. Then `/coop test voice`: a random voice line plays,
   and the chat says which. `/coop test talk`: Ghost's lines come with their voices, each as its text shows. Alt+U
   stops the voice; the banner (`/coop test talk ask`) never plays one. Silent? Send **PC** UE4SS.log (lines with
   `voice`).
4. **Travel to a conversation.** `/coop test pin` somewhere, walk away (out of the building too), then
   `/coop test talk ask` and press Alt+Y: you're taken to the pinned spot, inside the building first if it was in
   one, behind the black travel screen. Again with `/coop test talk`: on arrival you stand still if "Freeze
   watchers" is "Same area". If you can, pin a spot inside a city and travel there from outside (the `cow` route).
   Send the server log and UE4SS.log (lines with `travel`, `coc`, `cow`).
5. **Stage effects.** `/coop test effects MS93` lists the stages with effects, for example `20: teleport/items`.
   `/coop test stage <quest> <stage>`: Ghost "reached" it, and you get it as if live; the chat line says which
   effects you should get. Turn Live Teleport off on the page and try another teleport stage: you get the stage but
   stay put.
6. **Death.** Console: `player.modav health -10000`. "You are down...". Then `/coop test revive` (back where you
   fell), or Alt+G, or wait 60 s (back where you last stood on solid ground). Jump off something high: you come
   back at the top.
7. **Calm.** Save first. Hit a townsperson until they fight back, `/coop calm near`: they stop. Get a guard to chase
   you, `/coop calm <your name>`: the guard stops and your bounty is 0. Fight a few creatures, `/coop calm`: they
   stop for a moment (hostile ones start again, which is expected). Send the server log (`Combat:`, `Calm:`) and
   UE4SS.log (`StopCombat`, `scaonactor`).

## Test 4: two players (when your friend can join)

Stand next to each other:

1. **Conversation.** One of you talks to an NPC. The other sees the overlay with the NPC's name and topics, hears
   the voice, can't move until it ends, and sees a "Talking" tag over the talker. The talker opens Barter: the
   overlay says "... is bartering..." and the tag reads "Bartering". Then with the watcher in another building: the
   overlay shows but they can move; Alt+Y takes them to the talker.
2. **Shared progress.** One of you advances a quest. The other gets it within a few seconds, with "X advanced ...".
   A stage reward goes to both.
3. **Stage effects.** When one of you reaches a "Teleport" stage, the other moves too. With Live Teleport off, the
   other gets the stage but stays put.
4. **Death.** One of you goes down; the other gets a chat line, stands next to them and presses Alt+H. Next time,
   wait 60 s or Alt+G: they come back next to the other player.
5. **Walking around during a conversation** (decides the defaults, take notes). "Freeze watchers": "Nobody". One
   talks for a minute while the other walks and fights nearby. Repeat with each of you entering the area first. Do
   the NPCs near the walker move and fight normally? Does anything jump into place when the talk ends?
6. **Fights** (decides what to build next). The one who did *not* enter the area first attacks a townsperson or a
   guard. Does the NPC fight both of you or only the attacker? Is the other player "in combat" (music, can't wait)?
   What does the server log say (`Combat: ...`)? Then `/coop calm` and the page's **Calm** button.
7. **Leader only.** "Progress counts from": "Leader only", leader = you. Your friend's progress isn't shared; yours
   is.
8. **Admins.** As in Test 1, step 6.

## What to send me

* **Server:** the server log (`[QuestSync]` lines), from start-up to the problem.
* **PC:** UE4SS.log, copied before you start the game again.
* For the overlay or the travel screen: a screenshot.
* **PC:** your `Plugins.txt`, if quests from the DLCs or Knights don't sync.
* Never send `coop-quest-sync-strings.json` or `strings.json` anywhere public.

## Known limits

The full list is in [docs/KNOWN-ISSUES.md](../docs/KNOWN-ISSUES.md). The ones that matter for testing:

* The server can't make its own text file on a standard Steam install (Test 0, step B). The overlay doesn't need it.
* The overlay runs a few seconds behind the talker (the game reports a line when it ends). The voice plays with the
  text, from nowhere (not from the NPC).
* Going back into a building, travelling into another one or world space, and `/coop calm` all need the add-on
  running on the **PC**. Without it you come back outside, by the door.
* You can still turn the camera while frozen.
* Menus are recognised by the original game's menu numbers. Tell me which menu never shows "is bartering" (or the
  like).
* Respawning next to someone only works in the same area; otherwise you come back where you last stood on solid
  ground, at least 6 s before you died.
* The co-op keys also reach the game (Alt+U also presses U). Pick other letters under **Keys** on the page if one
  clashes. K is OblivionMP's chat.

## Settings file

**Server:** `<server folder>\mods\CoopQuestSync\server\config.json`, all optional. Every setting is listed in
[docs/USING.md](../docs/USING.md#configjson-server-host-optional). The ones used in these tests:

```json
{
  "WebBind": "127.0.0.1", "WebPassword": "", "WebPort": 9060,
  "GameFolder": "", "Language": "en", "StringsFile": "",
  "PanelDatabase": "data/web.db", "PanelPermission": "players.manage"
}
```

`GameFolder` empty means "find the game through Steam". `StringsFile` empty means
`<server folder>\coop-quest-sync-strings.json`. `config.json` is inside the mod's folder, so copy it back after an
update.
