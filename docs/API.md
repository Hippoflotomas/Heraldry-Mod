# Valheim Heraldry Mod: API for other mods

> **Experimental.** The API is new and may still change: a method may be renamed, take different parameters or
> behave differently in a later version, until it's marked stable. Every change raises `ApiVersion`, so check it
> and expect to update your code. The model tags in [ModderGuide.md](ModderGuide.md) are **not** experimental:
> they won't change.

Heraldry Mod paints coats of arms (blazons) on banners, shields, tabards and other mods' models. The API lets your
mod do four things:

1. **Make your models paintable** (from code, instead of naming parts in Blender).
2. **Read arms** from items, pieces, players and stands.
3. **Push arms** onto pieces and a player's items.
4. **Open the blazon editor** for the player.

Everything else is your mod's job: who may change arms and when, when to push them, getting them to each player's
client, items in storage, players who are offline.

## Contents

- [Versions](#versions)
- [Setting up](#setting-up)
- [Things to know first](#things-to-know-first)
- [Reference](#reference)
- [Recipes](#recipes)
- [Troubleshooting](#troubleshooting)
- [Without the bridge](#without-the-bridge)

## Versions

| `ApiVersion` | Heraldry Mod | What changed |
|---|---|---|
| 1 | 1.4.0 | First version: faces, read/write arms, blazon text, guild arms, `RenderBlazon`, the editor. |
| 2 | 1.5.0 | `GetSlotBlazon`, `OpenTableEditor`, `IsEditingLocked`. The editor calls ignore the server's new editing lock. |

## Setting up

Copy [`HeraldryBridge.cs`](HeraldryBridge.cs) into your project and change its namespace to yours. It finds Heraldry
Mod at runtime, so you **don't reference its dll**, and your mod works with or without it installed: without it,
every bridge call quietly returns false, `""`, `null` or an empty array.

Add a **soft dependency** so Heraldry Mod loads before you when it's there (it stays optional):

```csharp
[BepInPlugin("com.you.yourmod", "Your Mod", "1.0.0")]
[BepInDependency("com.hippotech.valheimheraldrymod", BepInDependency.DependencyFlags.SoftDependency)]
public class YourMod : BaseUnityPlugin
{
    private void Awake()
    {
        if (!HeraldryBridge.Available) return;            // Heraldry Mod isn't installed
        if (HeraldryBridge.ApiVersion < 2) Logger.LogWarning("Heraldry Mod is older than expected; some features are off.");

        HeraldryBridge.RegisterFace("YourKiteShield", "attach/model/front", 1);
    }
}
```

The bridge works against older Heraldry Mod versions too: a call the installed version doesn't have does nothing.

## Things to know first

**Blazons are text.** Arms are written as a blazon, `Azure, a bend or`. Every call takes and returns that text.
Use `CheckBlazon` to ask whether a blazon is fine before using it.

**Stored text.** Arms come out exactly as stored: the text players write, sometimes followed by an invisible
character (U+001F) and a hidden list naming the exact picture of each `[bracketed]` custom charge. Pass the text back
as it is to copy arms exactly, and use `DisplayBlazon` before showing it to a player.

**Patterns.** One item or piece can carry up to four separate sets of arms, numbered 1–4 (a shield with arms on its
face as pattern 1 and on its rim as pattern 2, say). Most things have only pattern 1. `GetItemPatterns` and
`GetPiecePatterns` say which exist.

**Where to call what.** Each player's game is its own client; a dedicated server has no player and draws nothing.

| Call | Where |
|---|---|
| `RegisterFace…`, `RegisterArmsProvider` | **Every client** (and the server, harmless), early: your plugin's `Awake`. Registration isn't sent anywhere. |
| `Get…Blazon`, `GetSlotBlazon` | Any client, or the server for what it has loaded. |
| `SetPieceBlazon` | Any client or the server, for a piece that's loaded there. Every client sees it within about a second. |
| `SetItemBlazon` | **The client of the player holding the item.** Items live in their owner's inventory; sending the blazon to that client is your mod's job. |
| `RenderBlazon`, `OpenEditor…`, `OpenTableEditor`, `IsEditingLocked` | A client with a player (not a dedicated server). |

**Costs, wards and the lock.**

| | Paint cost | Ward | Server's editing lock |
|---|---|---|---|
| `SetPieceBlazon`, `SetItemBlazon` | none | not checked | ignored |
| `OpenEditor` (a piece) | as normal (banner paint setting) | checked | **ignored** |
| `OpenTableEditor` | as normal (`PaintCosts`) | none (no table) | **ignored** |
| `OpenEditorForText` | none (nothing is painted) | none | **ignored** |
| A player using a banner, piece or table themselves | as normal | checked | **applies** |

A player can't call the API, so the writes trust your mod to decide who may change what. The server's limits on
length and complexity always apply: arms over them are refused.

**The editing lock.** A server can set `LockEditing` (section Server, admin only). Players then can't change arms
themselves: banners, pieces and the herald's table open the blazon window greyed out, with *"Arms must first be
granted in this realm."* Admins are never locked. The API's editor calls ignore the lock, so a mod can grant editing
on its own terms (a guild leader, a king, a quest reward). `IsEditingLocked()` tells you whether the local player is
locked.

**Nothing throws.** A failed call returns false, `""`, `null` or an empty array, and logs the reason as
`[Heraldry API] …` in BepInEx's log.

**Plain types only, no overloads.** Every method has its own name and uses strings, numbers, `GameObject`,
`ItemDrop.ItemData`, `Texture2D`, `Func` or `Action`, so reflection always finds it by name.

## Reference

### Making your models paintable

#### `bool RegisterFace(string prefabName, string partPath, int pattern)`
The object at `partPath` in your prefab is painted with pattern `pattern` (1–4), laid out flat, as if it were named
`ValheimHeraldryMod_Face_<pattern>`.

- `prefabName`: your prefab's name. A **shield** is its item prefab (the one with the `ItemDrop`); a **build piece** is
  the piece prefab.
- `partPath`: the path from the prefab's root, as `Transform.Find` takes it (`"attach/model/front"`); `""` = the root.
  The part needs a `MeshRenderer` and `MeshFilter`; skinned meshes can't be painted this way (see `RenderBlazon`).
- Registering the same part again replaces it. Several parts can share a pattern; each gets a full copy of the arms.
- Shields are then painted at the herald's table and show their arms in hand, on the back, on the ground, on stands
  and in their icon. Pieces are painted in place: E, or Shift+E when the piece already uses E (a chest, a door).
- Pieces placed before you registered pick it up the next time they're loaded.
- If the object also carries `ValheimHeraldryMod_Face` name tags, the tags win and this registration is ignored.
- Returns false for a missing name or a pattern outside 1–4. A wrong path is logged when the prefab is first scanned.

#### `bool RegisterFaceAdvanced(string prefabName, string partPath, int pattern, int materialSlot, string centrePath, bool useUVs)`
`RegisterFace` with every option:
- `materialSlot`: `-1` = the whole object is the face; `0`, `1`… = only that material slot (submesh).
- `centrePath`: an object (an empty) marking where the centre of the arms goes, or `null` for the middle.
- `useUVs`: lay the arms out in the part's own UV0 (0–1 across the area, +V up, not mirrored) instead of flat. Use it
  for faces that wrap round. **All or nothing per prefab:** true on any part switches every part of that prefab to
  its UVs, as the `ValheimHeraldryMod_UseUVs` tag does.

### Reading arms

#### `int[] GetPiecePatterns(GameObject piece)`
The patterns a placed build piece can carry: `[1]` for a heraldic banner, the numbers of its faces for a painted
piece, `[]` if it takes no arms. Any object under the piece works.

#### `string GetPieceBlazon(GameObject piece, int pattern)`
The arms on a placed piece, or `""`.

#### `int[] GetItemPatterns(ItemDrop.ItemData item)`
The patterns an item can carry: a shield's faces, `[1]` for a plain shield or a tabard, `[]` if it can't carry arms.

#### `string GetItemBlazon(ItemDrop.ItemData item, int pattern)`
The arms painted on an item, or `""`.

#### `string GetSlotBlazon(GameObject target, string slot, int pattern)` (API 2)
The arms by slot on a player, armour stand or item stand. It reads what Heraldry Mod already shares with every
client, so it works for **any loaded player or stand**, not only your own.

| Target | Slots |
|---|---|
| Player | `"HandLeft"` (shield in hand), `"BackLeft"` (shield on the back), `"Tabard"`; or `"0"`, `"1"`, `"2"` |
| Armour stand | Valheim's slot name (`"HandLeft"`, `"BackLeft"`…) or the index in the stand's slot list (`"0"`, `"1"`…) |
| Item stand | `""` (or `"0"`) |

- Names ignore case. An unknown slot returns `""` and logs the slots that exist.
- Players show **what's displayed**: a sheathed shield is on the back, not in the hand. It can lag an equip change by
  one network update.
- The tabard has only pattern 1. On stands, only shields are painted.
- **Which item** is in a slot is Valheim's to say: `ItemStand.m_currentItemName`, and `m_currentItemName` on each
  of `ArmorStand.m_slots`.

### Writing arms

#### `bool SetPieceBlazon(GameObject piece, string blazon, int pattern)`
Paints a placed piece (`""` takes the arms off). No paint, no ward check, ignores the lock; the server's limits
apply. Every client sees it within about a second. False if the object isn't a placed build piece that takes arms
in that pattern, or the arms are over the limits.

#### `bool SetItemBlazon(ItemDrop.ItemData item, string blazon, int pattern)`
Paints a shield or tabard (`""` takes the arms off). No paint, no ward check, ignores the lock; limits apply.
**Call it on the client of the player who holds the item.** If it's in the local player's inventory, what they hold
and wear updates at once. False for items that can't carry arms, a pattern the item doesn't have, or arms over the
limits.

### Blazon text

#### `string CheckBlazon(string blazon)`
`null` if the arms are fine to apply on this server, otherwise the first problem in plain words:
`"'bned': unknown charge 'bned': did you mean 'bend'?"`, `"too complex for this server: 31 things to draw, at most 25"`.
Words the editor only warns about (a passable spelling) count as fine.

#### `string DisplayBlazon(string blazon)`
The arms as players see them: the hidden picture list removed, runes shown as letters. Use it for any text you show.

#### `string ExpandBlazon(string blazon)`
A few secret words stand for special designs (when the server allows them). This turns one into the blazon it stands
for; anything else comes back unchanged.

### Guild arms

#### `bool RegisterArmsProvider(string label, Func<Player, string> armsFor)`
Offers arms in the editor. Each time the blazon window opens, `armsFor` is called with the local player and returns a
blazon (or `null`/`""` for none). The arms appear at the top of the player's saved blazons under `label` (in gold);
clicking loads them into the editor, where the player can change them and press Apply. Nothing is applied for them.
Registering the same label again replaces it. If your function throws, that row is skipped and the error logged.

### The arms as a picture

#### `Texture2D RenderBlazon(string blazon, int width, int height)`
The arms drawn as a `width` × `height` texture (each 16–2048), filling the whole rectangle. Tall ones lay out like
banners, others like shields. Use it for things the face route can't paint: skinned meshes, UI, map markers.
- **Shared:** the same arms at the same size give the same texture. Don't destroy it or change its pixels.
- **Drawn in the background:** it shows the field's colour at first and the full arms a frame or two later, in the
  same texture, so a material or `RawImage` using it updates by itself.
- `null` on a dedicated server or on failure.

#### `void ReleaseBlazonTexture(Texture2D texture)`
Lets go of a texture from `RenderBlazon`. Call it once for every `RenderBlazon` call, and don't use the texture
afterwards.

### The editor

#### `bool IsEditingLocked()` (API 2)
True when the server locks editing for the local player (`LockEditing` on, and they're not an admin).

#### `bool OpenEditor(GameObject piece, int pattern)`
Opens the blazon editor on a placed piece that takes arms, as if the player had pressed E. Ward access and paint as
normal; **the editing lock is ignored** (API 2). `pattern` 0 = the piece's first pattern. False if it isn't a piece that
takes arms, the player lacks ward access, or the editor is already open.

#### `bool OpenTableEditor()` (API 2)
Opens the herald's table window without a table: the local player chooses one of their shields or tabards (equipped
first) and paints it. Paint follows the server's `PaintCosts` like the table. **Ignores the editing lock.** This is
how a player paints their own items when the server locks editing.

#### `bool OpenEditorForText(string title, string blazon, float aspect, Action<string> onApply)`
Opens the editor on arms that aren't on anything yet (a guild leader choosing the guild's arms, a choice before
crafting). It starts with `blazon`, previews at `aspect` (width over height; `0` = a shield's shape) and, when the
player presses Apply, calls `onApply` with the arms as stored (already checked against the server's limits). Cancel
calls nothing. No paint is paid; nothing is painted. Ignores the editing lock.

### Version

`ApiVersion` (const int) and `ModVersion` (string, Heraldry Mod's own version). Through the bridge:
`HeraldryBridge.ApiVersion` (0 when Heraldry Mod isn't installed).

## Recipes

### Your shield and your gatehouse
```csharp
// In Awake, on every client.
// A shield: the item prefab, the face object inside it. Painted at the herald's table.
HeraldryBridge.RegisterFace("YourKiteShield", "attach/model/front", 1);
// A build piece with two crests: the main arms, and a second set over the gate, centred on an empty.
HeraldryBridge.RegisterFace("YourGatehouse", "model/crest_left", 1);
HeraldryBridge.RegisterFace("YourGatehouse", "model/crest_right", 1);
HeraldryBridge.RegisterFaceAdvanced("YourGatehouse", "model/gate_banner", 2, -1, "model/gate_banner_centre", false);
```
The gatehouse's door already uses E, so players edit its arms with Shift+E. Both crests show pattern 1; the gate
banner shows pattern 2, chosen in the editor's pattern picker. A cloth that billows (a sail with Unity's Cloth
component) is a skinned mesh: draw it with `RenderBlazon` and put the texture on it yourself.

### A guilds mod: the leader sets the arms, members wear them
```csharp
// 1. The leader chooses the guild's arms (from your guild window):
HeraldryBridge.OpenEditorForText("Arms of " + guild.Name, guild.Arms, 0f, arms => {
    guild.Arms = arms;            // your data; sync it to the server and members your way
});

// 2. Offer them in every member's editor (in Awake, every client):
HeraldryBridge.RegisterArmsProvider("Guild arms", player => MyGuilds.Of(player)?.Arms);

// 3. Or push them: on each member's own client, when your mod tells it to.
//    Equipped shields and tabards get the guild's arms as their first pattern.
foreach (var item in Player.m_localPlayer.GetInventory().GetEquippedItems())
{
    int[] patterns = HeraldryBridge.GetItemPatterns(item);   // [] = can't carry arms
    if (patterns.Length > 0) HeraldryBridge.SetItemBlazon(item, guild.Arms, patterns[0]);
}
```
Pushing is free and skips the lock. Getting `guild.Arms` to each member's client, and deciding when to push, is
your mod's part.

### A roleplay server where arms are granted
The server sets `LockEditing = true`, so players can only look. When your mod grants arms (a king's decree, a quest),
on that player's client:
```csharp
HeraldryBridge.OpenTableEditor();     // they paint their own shield or tabard, once, past the lock
// or paint it for them:
HeraldryBridge.SetItemBlazon(shield, "Gules, a lion or", 1);
```
Banners and pieces work the same way with `OpenEditor(piece, 0)` or `SetPieceBlazon`.

### Arms in your UI
```csharp
var tex = HeraldryBridge.RenderBlazon(arms, 128, 154);
rawImage.texture = tex;               // fills in by itself a frame later
// when the panel closes:
HeraldryBridge.ReleaseBlazonTexture(tex);
```

### What is that visitor carrying?
```csharp
string shield = HeraldryBridge.GetSlotBlazon(otherPlayer.gameObject, "HandLeft", 1);
if (shield == "") shield = HeraldryBridge.GetSlotBlazon(otherPlayer.gameObject, "BackLeft", 1);
string tabard = HeraldryBridge.GetSlotBlazon(otherPlayer.gameObject, "Tabard", 1);
```

## Troubleshooting

- **Look in the BepInEx log** for `[Heraldry API]` (problems with your calls) and `[Heraldry]` (each registered or
  tagged face, with its pattern and shape, is logged the first time it's painted or previewed).
- **A registered face doesn't paint:** check the prefab name (shields: the item prefab), the path (`Transform.Find`
  style, from the root), and that the part has a `MeshRenderer` and `MeshFilter`. Register on every client, in
  `Awake`. A part already carrying `ValheimHeraldryMod_Face` tags keeps the tags.
- **A write returns false:** the log says why (not a piece, no such pattern, over the server's limits).
  `CheckBlazon` first gives you the reason before writing.
- **Pushed arms show a magenta-and-black roundel:** the arms use a `[custom]` charge that player doesn't have yet.
  Each player gets a charge picture themselves (`heraldry_get` in the console, or the client setting
  `AutoGetServerCharges` for charges in the server's folder). Heraldry Mod doesn't trust pictures for players on
  your behalf.
- **A shield's arms lag behind an equip change:** players publish what they show with the game's own updates;
  read again a moment later.

## Without the bridge

Look the type up yourself and call by name:

```csharp
var api = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "ValheimHeraldryMod")?.GetType("ValheimHeraldryMod.HeraldryAPI");
if (api != null)
{
    int version = (int)api.GetField("ApiVersion").GetValue(null);
    api.GetMethod("RegisterFace")?.Invoke(null, new object[] { "YourKiteShield", "attach/model/front", 1 });
}
```
