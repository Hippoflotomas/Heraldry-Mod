// HeraldryBridge.cs: copy this file into your own mod to use Valheim Heraldry Mod's API without referencing its dll.
// If Heraldry Mod isn't installed, every call quietly does nothing (false, "", null). Change the namespace to yours.
//
// Recommended on your plugin class, so Heraldry Mod loads first when it's there (it stays optional):
//     [BepInDependency("com.hippotech.valheimheraldrymod", BepInDependency.DependencyFlags.SoftDependency)]
//
// Guide: https://github.com/Hippoflotomas/Heraldry-Mod/blob/master/docs/ModderGuide.md
// EXPERIMENTAL: the API may still change in later Heraldry Mod versions (check ApiVersion); update this file with it.
// Bridge version 1 (API version 1). You're free to copy this file into your own mod, change it and ship it.

using System;
using System.Reflection;
using UnityEngine;

namespace YourMod
{
    internal static class HeraldryBridge
    {
        private const string ApiTypeName = "ValheimHeraldryMod.HeraldryAPI";
        private const string AssemblyName = "ValheimHeraldryMod";

        private static bool bound, givenUp;
        private static int apiVersion;
        private static MethodInfo registerFace, registerFaceAdvanced, getPiecePatterns, getPieceBlazon, setPieceBlazon,
            getItemPatterns, getItemBlazon, setItemBlazon, checkBlazon, displayBlazon, expandBlazon, registerArmsProvider,
            renderBlazon, releaseBlazonTexture, openEditor, openEditorForText;

        /// <summary>True if Heraldry Mod is installed and its API is there.</summary>
        public static bool Available => Bind();

        /// <summary>Heraldry Mod's API version (0 = not installed). It goes up whenever the API changes.</summary>
        public static int ApiVersion => Bind() ? apiVersion : 0;

        // ------------------------------------------------------------ faces on your models

        /// <summary>Makes a part of your prefab paintable with pattern 1–4 (partPath from the prefab's root, e.g. "Model/Front").</summary>
        public static bool RegisterFace(string prefabName, string partPath, int pattern = 1) =>
            Call(registerFace, false, prefabName, partPath, pattern);

        /// <summary>RegisterFace with every option: materialSlot -1 = whole object; centrePath an empty or null; useUVs = lay out in UV0 (all or nothing per prefab).</summary>
        public static bool RegisterFaceAdvanced(string prefabName, string partPath, int pattern, int materialSlot, string centrePath, bool useUVs) =>
            Call(registerFaceAdvanced, false, prefabName, partPath, pattern, materialSlot, centrePath, useUVs);

        // ------------------------------------------------------------ reading and writing arms

        public static int[] GetPiecePatterns(GameObject piece) => Call(getPiecePatterns, new int[0], piece);
        public static string GetPieceBlazon(GameObject piece, int pattern = 1) => Call(getPieceBlazon, "", piece, pattern);
        /// <summary>No paint, no ward check (your mod decides); the server's limits still apply.</summary>
        public static bool SetPieceBlazon(GameObject piece, string blazon, int pattern = 1) => Call(setPieceBlazon, false, piece, blazon, pattern);

        public static int[] GetItemPatterns(ItemDrop.ItemData item) => Call(getItemPatterns, new int[0], item);
        public static string GetItemBlazon(ItemDrop.ItemData item, int pattern = 1) => Call(getItemBlazon, "", item, pattern);
        /// <summary>Shields and tabards. Call on the client of the player holding the item. No paint; limits apply.</summary>
        public static bool SetItemBlazon(ItemDrop.ItemData item, string blazon, int pattern = 1) => Call(setItemBlazon, false, item, blazon, pattern);

        // ------------------------------------------------------------ blazon text

        /// <summary>Null if fine, else the first problem in plain words. Without Heraldry Mod: null.</summary>
        public static string CheckBlazon(string blazon) => Call<string>(checkBlazon, null, blazon);
        /// <summary>The arms as players see them (hidden picture tail removed).</summary>
        public static string DisplayBlazon(string blazon) => Call(displayBlazon, blazon ?? "", blazon);
        /// <summary>A secret code becomes the blazon it stands for; anything else comes back as it is.</summary>
        public static string ExpandBlazon(string blazon) => Call(expandBlazon, blazon ?? "", blazon);

