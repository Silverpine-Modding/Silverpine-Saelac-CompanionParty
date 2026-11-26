using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

namespace CompanionParty;

[BepInPlugin(Id, "Companion Party", Version)]
[BepInDependency("Saelac.Silverpine.ModdingTools", "1.13.0")]
[BepInDependency(GearMods.Loader, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(GearMods.Expansion, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("renegadex.silverpine.additionalequipmentslots", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(GearMods.Revealing, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string Id = "silverpine.companionparty";
    internal const string Version = "0.2.10";
    internal static ManualLogSource Log;
    internal static ConfigEntry<int> Budget, RefreshDays;
    internal static ConfigEntry<bool> EquipmentInDialogue;
    internal static bool Ready => !SerializationManager.loadingSave && Player.Instance && WorldInfoManager.Instance && ActionQueue.Instance &&
        DialogBox.Instance && PauseMenuManager.Instance && Player.Instance.worldInventory != null &&
        NeuralNPC.neuralNPCs != null && !MainMenuUIActive();
    private static bool MainMenuUIActive() => MainMenuUI.Instance && MainMenuUI.Instance.IsOpen();
    private void Awake()
    {
        Log = Logger;
        EquipmentInDialogue = Config.Bind("Dialogue", "IncludeNpcEquipment", true,
            "Include worn equipment and clothing coverage for the speaker and NPCs they can see in dialogue prompts.");
        Budget = Config.Bind("Wishes", "AttemptsPerHalfHour", 2, new ConfigDescription("Shared attempts per 30 minutes of play. Zero disables wish generation.", new AcceptableValueRange<int>(0, 12)));
        RefreshDays = Config.Bind("Wishes", "MinimumDays", 3, new ConfigDescription("Minimum days between attempts for a character.", new AcceptableValueRange<int>(1, 90)));
        GearMods.Init();
        ModSaveData.Register(Id, new ModSaveDataDefinition { Id = Id + ".party", Capture = State.Capture, Restore = State.Restore, Reset = State.Reset });
        var harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        AddAction("recruit", "Travel with me", n => !PartyApi.IsRecruited(n) && !ExpeditionApi.Assigned(n), n =>
        {
            if (PartyApi.Recruit(n)) { DialogBox.Instance.EndDialog(); Notice(n.GetFinalName() + " joined your party."); }
        });
        AddAction("dismiss", "Return to your routine", PartyApi.IsRecruited, n => { PartyApi.Dismiss(n); DialogBox.Instance.EndDialog(); });
        AddAction("pack", "Manage gear", n => PartyApi.IsRecruited(n), n => PartyUI.Open(n));
        AddAction("giveitem", "Gift an item", null, n => PartyUI.Open(n, true), "Give Item");
        AddAction("givegold", "Gift gold", null, GiftGold, "Give Gold");
        DialoguePromptTransforms.Register(Id, new DialoguePromptTransformDefinition
        {
            Id = Id + ".pack-context",
            TransformText = (context, section, text) => section == DialoguePromptTextSection.Environment
                ? DialogueScene.Append(context.Npc, text) : text
        });
        InventoryModTools.RegisterSession(Id + ".controls", "Party controls",
            (_, session) => PartyUI.OpenRoster(session), order: 150);
        Log.LogInfo($"Companion Party {Version} registered party, gifts, equipment, shopping and essential combat.");
    }
    internal static void Notice(string text) { if (UpperNotificationUI.Instance) UpperNotificationUI.Instance.OneOff(text); }
    private static void AddAction(string id, string label, Func<NeuralNPC, bool> show, Action<NeuralNPC> select, string replaces = "")
    {
        DialogueActions.Register(Id, new DialogueActionDefinition
        {
            Id = Id + "." + id,
            Label = label,
            ReplacesNativeLabel = replaces,
            Order = 150,
            IsVisible = c => c.Npc && Ready && (show == null || show(c.Npc)),
            OnSelected = c => { if (c.Npc && Ready) select(c.Npc); }
        });
    }
    internal static void GiftGold(NeuralNPC npc)
    {
        int epoch = State.Epoch;
        PayUI.Instance.Open(amount =>
        {
            if (epoch == State.Epoch) TryGiftGold(npc, amount);
        });
    }
    internal static bool TryGiftGold(NeuralNPC npc, int amount)
    {
        if (!npc || !Player.Instance || !State.Near(npc, Player.Instance.transform, 3)) return false;
        var p = State.Get(npc);
        var wallet = Player.Instance.worldInventory.inventory;
        if (amount <= 0 || amount > wallet.gold || amount > int.MaxValue - p.Gold) return false;
        wallet.gold -= amount; p.Gold += amount;
        if (AnimationHelper.Instance) AnimationHelper.Instance.SpawnFloatingGoldText(-amount, Player.Instance.transform);
        npc.QuietlyAddSystemDialog($"{Player.Instance.playerName} gave {p.Name} {amount} gold for their own purse.");
        Notice($"{p.Name} received {amount} gold.");
        return true;
    }
}

internal sealed class PartyRuntime : MonoBehaviour
{
    private static PartyRuntime current;
    internal static PartyRuntime Ensure(Player player)
    {
        if (!player || player != Player.Instance) return null;
        if (current && current.gameObject == player.gameObject) return current;
        if (current) { current.enabled = false; PartyUI.Release(); Destroy(current); }
        current = player.GetComponent<PartyRuntime>() ?? player.gameObject.AddComponent<PartyRuntime>();
        return current;
    }
    private ActionQueue subscribed;
    private float nextWishCheck, nextRestore, nextDisplay;
    private void Update()
    {
        if (subscribed != ActionQueue.Instance)
        {
            if (subscribed) subscribed.OnPostProcessTurn -= OnTurn;
            subscribed = ActionQueue.Instance;
            if (subscribed) subscribed.OnPostProcessTurn += OnTurn;
        }
        if (!Plugin.Ready) { PartyUI.Release(); return; }
        if (Time.unscaledTime >= nextDisplay)
        {
            nextDisplay = Time.unscaledTime + .15f;
            PartyUI.Refresh();
        }
        if (!PauseMenuManager.Instance.open)
        {
            double delta = Math.Min(.5, Time.unscaledDeltaTime);
            Wishes.SessionSeconds += delta;
            State.Save.PlaySeconds += delta;
        }
        if (Time.unscaledTime >= nextRestore)
        {
            nextRestore = Time.unscaledTime + 2;
            State.EnsureCustomResidents();
            foreach (var p in State.Save.People.Values)
                if (p.Items.Any(e => e.Item == null && !p.Failed.Contains(e))) p.Rebind = true;
            foreach (var npc in NeuralNPC.neuralNPCs.Values.ToArray())
                if (npc && npc.isActiveAndEnabled && State.Find(npc, out var p) && p.Recruited && p.Actor.GetComponent<NPCRoutineExecutor>()?.currentRoutine != null)
                    PartyApi.EnsureRoutine(p);
        }
        if (Time.unscaledTime >= nextWishCheck && !subscribed.turnBeingProcessed && !subscribed.processTurnRequested)
        {
            nextWishCheck = Time.unscaledTime + 10;
            Wishes.Schedule();
        }
        if (PartyUI.Opened) PartyUI.HandleEscape(Input.GetKeyDown(KeyCode.Escape));
    }
    private static void OnTurn()
    {
        if (!Plugin.Ready) return;
        var npcs = NeuralNPC.neuralNPCs.Values.Where(n => n && n.isActiveAndEnabled).Distinct().ToList();
        var vendors = new List<NeuralNPC>();
        var providers = new Dictionary<NeuralNPC, ICompanionShop>();
        foreach (var n in npcs)
        {
            var provider = CompanionShops.Provider(n);
            if (provider != null) providers[n] = provider;
            if (provider != null || n.IsVendorActive()) vendors.Add(n);
        }
        foreach (var npc in npcs)
        {
            if (State.Find(npc, out var p)) Supplies.Tick(p);
            if (!ExpeditionApi.Assigned(npc)) Market.Visit(npc, vendors, providers);
        }
    }
    private void OnDestroy()
    {
        if (subscribed) subscribed.OnPostProcessTurn -= OnTurn;
        if (ReferenceEquals(this, current)) { current = null; PartyUI.Release(); }
    }
}

// Silverpine can destroy bootstrap objects, including DontDestroyOnLoad hosts.
[HarmonyPatch(typeof(Player), "Update")]
internal static class GameplayHost
{
    static void Postfix(Player __instance) => PartyRuntime.Ensure(__instance);
}

[HarmonyPatch(typeof(Player), nameof(Player.IsInputBlocked))]
internal static class InputPatch { static void Postfix(ref bool __result) { if (PartyUI.Opened) __result = true; } }

// A conversation also polls Return and the mouse wheel outside Unity's UI events.
[HarmonyPatch(typeof(DialogBox), "Update")]
internal static class PartyDialogInput
{
    static bool Prefix(DialogBox __instance) => !PartyUI.Covers(__instance);
}

[HarmonyPatch(typeof(NeuralNPC), "OnInputCallback")]
internal static class CasualMeal
{
    static void Prefix(NeuralNPC __instance)
    {
        if (!Plugin.Ready || !State.Find(__instance, out var p) || p.LastSnackTurn == Combat.Turn || p.Down) return;
        p.LastSnackTurn = Combat.Turn;
        if (UnityEngine.Random.value < .22f) Supplies.Snack(p, true);
    }
}
