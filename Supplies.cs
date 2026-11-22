using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CompanionParty;

internal static class Supplies
{
    private static readonly System.Reflection.MethodInfo Dark = AccessTools.Method(typeof(NeuralNPC), "IsLocationDark");
    private static readonly System.Reflection.FieldInfo LampGuid = AccessTools.Field(typeof(ItemComponent_Lantern), "guid");
    private static readonly System.Reflection.FieldInfo LampLit = AccessTools.Field(typeof(ItemComponent_Lantern), "lit");
    internal static bool Has<T>(Item i) where T : ItemComponent => i?.GetItemComponent<T>() != null;
    internal static bool Healing(Item i) => Fields.Read<List<AlchemyEffectType>>(i?.GetItemComponent<ItemComponent_Potion>(), "alchemyEffectTypes")?.Contains(AlchemyEffectType.Healing) == true;
    internal static bool Heal(Companion p, Stats stats)
    {
        if (p.HealingTurns > 0) return false;
        var entry = p.Items.FirstOrDefault(e => Healing(e.Item));
        if (entry == null) return false;
        var potion = entry.Item.GetItemComponent<ItemComponent_Potion>();
        float rate = 4 * Fields.Read(potion, "potency", 1f) * stats.Potency;
        p.HealRate = rate >= int.MaxValue ? int.MaxValue : Math.Max(1, Mathf.RoundToInt(rate));
        p.HealingTurns = 10;
        p.Remove(entry);
        if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingText("Healing", p.Actor.transform);
        return true;
    }
    internal static void Snack(Companion p, bool conversation)
    {
        if (p.Down || p.Actor.sleeping || Combat.Turn - p.LastMeal < 360) return;
        var food = p.Items.Where(e => Has<ItemComponent_Edible>(e.Item) && !Healing(e.Item))
            .OrderByDescending(e => Wishes.Match(p, e.Item)).FirstOrDefault();
        if (food == null) return;
        var edible = food.Item.GetItemComponent<ItemComponent_Edible>();
        p.LastMeal = Combat.Turn;
        p.Sanity = Mathf.Clamp(p.Sanity + Fields.Read<int>(edible, "sanityGain"), 0, 100);
        p.Energy = (int)Math.Min(Stats.Read(p).MaxEnergy, (long)p.Energy + Math.Max(1, Fields.Read<int>(edible, "hungerLoss")));
        p.Remove(food);
        string action = $"{p.Name} eats {food.Name} from their own pack.";
        if (conversation) p.Actor.QuietlyAddSystemDialog(action);
        if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingText("Eats " + food.Name, p.Actor.transform);
    }

