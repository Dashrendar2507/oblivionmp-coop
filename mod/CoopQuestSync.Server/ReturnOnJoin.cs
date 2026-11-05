using System.Numerics;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>Where a player last stood, kept in the state file by nickname.</summary>
public sealed class ReturnSpot
{
    public string Area { get; set; } = "";
    /// <summary>The interior's editor id (for the console's coc), or null outdoors.</summary>
    public string? Cell { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    public Vector3 Position => new(X, Y, Z);
}

/// <summary>
/// Puts a player back where they left when they join, as a vanilla save would.
///
/// The game starts every session from the player's local save, wherever that was, and OblivionMP does not move them
/// to where they last played. So the server remembers where each player last stood on the ground. Once they have
/// loaded in:
/// <list type="bullet">
/// <item>in the same area as that spot: the server moves them there (a position write).</item>
/// <item>the spot is inside a building: the server cannot change a player's area, but the game's console can. The
/// player's game is asked to run <c>coc &lt;cell&gt;</c> (the UE4SS add-on does it), and once they are in, the server
/// moves them to the exact spot.</item>
/// <item>if that fails: their last spot outdoors, when it is in the area they are in.</item>
/// </list>
/// </summary>
[Service]
public sealed partial class ReturnOnJoin(IEntities entities, IServerEvents events, QuestTokenSync sync, GameData data,
    CoopRpc rpc, ILogger logger)
{
    /// <summary>How long a player stays in an area before they are moved: the game must have finished loading it.</summary>
    private const float SettleSeconds = 4f;
    /// <summary>How long to wait for the game to take the player into the building.</summary>
    private const float CellTimeoutSeconds = 25f;
    private const float RecordEverySeconds = 2f;
    private const float SaveEverySeconds = 60f;
    /// <summary>Faster than this up or down (cm/s) is falling or jumping, not standing.</summary>
    private const float GroundedSpeed = 80f;
    /// <summary>Closer than this (cm) to the spot already: no need to move.</summary>
    private const float CloseEnough = 300f;

    private sealed class Session
    {
        public string? Area;
        public float Since;
        /// <summary>Recording starts once the player is back (or we gave up): before, they stand on the local save's spot.</summary>
        public bool Resolved;
        public bool Started;
        public ReturnSpot? Going;
        public float GoingSince;
        public Vector3? Target;
        public float MovedAt;
        public bool Checked;
        public float LastRecord;
    }

    private readonly Dictionary<PlayerId, Session> _sessions = new();
    private readonly Dictionary<PlayerId, ReturnSpot> _pinned = new();
    private float _clock;
    private float _elapsed;
    private float _sinceSave;
    private readonly HashSet<string> _unknownAreas = new();

    private void Start() => events.OnPlayerDisconnected += OnPlayerDisconnected;

    private void Stop() => events.OnPlayerDisconnected -= OnPlayerDisconnected;

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __)
    {
        _sessions.Remove(player);
        _pinned.Remove(player);
        sync.MarkDirty();   // the state file gets their last spot now
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < 0.5f)
            return;
        _elapsed = 0;

        _sinceSave += 0.5f;
        if (_sinceSave >= SaveEverySeconds)
        {
            _sinceSave = 0;
            sync.MarkDirty();
        }

        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            if (!sync.Players.TryGetValue(id, out var view) || view.Area is not { } areaId)
                continue;
            var area = GameData.AreaKey(areaId);
            if (!_sessions.TryGetValue(id, out var s))
                _sessions[id] = s = new Session();
            if (s.Area != area)
            {
                s.Area = area;
                s.Since = _clock;
            }
            var name = sync.Nickname(id);
            var settled = _clock - s.Since >= SettleSeconds;

            if (!s.Started)
            {
                if (!settled)
                    continue;
                s.Started = true;
                if (sync.Settings.ReturnOnJoin && sync.Returns.TryGetValue(name, out var spot))
                    GoTo(main, name, area, s, spot);
                else
                    s.Resolved = true;
                continue;
            }

            if (s.Going is { } going)
            {
                if (area == going.Area && settled)
                {
                    s.Going = null;
                    Place(main, name, s, going, "back inside");
                }
                else if (_clock - s.GoingSince > CellTimeoutSeconds)
                {
                    s.Going = null;
                    logger.LogWarning("[QuestSync] {Player}: the game didn't take them into {Cell} (is the CoopQuestSyncUI add-on installed? see UE4SS.log).",
                        name, going.Cell);
                    if (sync.Outdoors.TryGetValue(name, out var outside) && Fix(outside).Area == area)
                        Place(main, name, s, outside, "outside instead: couldn't take you back inside");
                    else
                        s.Resolved = true;
                }
                continue;
            }

            // The game may put them back where it loaded them: check once, a couple of seconds after the move.
            if (s.Target is { } t && !s.Checked && _clock - s.MovedAt > 2f)
            {
                s.Checked = true;
                if (Vector3.Distance(main.Position, t) > CloseEnough)
                {
                    main.Position = t;
                    logger.LogInformation("[QuestSync] {Player} moved back again (the game had put them elsewhere).", name);
                }
            }

