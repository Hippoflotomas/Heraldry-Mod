using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using ValheimHeraldryMod.Banners;
using ValheimHeraldryMod.Heraldry;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.UI
{
    /// <summary>
    ///     The blazon editing window (build order step 4).
    ///     Left: live preview in the banner's own shape. Right: text box with underlines, colour swatches and
    ///     hover tooltips, a list of problems, Apply / Cancel, and the player's saved blazons.
    ///     The text box's own text is invisible; an identical Text on top shows the words, and Unity's text
    ///     layout for that copy tells us where each word sits, so underlines and swatches line up exactly.
    /// </summary>
    internal class BlazonEditor : MonoBehaviour
    {
        private static BlazonEditor instance;

        // ---- layout (canvas units) ----
        private const float PanelW = 1060, PanelH = 720;
        private const float PreviewH = 560, PreviewX = 30, PreviewY = 70;
        private const float RightX = 310, RightW = 720;
        private const int FontSize = 20;
        private const float LineSpacing = 1.7f;   // room for the swatches above each line
        private const float TopPad = 12f;          // room for swatches above the first line

        private static readonly Color Beige = new Color(0.85f, 0.73f, 0.53f, 1f);
        private static readonly Color WrongColour = new Color(0.95f, 0.30f, 0.25f, 1f);
        private static readonly Color PassableColour = new Color(1.00f, 0.80f, 0.25f, 1f);
        private static readonly Color LaterColour = new Color(0.62f, 0.62f, 0.62f, 1f);

        private GameObject panel;
        private InputField input;
        private Text overlay;            // visible copy of the text
        private RectTransform marks;     // underlines and swatches live here
        private Text problems;
        private Text status;
        private Text tooltip;
        private RawImage preview;
        private InputField saveName;
        private RectTransform savedContent;

        private HeraldicBanner banner;
        private float aspect = 0.4f;
        private Blazon parsed;
        private readonly List<KeyValuePair<Rect, string>> wordRects = new List<KeyValuePair<Rect, string>>();

        private Texture2D previewTexture;
        private Task<Rgba[]> previewJob;
        private int previewW, previewH;
        private bool[] previewMask;
        private float renderAt = -1f;     // time to start the next preview render (debounce)
        private int unblockAfterFrame = -1; // release the game's input a frame after closing
        private bool rerenderWanted;

        private Entry pendingDelete;
        private float pendingDeleteUntil;
        private class Entry { public SavedBlazons.Entry Saved; public Text DeleteLabel; }

        private static Texture2D wavy, dashed, dotted;
        private static readonly Dictionary<string, Texture2D> furSwatches = new Dictionary<string, Texture2D>();

        public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

        public static void Open(HeraldicBanner target)
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
            if (instance == null)
            {
                var go = new GameObject("HeraldryBlazonEditor");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<BlazonEditor>();
            }
            instance.Show(target);
        }

        public static void Close() => instance?.Hide();

        // ================================================================== build

        private void Build()
        {
            var gui = GUIManager.Instance;
            MakeLineTextures();

            panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, PanelW, PanelH, true);
            panel.name = "HeraldryBlazonEditor";

            Label("Heraldic banner", 30, 18, 600, 40, 26, gui.ValheimOrange);

            // ---- preview ----
            var frame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(panel.transform, false);
            frame.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            Place(frame.GetComponent<RectTransform>(), PreviewX - 10, PreviewY - 10, 250, PreviewH + 20);
            var pv = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            pv.transform.SetParent(panel.transform, false);
            preview = pv.GetComponent<RawImage>();

            // ---- text box ----
            Label("Blazon", RightX, 70, 300, 26, 18, Beige);
            var field = gui.CreateInputField(panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "e.g.  Azure, a bend or", FontSize, RightW, 250);
            Place(field.GetComponent<RectTransform>(), RightX, 98, RightW, 250);
            input = field.GetComponent<InputField>();
            input.lineType = InputField.LineType.MultiLineSubmit;
            input.characterLimit = HeraldicBanner.MaxBlazonLength;
            input.customCaretColor = true;
            input.caretColor = Beige;
            input.caretWidth = 2;

            var textRt = input.textComponent.rectTransform;
            textRt.offsetMax += new Vector2(0, -TopPad);
            input.textComponent.lineSpacing = LineSpacing;
            input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            input.textComponent.alignment = TextAnchor.UpperLeft;
            var ph = input.placeholder as Text;
            if (ph != null)
            {
                ph.rectTransform.offsetMax += new Vector2(0, -TopPad);
                ph.lineSpacing = LineSpacing;
                ph.alignment = TextAnchor.UpperLeft;
            }

            // Visible copy, exactly over the field's own (now invisible) text
            var ov = new GameObject("Overlay", typeof(RectTransform), typeof(Text));
            ov.transform.SetParent(textRt.parent, false);
            overlay = ov.GetComponent<Text>();
            CopyRect(textRt, overlay.rectTransform);
            overlay.font = input.textComponent.font;
            overlay.fontSize = input.textComponent.fontSize;
            overlay.fontStyle = input.textComponent.fontStyle;
            overlay.lineSpacing = LineSpacing;
            overlay.alignment = TextAnchor.UpperLeft;
            overlay.horizontalOverflow = HorizontalWrapMode.Wrap;
            overlay.verticalOverflow = VerticalWrapMode.Overflow;
            overlay.supportRichText = false;
            overlay.raycastTarget = false;
            overlay.color = Color.white;
            var c = input.textComponent.color; c.a = 0f; input.textComponent.color = c;

            var mk = new GameObject("Marks", typeof(RectTransform));
            mk.transform.SetParent(textRt.parent, false);
            marks = mk.GetComponent<RectTransform>();
            CopyRect(textRt, marks);

            input.onValueChanged.AddListener(_ => OnTextChanged());

            // ---- problems ----
            problems = Label("", RightX, 356, RightW, 120, 15, Color.white);
            problems.supportRichText = true;
            problems.alignment = TextAnchor.UpperLeft;
            problems.verticalOverflow = VerticalWrapMode.Truncate;

            // ---- buttons ----
            AddButton("Apply", RightX, 480, 150, 40, Apply);
            AddButton("Cancel", RightX + 160, 480, 150, 40, Hide);
            status = Label("", RightX + 330, 488, RightW - 330, 28, 15, Beige);

            // ---- saved blazons ----
            Label("Saved blazons", RightX, 532, 300, 26, 18, Beige);
            var nameGo = gui.CreateInputField(panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "name", 16, 380, 34);
            Place(nameGo.GetComponent<RectTransform>(), RightX + 170, 528, 380, 34);
            saveName = nameGo.GetComponent<InputField>();
            saveName.characterLimit = 40;
            AddButton("Save as", RightX + 560, 526, 160, 38, SaveCurrent);

            var scroll = gui.CreateScrollView(panel.transform, false, true, 8f, 4f, ColorBlock.defaultColorBlock,
                new Color(0, 0, 0, 0.3f), RightW, 130);
            Place(scroll.GetComponent<RectTransform>(), RightX, 572, RightW, 130);
            savedContent = scroll.transform.Find("Scroll View/Viewport/Content") as RectTransform;
            var vlg = savedContent.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 3; vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset(6, 6, 4, 4);

            // ---- tooltip (last, so it draws on top) ----
            var tipGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
            tipGo.transform.SetParent(panel.transform, false);
            tipGo.GetComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 0.95f);
            tipGo.GetComponent<Image>().raycastTarget = false;
            var tipText = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
            tipText.transform.SetParent(tipGo.transform, false);
            tooltip = tipText.GetComponent<Text>();
            gui.ApplyTextStyle(tooltip, gui.AveriaSerifBold, Color.white, 15, false);
            tooltip.raycastTarget = false;
            tooltip.supportRichText = true;
            var fitter = tipText.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tipGo.SetActive(false);
        }

        // ================================================================== show / hide

        private void Show(HeraldicBanner target)
        {
            if (panel == null) Build();
            banner = target;
            aspect = Mathf.Clamp(target.m_widthOverHeight, 0.2f, 2f);

            // Preview sized to the banner's shape, with its real outline
            previewH = 400;
            previewW = Mathf.Max(16, Mathf.RoundToInt(previewH * aspect));
            float shownH = PreviewH, shownW = shownH * aspect;
            if (shownW > 230) { shownW = 230; shownH = shownW / aspect; }
            Place(preview.rectTransform, PreviewX + (230 - shownW) / 2, PreviewY, shownW, shownH);
            if (previewTexture == null || previewTexture.width != previewW || previewTexture.height != previewH)
            {
                if (previewTexture != null) Destroy(previewTexture);
                previewTexture = new Texture2D(previewW, previewH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            }
            previewMask = OutlineMask.FromTriangles(target.m_outlineUV, target.m_outlineTriangles, previewW, previewH, target.m_hiddenTop);
            preview.texture = previewTexture;

            input.text = target.GetText();
            saveName.text = "";
            RefreshSaved();
            status.text = "";
            panel.SetActive(true);
            if (unblockAfterFrame >= 0) unblockAfterFrame = -1; // reopened before the release: keep the block
            else GUIManager.BlockInput(true);
            input.ActivateInputField();
            input.MoveTextEnd(false);
            OnTextChanged();
            renderAt = 0f; // render the preview straight away
        }

        private void Hide()
        {
            if (panel == null || !panel.activeSelf) return;
            panel.SetActive(false);
            tooltip.transform.parent.gameObject.SetActive(false);
            banner = null;
            // Release next frame, so the Enter or Escape that closed us can't also open chat or the menu
            unblockAfterFrame = Time.frameCount;
        }

        private void Apply()
        {
            if (banner != null) banner.SetText(input.text.Trim());
            Hide();
        }

        // ================================================================== per frame

        private void Update()
        {
            if (unblockAfterFrame >= 0 && Time.frameCount > unblockAfterFrame)
            {
                unblockAfterFrame = -1;
                GUIManager.BlockInput(false);
            }
            if (panel == null || !panel.activeSelf) return;

            if (banner == null) { Hide(); return; } // banner destroyed while editing
            if (ZInput.GetKeyDown(KeyCode.Escape, false)) { Hide(); return; }
            if ((ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false)) && !saveName.isFocused)
            {
                Apply();
                return;
            }

            if (renderAt >= 0f && Time.unscaledTime >= renderAt && previewJob == null) StartPreview();
            if (previewJob != null && previewJob.IsCompleted) FinishPreview();

            UpdateTooltip();

            if (pendingDelete != null && Time.unscaledTime > pendingDeleteUntil)
            {
                pendingDelete.DeleteLabel.text = "Delete";
                pendingDelete = null;
            }
        }

        private void OnTextChanged()
        {
            string text = input.text ?? "";
            parsed = BlazonParser.Parse(text);
            LayoutMarks(text);
            ShowProblems();
            renderAt = Time.unscaledTime + 0.25f; // re-render the preview once typing pauses
        }

        // ================================================================== underlines, swatches, tooltips

        private void LayoutMarks(string text)
        {
            overlay.text = text;
            foreach (Transform child in marks) Destroy(child.gameObject);
            wordRects.Clear();
            if (text.Length == 0) return;

            Canvas.ForceUpdateCanvases();
            var gen = overlay.cachedTextGenerator;
            var chars = gen.characters;
            var lines = gen.lines;
            float ppu = overlay.pixelsPerUnit;
            if (chars.Count == 0 || lines.Count == 0) return;

            foreach (var w in parsed.Words)
            {
                if (w.Length == 0 || w.Start + w.Length > chars.Count) continue;
                int first = w.Start, last = w.Start + w.Length - 1;
                int line = LineOf(lines, first);
                if (LineOf(lines, last) != line) continue; // a word wrapped mid-way: skip its marks
                float x0 = chars[first].cursorPos.x / ppu;
                float x1 = (chars[last].cursorPos.x + chars[last].charWidth) / ppu;
                float top = lines[line].topY / ppu;
                float h = lines[line].height / ppu;
                float baseY = top - FontSize * 1.08f;

                Texture2D tex = null;
                Color col = Color.white;
                switch (w.Status)
                {
                    case WordStatus.Wrong: tex = wavy; col = WrongColour; break;
                    case WordStatus.Passable: tex = dashed; col = PassableColour; break;
                    case WordStatus.NotDrawnYet:
                    case WordStatus.Unsupported: tex = dotted; col = LaterColour; break;
                }
                if (tex != null && x1 > x0)
                    Underline(x0, baseY, x1 - x0, tex, col);

                if (w.Tincture != null)
                    Swatch((x0 + x1) * 0.5f, top + 1f, w.Tincture);

                if (w.Message != null)
                    wordRects.Add(new KeyValuePair<Rect, string>(new Rect(x0, top - h, x1 - x0, h), Colourise(w)));
            }
        }

        private static int LineOf(IList<UILineInfo> lines, int charIndex)
        {
            int l = 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].startCharIdx <= charIndex) l = i;
            return l;
        }

        private void Underline(float x, float y, float width, Texture2D tex, Color col)
        {
            var go = new GameObject("Underline", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(marks, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.color = col;
            img.raycastTarget = false;
            float h = tex.height;
            img.uvRect = new Rect(0, 0, width / tex.width, 1); // repeat along the word
            AtLocal(img.rectTransform, x, y, width, h);
        }

        private void Swatch(float centreX, float bottomY, Tincture t)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(RawImage), typeof(Outline));
            go.transform.SetParent(marks, false);
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            if (t.Kind == TinctureKind.Fur)
            {
                img.texture = FurSwatch(t.Name);
                img.color = Color.white;
            }
            else
            {
                Rgba c = t.Kind == TinctureKind.Hex ? t.Hex
                    : Lexicon.Palette.TryGetValue(t.Name, out var p) ? p : new Rgba(255, 0, 255);
                img.color = new Color32(c.R, c.G, c.B, 255);
            }
            const float size = 11f;
            AtLocal(img.rectTransform, centreX - size / 2, bottomY + size, size, size);
        }

        /// <summary>Positions a child of the marks layer using the text layout's local coordinates (y up, top-left pivot).</summary>
        private void AtLocal(RectTransform rt, float x, float topY, float w, float h)
        {
            // Anchor at the text's pivot: that is the origin of the text layout's coordinates
            rt.anchorMin = rt.anchorMax = overlay.rectTransform.pivot;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, topY);
        }

        private void UpdateTooltip()
        {
            var tipBox = tooltip.transform.parent.gameObject;
            var canvas = overlay.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 mouse = ZInput.pointerPosition;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay.rectTransform, mouse, cam, out var local))
            {
                tipBox.SetActive(false);
                return;
            }
            // Text layout coordinates are relative to the rect's pivot; ScreenPointToLocalPoint is too.
            foreach (var wr in wordRects)
            {
                if (!wr.Key.Contains(local)) continue;
                tooltip.text = wr.Value;
                tipBox.SetActive(true);
                var tipRt = tipBox.GetComponent<RectTransform>();
                Canvas.ForceUpdateCanvases();
                var size = tooltip.rectTransform.sizeDelta + new Vector2(16, 10);
                tipRt.sizeDelta = size;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(panel.GetComponent<RectTransform>(), mouse, cam, out var inPanel);
                tipRt.anchorMin = tipRt.anchorMax = new Vector2(0.5f, 0.5f);
                tipRt.pivot = new Vector2(0f, 0f);
                tipRt.anchoredPosition = inPanel + new Vector2(14, 14);
                var trt = tooltip.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.pivot = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = Vector2.zero;
                return;
            }
            tipBox.SetActive(false);
        }

        // ================================================================== problems list

        private void ShowProblems()
        {
            var sb = new StringBuilder();
            int lines = 0;
            bool later = false;
            foreach (var w in parsed.Words)
            {
                if (w.Status == WordStatus.NotDrawnYet) { later = true; continue; }
                if (w.Message == null || w.Status == WordStatus.Correct) continue;
                if (lines++ >= 5) { sb.Append("<color=#bbbbbb>…</color>\n"); break; }
                sb.Append(Colourise(w)).Append('\n');
            }
            foreach (var m in parsed.Messages)
                if (lines++ < 6) sb.Append($"<color=#f04a40>{m}</color>\n");
            if (later)
                sb.Append("<color=#a0a0a0>Charges are understood but not drawn yet.</color>\n");
            if (sb.Length == 0 && (input.text ?? "").Trim().Length > 0)
                sb.Append(parsed.IsValid ? "<color=#a8d070>Good blazon.</color>" : "");
            problems.text = sb.ToString();
        }

        private static string Colourise(WordNote w)
        {
            string hex = w.Status == WordStatus.Wrong ? "#f04a40" : w.Status == WordStatus.Passable ? "#ffcc40" : "#a0a0a0";
            return $"<color={hex}>'{w.Word}': {w.Message}</color>";
        }

        // ================================================================== preview

        private void StartPreview()
        {
            renderAt = -1f;
            var b = parsed;
            IconArt.EnsureFor(b);
            int w = previewW, h = previewH;
            var mask = previewMask;
            previewJob = Task.Run(() => FieldRenderer.Render(b, w, h, mask));
        }

        private void FinishPreview()
        {
            var job = previewJob;
            previewJob = null;
            if (job.IsFaulted || previewTexture == null) return;
            var src = job.Result;
            var dst = new Color32[previewW * previewH];
            for (int y = 0; y < previewH; y++)
            {
                int s = y * previewW, d = (previewH - 1 - y) * previewW;
                for (int x = 0; x < previewW; x++)
                {
                    var c = src[s + x];
                    bool cloth = previewMask == null || previewMask[s + x];
                    dst[d + x] = new Color32(c.R, c.G, c.B, cloth ? (byte)255 : (byte)0);
                }
            }
            previewTexture.SetPixels32(dst);
            previewTexture.Apply(false);
        }

        // ================================================================== saved blazons

        private void SaveCurrent()
        {
            string blazon = (input.text ?? "").Trim();
            if (blazon.Length == 0) { status.text = "Nothing to save."; return; }
            string name = (saveName.text ?? "").Trim();
            if (name.Length == 0) name = SavedBlazons.SuggestName(blazon);
            bool replaced = SavedBlazons.Put(name, blazon);
            status.text = replaced ? $"Updated '{name}'." : $"Saved '{name}'.";
            saveName.text = "";
            RefreshSaved();
        }

        private void RefreshSaved()
        {
            foreach (Transform child in savedContent) Destroy(child.gameObject);
            pendingDelete = null;
            var gui = GUIManager.Instance;
            foreach (var saved in SavedBlazons.All)
            {
                var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(savedContent, false);
                row.GetComponent<LayoutElement>().preferredHeight = 30;
                var hlg = row.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6; hlg.childControlWidth = true; hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

                var s = saved;
                var load = gui.CreateButton($"{saved.name}   <color=#a0a0a0>{Shorten(saved.blazon, 60)}</color>", row.transform,
                    Vector2.zero, Vector2.zero, Vector2.zero);
                load.GetComponentInChildren<Text>().supportRichText = true;
                load.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                load.GetComponentInChildren<Text>().fontSize = 15;
                load.AddComponent<LayoutElement>().flexibleWidth = 1;
                load.GetComponent<Button>().onClick.AddListener(() =>
                {
                    input.text = s.blazon; // loads into the box; Apply still needed
                    status.text = $"Loaded '{s.name}'. Apply to paint it.";
                });

                var del = gui.CreateButton("Delete", row.transform, Vector2.zero, Vector2.zero, Vector2.zero);
                del.AddComponent<LayoutElement>().preferredWidth = 90;
                var entry = new Entry { Saved = saved, DeleteLabel = del.GetComponentInChildren<Text>() };
                entry.DeleteLabel.fontSize = 14;
                del.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (pendingDelete == entry)
                    {
                        SavedBlazons.Remove(entry.Saved);
                        status.text = $"Deleted '{entry.Saved.name}'.";
                        RefreshSaved();
                        return;
                    }
                    if (pendingDelete != null) pendingDelete.DeleteLabel.text = "Delete";
                    pendingDelete = entry;
                    pendingDeleteUntil = Time.unscaledTime + 3f;
                    entry.DeleteLabel.text = "Sure?";
                });
            }
        }

        private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ================================================================== small helpers

        private Text Label(string text, float x, float y, float w, float h, int size, Color colour)
        {
            var gui = GUIManager.Instance;
            var go = gui.CreateText(text, panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                gui.AveriaSerifBold, size, colour, true, Color.black, w, h, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            return go.GetComponent<Text>();
        }

        private void AddButton(string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, w, h);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        /// <summary>Top-left placement inside the panel: x right, y down.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private static void CopyRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.offsetMin = from.offsetMin;
            to.offsetMax = from.offsetMax;
        }

        // ================================================================== textures

        private static void MakeLineTextures()
        {
            if (wavy != null) return;
            wavy = LineTexture(16, 6, (x, y) => Mathf.Abs(y - (2.5f + 2f * Mathf.Sin(x / 16f * Mathf.PI * 2f))) < 0.9f);
            dashed = LineTexture(12, 3, (x, y) => x < 7 && y >= 1 && y <= 2);
            dotted = LineTexture(6, 3, (x, y) => x < 2 && y >= 1 && y <= 2);
        }

        private static Texture2D LineTexture(int w, int h, System.Func<float, float, bool> on)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = on(x + 0.5f, y + 0.5f) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false);
            DontDestroyOnLoad(tex);
            return tex;
        }

        private static Texture2D FurSwatch(string fur)
        {
            if (furSwatches.TryGetValue(fur, out var tex) && tex != null) return tex;
            const int size = 48;
            var px = FieldRenderer.Render(BlazonParser.Parse(fur), size, size);
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var dst = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = px[y * size + x];
                dst[(size - 1 - y) * size + x] = new Color32(c.R, c.G, c.B, 255);
            }
            tex.SetPixels32(dst);
            tex.Apply(false);
            DontDestroyOnLoad(tex);
            furSwatches[fur] = tex;
            return tex;
        }
    }
}
