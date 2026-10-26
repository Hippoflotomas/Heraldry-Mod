using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHeraldryMod.Banners;
using ValheimHeraldryMod.Rendering;
using ValheimHeraldryMod.Shields;

namespace ValheimHeraldryMod
{
    /// <summary>
    ///     <para>
    ///         The public API for other mods (2026-10-25; Heraldry Mod 1.4.0). EXPERIMENTAL: names and behaviour may
    ///         still change until it's marked stable; every change raises <see cref="ApiVersion"/>. Meant to be called by reflection, so a mod needs no reference
    ///         to this dll and works with or without Heraldry Mod installed (a soft dependency). docs/HeraldryBridge.cs
    ///         is a ready-made copy-in file that does the reflection. Reference and examples: docs/API.md.
    ///     </para>
    ///     <para>
    ///         Rules: plain types only (string, int, bool, float, GameObject, ItemDrop.ItemData,
    ///         Texture2D, Func, Action); every method has its own name (no overloads, so <c>GetMethod(name)</c> is never
    ///         ambiguous); nothing throws (failures return false, null or an empty value, with the reason in the log);
    ///         once the API is marked stable, changes only ever add.
    ///     </para>
    ///     <para>
    ///         Blazons go in and come out as they're stored: the text players write, sometimes followed by a hidden tail
    ///         naming the exact picture of each [bracketed] charge. Pass it back as it is to copy arms exactly; use
    ///         <see cref="DisplayBlazon"/> to show it.
    ///     </para>
    /// </summary>
    public static class HeraldryAPI
    {
        /// <summary>
        ///     Goes up by one whenever the API changes. 1 = the first, experimental version (Heraldry Mod 1.4.0); 2 = adds
        ///     <see cref="OpenTableEditor"/>, <see cref="GetSlotBlazon"/> and <see cref="IsEditingLocked"/>, and the editor
        ///     calls ignore the server's editing lock (Heraldry Mod 1.5.0).
        /// </summary>
        public const int ApiVersion = 2;

        /// <summary>Heraldry Mod's own version ("1.5.0").</summary>
        public static string ModVersion => ValheimHeraldryMod.PluginVersion;

        // ================================================================== 1. faces on other mods' models

        /// <summary>
        ///     Makes a part of your prefab paintable, as if it were named <c>ValheimHeraldryMod_Face_n</c>: the object at
        ///     <paramref name="partPath"/> (from the prefab's root, e.g. "Model/Front"; "" = the root) is painted with
        ///     pattern <paramref name="pattern"/> (1–4), laid out flat. Shields are painted at the herald's table, build
        ///     pieces in place. Call it once, early (your plugin's Awake is fine); registering the same part again
        ///     replaces it. The part needs a MeshRenderer and MeshFilter (not skinned).
        /// </summary>
        public static bool RegisterFace(string prefabName, string partPath, int pattern) =>
            RegisterFaceAdvanced(prefabName, partPath, pattern, -1, null, false);

