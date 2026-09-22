using System.Linq;
using SpiritHelper.Resources;
using SpiritHelper.UI;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private void SelectMenuItem(string itemId)
    {
        if (_resourceSelection.Mode != ResourceFilterMode.Exact || _resourceSelection.ExactName != itemId)
            PrepareMenuFilterChange();
        SetExactFilter(itemId, ResourceName(itemId));
    }

    private void PrepareMenuFilterChange()
    {
        if (_job is SpiritJob.Follow or SpiritJob.Rest or SpiritJob.Stopped) return;
        SetJob(SpiritJob.Stopped);
        _ui.Notify("Предыдущее поручение остановлено для смены фильтра. Выбери новое действие во вкладке «Работа».", 6f);
    }

    private void StartHarvestCommand(SpiritJob job)
    {
        var compatible = _resources.All.Any(definition => definition.CanAutoHarvest &&
            _resourceSelection.Allows(definition) && JobFor(definition) == job);
        if (!compatible && _resourceSelection.Mode != ResourceFilterMode.All)
        {
            var previousFilter = FilterName();
            _resourceSelection.AllowAll();
            _ui.Notify($"Фильтр «{previousFilter}» не подходит для команды «{SpiritUI.JobName(job)}». Ищу подходящие ресурсы.", 6f);
        }
        _lastSuggestion = float.NegativeInfinity;
        _nextScan = 0f;
        SetJob(job);
    }
}
