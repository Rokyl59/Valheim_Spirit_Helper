using System;
using System.Collections.Generic;

namespace SpiritHelper.Core;

[Serializable]
public sealed class KnownResourceData
{
    public string Resource = string.Empty;
    public float[] Position = new float[3];
    public int LastSeenDay;
    public bool Depleted;
}

[Serializable]
public sealed class NamedZoneData
{
    public string Name = string.Empty;
    public NamedZoneType Type;
    public float[] Position = new float[3];
    public float Radius = 40f;
}

[Serializable]
public sealed class CourierRouteData
{
    public bool Enabled;
    public float[] Source = new float[3];
    public float[] Destination = new float[3];
    public string Resource = string.Empty;
}

[Serializable]
public sealed class SpiritRuleData
{
    public SpiritRuleTrigger Trigger;
    public SpiritRuleAction Action;
    public bool Enabled = true;
}

[Serializable]
public sealed class ScheduleEntryData
{
    public float StartHour;
    public float EndHour;
    public SpiritJob Job;
    public string ZoneName = string.Empty;
}

[Serializable]
public sealed class BiomeKnowledgeData
{
    public string Biome = string.Empty;
    public float Experience;
}

[Serializable]
public sealed class ResourceStatisticData
{
    public string Resource = string.Empty;
    public long Count;
}

[Serializable]
public sealed class DecisionRecordData
{
    public string Time = string.Empty;
    public string Message = string.Empty;
}

[Serializable]
public sealed class SpiritAutomationData
{
    public List<KnownResourceData> WorldMemory = new List<KnownResourceData>();
    public List<NamedZoneData> NamedZones = new List<NamedZoneData>();
    public CourierRouteData CourierRoute = new CourierRouteData();
    public List<SpiritRuleData> Rules = new List<SpiritRuleData>();
    public List<ScheduleEntryData> Schedule = new List<ScheduleEntryData>();
    public List<BiomeKnowledgeData> BiomeKnowledge = new List<BiomeKnowledgeData>();
    public List<ResourceStatisticData> ResourceStatistics = new List<ResourceStatisticData>();
    public List<DecisionRecordData> DecisionHistory = new List<DecisionRecordData>();
    public bool SuggestionsEnabled = true;
    public bool RepeatOrders;
    public bool ScheduleEnabled;
    public bool RulesEnabled;
    public bool BaseAlarmEnabled;
    public bool SecretDetectorEnabled;
    public int MaintainStock;
    public bool ShrineSet;
    public float[] ShrinePosition = new float[3];
    public int ShrineLevel = 1;
    public float Stability = 100f;
    public int PrestigeCount;
    public string Legacy = string.Empty;
    public float LongestTrip;
    public long ItemsRescued;
    public double TimeTogetherSeconds;
    public string LastBiome = string.Empty;
    public float[] LastDeathPosition = new float[3];
    public bool HasLastDeathPosition;
    public float[] LastBoatPosition = new float[3];
    public bool HasLastBoatPosition;
    public float[] LastBedPosition = new float[3];
    public bool HasLastBedPosition;
}
