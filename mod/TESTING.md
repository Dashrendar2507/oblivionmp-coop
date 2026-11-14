# Co-op Quest Sync 0.6.16: every test still to do

0.6.16 adds fixes from your 2026-11-13 tests (Part 1) to the combined build: no endless load after Alt+G (0.6.4), a
note on screen while you're down, `/coop calm near` that sticks, the voice stopping at once, arriving inside a
building straight on the spot, and a calmer "Taking you back" on join (0.6.7, after the 12:14 crash), no long black screen after a revive (0.6.8/0.6.9), no load with the chat open (0.6.10), lockpicking and pickpocketing shown to the others (0.6.11/0.6.12), merchant bartering shown again (0.6.15), and two new-start fixes (0.6.16, Part 5). It's for OblivionMP **1.0.x** (your server is 1.0.1) and contains:

* **0.5.4**: `/coop calm near` calms every NPC fighting within 15 m, `/coop calm <name>` works in dungeons and
  without the trailing dot, and the server learns the game's text from the players' games.
* **0.6.2**: the black screen covers every move through a load, and Alt+G / revive bring you back to the party
  after OblivionMP's reload.
* **0.6.1**: travel to a teammate any time (`/coop goto`, Alt+O, `/coop goto lock`).
* **0.6.0**: new players choose where they start (prison intro or sewer exit).

The tests are in the order that needs the fewest restarts. Parts 1 to 5 you can do alone. Part 6 needs your friend.
Part 7 is for later, when you move the server to OblivionMP 1.1.0.

Every file below says where it is: **Server** (the computer that runs `server.exe`) or **PC** (the computer you play
on). You host and play on the same computer, but they are still different folders.

## Where things are

| What | Where |
| --- | --- |
| Server folder | **Server:** the folder with `server.exe`, for example `C:\OblivionMP\oblivion-server-win64-1.0.1\` (below: `<server folder>`) |
| The mod on the server | **Server:** `<server folder>\mods\CoopQuestSync\` (`manifest.json`, `client\`, `server\`). Replaced on every update. |
| Shared progress and settings | **Server:** `<server folder>\coop-quest-sync-state.json`. Keep it. |
| The server's game text | **Server:** `<server folder>\coop-quest-sync-strings.json` (your hand-made one) and `<server folder>\coop-quest-sync-strings.<language>.json` (learned from the players). Never share them or put them in a zip. |
| Players' saves on the server | **Server:** `<server folder>\saves\players\` (`<ReadyM id>.sav`) |
| Optional settings file | **Server:** `<server folder>\mods\CoopQuestSync\server\config.json` |
| Server log | **Server:** the server console window, the newest log file in `<server folder>`, or the admin panel's **Terminal** tab (http://localhost:9050). The mod's lines start with `[QuestSync]`. |
| Co-op web page | **Server:** http://localhost:9060/ |
| The add-on that runs | **PC:** `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` (paste it into the Explorer address bar). The copy in the server's `client\addon\` is only what gets installed. |
| UE4SS.log | **PC:** `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\OblivionRemastered\Binaries\Win64\UE4SS.log`. A new one each game start: copy it before you start the game again. |
| Local saves | **PC:** `%USERPROFILE%\Documents\My Games\Oblivion Remastered\Saved\SaveGames\` |
| Plugins.txt | **PC:** `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\OblivionRemastered\Content\Dev\ObvData\Data\Plugins.txt` |

## Install 0.6.16

1. **Server:** stop the server. Delete `<server folder>\mods\CoopQuestSync\` and copy in the `CoopQuestSync`
   folder from the zip, so you get `<server folder>\mods\CoopQuestSync\manifest.json`. Don't touch
   `<server folder>\coop-quest-sync-state.json` or `<server folder>\coop-quest-sync-strings.json`.
2. **PC:** nothing to copy, but **the plugin changed** (`CoopQuestSync.esp`, 0.6.16): quit the game before you join,
   so it starts with the new one. The add-on is the same as in 0.6.14. Coming from an older build, a note in the top
   left corner asks you to restart on your first join: quit and start the game again once, so the new add-on runs.
3. **Server:** start the server. The log's mod load order shows **`CoopQuestSync v0.6.16`**. Also `The game's text: <server folder>\coop-quest-sync-strings.json`
   and `Quest admin page: http://localhost:9060/`.