    internal static void Tick(Companion p)
    {
        var stats = Stats.Read(p);
        foreach (var e in p.Items.Where(e => e.Item != null).ToArray())
        {
            var expiration = e.Item.GetItemComponent<ItemComponent_Expirable>();
            expiration?.OnPostProcessTurn();
            if (expiration != null && Fields.Read<int>(expiration, "turnsToExpire") <= 0 && Has<ItemComponent_Edible>(e.Item)) { p.Remove(e); continue; }
            var lantern = e.Item.GetItemComponent<ItemComponent_Lantern>();
            if (Fields.Read<bool>(lantern, "lit"))
                Combat.Wear(p, e.Item, stats, 100f / Math.Max(1, Fields.Read(lantern, "minutes", 60) * 2));
        }
        if (p.Down)
        {
            if (Combat.Turn - p.DownSince >= 12 && !Enemy.allEnemies.Any(e => Combat.Threat(p, e)))
                Revive(p);
            return;
        }
        if (p.HealingTurns > 0) { p.HealingTurns--; p.HP = (int)Math.Min(stats.MaxHP, (long)p.HP + p.HealRate); }
        int regen = stats.Regen >= int.MaxValue ? int.MaxValue : Mathf.FloorToInt(stats.Regen);
        if (regen < int.MaxValue && UnityEngine.Random.value < stats.Regen - regen) regen++;
        p.HP = (int)Math.Min(stats.MaxHP, (long)p.HP + regen);
        p.Energy = (int)Math.Min(stats.MaxEnergy, (long)p.Energy + 1);
        if (stats.ColdSensitive && Combat.Turn % 60 == 0 && stats.Armor[(int)DamageType.Frost] < 2 &&
            p.Actor.GetPositionZoneNameAddOutside() == "Outside" && WorldInfoManager.Instance.GetSeasonFor(p.Actor.gameObject) == Season.Winter)
            p.Energy = Math.Max(0, p.Energy - 3);
        var light = p.Actor.GetComponent<LightAttacher>();
        if (light != null && stats.Glow && p.RacialLight == null)
        {
            p.RacialLight = Plugin.Id + ".racial";
            light.AddLight(p.RacialLight, new Color(.65f, .89f, 1f), 2f, .5f, false);
        }
        if (light != null && !stats.Glow && p.RacialLight != null) { light.RemoveLight(p.RacialLight); p.RacialLight = null; }
        if (Combat.Turn % 4 != 0) return;
        var lamp = p.Items.FirstOrDefault(e => Has<ItemComponent_Lantern>(e.Item));
        if (lamp != null && light)
        {
            var lantern = lamp.Item.GetItemComponent<ItemComponent_Lantern>();
            var fuel = lamp.Item.GetItemComponent<ItemComponent_Durability>();
            if (fuel != null)
            {
                bool dark = !p.Actor.sleeping && !stats.Glow && !stats.NightSight &&
                    ((bool)Dark.Invoke(p.Actor, null) || (!WorldInfoManager.Instance.IsDaylight() && p.Actor.GetPositionZoneNameAddOutside() == "Outside"));
                MaintainLantern(p, lantern, fuel, light, dark);
            }
        }
        int hour = WorldInfoManager.Instance.GetCurrentHourOfDay();
        if ((hour == 7 || hour == 13 || hour == 19) && Combat.Turn - p.LastMeal >= 720 && !Enemy.allEnemies.Any(e => Combat.Threat(p, e)))
            Snack(p, false);
    }
    internal static void MaintainLantern(Companion p, ItemComponent_Lantern lantern, ItemComponent_Durability fuel, LightAttacher light, bool dark)
    {
        if (dark && fuel.Durability <= fuel.maxDurability * .2f)
        {
            var oil = p.Items.FirstOrDefault(e => Has<ItemComponent_LanternOil>(e.Item));
            if (oil != null) { fuel.Repair(); p.Remove(oil); }
        }
        bool needed = dark && fuel.Durability > 0;
        if ((bool)LampLit.GetValue(lantern) == needed) return;
        if (needed)
        {
            var id = Guid.NewGuid().ToString();
            LampGuid.SetValue(lantern, id);
            light.AddLight(id, new Color(1f, .68f, .41f), 5f, 2f, true);
        }
        else light.RemoveLight((string)LampGuid.GetValue(lantern));
        LampLit.SetValue(lantern, needed);
        if (AudioPlayer.Instance) AudioPlayer.Instance.PlayAudioAtPosition(needed ? "sound_misc_light" : "sound_misc_extinguish", .8f, p.Actor.transform.GetVector2IntPosition());
    }
    internal static void Revive(Companion p, bool helpedByPlayer = false)
    {
        if (!p.Down) return;
        p.DownSince = -1;
        p.LastRevivedTurn = Combat.Turn;
        p.HelpedUpByPlayer = helpedByPlayer;
        p.HP = Math.Max(1, Stats.Read(p).MaxHP / 2);
        if (helpedByPlayer && p.Recruited && PartySkillApi.ObservingActivity)
            PartySkillApi.Completed(PartyActivity.HelpedUp, new List<NeuralNPC> { p.Actor });
        if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingText("Back on my feet", p.Actor.transform);
    }
}

public interface ICompanionShop
{
    string Id { get; }
    bool IsOpen(NeuralNPC seller);
    IEnumerable<Item> Stock(NeuralNPC seller);
    int Price(NeuralNPC seller, NeuralNPC buyer, Item item);
    // True means the specified item was delivered into destination and the seller paid.
    // False leaves the stock, destination and seller's balance unchanged.
    bool TrySell(NeuralNPC seller, NeuralNPC buyer, Item item, int price, Inventory destination);
}

