# Using Co-op Quest Sync

## Keys

| Key | What it does | Chat command |
| --- | --- | --- |
| **Alt+U** | While someone talks to an NPC: leave the overlay (and stop being frozen), or listen in again. | `/coop follow` |
| **Alt+Y** | While someone talks to an NPC: travel to them, listening in or not (into their building or city too). | `/coop join` |
| **Alt+H** | Revive the downed teammate standing next to you. | `/coop revive` |
| **Alt+G** | While you're down: give up and respawn next to the party. | `/coop giveup` |

The game sees the letter too. If one of these letters does something in your game, the host can pick other letters
under **Keys** on the web page; every player's game switches within a few seconds. **K** is OblivionMP's chat key.

## How it plays

* **Quests.** When anyone finishes a quest stage, you get it a few seconds later, with a line in chat. If you're in
  a conversation, it waits until you're done talking.
* **Joining.** You catch up on what the party did while you were away, silently: the add-on closes the game's
  "Quest Added" windows that the catch-up brings up. You also get your own progress
  back: OblivionMP doesn't keep the game's quests between sessions, so the mod sets them again every time you join.
* **Conversations.** When a teammate talks to an NPC, you see what's said on an overlay, and you stand still until
  they're done if you're in the same area. The overlay tells you when they open the barter, persuasion, training,
  repair or spell screen, or pick a lock. Press **Alt+U** to leave or come back, and **Alt+Y** to travel to the
  talker, so the party can gather for a quest without missing what's said.
* **Death.** You go down instead of dying ("You are down..."). A teammate next to you presses **Alt+H** to bring you
  back where you fell. After 60 seconds, or when you press **Alt+G**, you respawn next to the party leader (or the
  first other player by name, if the leader is the one down). If nobody is in your area, you come back where you
  last stood on solid ground a few seconds before you died, so a fall doesn't kill you twice. Don't press **R** while
  down: that's OblivionMP's own "rebirth", which reloads your save.
* **Back where you left.** When you join, the server puts you back where you were when you left, inside buildings
  too. The screen goes black with "Taking you back to where you left..." until you stand in your spot, so you don't
  see the jumps. Travelling to a teammate (Alt+Y) through a load does the same.

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
  * **Give to everyone**: push a stage to every player,
  * **Reset this quest**: take it back from everyone.
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
| Keys | U, Y, H, G | The letters for Alt+U, Alt+Y, Alt+H, Alt+G. |
| Tags over heads | On | Talking, bartering, picking a lock, down. |
| Level of spawns and loot | Server default (halfway) | Lowest player level, halfway, or highest player level. |
| Difficulty, damage dealt / taken | Game default | Novice to Master. |
| Friendly fire | Always on | OblivionMP can't tell mods who hit whom. |

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
| `/coop follow`, `/coop join`, `/coop revive`, `/coop giveup` | Same as the keys. |
| `/coop calm` | Stop the fights in your area: each NPC fighting there stops, and guards stop chasing the players there. For a fight that went wrong, such as a quest giver turned hostile. |
| `/coop calm <name>` | The same in that player's area, and their bounty goes to 0. Case and a final dot don't matter, and the start of a name is enough when only one player matches. |
| `/coop calm all` | Every area with a player in it. |
| `/coop calm near` | Every NPC fighting within 15 m of you, and the closest NPC even if it isn't fighting. |

`/coop calm` needs the CoopQuestSyncUI add-on in the players' games: it runs the console's `StopCombat`,
`scaonactor` and `SetCrimeGold 0`. The web page has the same, as **Calm** on each player and **Calm all fights**.

`/qs push`, `/qs auto`, `/qs source`, `/qs reset`, `/coop calm`, `/qsprobe` and `/coop test` are for admins: the
admins of the ReadyM admin panel (port 9050, **Admins**) who have the **players.manage** permission (superusers
always do), plus anyone added with **Make admin** on the co-op page.
**Co-op settings > Commands** on the page can open them to everyone. Everyone else gets "Only server admins can
use ...". Players are recognised by their ReadyM account, so a nickname change doesn't matter.

Quests are named by their editor id (`MS93`, `MQ01`...). The web page shows each quest's id next to its name.

### Testing alone

The server can play a pretend teammate called "Ghost", so you can try the co-op features on your own. See
[mod/TESTING.md](../mod/TESTING.md), "Test 1b", for a walkthrough.

| Command | What it does |
| --- | --- |
| `/coop test talk [ask]` | Ghost talks to an NPC for about 40 s: you get the overlay, the freeze, and a barter and persuasion pause. |
| `/coop test stop` | End Ghost's conversation. |
| `/coop test stage <quest> <stage>` | Ghost reaches a stage: you get it as if a teammate did it live. |
| `/coop test effects <quest>` | List a quest's stages that have effects, and which. |
| `/coop test revive` | Ghost revives you while you're down. |
| `/coop test pin`, `/coop test back` | Remember where you stand, then go back there the way a join would. |
| `/coop test status` | What the server knows about you. |
| `/coop test asghost on\|off` | The server treats you like Ghost, a player who isn't an admin, so you can see the admin commands refused. Open to everyone. |

## config.json (server host, optional)

Create `config.json` in `mods\CoopQuestSync\server\` to change these. Every entry is optional.

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
| `StringsFile` | `""` | The game's text. Empty: `coop-quest-sync-strings.json` in the server's folder (next to the state file), made from the game on first start or copied once from a `strings.json` next to the server DLL. |
| `GameFolder` | `""` | Where Oblivion Remastered is installed, to read its text from. Empty: found through Steam. |
| `LearnTextFromPlayers` | `true` | Learn the game's text from the players' games (one `coop-quest-sync-strings.<language>.json` per language, next to the state file), for the web page and each player's chat lines in their language. |
| `Language` | `"en"` | Language of the web page and of a hand-made `coop-quest-sync-strings.json` (`de`, `fr`, `es`, `it`...). Players' chat lines use each player's own game language. |
| `PanelDatabase` | `"data/web.db"` | The ReadyM admin panel's database, where the mod finds the server's admins. Empty: only admins added on the co-op page. |
| `PanelPermission` | `"players.manage"` | The panel permission (set per admin under **Admins**) a panel admin needs for the co-op admin commands. Superusers always can. Empty: every active panel admin. |
