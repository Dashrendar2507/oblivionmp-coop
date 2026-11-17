# Changelog

## 0.9.0 (test build)

* **Deaths by script are shared (world state sync, part 1).** Vanilla quests kill many NPCs with a script command
  instead of damage (106 actors in Oblivion.esm: Captain Renault, Glenroy and the Emperor in the prison, quest
  victims...). Under OblivionMP that death stayed in the game that ran the script: the server kept the NPC alive and
  brought it back for everyone. The plugin now watches those actors, and when one dies in a player's game while they
  play, the server kills it too, so it is dead for everyone, including players who join later. Actors a script also
  brings back (the Arena's combatants) are left to the game.
* **Captain Renault dies in the first ambush of the prison intro.** In the game she dies by script when the assassins
  strike, but under OblivionMP that death stayed in one player's game: the server kept her alive and brought her
  back, and the intro waited for her. The server now kills her at the same moments (an assassin hits her, or
  assassin 1 or 2 is hit), and in a world where the intro got stuck, once all four assassins are dead. The plugin
  then moves the intro on. The assassins' missing armor and mace (summoned by a spell OblivionMP doesn't keep) are
  still OblivionMP's to fix.

## 0.8.2

* **Back where you left after leaving the sewers.** Every join loads the server's start save, the prison, so the
  game thought a returning player's intro was under way again: the mod kept them in the prison cell instead of
  taking them back, held the party's progress, and the vanilla tutorial ran again. The plugin now knows from the
  character's own "left the sewers" quest token (MQ01 88, kept on the server) that they are out, and ends the intro
  in their game at once. Players who already left on 0.8.0 or 0.8.1 have that token, so it works for them too.
* **Rejoining before going through the sewer gate puts you back at the sewer exit** (after the skip), instead of in
  the prison cell with the intro running.
* **Choosing the prison intro opens the race menu at once** (name, race, face). The start save is past the intro's
  own race menu; the intro still shows the birthsign and class menus later.
* A new character with a nickname that was used before is never taken to the old character's last spot.

## 0.8.1

* **Giving a stage on the web page right after resetting its quest works.** It reached nobody ("pushed MQ02 10 to
  0 player(s)"): after a reset, the server kept taking every token of that quest from players in game until they
  rejoined, including the ones an admin gave again. Now only the stages their game already had stay taken; a stage
  an admin gives (page, `/qs push`) or that they reach from then on counts at once.
* **Giving a stage to everyone waits for players in the prison intro.** They get it with the rest of the party's
  progress when they leave the sewers, as with auto sync, instead of in the prison (MQ02 stages end the intro). The
  log says who got it now and who waits.
* **Resetting a quest says what happens in game.** It is out of the shared progress at once; a player whose game
  has stages of it gets a chat line that their journal keeps it until they rejoin (nothing in the game can undo a
  quest stage while it runs). The server log names them, or says nobody in game had it.

## 0.8.0 (first public release, for OblivionMP 1.1)

The 0.7.0 test builds were never released; everything in them is here.

* **Built for OblivionMP 1.1.0** (SDK 1.1.0). It doesn't load on an OblivionMP 1.0.x server, and 0.6 and older don't
  load on 1.1. Moving a server: see "Moving from an OblivionMP 1.0 server" in docs/INSTALL.md.
* **New characters are set up on their first join.** OblivionMP 1.1 starts every new character as the same preset
  (a level 1 Dunmer near Aleswell, with that character's gear and a few quests under way). A character with no save
  on the server yet gets an empty bag except the starter kit, then the race, class and birthsign menus. The quest
  stages that preset comes with are never shared or announced, and the party's progress reaches the new player once
  the setup has run. Setting "Set up new characters" (on), with "Starter kit". The setup first takes them to the
  sewer exit, just inside the gate where the game's own start ends, and opens the menus there (setting "New
  characters start at the sewer exit", on; needs the CoopQuestSyncUI add-on).
* **Friendly fire is off by default**: OblivionMP 1.1 tells the server who hit whom, so a player's hit on another
  player is refused and does no damage. Healing still works, and NPCs hurt everyone. Setting "Friendly fire".
* **Esc and the inventory work after a rejoin and after a respawn.** On OblivionMP 1.1, moving a player by
  writing their position from the server left Esc and the inventory dead until the next loading screen, so
  every rejoin outdoors ("Taking you back to where you left") and every Alt+G respawn locked them. Players are
  now moved through their own game's console (`player.positionworld` outdoors, `player.positioncell` inside),
  which needs the CoopQuestSyncUI add-on, like trips into buildings already did.
* **Dying no longer locks the menus for good.** OblivionMP 1.1's rebirth (its R key, and our revive and respawn,
  which use it) heals the player in their game, but its server ignores that heal for a dead player, so the
  character was saved dead and Esc and the inventory never opened again for it. The server now gives the health
  back itself once the player is up, and on joining for a character that was already saved dead. The rebirth's
  reload doesn't run on 1.1 either: if you're still on the ground afterwards, quit and rejoin (the game says so).
* **New characters start in the prison cell.** OblivionMP starts every new character from the server's start save
  (world.sav in its modloader folder), by default a ready-made character near Aleswell. The mod now ships a save
  made in the prison cell and puts it there each time the server starts, keeping OblivionMP's own file next to the
  state file (coop-quest-sync-original-world.sav). Setting "Start in the prison" (on by default) turns it off and
  puts OblivionMP's save back, from the next server start. Players' games get the new save on their next join.
  New characters then get the game's own window: play the prison intro, or skip to the sewer exit. The prison is offered until someone has played the intro and left the sewers, and again
  on a new server world. New characters from OblivionMP's own save still get the setup above. The stages a new
  character starts with are taken from the latest new character, so a changed start save is picked up by itself.
  "New players start" is now "Ask" by default.
* The start window stays up until it is answered: OblivionMP doesn't pause the game for it, so a timer used to show it
  a second time underneath, and it came straight back after the first click. It now comes back only if it was lost
  (by a load) and the player walks about. The remaster puts "[NL]" (no localized text) in front of a plugin's text;
  the CoopQuestSyncUI add-on takes it off the start window's question and buttons. After a sewer exit skip, the race, class and birthsign menus wait until the player has
  arrived there. The window waits until no other menu is open, and the skip waits until every menu is closed: the
  move to the sewer exit with the Esc menu still open under the window left the race menu showing only its camera.
* Our plugins go right after AltarESPMain.esp in the server's load order, where the ReadyM Launcher (0.32+) puts
  them, so DLC and Altar items keep their ids.
* Includes everything from the 0.6 test builds below.

**From the 0.6 test builds**

* **New players choose where to start**: a player who has never left the sewers gets the game's own message box on
  their first join: "Play the prison intro" or "Skip to the sewer exit". The prison is only
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

* **OblivionMP's own rebirth is a respawn.** OblivionMP gets a dead player up by itself after some seconds (and at
  once with R, even while typing in the chat). The mod now notices it and, once its reload is over, takes the
  player to the party or their last safe spot, as with Alt+G. While you're down, a note under OblivionMP's "You
  died" message says what to do. `/coop test heal` is `/coop test revive` without an R in it.
