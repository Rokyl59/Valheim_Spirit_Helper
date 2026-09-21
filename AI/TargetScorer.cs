using SpiritHelper.Core;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public static class TargetScorer
{
    public static float Score(SpiritTarget target, Vector3 origin, SpiritJob job, Vector3? unloadPoint)
    {
        var score = Vector3.Distance(origin, target.Position);
        if (target.Component is TreeLog) score -= 1000f;
        if (target.Definition.Category == ResourceCategory.Ore) score -= 4f;
        if (target.Definition.Category == ResourceCategory.Rare) score -= 8f;
        if (job == SpiritJob.Gathering && target.Definition.Category == ResourceCategory.Food) score -= 2f;
        if (unloadPoint.HasValue)
        {
            var routeDistance = Vector3.Distance(target.Position, unloadPoint.Value);
            var directDistance = Vector3.Distance(origin, unloadPoint.Value);
            score += Mathf.Max(0f, routeDistance - directDistance) * 0.12f;
        }
        return score;
    }
}
