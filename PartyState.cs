using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace CompanionParty;

// One row per physical item. Inactive content keeps its original bytes and slot.
public sealed class PackEntry
{
    public string Name, Sprite, Data;
    public List<string> Components;
    public bool Loan;
    public int Slot;
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] internal int RedressSlot;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] internal List<EquipmentSlotType> SavedAccessorySlots;
    // Retain the garment's last known coverage while its item or slot provider is absent.
    [JsonProperty] internal BodyCoverage? SavedCoverage;
    [JsonIgnore] public Item Item;
    internal bool Visible => IsVisibleName(Item?.name ?? Name);
    internal bool KnownPossession => Visible && (Slot == 0 ||
        (Plugin.EquipmentInDialogue.Value && Item != null && Slot != (int)EquipmentSlotType.Fur));
    // The native InventoryUI uses this marker for implementation items such as racial fur.
    internal static bool IsVisibleName(string name) => name != null && !name.Contains("HIDDEN_");

    internal void Capture()
    {
        if (Item == null) return;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Item.Serialize(writer);
        Data = Convert.ToBase64String(stream.ToArray());
        Components = Item.GetItemComponents<ItemComponent>().Select(c => c.GetType().FullName).ToList();
        var accessorySlots = Item.GetItemComponent<ItemComponent_Accessory>()?.coveredSlots;
        SavedAccessorySlots = accessorySlots?.Count > 0 ? new List<EquipmentSlotType>(accessorySlots) : null;
    }

    internal bool CoversSlot(int slot) => (Item == null ? SavedAccessorySlots :
        Item.GetItemComponent<ItemComponent_Accessory>()?.coveredSlots)?.Contains((EquipmentSlotType)slot) == true;

    internal bool Restore(Inventory pack)
    {
        if (Item != null || !GearMods.Available(Sprite, Slot != 0 ? Slot : RedressSlot)) return false;
        using var stream = new MemoryStream(Convert.FromBase64String(Data));
        using var reader = new BinaryReader(stream);
        var item = new Item();
        item.Deserialize(reader);
        if (Components != null && !Components.SequenceEqual(item.GetItemComponents<ItemComponent>().Select(c => c.GetType().FullName)))
            throw new InvalidDataException("One or more saved item components are unavailable.");
        item.inventory = pack;
        pack.items.Add(item);
        Item = item;
        return true;
    }
}

public sealed class Companion
{
    public string Name = "", Order = "Guard", Visit = "", WishFingerprint = "";
    public bool Recruited, Pinned, WishEligible;
    [JsonProperty] internal bool ManagesOutfit;
    [JsonProperty] internal bool ReceivedStartingEquipment;
    [JsonIgnore] internal BodyCoverage Covered;
    public int Gold { get => Pack.gold; set => Pack.gold = value; }
    public int HP = 100, LastMaxHP = 100, Energy = 100, Sanity = 100, DownSince = -1, LastMeal = -2000, LastSnackTurn = -2000;
    public int VisitStart, LastShopTurn = -1000, LastWishDay = -1000, HealingTurns, HealRate;
    public int LastRevivedTurn = -1;
    public bool HelpedUpByPlayer;
    public bool VisitHandled, WishesFulfilled;
    public List<string> Wishes = new();
    public HashSet<string> ExpeditionReceipts = new(StringComparer.Ordinal);
    public List<PackEntry> Items = new();
    [JsonIgnore] internal Inventory Pack = new();
    [JsonIgnore] internal NeuralNPC Actor;
    [JsonIgnore] internal bool Rebind = true;
    [JsonIgnore] internal readonly HashSet<PackEntry> Failed = new();
    [JsonIgnore] internal int CombatTurn = -1, DidAttackTurn = -1;
    [JsonIgnore] internal Enemy Attacker, EngagedEnemy;
    [JsonIgnore] internal int AttackedTurn;
    [JsonIgnore] internal string RacialLight;
    [JsonIgnore] internal Stats CachedStats;
    [JsonIgnore] internal PartySkillBonuses[] SkillBonuses = Array.Empty<PartySkillBonuses>();
    [JsonIgnore] internal int StatsTurn = -1;