4. **Server:** on http://localhost:9060/, **Co-op settings** has both **Party > Travel to teammates any time** and
   **New players > New players start**. If either is missing, the old folder is still in place.

## Part 1: the fixes from your 2026-11-13 test

All in game on the **PC**, joined to your server, after the game restart from the install.

Already fine on 2026-11-13: Alt+Y into the dungeon stays black (1.1), Alt+G no longer loads forever (1.2), and
`/coop calm dashrendar2507.` works.

**1.0 "Taking you back" on join (the 12:14 crash).** Save first. Stand somewhere inside the Aleswell inn, quit to
the menu, and join again. On 2026-11-13 at 12:14 the game crashed here: OblivionMP's console crashed while it ran
`coc AleswellInn` (the same crash inside OblivionMP as the 2026-11-07 ones), and the very same join at 11:50 went
fine, so it doesn't happen every time. Two changes in 0.6.7:

* The add-on now waits 8 s after the join's quest catch-up (was 3 s) and 4 s after a menu opens or closes (was 2 s)
  before it runs a console command. That's my best guess at what makes it crash: the world is still settling. The
  black screen stays up a few seconds longer on join.
* That join used `coc` (door first) instead of `player.positioncell`. 0.6.6's server switched to `coc` for good, until
  it restarted, whenever a positioncell trip didn't arrive within 15 s, and the add-on can hold a command back up to
  30 s during a join. Now it waits 45 s and only switches for that one trip. **Server** log on join:
  `travels into AleswellInn: asking their game to go there (player.positioncell)`.

Join this way 3 or 4 times. If it crashes again, send **PC** `UE4SS.log` copied right after, and the **server** log.
If it keeps crashing, untick **Put players back where they left** on http://localhost:9060/ (**Co-op settings**) for now: you then
stay where the game puts you on join.

**1.1 Arriving inside, straight on the spot.** `/coop test pin` inside the Aleswell inn (somewhere away from the
door), walk outside, close the chat, `/coop test talk`, then **Alt+Y**. You now arrive directly on the pinned spot
instead of at the door first, so the people by the door shouldn't greet you. **PC** `UE4SS.log` shows
`ran player.positioncell ...`. If you end up at the door after all, the server log says `player.positioncell didn't
take them into ...; trying coc` and it travels the old way: send me both logs.

**1.2 The down note, and OblivionMP's own rebirth.** Die (console: `player.modav health -1000`). Under OblivionMP's
"You died / Press R to rebirth", a co-op note now says Alt+H (a teammate revives you), Alt+G (back with the party now)
and the seconds left, counting down. Then wait without touching anything.

On 2026-11-13 OblivionMP brought you back by itself after 9 s and 26 s. That doesn't come from the mod, and the part of
OblivionMP I can read only does it on R, so it's in its native part (I'll ask ReadyM). 0.6.6 and later treat it like Alt+G:
the screen goes black after OblivionMP's reload and you come back where you fell (or next to the party). Server log:
`came back through OblivionMP's own rebirth; taking them back after its reload.` Tell me how many seconds it took.

Then die again and press **Alt+G** before that happens: you come back a few steps from where you died, and the note
goes away.

**1.3 Revive.** Die again, then type `/coop test heal` (the same as `/coop test revive`, but with no R in it): "Ghost
revived you...", the black screen, OblivionMP's reload, and you stand where you fell.

