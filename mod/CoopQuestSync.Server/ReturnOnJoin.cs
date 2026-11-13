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
/// <item>the spot is outdoors in another world space (a city, the Shivering Isles...): the add-on runs
/// <c>cow &lt;world space&gt; &lt;x&gt; &lt;y&gt;</c>, and once they are in, the server moves them to the exact spot.</item>
/// <item>if that fails: their last spot outdoors, when it is in the area they are in.</item>
/// </list>
///
/// The same travel brings a player to a teammate (the join key on a conversation, see <see cref="Bring"/>).
/// </summary>
[Service]
public sealed partial class ReturnOnJoin(IEntities entities, IServerEvents events, QuestTokenSync sync, GameData data,
    CoopRpc rpc, QuestSyncConfig config, ILogger logger)
{
    /// <summary>How long a player stays in an area before they are moved: the game must have finished loading it.</summary>
    private const float SettleSeconds = 4f;
    /// <summary>
    /// How long a join waits for the plugin's handshake before deciding where the player goes. A brand-new character
    /// spends a minute in the main menu and the race menu first (2026-11-13: plugin seen 56 s after joining), and
    /// only the plugin can say they are new (CQSIntro).
    /// </summary>
    private const float PluginWaitSeconds = 150f;
    /// <summary>How long to wait for the game to take the player into the building. Longer than the add-on may hold a
    /// command back (30 s, while the join's quest catch-up or a menu runs), or the server gave up before the trip.</summary>
    private const float CellTimeoutSeconds = 45f;
    /// <summary>No sign of a positioncell trip after this long: coc instead, for this trip only. Longer than the add-on's
    /// 30 s hold, so the positioncell has run by then: 0.6.6's 15 s sent a coc while it still waited (a second console
    /// move right behind the first), and then used coc for every later trip until the server restarted.</summary>
    private const float DirectTimeoutSeconds = 45f;
    private const float RecordEverySeconds = 2f;
    private const float SaveEverySeconds = 60f;
    /// <summary>Faster than this up or down (cm/s) is falling or jumping, not standing.</summary>
    private const float GroundedSpeed = 80f;
    /// <summary>Closer than this (cm) to the spot already: no need to move.</summary>
    private const float CloseEnough = 300f;
    private static readonly float[] RecheckAfter = [2f, 5f, 10f];
    /// <summary>The travel screen lifts after this long whatever happens.</summary>
    private const float ScreenMaxSeconds = 45f;
    /// <summary>A client too old to say its reload is over: go on after this long anyway.</summary>
    private const float RebornTimeoutSeconds = 25f;
    /// <summary>After the client says its reload is over, the server's view of the player's area may lag a little.</summary>
    private const float RebornAreaSeconds = 1.5f;
    /// <summary>The trip waits this long after the rebirth's reload: a player.positioncell 0.7 s after the reload did
    /// nothing at all (2026-11-13 12:27), while one 13 s after the join's load worked.</summary>
    private const float AfterReloadSeconds = 6f;
    /// <summary>
    /// One game unit in centimetres, and the side of an exterior cell in game units: for the cell <c>cow</c> asks
    /// for. Only roughly right is needed: once the player is in the world space, the server puts them on the spot.
    /// </summary>
    private const float UnitCm = 1.4287f;
    private const float CellUnits = 4096f;
    /// <summary>A teammate is joined on a spot they walked through, this far from them (cm): ground, not a wall.</summary>
    private const float NearMin = 100f, NearMax = 800f;
    private const int TrailLength = 15;

    private sealed class Session
    {
        public string? Area;
        public float Since;
        /// <summary>Recording starts once the player is back (or we gave up): before, they stand on the local save's spot.</summary>
        public bool Resolved;
        public bool Started;
        public ReturnSpot? Going;
        public float GoingSince;
        /// <summary>The trip in was asked as positioncell (straight onto the spot): coc if that does nothing.</summary>
        public bool Direct;
        public Vector3? Target;
        /// <summary>Where the game had put them before the move (the door, for a coc).</summary>
        public Vector3 MovedFrom;
        public float MovedAt;
        public int Checks;
        /// <summary>The travel screen is up on their game (<see cref="CoopRpc.SendTravel"/>).</summary>
        public bool Screen;
        public bool ScreenAsked;
        public string ScreenText = "";
        public float ScreenSince;
        public float LastRecord;
        /// <summary>Travelling to this teammate: on arrival, the spot is next to where they stand then.</summary>
        public PlayerId? Joining;
        /// <summary>Where they stood lately on the ground in this area, newest last.</summary>
        public readonly List<Vector3> Trail = new();
        /// <summary>Brought back to life: OblivionMP reloads their save first, then they go to the party (<see cref="AfterRebirth"/>).</summary>
        public bool Reborn;
        public bool RebornLoaded;
        public float RebornLoadedAt;
        public float RebornSince;
        public float FirstSeen;
        public PlayerId? RebornTo;
        public ReturnSpot? RebornSpot;
    }

    /// <summary>A player who travelled to a teammate has arrived next to them.</summary>
    public event Action<PlayerId>? Arrived;

    private readonly Dictionary<PlayerId, Session> _sessions = new();
    private readonly Dictionary<PlayerId, ReturnSpot> _pinned = new();
    private float _clock;
    private float _elapsed;
    private float _sinceSave;
    private readonly HashSet<string> _unknownAreas = new();

    private void Start()
    {
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        rpc.RebornLoaded += OnRebornLoaded;
    }

    private void Stop()
    {
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        rpc.RebornLoaded -= OnRebornLoaded;
    }

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
                _sessions[id] = s = new Session { FirstSeen = _clock };
            if (s.Area != area)
            {
                s.Area = area;
                s.Since = _clock;
                s.Trail.Clear();
            }
            var name = sync.Nickname(id);
            var settled = _clock - s.Since >= SettleSeconds;

            if (s.Reborn)
            {
                var loaded = s.RebornLoaded && _clock - s.Since >= RebornAreaSeconds && _clock - s.RebornLoadedAt >= AfterReloadSeconds;
                if (main.IsDead || !(loaded || (_clock - s.RebornSince > RebornTimeoutSeconds && settled)))
                    continue;
                s.Reborn = false;
                FinishRebirth(main, name, area, s);
                continue;
            }

            if (!s.Started)
            {
                // A new character with an old nickname (2026-11-13: taken from the race menu to the shop where that
                // nickname last stood): nothing happens until the plugin has said whether they left the sewers.
                if (view.Phase != QuestTokenSync.Phase.Ready && _clock - s.FirstSeen < PluginWaitSeconds)
                    continue;
                // Never left the sewers (a new character, in the prison or choosing a start): the game's or the new
                // start's place, not where this nickname last stood. Decided once the plugin has had time to say so.
                if (view.Intro > 0 && sync.Players.ContainsKey(id))
                {
                    if (!settled)
                        continue;
                    s.Started = true;
                    s.Resolved = true;
                    if (s.Screen)
                        LowerScreen(id, s);
                    logger.LogInformation("[QuestSync] {Player}: hasn't left the sewers, so not taken back to where they last played.", name);
                    continue;
                }
                if (!s.ScreenAsked)
                {
                    s.ScreenAsked = true;
                    if (sync.Settings.ReturnOnJoin && sync.Returns.TryGetValue(name, out var back)
                        && (Fix(back).Area != area || Vector3.Distance(main.Position, back.Position) > CloseEnough))
                        RaiseScreen(id, s, "Taking you back to where you left...");
                }
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
                    if (s.Screen)
                        rpc.SendTravel(id, s.ScreenText); // the load may have dropped the page
                    if (s.Joining is { } joining)
                    {
                        s.Joining = null;
                        var near = NearSpot(joining, main) is { } n && n.Area == area ? n : going;
                        Place(main, name, s, near, $"next to {sync.Nickname(joining)}");
                        Arrived?.Invoke(id);
                    }
                    else
                        Place(main, name, s, going, going.Cell is null ? "back where you left" : "back inside");
                }
                else if (s.Direct && going.Cell is { } door && area != going.Area && _clock - s.GoingSince > DirectTimeoutSeconds)
                {
                    s.Direct = false;
                    s.GoingSince = _clock;
                    logger.LogWarning("[QuestSync] {Player}: player.positioncell didn't take them into {Cell} in {Seconds} s; trying coc.",
                        name, door, DirectTimeoutSeconds);
                    rpc.SendGoToCell(id, door);
                }
                else if (_clock - s.GoingSince > CellTimeoutSeconds)
                {
                    s.Going = null;
                    logger.LogWarning("[QuestSync] {Player}: the game didn't take them to {Place} (is the CoopQuestSyncUI add-on installed? see UE4SS.log).",
                        name, going.Cell ?? going.Area);
                    if (s.Joining is not null)
                    {
                        s.Joining = null;
                        s.Resolved = true;
                        rpc.SendNotice(id, "Couldn't travel there: is the CoopQuestSyncUI add-on installed?");
                    }
                    else if (sync.Outdoors.TryGetValue(name, out var outside) && Fix(outside).Area == area)
                        Place(main, name, s, outside, "outside instead: couldn't take you back inside");
                    else
                        s.Resolved = true;
                }
                continue;
            }

            // The game may put them back where it loaded them (at the door, 2026-11-07): check 2, 5 and 10 seconds
            // after the move.
            if (s.Target is { } t && s.Checks < RecheckAfter.Length && _clock - s.MovedAt > RecheckAfter[s.Checks])
            {
                s.Checks++;
                // Only when they stand where the game had put them, so someone who walked off isn't pulled back.
                if (Vector3.Distance(main.Position, t) > CloseEnough && Vector3.Distance(main.Position, s.MovedFrom) <= CloseEnough)
                {
                    main.Position = t;
                    logger.LogInformation("[QuestSync] {Player} moved back again (the game had put them elsewhere).", name);
                }
            }

            // The screen lifts once they stand where they should, after the first check that the game left them there.
            if (s.Screen && ((s.Resolved && (s.Target is null || s.Checks > 0 || main.IsDead)) || _clock - s.ScreenSince > ScreenMaxSeconds))
                LowerScreen(id, s);

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
            ScreenForMove(main.PlayerId, s);
            s.Direct = config.ArriveInsideDirectly;
            rpc.SendGoToCell(main.PlayerId, s.Direct ? $"{cell} {GameXyz(spot)}" : cell);
            logger.LogInformation("[QuestSync] {Player} {Why} {Cell}: asking their game to go there ({How}).",
                name, s.Joining is null ? "left inside" : "travels into", cell, s.Direct ? "player.positioncell" : "coc");
            return;
        }
        if (WorldPlace(spot) is { } place)
        {
            s.Going = spot;
            s.GoingSince = _clock;
            ScreenForMove(main.PlayerId, s);
            rpc.SendGoToWorld(main.PlayerId, place);
            logger.LogInformation("[QuestSync] {Player} {Why} {Area}: asking their game to go there (cow {Place}).",
                name, s.Joining is null ? "left outdoors in" : "travels to", spot.Area, place);
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

    /// <summary>
    /// The black screen for a move through a load (coc or cow), whatever started it: a join, a trip to a teammate, a
    /// test, a respawn. Without it the player saw the new place for a moment, then the jump onto the spot
    /// (2026-11-12, the Alt+Y test join).
    /// </summary>
    private void ScreenForMove(PlayerId id, Session s)
    {
        var text = s.Joining is { } to ? $"Travelling to {sync.Nickname(to)}..." : s.Screen ? s.ScreenText : "Travelling...";
        RaiseScreen(id, s, text);
    }

    /// <summary>A black screen with this line on the player's game while it moves them (setting "Black screen while travelling").</summary>
    private void RaiseScreen(PlayerId id, Session s, string text)
    {
        if (!sync.Settings.TravelScreen)
            return;
        s.Screen = true;
        s.ScreenText = text;
        s.ScreenSince = _clock;
        rpc.SendTravel(id, text);
    }

    private void LowerScreen(PlayerId id, Session s)
    {
        s.Screen = false;
        rpc.SendTravel(id, "");
    }

    private void Place(MainCharacter main, string name, Session s, ReturnSpot spot, string what)
    {
        s.Resolved = true;
        if (main.IsDead || Vector3.Distance(main.Position, spot.Position) <= CloseEnough)
            return;
        s.MovedFrom = main.Position;
        main.Position = spot.Position;
        main.Rotation = spot.Yaw;
        s.Target = spot.Position;
        s.MovedAt = _clock;
        s.Checks = 0;
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
        s.Trail.Add(p);
        if (s.Trail.Count > TrailLength)
            s.Trail.RemoveAt(0);
        var spot = new ReturnSpot { Area = area, Cell = cell, X = p.X, Y = p.Y, Z = p.Z, Yaw = main.Rotation };
        sync.Returns[name] = spot;
        if (!interior)
            sync.Outdoors[name] = spot;
    }

    /// <summary>
    /// "cow" arguments for an outdoor spot in another world space ("Tamriel 3 24"), or null for an interior.
    /// OblivionMP names an exterior area "L_" + the world space's editor id; the cell is worked out from the position.
    /// </summary>
    /// <summary>
    /// The spot in the game's own units for <c>player.positioncell</c>: "x y z", a little above the floor (the server
    /// puts the player exactly on the spot once they are in).
    /// </summary>
    private static string GameXyz(ReturnSpot spot)
    {
        static int Clamp(float v) => (int)Math.Clamp(MathF.Round(v), -999999f, 999999f);
        return $"{Clamp(spot.X / UnitCm)} {Clamp(-spot.Y / UnitCm)} {Clamp(spot.Z / UnitCm + 20)}";
    }

    private static string? WorldPlace(ReturnSpot spot)
    {
        if (GameData.IsInterior(spot.Area) || !spot.Area.StartsWith("L_", StringComparison.Ordinal))
            return null;
        var world = spot.Area[2..];
        if (world.Length is 0 or > 64 || !world.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return null;
        // The game's Y axis runs the other way from the remaster's.
        var x = (int)MathF.Floor(spot.X / UnitCm / CellUnits);
        var y = (int)MathF.Floor(-spot.Y / UnitCm / CellUnits);
        return $"{world} {Math.Clamp(x, -999, 999)} {Math.Clamp(y, -999, 999)}";
    }

    /// <summary>
    /// A spot next to <paramref name="to"/>: one they walked through a moment ago, a step or a few away, so it is
    /// ground and not inside a wall; else a step beside them.
    /// </summary>
    private ReturnSpot? NearSpot(PlayerId to, MainCharacter mover)
    {
        if (!entities.TryLookup(to, out MainCharacter them) || !sync.Players.TryGetValue(to, out var view) || view.Area is not { } areaId)
            return null;
        var area = GameData.AreaKey(areaId);
        var at = them.Position;
        var pick = at + new Vector3(NearMin, 0, 0);
        if (_sessions.TryGetValue(to, out var ts) && ts.Area == area)
            for (var i = ts.Trail.Count - 1; i >= 0; i--)
            {
                var d = Vector3.Distance(ts.Trail[i], at);
                if (d is >= NearMin and <= NearMax)
                {
                    pick = ts.Trail[i];
                    break;
                }
            }
        return new ReturnSpot
        {
            Area = area, Cell = GameData.IsInterior(area) ? data.InteriorCell(area) : null,
            X = pick.X, Y = pick.Y, Z = pick.Z, Yaw = mover.Rotation,
        };
    }

    /// <summary>
    /// Brings <paramref name="who"/> next to <paramref name="to"/>, wherever they are: a position write in the same
    /// area, else into their building or world space through the add-on first. Returns what to tell the player, or
    /// null when they were moved at once (the move says so itself).
    /// </summary>
    public string? Bring(PlayerId who, PlayerId to) => Bring(who, to, out _);

    /// <inheritdoc cref="Bring(PlayerId, PlayerId)"/>
    /// <param name="started">True when the player was moved or is on their way; false when the trip was refused.</param>
    public string? Bring(PlayerId who, PlayerId to, out bool started)
    {
        started = false;
        if (!entities.TryLookup(who, out MainCharacter main) || !sync.Players.TryGetValue(who, out var view) || view.Area is not { } areaId)
            return "The server doesn't know where you are yet.";
        if (main.IsDead)
            return "You can't travel while you are down.";
        if (sync.HoldsForIntro(view))
            return "Finish the intro first: go out through the sewer gate.";
        var them = sync.Nickname(to);
        if (NearSpot(to, main) is not { } spot)
            return $"The server doesn't know where {them} is yet.";
        var here = GameData.AreaKey(areaId);
        if (spot.Area == here && Vector3.Distance(main.Position, spot.Position) <= NearMax)
            return $"You are already next to {them}.";
        if (GameData.IsInterior(spot.Area) && spot.Cell is null)
            return $"{them} is in a place that isn't in the mod's table of interiors, so you can't travel there.";
        if (spot.Area != here && spot.Cell is null && WorldPlace(spot) is null)
            return $"{them} is somewhere the mod can't send you.";
        var name = sync.Nickname(who);
        if (!_sessions.TryGetValue(who, out var s))
            _sessions[who] = s = new Session { Area = here, Since = _clock };
        s.Started = true;
        s.Joining = to;
        started = true;
        if (spot.Area == here)
        {
            s.Joining = null;
            Place(main, name, s, spot, $"next to {them}");
            Arrived?.Invoke(who);
            return null;
        }
        GoTo(main, name, here, s, spot);
        return $"Travelling to {them}...";
    }

    /// <summary>
    /// The player is being brought back to life (a revive or a respawn). OblivionMP does it with its "rebirth", which
    /// reloads the player's own save, wherever that was (2026-11-12: Alt+G in a shop put the player outside, at the
    /// save's spot). So the travel screen goes up now, and once the reload is over the player travels next to
    /// <paramref name="to"/>, else to <paramref name="pos"/> in <paramref name="area"/> (where they fell, or their last
    /// safe spot), into a building or another world space too.
    /// </summary>
    public void AfterRebirth(PlayerId who, PlayerId? to, AreaId? area, Vector3? pos, string text)
    {
        if (!_sessions.TryGetValue(who, out var s))
            _sessions[who] = s = new Session { Since = _clock };
        s.Started = true;
        s.Going = null;
        s.Joining = null;
        s.Target = null;
        s.Reborn = true;
        s.RebornLoaded = false;
        s.RebornSince = _clock;
        s.RebornTo = to;
        s.RebornSpot = null;
        if (area is { } a && pos is { } p)
        {
            var key = GameData.AreaKey(a);
            s.RebornSpot = new ReturnSpot
            {
                Area = key, Cell = GameData.IsInterior(key) ? data.InteriorCell(key) : null,
                X = p.X, Y = p.Y, Z = p.Z,
                Yaw = entities.TryLookup(who, out MainCharacter m) ? m.Rotation : 0,
            };
        }
        RaiseScreen(who, s, text);
    }

    /// <summary>The client's reload after the rebirth is over (sent by its Resurrection).</summary>
    private void OnRebornLoaded(PlayerId player)
    {
        if (_sessions.TryGetValue(player, out var s) && s.Reborn)
        {
            s.RebornLoaded = true;
            s.RebornLoadedAt = _clock;
        }
    }

    private void FinishRebirth(MainCharacter main, string name, string area, Session s)
    {
        var id = main.PlayerId;
        if (s.Screen)
            RaiseScreen(id, s, s.ScreenText); // the reload dropped the page
        if (s.RebornTo is { } to && entities.TryLookup(to, out MainCharacter _))
        {
            var message = Bring(id, to, out var started);
            if (started)
            {
                if (message is not null)
                    rpc.SendNotice(id, message);
                logger.LogInformation("[QuestSync] {Player}: after the reload, back to {To}.", name, sync.Nickname(to));
                return;
            }
            if (message is not null && message.StartsWith("You are already next to", StringComparison.Ordinal))
            {
                s.Resolved = true;
                return;
            }
            logger.LogInformation("[QuestSync] {Player}: after the reload, can't go to {To} ({Why}).", name, sync.Nickname(to), message);
        }
        if (s.RebornSpot is { } spot)
        {
            logger.LogInformation("[QuestSync] {Player}: after the reload, back to {Place}.", name, spot.Cell ?? spot.Area);
            GoTo(main, name, area, s, spot);
            return;
        }
        s.Resolved = true;
        rpc.SendNotice(id, "You are back where your save put you.");
        logger.LogInformation("[QuestSync] {Player}: after the reload, nowhere to take them; they stay where the save put them.", name);
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
