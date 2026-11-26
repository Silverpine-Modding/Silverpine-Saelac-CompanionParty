# Companion Party 0.2.10

A BepInEx 5 plugin for Silverpine 1.7.3. It uses Silverpine Modding Tools 1.13.0 or later for per-save storage, conversation actions, and prompt integration. Custom Item Loader, Additional Equipment Slots, and Attribute Expansion are optional.

Created by **Saelac and ChatGPT**. [Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-CompanionParty/releases/latest)

## Installation

Install BepInEx 5 and Silverpine Modding Tools 1.13.0 or later. With Silverpine closed, extract the Companion Party release ZIP into the game directory. For a manual installation, place `CompanionParty.dll` in `BepInEx/plugins/CompanionParty/`. [Silverpine Modding Tools releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest)

## Playing

Talk to a nearby NPC and open the conversation's pop-out action list, the one containing **Gift an item** and **Gift gold**. Choose **Travel with me** to recruit them. During play, click a follower’s name and health bar on the left to open their **Orders**. The **Party controls** button above the bars opens the full roster. You can also reach the roster through **Inventory → Mods → Party controls**.

The party HUD sits beneath game menus. The debug menu takes priority over party controls wherever they overlap. Closing it lets you continue in the same party window.

**Manage gear** opens two independently scrolling packs, with the follower’s belongings on the left and yours on the right. Racial traits still affect the wearer’s stats and stay out of both packs. Use **Equipment** to inspect their loadout and **Orders** to direct them. **Close** returns to the conversation. Exchanges and orders require proximity.

**Guard me** lets a follower fight on their own. A nearby enemy that hits them takes priority over your target. Once that threat is gone, they can assist you or engage another nearby hostile. **Assist my target** focuses the follower on the enemy you most recently attacked, while that enemy is alive and within reach. Both orders use the game’s combat turns. **Return to your routine** dismisses the companion and keeps their personal belongings for your next meeting.

When you cross into another overland world tile, awake, standing party members travelling with you arrive on nearby open tiles in the same crossing. If the entrance is crowded and offers no safe space, the remaining followers keep using their normal pathfinding.

**Give** transfers ownership. **Lend** lets you reclaim the exact item later. Use **Equip** in the pack to preview the change and confirm it on the equipment page. Choosing gear manually enables **Keep my equipment choices**. Clear that option in Orders to allow automatic upgrades again. **Gift gold** credits the recipient’s own purse, including before recruitment. Dialogue gifts use the same purse. A merchant uses their till for ordinary trade. The player can reclaim a loan from the pack, and a gift of coins or personal property can be returned through an agreed dialogue gift.

An essential companion goes down at zero HP. Enemies drop them as a target. **Help up** costs a player turn. A downed character also recovers to half health after at least twelve world turns once nearby threats have cleared. Recruiting and combat use ordinary NPC objects and the game’s turn system. Followers use carried healing potions, snacks and fueled lanterns. At low fuel they consume a bottle of oil if they have one.

Each dialogue prompt describes the speaker’s current wounds, incapacitation, or recent recovery. A companion can recognize who attacked them, whom they are fighting, and whether the player helped them up. Recovery and combat details expire as the scene changes. This uses the existing dialogue request.

Open **Inventory → Mods → Party controls** to toggle **NPC equipment in dialogue**. It defaults to on. The speaker knows their own weapon and worn gear, and can see what nearby NPCs have equipped. Each description names the wearer and uses their pronouns. Native clothing details, registered custom slots, and visible clothing details from the optional Revealing mod are supported. Carried items and gold belong to the speaker’s private pack. The toggle applies to the next prompt and is saved as `[Dialogue] IncludeNpcEquipment` in `BepInEx/config/silverpine.companionparty.cfg`.

The ten base-game residents start with personal outfits based on their portraits. Each spawned custom NPC receives Silverpine’s **Linen Shirt** and **Linen Pants** once per save, including inactive residents and those in extended NPC slots. The clothes belong to the character by their custom content name, so their pack follows them if the game reassigns their slot. When upgrading a save, a resident who already has a managed outfit keeps wearing it and receives the starting clothes in their pack.