On 0.6.7 (12:27 and 12:35) the reload went fine, but the trip back after it (`player.positioncell`, under a second
after the reload) did nothing: you stood outside behind the black screen (the punching you heard) until the server
gave up after 45 s and used `coc`. 0.6.9 waits 6 s after the reload before the trip, and if the game still ignores the
positioncell, your game notices within 6 s (no load started) and goes in through the door. Worst case about 12 s of
black instead of 45. **PC** `UE4SS.log`: `ran player.positioncell ...` followed by
a load. If you see `The game ignored player.positioncell ...; going in through the door instead`, tell me.

**1.4 Calm near.** Save first. In the Aleswell dungeon, get the Serethi fighting you, then `/coop calm near`. On
2026-11-13 they stopped for a few seconds and fought again; now `near` also runs `player.scaonactor` (what makes the
name and area calms stick). They should stay calm.

**1.5 Calm by the start of a name.** `/coop calm dash` crashed the game on 2026-11-13. It's not the name: the server
found you and ran the same commands as the full name. The crash came inside the game, right after
`player.scaonactor`, when the console ran `player.SetCrimeGold 0`, while Shagol gro-Bumph (who OblivionMP keeps twice
in that dungeon, the bug I'll report to ReadyM) was still fighting you uncalmed. 0.6.5 clears the bounty first and
runs `scaonactor` last. Save first, then try `/coop calm dash` once in the dungeon and once in a town with a guard
chasing you. If it crashes again, send **PC** `UE4SS.log` copied right after.

**1.6 The voice stops at once.** `/coop test talk`, and press **Alt+U** in the middle of a spoken line: the voice
stops right away. **PC** `UE4SS.log` shows `voice stopped with ...` the first time. If it keeps talking, send the
lines with `could not stop a voice`.

Send for Part 1: the **server log** (lines with `Calm:`, `Combat:`, `positioncell`, `after the reload`) and **PC**
`UE4SS.log`.

## Part 2: travel to a teammate any time, alone

Ghost plays the teammate. `/coop test` needs admin, which you are.

1. **Server:** on http://localhost:9060/, **Co-op settings > Party** has **Travel to teammates any time** (on),
   **Seconds between trips** (60), **No trips during a fight** (on), and **Goto** in the Keys row (O).
2. Type `/coop goto`, then press **Alt+O**. Both say "Nobody else is playing right now."
3. Walk into a building, `/coop test pin`. Walk out, then `/coop test goto`: "Travelling to Ghost (your pinned
   spot)..." and you end up inside on the pinned spot, behind the black screen.
4. `/coop test goto` again at once: "You can travel again in N s."
5. Start a fight (a rat, a mud crab) and while it lasts `/coop test goto`: "You can't travel during a fight."
6. `/coop test askme`: the middle of the screen says "Ghost wants to come to you: Alt+Y or /coop accept to let
   them." Press **Alt+Y**: "Test: you let Ghost come." Then `/coop test askme` again and do nothing for 20 s:
   "Test: you didn't answer Ghost (that's a no)."
7. `/coop goto lock`, then `/coop test status`: it ends with "Goto: locked (others ask)". **Server:** restart the
   server, join again, `/coop test status` still says locked. Then `/coop goto unlock`.
   **0.6.10:** a trip now waits until the chat is closed ("Co-op: close the chat to travel."), because a load with
   the chat open breaks the game. Type `/coop test goto` and leave the chat open: nothing happens until you close it
   (Enter or Esc), then the trip runs. Left open 40 s: "travel cancelled, the chat stayed open."
