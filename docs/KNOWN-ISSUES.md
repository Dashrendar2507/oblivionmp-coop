# Known issues and limits

Co-op Quest Sync is in public test. These are the things we already know about. If you hit one of them, there's
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

Each player's add-on looks the overlay's text up in the running game. The server reads the text from the game's
files only when the game is installed on the server computer; on a dedicated host the web page shows names worked
out from the game's ids. The remaster's text is Oodle-compressed, so reading the files also needs
`oo2core_9_win64.dll` on that computer (the mod never downloads it);
the log says so, and the FModel route in [INSTALL.md](INSTALL.md#real-names-and-lines-on-the-page-and-the-overlay)
still works. Only English has been tried.

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

## Conversations

* **The overlay runs a few seconds behind.** The game reports a line when it ends, so watchers read it after the
  talker heard it. The voice plays with the text, from nowhere (not from the NPC), and can't yet be cut short when
  you leave the conversation: the current line finishes.
* **You can still turn the camera while frozen.** The freeze stops walking and fighting, not looking around.
* **Menus might not be recognised.** Barter, persuasion, training and so on are detected by the original game's
  menu numbers. If the overlay never says "is bartering" (or another menu), tell us which one.
* **The NPC's name** on the overlay sometimes falls back to the NPC closest to the talker, which can be wrong in a
  crowd.
* **Letting watchers walk around** during someone's conversation ("Freeze watchers: Nobody") hasn't been tested
  much. NPCs near the walker may stand still or jump into place. Reports welcome.
* **The compass may disappear** while a co-op banner or overlay is on screen.
* **Travelling to a conversation (Alt+Y)** into another building or world space relies on the add-on running the
  console's `coc` or `cow` command. You arrive on a spot the talker walked through a moment ago, or a step beside
  them, which in a tight room can be against a wall.

## Party

* **Friendly fire can't be turned off.** OblivionMP doesn't tell mods who hit whom.
* **Fights aren't shared yet.** When one player starts a fight, the others' games don't join it on purpose. NPCs run
  in one player's game (the area's master), and the server can't order an NPC to attack a given player. The server
  log now says who fights where (`Combat:` lines), to find out what OblivionMP already shares.
* **`/coop calm` stops fights, not grudges.** An NPC that is still hostile (an enemy faction, a low disposition) can
  start again. Guards also come back while you still have a bounty: calm that player by name to clear it.
* **Followers and companions** aren't shared or synced (not possible with OblivionMP's SDK 1.0).
* **Respawning next to someone only works in the same area** (both outside, or the same interior). Otherwise you
  come back where you last stood on solid ground.
* **The co-op keys also reach the game.** Alt+U also presses U in the game, and so on. If that's a problem, pick
  other letters on the web page.

## Back where you left

* Going back **inside a building** relies on the `CoopQuestSyncUI` add-on running the console's `coc` command.
  If that fails, you come back outside, by the door.
* Going back **outside** in another world space (a city, the Shivering Isles) relies on the add-on running the
  console's `cow` command; the server then puts you on your spot. Without the add-on, it only works when your local
  save starts you in the same world space as your last outdoor spot.
* OblivionMP has no way for mods to save your character, so your spot is only as fresh as the last time the server
  saw you.
* The game's own loading screen for the building still shows; the black travel screen hides the jumps around it.
* **Typing `/coop test back` with the chat open** can leave the chat stuck after the move's load (it won't close
  or take input until you restart the game). It's the same OblivionMP issue as dying with the chat open (below):
  close the chat before the move.

## OblivionMP issues seen while testing

These come from OblivionMP itself, not the mod; they're listed so you don't chase them:

* **R while down is OblivionMP's "rebirth"**: it heals you and reloads your save, which skips the mod's revive.
* **A reload with the chat (K) open** can leave the chat stuck until you restart the game.
* **"Multiple entities mapped to the same game object"** errors flooding UE4SS.log: the server ended up with two
  copies of one NPC (seen with an NPC who walked through a load door by himself). It repeats every time someone
  enters that area.
* **A crash when quitting to desktop**, while OblivionMP shuts its browser down.

## The UE4SS add-on

* After the mod installs or updates the add-on, you have to **restart the game once** for it to start. A note in the
  top left corner says so until you do.
* If `UE4SS.log` has no `[CoopQuestSyncUI]` lines after that restart, the add-on isn't running: quest pop-ups may
  show while you catch up, and "back inside" won't work. Please report it with the log.
* The add-on runs console commands (coc, cow, calm) only when the game is quiet: not during the join's catch-up and
  not within 2 seconds of a menu opening or closing (a command run then crashed the game). After 30 seconds it runs
  anyway.
* If a "Quest Added" window during the catch-up can't be closed (UE4SS.log says `could not close`), close it by
  hand; the black travel screen steps aside for it after 2.5 seconds.
* **If the game crashes on load**, put an empty file named `off` (`off.txt` works too) in
  `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` and restart the game. That switches the add-on off
  (UE4SS.log says "switched off"); quest sync still works, but pop-ups show during catch-up and travel, back and calm
  don't. If the crashes stop, the add-on is the cause: tell us, with UE4SS.log from a crash with it on. Delete `off`
  to turn it back on.
