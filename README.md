# Valheim Heraldry Mod

A Valheim mod (BepInEx + Jötunn) that paints heraldic **blazons** onto banners, shields and tabards. Players write a
blazon or pick one from dropdowns, and every client parses and draws it locally from the text. The player-facing
description is [ValheimHeraldryMod/Package/README.md](ValheimHeraldryMod/Package/README.md), which is also the
Thunderstore page.

## Layout
- `ValheimHeraldryMod/`: the plugin (C#, .NET Framework 4.8).
  - `Heraldry/`: pure C# with no Unity types: blazon parser, renderer, charge packs, PNG/zip/JSON readers. A dedicated
    server and the preview tool run it as-is.
  - `Banners/`, `Shields/`, `Tabards/`, `Station/`, `Paints/`, `Rendering/`, `UI/`: the game side.
  - `Art/`: models (`.hmesh`/`.hskin`, built at runtime; no asset bundles), the cloth normal map and the built-in charge.
  - `Package/`: Thunderstore files (manifest, icon, README, CHANGELOG).
- `docs/ModderGuide.md`: how other mods make their shields and pieces paintable (tagged faces).
- `tools/HeraldryPreview`: .NET 8 console tool that parses and renders blazons outside the game.
- `tools/ModelConvert`: Blender/FBX → `.hmesh` converter (via `assimp export`), and the tabard extractor.
- `tools/ExamplePack`: an example charge pack.
- `scripts/publish.ps1`: post-build step. Debug copies the DLL to `MOD_DEPLOYPATH`; Release builds the Thunderstore
  package in `publishables/`.
- `ValheimHeraldryModUnity/`: Unity project from the template. Not used for the mod's assets (they're built at
  runtime); the build still copies the game assemblies into it.

## Building
1. Create `Environment.props` in the repo root (gitignored, per machine):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <VALHEIM_INSTALL>C:\Program Files (x86)\Steam\steamapps\common\Valheim</VALHEIM_INSTALL>
    <BEPINEX_PATH>path\to\your\mod-manager-profile\BepInEx</BEPINEX_PATH>
    <MOD_DEPLOYPATH>$(BEPINEX_PATH)\plugins</MOD_DEPLOYPATH>
  </PropertyGroup>
</Project>
```
2. Open `ValheimHeraldryMod.sln` in Visual Studio and build. The first build may show reference errors; close and reopen
   the solution and build again.
3. Building also copies the game, BepInEx and Jötunn DLLs into `ValheimHeraldryModUnity/Assets/Assemblies`. Those are
   gitignored: Valheim's DLLs are Iron Gate's code and must not be pushed.

## Releasing
1. Bump `PluginVersion` in `ValheimHeraldryMod/ValheimHeraldryMod.cs` and `version_number` in
   `ValheimHeraldryMod/Package/manifest.json` together (the Release build stops if they differ), and add a
   `CHANGELOG.md` entry.
2. Build in **Release**. Upload `publishables/ValheimHeraldryMod-<version>.zip` to Thunderstore.

## Licence
MIT No Attribution. See `LICENSE`.

Built from the [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) template.
