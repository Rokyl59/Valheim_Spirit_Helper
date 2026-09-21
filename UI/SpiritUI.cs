using System;
using System.Collections.Generic;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using UnityEngine;

namespace SpiritHelper.UI;

public enum SpiritMenuAction
{
    Follow, Woodcutting, Mining, Gathering, Transport, Scout, Guard, Rest, Stop,
    OpenWork, OpenUnload, OpenFilter, OpenExactFilter, OpenAbilities, OpenSettings,
    OpenAppearance, OpenMovement, OpenLighting, OpenSafety,
    PlaceUnload, RemoveUnload, CycleUnloadType, PlaceWorkZone, RemoveWorkZone, CycleZoneMode,
    FilterAll, FilterWood, FilterStone, FilterOre, FilterPlants, FilterFood,
    FilterExactWood, FilterExactStone, FilterExactCopper, FilterExactTin, FilterExactSilver,
    FilterExactObsidian, FilterExactBlackMarble, FilterExactBerries, FilterExactPlants,
    Recall, ScanResources, ToggleTorch, CycleBalance, ToggleAutoCarry, ToggleHud, CycleWorkRadius,
    CycleOrbSize, CycleOrbColor, CycleFollowDistance, CycleFollowHeight, CycleFollowSpeed, CycleInertia,
    CycleBobbing, CycleLightMode, CycleLightIntensity, CycleLightRange, CycleLightDistance, CycleLightColor,
    CycleSafeRadius, ToggleProtectTrees, ToggleProtectCrops,
    Back
}

public sealed class SpiritUI
{
    private enum MenuPage { Main, Work, Unload, Filter, ExactFilter, Abilities, Settings, Appearance, Movement, Lighting, Safety }

