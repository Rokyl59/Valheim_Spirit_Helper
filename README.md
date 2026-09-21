# Spirit Helper

Client-side companion mod for Valheim. The spirit, UI, work zone and unload rune are local. World changes use only Valheim's existing damage, interaction and item-ownership paths; the mod registers no custom network prefabs or RPCs.

## Build

```powershell
dotnet build -c Release -p:GamePath="E:\SteamLibrary\steamapps\common\Valheim"
```

Copy `bin/Release/netstandard2.1/SpiritHelper.dll` into `BepInEx/plugins/SpiritHelper/`.

Управление: `G` открывает русское меню духа. Установка и удаление точки разгрузки, выбор её типа и рабочая зона находятся в подменю «Разгрузка и зона». Других горячих клавиш у мода нет. Настройки и локальный прогресс сохраняются в `BepInEx/config`.

Меню также содержит фильтры по категории и конкретному ресурсу, сканирование ресурсов, отзыв духа, переключение света, режим баланса, автоперенос, HUD и радиус работы. Фильтр применяется одновременно к поиску, добыче и подбору дропа. Заполненный груз доставляется к точке разгрузки, а при её отсутствии — к игроку.

On a server, damage, picking and item movement remain subject to vanilla ownership, ward and server validation. A server can reject or correct these actions; the mod will cancel the task instead of fabricating drops.