    internal void LeaveCombat()
    {
        Attacker = EngagedEnemy = null;
    }

    internal void Bind(NeuralNPC npc)
    {
        bool newActor = Actor != npc || Pack.owner != npc.gameObject;
        if (newActor)
        {
            LeaveCombat();
            Actor = npc;
            Pack.owner = npc.gameObject;
            Name = npc.GetComponent<CustomNPCHandler>()?.customContentName ?? npc.GetFinalName();
            Rebind = true;
        }
        if (!Rebind) return;
        Rebind = false;
        CachedStats = null;
        foreach (var entry in Items.Where(e => e.Item == null && !Failed.Contains(e)))
        {
            try
            {
                if (entry.Restore(Pack) && npc.npcName == NPCName.Aldric) StartingEquipment.RestoreAldricGarment(entry);
            }
            catch (Exception ex) { Failed.Add(entry); Plugin.Log.LogWarning($"Retaining unavailable {entry.Name}: {ex.GetBaseException().Message}"); }
        }
        if (newActor && Recruited) SkillBonuses = PartySkillApi.Resolve(npc);
        Clamp(preserveNativeAppearance: true);
    }

    internal IEnumerable<PackEntry> Worn => Items.Where(e => e.Slot != 0 && e.Item != null);
    internal PackEntry Wears(EquipmentSlotType slot) => Worn.FirstOrDefault(e => e.Slot == (int)slot);
    internal bool Down => DownSince >= 0;
    internal void RefreshSkills()
    {
        SkillBonuses = Recruited ? PartySkillApi.Resolve(Actor) : Array.Empty<PartySkillBonuses>();
        Clamp();
    }
    internal void Clamp(bool preserveNativeAppearance = false)
    {
        CachedStats = null;
        var stats = Stats.Read(this);
        LastMaxHP = stats.MaxHP;
        HP = Mathf.Clamp(HP, Down ? 0 : 1, stats.MaxHP);
        Energy = Mathf.Clamp(Energy, 0, stats.MaxEnergy);
        if (Pack.maxBulk != stats.Capacity)
        {
            Pack.maxBulk = stats.Capacity;
            Pack.UpdateEncumbered();
        }
        Outfit.Update(this, preserveNativeAppearance);
    }
    internal void Remove(PackEntry entry)
    {
        if (entry.Item == null || !Items.Contains(entry)) return;
        Pack.RemoveItem(entry.Item);
        Items.Remove(entry);
        Clamp();
    }
    internal bool Accept(Item item, bool loan)
    {
        if (!PackEntry.IsVisibleName(item?.name) || item.inventory == null || !item.inventory.ContainsItem(item) || !Pack.CanAddItem(item)) return false;
        var player = Player.Instance;
        if (player != null && item.inventory == player.worldInventory.inventory && player.IsItemEquipped(item))
            player.UnequipItem(item);
        if (!item.TryMoveItemToInventory(Pack)) return false;
        item.RemoveItemComponentsOfType<ItemComponent_VendorItem>();
        Items.Add(new PackEntry { Item = item, Name = item.name, Sprite = item.spriteName, Loan = loan });
        return true;
    }
    internal bool Equip(PackEntry entry, bool manual)
    {
        if (!Items.Contains(entry) || entry.Item == null) return false;
        var slot = entry.Item.GetEquipmentSlotType();
        if (slot == EquipmentSlotType.NotEquipable || !GearMods.Available(entry.Sprite, (int)slot)) return false;
        foreach (var other in Items)
        {
            if (other.Slot != 0 && Conflicts(entry, (int)slot, other, other.Slot))
                other.Slot = 0;
            if (other.RedressSlot != 0 && Conflicts(entry, (int)slot, other, other.RedressSlot))
                other.RedressSlot = 0;
        }
        entry.Slot = (int)slot;
        if (manual) Pinned = true;
        Clamp();
        return true;
    }

    internal static bool Conflicts(PackEntry a, int slotA, PackEntry b, int slotB) =>
        slotA == slotB || a.CoversSlot(slotB) || b.CoversSlot(slotA);
}

