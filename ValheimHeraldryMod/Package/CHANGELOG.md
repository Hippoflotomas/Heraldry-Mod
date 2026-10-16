## Unreleased
- Tagged faces (for other modders): up to four patterns per item or piece (`ValheimHeraldryMod_Face_1` … `_4`,
  unnumbered = 1; centre points numbered the same way), each with a full copy of its arms on every part that
  carries its number. The editor gets a pattern picker (greyed out when there's only one); each pattern is painted
  and paid for separately; inventory icons show pattern 1. Existing tagged items keep their arms.
- Tagged faces: `ValheimHeraldryMod_UseUVs` makes every tagged face use the modder's own UVs (for faces that wrap
  round); faces that can't be laid out flat are refused with a hint, strongly curved ones get a warning.
- `heraldry_info` and the other charge commands now see tagged pieces, and every pattern.

## Version 1.1.0
- Tartan: `tartan "K4 R24 K24 Y4"` (a traditional threadcount) wherever a tincture goes, woven as a 2/2 twill and
  centred on the arms. Each stripe gets its own colour swatch and checks in the editor; each colour costs paint.
- Mod page: videos of Basic mode, Advanced mode and making a tartan.
- Herald's table recipe: 10 wood and 6 leather scraps (the berries and guck are gone now that paints exist).

## Version 1.0.0
First public release.
- Heraldic banners: the vanilla hanging banner and a free-standing large standard, edited in place (E), ward
  access like signs.
- Herald's table: crafts paints and the heraldic tabard; Shift+E paints shields and tabards. Vanilla shields
  are painted in place, keeping quality and enchantments.
- Arms show in hand, on the back, on the ground, on item and armour stands, and on inventory icons.
- Blazon editor with a basic (dropdown) and an advanced (text) mode, live preview, underlined problems with
  explanations, colour swatches and saved arms.
- Field divisions, patterns, ordinaries, lines of partition, furs, hex colours, geometric charges, game trophy
  heads, any game item as a charge, placement and points of the shield, voided, fimbriated, counterchanged.
- Your own charge pictures (PNGs, masks for lines, claws and tongue, packs and atlases), passed between players
  through the server with each player's consent; personal block list and admin blacklist.
- Mottos on scrolls and ordinaries, in letters or runes; server toggle to hide them (admins still see them).
- Tattered banners and tabards, worn paint on shields.
- Paint costs mixed from five paints, all configurable.
- Tagged faces: other mods' shields and build pieces can be made paintable with a named object or material.
