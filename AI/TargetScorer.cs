using SpiritHelper.Core;
using System.Collections.Generic;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public static class TargetScorer
{
    private const float WorkedTargetLifetime = 30f;
    private const float WorkedTargetBonus = 12f;
    private static readonly Dictionary<int, float> WorkedTargets = new Dictionary<int, float>();

    public static void MarkWorked(SpiritTarget target)
    {
        if (WorkedTargets.Count >= 256)
        {
            var expired = new List<int>();
            foreach (var entry in WorkedTargets) if (entry.Value <= Time.time) expired.Add(entry.Key);
            foreach (var key in expired) WorkedTargets.Remove(key);
            if (WorkedTargets.Count >= 256) WorkedTargets.Clear();
        }
        WorkedTargets[target.Key] = Time.time + WorkedTargetLifetime;
    }

    public static float Score(SpiritTarget target, Vector3 origin, SpiritJob job, Vector3? unloadPoint,
        SpiritProgression progression)
    {
        var score = Vector3.Distance(origin, target.Position);
        var woodcuttingTier = progression.TalentTier("woodcutting");
        var miningTier = progression.TalentTier("mining");

        if (target.Component is TreeLog) score -= 1000f + woodcuttingTier * 75f;
        if (target.Definition.Category == ResourceCategory.Ore) score -= 4f + miningTier * 3f;
        if (target.Definition.Category == ResourceCategory.Rare) score -= 8f;
        if (job == SpiritJob.Gathering && target.Definition.Category == ResourceCategory.Food) score -= 2f;
        if (WorkedTargets.TryGetValue(target.Key, out var until))
        {
            if (Time.time < until) score -= WorkedTargetBonus;
            else WorkedTargets.Remove(target.Key);
        }
        if (unloadPoint.HasValue)
        {
            var routeDistance = Vector3.Distance(target.Position, unloadPoint.Value);
            var directDistance = Vector3.Distance(origin, unloadPoint.Value);
            score += Mathf.Max(0f, routeDistance - directDistance) * 0.12f;
        }
        return score;
    }
}
