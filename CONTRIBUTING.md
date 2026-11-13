# Reporting issues and giving feedback

Thanks for playing Co-op Quest Sync! The mod is young, so every report helps, including "it just worked".

## Please test these

Some things we couldn't test ourselves yet, mostly because they need two or more players. If you try one, a
**Test report** saying what happened helps a lot, even when it simply worked.

* **Things happening twice on OblivionMP 1.1.** OblivionMP 1.1 replays conversation results, levers and message
  boxes on nearby players' games, on top of the mod's quest sync. Talk to a quest giver with a teammate nearby: does
  a journal entry, a quest pop-up, gold or an item arrive twice, or for the player who wasn't talking?
* **Joining on OblivionMP 1.1**, which starts the game straight in the world: is the catch-up still silent, and does
  a brand-new character get the start window?

* **Sleeping and waiting.** OblivionMP gives each area's clock (the time of day) to one player's game, the area's
  host. With two or more players in the same area, have the player who **isn't** the host wait a few hours, then
  sleep in a bed. Does the clock stay moved forward, or snap back after a moment? Did the other players' sky and
  time change too? If you had enough skill increases, did the **level-up** screen appear when you woke up? We don't
  know which player is the host, so try it once from each side and tell us who did what.
* **Fast travel.** Same question: after one player fast travels, does the time of day stay where the trip left it?
* **Travel to a teammate** (`/coop goto <name>`, **Alt+O**), and to a conversation (**Alt+Y**): into a building,
  into a city, into the Shivering Isles. Did you arrive next to them, or somewhere odd (in a wall, at the door)?
* **Followers.** Recruit a follower (a quest companion or a hired one). Does it follow the player who recruited it,
  or another player?
* **Fights.** When one player starts a fight, do the NPCs attack the other players too? Does a teammate who walks in
  see the fight the same way?

## Before you report

1. Check [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md). If your problem is there, only report it if you have
   something new (a quest where it happens, a log, a workaround).
2. Search the [open issues](https://github.com/Dashrendar2507/oblivionmp-coop/issues). If someone already reported
   it, add a comment with your details, or a 👍 reaction if you have nothing to add.
3. Make sure you're on the newest version (see "Mod version" below). The
   [Releases page](https://github.com/Dashrendar2507/oblivionmp-coop/releases) lists the latest.

## Opening an issue

Go to [New issue](https://github.com/Dashrendar2507/oblivionmp-coop/issues/new/choose) and pick a form:

* **Bug report**: something broke, or didn't do what the docs say.
* **Test report**: you played a session and want to say how it went, good or bad.
* **Feature request**: an idea or a missing co-op feature.

One problem per issue, please. Two unrelated bugs in one issue tend to get only one fix.

## What makes a good bug report

The fastest bugs to fix come with:

* **What you did**, step by step, from joining the server.
* **What you expected, and what happened instead.**
* **The quest** involved, if any. The web page and `/qs mine` show quest ids such as `MS93`.
* **The logs** (see below), from the session where it happened.
* **A screenshot or short video**, for anything you can see on screen.
* **How many players** were online, and whether you were the host.

## Where to find the logs

Attach logs as files (drag them into the issue box). Please don't paste long logs straight into the text. Logs
can contain player names and your computer's folder paths; look them over and blank out anything you'd rather
not share.

### Server log (on the server)

The `[QuestSync]` lines tell us what the mod saw. Either:

* copy the whole server console window, from start-up to the moment the problem happened, into a text file, or
* attach the newest log file from the server folder (the one with `server.exe`), if your server writes one, or
* copy the log from the **Terminal** tab of the OblivionMP admin panel (http://localhost:9050 on the server).

The console doesn't always show every line, so a log file is better when you can find one.

### UE4SS.log (on each player's PC)

This is the game-side log, including our add-on's `[CoopQuestSyncUI]` lines.

* Steam: right-click **Oblivion Remastered**, **Manage**, **Browse local files**, then open
  `OblivionRemastered\Binaries\Win64\UE4SS.log`. With the default Steam folder that's
  `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\OblivionRemastered\Binaries\Win64\UE4SS.log`.
* It's overwritten every time the game starts, so **copy it before you start the game again**.

### Versions and load order

* **Mod version** (on the server): the `"version"` line in `<server folder>\mods\CoopQuestSync\manifest.json`
  (open it in Notepad), or the name of the zip you downloaded, such as `CoopQuestSync-0.7.0.zip`.
* **OblivionMP version** (on the server): the name of the server package, such as `oblivion-server-win64-1.1.0`, or
  the SDK version in the server log's list of mods.
* **Load order** (on each player's PC): attach your `Plugins.txt` from
  `<game folder>\OblivionRemastered\Content\Dev\ObvData\Data\`.
* List any **other mods** you use, in the game (UE4SS mods, `.esp` plugins, graphics or performance mods) and on
  the server.

### The state file (on the server, when quests are involved)

`<server folder>\coop-quest-sync-state.json` (next to `server.exe`) holds the shared progress. Attach it when
quests sync wrongly. Never attach `coop-quest-sync-strings*.json` or a `strings.json`: those are the game's own text.

## Security issues

If you find a way for players to harm someone else's server or computer through this mod, please don't post the
details in a public issue. Open an issue that only says you found a security problem, and we'll arrange a
private way to share it.

## Contributing code

Pull requests are welcome. The [README](README.md#for-developers) explains how the mod works, how to generate the
plugins and how to build. Please open an issue first for anything bigger than a small fix, so we can agree on the
approach. Never commit or ship any of the game's own files (`Oblivion.esm`, the DLC plugins, `Game.locres`,
`strings.json`...). The **No game files** check fails a pull request that adds one. Before uploading a release
zip, run `python3 -I tools/check_no_game_files.py CoopQuestSync-x.y.z.zip`.
