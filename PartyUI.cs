using System;
using System.Collections.Generic;
using System.Linq;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static CompanionParty.PartyLayout;

namespace CompanionParty;

internal static class PartyUI
{
    private enum Page { Roster, Inventory, Equipment, Orders }
    private static RectTransform gameplayCanvas, root, hud, overlay, panel, body;
    private static Vector2 hostSize;
    private static DialogBox sourceDialog;
    private static int lastCoveredFrame = -2;
    private static GameObject previousSelection;
    private static Companion selected;
    private static PackEntry preview;
    private static Page page;
    private static bool giftOnly, nearLast, downLast;
    private static string message;
    private static TextMeshProUGUI summary;
    private static ScrollRect followerList, playerList;
    private static Vector2 packOffsets;
    private static ModToolSession session;
    private static readonly List<HudRow> rows = new();
    internal static bool Opened => overlay;
    internal static bool Covers(DialogBox dialog) => Opened && sourceDialog == dialog;
    private static bool Visible(Behaviour menu) => menu && menu.isActiveAndEnabled;

    internal static void HandleEscape(bool pressed)
    {
        if (!Opened) return;
        if (Visible(GenericListUI.Instance) || Visible(TextInputUI.Instance) || Visible(ReadingUI.Instance) ||
            Visible(CraftingUI.Instance) || Visible(MarketUI.Instance) || Visible(WaitUI.Instance) ||
            Visible(PayUI.Instance) || (BlackScreen.Instance && BlackScreen.Instance.IsActive()) ||
            (PauseMenuManager.Instance.open && session == null) || (DialogBox.Instance.isOpen && !sourceDialog))
            lastCoveredFrame = Time.frameCount;
        // The upper menu may process its own Escape earlier in this frame.
        if (pressed && Time.frameCount > lastCoveredFrame + 1) Close();
    }

    private sealed class HudRow
    {
        internal Companion Person;
        internal TextMeshProUGUI Label;
        internal RectTransform Health;
        internal Image Fill;
    }

    private static void EnsureCanvas()
    {
        PartyRuntime.Ensure(Player.Instance);
        if (root) return;
        gameplayCanvas = Rect("Party gameplay canvas", null);
        var canvas = gameplayCanvas.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -1;
        var scaler = gameplayCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1f;
        gameplayCanvas.gameObject.AddComponent<GraphicRaycaster>();
        root = Rect("Companion Party UI", gameplayCanvas);
        Stretch(root);
    }

    // A window launched by a menu belongs just above that menu's own contents.
    // Leave sibling menus (including the debug list) and higher game canvases in place.
    private static void HostWindow()
    {
        Transform host = null;
        int index = -1;
        if (session != null && PauseMenuManager.Instance)
        {
            var inventory = PauseMenuManager.Instance.transform;
            if (inventory.parent && inventory.parent.GetComponent<Canvas>())
            { host = inventory.parent; index = inventory.GetSiblingIndex() + 1; }
        }
        else if (sourceDialog && sourceDialog.GetComponent<Canvas>())
            host = sourceDialog.transform;
        if (!host) return;
        root.SetParent(host, false);
        if (index >= 0) root.SetSiblingIndex(index);
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.anchoredPosition = Vector2.zero;
        hostSize = Vector2.zero;
        FitToHost();
    }

    private static void FitToHost()
    {
        if (!root || root.parent == gameplayCanvas) return;
        var size = ((RectTransform)root.parent).rect.size;
        if (size == hostSize || size.y <= 0) return;
        hostSize = size;
        float scale = size.y / 720f;
        root.localScale = new Vector3(scale, scale, 1);
        root.sizeDelta = size / scale;
    }