    private static readonly (string Label, SpiritMenuAction Action)[] MainEntries =
    {
        ("Следовать", SpiritMenuAction.Follow), ("Работа и добыча", SpiritMenuAction.OpenWork),
        ("Разгрузка и зона", SpiritMenuAction.OpenUnload), ("Фильтр ресурсов", SpiritMenuAction.OpenFilter),
        ("Способности", SpiritMenuAction.OpenAbilities), ("Разведка", SpiritMenuAction.Scout),
        ("Охранять", SpiritMenuAction.Guard), ("Отдыхать", SpiritMenuAction.Rest),
        ("Настройки", SpiritMenuAction.OpenSettings)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] WorkEntries =
    {
        ("Рубить дерево", SpiritMenuAction.Woodcutting), ("Добывать руду", SpiritMenuAction.Mining),
        ("Собирать растения", SpiritMenuAction.Gathering), ("Переносить груз", SpiritMenuAction.Transport),
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

    private static readonly (string Label, SpiritMenuAction Action)[] ExactFilterEntries =
    {
        ("Древесина", SpiritMenuAction.FilterExactWood), ("Камень", SpiritMenuAction.FilterExactStone),
        ("Медь", SpiritMenuAction.FilterExactCopper), ("Олово", SpiritMenuAction.FilterExactTin),
        ("Серебро", SpiritMenuAction.FilterExactSilver), ("Обсидиан", SpiritMenuAction.FilterExactObsidian),
        ("Чёрный мрамор", SpiritMenuAction.FilterExactBlackMarble), ("Ягоды", SpiritMenuAction.FilterExactBerries),
        ("Прочие растения", SpiritMenuAction.FilterExactPlants)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] AbilityEntries =
    {
        ("Отозвать духа", SpiritMenuAction.Recall), ("Сканировать ресурсы", SpiritMenuAction.ScanResources),
        ("Переключить свет", SpiritMenuAction.ToggleTorch), ("Назад", SpiritMenuAction.Back)
    };

    private static readonly (string Label, SpiritMenuAction Action)[] SettingsEntries =
    {
        ("Сменить баланс", SpiritMenuAction.CycleBalance), ("Автоперенос", SpiritMenuAction.ToggleAutoCarry),
        ("Показать/скрыть HUD", SpiritMenuAction.ToggleHud), ("Изменить радиус", SpiritMenuAction.CycleWorkRadius),
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
        ("Защищать урожай", SpiritMenuAction.ToggleProtectCrops), ("Назад", SpiritMenuAction.Back)
    };

    private readonly Queue<(string Text, float Until)> _notifications = new Queue<(string, float)>();
    private GUIStyle? _panel;
    private GUIStyle? _title;
    private MenuPage _page;
    public bool MenuOpen { get; private set; }
    public Action<SpiritMenuAction>? ActionSelected;

    public void ToggleMenu()
    {
        MenuOpen = !MenuOpen;
        if (MenuOpen) _page = MenuPage.Main;
    }

    public void HandleInput()
    {
        if (!MenuOpen) return;
        var entries = CurrentEntries;
        for (var index = 0; index < entries.Length; index++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)) &&
                !Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + index))) continue;
            Select(entries[index].Action);
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
            var content = $"Дух-помощник\nУровень: {progression.Level}  Опыт: {progression.Xp:0}/{progression.NextXp:0}\nЗадача: {JobName(job)}\nСостояние: {StateName(state)}\nФильтр: {filter}\nЭнергия: {energy:0}/100\nЦель: {target}\nГруз: {carried}";
            GUI.Box(new Rect(18f, 170f, 270f, 175f), content, _panel);
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
        SpiritJob.Guard => "Охрана", SpiritJob.Rest => "Отдых", _ => "Остановлен"
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
        MenuPage.ExactFilter => ExactFilterEntries, MenuPage.Abilities => AbilityEntries,
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
        if (action == SpiritMenuAction.OpenSettings) { _page = MenuPage.Settings; return; }
        if (action == SpiritMenuAction.OpenAppearance) { _page = MenuPage.Appearance; return; }
        if (action == SpiritMenuAction.OpenMovement) { _page = MenuPage.Movement; return; }
        if (action == SpiritMenuAction.OpenLighting) { _page = MenuPage.Lighting; return; }
        if (action == SpiritMenuAction.OpenSafety) { _page = MenuPage.Safety; return; }
        if (action == SpiritMenuAction.Back) { GoBack(); return; }
        ActionSelected?.Invoke(action);
        MenuOpen = false;
    }

    private void DrawRadial()
    {
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty);
        var heading = _page switch
        {
            MenuPage.Work => "РАБОТА И ДОБЫЧА", MenuPage.Unload => "РАЗГРУЗКА И РАБОЧАЯ ЗОНА",
            MenuPage.Filter => "ФИЛЬТР РЕСУРСОВ", MenuPage.ExactFilter => "ВЫБЕРИТЕ РЕСУРС",
            MenuPage.Abilities => "СПОСОБНОСТИ", MenuPage.Settings => "НАСТРОЙКИ",
            MenuPage.Appearance => "ВНЕШНИЙ ВИД", MenuPage.Movement => "ДВИЖЕНИЕ",
            MenuPage.Lighting => "ОСВЕЩЕНИЕ", MenuPage.Safety => "БЕЗОПАСНОСТЬ",
            _ => "ДУХ-ПОМОЩНИК"
        };
        GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 18f, 360f, 36f), heading, _title);
        var entries = CurrentEntries;
        var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        for (var index = 0; index < entries.Length; index++)
        {
            var angle = -Mathf.PI * 0.5f + index * Mathf.PI * 2f / entries.Length;
            var position = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 190f;
            if (!GUI.Button(new Rect(position.x - 82f, position.y - 24f, 164f, 48f), $"{index + 1}. {entries[index].Label}")) continue;
            Select(entries[index].Action);
        }
        if (_page == MenuPage.Main && GUI.Button(new Rect(centre.x - 62f, centre.y + 38f, 124f, 34f), "0. Остановить"))
            Select(SpiritMenuAction.Stop);
        else if (_page != MenuPage.Main && GUI.Button(new Rect(centre.x - 52f, centre.y + 38f, 104f, 34f), "0. Назад"))
            GoBack();
    }

    private void GoBack()
    {
        if (_page == MenuPage.ExactFilter) _page = MenuPage.Filter;
        else if (_page is MenuPage.Appearance or MenuPage.Movement or MenuPage.Lighting or MenuPage.Safety) _page = MenuPage.Settings;
        else if (_page != MenuPage.Main) _page = MenuPage.Main;
        else MenuOpen = false;
    }

    private void EnsureStyles()
    {
        if (_panel != null) return;
        _panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(12, 12, 9, 9), wordWrap = true };
        _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
    }
}
