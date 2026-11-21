using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CompanionParty.Checks;

// Loaded only by the disposable batch-mode player, after the production plugin's Awake.
[BepInPlugin("silverpine.companionparty.checks", "Companion Party checks", "0.1.0")]
[BepInDependency(Plugin.Id)]
[BepInDependency("salt.silverpine.extradebug", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("salt.silverpine.dialogueimpersonator", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Checks : BaseUnityPlugin
{
    private readonly List<string> passed = new();
    private bool rendered;
    private void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); passed.Add(name); }
    private static GameObject Inactive(string name) { var go = new GameObject(name); go.SetActive(false); return go; }
    private static bool SkipCoinAnimation() => false;
    private void CheckConversation(NeuralNPC actor, Companion person, Inventory till)
    {
        ActionQueue.Instance = Inactive("test-turns").AddComponent<ActionQueue>();
        PauseMenuManager.Instance = Inactive("test-pause").AddComponent<PauseMenuManager>();
        PauseMenuManager.Instance.open = false;
        DebugUI.Instance = Inactive("test-debug-menu").AddComponent<DebugUI>();
        SettingsUI.Instance = Inactive("test-language").AddComponent<SettingsUI>();
        InferenceServerSetupHandler.Instance = Inactive("test-offline").AddComponent<InferenceServerSetupHandler>();
        AnimationHelper.Instance = Inactive("test-floater").AddComponent<AnimationHelper>();
        new Harmony("silverpine.companionparty.checks.visuals").Patch(
            AccessTools.Method(typeof(AnimationHelper), nameof(AnimationHelper.SpawnFloatingGoldText)),
            prefix: new HarmonyMethod(typeof(Checks), nameof(SkipCoinAnimation)));
        var box = Inactive("test-conversation").AddComponent<DialogBox>();
        var fields = Traverse.Create(box);
        var buttons = new GameObject("actions", typeof(RectTransform), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
        var root = new GameObject("conversation panel", typeof(RectTransform));
        buttons.transform.SetParent(root.transform, false);
        var prefab = new GameObject("dialogue choice", typeof(RectTransform), typeof(Button));
        new GameObject("label", typeof(RectTransform), typeof(TextMeshProUGUI)).transform.SetParent(prefab.transform, false);
        fields.Field("buttonOptionsLayoutGroup").SetValue(buttons.GetComponent<VerticalLayoutGroup>());
        fields.Field("upperButtonPrefab").SetValue(prefab);
        foreach (string field in new[] { "popOutButton", "pageUpArrow", "pageDownArrow", "talkButton" })
            fields.Field(field).SetValue(new GameObject(field, typeof(RectTransform), typeof(Button)).GetComponent<Button>());
        var group = box.gameObject.AddComponent<CanvasGroup>();
        fields.Field("globalCanvasGroup").SetValue(group);
        var input = new GameObject("input", typeof(RectTransform), typeof(TMP_InputField)).GetComponent<TMP_InputField>();
        fields.Field("inputField").SetValue(input);
        box.gameObject.SetActive(true);
        NeuralNPC.currentActiveDialogNeuralNPC = actor;
        var draw = AccessTools.Method(typeof(DialogBox), "DrawUpperButtons");
        var choices = new List<UpperButtonOption>();
        draw.Invoke(box, new object[] { choices });
        var labels = buttons.GetComponentsInChildren<Button>().Where(b => b.interactable)
            .Select(b => b.GetComponentInChildren<TextMeshProUGUI>().text).ToList();
        Check(labels.Count(s => s == "Travel with me") == 1 && !labels.Contains("Return to your routine"),
            "Native conversation draws one clickable Travel with me button for an unrecruited NPC");
        Check(labels.Contains("Gift gold") && labels.Contains("Gift an item"), "Personal gift controls are visible in the conversation");
        // Recruit through the registered conversation action, with a live NPC
        // routine and the same player/zone data used by the game.
        var hero = Player.Instance;
        hero.playerName = "Tester";
        hero.gameObject.AddComponent<SpriteRenderer>();
        hero.gameObject.AddComponent<Damagable>();
        hero.gameObject.SetActive(true);
        var zone = Inactive("party-test-room").AddComponent<MapZone>();
        zone.zoneName = "Test room";
        Traverse.Create(typeof(MapZone)).Field("registered").GetValue<Dictionary<Vector2Int, MapZone>>()[Vector2Int.zero] = zone;
        actor.gameObject.AddComponent<SpriteRenderer>();
        actor.gameObject.AddComponent<EntityMover>();
        actor.gameObject.AddComponent<PathfindingEntity>();
        var routine = actor.gameObject.AddComponent<NPCRoutineExecutor>();
        routine.currentRoutine = new NPCRoutine("working", "working", "working", Vector2Int.zero);
        actor.gameObject.SetActive(true);
        // The synthetic dialog has no scene portraits to fade on close.
        new Harmony("silverpine.companionparty.checks.close-visuals").Patch(AccessTools.Method(typeof(DialogBox), nameof(DialogBox.CloseBox)),
            prefix: new HarmonyMethod(typeof(Checks), nameof(SkipCoinAnimation)));
        choices.Single(c => c.label == "Travel with me").callback();
        Check(person.Recruited && routine.currentRoutine.id == PartyApi.FollowId, "Travel with me recruits the real NPC into the party");
        draw.Invoke(box, new object[] { choices });
        Check(choices.Any(c => c.label == "Manage gear") && choices.Any(c => c.label == "Return to your routine") &&
            choices.All(c => c.label != "Travel with me"), "Recruited conversation offers gear and dismissal");
        CheckPartyControls(choices, box, actor, person);
        person.Recruited = false;
        // Seed an earlier English line to use the native offline payment path.
        actor.dialogElements.Add(new NeuralNPC.DialogElement { speakerType = SpeakerType.Player, contents = "Good morning." });
        var wallet = Player.Instance.worldInventory.inventory;
        wallet.gold = 100;
        input.SetTextWithoutNotify("*gives you 12 gold as a gift*");
        box.SendInput();
        Check(wallet.gold == 88 && person.Gold == 12 && till.gold == 320, "Native typed gift charges player and credits private purse");
        input.SetTextWithoutNotify("*pays 5 gold for the room*");
        box.SendInput();
        Check(wallet.gold == 83 && person.Gold == 12 && till.gold == 325, "Native typed payment credits shop till");
        if (File.Exists(Path.Combine(Paths.PluginPath, "SaltExtraDebug.dll")))
        {
            var debug = Chainloader.PluginInfos["salt.silverpine.extradebug"].Instance;
            var goldGuard = (BepInEx.Configuration.ConfigEntry<bool>)AccessTools.Field(debug.GetType(), "NoGoldDecreaseEnabled").GetValue(null);
            goldGuard.Value = true;
            input.SetTextWithoutNotify("*gives you 2 gold as a gift*");
            box.SendInput();
            Check(wallet.gold == 83 && person.Gold == 14 && till.gold == 325, "Extra Debug protects player gold while typed gifts still credit the NPC");
            goldGuard.Value = false;
        }
        person.Gold = 0; till.gold = 320;
    }
    private static Button PartyButton(string name) => GameObject.Find("Companion Party UI")
        .GetComponentsInChildren<Button>().Single(b => b.name == name);
    private void ClickPartyButton(string name)
    {
        Canvas.ForceUpdateCanvases();
        var b = PartyButton(name);
        var rect = (RectTransform)b.transform;
        var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        bool hit = hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>() == b;
        // In the pre-scene offline checks Unity has performed no graphic draw yet.
        // The separate rendered player requires an actual frontmost raycast.
        if (!rendered && b.targetGraphic.depth == -1)
            hit = rect.rect.width > 0 && rect.rect.height > 0 && b.targetGraphic.raycastTarget &&
                b.targetGraphic.Raycast(point, null) && new Rect(0, 0, Screen.width, Screen.height).Contains(point);
        if (!b.IsInteractable() || !hit)
            throw new InvalidOperationException($"{name} failed pointer hit testing at {point}, rect {rect.rect}, screen {Screen.width}x{Screen.height}, canvas {b.GetComponentInParent<Canvas>().pixelRect}, graphic depth {b.targetGraphic.depth}, interactable {b.IsInteractable()}, hits {string.Join(", ", hits.Select(h => h.gameObject.name))}");
        passed.Add(name + (rendered ? " receives the screen raycast" : " accepts a pointer click"));
        ExecuteEvents.Execute(b.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    private void CheckMenuOnTop(Component menu, Graphic overlapping, RectTransform sample = null)
    {
        Canvas.ForceUpdateCanvases();
        var rect = sample ? sample : overlapping.rectTransform;
        var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        var top = hits.FirstOrDefault().gameObject;
        Check(top && top.transform.IsChildOf(menu.transform) && hits.Any(h => h.gameObject == overlapping.gameObject),
            $"{menu.name} receives the frontmost pointer hit over {overlapping.name} (front: {top?.name})");
    }
    private void CheckPartyControls(List<UpperButtonOption> choices, DialogBox box, NeuralNPC actor, Companion p)
    {
        if (!EventSystem.current) new GameObject("test-events", typeof(EventSystem));
        var initial = PartyRuntime.Ensure(Player.Instance);
        UnityEngine.Object.DestroyImmediate(initial);
        // A player tick restores its driver even if the earlier host was destroyed.
        AccessTools.Method(typeof(GameplayHost), "Postfix").Invoke(null, new object[] { Player.Instance });
        Check(Player.Instance.GetComponent<PartyRuntime>() && !initial, "Gameplay recreates the party UI driver after host destruction");
        box.isOpen = true;
        var sample = new Item("Travel coat", "ui_test_coat", "", 1, ItemCategory.Clothing, ItemSound.Cloth, 1f,
            new ItemComponent_Clothing(EquipmentSlotType.Chest, false, ItemComponent_Clothing.ClothingTier.Leather));
        var wallet = Player.Instance.worldInventory.inventory;
        wallet.TryAddItem(sample);
        var hidden = new Item("HIDDEN_Fur", "ui_test_fur", "", 0, ItemCategory.Clothing, ItemSound.Cloth, 0f);
        wallet.TryAddItem(hidden);
        var savedTrait = new PackEntry { Name = "HIDDEN_Fur" };
        p.Items.Add(savedTrait);
        choices.Single(c => c.label == "Manage gear").callback();
        Check(PartyUI.Opened && PartyButton("Equipment") && Player.Instance.IsInputBlocked(), "Manage gear opens over the conversation and blocks player input");
        Check(PaneContains("Player pack", "Travel coat") && PaneContains("Follower pack", "Empty") &&
            !PaneContains("Player pack", "HIDDEN_Fur") && !PaneContains("Follower pack", "HIDDEN_Fur") &&
            !p.Accept(hidden, true) && wallet.ContainsItem(hidden), "Each pack shows its owner's items and protects hidden racial items");
        ClickPartyButton("Lend");
        Check(PaneContains("Follower pack", "Travel coat (loan)") && PaneContains("Player pack", "Empty"), "Lending moves the item between the two displayed packs");
        ClickPartyButton("Equip"); ClickPartyButton("Equip Travel coat");
        Check(p.Wears(EquipmentSlotType.Chest)?.Item == sample, "Gear panel lends and equips the exact item");
        ClickPartyButton("Reclaim");
        Check(wallet.ContainsItem(sample) && p.Wears(EquipmentSlotType.Chest) == null && PaneContains("Player pack", "Travel coat"), "Panel returns a reclaimed loan to the player's side");
        sample.RemoveItemFromInventory(); hidden.RemoveItemFromInventory(); p.Items.Remove(savedTrait); p.Pinned = false;
        ClickPartyButton("Equipment");
        Check(GameObject.Find("Companion Party UI").GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text.StartsWith("Chest ")),
            "Equipment tab displays the follower's equipment slots");
        ClickPartyButton("Orders");
        ClickPartyButton("Assist my target"); Check(p.Order == "Assist", "Order click changes the real follower's behavior");
        ClickPartyButton("Close"); Check(!PartyUI.Opened && box.isOpen, "Closing Manage gear returns to the conversation");
        box.isOpen = false;
        PartyUI.Refresh();
        ClickPartyButton(actor.GetFinalName());
        ClickPartyButton("Guard me"); Check(p.Order == "Guard", "Left health frame opens the follower's orders");
        Player.Instance.worldInventory.inventory.gold = 20;
        PartyButton("Give gold").GetComponentInParent<Canvas>().GetComponentInChildren<TMP_InputField>().SetTextWithoutNotify("7");
        ClickPartyButton("Give gold");
        Check(p.Gold == 7 && Player.Instance.worldInventory.inventory.gold == 13 && PartyUI.Opened, "Party controls transfer gold with the order window open");
        p.Gold = 0;
        ClickPartyButton("Close"); PartyUI.Refresh();
        ClickPartyButton("Party controls");
        Check(PartyUI.Opened && GameObject.Find("Companion Party UI").GetComponentsInChildren<Button>().Any(b => b.name.StartsWith(p.Name + " ")),
            "Party controls lists the recruited NPC");
        ClickPartyButton("Close");
        bool released = false;
        var tool = (Silverpine.ModdingTools.ModToolSession)Activator.CreateInstance(typeof(Silverpine.ModdingTools.ModToolSession),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            new object[] { "party-test", (Action)(() => released = true) }, null);
        PauseMenuManager.Instance.open = true;
        PartyUI.OpenRoster(tool); ClickPartyButton("Close");
        Check(released && tool.IsClosed, "Closing the roster releases the inventory Mods session");
        PauseMenuManager.Instance.open = false;
    }
    private static bool PaneContains(string pane, string label) =>
        GameObject.Find(pane).GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text == label);
    private void Awake()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Paths.GameRootPath, "party-test-only"))) return;
        if (Environment.GetCommandLineArgs().Contains("-party-render-check"))
        {
            rendered = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += StartDisplayCheck;
            return;
        }
        string report = Path.Combine(Paths.GameRootPath, "party-checks.log");
        try
        {
            RunChecks();
            File.WriteAllLines(report + ".pending", passed.Concat(new[] { "PASS" }));
        }
        catch (Exception ex) { File.WriteAllLines(report + ".pending", passed.Concat(new[] { ex.ToString() })); }
        File.Move(report + ".pending", report);
        // The parent terminates only this disposable player after observing the report.
        System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
    }
    private void StartDisplayCheck(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= StartDisplayCheck;
        // Keep the actual scene's player, inventory and event system. No save is opened.
        var host = new GameObject("Party rendered checks").AddComponent<DisplayRunner>();
        host.StartCoroutine(RunDisplay());
    }
    private System.Collections.IEnumerator RunDisplay()
    {
        yield return null;
        yield return null;
        Exception failure = null;
        var sequence = DisplaySteps();
        while (true)
        {
            try { if (!sequence.MoveNext()) break; } catch (Exception ex) { failure = ex; break; }
            yield return sequence.Current;
        }
        string report = Path.Combine(Paths.GameRootPath, "party-checks.log");
        File.WriteAllLines(report + ".pending", passed.Concat(new[] { failure?.ToString() ?? "PASS" }));
        File.Move(report + ".pending", report);
    }
    private System.Collections.IEnumerator DisplaySteps()
    {
        var end = Time.realtimeSinceStartup + 30;
        while ((!Player.Instance || !MainMenuUI.Instance || !DialogBox.Instance || !ActionQueue.Instance || !EventSystem.current)
               && Time.realtimeSinceStartup < end) yield return null;
        Check(Player.Instance && MainMenuUI.Instance && DialogBox.Instance && EventSystem.current, "Native scene and UI event system loaded");
        MainMenuUI.Instance.Close();
        Check(Plugin.Ready, "Native player can display the party");
        var actor = NeuralNPC.neuralNPCs.Values.First(n => n && n.isActiveAndEnabled);
        var p = State.Get(actor); p.Recruited = true; p.HP = p.LastMaxHP / 2;
        var player = Player.Instance;
        var old = PartyRuntime.Ensure(player);
        UnityEngine.Object.Destroy(old);
        yield return null;
        if (PauseMenuManager.Instance.open) PauseMenuManager.Instance.Close();
        end = Time.realtimeSinceStartup + 4;
        while (!GameObject.Find("Party HUD") && Time.realtimeSinceStartup < end) yield return null;
        yield return null;
        Check(!old && player.GetComponent<PartyRuntime>() && GameObject.Find("Party HUD"), "World updates restore the rendered party HUD");
        if (BlackScreen.Instance.IsActive())
        {
            CheckMenuOnTop(BlackScreen.Instance, PartyButton(p.Name).targetGraphic);
            yield return BlackScreen.Instance.FadeOutCoroutine(fadeAudio: false);
        }
        DebugUI.Instance.ShowDebugOptions();
        yield return null; yield return null;
        var hudGraphic = PartyButton(p.Name).targetGraphic;
        Check(hudGraphic.canvas.sortingOrder < GenericListUI.Instance.GetComponentInParent<Canvas>().sortingOrder,
            "The debug menu renders on a higher canvas than the party HUD");
        if (Chainloader.PluginInfos.ContainsKey("salt.silverpine.extradebug"))
            CheckMenuOnTop(GenericListUI.Instance, hudGraphic);
        GenericListUI.Instance.Close();
        yield return null;
        ClickPartyButton(p.Name);
        yield return null; yield return null;
        TextInputUI.Instance.Open("Menu layer check", _ => true, _ => { });
        yield return null; yield return null;
        var field = TextInputUI.Instance.GetComponentInChildren<TMP_InputField>();
        var fieldRect = (RectTransform)field.transform;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position =
            RectTransformUtility.WorldToScreenPoint(null, fieldRect.TransformPoint(fieldRect.rect.center)) }, hits);
        Check(hits.First().gameObject.transform.IsChildOf(TextInputUI.Instance.transform) &&
            hits.Any(h => h.gameObject.transform.IsChildOf(GameObject.Find("Party window").transform)),
            "Native text entry draws and receives clicks above the open party controls, pointer hits " +
            string.Join(", ", hits.Take(7).Select(h => h.gameObject.name)));
        TextInputUI.Instance.Close();
        PartyUI.HandleEscape(true);
        Check(PartyUI.Opened, "Closing a foreground text menu with Escape keeps the underlying party window open");
        yield return null;
        ClickPartyButton("Equipment");
        yield return null; yield return null;
        ClickPartyButton("Close");
        var box = DialogBox.Instance; NeuralNPC.currentActiveDialogNeuralNPC = actor;
        box.DisplayText("Hello.", _ => { }, new List<UpperButtonOption>());
        end = Time.realtimeSinceStartup + 5;
        while (box.isAnimatingText && Time.realtimeSinceStartup < end) yield return null;
        var layout = Traverse.Create(box).Field("buttonOptionsLayoutGroup").GetValue<LayoutGroup>();
        Traverse.Create(box).Field("popOutButton").GetValue<Button>().onClick.Invoke();
        end = Time.realtimeSinceStartup + 5;
        while (!layout.GetComponent<CanvasGroup>().blocksRaycasts && Time.realtimeSinceStartup < end) yield return null;
        var gear = layout.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TextMeshProUGUI>()?.text == "Manage gear");
        for (int i = 0; i < 20; i++) p.Items.Add(new PackEntry { Name = "Stored item " + i });
        Check(gear.IsInteractable() && layout.GetComponent<CanvasGroup>().blocksRaycasts, "Manage gear is enabled in the open native conversation menu");
        ExecuteEvents.Execute(gear.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        end = Time.realtimeSinceStartup + 5;
        while (!PartyUI.Opened && Time.realtimeSinceStartup < end) yield return null;
        Check(PartyUI.Opened, "Native conversation button and animation open the personal inventory");
        yield return null; yield return null;
        var left = GameObject.Find("Follower pack").GetComponentInChildren<ScrollRect>();
        var right = GameObject.Find("Player pack").GetComponentInChildren<ScrollRect>();
        var l = new Vector3[4]; var r = new Vector3[4];
        left.viewport.GetWorldCorners(l); right.viewport.GetWorldCorners(r);
        Check(l[0].x >= 0 && l[2].x < r[0].x && r[2].x <= Screen.width && Mathf.Abs(l[2].y - r[2].y) < 1 &&
            Mathf.Abs(left.viewport.rect.width - right.viewport.rect.width) < 1, "Both pack columns fit side by side in the rendered game");
        float rightY = right.content.anchoredPosition.y;
        left.verticalNormalizedPosition = 0;
        Canvas.ForceUpdateCanvases();
        Check(left.content.anchoredPosition.y > 0 && Mathf.Abs(rightY - right.content.anchoredPosition.y) < 1, "Follower pack scrolls independently");
        p.Items.RemoveAll(e => e.Name.StartsWith("Stored item "));
        ClickPartyButton("Orders");
        yield return null; yield return null;
        ClickPartyButton("Close");
        Check(box.isOpen && !PartyUI.Opened, "Rendered gear controls close back to the existing conversation");
        end = Time.realtimeSinceStartup + 5;
        while (!layout.enabled && Time.realtimeSinceStartup < end) yield return null;
        box.CloseBox();
        yield return null;
        Check(Silverpine.ModdingTools.InventoryModTools.TryOpen(Plugin.Id + ".controls") && PartyUI.Opened, "Installed Inventory Mods entry opens the roster");
        yield return null; yield return null;
        DebugUI.Instance.ShowDebugOptions();
        yield return null; yield return null;
        var debugButton = GenericListUI.Instance.GetComponentsInChildren<Button>()
            .First(b => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)GenericListUI.Instance.GetComponentInParent<Canvas>().transform,
                b.transform.TransformPoint(((RectTransform)b.transform).rect.center)));
        CheckMenuOnTop(GenericListUI.Instance, GameObject.Find("Party window").GetComponent<Image>(), (RectTransform)debugButton.transform);
        PartyUI.HandleEscape(true);
        Check(PartyUI.Opened, "The debug menu has keyboard priority over the Mods roster");
        GenericListUI.Instance.Close();
        yield return null;
        ClickPartyButton("Close");
        Check(PauseMenuManager.Instance.open && !PartyUI.Opened, "Rendered roster closes back to the inventory");
        PauseMenuManager.Instance.Close();
        var map = DungeonGenerationManager.Instance;
        var origin = map.currentPlayerWorldTilePosition;
        var destination = map.worldTiles.Keys.First(k => (k - origin).sqrMagnitude == 1 && !map.worldTiles[k].IsDungeonInstance());
        var followers = NeuralNPC.neuralNPCs.Values.Where(n => n && n != actor && n.isActiveAndEnabled && !n.sleeping &&
            map.GetPositionWorldTile(n.transform.GetVector2IntPosition()) == map.worldTiles[origin]).Take(2).Select(n => State.Get(n)).ToArray();
        Check(followers.Length == 2 && !actor.sleeping, "World has standing and downed travel fixtures");
        foreach (var f in followers) f.Recruited = true;
        followers[1].DownSince = Combat.Turn;
        var downedPos = followers[1].Actor.transform.position;
        int departureTurn = Combat.Turn;
        map.EnterWorldTile(destination, map.GetTravelTargetPosition(destination));
        var arrival = player.transform.GetVector2IntPosition();
        var guardPos = actor.transform.GetVector2IntPosition();
        var otherPos = followers[0].Actor.transform.GetVector2IntPosition();
        Check(map.GetPositionWorldTile(arrival) == map.worldTiles[destination] && departureTurn == Combat.Turn &&
            guardPos != arrival && otherPos != arrival && guardPos != otherPos &&
            (guardPos - arrival).sqrMagnitude <= 9 && (otherPos - arrival).sqrMagnitude <= 9 &&
            followers[1].Actor.transform.position == downedPos, "Two followers arrive on distinct tiles in the same crossing while a downed member stays behind");
        foreach (var f in followers) f.Recruited = false;

        var enemyPos = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }
            .Select(d => guardPos + d).Where(v => !TurfCollider.HasImpassableCollider(v)).OrderByDescending(v => (v - arrival).sqrMagnitude).First();
        var attacker = TestEnemy(enemyPos);
        var decoy = TestEnemy(guardPos);
        var attack = new Ability_BasicAttack(0, 3, 3, "", DamageType.Normal);
        Traverse.Create(attacker).Field("abilities").SetValue(new List<Ability> { attack });
        p.Order = "Guard"; p.HP = p.LastMaxHP;
        var weapon = new Item("Test weapon", "", "", 1, ItemCategory.Weapon, ItemSound.None, 0f, new ItemComponent_MeleeWeapon(8, 8, 0, 0, 5));
        player.worldInventory.inventory.TryAddItem(weapon);
        Check(p.Accept(weapon, true) && p.Equip(p.Items.Single(e => e.Item == weapon), true), "Guard equipped for the retaliation check");
        var queue = Traverse.Create(ActionQueue.Instance).Field("queueActions").GetValue<List<QueueAction>>();
        Combat.Focus(decoy.GetComponent<Damagable>());
        AccessTools.Method(typeof(Enemy), "OnPreProcessTurn").Invoke(attacker, null);
        var strike = queue.Last(q => q.queuer == attacker.gameObject && q.priority == ActionQueuePriority.Attack);
        queue.Remove(strike); strike.action();
        Check(p.Attacker == attacker && Combat.Target(p) == attacker, "Guard prioritizes the enemy that actually struck them over the player's target");
        p.Order = "Assist";
        Check(Combat.Target(p) == decoy, "Assist follows the player's target even when someone else hit the follower");
        p.Order = "Guard";
        AccessTools.Method(typeof(PathfindingEntity), "OnPreProcessTurn").Invoke(actor.GetComponent<PathfindingEntity>(), null);
        var counter = queue.Last(q => q.queuer == actor.gameObject && q.longAction != null);
        queue.Remove(counter);
        var victim = attacker.GetComponent<Damagable>();
        int hp = victim.HP;
        while (counter.longAction.MoveNext()) yield return counter.longAction.Current;
        Check(victim.HP < hp && decoy.GetComponent<Damagable>().HP == decoy.GetComponent<Damagable>().MaxHP,
            "On their combat turn the guard deals real damage to the attacker and spares the decoy");
        // The live scene keeps moving during the damage animation. Provide a living
        // target in reach for the separate check of target selection after a kill.
        decoy.transform.position = actor.transform.position;
        Combat.Focus(decoy.GetComponent<Damagable>());
        victim.HP = 0;
        Check(Combat.Target(p) == decoy && !p.Attacker, "Guard returns to the player's living target once the attacker is defeated");
        UnityEngine.Object.DestroyImmediate(attacker.gameObject);
        UnityEngine.Object.DestroyImmediate(decoy.gameObject);
        var back = origin - destination;
        var direction = new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right }
            .Single(d => Vector2Int.zero.GetDirectionPosition(d) == back);
        // The player bumps the border from one tile inside its row of changers.
        var edge = map.GetTravelTargetPosition(destination) + back * (back.x < 0 || back.y < 0 ? 49 : 48);
        player.Teleport(edge);
        map.ChangeWorldTile(direction);
        var homeArrival = player.transform.GetVector2IntPosition();
        var homeGuard = actor.transform.GetVector2IntPosition();
        Check(map.currentPlayerWorldTilePosition == origin && map.GetPositionWorldTile(homeArrival) == map.worldTiles[origin] &&
            homeGuard != homeArrival && (homeArrival - homeGuard).sqrMagnitude <= 9 &&
            map.GetPositionWorldTile(homeGuard) == map.worldTiles[origin], "An ordinary border crossing brings the follower through on the arrival side");
    }
    private static Enemy TestEnemy(Vector2Int pos)
    {
        var go = Inactive("party combat fixture");
        go.transform.position = (Vector2)pos;
        go.AddComponent<SpriteRenderer>();
        go.AddComponent<EntityMover>(); go.AddComponent<PathfindingEntity>();
        var health = go.AddComponent<Damagable>();
        health.armorValues = new DamageTypeIntDictionary { [DamageType.Normal] = 0 };
        var enemy = go.AddComponent<Enemy>();
        Traverse.Create(enemy).Field("initialized").SetValue(true);
        go.SetActive(true);
        return enemy;
    }
    private void RunChecks()
    {
        Check(Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(Plugin.Id) == true) >= 10, "Runtime Harmony patches installed");
        bool slots = Chainloader.PluginInfos.ContainsKey("renegadex.silverpine.additionalequipmentslots");
        bool expanded = Chainloader.PluginInfos.ContainsKey(GearMods.Expansion);
        Check(GearMods.Slots().ContainsKey(10001) == slots, "Back slot follows the installed provider");
        Check(GearMods.Expanded == expanded, "Optional modifier provider detected independently");
        var hero = Inactive("test-player").AddComponent<Player>(); Player.Instance = hero;
        WorldInfoManager.Instance = Inactive("test-world").AddComponent<WorldInfoManager>();
        hero.worldInventory = hero.gameObject.AddComponent<WorldInventory>();
        hero.worldInventory.inventory.owner = hero.gameObject;
        var actor = Inactive("test-npc").AddComponent<NeuralNPC>();
        actor.npcName = (NPCName)12;
        actor.gameObject.AddComponent<CustomNPCHandler>().customContentName = "PartyTestNPC";
        var merchant = actor.gameObject.AddComponent<WorldInventory>();
        merchant.inventory.owner = actor.gameObject; merchant.inventory.gold = 320;
        CustomContentDefinition_NPC.loaded["PartyTestNPC"] = new CustomContentDefinition_NPC
        {
            enabled = true,
            customPlayerCharacterDefinitionName = "PartyTestPC"
        };
        CustomContentDefinition_PlayerCharacter.loaded["PartyTestPC"] = new CustomContentDefinition_PlayerCharacter
        {
            racials = new List<string> { "Hollow Bones", "Thick Skin", "Night Vision" }
        };
        var p = State.Get(actor);
        CheckConversation(actor, p, merchant.inventory);
        Check(Stats.Read(p).MaxHP == 85 && Stats.Read(p).NightSight, "playerSILC racial stats and night vision resolved");
        var armour = new Item("Test Coat", "sprite_test", "", 20, ItemCategory.Clothing, ItemSound.Cloth, 1f,
            new ItemComponent_Clothing(EquipmentSlotType.Chest, false, ItemComponent_Clothing.ClothingTier.Leather),
            new ItemComponent_Enchantment(ItemComponent_Enchantment.EnchantmentType.Vitality),
            new ItemComponent_Durability(50));
        hero.worldInventory.inventory.TryAddItem(armour);
        Check(p.Accept(armour, true) && p.Pack.ContainsItem(armour) && !hero.worldInventory.inventory.ContainsItem(armour), "Loans move the exact item into the personal pack");
        p.Equip(p.Items.Single(), true);
        Check(Stats.Read(p).MaxHP == 95 && Stats.Read(p).Armor[0] == 4 && hero.equippedItems.Count == 0, "Armor and enchantment apply only to wearer");
        p.HP = 90;
        Combat.Wear(p, armour, Stats.Read(p), 7);
        Check(armour.GetItemComponent<ItemComponent_Durability>().Durability == 43, "Gear durability uses the follower's item");
        NeuralNPC.currentActiveDialogNeuralNPC = actor;
        TypedGifts.Credit(merchant.inventory, 349, "gives you 29 gold");
        Check(p.Gold == 29 && merchant.inventory.gold == 320, "Dialogue gift credits the private purse");
        TypedGifts.Credit(merchant.inventory, 330, "pays 10 gold for the room");
        Check(p.Gold == 29 && merchant.inventory.gold == 330, "Payment credits the trader");
        p.Recruited = true;
        p.Wishes.Add("lantern | travels after dark");
        p.Items.Add(new PackEntry { Name = "Missing modded cloak", Sprite = "custom:missing:cloak", Slot = 10001, Data = "AQID" });
        string snapshot = State.Capture();
        State.Reset(); State.Restore(snapshot);
        p = State.Get(actor);
        var coat = p.Items.Single(e => e.Loan);
        Check(p.Gold == 29 && p.HP == 90 && p.Recruited && p.Wishes.Count == 1 && coat.Item.GetItemComponent<ItemComponent_Durability>().Durability == 43, "Pack, loan, wishes, health, purse and roster round-trip");
        Check(p.Items.Last().Item == null && p.Items.Last().Data == "AQID" && p.Items.Last().Slot == 10001, "Unavailable content and slot survive load");
        Check(Stats.Read(p).MaxHP == 95 && p.Worn.Count() == 1, "Reload applies equipment once");
        coat.Slot = 0; p.Clamp();
        Check(Stats.Read(p).MaxHP == 85 && p.HP == 85, "Unequip clamps current HP");
        p.Equip(coat, true); Check(p.HP == 85, "Re-equipping does not heal");
        var healing = new Item("Remedy", "sprite_heal", "", 5, ItemCategory.Potion, ItemSound.None, 0, new ItemComponent_Potion(1f, AlchemyEffectType.Healing));
        Check(Supplies.Healing(healing) && !Supplies.Healing(armour), "Consumables recognized by components");
        Check(TypedGifts.Personal("hands you 12 gold") && !TypedGifts.Personal("pays the fine"), "Gift clauses distinguished from service payments");
        var seller = Inactive("test-trader").AddComponent<NeuralNPC>();
        seller.npcName = NPCName.Hayden;
        var till = seller.gameObject.AddComponent<WorldInventory>().inventory;
        till.owner = seller.gameObject;
        healing.AddItemComponent(new ItemComponent_VendorItem(seller.npcName));
        till.TryAddItem(healing);
        int price = healing.GetFinalValue();
        p.Gold = price + 20;
        Market.Trade(p, seller, null);
        Check(p.Pack.ContainsItem(healing) && !till.ContainsItem(healing) && p.Gold == 20 && till.gold == price, "NPC purchase moves the real stock and pays only the merchant");
        Check(Supplies.Heal(p, Stats.Read(p)) && !p.Pack.ContainsItem(healing) && p.HealingTurns == 10, "Healing uses one real potion and starts its effect");
        Combat.ApplyDamage(p, 10000, DamageType.Normal);
        Check(p.Down && p.HP == 0 && p.HealingTurns == 0, "Lethal hit incapacitates even when armor breaks on that hit");
        Combat.ApplyDamage(p, 10, DamageType.Normal);
        Check(p.Down && p.Actor == actor && p.Gold == 20, "Downed follower keeps identity, pack and purse");
        State.Restore(State.Capture()); p = State.Get(actor);
        Check(p.Down && p.HP == 0, "Downed state persists");
        Supplies.Revive(p);
        Check(!p.Down && p.HP == Stats.Read(p).MaxHP / 2, "Essential recovery restores partial health");
        var pastry = new Item("Pastry", "pastry", "", 3, ItemCategory.Food, ItemSound.None, 0,
            new ItemComponent_Edible(8, 0, 3));
        hero.worldInventory.inventory.TryAddItem(pastry); p.Accept(pastry, false);
        p.Energy = 20; p.Sanity = 70;
        Supplies.Snack(p, false);
        Check(!p.Pack.ContainsItem(pastry) && p.Energy == 28 && p.Sanity == 73, "Eating consumes the actual pastry and affects the NPC");
        var lamp = new Item("Lantern", "lamp", "", 6, ItemCategory.Tool, ItemSound.None, 0,
            new ItemComponent_Lantern(30), new ItemComponent_Durability(100));
        var oil = new Item("Oil", "oil", "", 2, ItemCategory.Tool, ItemSound.None, 0, new ItemComponent_LanternOil());
        hero.worldInventory.inventory.TryAddItem(lamp); p.Accept(lamp, false);
        hero.worldInventory.inventory.TryAddItem(oil); p.Accept(oil, false);
        var fuel = lamp.GetItemComponent<ItemComponent_Durability>();
        var lantern = lamp.GetItemComponent<ItemComponent_Lantern>();
        var light = actor.GetComponent<LightAttacher>();
        fuel.Durability = 12;
        Supplies.MaintainLantern(p, lantern, fuel, light, true);
        Check(fuel.Durability == 100 && !p.Pack.ContainsItem(oil) && Fields.Read<bool>(lantern, "lit"), "Low lantern refills from the private pack and lights its owner");
        Combat.Wear(p, lamp, Stats.Read(p), 4);
        Supplies.MaintainLantern(p, lantern, fuel, light, false);
        Check(!Fields.Read<bool>(lantern, "lit") && fuel.Durability == 96, "Burned oil stays spent after extinguishing the lantern");
        // Reset the original test coat for the optional integration checks.
        p.Equip(p.Items.Single(e => e.Loan), true);
        if (Chainloader.PluginInfos.TryGetValue(GearMods.Loader, out var loader))
        {
            var api = loader.Instance.GetType().Assembly.GetType("SilverpineMods.CustomItemLoader.CustomItemApi");
            var args = new object[] { slots ? "party.test:cape" : "party.test:coat", null };
            Check(api.GetMethod("TryCreateItem").Invoke(null, args) is true, "Actual custom pack item registered");
            var modded = (Item)args[1];
            hero.worldInventory.inventory.TryAddItem(modded);
            Check(p.Accept(modded, true) && p.Equip(p.Items.Last(), false), "Custom gear can be transferred and equipped");
            Check(p.Items.Last().Slot == (slots ? 10001 : (int)EquipmentSlotType.Chest), "Correct custom or native slot resolved");
            var stats = Stats.Read(p);
            Check(stats.MaxHP == (slots ? 107 : 97) && stats.Armor[(int)DamageType.Fire] >= 3, "Loader attribute bonuses combine with wearer traits");
            Check(Math.Abs(stats.Speed - (expanded ? .25f : 0)) < .001f, "Expansion stats follow actual provider availability");
        }
        string handoff = Path.Combine(Paths.GameRootPath, "equipment-roundtrip.json");
        if (File.Exists(handoff))
        {
            State.Restore(File.ReadAllText(handoff));
            p = State.Get(actor);
            var cloak = p.Items.Single(e => e.Sprite == "custom:party.test:cape");
            Check(cloak.Slot == 10001 && (cloak.Item != null) == slots, "Previous installation's cloak preserved at its original slot");
            if (cloak.Item != null)
                Check(cloak.Item.GetItemComponent<ItemComponent_Durability>().Durability == 19 && Math.Abs(Stats.Read(p).Speed - .25f) < .001f, "Restored cloak keeps its durability and a single regeneration of its modifiers");
            File.WriteAllText(handoff, State.Capture());
        }
        else if (slots && expanded)
        {
            var cloak = p.Items.Last().Item;
            var durability = cloak.GetItemComponent<ItemComponent_Durability>();
            durability.Durability = 19;
            File.WriteAllText(handoff, State.Capture());
        }
    }
}
