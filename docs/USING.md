# Using Co-op Quest Sync

## Keys

| Key | What it does | Chat command |
| --- | --- | --- |
| **Alt+U** | While someone talks to an NPC: leave the overlay (and stop being frozen), or listen in again. | `/coop follow` |
| **Alt+Y** | While someone talks to an NPC: travel to them, listening in or not (into their building or city too). | `/coop join` |
| **Alt+H** | Revive the downed teammate standing next to you. | `/coop revive` |
| **Alt+G** | While you're down: give up and respawn next to the party. | `/coop giveup` |
| **Alt+O** | Any time: travel to a teammate (the last one you picked with `/coop goto`, else the leader, else the only other player). | `/coop goto [name]` |

The game sees the letter too. If one of these letters does something in your game, the host can pick other letters
under **Keys** on the web page; every player's game switches within a few seconds. **K** is OblivionMP's chat key.

**R** is OblivionMP's own "rebirth" key, not the mod's. It works even while you type in the chat, so avoid words
with an R in them while you're down (see [Death](#how-it-plays) below).

## How it plays

* **Quests.** When anyone finishes a quest stage, you get it a few seconds later, with a line in chat. If you're in
  a conversation, it waits until you're done talking.
* **Joining.** You catch up on what the party did while you were away, silently: the add-on closes the game's
  "Quest Added" windows that the catch-up brings up. You also get your own progress
  back: OblivionMP doesn't keep the game's quests between sessions, so the mod sets them again every time you join.
* **Conversations.** When a teammate talks to an NPC, you see what's said on an overlay and hear the NPC's voice,
  and you stand still until they're done if you're in the same area. The overlay tells you when they open the
  barter, persuasion, training, repair or spell screen. Press **Alt+U** to leave or come back, and **Alt+Y** to
  travel to the talker, so the party can gather for a quest without missing what's said.
* **Tags over heads.** Above each player, a tag says when they're talking, bartering, persuading, training,
  picking a lock, pickpocketing or down. Lockpicking and pickpocketing come from the add-on, which sees which of the
  remaster's screens is open.
* **Death.** You go down instead of dying: OblivionMP's "You died" message shows, with a note under it saying what
  to do. A teammate next to you presses **Alt+H** to bring you back next to them. After 60 seconds, or when you press
  **Alt+G**, you respawn next to the party leader (or the first other player by name, if the leader is the one
  down), wherever they are. If nobody else is playing, you come back where you last stood on solid ground a few
  seconds before you died, so a fall doesn't kill you twice. Getting back up uses OblivionMP's "rebirth", which
  reloads your character: the screen stays black through that load and the trip back. On OblivionMP 1.1 that reload
  sometimes doesn't run and you stay on the ground; the game then says "Still on the ground? Quit and rejoin".
* **OblivionMP's own rebirth.** OblivionMP brings a dead player back by itself after a while (between about 10 and
  25 seconds in our tests), and **R** does the same at once, even while you type in the chat. The mod can't stop
  either, so it treats them as a respawn: once that reload is over, it takes you to the party (or your last safe
  spot), the same as Alt+G. A revive is only possible before that happens.
