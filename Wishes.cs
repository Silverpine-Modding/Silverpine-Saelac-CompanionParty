using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CompanionParty;

internal static class Wishes
{
    private static bool working;
    internal static double SessionSeconds;
    private static readonly List<double> SessionAttempts = new();
    internal static readonly HashSet<Task> ModelTasks = new();
    internal static bool Match(Companion p, Item item) => p.Wishes.Any(w => Matches(w, item));
    private static bool Matches(string wish, Item i)
    {
        string term = wish.Split('|')[0].Trim();
        if (term.Length < 2) return false;
        if (i.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        switch (term.ToLowerInvariant())
        {
            case "food": return Supplies.Has<ItemComponent_Edible>(i);
            case "healing potion": return Supplies.Healing(i);
            case "light": case "lantern": return Supplies.Has<ItemComponent_Lantern>(i);
            case "oil": return Supplies.Has<ItemComponent_LanternOil>(i);
            case "armor": return Supplies.Has<ItemComponent_Clothing>(i);
            case "weapon": return Supplies.Has<ItemComponent_MeleeWeapon>(i) || Supplies.Has<ItemComponent_RangedWeapon>(i);
            case "book": return (i.name + " " + i.spriteName + " " + i.description).IndexOf("book", StringComparison.OrdinalIgnoreCase) >= 0;
            default: return false;
        }
    }
    internal static void Fulfill(Companion p, Item item)
    { if (p.Wishes.RemoveAll(w => Matches(w, item)) > 0 && p.Wishes.Count == 0) p.WishesFulfilled = true; }
    internal static void Schedule()
    {
        var server = InferenceServerSetupHandler.Instance;
        if (working || Plugin.Budget.Value <= 0 || !server || server.noAIModelMode ||
            (!server.modelLoaded && !server.apiMode && !server.remoteMode) || server.client == null ||
            DialogBox.Instance.isOpen || PauseMenuManager.Instance.open || Enemy.AnyInCombat() || PartyUI.Opened) return;
        lock (ModelTasks)
        {
            ModelTasks.RemoveWhere(t => t.IsCompleted);
            if (ModelTasks.Count != 0) return;
        }
        var campaign = State.Save;
        campaign.Attempts.RemoveAll(t => campaign.PlaySeconds - t >= 1800);
        SessionAttempts.RemoveAll(t => SessionSeconds - t >= 1800);
        if (Math.Max(campaign.Attempts.Count, SessionAttempts.Count) >= Plugin.Budget.Value) return;
        int day = WorldInfoManager.Instance.GetCurrentDay();
        foreach (var p in campaign.People.Values.Where(p => p.WishEligible && p.Actor && p.Actor.isActiveAndEnabled && !p.Down &&
            day - p.LastWishDay >= Plugin.RefreshDays.Value).OrderBy(p => p.LastWishDay).ThenByDescending(p => p.Recruited))
        {
            var npc = p.Actor;
            string text = npc.GetFinalCharacterDescription(true);
            if (CustomContentDefinition_NPC.loaded.TryGetValue(npc.GetFinalName(), out var def) && def.enabled)
                text += "\n" + string.Join("\n", def.knowledgeEntries
                    .Select(k => k.entryName + ": " + k.lore)
                    .OrderByDescending(k => k.ContainsAnyWordIgnoreCase("favorite", "likes", "loves", "wants", "needs", "collects", "lost", "hobby", "interests"))
                    .Take(8));
            if (npc.advancedMemories != null)
                text += "\n" + string.Join("\n", npc.advancedMemories.Skip(Math.Max(0, npc.advancedMemories.Count - 2)).Select(m => m.memory));
            text = (npc.personalityTraits + "\n" + text).Replace("%NAME%", p.Name).Replace("%PLAYER_NAME%", Player.Instance.playerName);
            if (text.Length > 5000) text = text.Substring(0, 5000);
            using var hash = SHA256.Create();
            string fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
            if (fingerprint == p.WishFingerprint && !p.WishesFulfilled) continue;
            p.LastWishDay = day;
            campaign.Attempts.Add(campaign.PlaySeconds);
            SessionAttempts.Add(SessionSeconds);
            working = true;
            _ = Generate(p, text, fingerprint, State.Epoch);
            break;
        }
    }
    private static async Task Generate(Companion p, string context, string fingerprint, int epoch)
    {
        try
        {
            string prompt = "Choose zero to three possessions this person would privately like, grounded in their character knowledge. " +
                "Return only a compact JSON array of objects with string fields \"item\" and \"reason\". Use a short item name or category " +
                "(such as book, food, lantern, oil, healing potion, armor, weapon) and at most twelve words for each reason. " +
                "Use [] for no wishes. Prefer something they do not already own.\nCharacter: " + context +
                "\nOwned: " + string.Join(", ", p.Items.Where(e => e.KnownPossession).Take(25).Select(e => e.Name));
            string reply = await InferenceServerSetupHandler.Instance.GenerateSimpleLowTemp("", new List<string> { prompt }, true, maxLength: 150);
            if (epoch != State.Epoch) return;
            if (!ApplyReply(p, reply, fingerprint))
                throw new FormatException("Expected a complete wish entry or an explicit empty array.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("Companion wish attempt ended: " + ex.GetBaseException().Message); }
        finally { working = false; }
    }

    internal static bool ApplyReply(Companion p, string reply, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(reply)) return false;
        reply = reply.TrimStart();
        int searchFrom = reply[0] == '[' ? 0 : Math.Max(0, reply.IndexOf("```json", StringComparison.OrdinalIgnoreCase));
        int start = reply.IndexOf('[', searchFrom);
        if (start < 0) return false;
        var choices = new List<string>(3);
        bool sawEntry = false, closed = false;
        using var reader = new JsonTextReader(new StringReader(reply.Substring(start))) { DateParseHandling = DateParseHandling.None };
        reader.Read(); // StartArray
        try
        {
            while (choices.Count < 3 && reader.Read())
            {
                if (reader.TokenType == JsonToken.Comment) continue;
                if (reader.TokenType == JsonToken.EndArray) { closed = true; break; }
                sawEntry = true;
                if (reader.TokenType != JsonToken.StartObject) { reader.Skip(); continue; }
                // Load one complete object before accepting either field. The model may stop mid-entry.
                var entry = JObject.Load(reader);
                if (entry["item"]?.Type != JTokenType.String || entry["reason"]?.Type != JTokenType.String) continue;
                string item = ((string)entry["item"]).Trim(), reason = ((string)entry["reason"]).Trim();
                if (item.Length < 2 || item.Length > 65 || item.Contains("|") || reason.Length == 0 || reason.Length > 180) continue;
                string wish = item + " | " + reason;
                if (!choices.Contains(wish)) choices.Add(wish);
            }
        }
        catch (JsonReaderException)
        {
            // Keep any validated prefix. An unfinished or malformed first entry cannot clear existing wishes.
        }
        if (choices.Count == 0 && (sawEntry || !closed)) return false;
        p.Wishes = choices;
        p.WishesFulfilled = false;
        p.WishFingerprint = fingerprint;
        return true;
    }
}

[HarmonyPatch(typeof(KoboldSharp.KoboldClient), "Generate")]
internal static class ModelActivity
{
    static void Postfix(Task __result)
    {
        if (__result == null) return;
        lock (Wishes.ModelTasks) { Wishes.ModelTasks.RemoveWhere(t => t.IsCompleted); Wishes.ModelTasks.Add(__result); }
    }
}
