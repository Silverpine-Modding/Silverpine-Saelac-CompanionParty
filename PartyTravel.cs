using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CompanionParty;

// EnterWorldTile fires its change event before the player is actually placed.
// Record the travelling party at departure, then use the final spawn position.
[HarmonyPatch(typeof(DungeonGenerationManager), nameof(DungeonGenerationManager.EnterWorldTile))]
internal static class PartyTravel
{
    private sealed class Crossing
    {
        internal int Teleports;
        internal HashSet<Companion> Members = new();
    }

    internal static int PlayerTeleports;
    private static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    private static void Prefix(DungeonGenerationManager __instance, Vector2Int worldTilePosition, out Crossing __state)
    {
        __state = null;
        var origin = __instance.currentPlayerWorldTilePosition;
        if (!Plugin.Ready || origin == worldTilePosition || !__instance.worldTiles.TryGetValue(origin, out var oldTile) ||
            !__instance.worldTiles.TryGetValue(worldTilePosition, out var next) || oldTile.IsDungeonInstance() || next.IsDungeonInstance()) return;
        var crossing = new Crossing { Teleports = PlayerTeleports };
        var playerPos = Player.Instance.transform.GetVector2IntPosition();
        foreach (var npc in NeuralNPC.neuralNPCs.Values)
        {
            if (!npc || !npc.isActiveAndEnabled || !State.Find(npc, out var p) || !p.Recruited || p.Down || npc.sleeping) continue;
            var pos = npc.transform.GetVector2IntPosition();
            if (__instance.GetPositionWorldTile(pos) == oldTile || (pos - playerPos).sqrMagnitude <= 36)
                crossing.Members.Add(p);
        }
        if (crossing.Members.Count != 0) __state = crossing;
    }

    private static void Postfix(DungeonGenerationManager __instance, Vector2Int worldTilePosition, Crossing __state)
    {
        if (__state == null || __state.Teleports == PlayerTeleports || __instance.currentPlayerWorldTilePosition != worldTilePosition) return;
        var spawn = Player.Instance.transform.GetVector2IntPosition();
        var destination = __instance.GetCurrentPlayerWorldTile();
        if (__instance.GetPositionWorldTile(spawn) != destination) return;
        using var places = ArrivalPlaces(__instance, destination, spawn).GetEnumerator();
        var arrived = PartySkillApi.ObservingActivity ? new List<NeuralNPC>() : null;
        foreach (var npc in NeuralNPC.neuralNPCs.Values.ToArray())
        {
            if (!npc || !npc.isActiveAndEnabled || !State.Find(npc, out var p) || !__state.Members.Remove(p) || !p.Recruited || p.Down) continue;
            p.LeaveCombat();
            var pos = npc.transform.GetVector2IntPosition();
            if ((pos - spawn).sqrMagnitude <= 2 && pos != spawn && __instance.GetPositionWorldTile(pos) == destination)
            {
                arrived?.Add(npc);
                continue;
            }
            if (!npc.TryGetComponent<EntityMover>(out var mover) || !places.MoveNext()) continue;
            var path = npc.GetComponent<PathfindingEntity>();
            var exec = npc.GetComponent<NPCRoutineExecutor>();
            if (exec?.currentRoutine?.id == PartyApi.FollowId) exec.currentRoutine.targetPosition = spawn;
            if (path && !path.HasReachedTargetPosition) path.ClearPathAndDeclareSuccess();
            mover.Teleport(places.Current);
            mover.TurnTowards(spawn);
            if (arrived != null && __instance.GetPositionWorldTile(npc.transform.GetVector2IntPosition()) == destination)
                arrived.Add(npc);
        }
        if (arrived != null) PartySkillApi.Completed(PartyActivity.TravelledTogether, arrived);
    }

    private static IEnumerable<Vector2Int> ArrivalPlaces(DungeonGenerationManager map, WorldTile tile, Vector2Int spawn)
    {
        var visited = new HashSet<Vector2Int> { spawn };
        var frontier = new Queue<Vector2Int>();
        frontier.Enqueue(spawn);
        var zone = MapZone.GetPositionZoneName(spawn);
        while (frontier.Count > 0)
        {
            var pos = frontier.Dequeue();
            foreach (var step in Steps)
            {
                var next = pos + step;
                if (!visited.Add(next) || (next - spawn).sqrMagnitude > 144 || map.GetPositionWorldTile(next) != tile ||
                    TurfCollider.HasImpassableCollider(next) || MapZone.GetPositionZoneName(next) != zone ||
                    Turfs.TryGetTurfComponent<WorldTileChanger>(next, out _) || Turfs.TryGetTurfComponent<Trap>(next, out _) ||
                    Turfs.TryGetTurfComponent<AutoTurnback>(next, out _)) continue;
                frontier.Enqueue(next);
                if (!Turfs.IsOccupiedByCharacter(next) && !Turfs.TryGetTurfComponent<Enemy>(next, out _)) yield return next;
            }
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Teleport))]
internal static class CompletedPlayerTeleport
{
    static void Postfix(Player __instance) { if (__instance == Player.Instance) PartyTravel.PlayerTeleports++; }
}
