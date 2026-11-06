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
   [Releases page](https://github.com/Dashrendar2507/OB-MP-coop-mod/releases).
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

### Optional: real names and lines on the page and the overlay

The remaster keeps its text (NPC names, dialogue, journal entries) outside the plugins, so without help the
overlay and the web page show keys such as `LOC_FN_YsabelAndronicus` instead of words. The text belongs to
Bethesda, so the mod never ships it: each server host makes a `strings.json` from their own copy of the game.
Please don't share your `strings.json` either.

1. Install [FModel](https://fmodel.app) and point it at the game (`OblivionRemastered\Content\Paks`).
2. Find `Localization/Game/en/Game.locres` (or your language instead of `en`), right-click it and choose
   **Export Raw Data**.
3. With Python 3 installed, from a copy of this repository:
   ```sh
   python3 -I plugin/tools/locres.py path/to/Game.locres strings.json --keys mod/Content
   ```
4. Put `strings.json` in the server's `mods\CoopQuestSync\server\` folder and restart the server. The log line
   `Game data: ... text entries` should show a number other than 0.

If this is too much, skip it: everything works, only the words are missing.

### Letting other people open the web page

By default only the server computer can open the page. To open it to your network, create a file called
`config.json` in `mods\CoopQuestSync\server\` with:

```json
{ "WebBind": "0.0.0.0", "WebPassword": "pick-a-password" }
```

The page refuses to start on the network without a password. Don't open port 9060 to the internet.

### Updating to a new version

1. Stop the server.
2. If you made a `strings.json`, move it out of `mods\CoopQuestSync\server\` for a moment.
3. Delete the old `mods\CoopQuestSync` folder, copy in the new one, and put `strings.json` back in its `server\`
   folder.
4. Keep `coop-quest-sync-state.json`: it holds the shared progress and every setting from the web page. It's in
   the folder the server runs from, usually next to `server.exe`. The server prints its full path in the `[QuestSync]`
   line it logs at start-up.
5. Start the server. Players get the update automatically the next time they join.

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
   and to put you back inside buildings). A message tells you: **"Restart the game once"**.
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
