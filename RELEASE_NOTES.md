# Companion Party 0.2.10

Residents outside the player's party follow Silverpine's normal clothing decisions in individual and group conversations. Their real equipment follows the result. Party members continue to use their equipped coverage across scenes, including when recruitment happens during a pending scene check.

## Companion Party 0.2.9

Starter linen now reaches inactive custom residents and characters awaiting slot registration. The plugin retries as names and item templates become available. Custom residents in older saves who carry only their untouched, unworn starter shirt and pants can put them on through dialogue.

## Companion Party 0.2.8

NPCs can undress and redress during normal dialogue. Their personal pack remembers the original slot of each item they take off, including custom equipment. Redressing restores the same items across save and load and respects later gear changes and returned loans.

## Companion Party 0.2.7

NPCs keep complete wish entries when the model stops before the end of its JSON reply. A reply with no usable entry leaves prior wishes in place. An explicit empty list still clears them. Recovery uses the original reply and its existing token budget.

## Companion Party 0.2.6

Skills mods can now observe completed party journeys and player-assisted revivals through `PartySkillApi.PlayerActivity`.

## Companion Party 0.2.5

Skills mods can use `PartySkillApi.Register` to grant individual recruited companions combat, recovery, and carrying bonuses. Rank changes can refresh the party immediately. Dismissing a follower or unloading a provider removes its bonuses. The README includes the supported fields and a C# integration example.

Aldric now starts with a Merchant's Green Vest and Merchant's Breeches, matching his portrait and his role as the town trader. Version 0.2.3 saves update his two garment names on load. Hammers already in his possession are kept because that version stored starter, purchased, and gifted hammers as the same item.

All ten base-game residents receive personal starting outfits based on the in-game portraits. Custom NPCs receive the player's native Linen Shirt and Linen Pants. Starter items persist like other possessions. Existing outfits, loans, and vendor stock are preserved.

NPC equipment now determines chest and waist coverage, including optional underlayers and Revealing overrides. Dialogue can describe partial coverage, and a fully uncovered NPC uses their alternate portrait or linked playerSILC map sprite where available. Changing outfits requires no model request.

**NPC equipment in dialogue** is a saved toggle in Party controls. When enabled, the model receives the speaker’s live loadout and the gear of NPCs visible in the scene. Weapon names, clothing details, and custom slot labels are attributed to the wearer.

Dialogue now reflects a companion’s injuries, active opponent, incapacitation, and recent recovery. A companion helped up by the player can acknowledge it in the scene. The plugin adds this information when Silverpine builds the next dialogue prompt, with no additional model calls.

Recruit residents through conversation, give combat orders, trade or lend equipment, and manage each companion’s personal pack and purse. Those belongings persist through dismissal, save/load, and later recruitment.

The optional Adventurers’ Guild integration lets a resident fight during a quest and receive the contract reward in their own inventory. A saved receipt prevents a second payment for the same contract. [Adventurers’ Guild releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-AdventurersGuild/releases/latest)

Requires Silverpine with BepInEx 5 and Silverpine Modding Tools 1.13.0 or later. Extract the release ZIP into the game directory or place the standalone DLL in `BepInEx/plugins/CompanionParty/`. [Modding Tools releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest)
