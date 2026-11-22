using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using Charm = ItemComponent_Enchantment.EnchantmentType;

namespace CompanionParty;

internal static class Fields
{
    private static readonly Dictionary<(Type, string), FieldInfo> Cache = new();
    internal static T Read<T>(object target, string name, T fallback = default)
    {
        if (target == null) return fallback;
        var key = (target.GetType(), name);
        if (!Cache.TryGetValue(key, out var field)) Cache[key] = field = AccessTools.Field(key.Item1, name);
        return field?.GetValue(target) is T value ? value : fallback;
    }
}

internal static class GearMods
{
    internal const string Loader = "renegadex.silverpine.customitemloader";
    internal const string Expansion = "renegadex.silverpine.attributeexpansion";
    internal const string Revealing = "renegadex.silverpine.revealing";
    private static MethodInfo findItem, findSlot, allSlots, getModifier;
    private static MethodInfo findAppearance, isUnderTop, isUnderWaist;
    private static PropertyInfo slotId;
    private static IDictionary basics;
    internal static bool Expanded;
    internal static void Init()
    {
        Expanded = Chainloader.PluginInfos.ContainsKey(Expansion);
        if (Chainloader.PluginInfos.TryGetValue(Revealing, out var appearance))
        {
            var type = appearance.Instance.GetType().Assembly.GetType("SilverpineMods.Revealing.RevealingRuntime");
            if (type != null)
            {
                findAppearance = AccessTools.Method(type, "TryGet");
                isUnderTop = AccessTools.Method(type, "IsUnderTop");
                isUnderWaist = AccessTools.Method(type, "IsUnderWaist");
            }
        }
        if (!Chainloader.PluginInfos.TryGetValue(Loader, out var info)) return;
        var dll = info.Instance.GetType().Assembly;
        Type LoaderType(string name) => dll.GetType("SilverpineMods.CustomItemLoader." + name);
        var registry = LoaderType("CustomEquipmentSlotRegistry");
        findItem = LoaderType("CustomItemApi")?.GetMethod("TryGetItem");
        findSlot = registry?.GetMethods().FirstOrDefault(m => m.Name == "TryGetSlot" && m.GetParameters()[0].ParameterType == typeof(int));
        slotId = LoaderType("CustomEquipmentSlotInfo")?.GetProperty("Id");
        allSlots = registry?.GetMethod("GetSlots");
        getModifier = LoaderType("CustomAttributeModifierApi")?.GetMethod("GetModifier");
        basics = AccessTools.Field(info.Instance.GetType(), "AttributeModifiers")?.GetValue(null) as IDictionary;
    }
    internal static bool Available(string sprite, int slot)
    {
        if (sprite?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true &&
            !(findItem?.Invoke(null, new object[] { sprite, null }) is true)) return false;
        return slot <= 7 || findSlot?.Invoke(null, new object[] { slot, null }) is true;
    }
    internal static Dictionary<int, string> Slots()
    {
        var slots = Enum.GetValues(typeof(EquipmentSlotType)).Cast<EquipmentSlotType>()
            .Where(s => (int)s > 0).ToDictionary(s => (int)s, s => s.ToString());
        if (allSlots?.Invoke(null, null) is IEnumerable list)
            foreach (object slot in list)
                slots[(int)slot.GetType().GetProperty("NumericValue").GetValue(slot)] =
                    (string)slot.GetType().GetProperty("DisplayName").GetValue(slot);
        return slots;
    }
    internal static object Basic(Item i) => i.spriteName == null ? null : basics?[i.spriteName];
    private static object Appearance(Item item)
    {
        if (findAppearance == null || item == null) return null;
        object[] args = { item, null };
        return findAppearance.Invoke(null, args) is true ? args[1] : null;
    }

    internal static BodyCoverage NativeCoverage(PackEntry entry)
    {
        var slot = (EquipmentSlotType)entry.Slot;
        var covered = entry.Item?.GetItemComponent<ItemComponent_Accessory>()?.coveredSlots;
        BodyCoverage result = BodyCoverage.None;
        if (slot == EquipmentSlotType.Chest || covered?.Contains(EquipmentSlotType.Chest) == true) result |= BodyCoverage.Chest;
        if (slot == EquipmentSlotType.Legs || covered?.Contains(EquipmentSlotType.Legs) == true) result |= BodyCoverage.Waist;
        if (entry.Slot > (int)EquipmentSlotType.Neck && findSlot != null)
        {
            object[] args = { entry.Slot, null };
            if (findSlot.Invoke(null, args) is true)
                switch (slotId?.GetValue(args[1]) as string)
                {
                    case "silverpine.equipslots.under-top": result |= BodyCoverage.Chest; break;
                    case "silverpine.equipslots.under-waist": result |= BodyCoverage.Waist; break;
                }
        }
        return result;
    }

