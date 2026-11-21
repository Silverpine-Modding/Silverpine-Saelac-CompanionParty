using System;
using System.IO;

namespace CompanionParty;

// A field assignment never enrolls the NPC in the player's travelling party.
public static class ExpeditionApi
{
    private static Func<NeuralNPC, bool> assigned, active;
    public static void Register(Func<NeuralNPC, bool> isAssigned, Func<NeuralNPC, bool> canFight)
    {
        assigned = isAssigned;
        active = canFight;
    }
    internal static bool Assigned(NeuralNPC npc) => npc && assigned?.Invoke(npc) == true;
    internal static bool InField(Companion person) => person.Actor && active?.Invoke(person.Actor) == true;
    internal static bool Combatant(Companion person) => person.Recruited || InField(person);
    public static void Prepare(NeuralNPC npc) => State.Get(npc);
    public static bool Unavailable(NeuralNPC npc) => State.Find(npc, out var p) && (p.Down || p.Recruited);
    public static bool IsDown(NeuralNPC npc) => State.Find(npc, out var p) && p.Down;
    public static void Assist(NeuralNPC npc)
    {
        if (State.Find(npc, out var p) && p.Down) Supplies.Revive(p);
    }

    // Replayable by the guild after a native save and companion file are restored.
    public static bool Pay(NeuralNPC npc, string receipt, int gold, string itemData)
    {
        if (!npc || string.IsNullOrEmpty(receipt) || gold < 0) return false;
        var p = State.Get(npc);
        if (p.ExpeditionReceipts.Contains(receipt)) return true;
        string coins = receipt + "/gold";
        if (!p.ExpeditionReceipts.Contains(coins))
        {
            if (gold > int.MaxValue - p.Gold) return false;
            p.Gold += gold;
            p.ExpeditionReceipts.Add(coins);
        }
        if (!string.IsNullOrEmpty(itemData))
        {
            using var stream = new MemoryStream(Convert.FromBase64String(itemData));
            using var reader = new BinaryReader(stream);
            var item = new Item();
            item.Deserialize(reader);
            if (!p.Pack.TryAddItem(item)) return false;
            p.Items.Add(new PackEntry { Item = item, Name = item.name, Sprite = item.spriteName });
        }
        p.ExpeditionReceipts.Add(receipt);
        return true;
    }
}
