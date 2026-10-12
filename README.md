# Valheim Heraldry Mod

A Valheim mod (BepInEx + Jötunn) that renders heraldic **blazons** onto banners, and later shields and tabards. Players type a blazon; every client parses and draws it locally from the text.

Status: project set up, no features yet.

## Layout
- `ValheimHeraldryMod/`: the plugin (C#, .NET Framework 4.8)
- `ValheimHeraldryModUnity/`: Unity project for building asset bundles (open it in Unity 6000.0.75f1, the same editor as BannerShare/TabardShare)
- `scripts/publish.ps1`: post-build step. Debug copies the DLL to `MOD_DEPLOYPATH`; Release builds a Thunderstore zip.

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
2. Open `ValheimHeraldryMod.sln` in Visual Studio and build. The first build may show reference errors; close and reopen the solution and build again.
3. Building also copies the game, BepInEx and Jötunn DLLs into `ValheimHeraldryModUnity/Assets/Assemblies`. Those are gitignored: Valheim's DLLs are Iron Gate's code and must not be pushed.

## Licence
MIT No Attribution. See `LICENSE`.

Built from the [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) template.