8. **Into a city.** `/coop test pin` inside a city (Chorrol, Skingrad...), leave the city, wait out the cooldown,
   then `/coop test goto`: you arrive inside the city on the pin, behind the black screen (the add-on's `cow` route).

Send if something fails: **server log** lines with `goto`, `travels to` or `asks to`, and **PC** `UE4SS.log` if a
trip into a building or city didn't happen.

## Part 3: the server learns the game's text from your game

So far your server always had your hand-made file, so the learning never ran ("0 of 24730 still to learn").

1. **Server:** stop the server. Move `<server folder>\coop-quest-sync-strings.json` to your desktop, and check that
   there's no `strings.json` in `<server folder>\mods\CoopQuestSync\server\`. Keep the file safe.
2. **Server:** start the server. Expected `[QuestSync]` lines:
   * `Couldn't read the game's text from OblivionRemastered-Windows.pak: ... no oo2core_9_win64.dll ...` (expected;
     the mod never downloads it);
   * `The server will learn the game's text from the players' games instead, ...`;
   * `Game data: ... 0 text entries ...`.
   Nothing new appears in `<server folder>` or `<server folder>\mods\CoopQuestSync\` (nothing downloaded).
3. **PC:** join. Once you're caught up, the server log shows `... The server has 24730 of the mod's 24730 texts
   still to learn in it.`, `Learning the game's text in en from <you>'s game ...`, then `10%`, `20%`... and after 2
   to 4 minutes `The game's text in en is complete: ... kept in <server folder>\coop-quest-sync-strings.en.json.`
   **Play normally meanwhile and tell me if the game stutters.**
4. While it's still learning: `/coop test talk`. The overlay still shows real sentences (they come from your own
   game, not the server).
5. `/coop test status` ends with `Your game's language: en (server has ... texts learned in it).` Refresh
   http://localhost:9060/: quests show their real names.
6. **Server:** restart the server. The log shows `The game's text in en (from the players' games): ...
   coop-quest-sync-strings.en.json, 24730 of 24730.`, and joining doesn't learn again.
7. **Optional, a second language:** **PC:** switch the game to French (Steam, right-click the game, **Properties**,
   **Language**), restart it and join. The server learns French into `<server folder>\coop-quest-sync-strings.fr.json`.
   When it's complete, `/coop test stage <quest> <stage>` with a stage you don't have: the chat line names the quest
   in French. Switch the game back afterwards.
8. **Put things back (Server):** move your `coop-quest-sync-strings.json` back into `<server folder>` if you want the
   hand-made one first. The learned `coop-quest-sync-strings.en.json` can stay. Never share either.

If the log says `<you>'s game didn't send the text asked for in 45 s`, check that **PC** `UE4SS.log` has
`[CoopQuestSyncUI]` lines, and send both logs.

## Part 4: the 0.5 checks nobody has done yet (alone)

1. **Esc after joining.** Right after the catch-up, press Esc: the pause menu opens. If a "Quest Added" window
   appeared, it closed by itself (**PC** `UE4SS.log`: `closed the Quest Added pop-up`). Tell me if you had to close
   one by hand.
