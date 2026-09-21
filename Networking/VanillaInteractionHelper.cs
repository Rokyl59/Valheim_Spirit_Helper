using SpiritHelper.AI;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.Networking;

public sealed class VanillaInteractionHelper
{
    public bool TryWork(SpiritTarget target, Player player, BalanceMode mode, SpiritProgression progression,
        bool consumeDurability, Vector3 attackOrigin, out string error)
    {
        error = string.Empty;
        if (!target.IsValid) { error = "Цель больше не существует."; return false; }
        if (!PrivateArea.CheckAccess(target.Position, 0f, false, false)) { error = "Цель защищена охранным тотемом."; return false; }
        if (target.Definition.DamageType == ResourceDamageType.Interact)
        {
            var pickable = target.Component as Pickable;
            if (!pickable || !pickable.CanBePicked()) { error = "Ресурс пока нельзя собрать."; return false; }
            return pickable.Interact(player, false, false);
        }

        var tool = FindTool(player, target.Definition.RequiredToolType, target.Definition.MinToolTier);
        if (mode == BalanceMode.Vanilla && tool == null)
        {
            error = "Дух не может добыть этот ресурс. Требуется подходящий инструмент.";
            return false;
        }
        if (mode == BalanceMode.Balanced && progression.ProfessionLevel(target.Definition.Profession) < target.Definition.RequiredProfessionLevel)
        {
            error = $"Требуется {target.Definition.Profession} {target.Definition.RequiredProfessionLevel}.";
            return false;
        }

        var damage = new HitData.DamageTypes();
        if (tool != null)
        {
            var toolDamage = tool.GetDamage();
            if (target.Definition.DamageType == ResourceDamageType.Chop) damage.m_chop = toolDamage.m_chop;
            else damage.m_pickaxe = toolDamage.m_pickaxe;
        }
        if (mode != BalanceMode.Vanilla)
        {
            var power = 8f + progression.Level * 0.8f + progression.ProfessionLevel(target.Definition.Profession) * 1.2f;
            if (mode == BalanceMode.Free) power *= 5f;
            damage = new HitData.DamageTypes();
            if (target.Definition.DamageType == ResourceDamageType.Chop) damage.m_chop = power;
            else damage.m_pickaxe = power;
        }
        var hit = new HitData
        {
            m_damage = damage,
            m_point = target.HitPoint,
            m_hitCollider = target.HitCollider,
            m_radius = 0f,
            m_dir = (target.HitPoint - attackOrigin).normalized,
            m_toolTier = (short)(tool?.m_shared.m_toolTier ?? target.Definition.MinToolTier),
            m_skill = target.Definition.RequiredToolType == RequiredToolType.Axe ? Skills.SkillType.WoodCutting : Skills.SkillType.Pickaxes,
            m_skillLevel = tool == null ? progression.ProfessionLevel(target.Definition.Profession) : player.GetSkillLevel(target.Definition.RequiredToolType == RequiredToolType.Axe ? Skills.SkillType.WoodCutting : Skills.SkillType.Pickaxes)
        };
        hit.SetAttacker(player);
        ApplyDamage(target.Component, hit);
        if (consumeDurability && mode == BalanceMode.Vanilla && tool is { } usedTool && usedTool.m_shared.m_useDurability)
        {
            usedTool.m_durability = Mathf.Max(0f, usedTool.m_durability - Mathf.Max(0.1f, usedTool.m_shared.m_useDurabilityDrain));
        }
        return true;
    }

    private static ItemDrop.ItemData? FindTool(Player player, RequiredToolType required, int tier)
    {
        if (required == RequiredToolType.None) return null;
        ItemDrop.ItemData? best = null;
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (item.m_durability <= 0f || item.m_shared.m_toolTier < tier) continue;
            var damage = item.GetDamage();
            var suitable = required == RequiredToolType.Axe ? damage.m_chop > 0f : damage.m_pickaxe > 0f;
            if (suitable && (best == null || item.GetDamage().GetTotalDamage() > best.GetDamage().GetTotalDamage())) best = item;
        }
        return best;
    }

    private static void ApplyDamage(Component component, HitData hit)
    {
        if (component is TreeBase tree) tree.Damage(hit);
        else if (component is TreeLog log) log.Damage(hit);
        else if (component is MineRock5 rock5) rock5.Damage(hit);
        else if (component is MineRock rock) rock.Damage(hit);
        else if (component is Destructible destructible) destructible.Damage(hit);
    }
}
