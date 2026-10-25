using HarmonyLib;
using UnityEngine;

namespace ValheimHeraldryMod.Station
{
    /// <summary>
    ///     Hugin visits the first time a player comes near a herald's table (Hippo, 2026-10-25), the vanilla way: a
    ///     tutorial text registered with the game, shown with Player.ShowTutorial. Once the player has listened to him
    ///     it's marked seen in the character file and kept in the compendium, like Hugin's own texts.
    /// </summary>
    [HarmonyPatch]
    internal static class HuginTutorial
    {
        public const string Name = "ValheimHeraldryMod_HeraldsTable";
        private const string Topic = "The herald's table";
        private const string Text =
            "Displaying your colours, warrior? Good! Let your allies know to prepare a welcome, and your enemies know to " +
            "brace for death! Paint your shield or tabard here, and look in your compendium if the words of heraldry puzzle you.";
        private const float Range = 6f;

        private static bool askedThisSession;

        /// <summary>Our text joins the game's list of Hugin's texts.</summary>
        [HarmonyPatch(typeof(Tutorial), "Awake")]
        [HarmonyPostfix]
        private static void Register(Tutorial __instance)
        {
            if (__instance.m_texts == null || __instance.m_texts.Exists(t => t.m_name == Name)) return;
            __instance.m_texts.Add(new Tutorial.TutorialText
            {
                m_name = Name,
                m_topic = Topic,
                m_label = Topic,
                m_text = Text,
            });
        }

        /// <summary>From each herald's table about once a second: Hugin comes if the player is close and hasn't heard him yet.</summary>
        public static void Check(Vector3 tablePosition)
        {
            if (askedThisSession) return;
            var player = Player.m_localPlayer;
            if (player == null || Tutorial.instance == null) return;
            if (Vector3.Distance(player.transform.position, tablePosition) > Range) return;
            askedThisSession = true; // once per session; until the player has talked to him, he comes back next time
            player.ShowTutorial(Name);
        }
    }
}
