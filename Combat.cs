using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CompanionParty;

public static class PartyApi
{
    internal const string FollowId = Plugin.Id + ".follow";
    public static bool IsRecruited(NeuralNPC npc) => State.Find(npc, out var p) && p.Recruited;
    public static bool Recruit(NeuralNPC npc)
    {
        if (!npc || !npc.isActiveAndEnabled || ExpeditionApi.Assigned(npc)) return false;
        var exec = npc.GetComponent<NPCRoutineExecutor>();
        if (exec?.currentRoutine == null) return false;
        var person = State.Get(npc);
        EnsureRoutine(person);
        if (!exec.GetCurrentAndPreOverrideRoutines().Any(r => r?.id == FollowId)) return false;
        person.Recruited = person.WishEligible = true;
        person.RefreshSkills();
        return true;
    }
    internal static void EnsureRoutine(Companion person)
    {
        var exec = person.Actor.GetComponent<NPCRoutineExecutor>();
        if (exec?.currentRoutine == null || exec.GetCurrentAndPreOverrideRoutines().Any(r => r?.id == FollowId)) return;
        string activity = "following " + Player.Instance.playerName;
        exec.StartOverrideRoutine(new NPCRoutine(activity, activity, activity, Player.Instance.transform.GetVector2IntPosition(),
            new RoutineArgument_Following())
        { id = FollowId });
    }
    public static void Dismiss(NeuralNPC npc)
    {
        if (!State.Find(npc, out var p)) return;
        p.Recruited = false;
        p.RefreshSkills();
        p.LeaveCombat();
        var exec = npc.GetComponent<NPCRoutineExecutor>();
        foreach (var routine in exec.GetCurrentAndPreOverrideRoutines().Where(r => r?.id == FollowId).ToArray())
        {
            if (routine == exec.currentRoutine && exec.preOverrideRoutines.Count == 0)
            { exec.currentRoutine = null; exec.CheckRoutine(WorldInfoManager.Instance.GetCurrentHourOfDay(), false); }
            else exec.StopOverrideRoutine(routine);
        }
    }
}

