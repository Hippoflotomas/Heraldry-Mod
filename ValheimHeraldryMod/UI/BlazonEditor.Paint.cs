using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using ValheimHeraldryMod.Heraldry;
using ValheimHeraldryMod.Paints;

namespace ValheimHeraldryMod.UI
{
    /// <summary>
    ///     The paint the arms cost (Hippo, 2026-10-14): a small window under the editor, one icon per paint with
    ///     "needed (have)". Gone when paint costs are off (or off for banners). Apply refuses if the player is short.
    /// </summary>
    internal partial class BlazonEditor
    {
        private const float CostW = 640, CostH = 66, CostGap = 8;

        private GameObject costPanel;
        private Text costNote;
        private readonly List<Image> costIcons = new List<Image>();
        private readonly List<Text> costTexts = new List<Text>();
        /// <summary>What applying would cost now; empty = free.</summary>
        private Dictionary<Paint, int> cost = new Dictionary<Paint, int>();

        private void BuildCostPanel()
        {
            var gui = GUIManager.Instance;
            costPanel = gui.CreateWoodpanel(panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, CostW, CostH, false);
            costPanel.name = "PaintCost";
            var rt = costPanel.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -CostGap);

            Label("Paint", 20, 18, 70, 30, 18, gui.ValheimOrange, costPanel.transform);
            for (int i = 0; i < PaintCost.All.Length; i++)
            {
                float x = 90 + i * 92;
                costIcons.Add(Icon(costPanel.transform, x, 15, 36));
                costTexts.Add(Label("", x + 40, 20, 52, 28, 15, Color.white, costPanel.transform));
            }
            costNote = Label("", 90, 20, CostW - 110, 28, 15, Beige, costPanel.transform);
            costPanel.SetActive(false);
        }

        /// <summary>Recomputes the cost for the text in the box (after every change, and on opening).</summary>
        private void RefreshCost()
        {
            if (costPanel == null) return;
            bool on = target != null && target.CostsPaint;
            costPanel.SetActive(on);
            // Keep the editor and the cost window centred on screen together
            panel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, on ? (CostH + CostGap) / 2f : 0f);
            cost = new Dictionary<Paint, int>();
            if (!on) return;

            string text = (input.text ?? "").Trim();
            string note = null;
            if (text.Length == 0) note = "Taking the arms off is free.";
            else if (string.Equals(text, (target.GetText() ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase))
                note = "Same arms: no paint needed.";
            else
            {
                cost = PaintCost.For(parsed, HeraldryConfig.PaintPerTincture, HeraldryConfig.MaxPaintPerPainting);
                if (cost.Count == 0) note = "No tinctures to pay for.";
            }

            costNote.gameObject.SetActive(note != null);
            costNote.text = note ?? "";
            var player = Player.m_localPlayer;
            int slot = 0;
            foreach (var paint in PaintCost.All)
            {
                if (note != null || !cost.TryGetValue(paint, out int need)) continue;
                int have = PaintItems.Have(player, paint);
                bool missingItem = PaintItems.PaidItem(paint) == null;
                costIcons[slot].sprite = PaintItems.IconOf(paint);
                costIcons[slot].enabled = costIcons[slot].sprite != null;
                costTexts[slot].text = missingItem ? $"{need}" : $"{need} <size=12>({have})</size>";
                costTexts[slot].supportRichText = true;
                costTexts[slot].color = missingItem || have >= need ? Color.white : new Color(0.94f, 0.29f, 0.25f);
                slot++;
            }
            for (int i = 0; i < costIcons.Count; i++)
            {
                costIcons[i].gameObject.SetActive(i < slot);
                costTexts[i].gameObject.SetActive(i < slot);
            }
        }

        /// <summary>Why the arms can't be paid for, or null.</summary>
        private string CostProblem()
        {
            if (cost.Count == 0 || PaintItems.CanPay(Player.m_localPlayer, cost)) return null;
            var player = Player.m_localPlayer;
            var short_ = cost.Where(kv => PaintItems.PaidItem(kv.Key) != null && PaintItems.Have(player, kv.Key) < kv.Value)
                             .Select(kv => $"{kv.Value - PaintItems.Have(player, kv.Key)} {PaintItems.NameOf(kv.Key)}");
            return "not enough paint (" + string.Join(", ", short_) + " more)";
        }

        /// <summary>Takes the paint; call only right before applying.</summary>
        private void PayCost()
        {
            if (cost.Count > 0 && Player.m_localPlayer != null) PaintItems.Pay(Player.m_localPlayer, cost);
        }
    }
}
