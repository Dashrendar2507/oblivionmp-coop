# Changelog

## Unreleased

* **New players choose where to start**: a player who has never left the sewers gets the game's own message box on
  their first join: "In the prison: play the intro" or "At the sewer exit: skip the intro". The prison is only
  offered until someone has left the sewers on the server (the Emperor and the Blades are shared, so the intro plays
  once per world). The sewer exit start sets "Deliver the Amulet" in the player's journal, gives the Amulet of Kings
  and a starter kit, puts them just inside the sewer gate, and opens the race, class and birthsign menus; the gate
  then offers to change them again, as in the game. Settings "New players start", "Offer the prison intro",
  "Starter kit" and "Take back spare amulets"; `/qs newstart`. config.json: `StarterKitItems`.
* **The party's progress no longer breaks a new player's intro**: shared stages wait until the player is out of the
  sewers. Replaying "Deliver the Amulet" on join stopped the intro and left a new player stuck in the prison cell.
  Players who haven't left the sewers aren't taken back to where their nickname last played, and can't travel to a
  teammate until they're out.
* The plugin generator's script compiler knows `MessageBox` with buttons, `GetButtonPressed`, `MoveTo` and
  `AddTopic`, checked byte for byte against the game's own scripts.
* **Revive and respawn bring you back to the party.** OblivionMP brings a dead player back with its "rebirth", which
  reloads the player's own save, so Alt+G put you wherever your save was (2026-11-12), and the server's move right
  after it was lost during the load. Now the travel screen goes up before getting back up, the client tells the
  server when the reload is over, and the server then takes you next to whoever revived you, next to the leader
  (any area now, into buildings and cities through the add-on), or back to your last safe spot. Nothing moves you
  while the reload is still running.
* **Every move through a load is behind the black screen**, not only a join's or a trip to a teammate's: the test
  join with Ghost (Alt+Y during `/coop test talk`), `/coop test back` and `/coop test goto` showed the new place and
  then the jump.
* **Travel to a teammate any time**, not only to a conversation: `/coop goto <name>` (the start of a name is
  enough) takes you next to them, into their building or world space too, behind the black travel screen. The new
  goto key, **Alt+O**, goes to the last teammate you picked, else the leader, else the only other player. Not while
  down, talking or (setting) fighting, and once per cooldown (60 s). The teammate sees "Alice is coming to you".
  `/coop goto lock` makes teammates ask first: you see "Alice wants to come to you" and let them with Alt+Y or
  `/coop accept` within 20 s. Kept in the state file. Settings "Travel to teammates any time", "Seconds between
  trips", "No trips during a fight", and the Goto key.

* **The game's text survives updates**: the server keeps it as `coop-quest-sync-strings.json` in its own folder,
  next to `coop-quest-sync-state.json`, instead of `strings.json` inside the mod's folder (an update wiped it and
  quest names showed as ids). An old `strings.json` next to the DLL is copied there once.
* **Esc works after the catch-up**: the add-on closes each Quest Added pop-up only with the game's own Continue. Its
  fallback, CommonUI's DeactivateWidget, hid the pop-up but left the game thinking a menu was open.
* The world fades to black around a move only with a client that brings it back (it writes `fade-ok` in the add-on
  folder); an older client left it black for 40 s. At most 20 s now.
* **Admin-only commands**: `/qs push`, `/qs auto`, `/qs source`, `/qs reset`, `/coop calm`, `/qsprobe` and
  `/coop test` are for admins: the ReadyM admin panel's admins (read from its `data/web.db`), plus players added
  with **Make admin** on the co-op page. **Co-op settings > Commands** can open them to everyone. `/qs whoami` shows
  your ReadyM id and status; `/coop test asghost on|off` tests the refusals alone. config.json: `PanelDatabase`.
