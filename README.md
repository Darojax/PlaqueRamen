# PlaqueRamen

Live menu boards, framed certificates and old photographs for **Ramen Shop Simulator (Windows/Steam)**.

## Installation

1. Close the game.
2. Install [BepInEx 5 Windows x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) beside `Ramen Shop Simulator.exe`. Launch once, then close the game. Skip this step if BepInEx is already installed.
3. Download the mod ZIP from [Releases](https://github.com/Darojax/PlaqueRamen/releases/latest) and extract it into the game folder, merging the `BepInEx` folders.
4. Launch the game and load your save. The mod's name and version appear briefly at the bottom left.

When updating, replace the existing mod files. If you used the old `MenuPlaqueSync` prototype, remove its folder from `BepInEx/plugins` first.

## Using the mod

Buy a **Live Menu Board Shelf** from the Goods Shop for **2,000 yen**. The light version has nine slots; the dark version has seven. Set your menu at the ticket machine and the boards update automatically. Original Menu Board Shelves remain static.

The ticket machine is read from left to right across each row, starting at the top. Selected dishes appear **right to left** on the boards, with drinks to their left and a gap when space allows. The seven-slot board prioritises the most expensive dishes; if only one drink fits, Bottle takes priority over Draft. Plaques have individual wood grain, with blue drink names on light wood and worn gold on dark wood. The Horizon draft beer paper sign also shows the corrected **550 yen** price.

The original **Certificate of Appreciation** has sharper artwork at its original size. The Goods Shop adds these wall decorations:

| Item | Price |
| --- | ---: |
| Food Hygiene Award | 5,000 yen |
| Community Commendation | 5,000 yen |
| Culinary School Diploma | 5,000 yen |
| Ramen Training Certificate | 5,000 yen |
| Operating Permit - Sunset Yokocho 9 | 2,000 yen |
| Operating Permit - Sunset Yokocho 68 | 2,000 yen |
| Ancestor Portrait | 10,000 yen |
| Family Portrait | 10,000 yen |

Each design has its own frame and appearance. The Certificate of Appreciation and Food Hygiene Award include wooden display ledges. All items use the game's delivery, storage and placement controls. No configuration or hotkey is needed.

## Removal

Collect pending deliveries and sell all mod-added items, including stored ones, before uninstalling. Close the game and remove `BepInEx/plugins/PlaqueRamen`. The original Certificate of Appreciation returns to its vanilla appearance.

## Building from source

Requires Windows, the installed game, [Git](https://git-scm.com/downloads/win) and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

Run in PowerShell, replacing `-GameRoot` with your game folder:

```powershell
git clone https://github.com/Darojax/PlaqueRamen.git
cd PlaqueRamen
.\scripts\Build.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\Ramen Shop Simulator'
```

The script downloads and verifies the pinned BepInEx build dependencies, builds the mod, runs its checks and creates `dist/PlaqueRamen-0.3.7.zip`. Install that ZIP using the steps above. Game libraries are read from your installation and are not included.
