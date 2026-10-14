# ModelConvert

Turns a Blender model into the `.hmesh` file the mod embeds and builds at runtime, so no Unity asset bundle is
needed (and nothing clashes with BannerShare/TabardShare bundles loaded alongside).

```
assimp export "Herald Table2.fbx" table.obj
python obj_to_hmesh.py table.obj ../../ValheimHeraldryMod/Art/HeraldTable.hmesh --center
```

- Needs `assimp` (Assimp command-line tools) and Python with numpy.
- One submesh per Blender material slot; the mod maps slot names to vanilla materials (`Wood`, `Metal`, `Glass`, `brush`).
- Give the paintable face (e.g. the table's shield face) its own object with only the material `ValheimHeraldryMod_Face`; it becomes the "Display" part, with flat UVs made here.
- `--center` puts the origin in the middle of the footprint, on the floor.