        /// <summary>
        ///     <see cref="RegisterFace"/> with every option. <paramref name="materialSlot"/>: -1 = the whole object, or
        ///     the material slot (submesh) that is the face. <paramref name="centrePath"/>: an object (an empty) marking the
        ///     centre of the arms, or null. <paramref name="useUVs"/>: lay the arms out in the part's own UV0 (0–1, +V up)
        ///     instead of flat. Like the <c>ValheimHeraldryMod_UseUVs</c> tag it's all or nothing: true on any part of a
        ///     prefab switches every part of that prefab to its UVs.
        /// </summary>
        public static bool RegisterFaceAdvanced(string prefabName, string partPath, int pattern, int materialSlot, string centrePath, bool useUVs)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prefabName)) return Fail("RegisterFace: no prefab name.");
                if (pattern < 1 || pattern > TaggedFace.MaxPatterns) return Fail($"RegisterFace '{prefabName}': pattern {pattern}; only 1 to {TaggedFace.MaxPatterns} exist.");
                if (materialSlot < -1) return Fail($"RegisterFace '{prefabName}': material slot {materialSlot}; use -1 for the whole object.");
                Api.ApiRegistry.Add(prefabName.Trim(), new Api.ApiRegistry.Face
                {
                    Path = (partPath ?? "").Trim().Trim('/'), Slot = materialSlot, Number = pattern,
                    CentrePath = string.IsNullOrWhiteSpace(centrePath) ? null : centrePath.Trim().Trim('/'),
                }, useUVs);
                Jotunn.Logger.LogInfo($"[Heraldry API] Registered a face on '{prefabName}': '{partPath}'" +
                                      (materialSlot >= 0 ? $" slot {materialSlot}" : "") + $", pattern {pattern}" + (useUVs ? ", own UVs" : "") + ".");
                return true;
            }
            catch (Exception ex) { return Fail("RegisterFace: " + ex.Message); }
        }

        // ================================================================== 2. reading and writing arms

        /// <summary>The pattern numbers a placed piece can carry (a heraldic banner: just 1); empty if it can't carry arms.</summary>
        public static int[] GetPiecePatterns(GameObject piece)
        {
            try
            {
                if (piece == null) return new int[0];
                if (piece.GetComponentInParent<HeraldicBanner>() != null) return new[] { 1 };
                var hp = piece.GetComponentInParent<HeraldicPiece>();
                if (hp != null && hp.Numbers.Length > 0) return hp.Numbers;
                // Not painted here (a dedicated server builds no faces): the tags and registrations say which patterns exist
                return TaggedModel.Scan(Root(piece).gameObject, out _).Select(h => h.Value.Value).Distinct().OrderBy(n => n).ToArray();
            }
            catch (Exception ex) { Fail("GetPiecePatterns: " + ex.Message); return new int[0]; }
        }

        /// <summary>The arms on a placed piece (a heraldic banner, or a piece with faces): "" if none or not a piece that takes arms.</summary>
        public static string GetPieceBlazon(GameObject piece, int pattern)
        {
            try
            {
                var zdo = PieceZdo(piece, pattern, out string error);
                return zdo == null ? "" : zdo.GetString(Patterns.Key(HeraldicBanner.BlazonZdoKey, pattern), "");
            }
            catch (Exception ex) { Fail("GetPieceBlazon: " + ex.Message); return ""; }
        }

        /// <summary>
        ///     Writes arms on a placed piece ("" takes them off). No paint is paid and no ward is checked: your mod decides
        ///     who may do this. The server's limits still apply (too long or too complex = refused). Every client sees the
        ///     change within about a second. Works on the server too, for pieces it has loaded.
        /// </summary>
        public static bool SetPieceBlazon(GameObject piece, string blazon, int pattern)
        {
            try
            {
                var nview = piece != null ? piece.GetComponentInParent<ZNetView>() : null;
                var zdo = PieceZdo(piece, pattern, out string error);
                if (zdo == null) return Fail("SetPieceBlazon: " + error);
                string text = Clean(blazon);
                string problem = BlazonLimits.Problem(text);
                if (problem != null) return Fail($"SetPieceBlazon: refused, {problem}.");
                if (!nview.IsOwner()) nview.ClaimOwnership();
                zdo.Set(Patterns.Key(HeraldicBanner.BlazonZdoKey, pattern), text);
                return true;
            }
            catch (Exception ex) { return Fail("SetPieceBlazon: " + ex.Message); }
        }

        /// <summary>The pattern numbers an item can carry (a shield's faces, or 1 for a plain shield or a tabard); empty if it can't carry arms.</summary>
        public static int[] GetItemPatterns(ItemDrop.ItemData item)
        {
            try { return Paintable(item, out _) ? (Tabards.TabardItem.IsTabard(item) ? new[] { 1 } : ShieldPatches.PatternsOf(item)) : new int[0]; }
            catch (Exception ex) { Fail("GetItemPatterns: " + ex.Message); return new int[0]; }
        }

        /// <summary>The arms painted on an item (a shield or a tabard): "" if none.</summary>
        public static string GetItemBlazon(ItemDrop.ItemData item, int pattern)
        {
            try { return HeraldicShield.BlazonOf(item, pattern); }
            catch (Exception ex) { Fail("GetItemBlazon: " + ex.Message); return ""; }
        }

        /// <summary>
        ///     Paints an item (a shield or a tabard; "" takes the arms off). No paint is paid. The server's limits still
        ///     apply. Items live with whoever holds them, so call this on that player's own client (the local player's
        ///     inventory, or an item they're about to receive); what's in their hand and on their back updates at once.
        /// </summary>
        public static bool SetItemBlazon(ItemDrop.ItemData item, string blazon, int pattern)
        {
            try
            {
                if (!Paintable(item, out string error)) return Fail("SetItemBlazon: " + error);
                if (!GetItemPatterns(item).Contains(pattern)) return Fail($"SetItemBlazon: this item has no pattern {pattern}.");
                string text = Clean(blazon);
                string problem = BlazonLimits.Problem(text);
                if (problem != null) return Fail($"SetItemBlazon: refused, {problem}.");
                string key = Patterns.Key(Station.HeraldTable.ItemBlazonKey, pattern);
                if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
                if (text.Length == 0) item.m_customData.Remove(key);
                else item.m_customData[key] = text;

                var player = Player.m_localPlayer;
                if (player != null && player.GetInventory().ContainsItem(item))
                {
                    ShieldPatches.RefreshEquipment(player);
                    if (Tabards.TabardItem.IsTabard(item)) player.GetComponent<Tabards.HeraldicTabardWearer>()?.Publish();
                }
                return true;
            }
            catch (Exception ex) { return Fail("SetItemBlazon: " + ex.Message); }
        }

        /// <summary>
        ///     The arms on something holding items, by slot: what a player shows, or what an armour stand or item stand
        ///     holds. Read from what Heraldry Mod already shares with every client, so it works for any player or stand
        ///     that's loaded, not only your own. "" if nothing painted is there, or if the slot doesn't exist (logged).
        ///     <list type="bullet">
        ///         <item><b>Players:</b> "HandLeft" (shield in hand), "BackLeft" (shield on the back) or "Tabard"; or 0, 1, 2
        ///         in that order. Shows what's displayed: a sheathed shield is on the back, not in the hand. It can lag an
        ///         equip change by one network update. The tabard has only pattern 1.</item>
        ///         <item><b>Armour stands:</b> the stand's slot by Valheim's name ("HandLeft", "BackLeft"…) or by its index
        ///         in the stand's own slot list ("0", "1"…). Only shields are painted on stands.</item>
        ///         <item><b>Item stands:</b> one slot: "" (or "0").</item>
        ///     </list>
        ///     What item is in a slot is Valheim's business: ItemStand.m_currentItemName and each armour stand slot's
        ///     m_currentItemName already say. Names ignore case.
        /// </summary>
        public static string GetSlotBlazon(GameObject target, string slot, int pattern)
        {
            try
            {
                if (target == null) return "";
                string s = (slot ?? "").Trim();
                var player = target.GetComponentInParent<Player>();
                if (player != null)
                {
                    var zdo = Zdo(player);
                    if (zdo == null) return "";
                    switch (PlayerSlot(s))
                    {
                        case 0: return zdo.GetString(Patterns.Key(ShieldPatches.ZdoLeft, pattern), "");
                        case 1: return zdo.GetString(Patterns.Key(ShieldPatches.ZdoBack, pattern), "");
                        case 2:
                            if (pattern != 1) return "";
                            string worn = zdo.GetString(Tabards.HeraldicTabardWearer.ZdoKey, ""); // "1|blazon" while worn
                            int bar = worn.IndexOf('|');
                            return bar >= 0 ? worn.Substring(bar + 1) : "";
                        default:
                            Fail($"GetSlotBlazon: players have no slot '{slot}' (HandLeft, BackLeft, Tabard, or 0–2).");
                            return "";
                    }
                }
                var armour = target.GetComponentInParent<ArmorStand>();
                if (armour != null)
                {
                    int index = -1;
                    if (int.TryParse(s, out int n)) index = n;
                    else
                        for (int i = 0; i < armour.m_slots.Count; i++)
                            if (string.Equals(armour.m_slots[i].m_slot.ToString(), s, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                    if (index < 0 || index >= armour.m_slots.Count)
                    {
                        Fail($"GetSlotBlazon: this armour stand has no slot '{slot}' (it has {string.Join(", ", armour.m_slots.Select(x => x.m_slot.ToString()))}).");
                        return "";
                    }
                    return Zdo(armour)?.GetString(Patterns.Key(index + "_" + ShieldPatches.ZdoItem, pattern), "") ?? "";
                }
                var stand = target.GetComponentInParent<ItemStand>();
                if (stand != null)
                {
                    if (s.Length > 0 && s != "0")
                    {
                        Fail($"GetSlotBlazon: an item stand has one slot (\"\" or \"0\"), not '{slot}'.");
                        return "";
                    }
                    return Zdo(stand)?.GetString(Patterns.Key(ShieldPatches.ZdoItem, pattern), "") ?? "";
                }
                Fail("GetSlotBlazon: that object isn't a player, an armour stand or an item stand.");
                return "";
            }
            catch (Exception ex) { Fail("GetSlotBlazon: " + ex.Message); return ""; }
        }

        // ================================================================== 3. blazon text

        /// <summary>
        ///     Null if the arms are fine to apply here, else the first problem in plain words ("'gulez': not a tincture",
        ///     "too complex for this server: 31 things to draw, at most 25"). Words the editor would only warn about are
        ///     fine.
        /// </summary>
        public static string CheckBlazon(string blazon)
        {
            try
            {
                string text = Clean(blazon);
                if (text.Length == 0) return null;
                var parsed = Heraldry.BlazonParser.Parse(text);
                return BlazonLimits.Problem(text, parsed) ?? Heraldry.BlazonParser.FirstProblem(parsed);
            }
            catch (Exception ex) { return "couldn't be read: " + ex.Message; }
        }

        /// <summary>The arms as players see them: the hidden picture tail removed, and the runes shown as letters.</summary>
        public static string DisplayBlazon(string blazon)
        {
            try { return TextArt.ForDisplay(Heraldry.BlazonText.Visible(blazon ?? "")); }
            catch { return blazon ?? ""; }
        }

        /// <summary>
        ///     What the arms really are: a secret code (they exist; we don't list them) becomes the blazon it stands for,
        ///     when the server allows codes. Anything else comes back as it is.
        /// </summary>
        public static string ExpandBlazon(string blazon)
        {
            try
            {
                string visible = Heraldry.BlazonText.Visible(blazon ?? "");
                return HeraldryConfig.AllowSecretBlazons && Heraldry.SecretCodes.TryExpand(visible, out string expanded, out _) ? expanded : blazon ?? "";
            }
            catch { return blazon ?? ""; }
        }

        // ================================================================== 4. guild arms

        /// <summary>
        ///     Suggests arms in the editor: whenever it opens, <paramref name="armsFor"/> is asked for the local player's
        ///     arms (a blazon; null or "" = none), and they're offered at the top of the saved blazons under
        ///     <paramref name="label"/> ("Guild arms"). The player can load them, change them and apply them as they like.
        ///     Registering the same label again replaces it.
        /// </summary>
        public static bool RegisterArmsProvider(string label, Func<Player, string> armsFor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(label) || armsFor == null) return Fail("RegisterArmsProvider: needs a label and a function.");
                label = label.Trim();
                Api.ApiRegistry.Providers.RemoveAll(p => p.Label == label);
                Api.ApiRegistry.Providers.Add(new Api.ApiRegistry.Provider { Label = label, Arms = armsFor });
                return true;
            }
            catch (Exception ex) { return Fail("RegisterArmsProvider: " + ex.Message); }
        }

        // ================================================================== 5. the arms as a picture

        private static readonly Dictionary<Texture2D, KeyValuePair<string, int>> Rendered = new Dictionary<Texture2D, KeyValuePair<string, int>>();

        /// <summary>
        ///     The arms drawn as a <paramref name="width"/> × <paramref name="height"/> texture (16–2048 each), filling the
        ///     whole rectangle; tall ones lay out like banners, others like shields. The texture is shared: the same arms at
        ///     the same size give the same texture. Don't destroy or change it; call <see cref="ReleaseBlazonTexture"/>
        ///     when you're done. It's drawn in the background: it shows the field's colour at first and the full arms a
        ///     frame or two later, in the same texture, so a material using it updates by itself. Null on a dedicated
        ///     server (it draws nothing) or on failure.
        /// </summary>
        public static Texture2D RenderBlazon(string blazon, int width, int height)
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
                int w = Mathf.Clamp(width, 16, 2048), h = Mathf.Clamp(height, 16, 2048);
                float aspect = (float)w / h;
                var shape = new BlazonTextureCache.Shape
                {
                    Id = "Heraldry_API",
                    Arrangement = aspect < 0.75f ? Heraldry.FieldRenderer.Arrangement.Spread : Heraldry.FieldRenderer.Arrangement.Compact,
                };
                var tex = BlazonTextureCache.Acquire(BlazonTextureCache.Normalise(Clean(blazon)), w, h, shape, out string key);
                Rendered[tex] = new KeyValuePair<string, int>(key, Rendered.TryGetValue(tex, out var had) ? had.Value + 1 : 1);
                return tex;
            }
            catch (Exception ex) { Fail("RenderBlazon: " + ex.Message); return null; }
        }

        /// <summary>Lets go of a texture from <see cref="RenderBlazon"/> (once per RenderBlazon call). Don't use it afterwards.</summary>
        public static void ReleaseBlazonTexture(Texture2D texture)
        {
            try
            {
                if (texture == null || !Rendered.TryGetValue(texture, out var kv)) return;
                BlazonTextureCache.Release(kv.Key);
                if (kv.Value <= 1) Rendered.Remove(texture);
                else Rendered[texture] = new KeyValuePair<string, int>(kv.Key, kv.Value - 1);
            }
            catch (Exception ex) { Fail("ReleaseBlazonTexture: " + ex.Message); }
        }

        // ================================================================== 6. the editor

        /// <summary>
        ///     True when the server has locked editing for the local player (its LockEditing setting; admins are never
        ///     locked). Banners, pieces and the herald's table then open the blazon window greyed out, to look at only.
        ///     The editor calls below ignore the lock: they're how your mod lets a player edit anyway.
        /// </summary>
        public static bool IsEditingLocked()
        {
            try { return HeraldryConfig.EditingLocked; }
            catch { return false; }
        }

        /// <summary>
        ///     Opens the herald's table window for the local player without a table: choose one of their shields or
        ///     tabards (equipped first) and paint it. Paint costs follow the server's PaintCosts, like the table. Ignores
        ///     the editing lock. False if it can't be opened (no player, no game UI, the editor already open).
        /// </summary>
        public static bool OpenTableEditor()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null || UI.BlazonEditor.IsOpen) return false;
                UI.BlazonEditor.Open(new Station.TableTarget(null, player));
                return UI.BlazonEditor.IsOpen;
            }
            catch (Exception ex) { return Fail("OpenTableEditor: " + ex.Message); }
        }

        /// <summary>
        ///     Opens the blazon editor on a placed piece that takes arms, as if the player had pressed E: ward access and
        ///     paint costs as normal, but the editing lock is ignored. <paramref name="pattern"/>: which pattern to start
        ///     on (0 = the first). False if it can't be opened (not a piece that takes arms, no ward access, no game UI).
        /// </summary>
        public static bool OpenEditor(GameObject piece, int pattern)
        {
            try
            {
                if (piece == null || UI.BlazonEditor.IsOpen) return false;
                var banner = piece.GetComponentInParent<HeraldicBanner>();
                if (banner != null)
                {
                    if (!PrivateArea.CheckAccess(banner.transform.position, 0f, true, false)) return false;
                    UI.BlazonEditor.Open(banner);
                    return UI.BlazonEditor.IsOpen;
                }
                var hp = piece.GetComponentInParent<HeraldicPiece>();
                if (hp == null) return Fail("OpenEditor: that object can't carry arms (no faces tagged or registered on it).");
                return hp.OpenEditor(pattern, viaApi: true) && UI.BlazonEditor.IsOpen;
            }
            catch (Exception ex) { return Fail("OpenEditor: " + ex.Message); }
        }

        /// <summary>
        ///     Opens the blazon editor on arms that aren't on anything (a guild's arms, a choice before crafting): it starts
        ///     with <paramref name="blazon"/>, previews them at <paramref name="aspect"/> (width over height; 0 = a shield's
        ///     shape), and when the player presses Apply, <paramref name="onApply"/> gets the arms as they're stored
        ///     (checked against the server's limits). Cancel calls nothing. No paint is paid; nothing is painted. Ignores
        ///     the editing lock.
        /// </summary>
        public static bool OpenEditorForText(string title, string blazon, float aspect, Action<string> onApply)
        {
            try
            {
                if (onApply == null) return Fail("OpenEditorForText: needs an onApply function.");
                if (UI.BlazonEditor.IsOpen) return false;
                UI.BlazonEditor.Open(new UI.TextTarget(title, Clean(blazon), aspect, onApply));
                return UI.BlazonEditor.IsOpen;
            }
            catch (Exception ex) { return Fail("OpenEditorForText: " + ex.Message); }
        }

        // ================================================================== helpers

        private static string Clean(string blazon)
        {
            string text = (blazon ?? "").Trim();
            return text.Length > HeraldicBanner.MaxBlazonLength ? text.Substring(0, HeraldicBanner.MaxBlazonLength) : text;
        }

        private static ZDO Zdo(Component c)
        {
            var nview = c.GetComponent<ZNetView>() ?? c.GetComponentInParent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        /// <summary>A player's slot: 0 HandLeft, 1 BackLeft, 2 Tabard; -1 if it isn't one.</summary>
        private static int PlayerSlot(string s)
        {
            if (int.TryParse(s, out int n)) return n >= 0 && n <= 2 ? n : -1;
            if (string.Equals(s, "HandLeft", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(s, "BackLeft", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(s, "Tabard", StringComparison.OrdinalIgnoreCase)) return 2;
            return -1;
        }

        private static Transform Root(GameObject go)
        {
            var nview = go.GetComponentInParent<ZNetView>();
            return nview != null ? nview.transform : go.transform;
        }

        /// <summary>The ZDO holding a piece's arms, if it's a piece that takes arms with that pattern.</summary>
        private static ZDO PieceZdo(GameObject piece, int pattern, out string error)
        {
            error = null;
            if (piece == null) { error = "no object."; return null; }
            var nview = piece.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid() || nview.GetComponent<Piece>() == null) { error = "the object isn't a placed build piece."; return null; }
            if (!GetPiecePatterns(piece).Contains(pattern)) { error = $"'{Utils.GetPrefabName(nview.gameObject)}' takes no arms in pattern {pattern}."; return null; }
            return nview.GetZDO();
        }

        private static bool Paintable(ItemDrop.ItemData item, out string error)
        {
            error = null;
            if (item?.m_shared == null) { error = "no item."; return false; }
            if (Tabards.TabardItem.IsTabard(item)) return true;
            if (HeraldicShield.IsShield(item) && ShieldPatches.Paintable(item.m_dropPrefab)) return true;
            error = $"'{item.m_dropPrefab?.name ?? item.m_shared.m_name}' can't carry arms (only shields and tabards can).";
            return false;
        }

        private static bool Fail(string message)
        {
            Jotunn.Logger.LogWarning("[Heraldry API] " + message);
            return false;
        }
    }
}
