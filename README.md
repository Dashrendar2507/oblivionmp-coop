# Co-op Quest Sync for OblivionMP

**Play the Oblivion Remastered story together.** Co-op Quest Sync is a mod for [OblivionMP](https://docs.ready.mp)
(Oblivion Remastered multiplayer, on the ReadyM framework) that keeps everyone on a server on the same page: quest
progress is shared live, players who join later catch up silently, and conversations, deaths and rewards are
handled with a party in mind.

> **Status: public test (0.5.4).** It works, but it is young and has only been tested by a handful of people.
> Expect rough edges, back up your saves, and please [report what you find](CONTRIBUTING.md). Every report helps.

## Disclaimer - use of AI
AI has been used extensively for the creation of this mod (Claude Opus 5.5), but there's always a human in the loop for decisions, testing, troubleshooting and support. The goal here was to make a personal project, but I've decided to open it up to the community until there is a high quality, human-made alternative available. This repo is purposely human-opinionated when it comes to gameplay decisions and defaults, but I have made sure to allow for as many customization options via the WebUI.

## What it does

**Shared quest progress**
* When anyone finishes a quest stage, every other player on the server gets it within a few seconds, with a chat
  line such as "Alice advanced The Path of Dawn: ...".
* Players who join later catch up on everything the party did, **silently**: no wall of quest pop-ups.
* Your own progress comes back every time you reconnect (OblivionMP forgets vanilla quest progress on its own).
* Covers the base game, Shivering Isles, Knights of the Nine, the official DLCs and the remaster's own plugins
  (287 quests).

**Stage effects, with switches.** Many quest stages do something to the player: teleport them, start a fight, hand
out items, change a faction rank, play a cut scene. The player who reaches the stage always gets everything. For
everyone else, each kind of effect can be switched on or off separately for three moments: live, on join, and on
rejoin. The defaults keep it sane: nobody gets teleported or thrown into a fight just for logging in.

**Shared conversations**
* When someone talks to an NPC, the others see an overlay styled like the game's dialogue screen: the NPC's name,
  the topic and the line. Watchers can't click anything.
* Players in the same area stand still until the conversation ends (unless they're in combat).
* The overlay says when the talker is bartering, persuading, training, picking a lock and so on.
* Watchers hear the NPC's voice with each line, played from their own game in their own game's language, and read
  the text in that language too, as do the chat lines (nothing of the game's is shipped with the mod).
* **Alt+U** leaves or rejoins a conversation you're watching. Optionally, players only listen in when they press it.
* **Alt+Y** takes you to whoever is talking, into their building or another world space too.
* Quest updates wait until you're out of a conversation.

**Party**
* **Revive instead of death.** When you die you go down. A teammate next to you presses **Alt+H** to bring you back.
  After 60 s, or when you press **Alt+G**, you respawn next to the party leader.
* **Travel to a teammate any time** with `/coop goto <name>` or **Alt+O**. Players who'd rather be asked first type
  `/coop goto lock`.
* **Back where you left.** When you join, you come back where you left, inside buildings too, behind a black
  travel screen so you don't see the jumps.
* **Calm a fight that went wrong**: `/coop calm` (your area, a player's area, every area, or the NPC next to you),
  or the **Calm** buttons on the web page.
* "Talking" and "Down" tags over players' heads.
* Rewards handed out in conversation go to everyone. Quest items (keys, letters) can be shared too (off by default).
* Level scaling and difficulty for the whole session.

**Web admin page** on the server machine (http://localhost:9060/):
* Every quest and its stages, with the journal text, and which players have them.
* Give a stage to everyone, or reset a quest for everyone.
* Pick a **source player** ("leader only"): only their progress is shared, at once for everyone online and for
  players who join later. Or let anyone's progress count, which is the default.
* Every co-op setting above, saved across server restarts.
* **Admins**: the commands that change things for everybody (`/qs push`, `/qs reset`, `/coop calm`...) are only for
  the server's admins, taken from the ReadyM admin panel (port 9050), plus anyone you add with **Make admin**.

See [docs/USING.md](docs/USING.md) for the keys, chat commands and every setting, and [CHANGELOG.md](CHANGELOG.md)
for what changed in each version.

## Install

The short version (the full, step-by-step guide is in **[docs/INSTALL.md](docs/INSTALL.md)**):

1. **Server host:** download the latest `CoopQuestSync-x.y.z.zip` from
   [Releases](https://github.com/Dashrendar2507/oblivionmp-coop/releases), put its `CoopQuestSync` folder in your
   OblivionMP server's `mods\` folder, and restart the server.
2. **Every player:** add two lines to the end of the game's `Plugins.txt`:
   ```
   CoopQuestSync.esp
   CoopQuestSyncDialogue.esp
   ```
   Then join through the ReadyM Launcher as usual. The launcher downloads the mod and the game plugins for you.
3. **Every player, first join only:** the mod installs a small UE4SS add-on and asks you to **restart the game
   once**. After that, nothing else to do.
4. **Nothing to do for the game's text.** The mod never ships Bethesda's text. Each player's add-on reads it from
   their own running game, in their own language, and the server learns it from the players' games the first time a
   player of each language joins (a few minutes in the background), for the web page and chat lines
   ([details](docs/INSTALL.md#real-names-and-lines-on-the-page-and-the-overlay)). Nothing is downloaded.

**Requirements:** Oblivion Remastered (all official plugins, including the Deluxe ones, see
[known issues](docs/KNOWN-ISSUES.md)), the ReadyM Launcher, and an OblivionMP 1.0.1 server with its standard mods
(the Chat mod is required). OblivionMP 1.1 servers need a version of the mod built for SDK 1.1, which is on its way.

## Known issues

The important ones (full list in [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md)):

* Every player currently needs **all** the game's official plugins, including the **Deluxe** ones
  (`AltarDeluxe.esp`). This shouldn't be an issue because the Remaster comes with all those.
* Friendly fire can't be turned off (OblivionMP doesn't tell mods who hit whom).
* The conversation overlay is a few seconds behind the talker. The voice plays with the text, from nowhere rather
  than from the NPC.
* The server learns the game's text from the players' games, since it can't read the game's files without an Oodle
  library the game doesn't ship (see Install, step 4). Until the first player has been in for a few minutes, the web
  page shows names worked out from the game's ids.
* Followers and companions aren't shared.
* Quest stage scripts still run in each player's own game, so a few quests may behave oddly when replayed. Tell
  us which ones.

## Reporting bugs and giving feedback

Please do! **[CONTRIBUTING.md](CONTRIBUTING.md#please-test-these)** lists what we most need tested (sleeping and
waiting with several players, for one) and where to find the logs. Then
[open an issue](https://github.com/Dashrendar2507/oblivionmp-coop/issues/new/choose). There are forms for bug
reports, test reports ("I played for an hour, here's how it went") and feature ideas.

## For developers

### How it works

The OblivionMP SDK can't read or set the game's own quests, so a small game plugin does that part:

1. `CoopQuestSync.esp` gives every vanilla quest stage a hidden "token" item. When a stage is done in the game, the
   plugin puts its token in the player's inventory. When a token appears for a stage the game hasn't done, the
   plugin sets that stage.
2. OblivionMP already syncs inventories with the server, so the server mod reads the tokens, keeps the shared
   progress, and gives tokens to the other players.
3. `CoopQuestSyncDialogue.esp` does the same for dialogue lines and menus, which is how the conversation overlay
   works.

### Layout

- `mod/`: the mod itself (server and client halves, plus the generated plugins in `mod/Content/`). See
  `mod/TESTING.md` for the full test plan.
- `mod/Shared/GameFiles.cs`: finds the game and reads its text from the `.pak` files (used by the server, and by
  the client as a first try before it asks the add-on).
- `docs/`: the install guide, the keys, commands and settings (`USING.md`), and the known issues.
- `plugin/tools/`: Python tools that generate the plugins from `Oblivion.esm`, including a small Oblivion script
  compiler. `plugin/token-ids.json` pins every stage's token id, so regenerating never changes the id of a token
  players already carry.
- `mod/CoopQuestSync.Server/web/index.html`: the admin page, served by the server mod on port 9060 (see
  `WebAdmin.cs`).
- `mod/CoopQuestSync.Client/web/index.html`: the in-game conversation overlay.
- `ui/CoopQuestSyncUI/`: a UE4SS Lua add-on that hides HUD pop-ups and closes the "Quest Added" windows while
  stages are replayed (so catching up is silent), and runs the console's `coc`, `cow` and calm commands (back where
  you left, travel, `/coop calm`), plays the watchers' voices and looks up the game's text. The client installs it by itself.
- `probe/`: the Phase 0 test mod (inventory logging and `/qsprobe`).
- `tools/check_no_game_files.py`: fails when the game's own files (`strings.json`, `*.locres`, `*.esm`, the DLC
  plugins) are in the repository or in a release zip. CI runs it on every push.

### Generating the plugins

Needs Python 3 and your own copy of the game's plugins, which are not in this repo.

```sh
# every quest, with the DLC and remaster plugins
python3 -I plugin/tools/gen_plugin.py path/to/Oblivion.esm mod/Content \
  --plugins DLCBattlehornCastle.esp,DLCFrostcrag.esp,DLCHorseArmor.esp,DLCMehrunesRazor.esp,DLCOrrery.esp,DLCShiveringIsles.esp,DLCSpellTomes.esp,DLCThievesDen.esp,DLCVileLair.esp,Knights.esp,AltarESPMain.esp,AltarDeluxe.esp
# check the script compiler against the game's own compiled scripts
python3 -I plugin/tools/check_compiler.py path/to/Oblivion.esm
# run the generated scripts against a model of the game and the server's join handshake
python3 -I plugin/tools/simulate_protocol.py mod/Content/CoopQuestSync.esp
```

This writes the plugins and `tokens.json`. The server DLL embeds `tokens.json`, so rebuild the mod after
regenerating. To turn the game's text file (`Game.locres`, exported with FModel) into `strings.json`, see
`plugin/tools/locres.py`. Never commit that file or put it in a zip: it's Bethesda's text, and
`tools/check_no_game_files.py` refuses it.

### Building the mod

Requires the .NET 10 SDK. From `mod/`:

```powershell
.\MakeModFolder.ps1 Release
```

The packaged mod lands in `mod/Output/mods/CoopQuestSync/`.

## Credits

Made by Nicolas and Claude Opus 5.5, built on the [OblivionMP / ReadyM SDK](https://docs.ready.mp). Oblivion is © Bethesda Softworks;
this repository does not include any of the game's own files.

## License

The mod's code and tools are under the [MIT License](LICENSE): you can use, change and share them, as long as you
keep the copyright notice. The license doesn't cover the game or its files, which belong to Bethesda.
