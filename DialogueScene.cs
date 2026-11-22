using UnityEngine;

namespace CompanionParty;

internal static class DialogueScene
{
    internal static string Append(NeuralNPC npc, string environment)
    {
        if (!npc) return environment;
        bool tracked = State.Find(npc, out var person);
        bool combatant = tracked && ExpeditionApi.Combatant(person);
        // Outside party combat, custom NPCs can take damage through native health.
        var native = !combatant ? npc.GetComponent<Damagable>() : null;
        bool nativeInjury = native && native.MaxHP > 0 && native.HP < native.MaxHP;
        if (!tracked && !nativeInjury) return environment;

        string name = npc.GetFinalName();
        if (tracked && person.ManagesOutfit)
        {
            // The native environment appends this sentence to the speaker's character description.
            // The equipment section handles it so Revealing and the dialogue toggle also apply here.
            string note = " (" + name + " is currently naked.)";
            int index = environment.LastIndexOf(note, System.StringComparison.Ordinal);
            if (index >= 0 && (index + note.Length == environment.Length || environment[index + note.Length] == '\n'))
                environment = environment.Remove(index, note.Length);
        }
        string condition;
        if (tracked && person.Down)
            condition = $"{name} is alive and incapacitated. They cannot stand or fight and can be helped up.";
        else
        {
            int hp = nativeInjury ? native.HP : person.HP;
            int max = nativeInjury ? native.MaxHP : person.LastMaxHP;
            condition = $"{name} is {Injury(hp, max)}.";
            if (tracked && Combat.Recent(person.LastRevivedTurn))
                condition += person.HelpedUpByPlayer ? " The player recently helped them back to their feet." : " They recently got back to their feet.";
            if (tracked && hp < max && person.HealingTurns > 0)
                condition += " A healing potion is taking effect.";
            if (combatant)
                condition += DescribeCombat(person);
        }
        string scene = environment + "\n" + condition;
        if (!tracked) return scene;
        string equipment = EquipmentKnowledge.Describe(person);
        if (equipment.Length > 0) scene += "\n" + equipment;
        return scene + "\n" + PartyUI.DescribePack(person);
    }

    private static string Injury(int hp, int max)
    {
        float fraction = hp / (float)Mathf.Max(1, max);
        if (fraction >= 1f) return "uninjured";
        if (fraction <= .25f) return "critically wounded";
        if (fraction <= .5f) return "badly wounded";
        return fraction <= .75f ? "wounded" : "lightly wounded";
    }

    private static string DescribeCombat(Companion p)
    {
        var opponent = p.EngagedEnemy;
        var attacker = p.Attacker;
        bool fighting = Combat.Recent(p.CombatTurn, 1) && Combat.Hostile(opponent) && Combat.InSight(p, opponent);
        bool attacked = Combat.Recent(p.AttackedTurn) && Combat.Hostile(attacker) && Combat.InSight(p, attacker);
        if (!fighting && !attacked) return " They are currently out of combat.";
        if (fighting && attacked && opponent == attacker)
            return $" They are fighting {OpponentName(opponent)}, which recently attacked them.";
        string action = fighting ? $" They are fighting {OpponentName(opponent)}." : "";
        if (attacked) action += $" They recently came under attack from {OpponentName(attacker)}, still nearby.";
        return action;
    }

    private static string OpponentName(Enemy enemy)
    {
        var visible = enemy.GetComponent<INPCVisibleObject>();
        if (visible == null || visible is NPCVisibleObject { description: null }) return "a hostile creature";
        string description = visible.GetNPCVisibleObjectDescription();
        return string.IsNullOrWhiteSpace(description) ? "a hostile creature" : description.Trim().TrimEnd('.');
    }
}
