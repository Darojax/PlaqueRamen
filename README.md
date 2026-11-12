# PlaqueRamen

Menu plaques that follow your ticket machine automatically in **Ramen Shop Simulator (Windows/Steam)**.

The bright nine-plaque and dark seven-plaque Menu Board Shelves display your selected ramen dishes with individual wood grain and their correct prices. The Horizon draft beer paper sign is also corrected to **550 yen**.

## Installation

1. Close the game.
2. Install [BepInEx 5 Windows x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) in the folder containing `Ramen Shop Simulator.exe`. Launch once, then close the game. Skip this step if BepInEx is already installed.
3. Extract the PlaqueRamen ZIP into the game folder, merging the `BepInEx` folders.
4. Launch the game and load your save.

If you used the earlier private plaque prototype, remove its `BepInEx/plugins/MenuPlaqueSync` folder first. The two plugins must not run together.

## Using the mod

Set your menu at the ticket machine as usual. Every supported seven- or nine-plaque Menu Board Shelf updates automatically, including boards placed during play. No aiming, key combination or configuration is needed. Standalone wooden plaques keep their original appearance.

Each dish has bright and dark counterparts using the same approved name and price. Bright plaques use black names and red prices. Dark plaques use the original off-white paint and dark stain, with the dish name at the upper right and the price at the lower left. Each dish keeps its own grain pattern in both finishes.

Machine buttons are read across each row from the top left:

```text
1 2 3
4 5 6
7 8 9
```

Selected ramen dishes appear **right to left** on the board. Drinks and empty buttons are skipped; repeated dishes appear once. If more dishes are selected than the holder can display, it includes the **most expensive dishes**. Equal prices favour the earlier ticket-machine button. The included dishes retain their ticket-machine reading order.

Plaques stay together around the middle, with even counts leaning slightly to the right. All native hooks or pins stay fixed and visible, with their rear mounts reaching the frame.

The ten supported dishes are Instant Chicken, Egg Instant, Miso Butter Corn, Su-Tonkotsu, Kitakata, Hakata, Sapporo, Giro, Iekei and Toyama Black. If a game update changes a dish's price or the required game data, the affected signs are left original rather than displaying incorrect information. Compatibility is checked by functionality, without a fixed game-version restriction.

To uninstall, close the game and remove `BepInEx/plugins/PlaqueRamen`. The mod does not change recipes, selling prices or saved decorations.

## Building from source

Requires Windows, the installed game, [Git](https://git-scm.com/downloads/win) and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

In PowerShell:

```powershell
git clone https://github.com/Darojax/PlaqueRamen.git
cd PlaqueRamen
.\scripts\Build.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\Ramen Shop Simulator'
```

Replace `-GameRoot` with your game folder. The script downloads and hash-verifies the pinned BepInEx build dependencies, builds the DLL, runs the checks and creates **`dist/PlaqueRamen-0.1.9.zip`**. Game libraries are referenced from your installation and are not included in the ZIP. Install the ZIP using the steps above.