* **New players** start in the prison cell, like a new game. OblivionMP 1.1 starts every new character from one
  save on the server; the mod puts a save made in the prison cell there each time the server starts (setting "Start
  in the prison"). The new player makes their character the game's own way, and on arrival the game's own window
  asks: **play the prison intro** (the race menu opens right away; the intro shows the birthsign and class menus
  later), or **skip to the sewer exit** (with the Amulet of Kings, a starter kit and the race, class and birthsign
  menus, which open once you've stood at the sewer exit for a few seconds). The window
  stays up until you click; close the Esc menu before answering it. If a load closes the window before you answer,
  it comes back once you walk a few steps. The Emperor and the prison are shared by everyone, so the intro plays once per
  world: once someone has played it and left the sewers, later newcomers start at the sewer exit. Leave the game
  during the intro and you come back at the sewer exit, for the same reason. The party's
  progress reaches a new player once they've left the sewers, and nobody can travel to a teammate before that.

  With "Start in the prison" off, every new character is OblivionMP's own ready-made one: "Danmer", just out of the
  sewers near the Aleswell inn, with gear, gold and a few quest stages. About 8 seconds after such a character is in
  the world, the mod empties their bag except for a starter kit, takes them to the sewer exit, and opens the race
  menu, then class, then birthsign; a chat line says "Welcome! Make your character...". Without the CoopQuestSyncUI
  add-on the trip can't happen, and the menus open where they are after 60 seconds. That character's quest stages
  (Deliver the Amulet, the Knights of the Nine prophet, MS47...) are never shared with the party or announced.

  Either way, a new character isn't taken back to where their nickname last played. "New" means the server has no
  save for that player (`<server folder>\saves\players\<ReadyM id>.sav`); a character above level 1 is never
  touched. To start someone over, stop the server and move their file aside.

## The web admin page

The server host opens **http://localhost:9060/** on the server computer (see
[INSTALL.md](INSTALL.md#letting-other-people-open-the-web-page) to open it to the network).

* **Players**: who's online and what they're doing (talking, bartering...).
* **Auto sync**: turn automatic sharing on or off.
* **Source player**: under **Co-op settings**, set "Progress counts from" to **Leader only** and pick the player as
  **Party leader**. Only their progress is shared from then on, for everyone online and everyone who joins later.
  "Anyone" is the default.
* **Quests**: every quest with its stages and journal text, and which player has which stage. Search by name or
  journal text, and filter by "started by anyone", "in shared progress" or "all quests". Open a quest to:
  * **Give to everyone**: push a stage to every player. It reaches everyone in game within a few seconds; a
    player still in the prison intro gets it when they leave the sewers.
  * **Reset this quest**: take it back from everyone. It leaves the shared progress at once, but the game can't
    undo a quest stage while it runs: a player who already has the quest keeps it in their journal until they
    rejoin (they get a chat line saying so). Stages given again, or reached from then on, count as usual.
* **Reset all shared progress**: start the playthrough over.
* **Co-op settings**: every switch below. Changes apply at once and are kept across server restarts.

### Co-op settings

**Quest progress**

| Setting | Default | Meaning |
| --- | --- | --- |
| Progress counts from | Anyone | Anyone: a stage anyone reaches counts for all. Leader only: only the leader's stages are shared. |
| Party leader | (none) | Used by "leader only" and by respawns. |
| Wait while someone talks | On | New stages reach a player once their conversation ends. |
| Announce progress in chat | On | "Alice advanced The Quest: ..." |
| NPCs that quests bring in | On | The server keeps the hidden NPCs and creatures a quest script shows (an ambush, the prison rats), so they appear for everyone. |

**Effects of a stage someone else reached.** Each quest stage that does something to the player is tagged with
what it does: **Teleport**, **Combat**, **Items**, **Status**, **Faction ranks**, **Cut scene**. The player who
reaches the stage always gets everything. These switches decide what everyone else gets:

| Moment | Default on | Meaning |
| --- | --- | --- |
| Live | All | Someone advanced the quest while you were playing. "Items" here is the reward rule. |
| On join | Items, Status, Ranks | Stages the party did while you were away. |
| On rejoin | Ranks | Your own stages, set again each time you join. Items are put back anyway. |

**Conversations**

| Setting | Default | Meaning |
| --- | --- | --- |
| Show others' conversations to | Everyone | Everyone, same area only, or off. |
| Watchers follow a conversation | Automatically | Or "when they press the follow key": a banner says who is talking and you choose. |
| Watchers hear the voices | On | The NPC's voice with each line, from nowhere, while listening in. Needs the CoopQuestSyncUI add-on. |
| Travel to a conversation | On | The join key (Alt+Y) takes a player to whoever is talking. Into another building or city, it needs the CoopQuestSyncUI add-on. |
| Freeze watchers | Same area | Same area, everyone, or nobody. Players in combat are never frozen. |
| Share rewards handed out in conversation | On | Gold or items a line gives. Not trades. |
| Share quest items | Off | Keys, letters... picked up by one player go to everyone. |

**Party**

| Setting | Default | Meaning |
| --- | --- | --- |
| When a player dies | Down, wait for a revive | Or respawn next to the party at once, or the game's own death. |
| Seconds before respawning | 60 | How long a downed player waits for a revive. |
| Revive distance | 400 cm | About four metres. |
| Put players back where they left | On | On join. |
| Black screen while travelling | On | Hides the jumps while a player is put back where they left, or travels to a teammate. |
| Travel to teammates any time | On | `/coop goto <name>` and Alt+O. Into another building or city, it needs the CoopQuestSyncUI add-on. |
| Seconds between trips | 60 | Per player, for `/coop goto` and Alt+O. 0 means no wait. |
| No trips during a fight | On | A player in combat can't use `/coop goto`. |
| Keys | U, Y, H, G, O | The letters for Alt+U, Alt+Y, Alt+H, Alt+G, Alt+O. |
| Tags over heads | On | Talking, bartering, persuading, picking a lock, pickpocketing, down. |
| Level of spawns and loot | Server default (halfway) | Lowest player level, halfway, or highest player level. |
| Difficulty, damage dealt / taken | Game default | Novice to Master. |
| Friendly fire | Off | Off: a player's hit on another player does no damage. Healing still works, and NPCs hurt everyone. |

**New players**

| Setting | Default | Meaning |
| --- | --- | --- |
| Start in the prison | On | Each time the server starts, put the mod's prison-cell save in the server's `modloader` folder, so new characters start in the prison. OblivionMP's own save is kept as `<server folder>\coop-quest-sync-original-world.sav`. Off puts it back. Takes effect at the next server start; players' games get the new save on their next join. |
| Set up new characters | On | For OblivionMP's own ready-made character ("Start in the prison" off): empty the bag down to the starter kit, open the race, class and birthsign menus, and keep that character's quest stages out of the party's progress. |
| New characters start at the sewer exit | On | With "Set up new characters": take the character to the sewer exit (just inside the sewer gate) before the character menus open. Needs the CoopQuestSyncUI add-on. |
| Starter kit | On | Iron shortsword and bow, 25 arrows, 5 lockpicks, 3 healing potions, a torch, leather cuirass and boots. Off: the bag stays empty. config.json `StarterKitItems` changes it. |
| Prison or sewer start | Ask | With the prison start. Ask: the game's window offers the prison intro or the sewer exit. Sewer exit: always skip the intro. Game's own: no window, the intro plays as in a new game. With OblivionMP's own start save nothing is asked. A server whose settings were saved before 0.8.0 keeps "Game's own": switch it to Ask. |
| Offer the prison intro | Fresh world | With the prison start. Fresh world: until someone has played the intro and left the sewers (a new server world resets it). Always: for testing. Never. |
| Prison intro here | | Whether someone has played the intro and left the sewers on this world. **Played: reset** offers the prison again (only on a fresh world). |
| Take back spare amulets | On | With the prison start, every new player gets an Amulet of Kings. Once Jauffre has it ("Deliver the Amulet" stage 50), the other copies go. |

## Chat commands

| Command | What it does |
| --- | --- |
| `/qs status` | Auto sync on or off, the players, and whether the plugin was found in each game. |
| `/qs mine [quest]` | The stages the server sees in your game. |
| `/qs shared [quest]` | The shared progress. |
| `/qs push <quest> <stage>` | Give yourself one stage, for example `/qs push MS93 10`. |
| `/qs auto on\|off` | Turn automatic sharing on or off. |
| `/qs source <name>\|all` | Share only one player's progress, or everyone's. |
| `/qs reset` | Reset every shared quest. |
| `/qs whoami` | Your ReadyM id, and whether the server counts you as an admin. |
| `/qs newstart` | Where new players start, and where each player is (in the intro, at the sewer exit, out). |
| `/qs newstart again <name>` | With the prison start: show that player the start window again (only while they haven't left the sewers). |
| `/qs newstart fresh` | With the prison start: offer the prison intro again. Only on a fresh world: the Emperor alive, the prison doors closed. |
| `/coop follow`, `/coop join`, `/coop revive`, `/coop giveup` | Same as the keys. |
| `/coop calm` | Stop the fights in your area: each NPC fighting there stops, and guards stop chasing the players there. For a fight that went wrong, such as a quest giver turned hostile. |
| `/coop calm <name>` | The same in that player's area, and their bounty goes to 0. Case and a final dot don't matter, and the start of a name is enough when only one player matches. |
| `/coop calm all` | Every area with a player in it. |
| `/coop calm near` | Every NPC fighting within 15 m of you, and the closest NPC even if it isn't fighting. |

`/coop calm` needs the CoopQuestSyncUI add-on in the players' games: it runs the console's `StopCombat`,
`scaonactor` and `SetCrimeGold 0`. The web page has the same, as **Calm** on each player and **Calm all fights**.

`/qs push`, `/qs auto`, `/qs source`, `/qs reset`, `/qs newstart again|fresh`, `/coop calm`, `/qsprobe` and `/coop test` are for admins: the
admins of the ReadyM admin panel (port 9050, **Admins**) who have the **players.manage** permission (superusers
always do), plus anyone added with **Make admin** on the co-op page.
**Co-op settings > Commands** on the page can open them to everyone. Everyone else gets "Only server admins can
use ...". Players are recognised by their ReadyM account, so a nickname change doesn't matter.

Quests are named by their editor id (`MS93`, `MQ01`...). The web page shows each quest's id next to its name.

### Testing alone

The server can play a pretend teammate called "Ghost", so you can try the co-op features on your own. These are
admin commands. [mod/TESTING.md](../mod/TESTING.md) uses them in its walkthroughs.

| Command | What it does |
| --- | --- |
| `/coop test talk [ask]` | Ghost talks to an NPC for about 40 s: you get the overlay, the freeze, and a barter and persuasion pause. |
| `/coop test stop` | End Ghost's conversation. |
| `/coop test stage <quest> <stage>` | Ghost reaches a stage: you get it as if a teammate did it live. |
| `/coop test effects <quest>` | List a quest's stages that have effects, and which. |
| `/coop test heal` | Ghost revives you while you're down. (`/coop test revive` works too, but its R triggers OblivionMP's rebirth while you type.) |
| `/coop test voice` | Play one NPC line's voice, to check you hear the voices. |
| `/coop test pin`, `/coop test back` | Remember where you stand, then go back there the way a join would. |
| `/coop test goto` | Travel to where Ghost stands, the way `/coop goto` would. |
| `/coop test askme` | Ghost asks to come to you, as with `/coop goto lock`. |
| `/coop test status` | What the server knows about you. |
| `/coop test asghost on\|off` | The server treats you like Ghost, a player who isn't an admin, so you can see the admin commands refused. Open to everyone. |

## config.json (server host, optional)

On the **server**, create `<server folder>\mods\CoopQuestSync\server\config.json` (`<server folder>` is the one
with `server.exe`) to change these, then restart the server. Every entry is optional, and an update of the mod
replaces that folder, so keep a copy.

| Setting | Default | Meaning |
| --- | --- | --- |
| `WebPort` | `9060` | Port of the web page. `0` turns it off. |
| `WebBind` | `"127.0.0.1"` | Only this computer. `"0.0.0.0"` opens it to the network and needs a password. |
| `WebPassword` | `""` | Password the page asks for. |
| `AutoSync` | `true` | Share stages automatically. |
| `UndoReplayRewards` | `true` | When your own stages are set again on join, put your inventory back as it was (no double rewards). |
| `IntervalSeconds` | `1` | How often the server checks the players' games. |
| `StateFile` | `"coop-quest-sync-state.json"` | Where the shared progress and settings are kept. |
| `GamePlugins` | the game's plugins | The order of the game's own plugins in `Plugins.txt`. Only matters for DLC items and NPC names. |
| `StringsFile` | `""` | A hand-made file with the game's text. Empty: `coop-quest-sync-strings.json` in the server's folder (next to the state file), copied once from a `strings.json` next to the server DLL, or read from the game installed on the server computer when that works. |
| `ArriveInsideDirectly` | `true` | Travel into a building or dungeon straight onto the spot (the console's `positioncell`). `false` goes to its door first (`coc`), then the spot. |
| `StarterKitItems` | the kit | The starter kit for new characters: form id (hexadecimal, as the game's console shows it) and count, separated by commas, for example `"00000C0D:1,00017829:25"`. |
| `GameFolder` | `""` | Where Oblivion Remastered is installed, to read its text from. Empty: found through Steam. |
| `LearnTextFromPlayers` | `true` | Learn the game's text from the players' games (one `coop-quest-sync-strings.<language>.json` per language, next to the state file), for the web page and each player's chat lines in their language. |
| `Language` | `"en"` | Language of the web page and of a hand-made `coop-quest-sync-strings.json` (`de`, `fr`, `es`, `it`...). Players' chat lines use each player's own game language. |
| `PanelDatabase` | `"data/web.db"` | The ReadyM admin panel's database, where the mod finds the server's admins. Empty: only admins added on the co-op page. |
| `PanelPermission` | `"players.manage"` | The panel permission (set per admin under **Admins**) a panel admin needs for the co-op admin commands. Superusers always can. Empty: every active panel admin. |
