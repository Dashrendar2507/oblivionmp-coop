using System.Text.Json;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Values;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Tags over other players' heads: "Talking" while they are in a conversation ("Bartering", "Picking a lock"... while
/// they are in one of those menus), "Down" while they wait for a revive.
/// </summary>
[Service]
public sealed partial class PartyTags(IPlayers players, IMarkers markers, CoopKeys keys)
{
    private static readonly MarkerColor Talking = new(0.85f, 0.75f, 0.45f, 1f);
    private static readonly MarkerColor Down = new(0.9f, 0.3f, 0.25f, 1f);

    private sealed record Status(string Id, string Name, bool Talking, bool Downed, string? Activity);

    private Dictionary<string, Status> _status = new();
    private readonly Dictionary<PlayerId, MarkerHandle> _markers = new();
    private float _elapsed;

    public void Update(string json)
    {
        var list = JsonSerializer.Deserialize<List<Status>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        _status = list.ToDictionary(s => s.Id);
        Refresh();
    }

    private void Update()
    {
        // Players walk in and out of this game's area: recheck now and then.
        _elapsed += Time.DeltaTime;
        if (_elapsed < 1f)
            return;
        _elapsed = 0;
        Refresh();
    }

    private void Refresh()
    {
        var seen = new HashSet<PlayerId>();
        var me = players.LocalPlayer?.PlayerId;
        foreach (var main in players.AreaPlayers)
        {
            var id = main.PlayerId;
            if (me is { } mine && id == mine)
                continue;
            if (!_status.TryGetValue(id.ToString(), out var s))
                continue;
            seen.Add(id);
            var text = s.Downed ? $"Down - {keys.Name("revive")} to revive"
                : s.Activity is { Length: > 0 } a ? char.ToUpperInvariant(a[0]) + a[1..]
                : "Talking";
            var color = s.Downed ? Down : Talking;
            if (_markers.TryGetValue(id, out var handle))
                markers.SetMarkerText(handle, text, color);
            else
                _markers[id] = markers.CreateMarker(main, text, color);
        }
        foreach (var (id, handle) in _markers.ToList())
            if (!seen.Contains(id))
            {
                markers.DestroyMarker(handle);
                _markers.Remove(id);
            }
    }
}
