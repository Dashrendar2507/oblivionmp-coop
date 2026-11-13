# Co-op Quest Sync for OblivionMP

**Play the Oblivion Remastered story together.** Co-op Quest Sync is a mod for [OblivionMP](https://docs.ready.mp)
(Oblivion Remastered multiplayer, on the ReadyM framework) that keeps everyone on a server on the same page: quest
progress is shared live, players who join later catch up silently, and conversations, deaths, travel and rewards are
handled with a party in mind.

> **Version 0.7.0, for OblivionMP 1.1.** This is the first public release. It has been played a lot by a handful of
> people, but not by many, and not much yet on OblivionMP 1.1. Expect rough edges and
> [report what you find](CONTRIBUTING.md): every report helps.

## Disclaimer - use of AI
AI has been used extensively for the creation of this mod (Claude Opus 5.5), but there's always a human in the loop for decisions, testing, troubleshooting and support. The goal here was to make a personal project, but I've decided to open it up to the community until there is a high quality, human-made alternative available. This repo is purposely human-opinionated when it comes to gameplay decisions and defaults, but I have made sure to allow for as many customization options via the WebUI.

## What it does

**Shared quest progress**
* When anyone finishes a quest stage, every other player on the server gets it within a few seconds, with a chat
  line such as "Alice advanced The Path of Dawn: ...".
* Players who join later catch up on everything the party did, **silently**: no wall of quest pop-ups.
* Your own progress comes back every time you reconnect (OblivionMP doesn't keep the game's quest progress on its
  own).
* Covers the base game, Shivering Isles, Knights of the Nine, the official DLCs and the remaster's own plugins
  (287 quests).

**Stage effects, with switches.** Many quest stages do something to the player: teleport them, start a fight, hand
out items, change a faction rank, play a cut scene. The player who reaches the stage always gets everything. For
everyone else, each kind of effect can be switched on or off separately for three moments: live, on join, and on
rejoin. The defaults keep it sane: nobody gets teleported or thrown into a fight just for logging in.

**Shared conversations**
* When someone talks to an NPC, the others see an overlay styled like the game's dialogue screen: the NPC's name,
  the topic and the line. Watchers can't click anything.
* Watchers hear the NPC's voice with each line, played from their own game, and read the text in their own game's
  language. Nothing of the game's is shipped with the mod.
* Players in the same area stand still until the conversation ends (unless they're in combat).
* **Alt+U** leaves or rejoins a conversation you're watching. Optionally, players only listen in when they press it.
* **Alt+Y** takes you to whoever is talking, into their building or another world space too.
* Quest updates wait until you're out of a conversation.

**Party**
* **Revive instead of death.** When you die you go down. A teammate next to you presses **Alt+H** to bring you back.
  After 60 s, or when you press **Alt+G**, you respawn next to the party. If OblivionMP brings you back by itself
  first (its own "rebirth"), the mod still takes you back to the party.
* **Travel to a teammate any time** with `/coop goto <name>` or **Alt+O**. Players who'd rather be asked first type
  `/coop goto lock`.
* **Back where you left.** When you join, you come back where you left, inside buildings too, behind a black
  travel screen so you don't see the jumps.
* **New players choose where they start**: the game's prison intro (once per world, since the Emperor is shared) or
  the sewer exit with a starter kit, the Amulet of Kings and the character menus.
* **Calm a fight that went wrong**: `/coop calm` (your area, a player's area, every area, or the NPCs right next to
  you), or the **Calm** buttons on the web page.
* **Tags over players' heads**: talking, bartering, persuading, training, picking a lock, pickpocketing, down.
* Rewards handed out in conversation go to everyone. Quest items (keys, letters) can be shared too (off by default).
* Level scaling and difficulty for the whole session.

**Web admin page** on the server computer (http://localhost:9060/):
* Every quest and its stages, with the journal text, and which players have them.
* Give a stage to everyone, or reset a quest for everyone.
* Pick a **source player** ("leader only"): only their progress is shared, at once for everyone online and for
  players who join later. Or let anyone's progress count, which is the default.
* Every co-op setting, saved across server restarts, including the keys.
* **Admins**: the commands that change things for everybody (`/qs push`, `/qs reset`, `/coop calm`...) are only for
  the server's admins, taken from the ReadyM admin panel (port 9050), plus anyone you add with **Make admin**.

**The game's text, without shipping it.** NPC names, dialogue and journal entries belong to Bethesda, so the mod
never ships them and never downloads anything. Each player's game reads its own text, and the server learns it from
the players' games in the background the first time a player of each language joins.

See [docs/USING.md](docs/USING.md) for the keys, chat commands and every setting, and [CHANGELOG.md](CHANGELOG.md)
for what changed in each version.

## Install

The short version. The full, step-by-step guide is **[docs/INSTALL.md](docs/INSTALL.md)**, and it says for every
file whether it's on the server or on the player's PC.

1. **Server:** download the latest `CoopQuestSync-x.y.z.zip` from
   [Releases](https://github.com/Dashrendar2507/oblivionmp-coop/releases). Stop the server, copy the zip's
   `CoopQuestSync` folder into the server's `mods\` folder (so you get `<server folder>\mods\CoopQuestSync\manifest.json`),
   and start the server.
2. **Every player's PC:** join through the ReadyM Launcher as usual. The launcher downloads the mod and adds its two
   plugins to the game's plugin list for you while you play. Don't add them to `Plugins.txt` by hand: listed twice,
   they break the quest sync.
3. **Every player's PC, first join only:** the mod installs a small UE4SS add-on in
   `%APPDATA%\ReadyM.Launcher\Oblivion\Mods\CoopQuestSyncUI\` and a note asks you to **restart the game once**. After
   that, nothing else to do.

**Requirements:** Oblivion Remastered with all its official plugins (see [known issues](docs/KNOWN-ISSUES.md)), the
ReadyM Launcher, and an **OblivionMP 1.1.0 server** with the mods it ships with (the Chat mod is required). Version
0.7.0 doesn't load on an OblivionMP 1.0.x server, and older versions of the mod don't load on 1.1.

## Known issues

The important ones (full list in [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md)):

* **OblivionMP 1.1 is new**, and the mod has had little play on it so far. OblivionMP 1.1 replays some of the
  game's own scripts (conversation results, levers, message boxes) on nearby players' games, so a quest stage or a
  reward may arrive twice. Please report it if you see it.
* **OblivionMP's own issues** you'll probably meet: the game can crash when you quit; **R** is OblivionMP's
  "rebirth" and fires even while you type in the chat; OblivionMP brings a dead player back by itself after some
  seconds; paying a fine to a guard can move you; and a console command (`coc`, used to put you back inside a
  building) occasionally crashes the game.
* On joining, OblivionMP briefly shows "Your plugin load order differs from the server's, DLC areas will not
  sync". The launcher puts the mod's plugins before `AltarDeluxe.esp`, which OblivionMP doesn't expect. The mod
  works; the Deluxe content may not sync between players.
* Every player needs **all** the game's official plugins, including the **Deluxe** ones (`AltarDeluxe.esp`). The
  remaster comes with all of them.
* Friendly fire can't be turned off, and fights, followers and companions aren't shared: OblivionMP doesn't give
  mods a way to do it yet.
* The conversation overlay is a few seconds behind the talker. The voice plays with the text, from nowhere rather
  than from the NPC.
* Until the first player has been in for a few minutes, the web page shows names worked out from the game's ids.
* Quest stage scripts still run in each player's own game, so a few quests may behave oddly when replayed. Tell
  us which ones.

## Reporting bugs and giving feedback

Please do! **[CONTRIBUTING.md](CONTRIBUTING.md#please-test-these)** lists what we most need tested and where to find
the logs. Then [open an issue](https://github.com/Dashrendar2507/oblivionmp-coop/issues/new/choose). There are forms
for bug reports, test reports ("I played for an hour, here's how it went") and feature ideas.

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
4. The `CoopQuestSyncUI` UE4SS add-on does what neither the SDK nor a plugin can: it closes the "Quest Added"
   windows during the catch-up, runs a few console commands (`coc`, `cow`, `positioncell`, the calm commands),
   plays the watchers' voices, looks up the game's text and tells the server which of the remaster's screens is
   open (lockpicking, pickpocketing, bartering).

### Layout

- `mod/`: the mod itself (server and client halves, plus the generated plugins in `mod/Content/`). See
  `mod/TESTING.md` for the test plan.
- `mod/Shared/GameFiles.cs`: finds the game and reads its text from the `.pak` files (used by the server, and by
  the client as a first try before it asks the add-on).
- `docs/`: the install guide, the keys, commands and settings (`USING.md`), and the known issues.
- `plugin/tools/`: Python tools that generate the plugins from `Oblivion.esm`, including a small Oblivion script
  compiler. `plugin/token-ids.json` pins every stage's token id, so regenerating never changes the id of a token
  players already carry.
- `mod/CoopQuestSync.Server/web/index.html`: the admin page, served by the server mod on port 9060 (see
  `WebAdmin.cs`).
- `mod/CoopQuestSync.Client/web/`: the in-game conversation overlay and the on-screen notes.
- `ui/CoopQuestSyncUI/`: the UE4SS Lua add-on. The client copies it into the player's ReadyM mods folder by itself.
- `probe/`: the first test mod (inventory logging and `/qsprobe`).
- `tools/check_no_game_files.py`: fails when the game's own files (`strings.json`, `*.locres`, `*.esm`, the DLC
  plugins, learned text files) are in the repository or in a release zip. CI runs it on every push.

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
regenerating. To turn the game's text file (`Game.locres`, exported with FModel) into a text file for your own
server, see `plugin/tools/locres.py`. Never commit that file or put it in a zip: it's Bethesda's text, and
`tools/check_no_game_files.py` refuses it.

### Building the mod

Requires the .NET 10 SDK. The mod builds against the OblivionMP SDK 1.1.0 packages from nuget.org. From `mod/`:

```powershell
.\MakeModFolder.ps1 Release
```

The packaged mod lands in `mod/Output/mods/CoopQuestSync/`. Zip that `CoopQuestSync` folder for a release, and run
`python3 -I tools/check_no_game_files.py CoopQuestSync-x.y.z.zip` on the zip before uploading it.

## Credits

Made by Nicolas and Claude Opus 5.5, built on the [OblivionMP / ReadyM SDK](https://docs.ready.mp). Oblivion is
© Bethesda Softworks; this repository does not include any of the game's own files.

## License

The mod's code and tools are under the [MIT License](LICENSE): you can use, change and share them, as long as you
keep the copyright notice. The license doesn't cover the game or its files, which belong to Bethesda.