    internal static BodyCoverage Coverage(Item item, BodyCoverage native)
    {
        object settings = Appearance(item);
        if (settings == null) return native;
        return (Fields.Read<bool>(settings, "FullyCoversChest") ? BodyCoverage.Chest : BodyCoverage.None) |
            (Fields.Read<bool>(settings, "FullyCoversWaist") ? BodyCoverage.Waist : BodyCoverage.None);
    }

    internal static string VisibleDetail(Companion wearer, PackEntry entry)
    {
        object own = Appearance(entry.Item);
        if (own == null || Fields.Read<bool>(own, "FullyCoversChest") || Fields.Read<bool>(own, "FullyCoversWaist")) return null;
        // Revealing shows an under-layer's authored detail only through a configured outer garment.
        object[] slot = { (EquipmentSlotType)entry.Slot };
        if (isUnderTop?.Invoke(null, slot) is true && !OuterShows(EquipmentSlotType.Chest, "FullyCoversChest")) return null;
        if (isUnderWaist?.Invoke(null, slot) is true && !OuterShows(EquipmentSlotType.Legs, "FullyCoversWaist")) return null;
        return Fields.Read<string>(own, "Description");

        bool OuterShows(EquipmentSlotType outerSlot, string covers)
        {
            var outer = Appearance(wearer.Wears(outerSlot)?.Item);
            return outer != null && !Fields.Read<bool>(outer, covers, true);
        }
    }
    internal static float Extra(Item item, string field) =>
        Expanded && getModifier != null && item.spriteName.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)
            ? (float)getModifier.Invoke(null, new object[] { item, field }) : 0;
}