| Resident | Starting equipment |
| --- | --- |
| Acacia | Patterned Woven Top, Fringed Hide Wrap |
| Aldric | Merchant's Green Vest, Merchant's Breeches |
| Celandine | Corded Ivory Dress |
| Darian | Belted Trail Tunic, Trail Trousers and Gaiters, Belt Sword |
| Gareth | Tavern Keeper's Apron, Tavern Keeper's Breeches |
| Hayden | Baker's Apron and Shirt, Baker's Cuffed Trousers |
| Mirel | Blue-Embroidered Blouse, Tied Hip Wrap |
| Oriana | Laced Cream Gown |
| Orson | Archer's Blue Tunic, Archer's Field Trousers, Bow, Quiver |
| Rosalyn | Apothecary's Sashed Dress |

Chest and waist coverage follow the player’s equipment rules. A robe can cover both areas, underlayers count when Additional Equipment Slots is installed, and Revealing’s item settings take precedence for that garment. Removing the last covering layer selects the NPC’s alternate dialogue portrait when available. Linked playerSILC characters can also use their own alternate map sprite. The dialogue toggle controls whether speakers describe coverage and visible clothing. Changing equipment and artwork uses no model calls.

When an NPC undresses in conversation, they put their equipped items in their personal pack and remember each item's slot. On redressing they put those same items back on, including borrowed and enchanted gear. The outfit survives saving, loading, and leaving the party. Racial fur and ordinary rings and necklaces stay equipped. If you reclaim a loan or equip a replacement, the NPC respects that change. An NPC first encountered undressed can put on their starting outfit. Older saves also recover the redress action for a custom NPC whose only possessions are their untouched, unworn starter linens. For other older outfits without a wear history, equip the desired items once.

Outside your party, residents use Silverpine's normal dialogue actions. When a new conversation scene begins, the game can reconsider the clothing of an undressed resident who has an alternate portrait. If it decides they got dressed between conversations, they put their remembered gear back on. In your party, actual equipment determines their appearance across scenes without a clothing-state model request. Recruiting or dismissing an NPC switches this behavior immediately.

## Shopping and wishes

An awake shopper who spends time within three tiles of an open, visible merchant in the same zone can make one buying decision per visit. This applies to custom routines and vendors using the native trade system. The buyer pays from their personal gold for a specific item of merchant stock. Meals, medicine, fuel, equipment upgrades, and personal interests influence the choice. A shopper can sell replaced, personally owned gear accepted by the merchant. Pinned gear and loans are protected.

For a storefront implemented outside Silverpine’s native trading system, register an `ICompanionShop` with `CompanionShops.Register`. It supplies opening state, stock, prices, and a sale operation. A successful sale moves the specified item into the supplied inventory and credits the seller. Companion Party debits the buyer once. A failed sale must leave both parties’ state intact. Guild integrations can use `PartyApi.Recruit`, `PartyApi.IsRecruited`, and `PartyApi.Dismiss`.

The plugin adds a short summary of the speaker’s personal pack, purse, gear and wishes to existing conversations. Wish generation uses the game’s configured model, character description, personality, and the NPC’s own knowledge. It runs while the game and model are idle. Set `AttemptsPerHalfHour` in `BepInEx/config/silverpine.companionparty.cfg` to change the shared limit (default 2, zero disables it). `MinimumDays` defaults to 3 per NPC. Failed attempts count. Wishes, fulfilled state and cooldowns are saved. Shopping, combat and item use run locally.

## Equipment and saves

### Guild expeditions

Adventurers’ Guild can temporarily assign a resident to fieldwork without recruiting them into the player's party. In an active quest area the resident can fight with personal gear and use essential health. Their guild pay goes into the same personal purse and pack used for gifts, shopping, and later recruitment. Vendor restocks affect their shop stock separately. A resident can spend earned gold between adventures. Repeat delivery of the same guild reward is recognized by its saved receipt.

Other expedition integrations can call `ExpeditionApi.Register` with assignment and live-combat predicates, `Prepare` to bind a character, and `Pay` with a stable reward ID and optional base64 native item serialization. `Unavailable`, `IsDown`, and `Assist` expose the recruitment and rescue checks. The guild calls these methods through its optional integration.

