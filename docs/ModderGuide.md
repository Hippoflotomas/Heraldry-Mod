# Making your items paintable with Valheim Heraldry Mod

Valheim Heraldry Mod paints coats of arms (written as blazons) onto shields and build pieces. Your own mod's
shields and pieces can take arms too, with no code and no dependency on this mod: tag a face in your model, and
when Heraldry Mod is installed it finds the face and paints on it. Without Heraldry Mod the tag does nothing.
If you'd rather do it from code, or want to read or write arms, see [the API](#from-code-the-api-heraldry-mod-140-and-later).

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

## From code: the API (Heraldry Mod 1.4.0 and later; experimental)

> **Experimental.** The API is new and may still change: methods may be renamed, change their parameters or
> behave differently in a later version, until it's marked stable. Every change raises `ApiVersion`, so check it
> (`HeraldryBridge.ApiVersion`) and expect to update your code. The tags above are not experimental: they won't
> change.

Everything above also works from code, plus a few things tags can't do: reading and writing arms, suggesting a
guild's arms, getting the arms as a texture, and opening the editor. It's a public static class,
`ValheimHeraldryMod.HeraldryAPI`, meant to be called **by reflection**, so your mod needs no reference to Heraldry
Mod and still works when it isn't installed.

**The easy way:** copy [`HeraldryBridge.cs`](HeraldryBridge.cs) into your project and change its namespace. It does
the reflection once and gives you normal methods; without Heraldry Mod every call quietly returns false, "" or
null. Add a soft dependency so Heraldry Mod loads first when it's there:

```csharp
[BepInDependency("com.hippotech.valheimheraldrymod", BepInDependency.DependencyFlags.SoftDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        if (!HeraldryBridge.Available) return;
        // A shield: the item's prefab, path from its root. A build piece: the piece's prefab.
        HeraldryBridge.RegisterFace("MyKiteShield", "attach/model/front", 1);
        HeraldryBridge.RegisterFace("MySailShip", "ship/visual/sail", 1);
        // A guild's arms, offered in the editor to its members
        HeraldryBridge.RegisterArmsProvider("Guild arms", player => MyGuilds.ArmsOf(player));
    }
}
```

| Method | What it does |
|---|---|
| `RegisterFace(prefab, path, pattern)` | The part at `path` (from the prefab's root; `""` = the root) is painted as pattern 1–4, laid out flat. The same as naming it `ValheimHeraldryMod_Face_n`. |
| `RegisterFaceAdvanced(prefab, path, pattern, slot, centrePath, useUVs)` | With every option: `slot` -1 = the whole object, or one material slot; `centrePath` an empty marking the centre, or null; `useUVs` = use the part's own UV0 (all or nothing per prefab, as with the tag). |
| `GetPiecePatterns(piece)` / `GetItemPatterns(item)` | Which patterns a placed piece or an item can carry; empty if none. |
| `GetPieceBlazon(piece, pattern)` / `SetPieceBlazon(piece, blazon, pattern)` | Read or write the arms on a placed piece ("" takes them off). |
| `GetItemBlazon(item, pattern)` / `SetItemBlazon(item, blazon, pattern)` | The same for a shield or tabard. |
| `CheckBlazon(blazon)` | Null if the arms can be applied here, else the first problem in plain words. |
| `DisplayBlazon(blazon)` | The arms as players see them (see "Stored text" below). |
| `ExpandBlazon(blazon)` | A secret code becomes the blazon it stands for; anything else comes back as it is. |
| `RegisterArmsProvider(label, Func<Player, string>)` | Each time the editor opens, your function gets the local player and returns a blazon (or null). It's offered at the top of the saved blazons under `label`; the player can load it, change it and apply it. |
| `RenderBlazon(blazon, width, height)` / `ReleaseBlazonTexture(texture)` | The arms as a texture, for things the face route can't paint: skinned meshes, UI, map markers. |
| `OpenEditor(piece, pattern)` | The editor on a placed piece, as if the player had pressed E (ward and paint as normal; pattern 0 = the first). |
| `OpenEditorForText(title, blazon, aspect, Action<string>)` | The editor on arms that aren't on anything (a guild leader setting the guild's arms): Apply hands you the arms. Cancel calls nothing. `aspect` is width over height, 0 = a shield shape. |
| `ApiVersion`, `ModVersion` | The API's version (1) and Heraldry Mod's. |

**Rules you can count on:**
- **Writes cost no paint and check no ward.** A player can't call the API, so your mod decides who may change
  what. The server's limits (length, complexity) still apply: arms over them are refused.
- **Items live with whoever holds them.** Call `SetItemBlazon` on the holding player's own client (their inventory).
  Pieces can be written from any client or the server; every client sees the change within about a second.
- **Registration is local.** Call `RegisterFace` and `RegisterArmsProvider` on every client (your plugin's
  Awake is the place), before the world loads. Pieces placed before a face was registered pick it up when they're next
  loaded.
- **Tags win.** On an object carrying `ValheimHeraldryMod_Face` tags, a registration for the same object is ignored.
- **Textures are shared.** The same arms at the same size give the same texture. Don't destroy it or change its
  pixels: release it once for every `RenderBlazon`. It's drawn in the background, showing the field's colour at first
  and the full arms a frame or two later in the same texture (a material using it updates by itself). On a dedicated
  server it's null.
- **Nothing throws.** Failures return false, "", null or an empty array, with the reason in the log
  (`[Heraldry API] …`).
- **Plain and unambiguous.** Every method has its own name (no overloads, so `GetMethod(name)` is never ambiguous)
  and uses plain types. While the API is experimental, methods can still change; any change raises `ApiVersion`.
  Once it's marked stable, changes will only ever add.

**Stored text.** Arms come out exactly as stored: the text players write, sometimes followed by a hidden tail
(after the invisible character U+001F) naming the exact picture of each `[bracketed]` charge. Pass that text back
as it is to copy arms exactly, and use `DisplayBlazon` to show it to players.

**Without the bridge**, look the type up yourself:

```csharp
var api = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "ValheimHeraldryMod")?.GetType("ValheimHeraldryMod.HeraldryAPI");
api?.GetMethod("RegisterFace")?.Invoke(null, new object[] { "MyKiteShield", "attach/model/front", 1 });
```

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
