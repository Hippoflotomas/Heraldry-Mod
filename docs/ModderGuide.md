# Making your items paintable with Valheim Heraldry Mod

Valheim Heraldry Mod paints coats of arms (written as blazons) onto shields and build pieces. Your own mod's
shields and pieces can take arms too, with no code and no dependency on this mod: tag a face in your model, and
when Heraldry Mod is installed it finds the face and paints on it. Without Heraldry Mod the tag does nothing.

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

Names are matched exactly (capitals matter); Unity's own " (Instance)" and "(Clone)" endings are ignored.

## What Heraldry Mod does with it

- Works out its own flat layout of the face (your UVs on the face are ignored) and paints within the face's own
  outline, so any shape works: round, kite, heater, wavy.
- Straight-on view: the face's "up" is your model's up (+Y in Unity). A face lying flat uses +Z as up.
- **Shields** (anything with ItemType Shield) are painted at the herald's table and show their arms in hand, on
  the back, on the ground, on item and armour stands, and in their inventory icon. A tagged face takes priority
  over Heraldry Mod's own painting of vanilla shields, and over the server's shield exclusion list.
- **Build pieces** are painted where they stand, like heraldic banners: walk up and press **E**. If your piece
  already uses E (a chest, a door), players use **Shift+E** instead (they can change that key).

## Mesh contract

- A plain static mesh (not skinned). Worn gear (capes, armour) isn't supported yet.
- Read/Write doesn't have to be on: the mesh is read from the graphics card if it isn't.
- The face is rebuilt from positions, normals and UVs; vertex colours and second UV sets on the face are dropped.
- The painted face uses a copy of the face's own material with the arms as its main texture, so use a shader with
  a `_MainTex` slot (Valheim's own shaders do). If yours has none, a plain wood material is used.

## Checklist

- [ ] Object (or material) named `ValheimHeraldryMod_Face`, just proud of the surface
- [ ] Own-object face: its Mesh Renderer switched off
- [ ] Optional `ValheimHeraldryMod_CentrePoint` Empty
- [ ] In game with Heraldry Mod: the log says "has a tagged heraldry face"; paint it; check it from the front,
      that the top of the arms is at the top, and that nothing flickers

These names won't change. New optional markers may be added later; existing ones keep working.
