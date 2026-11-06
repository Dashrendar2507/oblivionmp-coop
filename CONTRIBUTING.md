# Reporting issues and giving feedback

Thanks for testing Co-op Quest Sync! This is a public test, so every report helps, including "it just worked".

## Before you report

1. Check [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md). If your problem is there, only report it if you have
   something new (a quest where it happens, a log, a workaround).
2. Search the [open issues](https://github.com/Dashrendar2507/OB-MP-coop-mod/issues). If someone already reported
   it, add a comment with your details, or a 👍 reaction if you have nothing to add.
3. Make sure you're on the newest version (see "Mod version" below). The
   [Releases page](https://github.com/Dashrendar2507/OB-MP-coop-mod/releases) lists the latest.

## Opening an issue

Go to [New issue](https://github.com/Dashrendar2507/OB-MP-coop-mod/issues/new/choose) and pick a form:

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

### Server log (server host)

The `[QuestSync]` lines tell us what the mod saw. Either:

* copy the whole server console window, from start-up to the moment the problem happened, into a text file, or
* attach the newest log file from the server folder, if your server writes one, or
* copy the log from the **Terminal** tab of the OblivionMP admin panel.

The console doesn't always show every line, so a log file is better when you can find one.

### UE4SS.log (every player)

This is the game-side log, including our add-on's `[CoopQuestSyncUI]` lines.

* Steam: right-click **Oblivion Remastered**, **Manage**, **Browse local files**, then open
  `OblivionRemastered\Binaries\Win64\UE4SS.log`.
* It's overwritten every time the game starts, so **copy it before you start the game again**.

### Mod version and load order (every player)

* **Mod version**: the `"version"` line in `mods\CoopQuestSync\manifest.json` on the server (open it in Notepad),
  or the name of the zip you downloaded, such as `CoopQuestSync-0.5.0.zip`.
* **Load order**: attach your `Plugins.txt` from `OblivionRemastered\Content\Dev\ObvData\Data\`.
* List any **other mods** you use, in the game (UE4SS mods, `.esp` plugins, graphics or performance mods) and on
  the server.

### The state file (server host, when quests are involved)

`coop-quest-sync-state.json`, from the folder the server runs from, holds the shared progress. Attach it when
quests sync wrongly.

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