internal static class Combat
{
    private static Enemy playerTarget;
    internal static void Reset() => playerTarget = null;
    internal static int Turn => WorldInfoManager.Instance.TotalTurnCount;
    internal static void Focus(Damagable target) => playerTarget = target ? target.GetComponent<Enemy>() : null;
    internal static bool Hostile(Enemy e) => e && e.isActiveAndEnabled && !Fields.Read<bool>(e, "passive") &&
        e.TryGetComponent<Damagable>(out var d) && d.HP > 0;
    internal static bool Threat(Companion p, Enemy e) => Hostile(e) && State.Near(p.Actor, e.transform, 6) &&
        (Vector2.Distance(e.transform.position, Player.Instance.transform.position) <= 7 ||
         Vector2.Distance(p.Actor.transform.position, e.transform.position) <= 2);
    internal static Enemy Target(Companion p)
    {
        if (!p.Recruited && ExpeditionApi.InField(p)) return Enemy.allEnemies.Where(e => Hostile(e) && State.Near(p.Actor, e.transform, 6)).OrderBy(e => (e.transform.position - p.Actor.transform.position).sqrMagnitude).FirstOrDefault();
        if (p.Order == "Guard" && CanRetaliate(p)) return p.Attacker;
        if (playerTarget && Threat(p, playerTarget)) return playerTarget;
        if (p.Order == "Assist") return null;
        return Enemy.allEnemies.Where(e => Threat(p, e)).OrderBy(e =>
            (e.transform.position - Player.Instance.transform.position).sqrMagnitude).FirstOrDefault();
    }
    internal static void RememberAttack(Companion p, Enemy source)
    {
        if (!ExpeditionApi.Combatant(p) || p.Down || !Hostile(source)) return;
        p.Attacker = source;
        p.AttackedTurn = Turn;
    }
    private static bool CanRetaliate(Companion p)
    {
        if (!p.Attacker) return false;
        if (!Hostile(p.Attacker) || !Recent(p.AttackedTurn))
        { p.Attacker = null; return false; }
        return InSight(p, p.Attacker);
    }
    internal static bool Recent(int when, int turns = 6) =>
        WorldInfoManager.Instance && when >= 0 && when <= Turn && Turn - when <= turns;
    internal static bool InSight(Companion p, Enemy enemy)
    {
        var from = p.Actor.transform.GetVector2IntPosition();
        var to = enemy.transform.GetVector2IntPosition();
        // An attack through an open doorway can originate across a named zone boundary.
        return (from - to).sqrMagnitude <= 36 &&
            (from.IsAdjacent(to) || TurfCollider.CanPositionsSeeEachOtherAdvanced(from, to));
    }
    internal static bool Act(Companion p, Stats stats)
    {
        if (p.Down || (p.Actor.sleeping && !(p.Order == "Guard" && CanRetaliate(p))))
        { p.EngagedEnemy = null; return true; }
        var enemy = Target(p);
        p.EngagedEnemy = enemy;
        if (!enemy) return false;
        p.CombatTurn = Turn;
        if (p.HP <= stats.MaxHP * .45f && Supplies.Heal(p, stats)) return true;
        var target = enemy.GetComponent<Damagable>();
        var weapon = p.Wears(EquipmentSlotType.Weapon)?.Item;
        var from = p.Actor.transform.GetVector2IntPosition();
        var to = enemy.transform.GetVector2IntPosition();
        // Silverpine's ordinary bow is used from the pack and has no equipment slot.
        var bow = weapon;
        if (!p.Pinned && !from.IsAdjacent(to) && !Supplies.Has<ItemComponent_RangedWeapon>(bow))
            bow = p.Items.Select(e => e.Item).FirstOrDefault(i => Supplies.Has<ItemComponent_RangedWeapon>(i) &&
                i.GetEquipmentSlotType() == EquipmentSlotType.NotEquipable);
        var ranged = bow?.GetItemComponent<ItemComponent_RangedWeapon>();
        var melee = weapon?.GetItemComponent<ItemComponent_MeleeWeapon>();
        var ammo = ranged == null ? null : p.Items.FirstOrDefault(e => e.Item != null &&
            e.Item.name == Fields.Read<string>(ranged, "ammunitionItemName") &&
            e.Item.GetItemComponent<ItemComponent_Charges>()?.HasCharge() == true);
        bool shoot = ranged != null && ammo != null && p.Energy >= 20 && (from - to).sqrMagnitude < 36;
        if (!shoot && !from.IsAdjacent(to))
        {
            SetGoal(p.Actor, to);
            return false;
        }
        var tool = shoot ? (object)ranged : melee;
        bool claws = tool == null && stats.Claws && p.Energy >= 33;
        int cost = shoot ? 20 : melee != null ? Fields.Read(melee, "energyCost", 10) : claws ? 33 : 5;
        float scale = tool is IQualityItemComponent q ? 1 + (int)q.Quality * .5f : 1;
        int lo = Mathf.RoundToInt(Fields.Read(tool, "minDamage", claws ? 10 : 0) * scale);
        int hi = Mathf.RoundToInt(Fields.Read(tool, "maxDamage", claws ? 19 : 5) * scale);
        long damage = (long)Random.Range(lo, Math.Max(lo, hi) + 1) + stats.AttackDamage;
        if (p.Energy < cost) damage /= 2;
        bool critical = !shoot && Random.value < stats.Crit + Fields.Read<float>(tool, "critChance");
        if (shoot) ammo.Item.GetItemComponent<ItemComponent_Charges>().TakeCharge();
        p.Energy = Math.Max(0, p.Energy - cost);
        p.Actor.GetComponent<EntityMover>().TurnTowards(to);
        int amount = (int)Math.Min(int.MaxValue, critical ? damage * 2 : damage);
        target.TakeDamage(p.Actor.gameObject, new Damage(amount, DamageType.Normal), critical);
        if (tool != null) Wear(p, shoot ? bow : weapon, stats);
        return true;
    }
    internal static void SetGoal(NeuralNPC npc, Vector2Int to)
    {
        var path = npc.GetComponent<PathfindingEntity>();
        if (path != null && (path.HasReachedTargetPosition || path.GetTargetPosition() != to))
            path.SetTargetPosition(to);
    }
    private static readonly System.Reflection.FieldInfo Remaining = AccessTools.Field(typeof(ItemComponent_Durability), "durability");
    internal static void Wear(Companion p, Item i, Stats stats, float loss = 1)
    {
        var durability = i?.GetItemComponent<ItemComponent_Durability>();
        if (durability == null || durability.Durability <= 0 || loss <= 0 || Random.value < stats.Preserve) return;
        if (i.GetItemComponent<ItemComponent_Enchantment>()?.enchantmentType == ItemComponent_Enchantment.EnchantmentType.Unbreaking &&
            Random.value < .5f) return;
        // The optional player's durability patch also runs on other owners' native setters.
        // Apply the same break transition with this wearer's preservation chance.
        Remaining.SetValue(durability, Math.Max(0, durability.Durability - loss));
        if (durability.Durability > 0) return;
        foreach (var handler in i.GetItemComponents<IDurabilityItemComponent>()) handler.OnBreak();
        var entry = p.Items.Find(e => e.Item == i);
        if (entry != null)
        {
            if (!p.Pack.ContainsItem(i)) p.Items.Remove(entry);
            else entry.Slot = 0;
            p.Clamp();
        }
    }
    internal static void Hit(Companion p, Enemy source, Ability ability)
    {
        if (p.Down || !Hostile(source)) return;
        RememberAttack(p, source);
        var stats = Stats.Read(p);
        bool melee = ability is Ability_BasicAttack;
        var weapon = p.Wears(EquipmentSlotType.Weapon)?.Item.GetItemComponent<ItemComponent_MeleeWeapon>();
        if (melee && Random.value < stats.Parry + Fields.Read<float>(weapon, "parryChance"))
        { AnimationHelper.Instance.SpawnFloatingText("Parried", p.Actor.transform); return; }
        int raw = Mathf.RoundToInt(Random.Range(ability.minPower, ability.maxPower + 1) * source.GetAbilityPowerModifier());
        var type = Fields.Read(ability, "damageType", DamageType.Normal);
        if (ability is Ability_HarmSanity)
        {
            if (stats.Consumption == 0) { p.Sanity = Math.Max(0, p.Sanity - raw); return; }
            type = DamageType.True;
        }
        int hit = ApplyDamage(p, raw, type);
        if (hit > 0 && Random.value < stats.Reflect)
            source.GetComponent<Damagable>().TakeDamage(p.Actor.gameObject, new Damage(raw, type));
    }
    internal static int ApplyDamage(Companion p, int raw, DamageType type)
    {
        if (p.Down) return 0;
        var stats = Stats.Read(p);
        int hit = Mathf.CeilToInt(raw * Mathf.Pow(.5f, stats.Armor[(int)type] / 10f));
        p.HP = Mathf.Clamp(p.HP - hit, 0, stats.MaxHP);
        bool knockedDown = p.HP == 0;
        if (knockedDown) { p.DownSince = Turn; p.HealingTurns = 0; p.LeaveCombat(); }
        if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingText(hit.ToString(), p.Actor.transform);
        if (hit > 0)
            foreach (var armor in p.Worn.Where(e => e.Item.GetItemComponent<ItemComponent_Clothing>() != null).ToArray()) Wear(p, armor.Item, stats, hit);
        if (knockedDown)
        {
            p.Actor.GetComponent<PathfindingEntity>()?.ClearPathAndDeclareSuccess();
            if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingText("Downed", p.Actor.transform);
        }
        return hit;
    }
    internal static bool EnemyActs(Enemy enemy)
    {
        if (!Plugin.Ready || !Hostile(enemy)) return false;
        if (!State.Save.People.Values.Any(p => !p.Down && p.Actor && ExpeditionApi.Combatant(p))) return false;
        var p = State.Save.People.Values.Where(p => !p.Down && p.Actor && ExpeditionApi.Combatant(p) &&
            State.Near(p.Actor, enemy.transform, 6)).OrderBy(p => (p.Actor.transform.position - enemy.transform.position).sqrMagnitude).FirstOrDefault();
        if (p == null) return false;
        float distance = Vector2.Distance(p.Actor.transform.position, enemy.transform.position);
        if (!Player.Instance.IsInvisible() && TurfCollider.CanSeePlayer(enemy.transform) &&
            distance > Vector2.Distance(Player.Instance.transform.position, enemy.transform.position)) return false;
        var abilities = Fields.Read<List<Ability>>(enemy, "abilities");
        if (abilities == null || !abilities.Any(DirectAttack)) return false;
        float playerDistance = Vector2.Distance(enemy.transform.position, Player.Instance.transform.position);
        if (!Player.Instance.IsInvisible() && TurfCollider.CanSeePlayer(enemy.transform) &&
            abilities.Any(a => !DirectAttack(a) && a.IsOffCooldown() && playerDistance < a.range)) return false;
        var path = enemy.GetComponent<PathfindingEntity>();
        foreach (var a in abilities) a.ProcessTurn();
        var attack = abilities.FirstOrDefault(a => DirectAttack(a) &&
            a.IsOffCooldown() && distance < a.range);
        enemy.GetComponent<EntityMover>().StopRandomWalking();
        if (attack == null)
        {
            var pos = p.Actor.transform.GetVector2IntPosition();
            if (path.GetTargetPosition() != pos || path.HasReachedTargetPosition) path.SetTargetPosition(pos);
        }
        else
        {
            path.ClearPathAndDeclareSuccess();
            enemy.GetComponent<EntityMover>().TurnTowards(p.Actor.transform.GetVector2IntPosition());
            attack.PutOnCooldown();
            ActionQueue.Instance.Enqueue(new QueueAction(() => { if (p.Actor && !p.Down && ExpeditionApi.Combatant(p) && Hostile(enemy)) Hit(p, enemy, attack); },
                ActionQueuePriority.Attack, enemy.gameObject));
        }
        return true;
    }
    private static bool DirectAttack(Ability a) => a is Ability_BasicAttack || a is Ability_Projectile || a is Ability_HarmSanity;
}

