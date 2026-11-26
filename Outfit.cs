using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;

namespace CompanionParty;

[Flags]
internal enum BodyCoverage { None = 0, Chest = 1, Waist = 2 }

internal static class Outfit
{
    // Silverpine leaves ordinary jewelry and racial fur equipped when the player undresses.
    private static bool Stowable(PackEntry entry)
    {
        if (entry.Slot == 0 || entry.Slot == (int)EquipmentSlotType.Fur || !entry.Visible) return false;
        if (entry.Slot != (int)EquipmentSlotType.Neck && entry.Slot != (int)EquipmentSlotType.Ring) return true;
        return (entry.SavedCoverage.GetValueOrDefault() | GearMods.NativeCoverage(entry)) != BodyCoverage.None;
    }

    internal static bool HasSetAside(Companion wearer) => wearer.Items.Any(e => e.RedressSlot != 0);
    internal static bool CanChange(Companion wearer) => HasSetAside(wearer) || wearer.Items.Any(Stowable);

    internal static void Change(Companion wearer, bool redress)
    {
        if (redress != HasSetAside(wearer)) return;
        if (MoveEquipment(wearer, redress)) wearer.Clamp();
    }

    internal static void ApplyNativeState(Companion wearer, bool nude)
    {
        SetAppearance(wearer.Actor, nude);
        if (MoveEquipment(wearer, !nude)) wearer.Clamp(preserveNativeAppearance: true);
    }

    private static bool MoveEquipment(Companion wearer, bool redress)
    {
        bool changed = false;
        foreach (var entry in wearer.Items)
        {
            if (redress)
            {
                int slot = entry.RedressSlot;
                if (slot == 0) continue;
                entry.RedressSlot = 0;
                // A dormant item can reclaim its saved slot until its provider returns.
                if (entry.Slot == 0 && (entry.Item == null || (int)entry.Item.GetEquipmentSlotType() == slot) &&
                    !wearer.Items.Any(other => other.Slot != 0 && Companion.Conflicts(entry, slot, other, other.Slot)))
                    entry.Slot = slot;
            }
            else
            {
                if (!Stowable(entry)) continue;
                entry.RedressSlot = entry.Slot;
                entry.Slot = 0;
            }
            changed = true;
        }
        return changed;
    }

    internal static void Update(Companion wearer, bool preserveNativeAppearance)
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
        bool coverageChanged = wearer.Covered != covered;
        wearer.Covered = covered;
        var npc = wearer.Actor;
        // The equipment preview evaluates a separate Companion with the same actor.
        if (!wearer.ManagesOutfit || !npc ||
            !State.Save.People.TryGetValue(State.Key(npc), out var live) || live != wearer) return;
        // A resident may have dressed in the scene even if their remembered items are gone.
        // Only an actual change in coverage should replace that decision between scenes.
        bool useEquipment = wearer.Recruited || (coverageChanged && !preserveNativeAppearance);
        SetAppearance(npc, useEquipment ? covered == BodyCoverage.None : npc.isNude);
    }

    private static void SetAppearance(NeuralNPC npc, bool nude)
    {
        var appearance = npc.GetComponent<EquipmentKnowledge>();
        appearance?.UpdateMapSprite(npc, nude);
        if (npc.isNude == nude) return;
        npc.isNude = nude;
        if (npc.dialogNudeSprite && NeuralNPC.currentActiveDialogNeuralNPC == npc && DialogBox.Instance && DialogBox.Instance.isOpen)
            appearance?.RefreshPortrait();
    }

    internal static bool Managed(NeuralNPC npc, out Companion wearer) =>
        State.Find(npc, out wearer) && wearer.ManagesOutfit;

    internal static bool InParty(NeuralNPC npc, out Companion wearer) => Managed(npc, out wearer) && wearer.Recruited;
}

[HarmonyPatch(typeof(NeuralNPC), nameof(NeuralNPC.GetDialogSprite))]
internal static class OutfitPortrait
{
    static void Prefix(NeuralNPC __instance)
    {
        if (Outfit.InParty(__instance, out var wearer)) __instance.isNude = wearer.Covered == BodyCoverage.None;
    }
}

