# OB-MP co-op mod

A co-op mod for OblivionMP (Oblivion Remastered on the ReadyM framework) that keeps quest progress in step for every player on a server, live and for players who join later.

## Status

Phase 0: proving the bridge. The SDK cannot read or set the game's own quests, so the plan is a small game plugin that mirrors quest stages into hidden "token" items. OblivionMP already syncs inventories with the server, so the server mod can read and write those tokens.

- `probe/`: a test mod that logs inventory changes on the server and the client, and adds or removes items from chat (`/qsprobe`). See `probe/TESTING.md` for the test steps.

## Building

Requires the .NET 10 SDK. From `probe/`:

```powershell
.\MakeModFolder.ps1 Release
```

The packaged mod lands in `probe/Output/mods/CoopQuestSync.Probe/`. Copy it into your server's `mods/` folder.
