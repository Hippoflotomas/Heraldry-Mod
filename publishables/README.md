# publishables

Building in **Release** creates the Thunderstore package here:

- `ValheimHeraldryMod-<version>.zip` - upload this to Thunderstore
- `ValheimHeraldryMod\` - the same contents unzipped, for checking

Contents come from `ValheimHeraldryMod\Package` (manifest.json, icon.png, README.md, CHANGELOG.md)
plus the built DLL in `plugins\`. The version in the zip name is read from manifest.json, and the
build stops if it doesn't match PluginVersion in ValheimHeraldryMod.cs - bump both together.

Everything in this folder except this README is ignored by git.
