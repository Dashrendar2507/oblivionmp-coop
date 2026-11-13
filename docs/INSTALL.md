# Installing Co-op Quest Sync

This guide assumes you've never modded Oblivion before. There are two parts:

* **[The server](#part-1-the-server)**: the person who runs the OblivionMP server installs the mod once.
* **[Every player's PC](#part-2-every-players-pc)** (the host's too, if they play): one small edit to the game, then
  join.

Every step says where it happens: **Server** (the computer that runs the OblivionMP server) or **PC** (the computer
a player plays on). If you host and play on the same computer, they're still different folders.

If anything goes differently from what's written here, that's worth a [bug report](../CONTRIBUTING.md).

## What you need

* **Oblivion Remastered**, with all its official plugins. Every copy of the remaster has them; see
  [the Deluxe note](KNOWN-ISSUES.md#everyone-needs-the-deluxe-plugins) if you're not sure.
* The **ReadyM Launcher**, which you use to join OblivionMP servers.
* For the host: an **OblivionMP 1.1.0 server** (see "Server > Installation" in the
  [OblivionMP docs](https://docs.ready.mp)) with the mods it ships with, and the .NET 10 runtime it asks for.
  Co-op Quest Sync needs the **Chat** mod (`OblivionMP.Chat`), which comes with the server.

Co-op Quest Sync 0.7.0 is built for OblivionMP 1.1.0 and **doesn't load on an OblivionMP 1.0.x server**. Mods built
for 1.0 (Co-op Quest Sync 0.6 and older) don't load on 1.1.

## Where things are

`<server folder>` is the folder you unzipped the OblivionMP server into, the one with `server.exe` in it. `<game
folder>` is where Oblivion Remastered is installed: on Steam, by default,
`C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\` (in Steam: right-click **Oblivion
Remastered**, **Manage**, **Browse local files**).

| What | Where |
| --- | --- |
| The mod | **Server:** `<server folder>\mods\CoopQuestSync\` (`manifest.json`, `client\`, `server\`). Replaced on every update. |
| Shared progress and settings | **Server:** `<server folder>\coop-quest-sync-state.json`. Keep it across updates. |
| The game's text the server learned | **Server:** `<server folder>\coop-quest-sync-strings.<language>.json`, and `<server folder>\coop-quest-sync-strings.json` if you made one by hand. Never share them. |
| Optional settings file | **Server:** `<server folder>\mods\CoopQuestSync\server\config.json` |
| Server log | **Server:** the server console window, or the **Terminal** tab of the ReadyM admin panel (http://localhost:9050). The mod's lines start with `[QuestSync]`. |
| Co-op web page | **Server:** http://localhost:9060/ |
| Plugins.txt | **PC:** `<game folder>\OblivionRemastered\Content\Dev\ObvData\Data\Plugins.txt` |
| The add-on | **PC:** `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` (paste it into the File Explorer address bar). Installed by the mod on the first join. |
| UE4SS.log | **PC:** `<game folder>\OblivionRemastered\Binaries\Win64\UE4SS.log`. A new one each time the game starts. |

## Part 1: the server

### Install

1. **Server:** download the newest `CoopQuestSync-x.y.z.zip` from the
   [Releases page](https://github.com/Dashrendar2507/oblivionmp-coop/releases).
2. **Server:** stop the OblivionMP server if it's running.
3. **Server:** open the zip and copy its `CoopQuestSync` folder into `<server folder>\mods\`. It should end up like
   this:

   ```text
   <server folder>\
   ├── server.exe
   └── mods\
       ├── OblivionMp.Sdk\
       ├── OblivionMP.Chat\
       ├── ...
       └── CoopQuestSync\
           ├── manifest.json
           ├── client\
           └── server\
   ```

   Make sure it's `mods\CoopQuestSync\manifest.json`, not `mods\CoopQuestSync\CoopQuestSync\manifest.json`. Unzipping
   tools sometimes add an extra folder.
4. **Server:** start the server. In its log you should see `CoopQuestSync v0.7.0` in the list of mods it loads, and
   lines starting with `[QuestSync]`, including:
   * `Loaded 287 quests / ... stages from the token map`
   * `Quest admin page: http://localhost:9060/`
5. **Server:** open http://localhost:9060/ in a browser on the server computer. You should see the **Co-op Quest
   Sync** page.

That's it for the server. It hands the mod to every player when they join.

### Moving from an OblivionMP 1.0 server

OblivionMP 1.1 comes as a new server folder. Set it up as ReadyM describes (copy `saves`, `data`, `branding` and
`config.json` from the old server folder into the new one), then:

1. **Server:** copy `coop-quest-sync-state.json` and every `coop-quest-sync-strings*.json` from the old server
   folder into the new one, next to `server.exe`. They hold the shared progress, the settings and the game's text.
2. **Server:** copy the **0.7.0** `CoopQuestSync` folder from the zip into the new `<server folder>\mods\`. Don't
   copy the old 0.6 one: it doesn't load on 1.1.
3. **Server:** if you made `<old server folder>\mods\CoopQuestSync\server\config.json`, copy it to the same place in
   the new folder.

### The game's text (nothing to do)

The remaster keeps its text (NPC names, dialogue, journal entries) outside the plugins. That text belongs to
Bethesda, so the mod never ships it, and it never downloads anything onto anyone's computer. It reads the text from
each player's own copy of the game instead:

* **Players** (the conversation overlay): the add-on looks each line's text up in the running game, in the
  language the game is set to. With several languages in one party, everyone reads in their own.
* **The server** (the web page and chat lines) learns the text from the players' games. For each language it
  doesn't have yet, it asks one player of that language for the text, a batch at a time, in the background, once
  they're in the world (a few minutes for the whole game). It keeps it in
  `<server folder>\coop-quest-sync-strings.<language>.json` (for example `coop-quest-sync-strings.fr.json`), where
  updates of the mod don't touch it, so each language is learned once. Chat lines then reach each player in their
  own language. The web page uses the language set in `config.json` (`"Language"`, English by default), or whichever
  one it learned. A dedicated host without the game installed works the same way.

Until the first player has been in for a few minutes, the page shows names worked out from the game's ids ("Ysabel
Andronicus" from `LOC_FN_YsabelAndronicus`); everything else works.

The server's log says what happened (lines starting with `[QuestSync]`):

* `The game's text in fr (from the players' games): ...coop-quest-sync-strings.fr.json, 24730 of 24730.`: learned
  before. Done.
* `Couldn't read the game's text from OblivionRemastered-Windows.pak: ...no oo2core_9_win64.dll on this computer`,
  then `The server will learn the game's text from the players' games instead`: the usual case. The server also tries
  the game installed on its own computer, but the remaster's text is compressed with Oodle, whose library the Steam
  version doesn't include. Nothing to do.
* When a player joins: `Alice's game runs in fr.`, `Learning the game's text in fr from Alice's game ...`, progress
  lines, then `The game's text in fr is complete ...`.
* `Alice's game didn't send the text asked for in 45 s (is the CoopQuestSyncUI add-on running?)`: that player's
  add-on isn't running. The server asks another player of that language, or that player again next time they join.

To stop the server learning (if you only want a file you made yourself), set `"LearnTextFromPlayers": false` in
`config.json`. For the web page in another language, set `"Language"` (`de`, `fr`, `es`, `it`...).

<details>
<summary><b>Making the text file by hand</b> (optional, for hosts who'd rather not rely on the players)</summary>

1. **Server:** install [FModel](https://fmodel.app) and point it at the game (`<game folder>\OblivionRemastered\Content\Paks`).
2. **Server:** find `Localization/Game/en/Game.locres` (or your language instead of `en`), right-click it and choose
   **Export Raw Data**.
3. **Server:** with Python 3 installed, from a copy of this repository:
   ```sh
   python3 -I plugin/tools/locres.py path/to/Game.locres strings.json --keys mod/Content
   ```
4. **Server:** put `strings.json` in `<server folder>\mods\CoopQuestSync\server\` (next to
   `CoopQuestSync.Server.dll`) and restart the server. The server copies it to
   `<server folder>\coop-quest-sync-strings.json`, where it stays across updates, and logs
   `Copied the game's text to ...`. You can then delete the `strings.json` in `mods\CoopQuestSync\server\`.

A hand-made file always comes first; the log then says `The game's text: <server folder>\coop-quest-sync-strings.json`.
Never share that file or put it in a zip: it's Bethesda's text.

</details>

### Letting other people open the web page

By default only the server computer can open the page. To open it to your network, create
`<server folder>\mods\CoopQuestSync\server\config.json` on the **server** with:

```json
{ "WebBind": "0.0.0.0", "WebPassword": "pick-a-password" }
```

The page refuses to start on the network without a password. Don't open port 9060 to the internet.

### Updating to a new version

1. **Server:** stop the server.
2. **Server:** delete `<server folder>\mods\CoopQuestSync\` and copy in the new `CoopQuestSync` folder from the zip.
   If you had a `config.json` in its `server\` folder, keep a copy and put it back.
3. **Server:** keep `<server folder>\coop-quest-sync-state.json` and `coop-quest-sync-strings*.json`: they hold the
   shared progress, every setting from the web page and the game's text, and they're outside the mod's folder.
4. **Server:** start the server. Players get the update automatically the next time they join. If the update
   changes the add-on, a note on their screen asks them to restart the game once.

### Starting over

To wipe the shared progress, click **Reset all shared progress** on the web page. It clears the server's progress
and takes the hidden quest tokens back from everyone, so the quests don't come back on the next join. To reset a
single quest, open it on the page and use its reset button.

### Uninstalling

**Server:** delete `<server folder>\mods\CoopQuestSync\` and restart the server. You can also delete
`coop-quest-sync-state.json` and `coop-quest-sync-strings*.json` from `<server folder>`. Players should remove the
two lines they added to `Plugins.txt` (see below).

## Part 2: every player's PC

### One-time setup: Plugins.txt

The game only loads plugins listed in a text file called `Plugins.txt`. The launcher copies our two plugins into
the game for you when you join, but you need to add them to the list once.

1. **PC:** find the game folder. On Steam: right-click **Oblivion Remastered** in your library, **Manage**,
   **Browse local files**. By default it's `C:\Program Files (x86)\Steam\steamapps\common\Oblivion Remastered\`.
2. **PC:** open `OblivionRemastered\Content\Dev\ObvData\Data\` inside it.
3. **PC:** open `Plugins.txt` in Notepad.
4. **PC:** add these two lines at the **very end** of the file, then save:

   ```text
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```

   Don't remove anything that's already there. The game's own plugins (`Oblivion.esm`, `Knights.esp`, the `DLC*.esp`
   files, `AltarESPMain.esp`, `AltarDeluxe.esp`...) must stay above our two.

You won't find `CoopQuestSync.esp` in that folder before you join a server: that's normal. The launcher puts the
plugins there when you join and removes them when you leave, so they never affect your single-player game.

### First join

1. **PC:** join the server through the ReadyM Launcher, as usual. The launcher downloads the mod. On OblivionMP 1.1
   the game starts straight in the multiplayer world, without the main menu.
2. **PC:** on your first join, the mod installs its add-on into
   `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\`. It hides pop-ups while you catch up, puts you back
   inside buildings, plays the voices and reads the game's text. A note in the top left corner tells you to restart
   the game; it stays until you do.
3. **PC:** quit the game and join again from the launcher.

From then on, joining is all there is to it. Within a few seconds of joining you should catch up on the party's
quests, without pop-ups. A brand-new character gets a window first, asking where to start (see
[USING.md](USING.md#how-it-plays)).

**Tip:** if the launcher says your join ticket expired, click **Join** in the launcher again. Each ticket is only
good for a short time. If you host and play on the same computer, join `127.0.0.1` rather than `localhost`.

### Checking it works

* Open your journal: quests the party has done should be there.
* Type `/qs mine` in chat: it lists the quests the server sees in your game.
* **PC:** `<game folder>\OblivionRemastered\Binaries\Win64\UE4SS.log` should contain lines starting with
  `[CoopQuestSyncUI]` after the restart.

If any of these don't happen, see [CONTRIBUTING.md](../CONTRIBUTING.md) for what to send.

### If the game crashes on load

**PC:** put an empty file named `off` (`off.txt` works too) in
`%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` and restart the game. That switches the add-on off;
quest sync still works, but quest pop-ups show while you catch up, and voices, travel into buildings and calm
don't. Tell us whether the crashes stopped (see [KNOWN-ISSUES.md](KNOWN-ISSUES.md#the-ue4ss-add-on)). Delete `off`
to turn the add-on back on.

### Removing it

1. **PC:** delete the two lines from `<game folder>\OblivionRemastered\Content\Dev\ObvData\Data\Plugins.txt`.
2. **PC (optional):** delete the add-on folder `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\`.