    internal static void Refresh()
    {
        FitToHost();
        if (Opened && selected != null)
        {
            bool near = Near();
            if (near != nearLast || selected.Down != downLast) Rebuild();
            if (summary) summary.text = Summary();
        }
        bool show = !Opened && !PauseMenuManager.Instance.open && !DialogBox.Instance.isOpen;
        if (!show) { if (hud) hud.gameObject.SetActive(false); return; }
        var roster = State.Save.People.Values.Where(p => p.Recruited).ToList();
        if (roster.Count == 0) { if (hud) hud.gameObject.SetActive(false); return; }
        EnsureCanvas();
        if (!hud || !rows.Select(r => r.Person).SequenceEqual(roster)) BuildHud(roster);
        hud.gameObject.SetActive(true);
        foreach (var row in rows)
        {
            var p = row.Person;
            int max = p.Actor ? Stats.Read(p).MaxHP : Math.Max(1, p.LastMaxHP);
            row.Label.text = $"{p.Name}   {p.HP}/{max}" + (p.Down ? "   Downed" : !p.Actor ? "   Away" : "");
            float fraction = Mathf.Clamp01((float)p.HP / max);
            row.Health.anchorMax = new Vector2(fraction, 1);
            row.Fill.color = fraction < .33f ? new Color(.89f, .47f, .28f) : new Color(.42f, .76f, .51f);
        }
    }

    private static void BuildHud(List<Companion> roster)
    {
        if (hud) Remove(hud.gameObject);
        rows.Clear(); hud = Rect("Party HUD", root);
        hud.anchorMin = hud.anchorMax = new Vector2(0, .73f); hud.pivot = new Vector2(0, 1);
        hud.anchoredPosition = new Vector2(12, 0);
        hud.sizeDelta = new Vector2(248, Math.Min(435, 47 + roster.Count * 49));
        Background(hud, Surface);
        var title = Button(hud, "Party controls", () => OpenRoster());
        var header = (RectTransform)title.transform; header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
        header.pivot = new Vector2(.5f, 1); header.anchoredPosition = new Vector2(0, -4); header.sizeDelta = new Vector2(-8, 32);
        var viewport = Rect("Followers", hud); Stretch(viewport, 4, 40, 4, 4);
        var content = Scroll(viewport);
        foreach (var p in roster)
        {
            var r = Rect(p.Name, content); Size(r, 44);
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = Background(r, Control);
            b.onClick.AddListener(() => ShowPerson(p, Page.Orders));
            var label = Rect("Name and health", r); Stretch(label, 9, 0, 7, 11);
            var track = Rect("Health bar", r); track.anchorMin = Vector2.zero; track.anchorMax = new Vector2(1, 0);
            track.offsetMin = new Vector2(9, 6); track.offsetMax = new Vector2(-9, 12);
            Background(track, Surface, false);
            var fill = Rect("Current health", track); Stretch(fill);
            rows.Add(new HudRow { Person = p, Label = Text(label, ""), Health = fill, Fill = Background(fill, Color.green, false) });
        }
    }