* **No endless load after Alt+G**: the travel screen no longer goes up while the rebirth's reload runs. After a
  revive, the trip waits for that reload to settle (6 s), and a move the game ignored falls back to `coc`.
* **A trip waits for the chat to close** ("Co-op: close the chat to travel."): a load with the chat open breaks
  OblivionMP's chat. Cancelled after 40 s.
* **Arrive inside on the spot**: travel into a building or dungeon goes straight to the spot with `positioncell`
  instead of the door first, so NPCs by the door don't greet you on the way in. config.json `ArriveInsideDirectly`.
* **Calmer join**: the add-on waits 8 s after the catch-up and 4 s after a menu before a console command, after a
  `coc` crash on join; a trip that didn't land after 45 s tries another way.
* **Lockpicking, pickpocketing and bartering shown again**: the remaster's own screens don't raise the original
  game's menu numbers, so the add-on now tells the server which screen is open. Getting caught pickpocketing isn't
  bartering, and a merchant's barter screen counts as bartering even after the remaster ends the talk.
* `/coop calm near` keeps the NPCs calm; the voice stops at once when you leave a conversation.
* **New-start fixes**: skipping the intro now finishes (kit, character menus), and a brand-new character with an old
  nickname isn't taken to that nickname's last spot from the race menu.
* A quest reset during a session can't be undone by a stage pushed for that quest in the same session.
* Web page: the keys' description gets the full width.
* **Docs** rewritten for the public release: every install step says whether it happens on the server or on a
  player's PC, with the full path.

## 0.5.4 (public test)

* **`/coop calm near` calms every NPC fighting within 15 m of you**, plus the closest one. It used to calm only the
  closest: with two attackers, the other kept fighting and the calmed one joined back in.

## 0.5.3 (public test)

* **`/coop calm` works in dungeons.** OblivionMP gives most NPCs inside dungeons no area on the server, so `/coop calm`,
  `/coop calm near` and `/coop calm <name>` skipped them and only `/coop calm all` stopped the fight. Those NPCs now
  count when they are within about 40 m of a player in that area (`near` keeps its 15 m). The log says how many were
  taken this way.
* **`/coop calm <name>` finds the player** without minding case or the dot at the end of a ReadyM name
  (`dashrendar2507` finds `dashrendar2507.`), and with just the start of a name when only one player matches.

## 0.5.2 (public test)

* **The server learns the game's text from the players' games**, so nobody makes a file by hand. Each player's
  client says which language its game runs in; for each language the server lacks, it asks one player of that
  language for the text, 200 keys at a time, in the background once they're in the world, and their add-on looks it
  up in the running game. Kept as `coop-quest-sync-strings.<language>.json` next to the state file. "X advanced ..."
  chat lines now reach each player in their own game's language. `/coop test status` shows your game's language.
  A host's own `coop-quest-sync-strings.json` still comes first. config.json: `LearnTextFromPlayers` (on). No add-on
  change, so no game restart.

* **Docs**: the README, install guide and known issues say why the server can't read the game's text from its files
  on a standard Steam install (no `oo2core_9_win64.dll`, and the mod never downloads one) and list every log line
  about it. `mod/TESTING.md` is rewritten, with a test for the server's text and the PC or server location of every
  file.

## 0.5.1 (public test)

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
