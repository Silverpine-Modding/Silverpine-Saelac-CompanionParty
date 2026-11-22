using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace CompanionParty;

[Flags]
internal enum BodyCoverage { None = 0, Chest = 1, Waist = 2 }

internal static class Outfit
{
    internal static void Update(Companion wearer)
    {
        BodyCoverage covered = BodyCoverage.None;
        foreach (var entry in wearer.Items)
        {
            if (entry.Slot == 0) continue;
            var native = GearMods.NativeCoverage(entry);
            var effective = entry.Item == null ? entry.SavedCoverage ?? native : GearMods.Coverage(entry.Item, native);
            if (entry.Item != null) entry.SavedCoverage = effective;
            if ((native | effective) != BodyCoverage.None) wearer.ManagesOutfit = true;
            covered |= effective;
        }
        wearer.Covered = covered;
        var npc = wearer.Actor;
        bool nude = covered == BodyCoverage.None;
        // The equipment preview evaluates a separate Companion with the same actor.
        if (!wearer.ManagesOutfit || !npc ||
            !State.Save.People.TryGetValue(State.Key(npc), out var live) || live != wearer) return;
        var appearance = npc.GetComponent<EquipmentKnowledge>();
        appearance?.UpdateMapSprite(npc, nude);
        if (npc.isNude == nude) return;
        npc.isNude = nude;
        if (npc.dialogNudeSprite && NeuralNPC.currentActiveDialogNeuralNPC == npc && DialogBox.Instance && DialogBox.Instance.isOpen)
            appearance?.RefreshPortrait();
    }

    internal static bool Managed(NeuralNPC npc, out Companion wearer) =>
        State.Find(npc, out wearer) && wearer.ManagesOutfit;
}

[HarmonyPatch(typeof(NeuralNPC), nameof(NeuralNPC.GetDialogSprite))]
internal static class OutfitPortrait
{
    static void Prefix(NeuralNPC __instance)
    {
        if (Outfit.Managed(__instance, out var wearer)) __instance.isNude = wearer.Covered == BodyCoverage.None;
    }
}

// The native clothing action flips a story flag. Equipped garments own that state
// once an NPC has a managed outfit, just as the player's equipped items do.
[HarmonyPatch]
internal static class OutfitDialogueAction
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.IsActive));
        yield return AccessTools.Method(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.IsAPIActive));
    }

    static bool Prefix(NPCFunction_ChangeClothingState __instance, ref bool __result)
    {
        if (!Outfit.Managed(__instance.owner, out _)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.Invoke))]
internal static class OutfitDialogueInvoke
{
    static bool Prefix(NPCFunction_ChangeClothingState __instance, ref Task __result)
    {
        if (!Outfit.Managed(__instance.owner, out _)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(NeuralNPC), nameof(NeuralNPC.AskYesNoCotQuestion))]
internal static class OutfitSceneContinuity
{
    static bool Prefix(NeuralNPC __instance, string question, int takes, ref Task<bool> __result)
    {
        if (takes != -1 || question != "Should " + __instance.GetFinalName() + " still be naked in this new scene?" ||
            !Outfit.Managed(__instance, out var wearer)) return true;
        __result = Task.FromResult(wearer.Covered == BodyCoverage.None);
        return false;
    }
}
