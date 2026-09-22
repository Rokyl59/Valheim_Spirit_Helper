using System;
using System.Collections.Generic;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.UI;

public enum SpiritMenuAction
{
    Follow, Woodcutting, Mining, Gathering, Transport, Scout, Guard, Rest, Stop, WorkAtPing,
    OpenWork, OpenUnload, OpenFilter, OpenExactFilter, OpenAbilities, OpenSettings,
    OpenAppearance, OpenMovement, OpenLighting, OpenSafety, OpenTalents,
    OpenAdvanced, OpenCompanion, OpenLogistics, OpenNavigation, OpenAutomation,
    PlaceUnload, RemoveUnload, CycleUnloadType, PlaceWorkZone, RemoveWorkZone, CycleZoneMode,
    FilterAll, FilterWood, FilterStone, FilterOre, FilterPlants, FilterFood,
    FilterExactWood, FilterExactStone, FilterExactCopper, FilterExactTin, FilterExactSilver,
    FilterExactObsidian, FilterExactBlackMarble, FilterExactBerries, FilterExactPlants,
    Recall, ScanResources, ToggleTorch, CycleBalance, ToggleAutoCarry, ToggleHud, CycleWorkRadius,
    CycleOrbSize, CycleOrbColor, CycleFollowDistance, CycleFollowHeight, CycleFollowSpeed, CycleInertia,
    CycleBobbing, CycleLightMode, CycleLightIntensity, CycleLightRange, CycleLightDistance, CycleLightColor,
    CycleSafeRadius, ToggleProtectTrees, ToggleProtectCrops, ToggleWorkZoneVisibility,
    TalentWoodcutting, TalentMining, TalentLogistics, TalentExploration,
    CycleQuantity, ToggleQueueMode, ClearQueue,
    IgnoreLookTarget, PlaceForbiddenZone, RemoveForbiddenZone,
    ShowStatistics, ShowJournal, ShowDecisions, ExplainIdle, InspectTarget, ContextCommand,
    Observe, Assist, Cleanup, FindLost, Courier, Caravan, Bring, BuildAssist, Sort,
    Patrol, GuideHome, GuideUnload, GuideWorkZone, GuideGrave, GuideBoat, GuideBed, GuideResource, ExploreDungeon, Expedition,
    PlaceShrine, UpgradeShrine, ChargeShrine, FeedShrine, SaveNamedZone, CycleNamedZone, CycleNamedZoneType,
    ToggleSuggestions, ToggleRepeatOrders, CycleMaintainStock, ToggleSchedule, ToggleRules,
    BaseAlarm, DetectSecret, LearnTarget, Production, ReleaseSpirit, ShowMemory, ShowBiomeKnowledge,
    ToggleDetailedHud, Back,
    OpenShrine, RemoveShrine, OpenGeneralSettings, OpenZones
}

public sealed class SpiritUI
{
    private const float MenuHeight = 520f;
    private const float EntrySpacing = 43f;
    private const float FooterHeight = 42f;
    private enum MenuPage
    {
        Main, Work, Unload, Filter, ExactFilter, Abilities, Talents, Settings, Appearance, Movement,
        Lighting, Safety, Advanced, Companion, Logistics, Navigation, Automation, Report, Shrine, GeneralSettings, Zones
    }

    private static readonly (string Label, SpiritMenuAction Action)[] MainEntries =
    {
        ("Следуй за мной", SpiritMenuAction.Follow), ("Работай с тем, на что я смотрю", SpiritMenuAction.ContextCommand),
        ("Помогай мне добывать", SpiritMenuAction.Assist), ("Отдыхай", SpiritMenuAction.Rest)
    };

