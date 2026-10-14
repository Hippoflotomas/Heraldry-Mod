# Example charge pack

Copy the `test-beasts` folder (or a .zip of it) into `Documents\ValheimHeraldryMod\Charges\` (created the first time
the mod runs), or type `heraldry_reload` in the console if the game is already running.
Then: `Or, a [wolf] passant sable armed and langued gules`.

The simplest charge needs no manifest at all: a PNG in the Charges folder (or in a pack folder) is a charge named after
its file (`black_dog.png` → `[black dog]`), with `black_dog.mask.png` as its optional mask.

## manifest.json

A manifest is for several names or attitudes per charge, or for atlases.

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

- Several drawings can share a name with different attitudes: `[wolf] rampant` picks the rampant one. A blazon asking
  for an attitude the pack doesn't have uses the closest drawing and says so.
- Limits: 1024 px per charge or cell, 4096 px per atlas. A bad image skips that one charge (see the log).
- Mask layers can go outside the colour image (a tongue in an open mouth); they're part of the outline.

## On a server

Every charge travels on its own, identified by a fingerprint of its picture, so two players' different "wolf"
pictures never get mixed up. Arms carry the fingerprints of the pictures they use.

- A server's own charges go in its `Documents\ValheimHeraldryMod\ServerCharges` folder (or the folder set in
  `ServerChargeFolder`).
- Players' own charges are passed on through the server when someone uses them (`ShareCharges`, on by default).
- Players are told when they meet a picture they don't have; `heraldry_pending` lists them, `heraldry_get <name>` or
  `heraldry_get all` downloads them into `Documents\ValheimHeraldryMod\Downloaded`. `AutoGetServerCharges` fetches the
  server's own charges without asking.
- `heraldry_block` hides a picture for you; admins can `heraldry_blacklist` it for the whole server.
