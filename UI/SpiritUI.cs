using System;
using System.Collections.Generic;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.UI;

public enum SpiritMenuAction
{
    Follow, Woodcutting, Mining, Gathering, Transport, Scout, Guard, Rest, Stop,
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
    Back
}

public sealed class SpiritUI
{
    private enum MenuPage
    {
        Main, Work, Unload, Filter, ExactFilter, Abilities, Talents, Settings, Appearance, Movement,
        Lighting, Safety, Advanced, Companion, Logistics, Navigation, Automation
    }

    private static readonly (string Label, SpiritMenuAction Action)[] MainEntries =
    {
        ("Следовать", SpiritMenuAction.Follow), ("Работа и добыча", SpiritMenuAction.OpenWork),
        ("Разгрузка и зона", SpiritMenuAction.OpenUnload), ("Фильтр ресурсов", SpiritMenuAction.OpenFilter),
        ("Способности", SpiritMenuAction.OpenAbilities), ("Таланты", SpiritMenuAction.OpenTalents), ("Разведка", SpiritMenuAction.Scout),
        ("Охранять", SpiritMenuAction.Guard), ("Отдыхать", SpiritMenuAction.Rest),
        ("Настройки", SpiritMenuAction.OpenSettings), ("Расширенные режимы", SpiritMenuAction.OpenAdvanced),
        ("Остановить", SpiritMenuAction.Stop)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] WorkEntries =
    {
        ("Рубить дерево", SpiritMenuAction.Woodcutting), ("Добывать руду", SpiritMenuAction.Mining),
        ("Собирать растения", SpiritMenuAction.Gathering), ("Переносить груз", SpiritMenuAction.Transport),
        ("Количество", SpiritMenuAction.CycleQuantity), ("Режим очереди", SpiritMenuAction.ToggleQueueMode),
        ("Очистить очередь", SpiritMenuAction.ClearQueue),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] UnloadEntries =
    {
        ("Поставить разгрузку", SpiritMenuAction.PlaceUnload), ("Удалить разгрузку", SpiritMenuAction.RemoveUnload),
        ("Сменить тип", SpiritMenuAction.CycleUnloadType), ("Поставить рабочую зону", SpiritMenuAction.PlaceWorkZone),
        ("Удалить рабочую зону", SpiritMenuAction.RemoveWorkZone), ("Режим рабочей зоны", SpiritMenuAction.CycleZoneMode),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] FilterEntries =
    {
        ("Все ресурсы", SpiritMenuAction.FilterAll), ("Только древесина", SpiritMenuAction.FilterWood),
        ("Только камень", SpiritMenuAction.FilterStone), ("Только руда", SpiritMenuAction.FilterOre),
        ("Только растения", SpiritMenuAction.FilterPlants), ("Только еда", SpiritMenuAction.FilterFood),
        ("Конкретный ресурс", SpiritMenuAction.OpenExactFilter), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AbilityEntries =
    {
        ("Отозвать духа", SpiritMenuAction.Recall), ("Сканировать ресурсы", SpiritMenuAction.ScanResources),
        ("Переключить свет", SpiritMenuAction.ToggleTorch), ("Контекстная команда", SpiritMenuAction.ContextCommand),
        ("Исследовать объект", SpiritMenuAction.InspectTarget), ("Почему ждёшь?", SpiritMenuAction.ExplainIdle),
        ("Статистика", SpiritMenuAction.ShowStatistics), ("Журнал", SpiritMenuAction.ShowJournal),
        ("Последние решения", SpiritMenuAction.ShowDecisions),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] TalentEntries =
    {
        ("Лесоруб", SpiritMenuAction.TalentWoodcutting), ("Шахтёр", SpiritMenuAction.TalentMining),
        ("Носильщик", SpiritMenuAction.TalentLogistics), ("Следопыт", SpiritMenuAction.TalentExploration),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AdvancedEntries =
    {
        ("Компаньон", SpiritMenuAction.OpenCompanion), ("Логистика", SpiritMenuAction.OpenLogistics),
        ("Навигация", SpiritMenuAction.OpenNavigation), ("Автоматизация", SpiritMenuAction.OpenAutomation),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] CompanionEntries =
    {
        ("Наблюдать и учиться", SpiritMenuAction.Observe), ("Помогай мне", SpiritMenuAction.Assist),
        ("Умная уборка", SpiritMenuAction.Cleanup), ("Найти потерянное", SpiritMenuAction.FindLost),
        ("Исследовать объект", SpiritMenuAction.InspectTarget), ("Научиться объекту", SpiritMenuAction.LearnTarget),
        ("Детектор секретов", SpiritMenuAction.DetectSecret), ("Дежурство", SpiritMenuAction.Patrol),
        ("Разведочная экспедиция", SpiritMenuAction.Expedition), ("Отпустить духа", SpiritMenuAction.ReleaseSpirit),
        ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] LogisticsEntries =
    {
        ("Почтальон A → B", SpiritMenuAction.Courier), ("Караван", SpiritMenuAction.Caravan),
        ("Принеси выбранное", SpiritMenuAction.Bring), ("Разобрать вещи", SpiritMenuAction.Sort),
        ("Строительный помощник", SpiritMenuAction.BuildAssist), ("Производственные цепочки", SpiritMenuAction.Production),
        ("Поставить алтарь", SpiritMenuAction.PlaceShrine), ("Улучшить алтарь", SpiritMenuAction.UpgradeShrine),
        ("Зарядиться у алтаря", SpiritMenuAction.ChargeShrine),
        ("Пожертвовать ресурс", SpiritMenuAction.FeedShrine), ("Назад", SpiritMenuAction.Back)
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
        ("Сохранить именованную зону", SpiritMenuAction.SaveNamedZone), ("Следующая зона", SpiritMenuAction.CycleNamedZone),
        ("Тип новой зоны", SpiritMenuAction.CycleNamedZoneType), ("Предложения духа", SpiritMenuAction.ToggleSuggestions),
        ("Повторять поручения", SpiritMenuAction.ToggleRepeatOrders), ("Поддерживать запас", SpiritMenuAction.CycleMaintainStock),
        ("Расписание", SpiritMenuAction.ToggleSchedule), ("Правила духа", SpiritMenuAction.ToggleRules),
        ("Сигнализация базы", SpiritMenuAction.BaseAlarm), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] SettingsEntries =
    {
        ("Сменить баланс", SpiritMenuAction.CycleBalance), ("Автоперенос", SpiritMenuAction.ToggleAutoCarry),
        ("Показать/скрыть HUD", SpiritMenuAction.ToggleHud), ("Изменить радиус", SpiritMenuAction.CycleWorkRadius),
        ("Граница рабочей зоны", SpiritMenuAction.ToggleWorkZoneVisibility),
        ("Внешний вид", SpiritMenuAction.OpenAppearance), ("Движение", SpiritMenuAction.OpenMovement),
        ("Освещение", SpiritMenuAction.OpenLighting), ("Безопасность", SpiritMenuAction.OpenSafety),
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
    private IReadOnlyList<ResourceDefinition> _resources = Array.Empty<ResourceDefinition>();
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    public bool MenuOpen { get; private set; }
    public Action<SpiritMenuAction>? ActionSelected;
    public Action<string>? ExactResourceSelected;

    public void SetResources(IReadOnlyList<ResourceDefinition> resources) => _resources = resources;

    public void ToggleMenu()
    {
        MenuOpen = !MenuOpen;
        if (MenuOpen)
        {
            _page = MenuPage.Main;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else RestoreCursor();
    }

    public void Close()
    {
        if (!MenuOpen) return;
        MenuOpen = false;
        RestoreCursor();
    }

    public void HandleInput()
    {
        if (!MenuOpen) return;
        var entries = CurrentEntries;
        for (var index = 0; index < entries.Length; index++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)) &&
                !Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + index))) continue;
            SelectEntry(index, entries[index].Action);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            if (_page == MenuPage.Main) Select(SpiritMenuAction.Stop);
            else GoBack();
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
        if (showHud)
        {
            var width = Mathf.Clamp(Screen.width * 0.18f, 245f, 330f);
            var name = string.IsNullOrWhiteSpace(progression.Data.SpiritName) ? "Дух" : progression.Data.SpiritName;
            var title = progression.Data.Bond >= 500f ? "Верный спутник" : progression.Data.Bond >= 100f ? "Друг" : progression.Data.Personality;
            var content = $"✦ {name}, {title}  •  ур. {progression.Level}\n{JobName(job)} — {StateName(state)}\n{target}  •  {carried}";
            GUI.Box(new Rect(18f, Screen.height * 0.18f, width, 86f), content, _panel);
            DrawBar(new Rect(30f, Screen.height * 0.18f + 62f, width - 24f, 10f), energy / 100f,
                energy < 20f ? new Color(1f, 0.35f, 0.25f) : new Color(0.2f, 0.8f, 1f));
        }
        while (_notifications.Count > 0 && _notifications.Peek().Until < Time.unscaledTime) _notifications.Dequeue();
        var y = 55f;
        foreach (var notification in _notifications)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 190f, y, 380f, 30f), notification.Text, _panel);
            y += 34f;
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

    private (string Label, SpiritMenuAction Action)[] CurrentEntries => _page switch
    {
        MenuPage.Work => WorkEntries, MenuPage.Unload => UnloadEntries, MenuPage.Filter => FilterEntries,
        MenuPage.ExactFilter => DynamicExactEntries(), MenuPage.Abilities => AbilityEntries, MenuPage.Talents => TalentEntries,
        MenuPage.Advanced => AdvancedEntries, MenuPage.Companion => CompanionEntries,
        MenuPage.Logistics => LogisticsEntries, MenuPage.Navigation => NavigationEntries,
        MenuPage.Automation => AutomationEntries,
        MenuPage.Settings => SettingsEntries, MenuPage.Appearance => AppearanceEntries,
        MenuPage.Movement => MovementEntries, MenuPage.Lighting => LightingEntries,
        MenuPage.Safety => SafetyEntries, _ => MainEntries
    };

    private void Select(SpiritMenuAction action)
    {
        if (action == SpiritMenuAction.OpenWork) { _page = MenuPage.Work; return; }
        if (action == SpiritMenuAction.OpenUnload) { _page = MenuPage.Unload; return; }
        if (action == SpiritMenuAction.OpenFilter) { _page = MenuPage.Filter; return; }
        if (action == SpiritMenuAction.OpenExactFilter) { _page = MenuPage.ExactFilter; return; }
        if (action == SpiritMenuAction.OpenAbilities) { _page = MenuPage.Abilities; return; }
        if (action == SpiritMenuAction.OpenTalents) { _page = MenuPage.Talents; return; }
        if (action == SpiritMenuAction.OpenAdvanced) { _page = MenuPage.Advanced; return; }
        if (action == SpiritMenuAction.OpenCompanion) { _page = MenuPage.Companion; return; }
        if (action == SpiritMenuAction.OpenLogistics) { _page = MenuPage.Logistics; return; }
        if (action == SpiritMenuAction.OpenNavigation) { _page = MenuPage.Navigation; return; }
        if (action == SpiritMenuAction.OpenAutomation) { _page = MenuPage.Automation; return; }
        if (action == SpiritMenuAction.OpenSettings) { _page = MenuPage.Settings; return; }
        if (action == SpiritMenuAction.OpenAppearance) { _page = MenuPage.Appearance; return; }
        if (action == SpiritMenuAction.OpenMovement) { _page = MenuPage.Movement; return; }
        if (action == SpiritMenuAction.OpenLighting) { _page = MenuPage.Lighting; return; }
        if (action == SpiritMenuAction.OpenSafety) { _page = MenuPage.Safety; return; }
        if (action == SpiritMenuAction.Back) { GoBack(); return; }
        ActionSelected?.Invoke(action);
        if (ClosesMenu(action))
        {
            MenuOpen = false;
            RestoreCursor();
        }
    }

    private void DrawRadial()
    {
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty);
        var heading = _page switch
        {
            MenuPage.Work => "РАБОТА И ДОБЫЧА", MenuPage.Unload => "РАЗГРУЗКА И РАБОЧАЯ ЗОНА",
            MenuPage.Filter => "ФИЛЬТР РЕСУРСОВ", MenuPage.ExactFilter => "ВЫБЕРИТЕ РЕСУРС",
            MenuPage.Abilities => "СПОСОБНОСТИ", MenuPage.Talents => "ТАЛАНТЫ", MenuPage.Settings => "НАСТРОЙКИ",
            MenuPage.Advanced => "РАСШИРЕННЫЕ РЕЖИМЫ", MenuPage.Companion => "КОМПАНЬОН",
            MenuPage.Logistics => "ЛОГИСТИКА", MenuPage.Navigation => "НАВИГАЦИЯ",
            MenuPage.Automation => "АВТОМАТИЗАЦИЯ",
            MenuPage.Appearance => "ВНЕШНИЙ ВИД", MenuPage.Movement => "ДВИЖЕНИЕ",
            MenuPage.Lighting => "ОСВЕЩЕНИЕ", MenuPage.Safety => "БЕЗОПАСНОСТЬ",
            _ => "ДУХ-ПОМОЩНИК"
        };
        GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 18f, 360f, 36f), heading, _title);
        var entries = CurrentEntries;
        var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (entries.Length > 9)
        {
            DrawGrid(entries, centre);
            return;
        }
        for (var index = 0; index < entries.Length; index++)
        {
            var angle = -Mathf.PI * 0.5f + index * Mathf.PI * 2f / entries.Length;
            var position = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 190f;
            if (!GUI.Button(new Rect(position.x - 92f, position.y - 25f, 184f, 50f), $"{index + 1}. {entries[index].Label}", _button)) continue;
            SelectEntry(index, entries[index].Action);
        }
        if (_page == MenuPage.Main && GUI.Button(new Rect(centre.x - 62f, centre.y + 38f, 124f, 34f), "0. Остановить"))
            Select(SpiritMenuAction.Stop);
        else if (_page != MenuPage.Main && GUI.Button(new Rect(centre.x - 52f, centre.y + 38f, 104f, 34f), "0. Назад"))
            GoBack();
    }

    private void DrawGrid((string Label, SpiritMenuAction Action)[] entries, Vector2 centre)
    {
        const int columns = 3;
        const float width = 210f;
        const float height = 46f;
        var rows = Mathf.CeilToInt(entries.Length / (float)columns);
        var startX = centre.x - columns * width * 0.5f;
        var startY = centre.y - rows * (height + 8f) * 0.5f + 35f;
        for (var index = 0; index < entries.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var rect = new Rect(startX + column * width, startY + row * (height + 8f), width - 8f, height);
            if (GUI.Button(rect, $"{index + 1}. {entries[index].Label}", _button)) SelectEntry(index, entries[index].Action);
        }
    }

    private void GoBack()
    {
        if (_page == MenuPage.ExactFilter) _page = MenuPage.Filter;
        else if (_page is MenuPage.Appearance or MenuPage.Movement or MenuPage.Lighting or MenuPage.Safety) _page = MenuPage.Settings;
        else if (_page is MenuPage.Companion or MenuPage.Logistics or MenuPage.Navigation or MenuPage.Automation) _page = MenuPage.Advanced;
        else if (_page != MenuPage.Main) _page = MenuPage.Main;
        else
        {
            MenuOpen = false;
            RestoreCursor();
        }
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
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string, SpiritMenuAction)>();
        foreach (var resource in _resources)
            if (unique.Add(resource.Name)) entries.Add((resource.Name, SpiritMenuAction.FilterExactWood));
        return entries.ToArray();
    }

    private void SelectEntry(int index, SpiritMenuAction action)
    {
        if (_page == MenuPage.ExactFilter && index >= 0 && index < CurrentEntries.Length)
        {
            ExactResourceSelected?.Invoke(CurrentEntries[index].Label);
            MenuOpen = false;
            RestoreCursor();
            return;
        }
        Select(action);
    }

    private static bool ClosesMenu(SpiritMenuAction action) => action is
        SpiritMenuAction.Follow or SpiritMenuAction.Woodcutting or SpiritMenuAction.Mining or
        SpiritMenuAction.Gathering or SpiritMenuAction.Transport or SpiritMenuAction.Scout or
        SpiritMenuAction.Guard or SpiritMenuAction.Rest or SpiritMenuAction.Stop or
        SpiritMenuAction.Recall or SpiritMenuAction.ScanResources or SpiritMenuAction.PlaceUnload or
        SpiritMenuAction.PlaceWorkZone or SpiritMenuAction.Observe or SpiritMenuAction.Assist or
        SpiritMenuAction.Cleanup or SpiritMenuAction.FindLost or SpiritMenuAction.Courier or
        SpiritMenuAction.Caravan or SpiritMenuAction.Bring or SpiritMenuAction.BuildAssist or SpiritMenuAction.Sort or
        SpiritMenuAction.Patrol or SpiritMenuAction.GuideHome or SpiritMenuAction.GuideUnload or
        SpiritMenuAction.GuideWorkZone or SpiritMenuAction.GuideGrave or SpiritMenuAction.GuideBoat or
        SpiritMenuAction.GuideBed or SpiritMenuAction.GuideResource or SpiritMenuAction.ExploreDungeon or
        SpiritMenuAction.Expedition or SpiritMenuAction.Production;

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