Armor uses damage type and quality. The equipment page displays the wearer’s stats. Supported playerSILC traits include Hollow Bones, Thick Skin, Fur, Large Claws, Night Vision, Bioluminescence, Slow and Cold Weakness. Custom Item Loader gear uses its registered slot and basic modifiers. Additional Equipment Slots supplies its live slot IDs. Attribute Expansion adds critical chance, parry, reflection, potion potency, sale and relationship bonuses, durability preservation, regeneration and movement bonuses.

Personal packs use the Modding Tools companion file alongside the ordinary game save. Keep the pair together when moving a save. Missing content retains its serialized item and assignment. Restoring the provider permits the item to load again with its saved durability. A vendor’s daily restock operates on their shop inventory.

## Skill API

Version 0.2.6 adds `PartySkillApi.PlayerActivity`. Subscribe an `Action<Player, PartyActivity, IReadOnlyList<NeuralNPC>>` to observe a completed overworld crossing with at least one arriving recruit (`TravelledTogether`) or a recruited companion the player helped up (`HelpedUp`). A crossing emits a single event for the whole travelling group. Each event supplies a read-only snapshot of the participating NPCs.

A skills mod can register a callback with `CompanionParty.PartySkillApi` to grant benefits to recruited NPCs. The callback receives the current `Player` and the individual `NeuralNPC`, including custom NPCs. Return `default` for a follower who does not qualify. Each provider's values add to gear, racial traits, and other providers. Guild assignments qualify only while the NPC is also recruited.

Reference `CompanionParty.dll` (0.2.5 or later) and the game's `Assembly-CSharp.dll` in the integration. The Companion Party BepInEx ID is `silverpine.companionparty`. The example takes the skill mod's own rank lookup and returns a registration to keep until unload.

```csharp
using System;
using CompanionParty;

public static class LeadershipIntegration
{
    public static IDisposable Connect(Func<Player, int> leadershipRank)
    {
        return PartySkillApi.Register("example.skills.leadership", (player, follower) =>
        {
            int rank = leadershipRank(player);
            return new PartySkillBonuses
            {
                MaxHealth = 5 * rank,
                CarryCapacity = 2 * rank,
                MeleeParryChance = 0.01f * rank
            };
        });
    }
}
```

| Bonus fields | Units and effect |
| --- | --- |
| `MaxHealth`, `MaxEnergy`, `CarryCapacity` | Extra points. Raising a maximum preserves current HP or energy. Lowering it clamps the current value. |
| `AttackDamage` | Flat damage per melee, unarmed, or ranged hit, before fatigue, melee criticals, and the target's armor. |
| `NormalArmor`, `FireArmor`, `FrostArmor` | Armor points against the matching damage type. |
| `HealthRegenerationPerTurn` | Additional HP per world turn for standing companions. Fractions give a chance to recover one extra HP. |
| `PotionPotencyBonus` | Added to the healing potion multiplier. A value of 0.25 adds 25 percentage points. |
| `MeleeCriticalChance`, `MeleeParryChance`, `DurabilityPreservationChance`, `ExtraStepChance` | Additional chance as a fraction. A value of 0.05 adds five percentage points. |

Use a unique provider ID and register once when your skill data is ready. Call `PartySkillApi.Refresh()` after a rank change, respec, or skill-save load. Recruitment, companion save loading, registration, and disposal also update the benefits. Combat, HUD updates, and gear previews reuse the calculated values. Dispose the handle on unload. Make these calls on Unity's main thread and keep the callback read-only. Negative and non-finite bonus inputs contribute zero. A throwing callback contributes zero for that NPC, logs its first failure, and can recover on the next refresh.

## Build and verification

Clone the repository into `Plugin Development/CompanionParty` under your Silverpine installation. From the game directory, build with `dotnet build "Plugin Development/CompanionParty/CompanionParty.csproj" -c Release`. Install the resulting `CompanionParty.dll` in its own folder under `BepInEx/plugins`.

Run `python "Plugin Development/CompanionParty/Tests/run_smoke.py"` for the Windows integration checks. The script starts disposable Unity players with selected plugin combinations. A rendered check opens the party HUD, the native conversation’s **Manage gear** action, and the inventory Mods entry, and checks that the visible controls receive pointer clicks. All checks run without opening a player save.