public sealed class Campaign
{
    public Dictionary<string, Companion> People = new(StringComparer.Ordinal);
    public double PlaySeconds;
    public List<double> Attempts = new();
}

internal static class State
{
    internal static Campaign Save = new();
    internal static int Epoch;
    internal static string Key(NeuralNPC npc)
    {
        var custom = npc.GetComponent<CustomNPCHandler>();
        return custom != null && !string.IsNullOrWhiteSpace(custom.customContentName)
            ? CustomKey(custom) : "native:" + npc.npcName;
    }
    private static string CustomKey(CustomNPCHandler custom) => "custom:" + custom.customContentName;

    internal static void EnsureCustomResidents()
    {
        // The custom manager owns these actors before slot registration and while some actors are inactive.
        if (SerializationManager.loadingSave) return;
        foreach (var custom in CustomNPCHandler.customNPCHandlers.ToArray())
        {
            if (!custom || string.IsNullOrWhiteSpace(custom.customContentName)) continue;
            if (!custom.TryGetComponent<NeuralNPC>(out var npc)) continue;
            if (Save.People.TryGetValue(CustomKey(custom), out var person) && person.Actor == npc && person.ReceivedStartingEquipment) continue;
            Get(npc);
        }
    }
    internal static Companion Get(NeuralNPC npc)
    {
        string key = Key(npc);
        if (!Save.People.TryGetValue(key, out var person)) Save.People.Add(key, person = new Companion());
        person.Bind(npc);
        StartingEquipment.Ensure(person);
        return person;
    }
    internal static bool Find(NeuralNPC npc, out Companion person)
    {
        person = null;
        if (npc == null) return false;
        var custom = npc.GetComponent<CustomNPCHandler>();
        if (custom && string.IsNullOrWhiteSpace(custom.customContentName)) return false;
        if (!Save.People.TryGetValue(custom ? CustomKey(custom) : "native:" + npc.npcName, out person))
        {
            if (!Plugin.Ready) return false;
            bool spawnedCustom = custom && CustomNPCHandler.customNPCHandlers.Contains(custom);
            if (!spawnedCustom && (!npc.isActiveAndEnabled || npc.npcName == NPCName.None ||
                !NeuralNPC.neuralNPCs.TryGetValue(npc.npcName, out var resident) || resident != npc)) return false;
            person = Get(npc);
            return true;
        }
        person.Bind(npc);
        StartingEquipment.Ensure(person);
        return true;
    }
    internal static string Capture()
    {
        foreach (var person in Save.People.Values)
            foreach (var entry in person.Items) entry.Capture();
        return JsonConvert.SerializeObject(Save);
    }
    private static void ReleaseOutfits()
    {
        foreach (var person in Save.People.Values)
            if (person.Actor) person.Actor.GetComponent<EquipmentKnowledge>()?.RestoreMapSprite();
    }
    internal static void Reset() { ReleaseOutfits(); Epoch++; Save = new Campaign(); PartyUI.Release(); Combat.Reset(); }
    internal static void Restore(string json)
    {
        var data = JsonConvert.DeserializeObject<Campaign>(json);
        if (data?.People == null || data.Attempts == null || data.People.Values.Any(p => p?.Items == null || p.Wishes == null || p.Gold < 0 ||
            p.Items.Any(e => e == null || string.IsNullOrEmpty(e.Data)))) throw new InvalidDataException("Invalid companion save.");
        foreach (var pair in data.People)
            if (pair.Key.StartsWith("custom:", StringComparison.Ordinal)) StartingEquipment.RestoreLegacyLinen(pair.Value);
        ReleaseOutfits();
        Save = data;
    }
    internal static bool Near(NeuralNPC npc, Transform target, float range)
    {
        if (!npc || !target || !npc.gameObject.activeInHierarchy || !target.gameObject.activeInHierarchy) return false;
        var a = npc.transform.GetVector2IntPosition();
        var b = target.GetVector2IntPosition();
        return (a - b).sqrMagnitude <= range * range && MapZone.GetPositionZoneName(a) == MapZone.GetPositionZoneName(b)
            && (a == b || TurfCollider.CanPositionsSeeEachOtherAdvanced(a, b));
    }
}
