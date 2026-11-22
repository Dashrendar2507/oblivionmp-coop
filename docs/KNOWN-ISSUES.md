# Known issues and limits

Co-op Quest Sync is young: this is its first public release. These are the things we already know about. If you hit one of them, there's
no need to report it again, unless you have something new to add (a quest where it happens, a log, a way around
it). OblivionMP itself also has [known issues](https://docs.ready.mp) that aren't caused by this mod.

## Before you install

### Everyone needs the Deluxe plugins

Our plugins are built on top of every official plugin of the game, including `AltarDeluxe.esp`. A player whose
game is missing one of them can't load our plugins, and nothing gets synced for them. If that's you, please
[open an issue](https://github.com/Dashrendar2507/oblivionmp-coop/issues/new/choose) with your `Plugins.txt`: a
version without the Deluxe dependency can be built.

### Who counts as an admin comes from the panel's own database

The SDK doesn't tell mods who the server's admins are, so the mod reads the ReadyM admin panel's database
(`data/web.db`) to find them. That file's layout isn't documented and could change with a server update. If it can't
be read, the server log says why, and only the admins added on the co-op page (**Make admin**) can use the admin
commands.

### The game's text comes from the players' own games

Each player's add-on looks the overlay's text up in the running game, so the overlay always has words. The server
can't read the game's files on a standard install (the remaster's text is Oodle-compressed and the game ships no
`oo2core_9_win64.dll`; the mod never downloads anything), so it learns the text from the players' games instead:
a few minutes in the background, the first time a player of each language joins. Until then the web page shows
names worked out from the game's ids.

* Any player of a language can teach the server that language's text, so a player with a modded or tampered game
  could put odd names on the page and in chat. Only keys the server asked for are taken, the first answer wins, and a
  host's own file always comes first. Delete `coop-quest-sync-strings.<language>.json` to learn it again, or set
  `"LearnTextFromPlayers": false`.
* The web page shows one language (`"Language"` in config.json, English by default, or the first one learned).
* Only English has been tried so far.

## OblivionMP 1.1

Version 0.8.0 is the first release for OblivionMP 1.1, and it has had much less play on 1.1 than on 1.0. It runs on
1.1.0 and 1.1.1 (1.1.1 recommended: it no longer counts a player as talking when two NPCs chat next to them, which
could show a conversation overlay or hold quest stages for nothing). Things 1.1 changed that we haven't tested
enough yet:

* **Getting back up after dying may need a rejoin.** On OblivionMP 1.1, the rebirth's reload (after **R**, Alt+G,
  a teammate's revive or OblivionMP's own timed rebirth) doesn't run, so your character can stay on the ground in
  your game. When that happens the mod says "Still on the ground? Quit and rejoin": leave and rejoin the server, and
  you're back on your feet. This is in OblivionMP's rebirth, not the mod, and we're reporting it to ReadyM.
* **Esc and the inventory locking is fixed.** Test builds before 0.8.0 could leave Esc and Tab dead after a death, a
  rejoin outdoors or an Alt+G respawn: OblivionMP saved a reborn character dead, and a position change sent from
  the server froze the menus until the next loading screen. The server now gives reborn players their health back
  (also for a character that was saved dead), and moves players through their own game's console instead, which
  needs the CoopQuestSyncUI add-on. If a character's menus still don't open after a rejoin, please report it. The
  way out is a new character: on the **server**, with the server stopped, move
  `<server folder>\saves\players\<ReadyM id>.sav` out of the folder (the server log names the id in a
  `[QuestSync] Player <id>:` line each time they join). The shared quest progress stays, and the mod catches the
  new character up.
* **The game's own scripts are now shared by OblivionMP.** By default, OblivionMP 1.1 replays conversation result
  scripts, scripted activations (levers, buttons), triggers and message-box buttons on nearby players' games. With
  the mod's quest sync on top, a quest stage, a journal entry, gold or an item may arrive **twice**, once from
  OblivionMP and once from the mod, or reach a player who wasn't in the conversation. If you see it, please report
  the quest and stage, and whether the other player was nearby.
* **Other changes scripts make to the world stay in one player's game.** OblivionMP shares NPC health, positions
  and containers, but not what a script changes otherwise. Since 0.9.0 the mod shares NPCs killed by script. Still
  not shared: Oblivion gates (a gate a friend closed stays open in your game, and each game picks its own random
  gates to open), and things a script shows, hides, locks or unlocks. That's the next part of the work.
* **Some NPCs a script brings into the world may still never appear.** Many quests keep an NPC hidden until a script
  shows it. Since 0.9.1 the server makes these NPCs itself so the script can show them, but not the hidden leveled
  creatures (a random pick from a list, about 400), nor anything that appears because a hidden marker is switched
  on. Please report the quest if someone who should show up never does.
* **The game starts straight in the multiplayer world**, without the main menu, and there are no local saves any
  more. The mod's add-on, the silent catch-up on join, the new-player start and getting back up after death haven't
  been tested much this way. Reports welcome, good or bad.

* **"Your plugin load order differs from the server's, DLC areas will not sync"** shows briefly on every join. The
  ReadyM Launcher adds the mod's two plugins to the game's list after `AltarESPMain.esp` and before
  `AltarDeluxe.esp`, and OblivionMP then finds `AltarDeluxe.esp` in a different place than the server has it. The
  mod's quest sync isn't affected. Content from the Deluxe plugin may not sync between players. This needs a fix in
  the launcher; adding the plugins to `Plugins.txt` by hand doesn't help (it lists them twice and breaks the sync).

## Quests

* **Quest scripts run in each player's game.** When a stage is shared, the game runs that stage's script for every
  player who gets it. Most stages are fine. The effect switches on the web page decide whether teleports, fights,
  items, statuses, faction ranks and cut scenes happen for the players who didn't reach the stage themselves. Some
  quests may still behave oddly, especially ones that depend on where you are or on an NPC being alive. Please
  report the quest and stage.
* **Your own quests are set again every time you join.** OblivionMP doesn't keep the game's quest progress between
  sessions, so the mod sets it again. Rewards you get again from this are taken back (`UndoReplayRewards`).
* **The tutorial (MQ01) isn't shared.** It's the game's help pop-ups, not a real quest.
* **"Leader only"** shares only the leader's stages. Stages others reached before you switched stay shared.

## New players

* **The mod changes the server's start save.** OblivionMP 1.1 starts every new character from `world.sav` in the
  server's `modloader` folder. With "Start in the prison" on, the server mod writes its prison-cell save over that
  file each time the server starts, and keeps OblivionMP's own as `<server folder>\coop-quest-sync-original-world.sav`.
  An OblivionMP update that brings back its own file is replaced again on the next start. If you swapped
  `world.sav` by hand before, there's no OblivionMP original to put back when you turn the setting off.
* **The prison intro plays once per world.** The Emperor, the Blades and the prison doors are shared world state.
  Once someone has played the intro and left the sewers, later new characters start at the sewer exit. A new server
  world offers the prison again. A player who leaves the game during the intro and comes back is put at the sewer
  exit: the prison can't be played twice.
* **The Mythic Dawn assassins in the prison and the sewers look naked and fight bare-handed.** Their armor and mace
  come from a spell, and OblivionMP dresses NPCs only with the items the server has for them, so the summoned gear
  is dropped. This is OblivionMP's to fix. Captain Renault still dies in the first ambush (the server mod sees to
  it since 0.9.0).
