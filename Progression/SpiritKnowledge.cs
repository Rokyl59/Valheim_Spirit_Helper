using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.AI;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Progression;

public sealed class SpiritKnowledge
{
    private const float SameResourceDistance = 4f;
    private readonly SpiritSaveData _data;

    public SpiritKnowledge(SpiritSaveData data) => _data = data;

    public void RememberSurvey(Dictionary<string, TargetScanner.SurveyEntry> survey, int day)
    {
        foreach (var finding in survey)
            foreach (var position in finding.Value.Positions)
                Remember(finding.Key, position, day);
    }

    public bool Remember(string resource, Vector3 position, int day)
    {
        var squaredDistance = SameResourceDistance * SameResourceDistance;
        var existing = _data.Automation.WorldMemory.FirstOrDefault(entry =>
            string.Equals(entry.Resource, resource, StringComparison.OrdinalIgnoreCase) &&
            Vector3.SqrMagnitude(ToVector(entry.Position) - position) <= squaredDistance);
        if (existing != null)
        {
            existing.Position = ToArray(position);
            existing.LastSeenDay = day;
            existing.Depleted = false;
            return false;
        }
        _data.Automation.WorldMemory.Add(new KnownResourceData
        {
            Resource = resource,
            Position = ToArray(position),
            LastSeenDay = day
        });
        return true;
    }

    public KnownResourceData? FindNearest(string resource, Vector3 origin) =>
        _data.Automation.WorldMemory
            .Where(entry => !entry.Depleted && (string.IsNullOrWhiteSpace(resource) ||
                string.Equals(entry.Resource, resource, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => Vector3.SqrMagnitude(ToVector(entry.Position) - origin))
            .FirstOrDefault();

    public void MarkDepleted(Vector3 position)
    {
        var nearest = _data.Automation.WorldMemory
            .OrderBy(entry => Vector3.SqrMagnitude(ToVector(entry.Position) - position))
            .FirstOrDefault();
        if (nearest != null && Vector3.Distance(ToVector(nearest.Position), position) <= 6f) nearest.Depleted = true;
    }

    public bool VisitBiome(Heightmap.Biome biome, float deltaTime)
    {
        var name = biome.ToString();
        var knowledge = _data.Automation.BiomeKnowledge.FirstOrDefault(entry => entry.Biome == name);
        var firstVisit = knowledge == null;
        if (knowledge == null)
        {
            knowledge = new BiomeKnowledgeData { Biome = name };
            _data.Automation.BiomeKnowledge.Add(knowledge);
        }
        knowledge.Experience = Mathf.Min(100f, knowledge.Experience + deltaTime * 0.02f);
        _data.Automation.LastBiome = name;
        return firstVisit;
    }

    public float BiomeKnowledge(Heightmap.Biome biome) =>
        _data.Automation.BiomeKnowledge.FirstOrDefault(entry => entry.Biome == biome.ToString())?.Experience ?? 0f;

    public long RecordResource(string resource, long amount = 1)
    {
        var statistic = _data.Automation.ResourceStatistics.FirstOrDefault(entry => entry.Resource == resource);
        if (statistic == null)
        {
            statistic = new ResourceStatisticData { Resource = resource };
            _data.Automation.ResourceStatistics.Add(statistic);
        }
        statistic.Count += amount;
        return statistic.Count;
    }

    public string FavoriteResource => _data.Automation.ResourceStatistics
        .OrderByDescending(entry => entry.Count)
        .FirstOrDefault()?.Resource ?? "—";

    public void RecordDecision(string message)
    {
        _data.Automation.DecisionHistory.Add(new DecisionRecordData
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Message = message
        });
        while (_data.Automation.DecisionHistory.Count > 40) _data.Automation.DecisionHistory.RemoveAt(0);
    }

    public static Vector3 ToVector(float[] value) => value.Length == 3 ? new Vector3(value[0], value[1], value[2]) : Vector3.zero;
    public static float[] ToArray(Vector3 value) => new[] { value.x, value.y, value.z };
}