* **Black screen while travelling**: when the server puts a player back where they left on join, or takes them to a
  teammate (Alt+Y) through another load, their screen fades to black with one line ("Taking you back to where you
  left...") and lifts once they stand in the right spot, so the save's spot, the door and the jumps stay hidden. It
  steps aside while a menu stays open for more than 2.5 s (a quest pop-up the add-on couldn't close), until the
  add-on has run the move; the add-on's own quick close of the catch-up's pop-ups no longer flashes the game. A coc or cow tears
  down OblivionMP's pages as its load starts, so the add-on first fades the world to black with the game's camera
  fade and brings it back once the client sees the player in the new place (20 s at most): no second of the game
  before the load. Setting "Black screen while travelling" (on).
* **Console commands wait for a quiet moment**: the add-on runs them one at a time, not during the join's quest
  catch-up, and not while a menu is open or within 2 seconds of one opening or closing. A `coc` run just as the game
  closed the quest pop-up crashed the game (2026-11-07). After 30 seconds a command runs anyway.
* Back where you left: the server checks 2, 5 and 10 seconds after the move whether the game put the player back at
  the door, and moves them again if so.
* **Travel to a conversation**: while a teammate talks to an NPC, Alt+Y takes you next to them, whether you're
  listening in or not, into their building or another world space too (through the add-on). Setting "Travel to a
  conversation"; `/coop join` does the same.
* **Calm fights**: `/coop calm` (your area), `/coop calm <name>` (their area, and their bounty), `/coop calm all`
  and `/coop calm near`, and **Calm** buttons on the web page, for fights that went wrong. Through the add-on.
* **The follow key is now Alt+U** (it was Alt+L; L also hides the game's interface, compass included). A server
  that had L moves to U.
* The add-on's console commands (pin/back, travel, calm) use the call other Oblivion Remastered mods use.
* Console commands try the shapes OblivionMP's UE4SS accepts (it wants four arguments where other mods pass three).
* Voices play at the player's spot without a callback (the callback version crashed the game).
* The game's text lookup reads the list of string tables the way OblivionMP's UE4SS returns it.
* `/coop calm` leaves alone fighting NPCs the server can't place in an area: StopCombat on those crashed the game.
* **The "Quest Added" pop-up closes itself during the join's catch-up**: the add-on presses its Continue
  (OnQuitButtonPressed on WBP_ModernMenu_QuestSkillPopup_C, found with a diagnostic on 2026-11-07), so joining stays
  silent. A quest that arrives later, live, still shows its pop-up.
* The restart note waits until you're in the world (one made during the join's loading screen was lost).
* The "you are down" message warns that R is OblivionMP's own rebirth, which reloads the save.
* **Fewer crashes on load**: the add-on no longer watches every new object the game makes (thousands during a load),
  no longer keeps the player controller, and does all its game work in one job at a time on the game thread. The
  crashes on loading a save and after the join pop-up came from UE4SS's background thread. The HUD hook is
  registered when the add-on starts, Lua's garbage collector only runs on the game thread, and the add-on no longer
  reloads itself while the game runs.
* An empty file named `off` (or `off.txt`) in the add-on folder switches the add-on off (restart the game), to tell
  whether a crash comes from it; one named `lite` stops only its regular menu checks.
* Voices are looked up in the game's own folders (one per race and sex).
* A revive or respawn waits until the chat or menu is closed (getting up with the chat open left it stuck).
* The server log says who fights where and in whose game (`Combat:` lines), to see what is shared already.

* **Voices for watchers (experimental)**: while listening in, you hear the NPC's voice with each line, from nowhere,
  using the game's own sounds. Setting "Watchers hear the voices". `/coop test voice` plays one.
* **No game text to set up**: each player's add-on looks the overlay's text up in the running game, in the language the game is set
  to. The server reads it too when the game is installed on the server computer (`GameFolder` and `Language` in
  `config.json`), for the web page. The FModel route still works as a backup. Nothing is ever downloaded.
* The dialogue plugin no longer carries the voice actors' direction notes.
* **Restart note**: when the co-op mod updates the add-on, a note stays in the top left corner until you restart the
  game (the add-on loads once, when the game starts).
* The conversation overlay steps aside while the map or another menu is open.
* Fixes: silent catch-up on join (the HUD's text has a hidden prefix), the add-on's console commands, and a join
  that skipped the restore after a long loading screen.

## 0.5.0 (public test)

* **Stage effects for everyone, with switches.** Quest stage scripts that act on the player are tagged Teleport,
  Combat, Items, Status, Faction ranks or Cut scene (446 stages in 181 quests). Each tag has a switch for live,
  on join and on rejoin.
* **Knights of the Nine, the DLCs and the remaster's own plugins** are covered (287 quests).
* **Shared conversations**: an overlay shows teammates' conversations, players in the same area are frozen until
  they end, and menus (barter, persuasion, training, lockpicking...) are announced. Alt+L leaves or follows a
  conversation, and following can be made opt-in.
* **Revive and respawn**: you go down instead of dying. Alt+H revives a teammate, Alt+G respawns next to the party.
* **Back where you left**: on join you come back where you left, inside buildings too.
* "Leader only" progress, chat announcements, shared dialogue rewards, optional quest-item sharing, tags over
  heads, level scaling and difficulty.
* Every setting is on the web page and kept across restarts. Keys can be changed there.
* The `CoopQuestSyncUI` UE4SS add-on now installs itself on the first join (restart the game once).
* Players no longer copy plugins by hand: the launcher installs them on join.
* `/coop test` commands to try everything alone with a pretend teammate.

## 0.4.0

* Web admin page on port 9060: every quest, its stages and who has them; give a stage to everyone, reset a quest.
* Source player: share only one player's progress.
* The tutorial (MQ01) is no longer shared: it caused pop-ups on rejoin.

## 0.3.0

* All vanilla quests.
* Silent catch-up on join (no quest pop-ups), through the `CoopQuestSyncUI` add-on.
* Join handshake, and rewards from replayed stages are taken back.

## 0.2.0

* First working sync, for two quests: a stage finished in one game reaches the server and the other players, and
  comes back after a rejoin.