* **"[NL]" in the start window without the add-on.** The remaster puts "[NL]" in front of plugin text its own
  string tables don't have. The CoopQuestSyncUI add-on takes it off the start window; with the add-on off, the
  question and both buttons show it.
* **Characters already saved on the server don't change.** Only new characters start from the prison save.
* **With OblivionMP's own start save** ("Start in the prison" off), every new character is the same ready-made one.
  The mod's "Set up new characters" clears its bag and opens the character menus, but its quest stages (Deliver the
  Amulet started, the Knights of the Nine prophet, MS47...) stay in the new player's own journal: the game can't
  un-set a quest stage. They're kept out of the party's progress.
* **A character only counts as new** when the server has no player save for them when they join. To set someone
  up again, stop the server and move their `<server folder>\saves\players\<ReadyM id>.sav` aside.

## Conversations

* **The overlay runs a few seconds behind.** The game reports a line when it ends, so watchers read it after the
  talker heard it. The voice plays with the text, from nowhere (not from the NPC). It
  should stop when you leave the conversation (Alt+U); tell us if a line keeps playing.
* **Some lines have no voice.** For 5 seconds after you join, go through a door or walk into another part of the
  world, lines show without their voice: playing one while the game loads the world around you crashed the game.
  If the game still crashes while listening in, the host can untick "Watchers hear the voices" on the settings page.
* **You can still turn the camera while frozen.** The freeze stops walking and fighting, not looking around.
* **Menus might not be recognised.** Persuasion, training, repair and the spell screens are detected by the
  original game's menu numbers. Lockpicking, pickpocketing and bartering come from the add-on, which sees which of
  the remaster's screens is open, and the server tells a merchant's barter from a chest by whether a conversation
  was going on. If a tag or the overlay says the wrong thing (or nothing), tell us what you were doing.
* **The NPC's name** on the overlay sometimes falls back to the NPC closest to the talker, which can be wrong in a
  crowd.
* **Letting watchers walk around** during someone's conversation ("Freeze watchers: Nobody") hasn't been tested
  much. NPCs near the walker may stand still or jump into place. Reports welcome.
* **The compass may disappear** while a co-op banner or overlay is on screen.
* **Travelling to a conversation (Alt+Y) or to a teammate (`/coop goto`, Alt+O)** into another building or world space relies on the add-on running the
  console's `coc` or `cow` command. You arrive on a spot the talker walked through a moment ago, or a step beside
  them, which in a tight room can be against a wall.

## Party

