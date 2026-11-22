using System;
using System.Collections.Generic;
using System.Linq;

namespace CompanionParty;

public enum PartyActivity { TravelledTogether, HelpedUp }

/// <summary>Additive benefits for one recruited NPC. Default values grant no benefit.</summary>
public struct PartySkillBonuses
{
    public int MaxHealth, MaxEnergy, CarryCapacity, AttackDamage;
    public int NormalArmor, FireArmor, FrostArmor;
    // Chances are fractions; 0.10 means ten percentage points.
    public float MeleeCriticalChance, MeleeParryChance, ExtraStepChance, DurabilityPreservationChance;
    public float HealthRegenerationPerTurn, PotionPotencyBonus;
}

/// <summary>Main-thread integration for mods that own player or companion skills.</summary>
public static class PartySkillApi
{
    /// <summary>One completed party crossing or player-assisted revival. The snapshot contains the participating recruits.</summary>
    public static event Action<Player, PartyActivity, IReadOnlyList<NeuralNPC>> PlayerActivity;

    internal static bool ObservingActivity => PlayerActivity != null;
    internal static void Completed(PartyActivity activity, List<NeuralNPC> participants)
    {
        var handlers = PlayerActivity;
        var player = Player.Instance;
        if (handlers == null || !player || SerializationManager.loadingSave || participants.Count == 0) return;
        var snapshot = Array.AsReadOnly(participants.ToArray());
        foreach (Action<Player, PartyActivity, IReadOnlyList<NeuralNPC>> handler in handlers.GetInvocationList())
        {
            try { handler(player, activity, snapshot); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"Party activity observer {handler.Method} failed: {ex}"); }
        }
    }

    private static Registration[] providers = Array.Empty<Registration>();
    private static int evaluating;

    /// <summary>Register a unique source. Dispose the returned handle to remove its benefits.</summary>
    public static IDisposable Register(string id, Func<Player, NeuralNPC, PartySkillBonuses> getBonuses)
    {
        CheckWritable();
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Supply a skill provider ID.", nameof(id));
        if (getBonuses == null) throw new ArgumentNullException(nameof(getBonuses));
        if (providers.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id)))
            throw new InvalidOperationException("Party skill provider already registered: " + id);
        var registration = new Registration(id, getBonuses);
        providers = providers.Append(registration).ToArray();
        Refresh();
        return registration;
    }

    /// <summary>Re-evaluate current party members after a rank, perk or skill save changes.</summary>
    public static void Refresh()
    {
        CheckWritable();
        foreach (var member in State.Save.People.Values.ToArray())
            if (member.Recruited && member.Actor) member.RefreshSkills();
        PartyUI.RefreshSkillBenefits();
    }

    internal static PartySkillBonuses[] Resolve(NeuralNPC npc)
    {
        var player = Player.Instance;
        if (!player || !npc || providers.Length == 0) return Array.Empty<PartySkillBonuses>();
        var bonuses = new PartySkillBonuses[providers.Length];
        evaluating++;
        try
        {
            for (int i = 0; i < providers.Length; i++) bonuses[i] = providers[i].Read(player, npc);
        }
        finally { evaluating--; }
        return bonuses;
    }

    private static void CheckWritable()
    {
        if (evaluating != 0) throw new InvalidOperationException("Party skill callbacks must return their bonuses before changing registrations or refreshing.");
    }

    private sealed class Registration : IDisposable
    {
        internal readonly string Id;
        private readonly Func<Player, NeuralNPC, PartySkillBonuses> getBonuses;
        private bool loggedFailure;

        internal Registration(string id, Func<Player, NeuralNPC, PartySkillBonuses> getBonuses)
        { Id = id; this.getBonuses = getBonuses; }

        internal PartySkillBonuses Read(Player player, NeuralNPC npc)
        {
            try { return getBonuses(player, npc); }
            catch (Exception ex)
            {
                if (!loggedFailure) Plugin.Log?.LogWarning($"Party skill provider '{Id}' failed for {npc.GetFinalName()}: {ex}");
                loggedFailure = true;
                return default;
            }
        }

        public void Dispose()
        {
            if (Array.IndexOf(providers, this) < 0) return;
            CheckWritable();
            providers = providers.Where(p => p != this).ToArray();
            Refresh();
        }
    }
}