// Silverpine keeps the previous action and compares its Name on the next turn.
// Capture the direction when offered so an undress never changes into a redress in that history.
internal sealed class OutfitChangeAction : NPCFunction_ChangeClothingState
{
    private readonly bool redress;
    private readonly Companion wearer;
    internal OutfitChangeAction(Companion wearer)
    {
        owner = wearer.Actor;
        this.wearer = wearer;
        redress = Outfit.HasSetAside(wearer);
    }
    public override string Name => "In this excerpt, did " + owner.GetFinalName() +
        (redress ? " put on clothes?" : " get undressed (nude)?");
    public override string APIName => redress ? "change_clothing_state_to_clothed" : "change_clothing_state_to_nude";
    public override Task Invoke()
    {
        if (Outfit.InParty(owner, out var current) && current == wearer) Outfit.Change(wearer, redress);
        return Task.CompletedTask;
    }
}

[HarmonyPatch(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.IsActive))]
internal static class OutfitDialogueAction
{
    private static readonly string[] dressingWords = { "redress", "redresses", "redressed", "redressing", "dresses", "dressed",
        "dressing", "don", "dons", "donning", "wear", "wears", "wearing", "clothed" };
    static bool Prefix(NPCFunction_ChangeClothingState __instance, string result, ref bool __result)
    {
        if (!Outfit.InParty(__instance.owner, out var wearer)) return true;
        __result = Outfit.CanChange(wearer) && !string.IsNullOrEmpty(result) &&
            (result.ContainsAnyWordIgnoreCase(NPCFunction_ChangeClothingState.undressingWords) ||
             (Outfit.HasSetAside(wearer) && (result.ContainsAnyWordIgnoreCase(dressingWords) ||
                 result.IndexOf("back on", StringComparison.OrdinalIgnoreCase) >= 0)));
        return false;
    }
}

[HarmonyPatch(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.IsAPIActive))]
internal static class OutfitDialogueTool
{
    static bool Prefix(NPCFunction_ChangeClothingState __instance, ref bool __result)
    {
        if (!Outfit.InParty(__instance.owner, out var wearer)) return true;
        __result = Outfit.CanChange(wearer);
        return false;
    }
}

[HarmonyPatch(typeof(NeuralNPC), "GetActiveNPCFunctions")]
internal static class OutfitDialogueCandidates
{
    static void Postfix(NeuralNPC __instance, List<NPCFunction> __result)
    {
        int index = __result.FindIndex(f => f.GetType() == typeof(NPCFunction_ChangeClothingState));
        if (index >= 0 && Outfit.InParty(__instance, out var wearer))
            __result[index] = new OutfitChangeAction(wearer);
    }
}

[HarmonyPatch(typeof(NPCFunction_ChangeClothingState), nameof(NPCFunction_ChangeClothingState.Invoke))]
internal static class OutfitDialogueInvoke
{
    static bool Prefix(NPCFunction_ChangeClothingState __instance, ref Task __result, out Companion __state)
    {
        __state = null;
        if (!Outfit.Managed(__instance.owner, out var wearer)) return true;
        if (!wearer.Recruited)
        {
            __state = wearer;
            return true;
        }
        Outfit.Change(wearer, Outfit.HasSetAside(wearer));
        __result = Task.CompletedTask;
        return false;
    }

    static void Postfix(NPCFunction_ChangeClothingState __instance, Companion __state)
    {
        // The native action sets isNude before its first await (the portrait crossfade).
        if (__state != null) Outfit.ApplyNativeState(__state, __instance.owner.isNude);
    }
}

[HarmonyPatch(typeof(NeuralNPC), nameof(NeuralNPC.AskYesNoCotQuestion))]
internal static class OutfitSceneContinuity
{
    static bool Prefix(NeuralNPC __instance, string question, int takes, ref Task<bool> __result, out Companion __state)
    {
        __state = null;
        if (takes != -1 || question != "Should " + __instance.GetFinalName() + " still be naked in this new scene?" ||
            !Outfit.Managed(__instance, out var wearer)) return true;
        if (!wearer.Recruited)
        {
            __state = wearer;
            return true;
        }
        __result = Task.FromResult(wearer.Covered == BodyCoverage.None);
        return false;
    }

    static void Postfix(NeuralNPC __instance, Companion __state, ref Task<bool> __result)
    {
        if (__state != null) __result = ApplyAnswer(__instance, __state, __result);
    }

    private static async Task<bool> ApplyAnswer(NeuralNPC npc, Companion wearer, Task<bool> answer)
    {
        bool nude = await answer;
        if (!npc || wearer.Actor != npc || !State.Save.People.TryGetValue(State.Key(npc), out var current) || current != wearer)
            return npc ? npc.isNude : nude;
        if (wearer.Recruited) return wearer.Covered == BodyCoverage.None;
        Outfit.ApplyNativeState(wearer, nude);
        return nude;
    }
}