// Some custom characters also carry native damage or death handlers. Route enemy
// and environmental damage through the same essential health used by party frames.
[HarmonyPatch(typeof(Damagable), "AnimateDamage")]
internal static class CompanionDamage
{
    static bool Prefix(Damagable __instance, Damage __0, ref System.Collections.IEnumerator __result)
    {
        if (!__instance.TryGetComponent<NeuralNPC>(out var n) || !State.Find(n, out var p) || !ExpeditionApi.Combatant(p)) return true;
        Combat.ApplyDamage(p, __0.amount, __0.type);
        __result = Array.Empty<object>().GetEnumerator();
        return false;
    }
}
[HarmonyPatch(typeof(Damagable), nameof(Damagable.Kill))]
internal static class EssentialDeath
{
    static bool Prefix(Damagable __instance, ref System.Collections.IEnumerator __result)
    {
        if (!__instance.TryGetComponent<NeuralNPC>(out var n) || !State.Find(n, out var p) || !ExpeditionApi.Combatant(p)) return true;
        p.HP = 0;
        p.DownSince = Combat.Turn;
        p.HealingTurns = 0;
        p.LeaveCombat();
        __result = Array.Empty<object>().GetEnumerator();
        return false;
    }
}
[HarmonyPatch(typeof(NPCRoutineExecutor), "OnPostProcessTurn")]
internal static class DownedRoutine
{
    static bool Prefix(NPCRoutineExecutor __instance) => !State.Find(__instance.neuralNPC, out var p) || !p.Down;
}

