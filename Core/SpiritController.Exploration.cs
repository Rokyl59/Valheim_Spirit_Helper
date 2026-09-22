using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.AI;
using SpiritHelper.Audio;
using SpiritHelper.Visuals;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const float ExpeditionDuration = 45f;
    private const float ExpeditionSurveyInterval = 2f;
    private const float ExpeditionSurveyRadius = 24f;
    private readonly Dictionary<string, HashSet<Vector3Int>> _expeditionFinds = new Dictionary<string, HashSet<Vector3Int>>();
    private Vector3 _expeditionOrigin;
    private Vector3 _lastExpeditionPosition;
    private float _expeditionVisitedDistance;
    private float _nextExpeditionSurvey;
    private bool _expeditionReturning;

    private void Scout(Player player, float deltaTime)
    {
        SetState(SpiritState.Scout);
        _visual!.Move(player.transform.position + player.transform.forward * 22f + Vector3.up * 8f, 13f, deltaTime);
        SpendEnergy(0.3f * deltaTime);
        if (Time.time < _nextScoutReport) return;

        _nextScoutReport = Time.time + 6f;
        var scoutPosition = _visual.Transform.position;
        var radius = Mathf.Min((45f + TalentTier("exploration") * 10f + _knowledge.BiomeKnowledge(player.GetCurrentBiome()) * 0.2f) *
            _shrineScoutMultiplier, EffectiveSearchRadius);
        var report = _scanner.Survey(scoutPosition, radius, _resourceSelection, _progression);
        var knownBefore = _progression.Data.Automation.WorldMemory.Count;
        _knowledge.RememberSurvey(report, CurrentDay());
        if (report.Count == 0) { _ui.Notify("Разведка: подходящих ресурсов не найдено.", 3f); return; }

        var lines = new List<string>();
        TargetScanner.SurveyEntry? nearest = null;
        string? nearestName = null;
        foreach (var entry in report)
        {
            lines.Add($"{ResourceName(entry.Key)}: {entry.Value.Count}");
            if (nearest == null || entry.Value.NearestDistance < nearest.NearestDistance)
            {
                nearest = entry.Value;
                nearestName = entry.Key;
            }
        }
        if (nearest != null)
        {
            _markers.ShowTemporaryMarker(nearest.NearestPosition, new Color(0.35f, 0.9f, 1f));
            var direction = nearest.NearestPosition - player.transform.position;
            lines.Add($"Ближайшее: {ResourceName(nearestName ?? string.Empty)}, {direction.magnitude:0} м, {DirectionName(direction)}");
        }
        _ui.Notify("Разведка завершена\n" + string.Join(" • ", lines), 6f);
        if (_progression.Data.Automation.WorldMemory.Count > knownBefore)
            AddJournal($"Разведка открыла новые места: {_progression.Data.Automation.WorldMemory.Count - knownBefore}.");
    }

    private void Guard(Player player, float deltaTime)
    {
        SetState(SpiritState.Guard);
        var nearestDistance = _guardThreat && !_guardThreat.IsDead()
            ? Vector3.SqrMagnitude(_guardThreat.transform.position - player.transform.position) : 25f * 25f;
        if (Time.time >= _nextGuardScan)
        {
            _nextGuardScan = Time.time + 0.75f;
            _guardThreat = null;
            nearestDistance = 25f * 25f;
            foreach (var character in Character.GetAllCharacters())
            {
                if (!character || character == player || character.IsDead() || !BaseAI.IsEnemy(player, character)) continue;
                var distance = Vector3.SqrMagnitude(character.transform.position - player.transform.position);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                _guardThreat = character;
            }
        }
        var point = _guardThreat
            ? Vector3.Lerp(player.transform.position, _guardThreat.transform.position, 0.55f) + Vector3.up * 2.5f
            : player.transform.position + new Vector3(Mathf.Cos(Time.time), 0.85f, Mathf.Sin(Time.time)) * 3f + Vector3.up * 1.7f;
        _visual!.Move(point, 8f, deltaTime);
        if (_guardThreat && Time.time >= _nextGuardNotice)
        {
            _nextGuardNotice = Time.time + 5f;
            var threatPosition = _guardThreat.transform.position;
            var critical = player.GetHealthPercentage() < 0.3f;
            var direction = threatPosition - player.transform.position;
            var offscreen = IsOffscreen(threatPosition);
            _ui.Notify(critical
                ? $"Опасность: мало здоровья, враг {DirectionName(direction)}, {Mathf.Sqrt(nearestDistance):0} м!"
                : $"Дозор: враг {DirectionName(direction)}, {Mathf.Sqrt(nearestDistance):0} м{(offscreen ? " вне поля зрения" : string.Empty)}.", 3f);
            _audio?.Play(SpiritAudioEvent.DangerDetected);
            _visual.ShowEmotion(SpiritEmotion.Alert, critical ? 5f : 2f);
            _markers.ShowTemporaryMarker(threatPosition, Color.red, 4f);
        }
    }

    private void Guide(Player player, float deltaTime)
    {
        SetState(SpiritState.Guide);
        if (!_guideDestination.HasValue) { Follow(player, deltaTime); return; }
        var destination = _guideDestination.Value;
        var distance = Vector3.Distance(player.transform.position, destination);
        if (distance <= 4f)
        {
            _ui.Notify($"Мы пришли: {_guideLabel}.");
            _guideDestination = null;
            SetJob(SpiritJob.Follow);
            return;
        }
        var direction = (destination - player.transform.position).normalized;
        _visual!.Move(player.transform.position + direction * Mathf.Min(9f, distance) + Vector3.up * 2.5f, 12f, deltaTime);
    }

    private void ExploreDungeon(Player player, float deltaTime)
    {
        SetState(SpiritState.Scout);
        if (!player.InInterior())
        {
            _ui.Notify("Дух может исследовать только загруженное подземелье рядом с вами.", 4f);
            SetJob(SpiritJob.Follow);
            return;
        }
        var point = player.transform.position + player.transform.forward * 5f + Vector3.up * 2.2f;
        _visual!.Move(point, 10f, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, player.transform.position) > 14f)
            _visual.Move(player.transform.position + Vector3.up * 2f, 12f, deltaTime);
        if (Time.time < _nextAdvancedTick) return;

        _nextAdvancedTick = Time.time + 2f;
        var scoutPosition = _visual.Transform.position;
        var drop = _carry.FindNearest(scoutPosition, 14f, _resourceSelection);
        if (drop.HasValue) _markers.ShowTemporaryMarker(drop.Value, new Color(0.8f, 0.65f, 1f), 8f);
        DetectNearbySecret(scoutPosition, 18f);
        TickThreatWarning(player, 18f);
    }

    private void Expedition(Player player, float deltaTime)
    {
        if (_expeditionReturning)
        {
            SetState(SpiritState.Guide);
            var returnPoint = player.transform.position + Vector3.up * 2f;
            _visual!.Move(returnPoint, 18f, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, returnPoint) > 3f) return;
            _expeditionReturning = false;
            SetJob(SpiritJob.Follow);
            return;
        }

        SetState(SpiritState.Expedition);
        if (_expeditionEndsAt <= 0f) StartExpedition(player);
        var maximumRange = Mathf.Min(_config.MaxDistanceFromPlayer.Value, EffectiveSearchRadius);
        var progress = 1f - Mathf.Clamp01((_expeditionEndsAt - Time.time) / ExpeditionDuration);
        var angle = progress * Mathf.PI * 8f;
        var radius = Mathf.Lerp(12f, maximumRange, progress);
        var target = _expeditionOrigin + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * radius + Vector3.up * 10f;
        _visual!.Move(target, 18f, deltaTime);
        TrackExpeditionPosition(maximumRange);
        if (Time.time >= _nextExpeditionSurvey)
        {
            _nextExpeditionSurvey = Time.time + ExpeditionSurveyInterval;
            CollectExpeditionSurvey(maximumRange);
        }
        if (Time.time < _expeditionEndsAt) return;
        FinishExpedition(player);
    }

    private void StartExpedition(Player player)
    {
        _expeditionEndsAt = Time.time + ExpeditionDuration;
        _expeditionOrigin = player.transform.position;
        _lastExpeditionPosition = _visual!.Transform.position;
        _expeditionVisitedDistance = 0f;
        _nextExpeditionSurvey = Time.time;
        _expeditionFinds.Clear();
        _ui.Notify("Дух отправился в разведочную экспедицию по загруженной местности.");
    }

    private void TrackExpeditionPosition(float maximumRange)
    {
        var position = _visual!.Transform.position;
        if (Vector3.Distance(_expeditionOrigin, position) <= maximumRange)
            _expeditionVisitedDistance += Vector3.Distance(_lastExpeditionPosition, position);
        _lastExpeditionPosition = position;
    }

    private void CollectExpeditionSurvey(float maximumRange)
    {
        var surveyPosition = _visual!.Transform.position;
        if (Vector3.Distance(_expeditionOrigin, surveyPosition) > maximumRange) return;
        var survey = _scanner.Survey(surveyPosition, ExpeditionSurveyRadius, _resourceSelection, _progression);
        _knowledge.RememberSurvey(survey, CurrentDay());
        foreach (var entry in survey)
        {
            if (!_expeditionFinds.TryGetValue(entry.Key, out var positions))
            {
                positions = new HashSet<Vector3Int>();
                _expeditionFinds[entry.Key] = positions;
            }
            foreach (var position in entry.Value.Positions)
                if (Vector3.Distance(_expeditionOrigin, position) <= maximumRange)
                    positions.Add(Vector3Int.RoundToInt(position));
        }
    }

    private void FinishExpedition(Player player)
    {
        _expeditionEndsAt = 0f;
        var lines = new List<string>();
        Vector3? nearestPosition = null;
        string? nearestName = null;
        var nearestDistance = float.MaxValue;
        foreach (var entry in _expeditionFinds.OrderBy(item => item.Key))
        {
            lines.Add($"{ResourceName(entry.Key)}: {entry.Value.Count}");
            foreach (var position in entry.Value)
            {
                var distance = Vector3.Distance(player.transform.position, (Vector3)position);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestPosition = position;
                nearestName = entry.Key;
            }
        }
        if (nearestPosition.HasValue)
        {
            var direction = nearestPosition.Value - player.transform.position;
            lines.Add($"Ближайшее: {ResourceName(nearestName ?? string.Empty)}, {nearestDistance:0} м, {DirectionName(direction)}");
            _markers.ShowTemporaryMarker(nearestPosition.Value, new Color(0.35f, 0.9f, 1f));
        }
        _ui.Notify(lines.Count == 0
            ? $"Экспедиция завершена: пройдено {_expeditionVisitedDistance:0} м, подходящих ресурсов не найдено."
            : $"Экспедиция завершена: пройдено {_expeditionVisitedDistance:0} м\n" + string.Join(" • ", lines), 7f);
        AddJournal($"Экспедиция обследовала {_expeditionVisitedDistance:0} м и нашла {_expeditionFinds.Count} типов ресурсов.");
        _expeditionReturning = true;
    }

    private static bool IsOffscreen(Vector3 position)
    {
        var camera = Camera.main;
        if (!camera) return true;
        var viewport = camera.WorldToViewportPoint(position);
        return viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;
    }
}
