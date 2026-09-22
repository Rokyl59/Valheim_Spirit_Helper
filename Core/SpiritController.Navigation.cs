using SpiritHelper.Visuals;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const float UnreachableTargetCooldown = 60f;
    private bool _followCloseToPlayer;
    private bool _restNearPlayer;

    private void RecoverBlockedNavigation()
    {
        if (_visual == null || !_visual.NavigationBlocked) return;
        _visual.ResetNavigation();
        if (_state == SpiritState.Work) return;

        if (_job == SpiritJob.Follow)
        {
            if (!_followCloseToPlayer) _ui.Notify("Путь перекрыт. Пробую подлететь ближе к тебе.");
            _followCloseToPlayer = true;
            return;
        }
        if (_job == SpiritJob.Rest)
        {
            if (!_restNearPlayer) _ui.Notify("Не могу добраться до места отдыха. Отдохну рядом с тобой.");
            _restNearPlayer = true;
            return;
        }

        if (_target != null && _carry.Count == 0)
        {
            if (_target.Component) _targetBlacklist[_target.Component.GetInstanceID()] = Time.time + UnreachableTargetCooldown;
            _carry.CancelPending();
            CancelTarget("Не нашёл проход к ресурсу. Ищу другую цель.");
            return;
        }

        var hadCargo = _carry.Count > 0;
        _carry.Release();
        if (_job == SpiritJob.Courier) _progression.Data.Automation.CourierRoute.Enabled = false;
        _buildAssistEnabled = false;
        SetJob(SpiritJob.Follow);
        _followCloseToPlayer = true;
        _lastError = hadCargo
            ? "Путь перекрыт. Груз оставлен на месте, задание остановлено."
            : "Не нашёл проход. Задание остановлено, возвращаюсь к тебе.";
        _ui.Notify(_lastError);
        _visual.ShowEmotion(SpiritEmotion.Frustrated);
    }
}
