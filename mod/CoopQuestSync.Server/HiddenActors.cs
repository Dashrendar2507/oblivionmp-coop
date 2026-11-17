using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Archetypes.Mixins;
using OblivionMP.SDK.Serverside.Archetypes;
using OblivionMP.SDK.Serverside.Items;
using OblivionMP.SDK.Serverside.Spawners;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;
using CellScope = ReadyM.SDK.Archetypes.Core.Cell;

namespace CoopQuestSync.Server;

/// <summary>
/// World state sync, part 2: the NPCs and creatures that wait hidden until a script brings them in (the "initially
/// disabled" ones: the rats that break through the prison wall, quest ambushes, people who turn up for a quest). The
/// OblivionMP server never creates them: a player's game reports only the actors it shows, and the seed loader seeds
/// furniture, containers, plants, doors, activators and items, not actors. So when a script enables one, the server
/// has nothing to enable and refuses ("refused Disabled False ... NoEntity", 2026-11-17: the prison rats never came and
/// the wall never broke). This service creates them on the server, still hidden, in their cell once a player is there,
/// from the server's own seed (the seed loader's seed.zip: same refs, same cells, same load order). Then the script's
/// enable goes through for everyone, as it does for a lever or a door. Leveled creatures (rolled from a list) are left
/// out: the server rolls those its own way.
/// </summary>
[Service]
public sealed partial class HiddenActors(IEntities entities, QuestTokenSync sync, Inventories inventories, LeveledListRoll lists,
    ILogger logger)
{
    internal readonly record struct Hidden(uint RefId, uint BaseId, Vector3 Position, float RotationZ, string? EditorId);

    private Task<Dictionary<FullCellId, List<Hidden>>>? _loading;
    private Dictionary<FullCellId, List<Hidden>>? _byCell;
    /// <summary>Cells already checked since the server started (the entities made are in the world save after that).</summary>
    private readonly HashSet<FullCellId> _done = new();
    /// <summary>Cells seen discovered (the master's game reported its actors) on an earlier pass.</summary>
    private readonly HashSet<FullCellId> _discovered = new();
    private float _elapsed;

    private void Start() => _loading = Task.Run(Load);

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < 2f)
            return;
        _elapsed = 0;
        if (_byCell is null)
        {
            if (_loading is not { IsCompleted: true } loading)
                return;
            _loading = null;
            if (loading.IsCompletedSuccessfully)
                _byCell = loading.Result;
            else
            {
                logger.LogWarning(loading.Exception?.GetBaseException(), "[QuestSync] Hidden NPCs: couldn't read the seed. Scripts that bring in hidden NPCs won't work for them.");
                _byCell = new();
            }
        }
        if (_byCell.Count == 0 || !sync.Settings.HiddenNpcs)
            return;

        List<(CellScope Cell, PlayerId Owner, List<Hidden> Actors)>? todo = null;
        foreach (var cell in entities.Query<CellScope>())
        {
            var id = cell.FullCellId;
            if (_done.Contains(id) || !_byCell.TryGetValue(id, out var actors))
                continue;
            // Made once a player is in the cell: that player's game runs its NPCs, as for the ones it reports itself.
            var owner = cell.MasterClient;
            if (owner == PlayerId.Invalid || owner == PlayerId.Server)
                continue;
            // Only once the master's game has reported the actors it shows there, and one pass later (2 s): an actor that
            // world.sav already has in the world comes from that report, shown, and must not be made here hidden.
            var cellRef = cell;
            if (!cellRef.IsDiscovered || _discovered.Add(id))
                continue;
            (todo ??= []).Add((cell, owner, actors));
        }
        if (todo is null)
            return;

        var have = new HashSet<uint>();
        foreach (var held in entities.Query<RefIdMixin>())
            have.Add(held.RefId);
        foreach (var (cell, owner, actors) in todo)
        {
            _done.Add(cell.FullCellId);
            var made = new List<string>();
            foreach (var actor in actors)
            {
                // Already on the server: made here earlier (kept in the world save) or reported by a game.
                if (!have.Add(actor.RefId))
                    continue;
                var npc = entities.Create<Npc>(cell, owner);
                npc.RefId = actor.RefId;
                npc.BaseId = new FormId(actor.BaseId);
                npc.Position = TesUnrealSpace.ToUnrealLocation(actor.Position);
                npc.Rotation = TesUnrealSpace.ToUnrealYawDegrees(actor.RotationZ);
                npc.IsDisabled = true;
                Dress(actor.BaseId, owner, npc.As<Equipment>());
                made.Add(actor.EditorId ?? $"{actor.RefId:X8}");
            }
            if (made.Count > 0)
                logger.LogInformation("[QuestSync] Hidden NPCs: {Count} in {Cell} now on the server, still hidden, for scripts to bring in: {Names}",
                    made.Count, cell.FullCellId, string.Join(", ", made.Take(12)) + (made.Count > 12 ? ", ..." : ""));
        }
    }

    /// <summary>
    /// Their inventory, rolled as the server rolls the NPCs a game reports (OblivionMP's own NpcInventoryRoll, which a mod
    /// can't call): the base form's items, leveled lists at the owner's level. Each game dresses an NPC from these.
    /// </summary>
    private void Dress(uint baseId, PlayerId owner, Equipment equipment)
    {
        NpcTemplate? found = null;
        foreach (var template in entities.Query<NpcTemplate>())
            if (template.Id == new FormId(baseId))
            {
                found = template;
                break;
            }
        if (found is not { } t)
            return;
        var playerLevel = 1;
        foreach (var main in entities.Query<MainCharacter>())
            if (main.PlayerId == owner)
                playerLevel = Math.Max(1, (int)main.Level);
        var level = Math.Min(playerLevel, NpcLevel(t.Level, t.PcLevelOffset, t.CalcMin, t.CalcMax, playerLevel));
        for (var i = 0; i < t.ItemsCount; i++)
        {
            var item = t.GetItems(i);
            var count = Math.Abs(item.Count);
            if (item.Kind != FormType.LeveledItem)
            {
                inventories.Add(equipment, new InventoryEntry(item.Form, count, 0));
                continue;
            }
            foreach (var rolled in lists.Roll(item.Form, level, count).Items)
                inventories.Add(equipment, new InventoryEntry(rolled.Form, rolled.Count, 0));
        }
        if (equipment.ItemsCount > 0)
            equipment.IsServerSeeded = true;
    }

    /// <summary>The game's NPC level: its own, or offset from the player's within its bounds (0: no bound).</summary>
    internal static int NpcLevel(int level, bool pcOffset, int calcMin, int calcMax, int playerLevel)
    {
        if (!pcOffset)
            return level;
        var l = playerLevel + level;
        if (calcMin != 0 && l < calcMin)
            return calcMin;
        if (calcMax != 0 && l > calcMax)
            return calcMax;
        return Math.Max(l, 1);
    }

    private Dictionary<FullCellId, List<Hidden>> Load()
    {
        var path = FindSeed();
        if (path is null)
        {
            logger.LogWarning("[QuestSync] Hidden NPCs: no seed.zip found in the server's mods folder (is the OblivionMP seed loader installed?). Scripts that bring in hidden NPCs won't work for them.");
            return new();
        }
        var byCell = Read(path, out var leveled);
        logger.LogInformation("[QuestSync] Hidden NPCs: {Count} hidden NPCs and creatures in {Cells} cells, from {Path} ({Leveled} hidden leveled creatures left to the server).",
            byCell.Values.Sum(l => l.Count), byCell.Count, path, leveled);
        return byCell;
    }

    /// <summary>The hidden (initially disabled) NPCs and creatures of a seed, by cell. Leveled creatures are only counted.</summary>
    internal static Dictionary<FullCellId, List<Hidden>> Read(string seedZip, out int leveled)
    {
        leveled = 0;
        var byCell = new Dictionary<FullCellId, List<Hidden>>();
        using var zip = ZipFile.OpenRead(seedZip);
        using var index = JsonDocument.Parse(ReadEntry(zip, "world/index.json") ?? throw new InvalidDataException("The seed has no world/index.json"));
        foreach (var area in index.RootElement.GetProperty("Areas").EnumerateArray())
        {
            var areaId = area.GetProperty("AreaId").GetString()!;
            var interior = area.TryGetProperty("IsInterior", out var i) && i.ValueKind == JsonValueKind.True;
            foreach (var cell in area.GetProperty("Cells").EnumerateArray())
            {
                var text = ReadEntry(zip, "world/" + cell.GetProperty("File").GetString());
                if (text is null || !text.Contains("InitiallyDisabled"))
                    continue;
                List<Hidden>? list = null;
                using var file = JsonDocument.Parse(text);
                foreach (var r in file.RootElement.GetProperty("References").EnumerateArray())
                {
                    if (!r.TryGetProperty("InitiallyDisabled", out var off) || off.ValueKind != JsonValueKind.True)
                        continue;
                    var type = r.GetProperty("BaseType").GetString();
                    if (type == "LeveledCreature")
                        leveled++;
                    if (type is not ("NPC" or "Creature"))
                        continue;
                    var pos = r.GetProperty("Position");
                    var rotZ = r.TryGetProperty("Rotation", out var rot) && rot.TryGetProperty("Z", out var z) ? z.GetSingle() : 0f;
                    list ??= [];
                    list.Add(new Hidden(r.GetProperty("RefId").GetUInt32(), r.GetProperty("BaseId").GetUInt32(),
                        new Vector3(pos.GetProperty("X").GetSingle(), pos.GetProperty("Y").GetSingle(), pos.GetProperty("Z").GetSingle()),
                        rotZ, r.TryGetProperty("BaseEditorId", out var ed) ? ed.GetString() : null));
                }
                if (list is null)
                    continue;
                var cellId = interior ? OblivionCellIds.Interior : OblivionCellIds.Exterior(cell.GetProperty("X").GetInt32(), cell.GetProperty("Y").GetInt32());
                var key = new FullCellId(new AreaId(areaId), cellId);
                if (byCell.TryGetValue(key, out var existing))
                    existing.AddRange(list);
                else
                    byCell[key] = list;
            }
        }
        return byCell;
    }

    private static string? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
            return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>The seed loader's seed.zip, in the server's mods folder (it reads it from its own server/content folder).</summary>
    private static string? FindSeed()
    {
        // The server runs in its own folder; AppContext.BaseDirectory can be empty on Windows (see PlayerSaves).
        var places = new List<string> { Directory.GetCurrentDirectory() };
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(HiddenActors).Assembly.Location) })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var count = 0;
            for (var dir = new DirectoryInfo(start); dir is not null && count < 5; dir = dir.Parent, count++)
                places.Add(dir.FullName);
        }
        foreach (var place in places.Distinct())
        {
            var mods = Path.GetFileName(place).Equals("mods", StringComparison.OrdinalIgnoreCase) ? place : Path.Combine(place, "mods");
            if (!Directory.Exists(mods))
                continue;
            IEnumerable<string> found;
            try
            {
                found = Directory.EnumerateFiles(mods, "seed.zip", SearchOption.AllDirectories).ToList();
            }
            catch (Exception)
            {
                continue;
            }
            // A clone without Git LFS has a small text file in the seed's place.
            foreach (var file in found)
                if (new FileInfo(file).Length > 4096)
                    return file;
        }
        return null;
    }
}
