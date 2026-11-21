using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace CompanionParty;

// The game performs typed transfers in an async state machine. Replace only the
// recipient credit; the existing parser, insufficient-funds check and payment paths stay in charge.
[HarmonyPatch]
internal static class TypedGifts
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(DialogBox), "SendInput")
        .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    internal static bool Personal(string clause) => Regex.IsMatch(clause, @"\b(as a gift|as a present|for the road|for your (journey|travels|purse)|for yourself|to keep|spending money)\b", RegexOptions.IgnoreCase) ||
        !Regex.IsMatch(clause, @"\b(pay|pays|paying|payment|price|rent|fee|fine|purchase|buy|buys|buying|owes?|debt|in exchange|in return)\b", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(clause, @"\bfor (the|a|an|this|that|my|your)\b", RegexOptions.IgnoreCase);
    internal static void Credit(Inventory till, int updated, string clause)
    {
        var npc = NeuralNPC.currentActiveDialogNeuralNPC;
        if (!npc || !Personal(clause)) { till.gold = updated; return; }
        int amount = unchecked(updated - till.gold);
        var p = State.Get(npc);
        if (amount > 0 && p.Gold <= int.MaxValue - amount) p.Gold += amount;
        else if (amount > 0) Player.Instance.worldInventory.inventory.gold += amount;
    }
    // Salt Extra Debug wraps native gold assignments. Leave the player's debit
    // available to that mod after routing the NPC's credit.
    [HarmonyBefore("salt.silverpine.extradebug")]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();
        var gold = AccessTools.Field(typeof(Inventory), "gold");
        int marker = il.FindIndex(c => c.opcode == OpCodes.Ldstr && ((string)c.operand).Contains("gold coins|gold coin"));
        var actor = AccessTools.Field(typeof(NeuralNPC), nameof(NeuralNPC.currentActiveDialogNeuralNPC));
        int store = -1;
        for (int i = Math.Max(8, marker + 1); marker > 0 && i < il.Count; i++)
        {
            if (il[i - 7].LoadsField(actor) && il[i - 4].opcode == OpCodes.Dup &&
                il[i - 3].LoadsField(gold) && il[i - 1].opcode == OpCodes.Add && il[i].StoresField(gold))
            { store = i; break; }
        }
        string clauseLoad = marker > 0 ? il[marker - 1].opcode.Name : "";
        if (store < 0 || !(clauseLoad == "ldloc" || clauseLoad.StartsWith("ldloc.", StringComparison.Ordinal)))
        {
            Plugin.Log.LogWarning("Typed gold transfers use the game's handler because the NPC credit could not be located. Gift gold still uses the personal purse.");
            return il;
        }
        var clause = il[marker - 1];
        il[store].opcode = OpCodes.Call;
        il[store].operand = AccessTools.Method(typeof(TypedGifts), nameof(Credit));
        var loadClause = new CodeInstruction(clause.opcode, clause.operand).MoveLabelsFrom(il[store]);
        il.Insert(store, loadClause);
        return il;
    }
}

// Native NPC gifts are already authorized by the conversation. Cap coin piles
// before OnAddedToInventory converts them, then suppress the native shop debit.
[HarmonyPatch]
internal static class ApprovedGifts
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(NPCFunction_GiveItem), "Invoke")
        .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    internal static bool Deliver(Inventory destination, Item item, NPCFunction function)
    {
        var person = State.Get(function.owner);
        var coins = item.GetItemComponent<ItemComponent_Gold>();
        if (coins != null)
        {
            int amount = Math.Min(person.Gold, Math.Max(0, coins.GetAmount()));
            amount = Math.Min(amount, int.MaxValue - destination.gold);
            if (amount == 0 || !destination.CanAddItem(item)) return false;
            coins.SetAmount(amount);
            if (!destination.TryAddItem(item)) return false;
            person.Gold -= amount;
            return true;
        }
        var owned = person.Items.FirstOrDefault(e => e.Item != null && string.Equals(e.Name, item.name, StringComparison.OrdinalIgnoreCase));
        if (owned == null) return destination.TryAddItem(item);
        if (!owned.Item.TryMoveItemToInventory(destination)) return false;
        person.Items.Remove(owned);
        person.Clamp();
        return true;
    }
    static void IgnoreShopDebit(Inventory inventory, int value) { }
    static void RemoveDonatedItem(Item item, NPCFunction function)
    {
        // Silverpine may resolve the same item name in the merchant's shop first.
        // Keep that copy in stock when Deliver will release a personal item.
        if (State.Get(function.owner).Items.Any(e => e.Item != null && string.Equals(e.Name, item.name, StringComparison.OrdinalIgnoreCase))) return;
        item.RemoveItemFromInventory();
    }
    static void StripVendorTag(Item item, NPCFunction function)
    {
        if (State.Get(function.owner).Items.Any(e => e.Item != null && string.Equals(e.Name, item.name, StringComparison.OrdinalIgnoreCase))) return;
        item.RemoveItemComponentsOfType<ItemComponent_VendorItem>();
    }
    [HarmonyBefore("salt.silverpine.extradebug")]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var self = __originalMethod.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(NPCFunction_GiveItem));
        var add = AccessTools.Method(typeof(Inventory), nameof(Inventory.TryAddItem));
        var remove = AccessTools.Method(typeof(Item), nameof(Item.RemoveItemFromInventory));
        var gold = AccessTools.Field(typeof(Inventory), "gold");
        foreach (var c in instructions)
        {
            bool untag = c.operand is MethodInfo m && m.DeclaringType == typeof(Item) && m.Name == nameof(Item.RemoveItemComponentsOfType) &&
                m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(ItemComponent_VendorItem);
            if (c.Calls(add) || c.Calls(remove) || untag)
            {
                string method = c.Calls(add) ? nameof(Deliver) : untag ? nameof(StripVendorTag) : nameof(RemoveDonatedItem);
                var loadSelf = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c);
                yield return loadSelf;
                yield return new CodeInstruction(OpCodes.Ldfld, self);
                c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ApprovedGifts), method);
            }
            else if (c.opcode == OpCodes.Stfld && Equals(c.operand, gold))
            { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ApprovedGifts), nameof(IgnoreShopDebit)); }
            yield return c;
        }
    }
}

[HarmonyPatch(typeof(NPCFunction_IncreaseRelationship), nameof(NPCFunction_IncreaseRelationship.Invoke))]
internal static class CompanionRapport
{
    static void Prefix(NPCFunction_IncreaseRelationship __instance, out int __state) => __state = __instance.owner.relationshipLevel;
    static void Postfix(NPCFunction_IncreaseRelationship __instance, int __state)
    {
        var npc = __instance.owner;
        if (npc.relationshipLevel <= __state || !State.Find(npc, out var p) || UnityEngine.Random.value >= Stats.Read(p).Social) return;
        int cap = npc.GetRelationshipLevelCap();
        if (npc.relationshipLevel < cap) { npc.relationshipLevel++; RelationshipBar.Instance.SetValue(npc.relationshipLevel, cap); }
    }
}