    private static readonly (string Label, MenuPage Page)[] Tabs =
    {
        ("Команды", MenuPage.Main), ("Работа", MenuPage.Work),
        ("Спутник", MenuPage.Companion), ("Настройки", MenuPage.Settings)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] WorkEntries =
    {
        ("Рубить дерево", SpiritMenuAction.Woodcutting), ("Добывать руду", SpiritMenuAction.Mining),
        ("Собирать растения", SpiritMenuAction.Gathering), ("Предмет / фильтр", SpiritMenuAction.OpenExactFilter),
        ("Зона и разгрузка", SpiritMenuAction.OpenUnload), ("Доставка и автоматизация", SpiritMenuAction.OpenLogistics)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] UnloadEntries =
    {
        ("Поставить разгрузку", SpiritMenuAction.PlaceUnload), ("Удалить разгрузку", SpiritMenuAction.RemoveUnload),
        ("Сменить тип", SpiritMenuAction.CycleUnloadType), ("Поставить рабочую зону", SpiritMenuAction.PlaceWorkZone),
        ("Удалить рабочую зону", SpiritMenuAction.RemoveWorkZone), ("Режим рабочей зоны", SpiritMenuAction.CycleZoneMode),
        ("Работать у метки", SpiritMenuAction.WorkAtPing), ("Именованные зоны", SpiritMenuAction.OpenZones),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] FilterEntries =
    {
        ("Конкретный ресурс", SpiritMenuAction.OpenExactFilter),
        ("Все ресурсы", SpiritMenuAction.FilterAll), ("Только древесина", SpiritMenuAction.FilterWood),
        ("Только камень", SpiritMenuAction.FilterStone), ("Только руда", SpiritMenuAction.FilterOre),
        ("Только растения", SpiritMenuAction.FilterPlants), ("Только еда", SpiritMenuAction.FilterFood),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AbilityEntries =
    {
        ("Почему ждёшь?", SpiritMenuAction.ExplainIdle), ("Исследовать объект", SpiritMenuAction.InspectTarget),
        ("Статистика", SpiritMenuAction.ShowStatistics), ("Журнал", SpiritMenuAction.ShowJournal),
        ("Последние решения", SpiritMenuAction.ShowDecisions),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] TalentEntries =
    {
        ("Лесоруб — быстрее рубка", SpiritMenuAction.TalentWoodcutting), ("Шахтёр — быстрее добыча", SpiritMenuAction.TalentMining),
        ("Носильщик — больше груза", SpiritMenuAction.TalentLogistics), ("Следопыт — лучше поиск", SpiritMenuAction.TalentExploration),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AdvancedEntries =
    {
        ("Наблюдать и учиться", SpiritMenuAction.Observe), ("Научиться объекту", SpiritMenuAction.LearnTarget),
        ("Умная уборка", SpiritMenuAction.Cleanup), ("Найти потерянное", SpiritMenuAction.FindLost),
        ("Разведка", SpiritMenuAction.Scout), ("Охранять", SpiritMenuAction.Guard),
        ("Дежурство", SpiritMenuAction.Patrol), ("Разведочная экспедиция", SpiritMenuAction.Expedition),
        ("Детектор секретов", SpiritMenuAction.DetectSecret), ("Сканировать ресурсы", SpiritMenuAction.ScanResources),
        ("Отозвать застрявшего духа", SpiritMenuAction.Recall),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] CompanionEntries =
    {
        ("Таланты", SpiritMenuAction.OpenTalents), ("Алтарь", SpiritMenuAction.OpenShrine),
        ("Журнал и состояние", SpiritMenuAction.OpenAbilities), ("Проводник", SpiritMenuAction.OpenNavigation),
        ("Другие занятия", SpiritMenuAction.OpenAdvanced)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] LogisticsEntries =
    {
        ("Автоматизация и очередь", SpiritMenuAction.OpenAutomation),
        ("Переносить груз", SpiritMenuAction.Transport), ("Принеси выбранное", SpiritMenuAction.Bring),
        ("Почтальон A → B", SpiritMenuAction.Courier), ("Караван", SpiritMenuAction.Caravan),
        ("Разобрать вещи", SpiritMenuAction.Sort), ("Строительный помощник", SpiritMenuAction.BuildAssist)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] ShrineEntries =
    {
        ("Поставить алтарь", SpiritMenuAction.PlaceShrine), ("Улучшить алтарь", SpiritMenuAction.UpgradeShrine),
        ("Зарядиться у алтаря", SpiritMenuAction.ChargeShrine), ("Пожертвовать ресурс", SpiritMenuAction.FeedShrine),
        ("Убрать алтарь (улучшения сохранятся)", SpiritMenuAction.RemoveShrine)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] NavigationEntries =
    {
        ("Веди домой", SpiritMenuAction.GuideHome), ("Веди к разгрузке", SpiritMenuAction.GuideUnload),
        ("Веди к рабочей зоне", SpiritMenuAction.GuideWorkZone), ("Веди к могиле", SpiritMenuAction.GuideGrave),
        ("Веди к кораблю", SpiritMenuAction.GuideBoat), ("Веди к кровати", SpiritMenuAction.GuideBed),
        ("Веди к ресурсу", SpiritMenuAction.GuideResource),
        ("Исследуй пещеру", SpiritMenuAction.ExploreDungeon), ("Память ресурсов", SpiritMenuAction.ShowMemory),
        ("Знание биомов", SpiritMenuAction.ShowBiomeKnowledge), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AutomationEntries =
    {
        ("Режим очереди", SpiritMenuAction.ToggleQueueMode), ("Очистить очередь", SpiritMenuAction.ClearQueue),
        ("Повторять поручения", SpiritMenuAction.ToggleRepeatOrders), ("Поддерживать запас", SpiritMenuAction.CycleMaintainStock),
        ("Расписание", SpiritMenuAction.ToggleSchedule), ("Правила духа", SpiritMenuAction.ToggleRules),
        ("Производственные цепочки", SpiritMenuAction.Production), ("Сигнализация базы", SpiritMenuAction.BaseAlarm)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] ZoneEntries =
    {
        ("Сохранить именованную зону", SpiritMenuAction.SaveNamedZone), ("Следующая зона", SpiritMenuAction.CycleNamedZone),
        ("Тип новой зоны", SpiritMenuAction.CycleNamedZoneType)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] SettingsEntries =
    {
        ("Общие", SpiritMenuAction.OpenGeneralSettings), ("Внешний вид", SpiritMenuAction.OpenAppearance),
        ("Движение", SpiritMenuAction.OpenMovement), ("Освещение", SpiritMenuAction.OpenLighting),
        ("Безопасность", SpiritMenuAction.OpenSafety)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] GeneralSettingsEntries =
    {
        ("Сменить баланс", SpiritMenuAction.CycleBalance), ("Автоперенос", SpiritMenuAction.ToggleAutoCarry),
        ("Показать/скрыть HUD", SpiritMenuAction.ToggleHud), ("Подробный HUD", SpiritMenuAction.ToggleDetailedHud),
        ("Изменить радиус", SpiritMenuAction.CycleWorkRadius),
        ("Граница рабочей зоны", SpiritMenuAction.ToggleWorkZoneVisibility),
        ("Предложения духа", SpiritMenuAction.ToggleSuggestions), ("Отпустить духа и сбросить развитие", SpiritMenuAction.ReleaseSpirit),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AppearanceEntries =
    {
        ("Размер шара", SpiritMenuAction.CycleOrbSize), ("Цвет шара", SpiritMenuAction.CycleOrbColor),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] MovementEntries =
    {
        ("Расстояние", SpiritMenuAction.CycleFollowDistance), ("Высота", SpiritMenuAction.CycleFollowHeight),
        ("Скорость", SpiritMenuAction.CycleFollowSpeed), ("Плавность и инертность", SpiritMenuAction.CycleInertia),
        ("Покачивание", SpiritMenuAction.CycleBobbing), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] LightingEntries =
    {
        ("Режим света", SpiritMenuAction.CycleLightMode), ("Яркость", SpiritMenuAction.CycleLightIntensity),
        ("Дальность", SpiritMenuAction.CycleLightRange), ("Расстояние перед камерой", SpiritMenuAction.CycleLightDistance),
        ("Цвет света", SpiritMenuAction.CycleLightColor), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] SafetyEntries =
    {
        ("Защита построек", SpiritMenuAction.CycleSafeRadius),
        ("Защищать посадки деревьев", SpiritMenuAction.ToggleProtectTrees),
        ("Защищать урожай", SpiritMenuAction.ToggleProtectCrops),
        ("Игнорировать объект", SpiritMenuAction.IgnoreLookTarget),
        ("Поставить запретную зону", SpiritMenuAction.PlaceForbiddenZone),
        ("Удалить запретную зону", SpiritMenuAction.RemoveForbiddenZone), ("Назад", SpiritMenuAction.Back)
    };

    private readonly Queue<(string Text, float Until)> _notifications = new Queue<(string, float)>();
    private GUIStyle? _panel;
    private GUIStyle? _title;
    private GUIStyle? _button;
    private MenuPage _page;
    private MenuPage _tab;
    private readonly Stack<(MenuPage Page, int Offset)> _history = new Stack<(MenuPage, int)>();
    private int _pageOffset;
    private bool _detailedHud;
    private bool _releaseConfirmationPending;
    private bool _clearFocusPending;
    private string _taskStatus = string.Empty;
    private string _quantityInput = "0";
    private string _zoneName = string.Empty;
    private string _resourceSearch = string.Empty;
    private readonly List<ResourceDefinition> _filteredResources = new List<ResourceDefinition>();
    private (string Label, SpiritMenuAction Action)[] _resourceEntries = Array.Empty<(string, SpiritMenuAction)>();
    private string? _cachedResourceSearch;
    private int _cachedResourceCount = -1;
    private SpiritJob _completionJob = SpiritJob.Follow;
    private string _talentSummary = string.Empty;
    private string _reportTitle = string.Empty;
    private string _reportBody = string.Empty;
    private Vector2 _reportScroll;
    private IReadOnlyList<string> _places = Array.Empty<string>();
    private int _workPlaceIndex = -1;
    private int _deliveryPlaceIndex = -1;
    private int _spiritLevel;
    private IReadOnlyList<ResourceDefinition> _resources = Array.Empty<ResourceDefinition>();
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    public bool MenuOpen { get; private set; }
    public Action<SpiritMenuAction>? ActionSelected;
    public Action<string>? ExactResourceSelected;
    public Action<int>? OrderQuantityChanged;
    public Action<SpiritJob>? OrderCompletionChanged;
    public Func<SpiritMenuAction, string>? ActionValue;
    public Action<string>? ZoneNameChanged;
    public Action<int>? WorkPlaceSelected;
    public Action<int>? DeliveryPlaceSelected;
    public Func<SpiritMenuAction, int>? RequiredLevel;

    public void SetResources(IReadOnlyList<ResourceDefinition> resources)
    {
        _resources = resources;
        _cachedResourceSearch = null;
    }
    public void SetTaskStatus(string taskStatus) => _taskStatus = taskStatus ?? string.Empty;
    public void SetPlaces(IReadOnlyList<string> places)
    {
        _places = places ?? Array.Empty<string>();
        _workPlaceIndex = ClampPlaceIndex(_workPlaceIndex);
        _deliveryPlaceIndex = ClampPlaceIndex(_deliveryPlaceIndex);
    }
    public void ShowReport(string title, string body)
    {
        _reportTitle = title;
        _reportBody = body;
        _reportScroll = Vector2.zero;
        if (!MenuOpen)
        {
            SwitchTab(MenuPage.Main);
            MenuOpen = true;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        OpenPage(MenuPage.Report);
    }

    public void ToggleMenu()
    {
        MenuOpen = !MenuOpen;
        if (MenuOpen)
        {
            SwitchTab(MenuPage.Main);
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            _releaseConfirmationPending = false;
            RestoreCursor();
        }
    }

    public void Close()
    {
        if (!MenuOpen) return;
        MenuOpen = false;
        _releaseConfirmationPending = false;
        RestoreCursor();
    }

    public void HandleInput()
    {
        if (!MenuOpen) return;
        if (GUI.GetNameOfFocusedControl() is "SpiritOrderQuantity" or "SpiritResourceSearch" or "SpiritZoneName")
        {
            if (Input.GetKeyDown(KeyCode.Escape)) _clearFocusPending = true;
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var index = Array.FindIndex(Tabs, tab => tab.Page == _tab);
            SwitchTab(Tabs[(index + 1) % Tabs.Length].Page);
            return;
        }
        if (_page == MenuPage.Report)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Escape)) GoBack();
            return;
        }
        var entries = CurrentEntries;
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            _pageOffset = Mathf.Max(0, _pageOffset - VisibleEntryCount);
            return;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (_pageOffset + VisibleEntryCount < entries.Length) _pageOffset += VisibleEntryCount;
            return;
        }
        var visibleCount = Mathf.Min(VisibleEntryCount, entries.Length - _pageOffset);
        for (var index = 0; index < visibleCount; index++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)) &&
                !Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + index))) continue;
            var entry = entries[_pageOffset + index];
            var requiredLevel = RequiredLevel?.Invoke(entry.Action) ?? 0;
            if (requiredLevel > _spiritLevel)
            {
                Notify($"Нужен уровень {requiredLevel}.");
                return;
            }
            SelectEntry(_pageOffset + index, entry.Action);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            GoBack();
        }
        else if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
    }

    public void Notify(string text, float duration = 4f)
    {
        while (_notifications.Count >= 5) _notifications.Dequeue();
        _notifications.Enqueue((text, Time.unscaledTime + duration));
    }

    public void Draw(bool showHud, SpiritProgression progression, SpiritState state, SpiritJob job,
        float energy, string target, string carried, string filter, bool debug, string debugText)
    {
        EnsureStyles();
        if (_clearFocusPending)
        {
            GUI.FocusControl(string.Empty);
            _clearFocusPending = false;
        }
        _spiritLevel = progression.Level;
        _talentSummary = TalentSummary(progression);
        if (showHud)
        {
            var width = Mathf.Clamp(Screen.width * 0.18f, 245f, 330f);
            var name = string.IsNullOrWhiteSpace(progression.Data.SpiritName) ? "Дух" : progression.Data.SpiritName;
            var title = progression.Data.Bond >= 500f ? "Верный спутник" : progression.Data.Bond >= 100f ? "Друг" : progression.Data.Personality;
            var panelHeight = _detailedHud ? 164f : 86f;
            var content = $"✦ {name}, {title}  •  ур. {progression.Level}\n{JobName(job)} — {StateName(state)}\n{target}  •  {carried}";
            GUI.Box(new Rect(18f, Screen.height * 0.18f, width, panelHeight), content, _panel);
            DrawBar(new Rect(30f, Screen.height * 0.18f + 62f, width - 24f, 10f), energy / 100f,
                energy < 20f ? new Color(1f, 0.35f, 0.25f) : new Color(0.2f, 0.8f, 1f));
            if (_detailedHud)
            {
                var profession = JobProfession(job);
                var level = progression.ProfessionLevel(profession);
                var xp = progression.Data.ProfessionXp[profession];
                var requiredXp = 100f + (level - 1) * 75f;
                GUI.Label(new Rect(30f, Screen.height * 0.18f + 80f, width - 24f, 20f), $"Фильтр: {filter}", _panel);
                GUI.Label(new Rect(30f, Screen.height * 0.18f + 102f, width - 24f, 20f), $"{profession}: ур. {level}  {xp:0}/{requiredXp:0}", _panel);
                DrawBar(new Rect(30f, Screen.height * 0.18f + 125f, width - 24f, 8f), xp / requiredXp, new Color(0.56f, 0.72f, 1f));
                if (!string.IsNullOrWhiteSpace(_taskStatus)) GUI.Label(new Rect(30f, Screen.height * 0.18f + 138f, width - 24f, 20f), ShortText(_taskStatus, 64), _panel);
            }
        }
        while (_notifications.Count > 0 && _notifications.Peek().Until < Time.unscaledTime) _notifications.Dequeue();
        var y = 55f;
        foreach (var notification in _notifications)
        {
            const float width = 380f;
            var height = Mathf.Clamp(_panel!.CalcHeight(new GUIContent(notification.Text), width), 30f, 72f);
            GUI.Box(new Rect(Screen.width * 0.5f - width * 0.5f, y, width, height), notification.Text, _panel);
            y += height + 4f;
        }
        if (debug) GUI.Box(new Rect(18f, 355f, 390f, 125f), debugText, _panel);
        if (MenuOpen) DrawRadial();
    }

    public static string JobName(SpiritJob job) => job switch
    {
        SpiritJob.Follow => "Следовать", SpiritJob.Woodcutting => "Рубка дерева",
        SpiritJob.Mining => "Добыча руды", SpiritJob.Gathering => "Сбор растений",
        SpiritJob.Transport => "Перенос", SpiritJob.Scout => "Разведка",
        SpiritJob.Guard => "Дозор", SpiritJob.Rest => "Отдых", SpiritJob.Observe => "Наблюдение",
        SpiritJob.Assist => "Помощь игроку", SpiritJob.Cleanup => "Умная уборка",
        SpiritJob.Courier => "Почтальон", SpiritJob.Caravan => "Караван", SpiritJob.Bring => "Принести",
        SpiritJob.Sort => "Сортировка", SpiritJob.Patrol => "Дежурство", SpiritJob.Guide => "Проводник",
        SpiritJob.ExploreDungeon => "Исследование подземелья", SpiritJob.Expedition => "Экспедиция",
        SpiritJob.Production => "Производственная логистика", _ => "Остановлен"
    };

    private static string StateName(SpiritState state) => state switch
    {
        SpiritState.Idle => "Ожидание", SpiritState.FollowPlayer => "Следует за игроком",
        SpiritState.SearchTarget => "Ищет цель", SpiritState.MoveToTarget => "Летит к цели",
        SpiritState.Work => "Работает", SpiritState.WaitForDrop => "Ждёт добычу",
        SpiritState.CollectDrops => "Собирает", SpiritState.CarryDrops => "Несёт груз",
        SpiritState.Unload => "Разгружает", SpiritState.ReturnToPlayer => "Возвращается",
        SpiritState.Rest => "Отдыхает", SpiritState.Scout => "Разведывает",
        SpiritState.Guard => "Охраняет", SpiritState.Recharge => "Восстанавливается",
        SpiritState.Celebrate => "Радуется", _ => state.ToString()
    };

    private (string Label, SpiritMenuAction Action)[] CurrentEntries
    {
        get
        {
            var entries = _page switch
            {
                MenuPage.Work => WorkEntries, MenuPage.Unload => UnloadEntries, MenuPage.Filter => FilterEntries,
                MenuPage.ExactFilter => DynamicExactEntries(), MenuPage.Abilities => AbilityEntries, MenuPage.Talents => TalentEntries,
                MenuPage.Advanced => AdvancedEntries, MenuPage.Companion => CompanionEntries, MenuPage.Logistics => LogisticsEntries,
                MenuPage.Navigation => NavigationEntries, MenuPage.Automation => AutomationEntries, MenuPage.Settings => SettingsEntries,
                MenuPage.Appearance => AppearanceEntries, MenuPage.Movement => MovementEntries, MenuPage.Lighting => LightingEntries,
                MenuPage.Safety => SafetyEntries, MenuPage.Shrine => ShrineEntries, MenuPage.Zones => ZoneEntries,
                MenuPage.GeneralSettings => GeneralSettingsEntries, _ => MainEntries
            };
            if (entries.Length == 0 || entries[entries.Length - 1].Action != SpiritMenuAction.Back) return entries;
            var result = new (string Label, SpiritMenuAction Action)[entries.Length - 1];
            Array.Copy(entries, result, result.Length);
            return result;
        }
    }

    private void Select(SpiritMenuAction action)
    {
        if (action != SpiritMenuAction.ReleaseSpirit) _releaseConfirmationPending = false;
        if (action == SpiritMenuAction.ToggleDetailedHud)
        {
            _detailedHud = !_detailedHud;
            Notify(_detailedHud ? "Подробный HUD включён." : "Подробный HUD выключен.");
            return;
        }
        if (action == SpiritMenuAction.ReleaseSpirit)
        {
            if (!_releaseConfirmationPending)
            {
                _releaseConfirmationPending = true;
                Notify("Повторно выберите «Отпустить духа», чтобы подтвердить сброс уровня.", 6f);
                return;
            }
            _releaseConfirmationPending = false;
        }
        var destination = action switch
        {
            SpiritMenuAction.OpenWork => MenuPage.Work, SpiritMenuAction.OpenUnload => MenuPage.Unload,
            SpiritMenuAction.OpenFilter => MenuPage.Filter, SpiritMenuAction.OpenExactFilter => MenuPage.ExactFilter,
            SpiritMenuAction.OpenAbilities => MenuPage.Abilities, SpiritMenuAction.OpenTalents => MenuPage.Talents,
            SpiritMenuAction.OpenAdvanced => MenuPage.Advanced, SpiritMenuAction.OpenCompanion => MenuPage.Companion,
            SpiritMenuAction.OpenLogistics => MenuPage.Logistics, SpiritMenuAction.OpenNavigation => MenuPage.Navigation,
            SpiritMenuAction.OpenAutomation => MenuPage.Automation, SpiritMenuAction.OpenSettings => MenuPage.Settings,
            SpiritMenuAction.OpenAppearance => MenuPage.Appearance, SpiritMenuAction.OpenMovement => MenuPage.Movement,
            SpiritMenuAction.OpenLighting => MenuPage.Lighting, SpiritMenuAction.OpenSafety => MenuPage.Safety,
            SpiritMenuAction.OpenShrine => MenuPage.Shrine, SpiritMenuAction.OpenGeneralSettings => MenuPage.GeneralSettings,
            SpiritMenuAction.OpenZones => MenuPage.Zones, _ => (MenuPage?)null
        };
        if (destination.HasValue)
        {
            if (destination == MenuPage.ExactFilter) _resourceSearch = string.Empty;
            OpenPage(destination.Value);
            return;
        }
        if (action == SpiritMenuAction.Back) { GoBack(); return; }
        if (_page == MenuPage.Work && action is (SpiritMenuAction.Woodcutting or SpiritMenuAction.Mining or SpiritMenuAction.Gathering))
        {
            if (!int.TryParse(_quantityInput, out var quantity) || quantity < 0)
            {
                Notify("Введите количество от 0 до 9999. 0 — без ограничения.");
                return;
            }
            OrderQuantityChanged?.Invoke(quantity);
        }
        ActionSelected?.Invoke(action);
        if (action is SpiritMenuAction.FilterAll or SpiritMenuAction.FilterWood or SpiritMenuAction.FilterStone or
            SpiritMenuAction.FilterOre or SpiritMenuAction.FilterPlants or SpiritMenuAction.FilterFood)
            SwitchTab(MenuPage.Work);
        if (ClosesMenu(action))
        {
            Close();
        }
    }

    private void DrawRadial()
    {
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty);
        if (_page == MenuPage.Report)
        {
            DrawReport();
            return;
        }
        var entries = CurrentEntries;
        var rect = MenuRect;
        GUI.Box(rect, string.Empty, _panel);
        if (DrawTabs(rect)) return;
        var heading = _page switch
        {
            MenuPage.Main => "ДУХ-ПОМОЩНИК", MenuPage.Work => "ПОРУЧЕНИЯ",
            MenuPage.Unload => "СКЛАДЫ И РАБОЧАЯ ЗОНА", MenuPage.Filter => "ФИЛЬТР РЕСУРСОВ",
            MenuPage.ExactFilter => "ВЫБОР РЕСУРСА", MenuPage.Abilities => "СПОСОБНОСТИ И ЖУРНАЛ",
            MenuPage.Talents => "ТАЛАНТЫ", MenuPage.Settings => "НАСТРОЙКИ",
            MenuPage.Appearance => "ВНЕШНИЙ ВИД", MenuPage.Movement => "ДВИЖЕНИЕ",
            MenuPage.Lighting => "ОСВЕЩЕНИЕ", MenuPage.Safety => "БЕЗОПАСНОСТЬ",
            MenuPage.Advanced => "ДРУГИЕ ЗАНЯТИЯ", MenuPage.Companion => "СПУТНИК",
            MenuPage.Logistics => "ЛОГИСТИКА", MenuPage.Navigation => "НАВИГАЦИЯ",
            MenuPage.Automation => "АВТОМАТИЗАЦИЯ И ОЧЕРЕДЬ", MenuPage.Shrine => "АЛТАРЬ",
            MenuPage.Zones => "ИМЕНОВАННЫЕ ЗОНЫ", MenuPage.GeneralSettings => "ОБЩИЕ НАСТРОЙКИ", _ => "ДУХ-ПОМОЩНИК"
        };
        GUI.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 30f), heading, _title);
        var entriesY = rect.y + EntryTop;
        if (_page == MenuPage.Talents)
        {
            GUI.Label(new Rect(rect.x + 16f, entriesY - 56f, rect.width - 32f, 54f), _talentSummary, _panel);
        }
        if (_page == MenuPage.ExactFilter)
        {
            GUI.Label(new Rect(rect.x + 16f, entriesY - 30f, 62f, 24f), "Поиск:", _panel);
            GUI.SetNextControlName("SpiritResourceSearch");
            var search = GUI.TextField(new Rect(rect.x + 78f, entriesY - 30f, rect.width - 94f, 24f), _resourceSearch, 48);
            if (!string.Equals(search, _resourceSearch, StringComparison.Ordinal))
            {
                _resourceSearch = search;
                _pageOffset = 0;
                return;
            }
            if (_filteredResources.Count == 0)
                GUI.Label(new Rect(rect.x + 16f, entriesY + 2f * EntrySpacing, rect.width - 32f, 52f),
                    _resources.Count == 0 ? "Каталог предметов ещё не загружен. Открой меню после входа в мир." : "Предметы не найдены. Попробуй другое название.", _panel);
        }
        var count = Mathf.Min(VisibleEntryCount, entries.Length - _pageOffset);
        for (var index = 0; index < count; index++)
        {
            var entry = entries[_pageOffset + index];
            var requiredLevel = RequiredLevel?.Invoke(entry.Action) ?? 0;
            var locked = requiredLevel > _spiritLevel;
            var oldEnabled = GUI.enabled;
            GUI.enabled = !locked;
            var label = locked ? $"{entry.Label} · ур. {requiredLevel}" : EntryLabel(entry);
            if (GUI.Button(new Rect(rect.x + 16f, entriesY + index * EntrySpacing, rect.width - 32f, 37f),
                    $"{index + 1}. {label}", _button))
            {
                GUI.enabled = oldEnabled;
                SelectEntry(_pageOffset + index, entry.Action);
                return;
            }
            GUI.enabled = oldEnabled;
        }
        var footerY = rect.yMax - FooterHeight;
        if (_pageOffset > 0 && GUI.Button(new Rect(rect.center.x - 76f, footerY, 38f, 30f), "←", _button))
            _pageOffset = Mathf.Max(0, _pageOffset - VisibleEntryCount);
        if (entries.Length > VisibleEntryCount)
            GUI.Label(new Rect(rect.center.x - 35f, footerY, 66f, 30f), $"{_pageOffset / VisibleEntryCount + 1}/{(entries.Length + VisibleEntryCount - 1) / VisibleEntryCount}", _title);
        if (_pageOffset + VisibleEntryCount < entries.Length && GUI.Button(new Rect(rect.center.x + 38f, footerY, 38f, 30f), "→", _button))
            _pageOffset += VisibleEntryCount;
        if (_page == MenuPage.Work)
        {
            GUI.SetNextControlName("SpiritOrderQuantity");
            _quantityInput = GUI.TextField(new Rect(rect.x + 16f, footerY - 33f, 72f, 27f), _quantityInput, 4);
            if (GUI.Button(new Rect(rect.x + 94f, footerY - 33f, 112f, 27f), "Задать (0 = ∞)", _button) &&
                int.TryParse(_quantityInput, out var quantity) && quantity >= 0) OrderQuantityChanged?.Invoke(quantity);
            if (GUI.Button(new Rect(rect.x + 212f, footerY - 33f, rect.width - 228f, 27f),
                    $"После: {CompletionName(_completionJob)}", _button))
            {
                _completionJob = _completionJob switch
                {
                    SpiritJob.Follow => SpiritJob.Rest,
                    SpiritJob.Rest => SpiritJob.Guide,
                    _ => SpiritJob.Follow
                };
                OrderCompletionChanged?.Invoke(_completionJob);
            }
            DrawPlaceSelectors(rect, footerY - 64f);
        }
        if (_page == MenuPage.Zones) DrawZoneNameInput(rect, footerY - 33f);
        if (GUI.Button(new Rect(rect.x + 16f, footerY, 90f, 30f), _history.Count > 0 ? "0. Назад" : "0. Закрыть", _button))
        { GoBack(); return; }
        if (GUI.Button(new Rect(rect.xMax - 120f, footerY, 104f, 30f), "Стоп", _button)) Select(SpiritMenuAction.Stop);
    }

    private void GoBack()
    {
        if (_history.Count > 0)
        {
            var previous = _history.Pop();
            _page = previous.Page;
            _pageOffset = previous.Offset;
            _releaseConfirmationPending = false;
            _clearFocusPending = true;
            return;
        }
        Close();
    }

    private void OpenPage(MenuPage page)
    {
        if (_page == page) return;
        _history.Push((_page, _pageOffset));
        _page = page;
        _pageOffset = 0;
        _releaseConfirmationPending = false;
        _clearFocusPending = true;
    }

    private void SwitchTab(MenuPage page)
    {
        _tab = page;
        _page = page;
        _history.Clear();
        _pageOffset = 0;
        _releaseConfirmationPending = false;
        _clearFocusPending = true;
    }

    private bool DrawTabs(Rect rect)
    {
        var width = (rect.width - 32f) / Tabs.Length;
        for (var index = 0; index < Tabs.Length; index++)
        {
            var tab = Tabs[index];
            var oldColor = GUI.backgroundColor;
            if (_tab == tab.Page) GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
            var clicked = GUI.Button(new Rect(rect.x + 16f + index * width, rect.y + 12f, width - 4f, 30f), tab.Label, _button);
            GUI.backgroundColor = oldColor;
            if (!clicked) continue;
            SwitchTab(tab.Page);
            return true;
        }
        return false;
    }

    private string EntryLabel((string Label, SpiritMenuAction Action) entry)
    {
        var value = ActionValue?.Invoke(entry.Action);
        return string.IsNullOrWhiteSpace(value) ? entry.Label : $"{entry.Label}: {value}";
    }

    private void DrawZoneNameInput(Rect menuRect, float y)
    {
        GUI.SetNextControlName("SpiritZoneName");
        var name = GUI.TextField(new Rect(menuRect.x + 16f, y, 132f, 27f), _zoneName, 24);
        if (!string.Equals(name, _zoneName, StringComparison.Ordinal))
        {
            _zoneName = name;
            ZoneNameChanged?.Invoke(_zoneName);
        }
        GUI.Label(new Rect(menuRect.x + 154f, y, menuRect.width - 170f, 27f), "Имя зоны для сохранения", _panel);
    }

    private void DrawPlaceSelectors(Rect menuRect, float y)
    {
        if (_places.Count == 0) return;
        if (GUI.Button(new Rect(menuRect.x + 16f, y, menuRect.width * 0.5f - 20f, 27f),
                $"Работа: {PlaceName(_workPlaceIndex)}", _button))
        {
            _workPlaceIndex = NextPlaceIndex(_workPlaceIndex);
            WorkPlaceSelected?.Invoke(_workPlaceIndex);
        }
        if (GUI.Button(new Rect(menuRect.center.x + 4f, y, menuRect.width * 0.5f - 20f, 27f),
                $"Доставка: {PlaceName(_deliveryPlaceIndex)}", _button))
        {
            _deliveryPlaceIndex = NextPlaceIndex(_deliveryPlaceIndex);
            DeliveryPlaceSelected?.Invoke(_deliveryPlaceIndex);
        }
    }

    private int ClampPlaceIndex(int index) => _places.Count == 0 ? -1 : Mathf.Clamp(index, -1, _places.Count - 1);
    private int NextPlaceIndex(int index) => _places.Count == 0 ? -1 : index >= _places.Count - 1 ? -1 : index + 1;
    private string PlaceName(int index) => index < 0 || index >= _places.Count ? "игрок" : _places[index];

    private static string CompletionName(SpiritJob job) => job switch
    {
        SpiritJob.Rest => "отдых", SpiritJob.Guide => "вернуться", _ => "следовать"
    };

    private static string TalentSummary(SpiritProgression progression)
    {
        var points = progression.AvailableTalentPoints;
        return $"Очки талантов: {points}\nЛес {TalentTier(progression, "woodcutting")}/4 · " +
               $"руда {TalentTier(progression, "mining")}/4 · груз {TalentTier(progression, "logistics")}/4 · " +
               $"поиск {TalentTier(progression, "exploration")}/4";
    }

    private static int TalentTier(SpiritProgression progression, string branch)
    {
        var tier = 0;
        while (tier < 4 && progression.HasTalent($"{branch}.{tier + 1}")) tier++;
        return tier;
    }

    private void EnsureStyles()
    {
        if (_panel != null) return;
        _panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(12, 12, 9, 9), wordWrap = true };
        _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
        _button = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold,
            padding = new RectOffset(12, 12, 8, 8), border = new RectOffset(12, 12, 12, 12)
        };
        _button.normal.textColor = new Color(0.78f, 0.95f, 1f);
        _button.hover.textColor = Color.white;
    }

    private (string Label, SpiritMenuAction Action)[] DynamicExactEntries()
    {
        if (_cachedResourceSearch == _resourceSearch && _cachedResourceCount == _resources.Count) return _resourceEntries;
        _cachedResourceSearch = _resourceSearch;
        _cachedResourceCount = _resources.Count;
        _filteredResources.Clear();
        var search = _resourceSearch.Trim();
        foreach (var resource in _resources)
            if (search.Length == 0 || resource.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                resource.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                _filteredResources.Add(resource);
        _filteredResources.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName));
        var entries = new List<(string, SpiritMenuAction)>
        {
            ("Любой подходящий предмет", SpiritMenuAction.FilterAll),
            ("Категории ресурсов", SpiritMenuAction.OpenFilter)
        };
        foreach (var resource in _filteredResources)
            entries.Add(($"{resource.DisplayName}{(resource.CanAutoHarvest ? string.Empty : " · переноска")}", SpiritMenuAction.FilterExactWood));
        _resourceEntries = entries.ToArray();
        return _resourceEntries;
    }

    private Rect MenuRect
    {
        get
        {
            var width = Mathf.Min(Screen.width - 24f, 500f);
            var contentHeight = _page == MenuPage.Report ? MenuHeight :
                EntryTop + Mathf.Clamp(CurrentEntries.Length, _page == MenuPage.ExactFilter ? 4 : 1, 6) * EntrySpacing + FooterHeight + EntryFooterSpace;
            var height = Mathf.Min(Screen.height - 40f, contentHeight);
            return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        }
    }

    private float EntryTop => 88f + (_page == MenuPage.Talents ? 56f : _page == MenuPage.ExactFilter ? 30f : 0f);

    private float EntryFooterSpace => _page == MenuPage.Work ? (_places.Count > 0 ? 70f : 37f) : _page == MenuPage.Zones ? 37f : 0f;

    private int VisibleEntryCount => Mathf.Clamp(Mathf.FloorToInt((MenuRect.height - EntryTop - FooterHeight -
        EntryFooterSpace) / EntrySpacing), 1, 6);

    private void DrawReport()
    {
        var rect = MenuRect;
        GUI.Box(rect, string.Empty, _panel);
        if (DrawTabs(rect)) return;
        GUI.Label(new Rect(rect.x + 16f, rect.y + 48f, rect.width - 32f, 28f), _reportTitle, _title);
        var viewport = new Rect(rect.x + 16f, rect.y + 88f, rect.width - 32f, rect.height - 136f);
        var contentHeight = Mathf.Max(viewport.height, _panel!.CalcHeight(new GUIContent(_reportBody), viewport.width - 18f));
        _reportScroll = GUI.BeginScrollView(viewport, _reportScroll, new Rect(0f, 0f, viewport.width - 18f, contentHeight));
        GUI.Label(new Rect(0f, 0f, viewport.width - 18f, contentHeight), _reportBody, _panel);
        GUI.EndScrollView();
        if (GUI.Button(new Rect(rect.x + 16f, rect.yMax - FooterHeight, 104f, 30f), "0. Назад", _button))
        { GoBack(); return; }
        if (GUI.Button(new Rect(rect.xMax - 120f, rect.yMax - FooterHeight, 104f, 30f), "Стоп", _button)) Select(SpiritMenuAction.Stop);
    }

    private static string ShortText(string text, int maxLength) => text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";

    private void SelectEntry(int index, SpiritMenuAction action)
    {
        if (_page == MenuPage.ExactFilter && index >= 0 && index < CurrentEntries.Length)
        {
            if (index < 2) { Select(action); return; }
            ExactResourceSelected?.Invoke(_filteredResources[index - 2].Name);
            SwitchTab(MenuPage.Work);
            return;
        }
        Select(action);
    }

    private static bool ClosesMenu(SpiritMenuAction action) => action is
        SpiritMenuAction.Follow or SpiritMenuAction.Woodcutting or SpiritMenuAction.Mining or
        SpiritMenuAction.Gathering or SpiritMenuAction.Transport or SpiritMenuAction.Scout or
        SpiritMenuAction.Guard or SpiritMenuAction.Rest or SpiritMenuAction.Stop or SpiritMenuAction.WorkAtPing or
        SpiritMenuAction.Recall or SpiritMenuAction.ScanResources or SpiritMenuAction.PlaceUnload or
        SpiritMenuAction.PlaceWorkZone or SpiritMenuAction.Observe or SpiritMenuAction.Assist or
        SpiritMenuAction.Cleanup or SpiritMenuAction.FindLost or SpiritMenuAction.Courier or
        SpiritMenuAction.Caravan or SpiritMenuAction.Bring or SpiritMenuAction.BuildAssist or SpiritMenuAction.Sort or
        SpiritMenuAction.Patrol or SpiritMenuAction.GuideHome or SpiritMenuAction.GuideUnload or
        SpiritMenuAction.GuideWorkZone or SpiritMenuAction.GuideGrave or SpiritMenuAction.GuideBoat or
        SpiritMenuAction.GuideBed or SpiritMenuAction.GuideResource or SpiritMenuAction.ExploreDungeon or
        SpiritMenuAction.Expedition or SpiritMenuAction.Production or SpiritMenuAction.ContextCommand;

    private static Profession JobProfession(SpiritJob job) => job switch
    {
        SpiritJob.Woodcutting => Profession.Woodcutting,
        SpiritJob.Mining => Profession.Mining,
        SpiritJob.Gathering => Profession.Gathering,
        SpiritJob.Transport or SpiritJob.Courier or SpiritJob.Caravan or SpiritJob.Bring or SpiritJob.Sort => Profession.Logistics,
        _ => Profession.Exploration
    };

    private void RestoreCursor()
    {
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
    }

    private static void DrawBar(Rect rect, float value, Color color)
    {
        GUI.Box(rect, string.Empty);
        var oldColor = GUI.color;
        GUI.color = color;
        GUI.Box(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * Mathf.Clamp01(value), rect.height - 4f), string.Empty);
        GUI.color = oldColor;
    }
}