* **Friendly fire off covers player hits only.** With "Friendly fire" off, the server refuses a player's hit on
  another player, so it does no damage. Healing still works, and NPCs still hurt everyone.
* **Fights aren't shared yet.** When one player starts a fight, the others' games don't join it on purpose. NPCs run
  in one player's game (the area's master), and the server can't order an NPC to attack a given player. The server
  log now says who fights where (`Combat:` lines), to find out what OblivionMP already shares.
* **`/coop calm` stops fights, not grudges.** An NPC that is still hostile (an enemy faction, a low disposition) can
  start again. Guards also come back while you still have a bounty: calm that player by name to clear it. Inside
  dungeons the server doesn't know which area an NPC is in, so calm takes the fighting NPCs within about 40 m of the
  players there instead: a fight further away needs `/coop calm all`.
* **Followers and companions** aren't shared or synced (OblivionMP's SDK doesn't give mods a way to yet).
* **Getting back up reloads your character** (on OblivionMP 1.0; on 1.1 see [getting back up after dying needs
  a rejoin](#oblivionmp-11)). OblivionMP brings a dead player back with its "rebirth", which
  reloads you. The mod then takes you back to the party (through the add-on's `coc`/`cow` when that's another
  building or city), so it takes a load or two, behind a black screen.
* **OblivionMP brings you back by itself.** After some seconds down (about 10 to 25 in our tests), OblivionMP's
  own rebirth gets you up whether or not a teammate is coming, and **R** does it at once. The mod treats both as a
  respawn and takes you to the party, so the 60 seconds to wait for a revive are often cut short.
* **The co-op keys also reach the game.** Alt+U also presses U in the game, and so on. If that's a problem, pick
  other letters on the web page.

## Back where you left

* Going back **inside a building** relies on the `CoopQuestSyncUI` add-on running the console's `positioncell` or
  `coc` command. If that fails, you come back outside, by the door.
* **The game occasionally crashes when the add-on runs `coc`**, mostly right after a join. The crash is inside
  OblivionMP's console code and happens at random; the add-on waits for a quiet moment before every console
  command to make it rarer. If it happens to you often, the host can untick **Put players back where they left** on
  the web page: you then come back wherever OblivionMP puts you.
* Going back **outside** in another world space (a city, the Shivering Isles) relies on the add-on running the
  console's `cow` command; the server then puts you on your spot. Without the add-on, it only works when your local
  save starts you in the same world space as your last outdoor spot.
* OblivionMP has no way for mods to save your character, so your spot is only as fresh as the last time the server
  saw you.
* The game's own loading screen for the building still shows; the black travel screen hides the jumps around it.
* **A trip waits for the chat to close.** A load with the chat open breaks OblivionMP's chat (below), so a move
  through a load says "Co-op: close the chat to travel." and waits. If the chat stays open for 40 seconds, the trip
  is cancelled.

## OblivionMP issues seen while testing

These come from OblivionMP itself, not the mod; they're listed so you don't chase them:

* **A crash when quitting to desktop**, while OblivionMP shuts its browser down.
* **R is OblivionMP's "rebirth", even while you type in the chat.** Pressed while down, it gets you up at once,
  which skips a teammate's revive (the mod still takes you to the party). Typing a word with an R in the chat while
  you're down does the same.
* **OblivionMP gets a dead player up by itself** after some seconds (see Party, above).
* **Paying a fine to a guard can move you** somewhere else. The mod doesn't move you there (its logs show nothing at
  that moment); it comes from OblivionMP.
* **A console command (`coc`) occasionally crashes the game** (see Back where you left, above).
* **A reload with the chat (K) open** can leave the chat stuck until you restart the game. The mod's own trips wait
  until the chat is closed.
* **"Multiple entities mapped to the same game object"** errors flooding UE4SS.log: the server ended up with two
  copies of one NPC (seen with an NPC who walked through a load door by himself). It repeats every time someone
  enters that area.

## The UE4SS add-on

* After the mod installs or updates the add-on, you have to **restart the game once** for it to start. A note in the
  top left corner says so until you do.
* If `UE4SS.log` has no `[CoopQuestSyncUI]` lines after that restart, the add-on isn't running: quest pop-ups may
  show while you catch up, and "back inside" won't work. Please report it with the log.
* The add-on runs console commands (coc, cow, positioncell, calm) only when the game is quiet: not during the join's
  catch-up nor in the 8 seconds after it, and not within 4 seconds of a menu opening or closing (a command run then
  crashed the game). After 30 seconds it runs anyway.
* If a "Quest Added" window during the catch-up can't be closed (UE4SS.log says `could not close`), close it by
  hand; the black travel screen steps aside for it after 2.5 seconds.
* **If the game crashes on load**, put an empty file named `off` (`off.txt` works too) in
  `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` and restart the game. That switches the add-on off
  (UE4SS.log says "switched off"); quest sync still works, but pop-ups show during catch-up and travel, back and calm
  don't. If the crashes stop, the add-on is the cause: tell us, with UE4SS.log from a crash with it on. Delete `off`
  to turn it back on.
