using System.Collections.Generic;
using System.Linq;

namespace CompanionParty;

internal static class StartingEquipment
{
    private sealed class Piece
    {
        internal readonly string Template, Name, Description;
        internal Piece(string template, string name = null, string description = null)
        { Template = template; Name = name; Description = description; }

        internal Item Create()
        {
            var item = ItemLibrary.GetItemByName(Template, true);
            if (Name != null) item.name = Name;
            if (Description != null)
            {
                item.description = Description;
                if (item.itemCategory == ItemCategory.Clothing)
                    item.AddItemComponent(new ItemComponent_SpecialVisibleClothing(Description));
            }
            return item;
        }
    }

    private static readonly Piece[] Linen = { new("Linen Shirt"), new("Linen Pants") };
    private static readonly Piece MerchantVest = new("Linen Shirt", "Merchant's Green Vest",
        "A long green vest hangs over a pale work shirt. The sleeves are rolled clear of the leather wrist guards.");
    private static readonly Piece MerchantBreeches = new("Linen Pants", "Merchant's Breeches",
        "Green breeches gather at the calf above brown leather gaiters.");
    // These are the ten named residents instantiated by the main game scene.
    private static readonly Dictionary<NPCName, Piece[]> Residents = new()
    {
        [NPCName.Hayden] = new Piece[] {
            new("Linen Shirt", "Baker's Apron and Shirt", "A wheat sprig decorates a green apron tied over a white shirt with rolled sleeves."),
            new("Linen Pants", "Baker's Cuffed Trousers", "Dark work trousers gather into broad cuffs above the ankles.") },
        [NPCName.Rosalyn] = new Piece[] {
            new("Linen Robe", "Apothecary's Sashed Dress", "A dark band edges the wide neckline of a pale dress. A brown bodice and long sash gather it at the waist.") },
        [NPCName.Aldric] = new[] { MerchantVest, MerchantBreeches },
        [NPCName.Gareth] = new Piece[] {
            new("Linen Shirt", "Tavern Keeper's Apron", "Broad shoulder straps hold a plain brown bib apron, cinched at the waist with a leather belt."),
            new("Linen Pants", "Tavern Keeper's Breeches", "Dark knee breeches leave the lower legs free.") },
        [NPCName.Celandine] = new Piece[] {
            new("Linen Robe", "Corded Ivory Dress", "A fine dark cord circles the waist twice and hangs down the front of a softly gathered ivory dress.") },
        [NPCName.Oriana] = new Piece[] {
            new("Linen Robe", "Laced Cream Gown", "Short dark laces close the bodice of a cream gown with long, flared sleeves and a full skirt.") },
        [NPCName.Darian] = new Piece[] {
            new("Linen Shirt", "Belted Trail Tunic", "A diagonal leather strap crosses a long brown tunic. A belt gathers its uneven hem at the hip."),
            new("Linen Pants", "Trail Trousers and Gaiters", "Brown trousers tuck into dark leather gaiters laced up the shins."),
            new("Iron Sword", "Belt Sword", "A short iron blade with a straight crossguard fits the scabbard at the hip.") },
        [NPCName.Mirel] = new Piece[] {
            new("Linen Shirt", "Blue-Embroidered Blouse", "Blue stitching borders the neckline and sleeves of a white gathered blouse."),
            new("Linen Pants", "Tied Hip Wrap", "A broad brown cloth wraps around the hips, knotted at the front with a short overlapping panel.") },
        [NPCName.Acacia] = new Piece[] {
            new("Linen Shirt", "Patterned Woven Top", "Rows of dark geometric stitching cross a pale woven top that leaves the midriff bare."),
            new("Linen Pants", "Fringed Hide Wrap", "Layered dark hide panels hang from a low belt beside small russet pouches.") },
        [NPCName.Orson] = new Piece[] {
            new("Linen Shirt", "Archer's Blue Tunic", "A quiver strap crosses a blue lace-front tunic with a narrow belt and brown wrist guards."),
            new("Linen Pants", "Archer's Field Trousers", "Loose pale trousers tuck into tall brown leather gaiters."), new("Bow"), new("Quiver") }
    };

    internal static void RestoreAldricGarment(PackEntry entry)
    {
        var replacement = entry.Name switch
        {
            "Smith's Green Work Vest" => MerchantVest,
            "Smith's Work Breeches" => MerchantBreeches,
            _ => null
        };
        if (replacement != null && entry.Item?.name == entry.Name && entry.Item.description == replacement.Description)
            entry.Name = entry.Item.name = replacement.Name;
        // The old hammer was an ordinary library item with no record of how it was acquired.
    }

    internal static void Ensure(Companion person)
    {
        var npc = person.Actor;
        if (person.ReceivedStartingEquipment || SerializationManager.loadingSave) return;
        if (!Residents.TryGetValue(npc.npcName, out var outfit))
        {
            if (!npc.IsCustomNPC() || string.IsNullOrWhiteSpace(npc.GetComponent<CustomNPCHandler>()?.customContentName)) return;
            outfit = Linen;
        }
        // Create every physical item before changing the save. Existing outfits and deliberate
        // undress keep their appearance. A starter set can still be worn later from the pack.
        bool preserveClothes = person.ManagesOutfit || npc.isNude || person.Pinned ||
            person.Items.Any(e => e.Slot != 0 && e.Item == null);
        var items = outfit.Select(piece => piece.Create()).ToArray();
        foreach (var item in items)
        {
            var slot = item.GetEquipmentSlotType();
            bool wear = slot != EquipmentSlotType.NotEquipable && !person.Items.Any(e => e.Slot == (int)slot) &&
                (slot == EquipmentSlotType.Weapon ? !person.Pinned : !preserveClothes);
            // Starting possessions, like restored possessions, remain owned even in a full pack.
            item.inventory = person.Pack;
            person.Pack.items.Add(item);
            person.Items.Add(new PackEntry { Name = item.name, Sprite = item.spriteName, Item = item, Slot = wear ? (int)slot : 0 });
        }
        person.ReceivedStartingEquipment = true;
        if (npc.isNude) person.ManagesOutfit = true;
        person.Clamp();
        person.Pack.UpdateEncumbered();
    }
}
