using System.Numerics;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>Where a player last stood outdoors, kept in the state file by nickname.</summary>
public sealed class ReturnSpot
{
    public string Area { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
}

/// <summary>
/// Puts a player back where they left when they join, as a vanilla save would.
///
/// The game starts every session from the player's local save, wherever that was, and OblivionMP does not move them
/// to where they last played. So the server remembers where each player last stood on the ground outdoors, and moves
/// them there once they have loaded into that same exterior. Someone who left from inside a building comes back outside
/// the door they went in by: the server can move a player within the area they are in, but cannot send them into an
/// interior.
/// </summary>
[Service]
public sealed partial class ReturnOnJoin(IEntities entities, IServerEvents events, QuestTokenSync sync, CoopRpc rpc, ILogger logger)
{
    private const string InteriorPrefix = "L_PersistentDungeon";
    /// <summary>How long a joining player stays in their first area before they are moved: the game must have finished loading.</summary>
    private const float SettleSeconds = 4f;
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
        public bool Done;
        public Vector3? Target;
        public float MovedAt;
        public bool Checked;
        public float LastRecord;
    }

    private readonly Dictionary<PlayerId, Session> _sessions = new();
    private float _clock;
    private float _elapsed;
    private float _sinceSave;

    private void Start() => events.OnPlayerDisconnected += OnPlayerDisconnected;

    private void Stop() => events.OnPlayerDisconnected -= OnPlayerDisconnected;

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __)
    {
        _sessions.Remove(player);
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
            var area = areaId.ToString();
            if (!_sessions.TryGetValue(id, out var s))
                _sessions[id] = s = new Session();
            if (s.Area != area)
            {
                s.Area = area;
                s.Since = _clock;
            }
            var name = sync.Nickname(id);

            if (!s.Done)
            {
                // Nothing is recorded before this: the first spot of a session is the local save's, not where they left.
                if (_clock - s.Since < SettleSeconds)
                    continue;
                s.Done = true;
                MoveBack(main, name, area, s);
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

            if (area.StartsWith(InteriorPrefix, StringComparison.Ordinal) || main.IsDead
                || MathF.Abs(main.VerticalSpeed) > GroundedSpeed || _clock - s.LastRecord < RecordEverySeconds)
                continue;
            s.LastRecord = _clock;
            var p = main.Position;
            sync.Returns[name] = new ReturnSpot { Area = area, X = p.X, Y = p.Y, Z = p.Z, Yaw = main.Rotation };
        }
    }

    private void MoveBack(MainCharacter main, string name, string area, Session s)
    {
        if (!sync.Settings.ReturnOnJoin || !sync.Returns.TryGetValue(name, out var spot))
            return;
        if (spot.Area != area)
        {
            logger.LogInformation("[QuestSync] {Player} left in {Then} but joined in {Now}; they stay where the game put them.", name, spot.Area, area);
            return;
        }
        var target = new Vector3(spot.X, spot.Y, spot.Z);
        if (main.IsDead || Vector3.Distance(main.Position, target) <= CloseEnough)
            return;
        main.Position = target;
        main.Rotation = spot.Yaw;
        s.Target = target;
        s.MovedAt = _clock;
        rpc.SendNotice(main.PlayerId, "Back where you left.");
        logger.LogInformation("[QuestSync] {Player} is back where they left ({X:0}, {Y:0}, {Z:0}).", name, spot.X, spot.Y, spot.Z);
    }
}
