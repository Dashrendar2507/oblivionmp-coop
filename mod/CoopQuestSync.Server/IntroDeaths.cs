using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Events;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Captain Renault dies in the first ambush of the prison intro on the server too. In the game she dies by script: the
/// first time an assassin hits her (CGRenoteScript's onHit blocks), or when assassin 1 or 2 is hit
/// (CGMythicDawnAmbushA1/A2Script: RenoteRef.kill). Under OblivionMP a scripted Kill stays in that player's game: the
/// server still has her alive and brings her back ("dead here but alive on the server, resurrecting it", 2026-11-14),
/// and the intro waits for her death (2026-11-17, players and Nicolas). The assassins also lost their summoned mace
/// with their summoned armor, so they hardly hurt her. So the server kills her itself at those same moments, and once
/// every assassin of that ambush is dead (a world where the intro got stuck). The plugin then moves the intro on
/// (Charactergen 24) in the player's game.
/// </summary>
[Service]
public sealed partial class IntroDeaths(IEntities entities, ILogger logger)
{
    /// <summary>RenoteRef in Oblivion.esm.</summary>
    internal const uint Renault = 0x00032A15;
    /// <summary>CGAssassin01Ref..04Ref: the first ambush. Hitting 1 or 2 kills Renault in the game's own scripts.</summary>
    internal static readonly uint[] Ambush = [0x00014A30, 0x00014A2C, 0x000178C3, 0x000178C4];
    private static readonly HashSet<uint> KillOnHit = [0x00014A30, 0x00014A2C];

    private string? _due;
    private float _elapsed;
    private DateTime _killed;

    [EventHandler(Stage.Validate)]
    public void Check(in HealthChangedEvent e, EventContext ctx)
    {
        if (_due is not null || e.Amount >= 0 || !e.Victim.TryAs(out Npc victim))
            return;
        uint target = victim.RefId;
        if (KillOnHit.Contains(target))
            _due = "assassin 1 or 2 was hit";
        else if (target == Renault && e.Attacker is { } attacker && attacker.TryAs(out Npc from) && Ambush.Contains((uint)from.RefId))
            _due = "an assassin hit her";
    }

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_due is null && _elapsed < 2f)
            return;
        _elapsed = 0;
        Npc? renault = null;
        var assassins = 0;
        var dead = 0;
        foreach (var npc in entities.Query<Npc>())
        {
            uint refId = npc.RefId;
            if (refId == Renault)
                renault = npc;
            else if (Ambush.Contains(refId))
            {
                assassins++;
                if (npc.IsDead)
                    dead++;
            }
        }
        if (_due is null && assassins == Ambush.Length && dead == assassins)
            _due = "every assassin of the ambush is dead";
        if (_due is null)
            return;
        var why = _due;
        _due = null;
        // Not again within a minute if it didn't take (one log line, not one every 2 s).
        if (renault is not { } r || r.IsDead || DateTime.UtcNow - _killed < TimeSpan.FromMinutes(1))
            return;
        _killed = DateTime.UtcNow;
        r.Hp = 0;
        logger.LogInformation("[QuestSync] Prison intro: Captain Renault dies on the server ({Why}), as in the game's own intro.", why);
    }
}
