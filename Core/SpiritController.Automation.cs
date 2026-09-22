using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.Audio;
using SpiritHelper.Logistics;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using SpiritHelper.Visuals;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const int AutomationRequiredLevel = 25;
    private const float CargoThreshold = 0.8f;
    private const float InventoryThreshold = 0.9f;
    private const float LongTermScanInterval = 12f;
    private readonly Dictionary<int, string> _processedWorkOrders = new Dictionary<int, string>();
    private int _activeScheduleIndex = -1;
    private bool _resumeAfterThreat;
    private float _nextLongTermWorldScan;
    private Vector3? _orderDeliveryPoint;
    private float _baseAlarmReturnUntil;
    private float _nextEnemyScan;
    private Vector3 _enemyScanCentre;
    private float _enemyScanRadius;
    private int _enemyScanCount;

    private void TickLongTermProgress(Player player, float deltaTime)
    {
        var automation = _progression.Data.Automation;
        automation.TimeTogetherSeconds += deltaTime;
        var biome = player.GetCurrentBiome();
        if (_knowledge.VisitBiome(biome, deltaTime)) AddJournal($"Впервые посещён биом {biome}.");
        var isDead = player.IsDead();
        if (isDead && !_wasDead)
        {
            automation.LastDeathPosition = ToArray(player.transform.position);
            automation.HasLastDeathPosition = true;
            if (_visual != null) _carry.Release(_visual.Transform.position);
            if (_visual != null) _production.Cancel(_visual.Transform.position);
            AddJournal("Дух запомнил место гибели игрока.");
        }
        if (!isDead && _wasDead)
        {
            _celebrateUntil = Time.time + 2.5f;
            _ui.Notify("Дух рад вашему возвращению.");
        }
        _wasDead = isDead;

        var enemies = CountEnemies(player.transform.position, 25f);
        var biomeFear = biome switch
        {
            Heightmap.Biome.Meadows => 1,
            Heightmap.Biome.BlackForest => 5,
            Heightmap.Biome.Swamp => 10,
            Heightmap.Biome.Mountain => 15,
            Heightmap.Biome.Plains => 20,
            Heightmap.Biome.Mistlands => 25,
            _ => 30
        };
        var shouldFear = enemies >= 4 || _progression.Level < biomeFear;
        var stabilityChange = shouldFear ? -7f : _progression.Data.Personality == "Спокойный" ? 6f : 4f;
        automation.Stability = Mathf.Clamp(automation.Stability + stabilityChange * deltaTime, 0f, 100f);
        if (_progression.Data.Personality == "Осторожный" && enemies > 0 && _job is not SpiritJob.Follow and not SpiritJob.Guard and not SpiritJob.Rest and not SpiritJob.Stopped)
        {
            _jobBeforeRest = _job;
            _resumeAfterThreat = true;
            _job = SpiritJob.Follow;
            _target = null;
        }
        else if (_progression.Data.Personality == "Осторожный" && enemies == 0 && _resumeAfterThreat && _job == SpiritJob.Follow)
        {
            _resumeAfterThreat = false;
            _job = _jobBeforeRest;
            _target = null;
            _knowledge.RecordDecision($"Cautious: threat cleared → resume {_job}");
        }
        if (automation.Stability < 20f && Time.time >= _lastSuggestion + 10f)
        {
            _lastSuggestion = Time.time;
            _ui.Notify("✦ Дух напуган и держится ближе к игроку.", 4f);
            _audio?.Play(SpiritAudioEvent.DangerDetected);
        }
        if (Time.time >= _nextEmotion)
        {
            _nextEmotion = Time.time + 15f;
            var bossNearby = Character.GetAllCharacters().Any(character => character && character != player &&
                !character.IsDead() && character.IsBoss() && Vector3.Distance(character.transform.position, player.transform.position) < 60f);
            if (bossNearby)
            {
                _visual?.ShowEmotion(SpiritEmotion.Afraid, 5f);
                _ui.Notify("✦ Дух дрожит и прячется за вами: рядом могущественная угроза.", 5f);
            }
            else if (EnvMan.instance && EnvMan.instance.GetCurrentEnvironment().m_rainCloudAlpha > 0.65f)
            {
                _visual?.ShowEmotion(SpiritEmotion.Alert, 4f);
                _ui.Notify("✦ Вспышки грозы пробегают по духу.", 4f);
            }
            else if (player.IsSitting() && IsNearFire(player.transform.position))
            {
                _visual?.ShowEmotion(SpiritEmotion.Resting, 4f);
                _ui.Notify("✦ Дух спокойно отдыхает рядом с огнём.", 4f);
            }
            else if (automation.Stability < 20f) _visual?.ShowEmotion(SpiritEmotion.Afraid, 4f);
        }
        RememberNearbyAnchors(player, automation);
        if (biome == Heightmap.Biome.Mistlands && Time.time >= _nextScoutReport)
        {
            var probe = player.transform.position + player.transform.forward * 6f + Vector3.up * 2f;
            if (!Physics.Raycast(probe, Vector3.down, 12f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                _nextScoutReport = Time.time + 5f;
                _ui.Notify("✦ Впереди резкий обрыв.", 4f);
                _audio?.Play(SpiritAudioEvent.DangerDetected);
            }
        }
        if (_secretDetectorEnabled && Time.time >= _nextAdvancedTick)
        {
            _nextAdvancedTick = Time.time + 4f;
            DetectNearbySecret(player.transform.position, 24f);
        }
        if (_baseAlarmEnabled)
        {
            var home = automation.NamedZones.Find(zone => zone.Type == NamedZoneType.Home);
            if (home != null) TickBaseAlarm(player, SpiritKnowledge.ToVector(home.Position));
        }
    }

    private void TickAutomationRules(Player player)
    {
        if (Time.time < _nextRulesTick) return;
        _nextRulesTick = Time.time + 2f;
        if (_progression.Level >= AutomationRequiredLevel && _job is not SpiritJob.Stopped and not SpiritJob.Rest) ReadWorkOrders(player);
        if (_scheduleEnabled) ApplySchedule();
        if (_progression.Level >= AutomationRequiredLevel) MaintainStock(player);
        if (!_rulesEnabled || _progression.Level < AutomationRequiredLevel || _job is SpiritJob.Stopped or SpiritJob.Rest) return;

        var enemyNearby = CountEnemies(player.transform.position, 18f) > 0;
        if (!enemyNearby && _resumeAfterThreat && _job == SpiritJob.Follow)
        {
            _resumeAfterThreat = false;
            _job = _jobBeforeRest;
            _target = null;
            _knowledge.RecordDecision($"Rule: threat cleared → resume {_job}");
        }

        var rules = _progression.Data.Automation.Rules;
        var priorities = new[]
        {
            SpiritRuleTrigger.EnergyBelowTwenty,
            SpiritRuleTrigger.EnemyNearby,
            SpiritRuleTrigger.CargoEightyPercent,
            SpiritRuleTrigger.PlayerInventoryNinetyPercent
        };
        foreach (var trigger in priorities)
        {
            var rule = rules.FirstOrDefault(candidate => candidate.Enabled && candidate.Trigger == trigger);
            if (rule == null || !IsRuleTriggered(trigger, player, enemyNearby)) continue;
            if (ApplyRule(rule.Action)) break;
        }
    }

    private void ReadWorkOrders(Player player)
    {
        var hits = Physics.OverlapSphere(player.transform.position, WorkRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            var sign = hit ? hit.GetComponentInParent<Sign>() : null;
            if (!sign) continue;
            var text = sign.GetText().Trim();
            var signId = sign.GetInstanceID();
            if (!text.StartsWith("Spirit:", StringComparison.OrdinalIgnoreCase) ||
                _processedWorkOrders.TryGetValue(signId, out var previousText) && previousText == text ||
                !PrivateArea.CheckAccess(sign.transform.position, 0f, false, false)) continue;
            if (!TryParseWorkOrder(text, out var definition, out var quantity, out var radius, out var deliveryZone)) continue;
            if (!CanAutomate(definition))
            {
                _processedWorkOrders[signId] = text;
                _knowledge.RecordDecision($"Work order rejected by level: {definition.Name}");
                continue;
            }
            _processedWorkOrders[signId] = text;
            _resourceSelection.SelectExact(definition.Name);
            _selectedQuantity = Mathf.Max(0, quantity);
            _config.WorkRadius.Value = radius;
            _markers.SetWorkZone(sign.transform.position, radius, _config.ShowWorkZone.Value);
            _orderDeliveryPoint = deliveryZone == null ? null : SpiritKnowledge.ToVector(deliveryZone.Position);
            SetJob(JobFor(definition));
            _knowledge.RecordDecision($"Work order sign: {definition.Name} x{quantity}; radius {radius:0}; deliver {deliveryZone?.Name ?? "default"}");
            _ui.Notify($"Получен Work Order: {ResourceName(definition.Name)} × {(quantity == 0 ? "∞" : quantity.ToString())}.", 7f);
            return;
        }
    }

    private void MaintainStock(Player player)
    {
        var targetStock = _progression.Data.Automation.MaintainStock;
        if (targetStock <= 0 || _resourceSelection.Mode != ResourceFilterMode.Exact || _progression.Level < AutomationRequiredLevel) return;
        var definition = _resources.BySelection(_resourceSelection.ExactName);
        if (definition == null || !definition.CanAutoHarvest) return;
        if (!_markers.TryFindUnload(definition, _resourceSelection, player.transform.position, out var deliveryPoint)) return;
        var container = _markers.ResolveContainer(deliveryPoint);
        if (!container || !CarrySystem.CanAccessContainer(container)) return;
        var total = 0;
        foreach (var item in container.GetInventory().GetAllItems())
            if (item.m_dropPrefab && definition.DropNames.Contains(item.m_dropPrefab.name)) total += item.m_stack;
        if (total >= targetStock)
        {
            if (_job == JobFor(definition) && _carry.Count == 0 && _carry.PendingCount == 0)
                ActivateOrder(CaptureOrder(SpiritJob.Follow));
            else if (_job == JobFor(definition)) _forceDelivery = true;
            return;
        }
        if (_job == SpiritJob.Follow)
        {
            _selectedQuantity = targetStock - total;
            _orderDeliveryPoint = deliveryPoint;
            ActivateOrder(CaptureOrder(JobFor(definition)));
            _ui.Notify($"Запас {ResourceName(definition.Name)} ниже цели: {total}/{targetStock}. Работа возобновлена.");
        }
    }

    private void ApplySchedule()
    {
        if (!EnvMan.instance) return;
        var hour = EnvMan.instance.GetDayFraction() * 24f;
        if (_job is SpiritJob.Stopped or SpiritJob.Rest) return;
        var activeIndex = -1;
        for (var index = 0; index < _progression.Data.Automation.Schedule.Count; index++)
        {
            var entry = _progression.Data.Automation.Schedule[index];
            var active = entry.StartHour <= entry.EndHour
                ? hour >= entry.StartHour && hour < entry.EndHour
                : hour >= entry.StartHour || hour < entry.EndHour;
            if (active) { activeIndex = index; break; }
        }
        if (activeIndex == _activeScheduleIndex) return;
        _activeScheduleIndex = activeIndex;
        if (activeIndex < 0) return;
        var selected = _progression.Data.Automation.Schedule[activeIndex];
        if (!IsJobUnlocked(selected.Job, out _) || selected.Job is SpiritJob.Stopped or SpiritJob.Rest) return;
        if (!string.IsNullOrWhiteSpace(selected.ZoneName))
        {
            var zone = FindNamedZone(selected.ZoneName);
            if (zone == null) { _knowledge.RecordDecision($"Schedule zone not found: {selected.ZoneName}"); return; }
            _config.WorkRadius.Value = zone.Radius;
            _markers.SetWorkZone(SpiritKnowledge.ToVector(zone.Position), zone.Radius, _config.ShowWorkZone.Value);
        }
        _orderDeliveryPoint = null;
        ActivateOrder(CaptureOrder(selected.Job));
        _knowledge.RecordDecision($"Schedule → {selected.Job}");
    }

    private bool IsRuleTriggered(SpiritRuleTrigger trigger, Player player, bool enemyNearby)
    {
        switch (trigger)
        {
            case SpiritRuleTrigger.CargoEightyPercent:
                return _carry.Count + _carry.PendingCount >= Mathf.CeilToInt(EffectiveCarryStacks * CargoThreshold) ||
                       _carry.CurrentWeight >= EffectiveCarryWeight * CargoThreshold;
            case SpiritRuleTrigger.PlayerInventoryNinetyPercent:
                var inventory = player.GetInventory();
                var capacity = inventory.GetWidth() * inventory.GetHeight();
                return capacity > 0 && inventory.GetAllItems().Count >= Mathf.CeilToInt(capacity * InventoryThreshold);
            case SpiritRuleTrigger.EnemyNearby:
                return enemyNearby;
            case SpiritRuleTrigger.EnergyBelowTwenty:
                return _config.EnergyEnabled.Value && EnergyRatio < 0.2f;
            default:
                return false;
        }
    }

    private bool ApplyRule(SpiritRuleAction action)
    {
        switch (action)
        {
            case SpiritRuleAction.DeliverAndResume:
                if (_carry.Count == 0) return false;
                _forceDelivery = true;
                _knowledge.RecordDecision("Rule: cargo threshold → deliver and resume");
                return true;
            case SpiritRuleAction.CleanupNearby:
                if (_job == SpiritJob.Cleanup) return false;
                _jobBeforeRest = _job;
                _job = SpiritJob.Cleanup;
                _target = null;
                _knowledge.RecordDecision("Rule: inventory threshold → collect nearby cargo");
                return true;
            case SpiritRuleAction.ReturnToPlayer:
                if (_job == SpiritJob.Follow) return false;
                _jobBeforeRest = _job;
                _resumeAfterThreat = true;
                _job = SpiritJob.Follow;
                _target = null;
                _knowledge.RecordDecision("Rule: enemy nearby → return to player");
                return true;
            case SpiritRuleAction.RechargeAndResume:
                _jobBeforeRest = _job;
                _resumeAfterRest = true;
                _job = SpiritJob.Rest;
                _target = null;
                _knowledge.RecordDecision("Rule: energy below 20% → recharge, then resume");
                return true;
            default:
                return false;
        }
    }

    private bool TryParseWorkOrder(string text, out ResourceDefinition definition, out int quantity, out float radius,
        out NamedZoneData? deliveryZone)
    {
        definition = null!;
        quantity = 0;
        radius = WorkRadius;
        deliveryZone = null;
        var sections = text.Substring(7).Split(';');
        var command = sections[0].Trim();
        definition = _resources.All.OrderByDescending(item => item.Name.Length).FirstOrDefault(item =>
            command.StartsWith(item.Name, StringComparison.OrdinalIgnoreCase) &&
            (command.Length == item.Name.Length || char.IsWhiteSpace(command[item.Name.Length])));
        if (definition == null) return false;
        var quantityText = command.Substring(definition.Name.Length).Trim().TrimStart('×', 'x', 'X').Trim();
        if (!string.IsNullOrEmpty(quantityText) && !int.TryParse(quantityText, out quantity)) return false;
        quantity = Mathf.Max(0, quantity);
        for (var index = 1; index < sections.Length; index++)
        {
            var option = sections[index].Split(new[] { ':' }, 2);
            if (option.Length != 2) continue;
            var key = option[0].Trim();
            var value = option[1].Trim();
            if (key.Equals("Radius", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(value, out var requestedRadius)) return false;
                radius = Mathf.Clamp(requestedRadius, 1f, _config.MaxWorkRadius.Value);
            }
            else if (key.Equals("Deliver", StringComparison.OrdinalIgnoreCase))
            {
                deliveryZone = FindNamedZone(value);
                if (deliveryZone == null || !PrivateArea.CheckAccess(SpiritKnowledge.ToVector(deliveryZone.Position), 0f, false, false)) return false;
            }
        }
        return true;
    }

    private bool CanAutomate(ResourceDefinition definition)
    {
        return _progression.Level >= definition.RequiredSpiritLevel &&
               _progression.ProfessionLevel(definition.Profession) >= definition.RequiredProfessionLevel &&
               definition.CanAutoHarvest;
    }

    private NamedZoneData? FindNamedZone(string name)
    {
        return _progression.Data.Automation.NamedZones.FirstOrDefault(zone =>
            string.Equals(zone.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(zone.Type.ToString(), name, StringComparison.OrdinalIgnoreCase));
    }

    private void RememberNearbyAnchors(Player player, SpiritAutomationData automation)
    {
        if (Time.time < _nextLongTermWorldScan) return;
        _nextLongTermWorldScan = Time.time + LongTermScanInterval;
        var nearestShip = default(Ship);
        var nearestBed = default(Bed);
        var nearestShipDistance = float.MaxValue;
        var nearestBedDistance = float.MaxValue;
        foreach (var hit in Physics.OverlapSphere(player.transform.position, 35f, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            if (!hit) continue;
            var ship = hit.GetComponentInParent<Ship>();
            if (ship)
            {
                var distance = Vector3.SqrMagnitude(ship.transform.position - player.transform.position);
                if (distance < nearestShipDistance) { nearestShip = ship; nearestShipDistance = distance; }
            }
            var bed = hit.GetComponentInParent<Bed>();
            if (bed)
            {
                var distance = Vector3.SqrMagnitude(bed.transform.position - player.transform.position);
                if (distance < nearestBedDistance) { nearestBed = bed; nearestBedDistance = distance; }
            }
        }
        if (nearestShip) { automation.LastBoatPosition = ToArray(nearestShip.transform.position); automation.HasLastBoatPosition = true; }
        if (nearestBed) { automation.LastBedPosition = ToArray(nearestBed.transform.position); automation.HasLastBedPosition = true; }
    }

    private void TickBaseAlarm(Player player, Vector3 centre)
    {
        var enemies = CountEnemies(centre, 25f);
        if (enemies == 0 || Time.time < _nextGuardNotice) return;
        _nextGuardNotice = Time.time + 8f;
        var direction = centre - player.transform.position;
        _ui.Notify($"⚠ Опасность у дома: {enemies} враг(а), направление {DirectionName(direction)}.", 6f);
        _audio?.Play(SpiritAudioEvent.DangerDetected);
        _visual?.ShowEmotion(SpiritEmotion.Alert, 6f);
        _baseAlarmReturnUntil = Time.time + 4f;
        foreach (var enemy in Character.GetAllCharacters())
            if (enemy && !enemy.IsDead() && BaseAI.IsEnemy(player, enemy) && Vector3.Distance(enemy.transform.position, centre) < 25f)
            { _markers.ShowTemporaryMarker(enemy.transform.position, Color.red, 8f); break; }
    }

    private void TickThreatWarning(Player player, float radius)
    {
        var enemies = CountEnemies(player.transform.position, radius);
        if (enemies == 0 || Time.time < _nextGuardNotice) return;
        _nextGuardNotice = Time.time + 6f;
        _ui.Notify($"✦ Впереди опасность: {enemies} враг(а).", 4f);
        _audio?.Play(SpiritAudioEvent.DangerDetected);
    }

    private int CountEnemies(Vector3 centre, float radius)
    {
        if (Time.time < _nextEnemyScan && Vector3.SqrMagnitude(centre - _enemyScanCentre) < 1f &&
            Mathf.Approximately(radius, _enemyScanRadius)) return _enemyScanCount;
        var squaredRadius = radius * radius;
        var player = Player.m_localPlayer;
        var count = 0;
        foreach (var character in Character.GetAllCharacters())
            if (character && player && character != player && !character.IsDead() && BaseAI.IsEnemy(player, character) &&
                Vector3.SqrMagnitude(character.transform.position - centre) <= squaredRadius) count++;
        _nextEnemyScan = Time.time + 0.75f;
        _enemyScanCentre = centre;
        _enemyScanRadius = radius;
        _enemyScanCount = count;
        return _enemyScanCount;
    }

    private static bool IsNearFire(Vector3 centre)
    {
        var hits = Physics.OverlapSphere(centre, 8f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            if (!hit) continue;
            var name = hit.gameObject.name.ToLowerInvariant();
            if (name.Contains("fire") || name.Contains("hearth") || name.Contains("bonfire")) return true;
        }
        return false;
    }

    private void DetectNearbySecret(Vector3 centre, float radius)
    {
        var hits = Physics.OverlapSphere(centre, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            if (!hit) continue;
            var name = hit.gameObject.name.ToLowerInvariant();
            if (!name.Contains("chest") && !name.Contains("vegvisir") && !name.Contains("boss") &&
                !name.Contains("location") && !name.Contains("rare")) continue;
            var approximate = Vector3.Lerp(centre, hit.transform.position, 0.65f);
            _markers.ShowTemporaryMarker(approximate, new Color(1f, 0.65f, 0.9f), 7f);
            _ui.Notify("✦ Что-то заинтересовало духа...", 4f);
            return;
        }
    }

    private static string DirectionName(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return "рядом";
        var angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        var directions = new[] { "север", "северо-восток", "восток", "юго-восток", "юг", "юго-запад", "запад", "северо-запад" };
        return directions[Mathf.RoundToInt(angle / 45f) % directions.Length];
    }

    private static int CurrentDay() => EnvMan.instance ? EnvMan.instance.GetDay() : 0;

}
