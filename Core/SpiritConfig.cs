using BepInEx.Configuration;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed class SpiritConfig
{
    public ConfigEntry<BalanceMode> BalanceMode { get; }
    public ConfigEntry<KeyboardShortcut> MenuKey { get; }
    public ConfigEntry<float> WorkRadius { get; }
    public ConfigEntry<bool> ShowWorkZone { get; }
    public ConfigEntry<float> MaxWorkRadius { get; }
    public ConfigEntry<float> MaxDistanceFromPlayer { get; }
    public ConfigEntry<float> SearchInterval { get; }
    public ConfigEntry<float> WorkInterval { get; }
    public ConfigEntry<float> SafeBaseRadius { get; }
    public ConfigEntry<bool> ProtectPlantedTrees { get; }
    public ConfigEntry<bool> ProtectCrops { get; }
    public ConfigEntry<bool> UseToolDurability { get; }
    public ConfigEntry<bool> EnergyEnabled { get; }
    public ConfigEntry<float> EnergyRegenRate { get; }
    public ConfigEntry<bool> EnableHud { get; }
    public ConfigEntry<bool> EnableParticles { get; }
    public ConfigEntry<bool> EnableSounds { get; }
    public ConfigEntry<bool> AutoCarry { get; }
    public ConfigEntry<int> MaxCarryStacks { get; }
    public ConfigEntry<float> MaxCarryWeight { get; }
    public ConfigEntry<float> MasterVolume { get; }
    public ConfigEntry<bool> EnableAmbientSounds { get; }
    public ConfigEntry<float> AmbientVolume { get; }
    public ConfigEntry<float> AmbientIntervalMin { get; }
    public ConfigEntry<float> AmbientIntervalMax { get; }
    public ConfigEntry<bool> DebugMode { get; }
    public ConfigEntry<float> OrbSize { get; }
    public ConfigEntry<string> OrbColor { get; }
    public ConfigEntry<float> FollowDistance { get; }
    public ConfigEntry<float> FollowHeight { get; }
    public ConfigEntry<float> FollowSpeed { get; }
    public ConfigEntry<float> MovementInertia { get; }
    public ConfigEntry<float> BobbingAmount { get; }
    public ConfigEntry<SpiritLightMode> LightMode { get; }
    public ConfigEntry<string> LightColor { get; }
    public ConfigEntry<float> LightIntensity { get; }
    public ConfigEntry<float> LightRange { get; }
    public ConfigEntry<float> CameraLightDistance { get; }
    public ConfigEntry<float> SpiritXpMultiplier { get; }
    public ConfigEntry<float> ProfessionXpMultiplier { get; }
    public ConfigEntry<float> LogisticsXp { get; }
    public ConfigEntry<int> MaxWorkAttempts { get; }
    public ConfigEntry<float> TargetTimeout { get; }

    public SpiritConfig(ConfigFile file)
    {
        BalanceMode = file.Bind("Баланс", "Режим", Core.BalanceMode.Vanilla, "Режим баланса: Vanilla, Balanced или Free.");
        MenuKey = file.Bind("Управление", "Открыть меню", new KeyboardShortcut(KeyCode.G), "Единственная горячая клавиша мода. Остальные действия находятся в меню.");
        WorkRadius = file.Bind("Рабочая зона", "Радиус работы", 50f, new ConfigDescription("Радиус рабочей зоны в метрах.", new AcceptableValueRange<float>(5f, 150f)));
        ShowWorkZone = file.Bind("Рабочая зона", "Показывать границу", true, "Показывать светящуюся границу рабочей зоны.");
        MaxWorkRadius = file.Bind("Рабочая зона", "Максимальный радиус", 150f, "Жёсткое ограничение радиуса рабочей зоны.");
        MaxDistanceFromPlayer = file.Bind("Рабочая зона", "Максимальная дальность от игрока", 80f, "Дух отменяет цель дальше этого расстояния.");
        SearchInterval = file.Bind("Поведение", "Интервал поиска", 1f, new ConfigDescription("Пауза между сканированиями в секундах.", new AcceptableValueRange<float>(0.25f, 10f)));
        WorkInterval = file.Bind("Поведение", "Интервал работы", 1.5f, new ConfigDescription("Пауза между ударами или действиями.", new AcceptableValueRange<float>(0.2f, 10f)));
        SafeBaseRadius = file.Bind("Безопасность", "Радиус защиты базы", 15f, "Не добывать ресурсы рядом с постройками игрока.");
        ProtectPlantedTrees = file.Bind("Безопасность", "Защищать посаженные деревья", true, "Не рубить деревья, связанные с объектами игрока.");
        ProtectCrops = file.Bind("Безопасность", "Защищать урожай", true, "Не собирать выращиваемые игроком растения.");
        UseToolDurability = file.Bind("Баланс", "Расходовать прочность инструмента", true, "В режиме Vanilla работа духа расходует прочность подходящего инструмента.");
        EnergyEnabled = file.Bind("Энергия", "Использовать энергию", true, "Включить расход и восстановление энергии духа.");
        EnergyRegenRate = file.Bind("Энергия", "Восстановление в секунду", 4f, "Скорость восстановления энергии во время отдыха.");
        EnableHud = file.Bind("Интерфейс", "Показывать панель духа", true, "Показывать состояние, энергию и текущую цель.");
        EnableParticles = file.Bind("Внешний вид", "Включить частицы", true, "Показывать локальные частицы духа.");
        EnableSounds = file.Bind("Звук", "Включить звуки духа", true, "Включить звуки действий и интерфейса.");
        EnableAmbientSounds = file.Bind("Звук", "Включить самостоятельные звуки", true, "Дух периодически издаёт тихие звуки без команды игрока.");
        MasterVolume = file.Bind("Звук", "Общая громкость", 1f, new ConfigDescription("Общая громкость духа.", new AcceptableValueRange<float>(0f, 1f)));
        AmbientVolume = file.Bind("Звук", "Громкость самостоятельных звуков", 0.25f, new ConfigDescription("Громкость редких фоновых сигналов.", new AcceptableValueRange<float>(0f, 1f)));
        AmbientIntervalMin = file.Bind("Звук", "Минимальный интервал самостоятельных звуков", 8f, "Минимальная пауза в секундах.");
        AmbientIntervalMax = file.Bind("Звук", "Максимальный интервал самостоятельных звуков", 20f, "Максимальная пауза в секундах.");
        AutoCarry = file.Bind("Перенос", "Автоматически переносить", true, "Переносить найденный дроп к точке разгрузки.");
        MaxCarryStacks = file.Bind("Перенос", "Максимум стопок", 3, new ConfigDescription("Максимальное число переносимых объектов.", new AcceptableValueRange<int>(1, 20)));
        MaxCarryWeight = file.Bind("Перенос", "Максимальный вес", 100f, "Максимальный суммарный вес груза.");
        DebugMode = file.Bind("Отладка", "Режим отладки", false, "Показывать внутреннее состояние духа.");
        OrbSize = file.Bind("Внешний вид", "Размер шара", 0.38f, new ConfigDescription("Диаметр визуального шара.", new AcceptableValueRange<float>(0.15f, 1f)));
        OrbColor = file.Bind("Внешний вид", "Цвет шара", "#33E6FF", "Цвет в формате #RRGGBB или #RRGGBBAA.");
        FollowDistance = file.Bind("Движение", "Расстояние от игрока", 1.7f, new ConfigDescription("Боковое расстояние при следовании.", new AcceptableValueRange<float>(0.5f, 8f)));
        FollowHeight = file.Bind("Движение", "Высота полёта", 2f, new ConfigDescription("Высота духа относительно игрока.", new AcceptableValueRange<float>(0.5f, 8f)));
        FollowSpeed = file.Bind("Движение", "Скорость полёта", 9f, new ConfigDescription("Обычная максимальная скорость.", new AcceptableValueRange<float>(2f, 30f)));
        MovementInertia = file.Bind("Движение", "Инертность", 0.3f, new ConfigDescription("Время плавного разгона и торможения. Больше — инертнее.", new AcceptableValueRange<float>(0.05f, 1.5f)));
        BobbingAmount = file.Bind("Движение", "Амплитуда покачивания", 0.25f, new ConfigDescription("Вертикальное покачивание шара.", new AcceptableValueRange<float>(0f, 1f)));
        LightMode = file.Bind("Освещение", "Режим света", SpiritLightMode.Spirit, "Off — выключен, Spirit — вокруг духа, CameraForward — освещает перед камерой.");
        LightColor = file.Bind("Освещение", "Цвет света", "#B8F4FF", "Цвет света в формате #RRGGBB.");
        LightIntensity = file.Bind("Освещение", "Яркость", 1.35f, new ConfigDescription("Яркость источника света.", new AcceptableValueRange<float>(0f, 5f)));
        LightRange = file.Bind("Освещение", "Дальность", 12f, new ConfigDescription("Дальность освещения.", new AcceptableValueRange<float>(2f, 40f)));
        CameraLightDistance = file.Bind("Освещение", "Расстояние перед камерой", 6f, new ConfigDescription("Положение света перед камерой.", new AcceptableValueRange<float>(1f, 20f)));
        SpiritXpMultiplier = file.Bind("Прогресс", "Множитель опыта духа", 0.25f, new ConfigDescription("Общий множитель опыта духа.", new AcceptableValueRange<float>(0f, 3f)));
        ProfessionXpMultiplier = file.Bind("Прогресс", "Множитель опыта профессий", 0.25f, new ConfigDescription("Множитель опыта профессий.", new AcceptableValueRange<float>(0f, 3f)));
        LogisticsXp = file.Bind("Прогресс", "Опыт за доставку", 0.1f, new ConfigDescription("Базовый опыт за одну завершённую доставку.", new AcceptableValueRange<float>(0f, 5f)));
        MaxWorkAttempts = file.Bind("Поведение", "Максимум попыток работы", 120, new ConfigDescription("Лимит ударов по одной секции до отмены.", new AcceptableValueRange<int>(10, 500)));
        TargetTimeout = file.Bind("Поведение", "Таймаут цели", 120f, new ConfigDescription("Максимальное время работы с одной секцией.", new AcceptableValueRange<float>(15f, 600f)));
    }
}