            if (s.Resolved && settled)
                Record(main, name, area, s);
        }
    }

    /// <summary>Spots saved before 0.5's last fix hold "AreaId[...]" and no cell: bring them up to date.</summary>
    private ReturnSpot Fix(ReturnSpot spot)
    {
        if (spot.Area.StartsWith("AreaId[", StringComparison.Ordinal) && spot.Area.EndsWith(']'))
            spot.Area = spot.Area[7..^1];
        if (GameData.IsInterior(spot.Area))
            spot.Cell ??= data.InteriorCell(spot.Area);
        return spot;
    }

    /// <summary>Starts bringing a player to <paramref name="spot"/>: a position write, or the console's coc first.</summary>
    private void GoTo(MainCharacter main, string name, string area, Session s, ReturnSpot spot)
    {
        Fix(spot);
        if (spot.Area == area)
        {
            Place(main, name, s, spot, "back where you left");
            return;
        }
        if (spot.Cell is { } cell)
        {
            s.Going = spot;
            s.GoingSince = _clock;
            rpc.SendGoToCell(main.PlayerId, cell);
            logger.LogInformation("[QuestSync] {Player} left inside {Cell}: asking their game to go there.", name, cell);
            return;
        }
        if (sync.Outdoors.TryGetValue(name, out var outside) && Fix(outside).Area == area && !GameData.IsInterior(area))
        {
            Place(main, name, s, outside, "back outside where you were");
            return;
        }
        logger.LogInformation("[QuestSync] {Player} left in {Then} but joined in {Now}; they stay where the game put them.", name, spot.Area, area);
        s.Resolved = true;
    }

    private void Place(MainCharacter main, string name, Session s, ReturnSpot spot, string what)
    {
        s.Resolved = true;
        if (main.IsDead || Vector3.Distance(main.Position, spot.Position) <= CloseEnough)
            return;
        main.Position = spot.Position;
        main.Rotation = spot.Yaw;
        s.Target = spot.Position;
        s.MovedAt = _clock;
        s.Checked = false;
        rpc.SendNotice(main.PlayerId, char.ToUpperInvariant(what[0]) + what[1..] + ".");
        logger.LogInformation("[QuestSync] {Player}: {What} ({X:0}, {Y:0}, {Z:0} in {Area}).", name, what, spot.X, spot.Y, spot.Z, spot.Area);
    }

    /// <summary>Notes where the player stands, every couple of seconds, when they are alive and on the ground.</summary>
    private void Record(MainCharacter main, string name, string area, Session s)
    {
        if (main.IsDead || MathF.Abs(main.VerticalSpeed) > GroundedSpeed || _clock - s.LastRecord < RecordEverySeconds)
            return;
        s.LastRecord = _clock;
        var interior = GameData.IsInterior(area);
        var cell = interior ? data.InteriorCell(area) : null;
        if (interior && cell is null && _unknownAreas.Add(area))
            logger.LogWarning("[QuestSync] Interior {Area}: no cell found for it, so a player who leaves here comes back outside.", area);
        var p = main.Position;
        var spot = new ReturnSpot { Area = area, Cell = cell, X = p.X, Y = p.Y, Z = p.Z, Yaw = main.Rotation };
        sync.Returns[name] = spot;
        if (!interior)
            sync.Outdoors[name] = spot;
    }

    /// <summary>Test: remembers where the player stands now, for <see cref="TestBack"/>.</summary>
    public string TestPin(PlayerId id)
    {
        var name = sync.Nickname(id);
        if (!sync.Returns.TryGetValue(name, out var spot))
            return "No spot recorded yet: stand still for a few seconds.";
        _pinned[id] = spot;
        return $"Pinned {spot.Cell ?? "an outdoor spot"} ({spot.X:0}, {spot.Y:0}, {spot.Z:0}). Walk away (out of the building too), then /coop test back.";
    }

    /// <summary>Test: brings the player to the pinned spot (else their recorded one) as a join would.</summary>
    public string TestBack(PlayerId id)
    {
        if (!entities.TryLookup(id, out MainCharacter main) || !sync.Players.TryGetValue(id, out var view) || view.Area is not { } areaId)
            return "The server doesn't know where you are yet.";
        var name = sync.Nickname(id);
        if (!_pinned.TryGetValue(id, out var spot) && !sync.Returns.TryGetValue(name, out spot))
            return "Nothing to go back to: /coop test pin first.";
        Fix(spot);
        if (!_sessions.TryGetValue(id, out var s))
            _sessions[id] = s = new Session { Started = true };
        var here = GameData.AreaKey(areaId);
        if (spot.Area == here)
        {
            GoTo(main, name, here, s, spot);
            return "You are in the same place as that spot: moved you onto it.";
        }
        if (GameData.IsInterior(spot.Area) && spot.Cell is null)
            return $"That spot is in {spot.Area}, which isn't in the table of interiors, so I can't take you there.";
        GoTo(main, name, here, s, spot);
        return s.Going is not null
            ? $"Asking your game to go into {spot.Cell} (the add-on runs: coc {spot.Cell})..."
            : $"That spot is in {spot.Area} and you are in {here}: the server can't take you there.";
    }

    /// <summary>For /coop test status.</summary>
    public string Describe(PlayerId id)
    {
        var name = sync.Nickname(id);
        return sync.Returns.TryGetValue(name, out var spot)
            ? $"Last spot: {spot.Cell ?? "outdoors"} in {spot.Area} ({spot.X:0}, {spot.Y:0}, {spot.Z:0})."
            : "No spot recorded yet.";
    }
}
