# Changelog

## Unreleased

* **Travel to a conversation**: while a teammate talks to an NPC, Alt+Y takes you next to them, whether you're
  listening in or not, into their building or another world space too (through the add-on). Setting "Travel to a
  conversation"; `/coop join` does the same.
* **Calm fights**: `/coop calm` (your area), `/coop calm <name>` (their area, and their bounty), `/coop calm all`
  and `/coop calm near`, and **Calm** buttons on the web page, for fights that went wrong. Through the add-on.
* **The follow key is now Alt+U** (it was Alt+L; L also hides the game's interface, compass included). A server
  that had L moves to U.
* The add-on's console commands (pin/back, travel, calm) use the call other Oblivion Remastered mods use, and the
  add-on no longer keeps the game's player controller across a load (a likely cause of crashes on loading a save).
* Voices are looked up in the game's own folders (one per race and sex).
* A revive or respawn waits until the chat or menu is closed (getting up with the chat open left it stuck).
* The server log says who fights where and in whose game (`Combat:` lines), to see what is shared already.

* **Voices for watchers (experimental)**: while listening in, you hear the NPC's voice with each line, from nowhere,
  using the game's own sounds. Setting "Watchers hear the voices". `/coop test voice` plays one.
* **No game text to set up**: each player's add-on looks the overlay's text up in the running game, in the language the game is set
  to. The server reads it too when the game is installed on the server computer (`GameFolder` and `Language` in
  `config.json`), for the web page. The FModel route still works as a backup. Nothing is ever downloaded.
* The dialogue plugin no longer carries the voice actors' direction notes.
* The add-on reloads its logic while the game runs, so add-on updates no longer need a restart.
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
