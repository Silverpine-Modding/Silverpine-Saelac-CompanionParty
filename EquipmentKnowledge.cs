using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CompanionParty;

internal sealed class EquipmentKnowledge : MonoBehaviour, INPCVisibleObjectAddition
{
    private bool refreshPending;
    private EntityMover mapMover;
    private Sprite originalMapSprite, alternateMapSprite;
    private bool directional;

    internal void UpdateMapSprite(NeuralNPC npc, bool nude)
    {
        Sprite sprite = null;
        if (nude && CustomContentDefinition_NPC.loaded.TryGetValue(npc.GetFinalName(), out var definition) &&
            definition.enabled && (npc.IsCustomNPC() || definition.overrideExisting) &&
            CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(definition.customPlayerCharacterDefinitionName, out var character))
            character.smallNudeSprites.TryGetValue(npc.sex, out sprite);
        if (sprite == alternateMapSprite && (!sprite || (mapMover && mapMover.nonDirectionalSprite == sprite))) return;
        RestoreMapSprite();
        if (!sprite) return;
        var mover = npc.GetComponent<EntityMover>();
        if (!mover || !Fields.Read<SpriteRenderer>(mover, "spriteRenderer")) return;
        mapMover = mover;
        originalMapSprite = mover.nonDirectionalSprite;
        alternateMapSprite = sprite;
        directional = Fields.Read<bool>(mover, "useDirectionalSprites", true);
        var direction = mover.currentVisualDirection;
        mover.nonDirectionalSprite = sprite;
        mover.StopUsingDirectionalSprite();
        mover.HandleDirectionalSprite(direction);
    }

    internal void RestoreMapSprite()
    {
        if (mapMover && mapMover.nonDirectionalSprite == alternateMapSprite)
        {
            var direction = mapMover.currentVisualDirection;
            mapMover.nonDirectionalSprite = originalMapSprite;
            if (directional) mapMover.StartUsingDirectionalSprites();
            mapMover.HandleDirectionalSprite(direction);
        }
        mapMover = null;
        alternateMapSprite = originalMapSprite = null;
    }

    internal void RefreshPortrait()
    {
        if (!refreshPending && isActiveAndEnabled) StartCoroutine(UpdateOpenPortrait());
    }

    private IEnumerator UpdateOpenPortrait()
    {
        refreshPending = true;
        var actor = GetComponent<NeuralNPC>();
        var box = DialogBox.Instance;
        // Allow any current portrait switch to finish before showing the latest outfit.
        do { yield return null; }
        while (box && box.isOpen && NeuralNPC.currentActiveDialogNeuralNPC == actor && box.npcSwitchAnimationPlaying);
        refreshPending = false;
        if (box && box.isOpen && NeuralNPC.currentActiveDialogNeuralNPC == actor && Fields.Read<bool>(box, "isNPCDIalog"))
            actor.DoStartNPCMode(DialogBox.SpriteSwitchMode.Instant);
    }

    private void OnDisable() { StopAllCoroutines(); refreshPending = false; }

    // Silverpine requests additions only for other characters visible to the speaker.
    public string NPCVisibleObjectAddition => Plugin.EquipmentInDialogue.Value &&
        State.Find(GetComponent<NeuralNPC>(), out var wearer) ? Describe(wearer) : "";

    internal static string Describe(Companion wearer)
    {
        if (!Plugin.EquipmentInDialogue.Value) return "";
        var lines = new List<string>();
        Dictionary<int, string> slots = null;
        string name = wearer.Actor.GetFinalName();
        BodyCoverage described = BodyCoverage.None;
        foreach (var entry in wearer.Worn.Where(e => e.Visible && e.Slot != (int)EquipmentSlotType.Fur).OrderBy(e => e.Slot))
        {
            Item item = entry.Item;
            if (entry.Slot == (int)EquipmentSlotType.Weapon)
            {
                lines.Add($"{name} wields {item.name}.");
            }
            else
            {
                slots ??= GearMods.Slots();
                string slot = slots.TryGetValue(entry.Slot, out var label) ? $" ({label})" : "";
                lines.Add($"{name} wears {item.name}{slot}.");
            }
            // GetVisibleString() substitutes the player's pronouns, even on someone else's gear.
            string native = Fields.Read<string>(item.GetItemComponent<ItemComponent_SpecialVisibleClothing>(), "visibleString");
            AddDetail(native);
            string custom = GearMods.VisibleDetail(wearer, entry);
            if (custom != native) AddDetail(custom);
            if (!string.IsNullOrWhiteSpace(custom)) described |= GearMods.NativeCoverage(entry);
        }
        if (wearer.ManagesOutfit)
        {
            // Revealing uses the authored text in place of a generic line for that body area.
            switch (wearer.Covered | described)
            {
                case BodyCoverage.None when wearer.Recruited || wearer.Actor.isNude: lines.Add($"{name} is nude."); break;
                case BodyCoverage.Waist: lines.Add($"{name}'s chest is uncovered."); break;
                case BodyCoverage.Chest: lines.Add($"{name} is uncovered below the waist."); break;
            }
        }
        return string.Join(" ", lines);

        void AddDetail(string template)
        {
            if (template == null) return;
            var pronouns = wearer.Actor.pronouns;
            string detail = template.Replace("%HE%", pronouns.He()).Replace("%HIM%", pronouns.Him())
                .Replace("%HIS%", pronouns.His()).Replace("%NAME%", name).Trim();
            if (detail.Length == 0) return;
            char last = detail[detail.Length - 1];
            lines.Add(last == '.' || last == '?' || last == '!' ? detail : detail + ".");
        }
    }
}

[HarmonyPatch(typeof(NeuralNPC), "Awake")]
internal static class ObserveNpcEquipment
{
    static void Postfix(NeuralNPC __instance)
    {
        if (!__instance.GetComponent<EquipmentKnowledge>()) __instance.gameObject.AddComponent<EquipmentKnowledge>();
    }
}