    internal static void OpenRoster(ModToolSession tool = null)
    {
        if (!Plugin.Ready) { tool?.Close(); return; }
        if (tool != null && tool != session)
        {
            Close(); session = tool;
            tool.Closed += Close;
        }
        selected = null; giftOnly = false; page = Page.Roster; preview = null; message = "";
        Show();
    }
    internal static void Open(NeuralNPC npc, bool onlyGive = false)
    {
        if (!Opened && DialogBox.Instance && DialogBox.Instance.isOpen) sourceDialog = DialogBox.Instance;
        ShowPerson(State.Get(npc), Page.Inventory, onlyGive);
    }
    private static void ShowPerson(Companion p, Page destination, bool onlyGive = false)
    {
        followerList = playerList = null; packOffsets = Vector2.zero;
        selected = p; giftOnly = onlyGive; page = destination; preview = null; message = "";
        Show();
    }
    private static void Show()
    {
        EnsureCanvas();
        if (hud) hud.gameObject.SetActive(false);
        if (!overlay)
        {
            HostWindow();
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            overlay = Rect("Party window", root); Stretch(overlay);
            Background(overlay, new Color(0, 0, 0, .64f));
        }
        Rebuild();
    }
    internal static void Close()
    {
        if (overlay) Remove(overlay.gameObject);
        overlay = panel = body = null; summary = null; selected = null; preview = null;
        followerList = playerList = null; packOffsets = Vector2.zero;
        sourceDialog = null;
        if (root && gameplayCanvas && root.parent != gameplayCanvas)
        {
            root.SetParent(gameplayCanvas, false);
            root.localScale = Vector3.one;
            Stretch(root);
        }
        var tool = session; session = null;
        if (tool != null) { tool.Closed -= Close; tool.Close(); }
        if (previousSelection && previousSelection.activeInHierarchy && EventSystem.current)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }
    internal static void Release()
    {
        Close();
        if (gameplayCanvas) Remove(gameplayCanvas.gameObject);
        gameplayCanvas = root = hud = null; rows.Clear();
    }
    private static bool Near() => selected != null && selected.Actor && Player.Instance &&
        State.Near(selected.Actor, Player.Instance.transform, 3);
    private static string Summary()
    {
        var p = selected;
        return $"HP  {p.HP}/{p.LastMaxHP}     Gold  {p.Gold}     Bulk  {p.Pack.GetBulk():0.#}/{p.Pack.maxBulk:0.#}";
    }
    internal static void RefreshSkillBenefits()
    {
        if (Opened && (page == Page.Roster || selected?.Recruited == true)) Rebuild();
    }
    private static void Rebuild()
    {
        if (!overlay) return;
        if (followerList && playerList)
            packOffsets = new Vector2(followerList.content.anchoredPosition.y, playerList.content.anchoredPosition.y);
        followerList = playerList = null;
        if (panel) Remove(panel.gameObject);
        panel = Rect("Party panel", overlay);
        // Relative anchors keep the window and Close button on screen after a resize.
        panel.anchorMin = new Vector2(.12f, .15f); panel.anchorMax = new Vector2(.88f, .85f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        Background(panel, Surface); Vertical(panel, 6);
        panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
        var heading = Row(panel, 39);
        var title = Line(heading, selected == null ? "Party controls" : selected.Name, 38); title.fontSize = 24;
        if (selected != null && !giftOnly) Button(heading, "Party", () => OpenRoster(), 76);
        Button(heading, "Close", Close, 78);
        if (page == Page.Roster)
        {
            var setting = Plugin.EquipmentInDialogue;
            Button(panel, "NPC equipment in dialogue (" + (setting.Value ? "on" : "off") + ")", () =>
            {
                setting.Value = !setting.Value;
                Rebuild();
            }, chosen: setting.Value);
        }
        summary = null;
        nearLast = Near(); downLast = selected?.Down ?? false;
        if (selected != null)
        {
            summary = Line(panel, Summary());
            if (!nearLast) Line(panel, "Move closer to exchange items or give orders.");
            if (!giftOnly)
            {
                var tabs = Row(panel);
                foreach (var tab in new[] { Page.Orders, Page.Inventory, Page.Equipment })
                    Button(tabs, tab.ToString(), () => { page = tab; message = ""; Rebuild(); }, chosen: tab == page);
            }
        }
        var viewport = Rect("Page", panel);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        body = page == Page.Inventory ? viewport : Scroll(viewport);
        switch (page)
        {
            case Page.Roster:
                var party = State.Save.People.Values.Where(p => p.Recruited).ToArray();
                foreach (var p in party)
                    Button(body, $"{p.Name}    {p.HP}/{p.LastMaxHP}    {(p.Down ? "Downed" : p.Order)}", () => ShowPerson(p, Page.Orders));
                if (party.Length == 0) Line(body, "Talk to an NPC and choose Travel with me to recruit them.");
                break;
            case Page.Inventory: Inventory(); break;
            case Page.Equipment: Equipment(); break;
            case Page.Orders: Orders(); break;
        }
        if (!string.IsNullOrEmpty(message)) Line(panel, message);
        if (page == Page.Inventory && packOffsets.sqrMagnitude > 0)
        {
            Canvas.ForceUpdateCanvases();
            RestorePackOffset(followerList, packOffsets.x);
            RestorePackOffset(playerList, packOffsets.y);
        }
    }
    private static void LocalButton(Transform parent, string label, Action action, float width = -1, bool enabled = true, bool chosen = false)
    {
        var owner = selected;
        var b = Button(parent, label, () =>
        {
            if (owner != selected || !Near()) { Rebuild(); return; }
            action();
            if (Opened) Rebuild();
        }, width, chosen);
        b.interactable = nearLast && enabled;
    }
    private static void Orders()
    {
        var p = selected;
        if (p.Down)
        {
            LocalButton(body, "Help up", () => {
                Supplies.Revive(p, helpedByPlayer: true); Close(); if (DialogBox.Instance.isOpen) DialogBox.Instance.EndDialog();
                ActionQueue.Instance.RequestProcessTurn("Help companion up");
            });
            return;
        }
        LocalButton(body, "Guard me", () => p.Order = "Guard", chosen: p.Order == "Guard");
        LocalButton(body, "Assist my target", () => p.Order = "Assist", chosen: p.Order == "Assist");
        Line(body, p.Order == "Assist" ? "Fights the enemy you last attacked." : "Fights nearby threats and prioritizes an enemy attacking this follower.");
        LocalButton(body, "Keep my equipment choices  " + (p.Pinned ? "(on)" : "(off)"), () => p.Pinned = !p.Pinned, chosen: p.Pinned);
        var goldRow = Row(body);
        Line(goldRow, $"Your gold  {Player.Instance.worldInventory.inventory.gold}");
        var amount = Rect("Gold amount", goldRow); Size(amount, 36, 100);
        var field = amount.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = Background(amount, Control);
        var value = Rect("Amount", amount); Stretch(value, 7, 0, 7, 0);
        field.textViewport = amount; field.textComponent = Text(value, "1");
        field.contentType = TMP_InputField.ContentType.IntegerNumber; field.characterLimit = 10;
        field.SetTextWithoutNotify("1"); field.interactable = nearLast;
        LocalButton(goldRow, "Give gold", () => {
            if (int.TryParse(field.text, out int n) && Plugin.TryGiftGold(p.Actor, n)) message = $"Gave {p.Name} {n} gold.";
            else message = "Enter a positive amount you can give.";
        }, 100);
        LocalButton(body, "Return to your routine", () => { PartyApi.Dismiss(p.Actor); Close(); });
    }
    private static void Inventory()
    {
        var p = selected;
        followerList = PackPane(p.Name + "’s pack", true);
        playerList = PackPane("Your pack", false);
        var wallet = Player.Instance.worldInventory.inventory;
        foreach (var item in wallet.items.Where(i => PackEntry.IsVisibleName(i?.name)))
        {
            var row = Row(playerList.content);
            Line(row, item.name + (Player.Instance.IsItemEquipped(item) ? " (equipped)" : ""));
            bool fits = p.Pack.CanAddItem(item);
            LocalButton(row, "Give", () => Transfer(p, item, false), 66, fits);
            if (!giftOnly) LocalButton(row, "Lend", () => Transfer(p, item, true), 66, fits);
        }
        if (playerList.content.childCount == 0) Line(playerList.content, "Empty");
        foreach (var entry in p.Items.Where(e => e.Visible))
        {
            var row = Row(followerList.content);
            Line(row, entry.Name + (entry.Item == null ? " (content unavailable)" : entry.Slot != 0 ? " (equipped)" : entry.Loan ? " (loan)" : ""));
            if (giftOnly || entry.Item == null) continue;
            if (entry.Item.GetEquipmentSlotType() != EquipmentSlotType.NotEquipable)
                LocalButton(row, entry.Slot == 0 ? "Equip" : "Unequip", () => {
                    if (entry.Slot == 0) { preview = entry; page = Page.Equipment; }
                    else { entry.Slot = 0; p.Pinned = true; p.Clamp(); }
                }, 85);
            if (entry.Loan) LocalButton(row, "Reclaim", () => {
                if (p.Items.Contains(entry) && entry.Item.TryMoveItemToInventory(wallet))
                { p.Items.Remove(entry); p.Clamp(); message = "Loan returned."; }
                else message = "The loan could not be returned.";
            }, 85);
        }
        if (followerList.content.childCount == 0) Line(followerList.content, "Empty");
    }
    private static ScrollRect PackPane(string heading, bool left)
    {
        var pane = Rect(left ? "Follower pack" : "Player pack", body);
        pane.anchorMin = new Vector2(left ? 0 : .5f, 0);
        pane.anchorMax = new Vector2(left ? .5f : 1, 1);
        pane.offsetMin = new Vector2(left ? 0 : 5, 0);
        pane.offsetMax = new Vector2(left ? -5 : 0, 0);
        Background(pane, new Color(.13f, .16f, .19f), false);
        Vertical(pane);
        pane.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(8, 8, 5, 6);
        Line(pane, heading, 30).fontStyle = FontStyles.Bold;
        var viewport = Rect("Items", pane);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        Scroll(viewport);
        return viewport.GetComponent<ScrollRect>();
    }
    private static void RestorePackOffset(ScrollRect list, float offset)
    {
        float overflow = list.content.rect.height - list.viewport.rect.height;
        list.verticalNormalizedPosition = overflow > 0 ? 1 - Mathf.Clamp01(offset / overflow) : 1;
    }
    private static void Transfer(Companion p, Item item, bool loan)
    {
        if (!p.Accept(item, loan)) { message = "Transfer could not be completed."; return; }
        message = (loan ? "Lent " : "Gave ") + item.name + " to " + p.Name + ".";
        if (DialogBox.Instance.isOpen && NeuralNPC.currentActiveDialogNeuralNPC == p.Actor)
            p.Actor.QuietlyAddSystemDialog(Player.Instance.playerName + " " + message);
    }
    private static void Equipment()
    {
        var p = selected;
        if (preview != null && p.Items.Contains(preview))
        {
            var shadow = new Companion { Actor = p.Actor, Recruited = p.Recruited, SkillBonuses = p.SkillBonuses,
                Items = p.Items.Select(e => new PackEntry { Item = e.Item, Slot = e.Slot }).ToList() };
            if (shadow.Equip(shadow.Items[p.Items.IndexOf(preview)], false))
            {
                Line(body, "With " + preview.Name);
                ItemDescription(preview.Item);
                foreach (var line in Stats.Describe(Stats.Read(shadow)).Split('\n')) Line(body, line);
                LocalButton(body, "Equip " + preview.Name, () => { p.Equip(preview, true); preview = null; page = Page.Inventory; });
            }
        }
        foreach (var line in Stats.Describe(Stats.Read(p)).Split('\n')) Line(body, line);
        foreach (var slot in GearMods.Slots())
        {
            var entry = p.Items.FirstOrDefault(e => e.Slot == slot.Key && e.Visible);
            if (slot.Key != (int)EquipmentSlotType.Fur || entry != null)
            {
                Line(body, slot.Value + "    " + (entry?.Name ?? "—"));
                ItemDescription(entry?.Item);
            }
        }
    }
    private static void ItemDescription(Item item)
    {
        if (string.IsNullOrWhiteSpace(item?.description)) return;
        var label = Text(Rect("Item description", body), item.description);
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
    }
    internal static string DescribePack(Companion p)
    {
        var names = p.Items.Where(e => e.Slot == 0 && e.Item != null && e.Visible).GroupBy(e => e.Name).Select(g => g.Key + " x" + g.Count());
        var lamp = p.Items.FirstOrDefault(e => e.KnownPossession && Supplies.Has<ItemComponent_Lantern>(e.Item))?.Item;
        var fuel = lamp?.GetItemComponent<ItemComponent_Durability>();
        string report = $"{p.Name}'s personal belongings: {string.Join(", ", names.Take(50))}. Personal gold: {p.Gold}.";
        if (fuel != null) report += $" Lantern fuel: {fuel.Durability:0}/{fuel.maxDurability:0}.";
        if (p.Wishes.Count > 0) report += " Private wishes: " + string.Join("; ", p.Wishes) + ".";
        return report;
    }
}