internal sealed class Stats
{
    internal int MaxHP = 100, MaxEnergy = 100, Capacity = 40, AttackDamage;
    internal readonly int[] Armor = new int[4];
    internal float Crit, Parry, Reflect, Potency = 1, Sell, Social, Preserve, Regen, Speed;
    internal bool Claws, NightSight, Glow, Slow, ColdSensitive;
    internal int Consumption;
    internal float Knowledge, CustomPotency;
    private static readonly MethodInfo ArmorValues = AccessTools.Method(typeof(ItemComponent_Clothing), "GetArmorValues");
    internal static Stats Read(Companion person)
    {
        int turn = WorldInfoManager.Instance ? WorldInfoManager.Instance.TotalTurnCount : -1;
        if (person.CachedStats != null && person.StatsTurn == turn) return person.CachedStats;
        var result = new Stats();
        if (person.Actor != null)
        {
            var custom = person.Actor.GetComponent<CustomNPCHandler>();
            string name = custom != null ? custom.customContentName : person.Actor.GetFinalName();
            if (CustomContentDefinition_NPC.loaded.TryGetValue(name, out var def) && def.enabled &&
                CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(def.customPlayerCharacterDefinitionName, out var pc))
                foreach (var racial in pc.racials.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    switch (racial.Replace("Racial_", "").Replace("_", "").Replace(" ", "").ToLowerInvariant())
                    {
                        case "hollowbones": result.MaxHP -= 15; break;
                        case "thickskin": result.Armor[(int)DamageType.Normal]++; break;
                        case "fur": result.Armor[(int)DamageType.Frost]++; break;
                        case "largeclaws": result.Claws = true; break;
                        case "nightvision": result.NightSight = true; break;
                        case "bioluminescence": result.Glow = true; break;
                        case "slow": result.Slow = true; break;
                        case "coldweakness": result.ColdSensitive = true; break;
                    }
                }
        }
        foreach (var entry in person.Worn) result.Add(entry.Item);
        if (person.Recruited)
            foreach (var benefit in person.SkillBonuses) result.Add(benefit);
        result.MaxHP = Math.Max(1, result.MaxHP);
        result.MaxEnergy = Math.Max(1, result.MaxEnergy);
        result.Capacity = Math.Max(1, result.Capacity);
        if (GearMods.Expanded)
            for (int i = 0; i < 4; i++) result.Armor[i] = Mathf.Clamp(result.Armor[i], 0, 254);
        person.StatsTurn = turn;
        person.CachedStats = result;
        return result;
    }
    private void Add(PartySkillBonuses benefit)
    {
        MaxHP = Points(MaxHP, benefit.MaxHealth);
        MaxEnergy = Points(MaxEnergy, benefit.MaxEnergy);
        Capacity = Points(Capacity, benefit.CarryCapacity);
        AttackDamage = Points(AttackDamage, benefit.AttackDamage);
        Armor[(int)DamageType.Normal] = Points(Armor[(int)DamageType.Normal], benefit.NormalArmor);
        Armor[(int)DamageType.Fire] = Points(Armor[(int)DamageType.Fire], benefit.FireArmor);
        Armor[(int)DamageType.Frost] = Points(Armor[(int)DamageType.Frost], benefit.FrostArmor);
        Crit += Mathf.Clamp01(Positive(benefit.MeleeCriticalChance));
        Parry += Mathf.Clamp01(Positive(benefit.MeleeParryChance));
        Speed += Mathf.Clamp01(Positive(benefit.ExtraStepChance));
        Preserve += Mathf.Clamp01(Positive(benefit.DurabilityPreservationChance));
        Regen += Positive(benefit.HealthRegenerationPerTurn);
        Potency += Positive(benefit.PotionPotencyBonus);
    }
    private static int Points(int current, int bonus) => (int)Math.Min(int.MaxValue, (long)current + Math.Max(0, bonus));
    private static float Positive(float bonus) => float.IsNaN(bonus) || float.IsInfinity(bonus) ? 0 : Math.Max(0, bonus);
    internal void Add(Item item)
    {
        foreach (var clothing in item.GetItemComponents<ItemComponent_Clothing>())
            foreach (var armor in (Dictionary<DamageType, int>)ArmorValues.Invoke(clothing, null))
                Armor[(int)armor.Key] += Mathf.RoundToInt(armor.Value * (1 + .3f * (int)clothing.Quality));
        var basic = GearMods.Basic(item);
        MaxHP += Fields.Read<int>(basic, "maxHealth");
        MaxEnergy += Fields.Read<int>(basic, "maxEnergy");
        Capacity += Fields.Read<int>(basic, "carryCapacity");
        Armor[0] += Fields.Read<int>(basic, "normalArmor");
        Armor[2] += Fields.Read<int>(basic, "fireArmor");
        Armor[3] += Fields.Read<int>(basic, "frostArmor");
        foreach (var effect in item.GetItemComponents<ItemComponent_Enchantment>())
        {
            switch (effect.enchantmentType)
            {
                case Charm.Vitality: MaxHP += 10; break;
                case Charm.Warding: for (int i = 0; i < 4; i++) Armor[i]++; break;
                case Charm.Protection: Armor[2] += 3; Armor[3] += 3; break;
                case Charm.Effortlessness: Capacity += 10; break;
                case Charm.Invigoration: MaxEnergy += 15; break;
                case Charm.Precision: Crit += .05f; break;
                case Charm.Daring: Parry += .1f; break;
                case Charm.Reflection: Reflect += .1f; break;
                case Charm.Knowledge: Knowledge += .5f; break;
                case Charm.Charming: Social += .1f; Sell += .1f; break;
                case Charm.Regeneration: Regen += .04f; break;
                case Charm.Consumption: Consumption++; break;
                    // Unbreaking is evaluated by the item's native durability setter.
            }
        }
        Crit += GearMods.Extra(item, "criticalStrikeChance");
        Parry += GearMods.Extra(item, "parryChance");
        Reflect += GearMods.Extra(item, "reflectionChance");
        CustomPotency += GearMods.Extra(item, "potionPotencyMultiplier");
        Potency = (1 + Knowledge) * Mathf.Max(0, 1 + CustomPotency);
        Sell += GearMods.Extra(item, "sellPriceMultiplier");
        Social += GearMods.Extra(item, "relationshipBonusChance");
        Preserve += GearMods.Extra(item, "durabilityPreservationChance");
        Regen += GearMods.Extra(item, "healthRegenerationPerTurn");
        Speed += GearMods.Extra(item, "speedBonus");
    }
    internal static string Describe(Stats s) =>
        $"Max HP {s.MaxHP}   Energy {s.MaxEnergy}   Carry {s.Capacity}\nArmor   Normal {s.Armor[0]}   Fire {s.Armor[2]}   Frost {s.Armor[3]}\n" +
        (s.AttackDamage > 0 ? $"Attack damage +{s.AttackDamage}\n" : "") +
        $"Crit bonus {s.Crit:P0}   Parry {s.Parry:P0}   Reflect {s.Reflect:P0}   Potion potency {s.Potency:P0}\n" +
        $"Regen {s.Regen:0.##}/turn   Extra step {s.Speed:P0}   Preserve gear {s.Preserve:P0}   Sales {s.Sell:+0%;-0%;0%}   Rapport {s.Social:P0}";
}