2. **Menus.** Talk to a merchant and open Barter: the server log shows `<you> is bartering.`, then
   `<you> stopped bartering.`. (Persuasion already worked.)
   * **Lockpicking:** works since 0.6.11 (2026-11-13, the game's `WBP_ModernMenu_LockPick_C` screen).
   * **Pickpocketing:** works since 0.6.12.
   * **Caught pickpocketing (0.6.14).** On 2026-11-13 the guard's fine conversation showed as `is bartering`. Now
     pockets stay pockets when you're caught, and the conversation alone is never bartering. Get caught again and
     pay: no `is bartering`. Then barter with a real merchant: `is bartering` should still show, and **PC**
     `UE4SS.log` has an `open screens: ...` line for the barter screen: send it to me.
   * **Merchant barter (0.6.15).** On 2026-11-13 a real merchant's Barter showed as `opened an item screen, not
     sneaking: a chest, not pockets.` probably because the game ends the conversation when the barter screen opens. Now a
     screen opened during a talk, or within 3 s of it ending, is a merchant. Talk to a merchant and open Barter:
     the **server** log shows `<you> is bartering.`, the tag on http://localhost:9060/ shows **bartering**, and
     closing it gives `<you> stopped bartering.`. Also open a chest outside any talk: still `a chest, not pockets.`
     If it fails, send me the **server** log and **PC** `UE4SS.log` (path in the table at the top) lines
     with `open screens:`.
3. **Voices.** Talk to any NPC yourself for a line or two first. Then `/coop test voice`: a voice line plays and the
   chat says which. Then `/coop test talk`: Ghost's lines come with their voices. Alt+U stops the voice;
   `/coop test talk ask` (the banner) never plays one. Silent? Send **PC** `UE4SS.log` (lines with `voice`).
4. **Stage effects.** Don't reset the quest on the web page first: a quest reset while you play is held back until
   you rejoin (your game still has its old stages). 0.6.13 now says so instead of giving a token and taking it back
   at once, which is what happened with MS05 at 13:34. MS05 70 also only runs inside the quest's dream (each of its
   scripts has a condition), so it can't be tested out of order. Use the Nine Divines pilgrimage instead:
   * Save first. `/coop test effects NDPilgrim` lists `35: teleport` and `60: teleport`.
   * `/coop test stage NDPilgrim 35`: the game moves you to the sky battle (Pelinal's vision). Load your save.
   * On the page turn **Live > Teleport** off, `/coop test stage NDPilgrim 35`: you get the stage but stay put.
     Turn it back on and load your save again.
   * Items already passed: MS05 40 gave you the Dreamworld Amulet at 13:32.
5. **Not an admin.** `/coop test asghost on`: `/qs auto off` and `/coop test status` are refused with "Only server
   admins can use ...", `/qs whoami` and `/coop follow` still work, and the page shows **testing as Ghost (not
   admin)**. `/coop test asghost off` gives your rights back.

## Part 5: where new players start (alone, last)

This one needs a character still in the intro, so it hides your real one for a while. Do it last.

**Fixed in 0.6.16** (from your 2026-11-13 14:13 test):

* A new character was sent to where your nickname last played (Borba's shop in Cheydinhal) while the race menu was
  still open. The "Taking you back" trip now waits until the plugin has said whether you've left the sewers, and a
  new character is never taken back. Server log: `<name>: hasn't left the sewers, so not taken back to where they
  last played.`
* After the skip, the game stopped reading our start script at the move to the sewer exit, so the rest never ran:
  no amulet, no starter kit, no race/class/birthsign menus, and the script kept moving you back to the sewer exit
  every half second, even after you went out (that's why Esc didn't work). Now the move is the script's last step.
  Your 14:13 test character (the one whose Esc didn't work) is safe to load: it won't be moved again.

1. **Server:** stop the server. In `<server folder>\saves\players\`, move `<your ReadyM id>.sav` (your id
   starts with `7198b060`) to your desktop. That's your real character: keep it safe.
2. **Server:** start the server. On http://localhost:9060/, **Co-op settings > New players**: set **Offer the prison
   intro** to **Always** (your old character has left the sewers, so **Fresh world** would only offer the sewer exit).
3. **PC:** copy `%USERPROFILE%\Documents\My Games\Oblivion Remastered\Saved\SaveGames\` somewhere safe. Start
   Oblivion Remastered on its own (not through the launcher), **New Game**, wait until you're in the prison cell, save,
   quit.
4. **PC:** join through the launcher. You're in the cell, and after a second or two a window asks where you want to
   start. Server log: `<name> is new: asking where they want to start (prison or sewer exit).`
   If you're not in the cell, or no window appears, stop here and send the server log lines with `new player` or
   `intro`: that tells me how 1.0 starts a new character.

**5A, the skip.** Press **At the sewer exit: skip the intro**.

1. You're moved next to the sewer gate, once. Server log: `<name>: start at the sewer exit: gave the Amulet of Kings,
   <n> starter item(s).` Your inventory has the Amulet of Kings, an iron shortsword, an iron bow, 25
   iron arrows, 5 lockpicks, 3 healing potions, a torch, a leather cuirass and leather boots. The journal shows
   "Deliver the Amulet".
2. The race menu opens, then class, then birthsign. Then: "Your character is ready. The sewer gate lets you change
   it before you go out."
3. Use the sewer gate: the game's own box (Edit Race, Birthsign, Class, Finished). Press **Finished**.
4. Outside, the party's progress arrives. Server log: `<name>: out of the sewers; catching up with the party.`
5. You stay outside: walk away from the gate, then press Esc. The game's menu opens.

**5B, the prison.** **Server:** delete the test `<id>.sav` in `<server folder>\saves\players\`. Then repeat
steps 3 and 4 above with a fresh new-game save, and press **In the prison: play the intro**. The intro plays as
usual. Tell me if anything looks wrong with the Emperor, his guards or Baurus. While you're inside, the page shows you
as **new: in the intro (progress waits)**. Leaving through the sewer gate brings the catch-up, as in 5A.

**Put things back:**

* **Server:** stop the server, delete the test `<id>.sav` in `<server folder>\saves\players\`, and move your
  real one back.
* **PC:** remove the test saves from `%USERPROFILE%\Documents\My Games\Oblivion Remastered\Saved\SaveGames\` (or put
  your copy back).
* **Server:** on the page, set **Offer the prison intro** back to **Fresh world**.
* Join with your real character: nothing new happens, and `/qs newstart` lists it as "out of the sewers".

## Part 6: two players (when your friend can join)

Your friend installs nothing by hand: the launcher gives them the mod when they join. The end of their **PC**
`Plugins.txt` must read `CoopQuestSync.esp` then `CoopQuestSyncDialogue.esp`, and their game needs one restart after
the first join (the add-on installs itself, a note says so).

1. **Conversation.** Standing together, one talks to an NPC. The other sees the overlay with the NPC's name and
   lines, hears the voice, can't move until it ends, and sees a "Talking" tag over the talker. The talker opens
   Barter: "... is bartering..." on the overlay and "Bartering" on the tag. Then with the watcher in another
   building: overlay but free to move; **Alt+Y** takes them to the talker behind the black screen.
2. **Shared progress.** One advances a quest. The other gets it within seconds, with "X advanced ...". A stage reward
   reaches both.
3. **Stage effects.** One reaches a "Teleport" stage: the other moves too. With **Live > Teleport** off, the other
   gets the stage but stays put.
4. **Death.** One goes down; the other gets a chat line, stands next to them and presses **Alt+H**: they get up next
   to the reviver (after OblivionMP's reload). Next time, **Alt+G** or wait 60 s: they come back next to the other
   player, even in another building.
5. **Goto.** Your friend types `/coop goto <start of your name>`: they arrive next to you, and you see "<friend> is
   coming to you". Try with you inside a building and them outside, and with you in a city and them in the wilds. A
   minute later they press **Alt+O**: same trip, no name needed.
6. **Goto lock.** You type `/coop goto lock`. Your friend presses **Alt+O**: you see "<friend> wants to come to you".
   **Alt+Y**: they travel. Again, but ignore it: after 20 s they see "<you> didn't answer." While you talk to an NPC
   with nobody asking, **Alt+Y** on their side still takes them to your conversation.
7. **Walking during a conversation** (decides defaults, take notes). Page: **Freeze watchers** = **Nobody**. One talks
   for a minute while the other walks and fights nearby; repeat with each of you entering the area first. Do the NPCs
   near the walker move and fight normally? Does anything jump into place when the talk ends?
8. **Fights** (decides what to build next). The one who did *not* enter the area first attacks a townsperson or a
   guard. Does the NPC fight both of you or only the attacker? Is the other in combat (music, can't wait)? Then
   `/coop calm near` and the page's **Calm** button.
9. **Leader only.** Page: **Progress counts from** = **Leader only**, leader = you. Your friend's progress isn't
   shared; yours is.
10. **Admins.** Your friend's `/qs reset` is refused; **Make admin** next to them on the page lets them in.
11. **Two languages, if your friend plays in another language:** their "X advanced ..." chat lines come in their
    language, and the server learns it the first time (Part 3, step 3, with their language).
12. **Sleep and wait.** The one who entered the area second waits an hour (T): does the clock snap back? Then sleeps
    in a bed with a level ready: does the level-up still show?
13. **A new player and goto.** If your friend starts a new character (Part 5), `/coop goto` before they leave the
    sewers says "Finish the intro first: go out through the sewer gate."

## Part 7: later, on OblivionMP 1.1.0

Not now: 0.6.16 is built for 1.0 and **won't load on a 1.1.0 server**, and a 1.1 build won't load on 1.0.1. The move
to SDK 1.1.0 is ready in its own pull request. When you upgrade the server, ask for a 0.6.16 build for 1.1 (the
earlier `0.6.1-sdk1.1` zip doesn't have the 0.5.4 and 0.6.2 fixes).

1. **Server:** set up the 1.1.0 server as ReadyM describes: copy `saves`, `data`, `branding` and `config.json` from
   `<server folder>` to the new server folder, plus `coop-quest-sync-state.json` and
   `coop-quest-sync-strings*.json`. Copy the 1.1 build's `CoopQuestSync` folder into the new server's `mods` folder.
   The log's mod load order shows `CoopQuestSync` with `OblivionMP.SDK v1.1.0`.
2. **The game starts straight in the multiplayer world** (no main menu). Check the add-on still works after the
   loading screen (overlay, Alt keys), and that the join catch-up is still quiet.
3. **OblivionMP 1.1 now shares the game's own scripts**: dialogue result scripts, activators, triggers and message
   boxes are replayed on everyone's game. With our quest sync on top, things may happen twice. Watch for:
   * a quest pop-up or journal entry twice, or for a player who wasn't in the conversation;
   * items or gold given twice, NPCs moving or attacking twice, a stage effect although its switch is off;
   * the new-player window (Part 5) appearing on the other player's screen.
   Note the quest and stage and whether the other player was nearby. If it happens, the mod can turn OblivionMP's
   replay off for dialogue results and leave quests to our sync.
4. **New players on 1.1** have no local save to start from, so redo Part 5 there: what does a brand-new player get?
5. Then a quick pass of Parts 1, 2 and 6 on 1.1.

## Already done (no need to redo)

From your tests on 2026-11-07, 2026-11-12 and 2026-11-13:

* Admin commands: the ReadyM panel's admin list is read, `/qs whoami` and `/qs auto` work.
* The server uses your hand-made text file.
* Joining: silent restore and catch-up; back where you left, outdoors and inside buildings, behind the black screen.
* Conversation overlay with Ghost, freeze, Alt+U, persuasion detection.
* Alt+Y into a dungeon works (only the last fade was missing: Part 1.1).
* `/coop calm` (all) inside a dungeon, with no crash.
* Respawn alone after 60 s, back on your last safe spot.
* The add-on no longer crashes on loads.
* 2026-11-13: calm (near, by name, all), the down note and OblivionMP's own rebirth, the revive, the voice cut off
  when you leave a conversation, and Part 2 tests 2, 3, 6 and 8.

## Things to watch while testing

* On 2026-11-12 the server once logged `the plugin never answered the handshake. Syncing without restoring.` after a
  rejoin. If you see it, note what you did just before (a reload, R, a rejoin) and send the server log.
* The crash when quitting the game is OblivionMP's (CEF shutdown), not the mod's.
* The co-op keys also reach the game (Alt+O also presses O). If one clashes, pick another letter under **Keys** on
  the page.

## What to send me

* **Server:** the server log (`[QuestSync]` lines), from start-up to the problem.
* **PC:** `UE4SS.log`, copied before you start the game again.
* A screenshot for anything on screen (overlay, black screen, window).
* Say which Part and step. Never send `coop-quest-sync-strings*.json` or `strings.json` anywhere public.