[HarmonyPatch(typeof(Player), nameof(Player.Attack))]
internal static class PlayerFocusPatch { static void Prefix(Damagable __0) => Combat.Focus(__0); }

[HarmonyPatch(typeof(Damagable), nameof(Damagable.TakeDamage))]
internal static class AttackSource
{
    static void Prefix(Damagable __instance, GameObject __0, Damage __1)
    {
        if (!__0 || __1 == null || __1.amount <= 0) return;
        if (Player.Instance && __0 == Player.Instance.gameObject && __instance.GetComponent<Enemy>())
            Combat.Focus(__instance);
        if (__instance.TryGetComponent<NeuralNPC>(out var npc) && State.Find(npc, out var p) &&
            __0.TryGetComponent<Enemy>(out var source)) Combat.RememberAttack(p, source);
    }
}

[HarmonyPatch(typeof(Enemy), "OnPreProcessTurn")]
internal static class DefendPatch
{
    static bool Prefix(Enemy __instance, ref bool ___inCombat)
    {
        if (!Combat.EnemyActs(__instance)) return true;
        ___inCombat = true;
        return false;
    }
}

[HarmonyPatch(typeof(RoutineArgument_Following), nameof(RoutineArgument_Following.OnBeforeDialogBegins))]
internal static class KeepPartyOnTalk { static bool Prefix(RoutineArgument_Following __instance) => __instance.routine.id != PartyApi.FollowId; }

[HarmonyPatch(typeof(RoutineArgument_Following), nameof(RoutineArgument_Following.OnPostProcessTurn))]
internal static class FollowPath
{
    static bool Prefix(RoutineArgument_Following __instance)
    {
        var r = __instance.routine;
        if (r.id != PartyApi.FollowId) return true;
        var npc = r.executor.neuralNPC;
        if (!Plugin.Ready || !State.Find(npc, out var p) || p.Down) return false;
        var enemy = Combat.Target(p);
        var target = enemy ? enemy.transform : Player.Instance.transform;
        r.targetPosition = target.GetVector2IntPosition();
        if (!npc.transform.GetVector2IntPosition().IsAdjacent(target.GetVector2IntPosition()))
            Combat.SetGoal(npc, r.targetPosition);
        else if (!r.executor.pathfindingEntity.HasReachedTargetPosition)
            r.executor.pathfindingEntity.ClearPathAndDeclareSuccess();
        return false;
    }
}

[HarmonyPatch(typeof(PathfindingEntity), "OnPreProcessTurn")]
internal static class FollowerStep
{
    static bool Prefix(PathfindingEntity __instance, out float __state)
    {
        __state = __instance.speedBonus;
        if (!Plugin.Ready || !__instance.TryGetComponent<NeuralNPC>(out var npc) ||
            !State.Find(npc, out var p) || !ExpeditionApi.Combatant(p)) return true;
        var stats = Stats.Read(p);
        if (p.Down || p.DidAttackTurn == Combat.Turn) return false;
        if (Combat.Act(p, stats)) { p.DidAttackTurn = Combat.Turn; return false; }
        __instance.speedBonus = Mathf.Clamp01(__state + stats.Speed);
        return !stats.Slow || Random.value >= .05f;
    }
    static void Finalizer(PathfindingEntity __instance, float __state) => __instance.speedBonus = __state;
}
