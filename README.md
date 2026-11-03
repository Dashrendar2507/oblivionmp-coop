# OB-MP co-op mod

A co-op mod for OblivionMP (Oblivion Remastered on the ReadyM framework) that keeps quest progress in step for every player on a server, live and for players who join later.

## How it works

The SDK cannot read or set the game's own quests, so a small game plugin does that part:

1. `CoopQuestSync.esp` gives every vanilla quest stage a hidden "token" item. When a stage is done in the game, the plugin puts its token in the player's inventory. When a token appears for a stage the game hasn't done, the plugin sets that stage.
2. OblivionMP already syncs inventories with the server, so the server mod reads the tokens, keeps the shared progress, and gives tokens to the other players.

## Layout

- `mod/`: the real mod (server and client halves, plus the generated plugin in `mod/Content/`). See `mod/TESTING.md` for test steps.
- `plugin/tools/`: Python tools that generate the plugin from `Oblivion.esm`, including a small Oblivion script compiler. `plugin/token-ids.json` pins every stage's token id, so regenerating never changes the id of a token players already carry.
- `ui/CoopQuestSyncUI/`: a UE4SS Lua add-on that hides HUD pop-ups while the plugin replays stages, so catching up on join is silent.
- `probe/`: the Phase 0 test mod (inventory logging and `/qsprobe`).

## Generating the plugin

Needs Python 3 and your own copy of `Oblivion.esm`, which is not in this repo.

```sh
# every quest
python3 -I plugin/tools/gen_plugin.py path/to/Oblivion.esm mod/Content
# check the script compiler against the game's own compiled scripts
python3 -I plugin/tools/check_compiler.py path/to/Oblivion.esm
```

This writes `CoopQuestSync.esp` and `tokens.json`. The server DLL embeds `tokens.json`, so rebuild the mod after regenerating.

## Building the mod

Requires the .NET 10 SDK. From `mod/`:

```powershell
.\MakeModFolder.ps1 Release
```

The packaged mod lands in `mod/Output/mods/CoopQuestSync/`. Copy it into your server's `mods/` folder. Each player also copies `CoopQuestSync.esp` into the game's `Data` folder and adds it to `Plugins.txt` (see `mod/TESTING.md`).
