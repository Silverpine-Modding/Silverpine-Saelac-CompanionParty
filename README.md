# Companion Party 0.2.0

A BepInEx 5 plugin for Silverpine 1.7.3. It uses Silverpine Modding Tools 1.13.0 or later for per-save storage, conversation actions, and prompt integration. Custom Item Loader, Additional Equipment Slots, and Attribute Expansion are optional.

Created by **Saelac and ChatGPT**. [Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-CompanionParty/releases/latest)

## Installation

Install BepInEx 5 and Silverpine Modding Tools 1.13.0 or later. With Silverpine closed, extract `CompanionParty-0.2.0.zip` into the game directory. For a manual installation, place `CompanionParty.dll` in `BepInEx/plugins/CompanionParty/`. [Silverpine Modding Tools releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest)

## Playing

Talk to a nearby NPC and open the conversation's pop-out action list, the one containing **Gift an item** and **Gift gold**. Choose **Travel with me** to recruit them. During play, click a follower’s name and health bar on the left to open their **Orders**. The **Party controls** button above the bars opens the full roster. You can also reach the roster through **Inventory → Mods → Party controls**.

The party HUD sits beneath game menus. The debug menu takes priority over party controls wherever they overlap. Closing it lets you continue in the same party window.

**Manage gear** opens two independently scrolling packs, with the follower’s belongings on the left and yours on the right. Racial traits still affect the wearer’s stats and stay out of both packs. Use **Equipment** to inspect their loadout and **Orders** to direct them. **Close** returns to the conversation. Exchanges and orders require proximity.

**Guard me** lets a follower fight on their own. A nearby enemy that hits them takes priority over your target. Once that threat is gone, they can assist you or engage another nearby hostile. **Assist my target** focuses the follower on the enemy you most recently attacked, while that enemy is alive and within reach. Both orders use the game’s combat turns. **Return to your routine** dismisses the companion and keeps their personal belongings for your next meeting.

When you cross into another overland world tile, awake, standing party members travelling with you arrive on nearby open tiles in the same crossing. If the entrance is crowded and offers no safe space, the remaining followers keep using their normal pathfinding.

**Give** transfers ownership. **Lend** lets you reclaim the exact item later. Use **Equip** in the pack to preview the change and confirm it on the equipment page. Choosing gear manually enables **Keep my equipment choices**. Clear that option in Orders to allow automatic upgrades again. **Gift gold** credits the recipient’s own purse, including before recruitment. Dialogue gifts use the same purse. A merchant uses their till for ordinary trade. The player can reclaim a loan from the pack, and a gift of coins or personal property can be returned through an agreed dialogue gift.

An essential companion goes down at zero HP. Enemies drop them as a target. **Help up** costs a player turn. A downed character also recovers to half health after at least twelve world turns once nearby threats have cleared. Recruiting and combat use ordinary NPC objects and the game’s turn system. Followers use carried healing potions, snacks and fueled lanterns. At low fuel they consume a bottle of oil if they have one.

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

## Build and verification

Clone the repository into `Plugin Development/CompanionParty` under your Silverpine installation. From the game directory, build with `dotnet build "Plugin Development/CompanionParty/CompanionParty.csproj" -c Release`. Install the resulting `CompanionParty.dll` in its own folder under `BepInEx/plugins`.

Run `python "Plugin Development/CompanionParty/Tests/run_smoke.py"` for the Windows integration checks. The script starts disposable Unity players with selected plugin combinations. A rendered check opens the party HUD, the native conversation’s **Manage gear** action, and the inventory Mods entry, and checks that the visible controls receive pointer clicks. All checks run without opening a player save.
