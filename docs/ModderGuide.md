# Making your items paintable with Valheim Heraldry Mod

Valheim Heraldry Mod paints coats of arms (written as blazons) onto shields and build pieces. Your own mod's
shields and pieces can take arms too, with no code and no dependency on this mod: tag a face in your model, and
when Heraldry Mod is installed it finds the face and paints on it. Without Heraldry Mod the tag does nothing.
If you'd rather do it from code, or want to read or write arms, see [API.md](API.md).

## What you add (a few minutes in Blender)

1. **The face.** Pick one of the two ways:
   - **Own object (recommended):** duplicate the polygons of the surface that should carry the arms, separate them
     into their own object, and name that object exactly **`ValheimHeraldryMod_Face`**. Push it a millimetre or two
     out from the surface so it doesn't flicker. In Unity, **switch off its Mesh Renderer** (the component, not the
     object): unpainted items then look exactly as you made them, and Heraldry Mod switches it on when arms are
     painted.
   - **Material:** give the faces a material named exactly **`ValheimHeraldryMod_Face`**. They stay visible with
     that material until painted, so give it a sensible plain look.
2. **The centre (optional).** For a lopsided shape (a shield whose boss isn't in the middle), add an Empty named
   exactly **`ValheimHeraldryMod_CentrePoint`** where the centre of the arms should be. Partition lines, crosses,
   saltires and a lone charge all meet there. Without it, the middle of the face is used.

## More than one pattern (up to four)

An item or piece can carry up to **four different sets of arms** ("patterns"), for example a shield with its main
arms on the face and a second pattern on the rim. Number the tags:

| Tag | Pattern |
|---|---|
| `ValheimHeraldryMod_Face` or `ValheimHeraldryMod_Face_1` | 1 |
| `ValheimHeraldryMod_Face_2` | 2 |
| `ValheimHeraldryMod_Face_3` | 3 |
| `ValheimHeraldryMod_Face_4` | 4 |

- **Every part with the same number shows the same arms**, and each part gets a **full copy** laid out on its own
  shape. A "part" is one tagged object, or one tagged material slot on an object. So two separate cloths both tagged
  `_Face_2` each show the whole of pattern 2.
- If you want one image to run across several pieces instead, make them one part (one object, or one material slot)
  or use your own UVs (below).
- Centre points follow the same numbers: `ValheimHeraldryMod_CentrePoint_2` is pattern 2's centre;
  `ValheimHeraldryMod_CentrePoint` (no number) is pattern 1's.
- Players choose which pattern they're editing in the editor (a "Pattern" drop-down; it's greyed out when there's
  only one). Each pattern is painted, and paid for, separately. The inventory icon shows pattern 1.
- Numbers other than 1–4 are ignored, with a warning in the log.

**Watch out for Blender's `.001`.** When you duplicate an object or material, Blender names the copy
`ValheimHeraldryMod_Face.001`. That still counts as pattern 1 (the `.001` is ignored), which is fine for a second
copy of pattern 1, but if you meant pattern 2, rename it to `ValheimHeraldryMod_Face_2`. That's why the number
goes after an underscore and never after a dot.

## Flat, gently curved, or wrapping round?

By default Heraldry Mod ignores your UVs on the face and works out its own straight-on, flat layout:

- The face's "up" is your model's up (+Y in Unity). A face lying flat uses +Z as up.
- It paints within the face's own outline, so any shape works: round, kite, heater, wavy.
- **This is right for flat and gently curved faces** (most shields). The more the face turns away from the front,
  the more its sides stretch: about 15% at 30°, 40% at 45°, double at 60°. The log warns when a face curves a lot.

For a face that **wraps round** (a sleeve, a barrel, a cape, a curved tower shield) the flat layout can't work:
the sides smear and the back mirrors the front. Then use **your own UVs**:

1. Add an object (an Empty is fine) named exactly **`ValheimHeraldryMod_UseUVs`** anywhere in the model.
2. Unwrap each tagged part so its **UV0 fills 0–1** across the area the arms should cover, **right way up (+V is
   up)** and not mirrored: seen from the front, U runs left to right. Parts with the same number each use the
   whole 0–1 square (overlapping on purpose is fine; each gets its own copy of the arms).

The marker is **all or nothing**: with it, every tagged part on the model uses its UVs; without it, every part is
laid out flat. Proportions are worked out from the model, so a 2 m by 1 m part comes out 2:1 even though its UVs
are a square. A centre point is found on the part nearest to the Empty. The log warns if UVs fall outside 0–1 or
look mirrored.

## Names

All names are matched exactly (capitals matter). Unity's own " (Instance)" and "(Clone)" endings and Blender's
".001"-style endings are ignored.

## What Heraldry Mod does with it

- **Shields** (anything with ItemType Shield) are painted at the herald's table and show their arms in hand, on
  the back, on the ground, on item and armour stands, and in their inventory icon. A tagged face takes priority
  over Heraldry Mod's own painting of vanilla shields, and over the server's shield exclusion list.
- **Build pieces** are painted where they stand, like heraldic banners: walk up and press **E**. If your piece
  already uses E (a chest, a door), players use **Shift+E** instead (they can change that key).
- Pattern 1 is stored where it always was; patterns 2–4 are stored next to it, so items and pieces painted before
  numbered tags existed keep their arms.

## Mesh contract

- A plain static mesh (not skinned). Worn gear (capes, armour) isn't supported yet.
- Read/Write doesn't have to be on: the mesh is read from the graphics card if it isn't.
- Laid out flat: the face is rebuilt from positions, normals and UVs; vertex colours and second UV sets on the face
  are dropped. With `ValheimHeraldryMod_UseUVs` your mesh is used as it is.
- The painted face uses a copy of the face's own material with the arms as its main texture, so use a shader with
  a `_MainTex` slot (Valheim's own shaders do). If yours has none, a plain wood material is used.

## From code: the API (experimental)

Everything above can also be done from code, along with things tags can't do: reading and writing arms, offering a
guild's arms, drawing arms as a texture, and opening the blazon editor. It's called by reflection, so your mod
needs no reference to Heraldry Mod and still works without it. Copy [`HeraldryBridge.cs`](HeraldryBridge.cs) into
your project and see **[API.md](API.md)** for the full reference and examples. The API is experimental and may
still change; these tags won't.

## Checklist

- [ ] Object (or material) named `ValheimHeraldryMod_Face`, or `_Face_1` … `_Face_4` for several patterns, just
      proud of the surface
- [ ] No leftover `.001` copies that should have been a different number
- [ ] Own-object face: its Mesh Renderer switched off
- [ ] Optional `ValheimHeraldryMod_CentrePoint` (or `_CentrePoint_2` …) Empty
- [ ] Wraps round? `ValheimHeraldryMod_UseUVs` added, every tagged part unwrapped 0–1, right way up
- [ ] In game with Heraldry Mod: the log lists each tagged face with its pattern number; paint each pattern; check
      it from the front, that the top of the arms is at the top, and that nothing flickers

These names won't change. New optional markers may be added later; existing ones keep working.
