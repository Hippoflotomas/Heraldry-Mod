# Example charge pack

Copy the `test-beasts` folder (or a .zip of it) into `BepInEx/config/Heraldry/Charges/` (the folder is created the first time the mod runs).
Then: `Or, a [wolf] passant sable armed and langued gules`.

## manifest.json

```json
{
  "pack": "test-beasts",          // name for [test-beasts:wolf] when two packs share a charge name
  "version": 1,
  "charges": [                      // one PNG per drawing
    {
      "names": ["wolf", "wolves"],  // what goes in the brackets: singular, plural, any aliases
      "attitudes": ["passant"],     // what this drawing shows; leave out for the default drawing
      "image": "wolf_passant.png",  // colour + alpha: the outline, and the colours used for "proper"
      "mask": "wolf_passant_mask.png", // optional, same size: red = interior lines, green = armed, blue = langued
      "noLines": false              // true = don't derive interior lines from the colours when the mask has none
    }
  ],
  "atlases": [                      // or many drawings in one image
    {
      "file": "beasts.png", "mask": "beasts_mask.png",
      "cellSize": 256, "cols": 4, "rows": 4,
      "charges": [ { "cell": 0, "names": ["boar", "boars"], "attitudes": ["passant"] } ]
    }
  ]
}
```
(JSON itself has no comments; they're only here to explain.)

- Several drawings can share a name with different attitudes: `[wolf] rampant` picks the rampant one. A blazon asking for an attitude the pack doesn't have uses the closest drawing and says so.
- Limits: 1024 px per charge or cell, 4096 px per atlas, 16 MB per file. A bad image skips that one charge (see the log).
- Mask layers can go outside the colour image (a tongue in an open mouth); they're part of the outline.

## On a server

A server sends the packs in its own `BepInEx/config/Heraldry/Charges/` to players (folders are zipped for sending).
Admin settings: `MaxChargePackMB` (per pack, default 8) and `MaxChargePacksTotalMB` (default 32).

Players:
1. Turn on `ReceiveServerPacks` in the Heraldry config (off by default).
2. On joining, they're told which packs the server offers. `heraldry_packs` in the console (F5) lists them.
3. `heraldry_trust <pack>` (or `heraldry_trust all`) allows a pack. Trust is for that exact pack: if the server
   changes it, players are asked again. `heraldry_untrust <pack>` forgets it.
4. Allowed packs download once and are cached by fingerprint (`BepInEx/cache/Heraldry/packs`).

On someone else's server only the server's packs are used, so everyone sees the same arms. Your own packs are used in
single player and when you host.
