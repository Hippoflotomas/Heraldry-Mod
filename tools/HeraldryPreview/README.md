# HeraldryPreview

Dev tool for the parser and renderer in `ValheimHeraldryMod/Heraldry/`. Not shipped with the mod.

```
dotnet run -- parse blazons.txt                      # how each blazon is read, word by word
dotnet run -- render sheet2.txt out 406 1024 point   # one PNG per line (406x1024 = vanilla banner, 'point' = pointed bottom)
python sheet.py sheet2.txt out sheet2.png 9 130      # labelled contact sheet, shaded with the cloth normal map (needs Pillow + numpy)
```

Sheets: `sheet1-3` fields, ordinaries and lines; `sheet4` charge shapes and styles; `sheet5` charge placements; `sheet6` default arrangements by count.

An optional last argument to `render` picks how field charges use the tall banner (`compact`, `spread` or `reflow`; banners use `spread`), for comparing:

```
dotnet run -- render sheet6.txt out 406 1024 point reflow
```
