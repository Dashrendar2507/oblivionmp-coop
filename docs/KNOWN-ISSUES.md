# Known issues and limits

Co-op Quest Sync is in public test. These are the things we already know about. If you hit one of them, there's
no need to report it again, unless you have something new to add (a quest where it happens, a log, a way around
it). OblivionMP itself also has [known issues](https://docs.ready.mp) that aren't caused by this mod.

## Before you install

### Everyone needs the Deluxe plugins

Our plugins are built on top of every official plugin of the game, including `AltarDeluxe.esp`. A player whose
game is missing one of them can't load our plugins, and nothing gets synced for them. If that's you, please
[open an issue](https://github.com/Dashrendar2507/OB-MP-coop-mod/issues/new/choose) with your `Plugins.txt`: a
version without the Deluxe dependency can be built.

### Chat commands aren't limited to admins

Any player can type `/qs reset`, `/qs push` or `/qs source` in chat. Only play with people you trust for now, and
keep a copy of `coop-quest-sync-state.json` if your playthrough matters to you. The web page is only open on the
server computer by default.

### Text needs a strings.json

Without the game's text file (see [INSTALL.md](INSTALL.md#optional-real-names-and-lines-on-the-page-and-the-overlay)),
the conversation overlay and the web page show keys such as `LOC_FN_YsabelAndronicus` instead of names and lines.
Only English has been tried.

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
  talker heard it. There is no voice.
* **You can still turn the camera while frozen.** The freeze stops walking and fighting, not looking around.
* **Menus might not be recognised.** Barter, persuasion, training and so on are detected by the original game's
  menu numbers. If the overlay never says "is bartering" (or another menu), tell us which one.
* **The NPC's name** on the overlay sometimes falls back to the NPC closest to the talker, which can be wrong in a
  crowd.
* **Letting watchers walk around** during someone's conversation ("Freeze watchers: Nobody") hasn't been tested
  much. NPCs near the walker may stand still or jump into place. Reports welcome.
* **The compass may disappear** while a co-op banner or overlay is on screen.

## Party

* **Friendly fire can't be turned off.** OblivionMP doesn't tell mods who hit whom.
* **Followers and companions** aren't shared or synced (not possible with OblivionMP's SDK 1.0).
* **Respawning next to someone only works in the same area** (both outside, or the same interior). Otherwise you
  come back where you last stood on solid ground.
* **The co-op keys also reach the game.** Alt+L also presses L in the game, and so on. If that's a problem, pick
  other letters on the web page.

## Back where you left

* Going back **inside a building** relies on the `CoopQuestSyncUI` add-on running the console's `coc` command.
  If that fails, you come back outside, by the door.
* Going back **outside** only works when your local save starts you in the same world (Tamriel or the Shivering
  Isles) as your last outdoor spot.
* OblivionMP has no way for mods to save your character, so your spot is only as fresh as the last time the server
  saw you.

## The UE4SS add-on

* After the mod installs or updates the add-on, you have to **restart the game once** for it to start.
* If `UE4SS.log` has no `[CoopQuestSyncUI]` lines after that restart, the add-on isn't running: quest pop-ups may
  show while you catch up, and "back inside" won't work. Please report it with the log.