public static class CompanionShops
{
    private static readonly Dictionary<string, ICompanionShop> Providers = new(StringComparer.Ordinal);
    public static void Register(ICompanionShop shop)
    {
        if (shop == null || string.IsNullOrWhiteSpace(shop.Id)) throw new ArgumentException("A shop ID is required.");
        Providers[shop.Id] = shop;
    }
    public static void Unregister(string id) => Providers.Remove(id);
    internal static ICompanionShop Provider(NeuralNPC seller) => Providers.Values.FirstOrDefault(s => s.IsOpen(seller));
}

internal static class Market
{
    internal static void Visit(NeuralNPC buyer, IReadOnlyList<NeuralNPC> vendors, IDictionary<NeuralNPC, ICompanionShop> providers)
    {
        var seller = vendors.Where(v => v != buyer && State.Near(buyer, v.transform, 3))
            .OrderBy(v => (buyer.transform.position - v.transform.position).sqrMagnitude).FirstOrDefault();
        if (!seller || buyer.sleeping)
        {
            if (State.Find(buyer, out var old)) { old.Visit = ""; old.VisitHandled = false; }
            return;
        }
        var p = State.Get(buyer);
        if (p.Down || (p.Recruited && Enemy.allEnemies.Any(e => Combat.Threat(p, e)))) return;
        providers.TryGetValue(seller, out var provider);
        string id = State.Key(seller) + "|" + (provider?.Id ?? "shop");
        if (p.Visit != id) { p.Visit = id; p.VisitStart = Combat.Turn; p.VisitHandled = false; return; }
        if (p.VisitHandled || Combat.Turn - p.VisitStart < 2 || Combat.Turn - p.LastShopTurn < 10) return;
        p.VisitHandled = p.WishEligible = true;
        p.LastShopTurn = Combat.Turn;
        Trade(p, seller, provider);
    }
    internal static void Trade(Companion p, NeuralNPC seller, ICompanionShop provider)
    {
        var buyer = p.Actor;
        var till = seller.GetComponent<WorldInventory>()?.inventory;
        // Buy mode exposes the merchant's shop inventory, including untagged stock
        // supplied by other mods. The companion's personal pack is a separate object.
        var stock = provider != null ? provider.Stock(seller) : till?.items;
        if (stock == null) return;
        if (provider == null) SellSurplus(p, seller, till);
        if (p.Gold <= 0) return;
        var offer = stock.Where(i => PackEntry.IsVisibleName(i?.name) && p.Pack.CanAddItem(i)).Select(i => new
        {
            Item = i,
            Price = provider?.Price(seller, buyer, i) ?? i.GetFinalValue(),
            Score = Score(p, i)
        }).Where(o => o.Price > 0 && o.Price <= p.Gold && o.Score > 0)
          .OrderByDescending(o => o.Score).ThenBy(o => o.Price).FirstOrDefault();
        if (offer == null) return;
        if (provider == null)
        {
            if (!till.ContainsItem(offer.Item) || till.gold > int.MaxValue - offer.Price || !p.Accept(offer.Item, false)) return;
            till.gold += offer.Price;
        }
        else
        {
            if (!provider.TrySell(seller, buyer, offer.Item, offer.Price, p.Pack)) return;
            if (!p.Pack.ContainsItem(offer.Item)) throw new InvalidOperationException("Shop did not deliver the quoted item.");
            offer.Item.RemoveItemComponentsOfType<ItemComponent_VendorItem>();
            p.Items.Add(new PackEntry { Item = offer.Item, Name = offer.Item.name, Sprite = offer.Item.spriteName });
        }
        p.Gold -= offer.Price;
        var item = p.Items.Last();
        Wishes.Fulfill(p, item.Item);
        var slot = item.Item.GetEquipmentSlotType();
        if (!p.Pinned && slot != EquipmentSlotType.NotEquipable && GearScore(item.Item) > GearScore(p.Wears(slot)?.Item)) p.Equip(item, false);
    }
    private static void SellSurplus(Companion owner, NeuralNPC merchant, Inventory till)
    {
        // Only replaced personal equipment is considered surplus. Loans and wish items stay with the NPC.
        var accepts = merchant.GetComponent<Vendor>()?.buyList;
        if (accepts == null || owner.Pinned) return;
        var spare = owner.Items.FirstOrDefault(e => e.Item != null && !e.Loan && e.Slot == 0 &&
            accepts.Contains(e.Item.itemCategory) && !Wishes.Match(owner, e.Item) &&
            e.Item.GetEquipmentSlotType() != EquipmentSlotType.NotEquipable &&
            owner.Wears(e.Item.GetEquipmentSlotType()) is PackEntry worn && GearScore(worn.Item) > GearScore(e.Item));
        if (spare == null || !till.CanAddItem(spare.Item)) return;
        float rate = (InventoryUI.Instance ? InventoryUI.Instance.sellValueMultiplier : .5f) + Stats.Read(owner).Sell;
        int price = Mathf.RoundToInt(spare.Item.GetFinalValue() * Mathf.Max(0, rate));
        if (price <= 0 || price > till.gold || owner.Gold > int.MaxValue - price || !spare.Item.TryMoveItemToInventory(till)) return;
        spare.Item.AddItemComponent(new ItemComponent_VendorItem(merchant.npcName));
        owner.Items.Remove(spare);
        till.gold -= price;
        owner.Gold += price;
    }
    private static float Score(Companion p, Item i)
    {
        var slot = i.GetEquipmentSlotType();
        var matching = p.Items.Where(e => e.Sprite == i.spriteName && e.Name == i.name &&
            (e.Item?.GetItemComponent<ItemComponent_Charges>()?.HasCharge() ?? true)).ToList();
        if (matching.Count > 0 && (p.Pinned || slot == EquipmentSlotType.NotEquipable ||
            matching.Any(e => e.Item == null || GearScore(e.Item) >= GearScore(i)))) return 0;
        if (Supplies.Has<ItemComponent_LanternOil>(i))
        {
            var lamp = p.Items.FirstOrDefault(e => Supplies.Has<ItemComponent_Lantern>(e.Item));
            var fuel = lamp?.Item.GetItemComponent<ItemComponent_Durability>();
            if (fuel != null && fuel.Durability <= .3f * fuel.maxDurability) return 95;
        }
        float wish = Wishes.Match(p, i) ? 85 : 0;
        if (Supplies.Has<ItemComponent_Charges>(i) && p.Items.Any(e =>
            Fields.Read<string>(e.Item?.GetItemComponent<ItemComponent_RangedWeapon>(), "ammunitionItemName") == i.name))
            return Math.Max(wish, 72);
        if (!p.Pinned && Supplies.Has<ItemComponent_RangedWeapon>(i) &&
            !p.Items.Any(e => Supplies.Has<ItemComponent_RangedWeapon>(e.Item))) return Math.Max(wish, 45);
        if (Supplies.Healing(i) && !p.Items.Any(e => Supplies.Healing(e.Item))) return Math.Max(wish, 80);
        if (Supplies.Has<ItemComponent_Edible>(i) && !p.Items.Any(e => Supplies.Has<ItemComponent_Edible>(e.Item))) return Math.Max(wish, 65);
        if (Supplies.Has<ItemComponent_Lantern>(i) && !p.Items.Any(e => Supplies.Has<ItemComponent_Lantern>(e.Item)) &&
            !Stats.Read(p).NightSight && !Stats.Read(p).Glow) return Math.Max(wish, 45);
        if (!p.Pinned && slot != EquipmentSlotType.NotEquipable)
        {
            float gain = GearScore(i) - GearScore(p.Wears(slot)?.Item);
            if (gain > 0) return Math.Max(wish, 40 + Math.Min(40, gain));
        }
        return wish;
    }
    internal static float GearScore(Item i)
    {
        if (i == null) return 0;
        var s = new Stats(); s.Add(i);
        object w = (object)i.GetItemComponent<ItemComponent_MeleeWeapon>() ?? i.GetItemComponent<ItemComponent_RangedWeapon>();
        return 1 + s.Armor.Sum() + .2f * (s.MaxHP - 100) + .05f * (s.MaxEnergy - 100 + s.Capacity - 40) +
            10 * (s.Crit + s.Parry + s.Regen + s.Speed + s.Reflect + s.Potency - 1 + s.Preserve) +
            (Fields.Read<int>(w, "minDamage") + Fields.Read<int>(w, "maxDamage")) * .5f * (1 + (w is IQualityItemComponent q ? (int)q.Quality * .5f : 0));
    }
}
