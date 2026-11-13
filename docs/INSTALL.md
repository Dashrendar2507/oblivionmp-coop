# Installing Co-op Quest Sync

This guide assumes you've never modded Oblivion before. There are two parts:

* **[The server host](#part-1-the-server-host)** (the person who runs the OblivionMP server) installs the mod once.
* **[Every player](#part-2-every-player)** (the host too, if they play) does one small edit to the game, then joins.

If anything goes differently from what's written here, that's worth a [bug report](../CONTRIBUTING.md).

## What you need

* **Oblivion Remastered**, with all its official plugins. Most copies have them; see
  [the Deluxe note](KNOWN-ISSUES.md#everyone-needs-the-deluxe-plugins) if you're not sure.
* The **ReadyM Launcher**, which you use to join OblivionMP servers.
* For the host: an **OblivionMP 1.0.1 server** (see "Server > Installation" in the [OblivionMP docs](https://docs.ready.mp))
  with the mods it ships with. Co-op Quest Sync needs the **Chat** mod (`OblivionMP.Chat`), which comes with the
  server.

## Part 1: the server host

### Install

1. Download the newest `CoopQuestSync-x.y.z.zip` from the
   [Releases page](https://github.com/Dashrendar2507/oblivionmp-coop/releases).
2. Stop the server if it's running.
3. Open the zip. Copy the `CoopQuestSync` folder into the server's `mods` folder. It should end up like this:

   ```text
   server/
   ├── server.exe
   └── mods/
       ├── OblivionMp.Sdk/
       ├── OblivionMP.Chat/
       ├── ...
       └── CoopQuestSync/
           ├── manifest.json
           ├── client/
           └── server/
   ```

   Make sure it's `mods\CoopQuestSync\manifest.json`, not `mods\CoopQuestSync\CoopQuestSync\manifest.json`. Unzipping
   tools sometimes add an extra folder.
4. Start the server. In its console you should see lines starting with `[QuestSync]`, including:
   * `Loaded 287 quests / ... stages from the token map`
   * `Quest admin page: http://localhost:9060/`
5. On the server computer, open http://localhost:9060/ in a browser. You should see the **Co-op Quest Sync** page.

That's it for the host. The server hands the mod to every player when they join.

### Real names and lines on the page and the overlay

The remaster keeps its text (NPC names, dialogue, journal entries) outside the plugins. That text belongs to
Bethesda, so the mod never ships it. Instead it reads the text from each player's own copy of the game:

* **Players** (the conversation overlay): the CoopQuestSyncUI add-on looks each line's text up in the running game,
  in the language the game is set to. Nothing to do. With several languages in one party, everyone reads in their
  own.
* **The server** (the web page and chat lines) learns the text from the players' games. Each player's game says
  which language it runs in; for each language the server doesn't have yet, it asks one player of that language for
  the text, a batch at a time, in the background, once they're in the world (a few minutes for the whole game). It
  keeps it in `coop-quest-sync-strings.<language>.json` (for example `coop-quest-sync-strings.fr.json`), in the
  folder the server runs from (next to `server.exe` and `coop-quest-sync-state.json`), where updates of the mod don't
  touch it, so each language is learned once. Chat lines then reach each player in their own language. The web page
  uses the language set in `config.json` (`"Language"`, English by default), or whichever one it learned. Please
  don't share those files. A dedicated host without the game works the same way.

Before that, the server also tries two other ways: a `coop-quest-sync-strings.json` the host made by hand (below),
and reading the game installed on the server computer. The second needs Oodle's `oo2core_9_win64.dll`, which the
Steam version of the remaster doesn't include, and the mod never downloads it, so on most computers it fails and the
server learns from the players instead. Until the first player has been in for a few minutes, the page shows names
worked out from the game's ids ("Ysabel Andronicus" from `LOC_FN_YsabelAndronicus`); everything else works.

The server's log says what happened (lines starting with `[QuestSync]`):

* `The game's text: C:\...\coop-quest-sync-strings.json`: the host's own file. Done.
* `The game's text in fr (from the players' games): ...coop-quest-sync-strings.fr.json, 24730 of 24730.`: learned
  before. Done.
* `Couldn't read the game's text from OblivionRemastered-Windows.pak: the game's text is Oodle-compressed and there's
  no oo2core_9_win64.dll on this computer`, then `The server will learn the game's text from the players' games
  instead`: the usual case. Nothing to do.
* When a player joins: `Alice's game runs in fr.`, `Learning the game's text in fr from Alice's game ...`, progress
  lines, then `The game's text in fr is complete ...`.
* `Alice's game didn't send the text asked for in 45 s (is the CoopQuestSyncUI add-on running?)`: that player's
  add-on isn't running. The server asks another player of that language, or that player again next time they join.

To stop the server learning (if you only want a file you made yourself), set `"LearnTextFromPlayers": false` in
`config.json`. For the web page in another language, set `"Language"` (`de`, `fr`, `es`, `it`...).

**By hand**, if you'd rather make the file yourself (optional):

1. Install [FModel](https://fmodel.app) and point it at the game (`OblivionRemastered\Content\Paks`).
2. Find `Localization/Game/en/Game.locres` (or your language instead of `en`), right-click it and choose
   **Export Raw Data**.
3. With Python 3 installed, from a copy of this repository:
   ```sh
   python3 -I plugin/tools/locres.py path/to/Game.locres strings.json --keys mod/Content
   ```
4. On the server computer, put `strings.json` in the server's `mods\CoopQuestSync\server\` folder (next to
   `CoopQuestSync.Server.dll`) and restart the server. The server copies it to `coop-quest-sync-strings.json` in
   its own folder (next to `server.exe`), where it stays across updates, and logs `Copied the game's text to ...`.
   The log line `Game data: ... text entries` should show a number other than 0. You can then delete the
   `strings.json` in `mods\CoopQuestSync\server\`.

Without any of this everything still works; only the words are missing.

### Letting other people open the web page

By default only the server computer can open the page. To open it to your network, create a file called
`config.json` in `mods\CoopQuestSync\server\` with:

```json
{ "WebBind": "0.0.0.0", "WebPassword": "pick-a-password" }
```

The page refuses to start on the network without a password. Don't open port 9060 to the internet.

### Updating to a new version

1. Stop the server.
2. Delete the old `mods\CoopQuestSync` folder and copy in the new one. The game's text
   (`coop-quest-sync-strings.json`) is outside it and stays. (Older versions kept it as `server\strings.json` inside the
   mod's folder: put that one back in the new `server\` folder once, and the server moves a copy out.)
3. Keep `coop-quest-sync-state.json`: it holds the shared progress and every setting from the web page. It's in
   the folder the server runs from, usually next to `server.exe`. The server prints its full path in the `[QuestSync]`
   line it logs at start-up.
4. Start the server. Players get the update automatically the next time they join.

### Starting over

To wipe the shared progress, click **Reset all shared progress** on the web page. It clears the
server's progress and takes the hidden quest tokens back from everyone, so the quests don't come back on the next
join. To reset a single quest, open it on the page and use its reset button.

### Uninstalling

Delete `mods\CoopQuestSync` and restart the server. Players should also remove the two lines they added to
`Plugins.txt` (see below).

## Part 2: every player

### One-time setup: Plugins.txt

The game only loads plugins listed in a text file called `Plugins.txt`. The launcher copies our two plugins into
the game for you when you join, but you need to add them to the list once.

1. Find the game folder. On Steam: right-click **Oblivion Remastered** in your library, **Manage**, **Browse local
   files**.
2. Open `OblivionRemastered\Content\Dev\ObvData\Data\`.
3. Open `Plugins.txt` in Notepad.
4. Add these two lines at the **very end** of the file, then save:

   ```text
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```

   Don't remove anything that's already there. The game's own plugins (`Oblivion.esm`, `Knights.esp`, the `DLC*.esp`
   files, `AltarESPMain.esp`, `AltarDeluxe.esp`...) must stay above our two.

You won't find `CoopQuestSync.esp` in that folder before you join a server: that's normal. The launcher puts the
plugins there when you join and removes them when you leave, so they never affect your single-player game.

### First join

1. Join the server through the ReadyM Launcher, as usual. The launcher downloads the mod.
2. On your first join, the mod installs a small add-on (`CoopQuestSyncUI`, used to hide pop-ups while you catch up
   and to put you back inside buildings). A note in the top left corner tells you to restart the game; it stays
   until you do.
3. Quit the game and join again from the launcher.

From then on, joining is all there is to it. Within a few seconds of joining you should catch up on the party's
quests, without pop-ups.

**Tip:** if the launcher says your join ticket expired, click **Join** in the launcher again. Each ticket is only
good for a short time. If you host and play on the same computer, join `127.0.0.1` rather than `localhost`.

### Checking it works

* Open your journal: quests the party has done should be there.
* Type `/qs mine` in chat: it lists the quests the server sees in your game.
* `UE4SS.log` (in `OblivionRemastered\Binaries\Win64\`) should contain lines starting with `[CoopQuestSyncUI]`
  after the restart.

If any of these don't happen, see [CONTRIBUTING.md](../CONTRIBUTING.md) for what to send.

### Removing it

Delete the two lines from `Plugins.txt`. Optionally, delete the add-on folder
`%AppData%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI` (paste that path into the File Explorer address bar).