        // ------------------------------------------------------------ guild arms

        /// <summary>Offers arms at the top of the editor's saved list, under label; armsFor gets the local player and returns a blazon or null.</summary>
        public static bool RegisterArmsProvider(string label, Func<Player, string> armsFor) => Call(registerArmsProvider, false, label, armsFor);

        // ------------------------------------------------------------ the arms as a picture

        /// <summary>A shared texture of the arms (filled in a frame or two later). Don't destroy it: release it.</summary>
        public static Texture2D RenderBlazon(string blazon, int width, int height) => Call<Texture2D>(renderBlazon, null, blazon, width, height);
        public static void ReleaseBlazonTexture(Texture2D texture) => Call<object>(releaseBlazonTexture, null, texture);

        // ------------------------------------------------------------ the editor

        /// <summary>The editor on a placed piece, as if the player pressed E (pattern 0 = the first).</summary>
        public static bool OpenEditor(GameObject piece, int pattern = 0) => Call(openEditor, false, piece, pattern);
        /// <summary>The editor on arms that aren't on anything; onApply gets the arms when the player presses Apply. aspect 0 = shield shape.</summary>
        public static bool OpenEditorForText(string title, string blazon, float aspect, Action<string> onApply) =>
            Call(openEditorForText, false, title, blazon, aspect, onApply);

        // ------------------------------------------------------------ plumbing

        private static T Call<T>(MethodInfo method, T fallback, params object[] args)
        {
            if (!Bind() || method == null) return fallback;
            try
            {
                object result = method.Invoke(null, args);
                return result is T t ? t : fallback;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HeraldryBridge] {method.Name} failed: {(ex.InnerException ?? ex).Message}");
                return fallback;
            }
        }

        private static bool Bind()
        {
            if (bound) return true;
            if (givenUp) return false;
            Type api = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != AssemblyName) continue;
                api = assembly.GetType(ApiTypeName, false);
                if (api != null) break;
            }
            if (api == null)
            {
                // Not loaded (yet). Once the game is running every mod is loaded, so then it isn't installed.
                if (ZNetScene.instance != null) givenUp = true;
                return false;
            }
            return Bind(api);
        }

        /// <summary>Looks every method up once, by name and exact parameter types (a method missing from an older version stays null).</summary>
        internal static bool Bind(Type api)
        {
            MethodInfo M(string name, params Type[] types) => api.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            var go = typeof(GameObject);
            var item = typeof(ItemDrop.ItemData);
            var s = typeof(string);
            var i = typeof(int);
            registerFace = M("RegisterFace", s, s, i);
            registerFaceAdvanced = M("RegisterFaceAdvanced", s, s, i, i, s, typeof(bool));
            getPiecePatterns = M("GetPiecePatterns", go);
            getPieceBlazon = M("GetPieceBlazon", go, i);
            setPieceBlazon = M("SetPieceBlazon", go, s, i);
            getItemPatterns = M("GetItemPatterns", item);
            getItemBlazon = M("GetItemBlazon", item, i);
            setItemBlazon = M("SetItemBlazon", item, s, i);
            checkBlazon = M("CheckBlazon", s);
            displayBlazon = M("DisplayBlazon", s);
            expandBlazon = M("ExpandBlazon", s);
            registerArmsProvider = M("RegisterArmsProvider", s, typeof(Func<Player, string>));
            renderBlazon = M("RenderBlazon", s, i, i);
            releaseBlazonTexture = M("ReleaseBlazonTexture", typeof(Texture2D));
            openEditor = M("OpenEditor", go, i);
            openEditorForText = M("OpenEditorForText", s, s, typeof(float), typeof(Action<string>));
            var version = api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
            apiVersion = version != null ? (int)version.GetValue(null) : 1;
            bound = true;
            return true;
        }
    }
}
