# Skyrim Audio Selector

A small WPF tool for resolving **audio conflicts** between Skyrim mods. When two or more mods (or the base game) provide the same sound or music file, this tool lets you preview each version and pick a winner, then generates a patch mod containing only your chosen files.

The patch is written as a regular mod folder so MO2/Vortex treat it like any other mod.

## Requirements

- Windows
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed if you build from source — published binaries can be self-contained)
- Skyrim Special Edition installed
- **Optional:** [`bsarch.exe`](https://www.nexusmods.com/skyrimspecialedition/mods/1756) — only needed if you want to pack the patch as a BSA instead of loose files
- **Optional:** `ffmpeg.exe` — only needed for previewing non-WAV audio (XWM, FUZ, etc.). Drop `ffmpeg.exe` next to `Skyrim Audio Selector.exe`, or make sure it is on your `PATH`

## How to use

1. **Set the paths** at the top of the window:
   - **Mods root** — your MO2 / Vortex `mods` folder (the folder that contains one subfolder per mod)
   - **Skyrim Data** — the `Data` folder under your Skyrim Special Edition install
   - **Output root** *(optional)* — where to create the patch mod folder. If left empty, the patch is created inside Mods root as `SkyrimAudioSelector_Patch`
   - **modlist.txt** *(optional)* — if you point this at your MO2-style `modlist.txt`, the tool will respect which mods are enabled and use their priority order. If omitted, every folder in Mods root is treated as enabled
2. Click **Scan for conflicts**. The tool walks the base game and every enabled mod (loose files + BSA/BA2 archives) and lists every audio path that is provided by more than one source.
3. Select a conflict in the left list. The right panel shows every variant — which mod, priority, duration, and source (loose vs. BSA). Use the **Play / Stop** button to preview each one.
4. Click **Set selected as winner** to mark a variant. Repeat for as many conflicts as you want — anything you don't pick a winner for is left alone and will keep following your normal load order.
5. Click **Generate patch**. The chosen winners are copied into the patch mod folder. If you tick **Pack patch into BSA** and provide a `bsarch.exe` path, the patch is packed as a BSA and the loose files are removed.

## Filters

- **Filter by mod** — only show conflicts that involve a specific mod
- **Show vanilla conflicts** — when off, hides conflicts that involve only the base game and at most one mod (so you only see real multi-mod conflicts)
- **Safe mode** — only consider conflicts that involve the base game plus at least one mod, and warn before generating a patch that includes pure mod-vs-mod winners
- **Search** — filter by any part of the audio path (e.g. `music/special`)

## Output

The patch is written to `<Output root>/SkyrimAudioSelector_Patch/` (or to a BSA inside that folder if BSA packing is enabled). Enable it in your mod manager and place it at the bottom of your load order so its files win.

To regenerate, just run the tool again and click **Generate patch** — the existing patch folder is replaced safely.

## Building from source

```
dotnet build "Skyrim Audio Selector.sln" -c Release
```

The build output lands in `Skyrim Audio Selector/bin/Release/net8.0-windows/`.