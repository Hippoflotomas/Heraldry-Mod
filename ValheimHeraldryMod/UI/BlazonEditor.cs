using System.Collections.Generic;
using System.Linq;
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
    internal partial class BlazonEditor : MonoBehaviour
    {
        private static BlazonEditor instance;

        // ---- layout (canvas units) ----
        private const float PanelW = 1060, PanelH = 720;
        private const float PreviewH = 560, PreviewX = 30, PreviewY = 70;
        private const float RightX = 310, RightW = 720;
        private const float ChoiceW = 420, ChoiceRowH = 40;
        private const float PatternW = 160;
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

        private IBlazonTarget target;
        private Text title, applyLabel, choiceLabel;

        // ---- basic mode (dropdowns; Hippo 2026-10-13) ----
        private const float BasicRowY = 96, BasicRowStep = 38, BasicRowH = 32;
        private const float ColLabel = 0, ColMain = 90, Col1 = 316, Col2 = 472, Col3 = 628;
        private const int DropdownFont = 14;
        // Bottom of the window, from the bottom up: saved list, saved row, Apply/Cancel, problems; the blazon box
        // takes everything above that (Hippo: buttons further down, text box as tall as possible).
        private const float ScrollY = 606, ScrollH = 98, SavedRowY = 562, ButtonsY = 514;
        private const float ProblemsH = 70, ProblemsY = ButtonsY - 8 - ProblemsH;
        private const float AdvancedFieldY = 98, AdvancedFieldH = ProblemsY - 6 - AdvancedFieldY;
        private const float BasicFieldY = BasicRowY + 5 * BasicRowStep + 2, BasicFieldH = ProblemsY - 6 - BasicFieldY;
        private GameObject basicPanel;
        private Text blazonLabel, basicTabLabel, advancedTabLabel;
        private RectTransform fieldRect;
        private bool basicMode;
        private bool syncingBasic;
        private BasicBlazon basic = new BasicBlazon();
        private readonly List<BasicBlazon.ChargeChoice> chargeChoices = new List<BasicBlazon.ChargeChoice>();
        private Dropdown ddField, ddColour1, ddColour2, ddFieldLine;
        private Dropdown ddOrdinary, ddOrdinaryColour, ddOrdinaryLine;
        private Dropdown ddSecond, ddSecondColour, ddSecondLine;
        private Dropdown ddCharge, ddStyle, ddChargeColour, ddCount, ddPlacement;
        private Image choiceIcon;
        // ---- pattern picker (tagged faces with up to four patterns; Hippo 2026-10-16) ----
        private Dropdown patternPicker;
        private List<int> patternNumbers = new List<int> { 1 };
        private bool syncingPattern;
        private GameObject choiceRow, choiceList;
        private RectTransform choiceListContent;
        private float aspect = 0.4f;
        private Blazon parsed;
        private readonly List<KeyValuePair<Rect, string>> wordRects = new List<KeyValuePair<Rect, string>>();

        private Texture2D previewTexture;
        private Task<Rgba[]> previewJob;
        private int previewW, previewH;
        private bool[] previewMask;
        private float renderAt = -1f;     // time to start the next preview render (debounce)
        private int unblockAfterFrame = -1; // release the game's input a frame after closing

        private Entry pendingDelete;
        private float pendingDeleteUntil;
        private class Entry { public SavedBlazons.Entry Saved; public Text DeleteLabel; }

        private static Texture2D wavy, dashed, dotted;
        private static readonly Dictionary<string, Texture2D> furSwatches = new Dictionary<string, Texture2D>();

        public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

        public static void Open(HeraldicBanner banner) => Open(new BannerTarget(banner));

        public static void Open(IBlazonTarget target)
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

            title = Label("Heraldic banner", 30, 18, 600, 40, 26, gui.ValheimOrange);

            // ---- chooser (the herald's table: which shield) ----
            // A drop-down: the chosen shield (icon and name) on a button; clicking it lists them all.
            choiceRow = new GameObject("Chooser", typeof(RectTransform));
            choiceRow.transform.SetParent(panel.transform, false);
            Place(choiceRow.GetComponent<RectTransform>(), RightX + 300, 56, ChoiceW, 40);
            var header = GUIManager.Instance.CreateButton("", choiceRow.transform, Vector2.zero, Vector2.zero, Vector2.zero, ChoiceW, 40);
            Place(header.GetComponent<RectTransform>(), 0, 0, ChoiceW, 40);
            header.GetComponent<Button>().onClick.AddListener(ToggleChoiceList);
            choiceIcon = Icon(header.transform, 6, 4, 32);
            choiceLabel = header.GetComponentInChildren<Text>();
            choiceLabel.alignment = TextAnchor.MiddleLeft;
            choiceLabel.fontSize = 17;
            choiceLabel.supportRichText = true;
            var labelRt = choiceLabel.rectTransform;
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(46, 0); labelRt.offsetMax = new Vector2(-30, 0);
            var arrow = Label("\u25BC", ChoiceW - 28, 10, 22, 20, 14, Beige, header.transform);
            arrow.alignment = TextAnchor.MiddleCenter;

            // ---- pattern picker: which of the item's patterns is being edited; greyed out when there's only one ----
            var patternGo = gui.CreateDropDown(panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, DropdownFont, PatternW, BasicRowH);
            Place(patternGo.GetComponent<RectTransform>(), RightX + 130, 62, PatternW, BasicRowH);
            patternGo.AddComponent<CanvasGroup>();
            patternPicker = patternGo.GetComponent<Dropdown>();
            patternPicker.onValueChanged.AddListener(PickPattern);

            // ---- preview ----
            var frame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(panel.transform, false);
            frame.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            frame.GetComponent<Image>().raycastTarget = false;
            Place(frame.GetComponent<RectTransform>(), PreviewX - 10, PreviewY - 10, 250, PreviewH + 20);
            var pv = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            pv.transform.SetParent(panel.transform, false);
            preview = pv.GetComponent<RawImage>();

            // ---- text box ----
            blazonLabel = Label("Blazon", RightX, 70, 300, 26, 18, Beige);
            var field = gui.CreateInputField(panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "e.g.  Azure, a bend or", FontSize, RightW, AdvancedFieldH);
            fieldRect = field.GetComponent<RectTransform>();
            Place(fieldRect, RightX, AdvancedFieldY, RightW, AdvancedFieldH);
            input = field.GetComponent<InputField>();
            input.lineType = InputField.LineType.MultiLineSubmit;
            input.characterLimit = HeraldicBanner.MaxBlazonLength; // the server limit is checked separately (BlazonLimits)
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
            problems = Label("", RightX, ProblemsY, RightW, ProblemsH, 15, Color.white);
            problems.supportRichText = true;
            problems.alignment = TextAnchor.UpperLeft;
            problems.verticalOverflow = VerticalWrapMode.Truncate;

            // ---- buttons ----
            applyLabel = AddButton("Apply", RightX, ButtonsY, 150, 40, Apply);
            AddButton("Cancel", RightX + 160, ButtonsY, 150, 40, Hide);
            status = Label("", RightX + 330, ButtonsY + 8, RightW - 330, 28, 15, Beige);

            // ---- saved blazons ----
            Label("Saved blazons", RightX, SavedRowY + 4, 300, 26, 18, Beige);
            var nameGo = gui.CreateInputField(panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "name", 16, 380, 34);
            Place(nameGo.GetComponent<RectTransform>(), RightX + 170, SavedRowY, 380, 34);
            saveName = nameGo.GetComponent<InputField>();
            saveName.characterLimit = 40;
            AddButton("Save as", RightX + 560, SavedRowY - 2, 160, 38, SaveCurrent);

            var scroll = gui.CreateScrollView(panel.transform, false, true, 8f, 4f, ColorBlock.defaultColorBlock,
                new Color(0, 0, 0, 0.3f), RightW, ScrollH);
            Place(scroll.GetComponent<RectTransform>(), RightX, ScrollY, RightW, ScrollH);
            savedContent = scroll.transform.Find("Scroll View/Viewport/Content") as RectTransform;
            var vlg = savedContent.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 3; vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset(6, 6, 4, 4);

            BuildBasic();
            BuildCostPanel();

            // ---- the chooser's list (built when opened, drawn over everything but the tooltip) ----
            choiceList = gui.CreateScrollView(panel.transform, false, true, 8f, 4f, ColorBlock.defaultColorBlock,
                new Color(0.08f, 0.06f, 0.05f, 0.97f), ChoiceW, 200);
            choiceList.name = "ChooserList";
            choiceListContent = choiceList.transform.Find("Scroll View/Viewport/Content") as RectTransform;
            var listLayout = choiceListContent.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 2; listLayout.childControlHeight = true; listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false; listLayout.childForceExpandWidth = true;
            listLayout.padding = new RectOffset(4, 4, 4, 4);
            choiceList.SetActive(false);

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

        private void Show(IBlazonTarget newTarget)
        {
            if (panel == null) Build();
            target = newTarget;
            title.text = target.Title;
            applyLabel.text = target.ApplyLabel;
            SetupShape();

            choiceRow.SetActive(target.Choices != null);
            choiceList.SetActive(false);
            ShowChoice();
            ShowPatterns();
            Load(target.GetText());
            basic = new BasicBlazon();
            FillChargeList();
            SyncBasic();
            SetMode(HeraldryConfig.LastEditorMode != "advanced", false);
            if (basicMode && input.text.Trim().Length == 0) input.text = basic.Build(); // start from the default arms
            saveName.text = "";
            RefreshSaved();
            status.text = target.Blocked ?? "";
            panel.SetActive(true);
            if (unblockAfterFrame >= 0) unblockAfterFrame = -1; // reopened before the release: keep the block
            else GUIManager.BlockInput(true);
            if (!basicMode)
            {
                input.ActivateInputField();
                input.MoveTextEnd(false);
            }
            OnTextChanged();
            renderAt = 0f; // render the preview straight away
        }

        /// <summary>The preview's size and outline, from the target (for the table: the chosen shield's face).</summary>
        private void SetupShape()
        {
            aspect = Mathf.Clamp(target.Aspect, 0.15f, 4f);
            // Preview sized to the banner's shape, with its real outline; the longer side 400 px (a wide drape is
            // wider than tall)
            previewH = aspect <= 1f ? 400 : Mathf.Max(16, Mathf.RoundToInt(400 / aspect));
            previewW = aspect <= 1f ? Mathf.Max(16, Mathf.RoundToInt(previewH * aspect)) : 400;
            float shownH = PreviewH, shownW = shownH * aspect;
            if (shownW > 230) { shownW = 230; shownH = shownW / aspect; }
            Place(preview.rectTransform, PreviewX + (230 - shownW) / 2, PreviewY, shownW, shownH);
            if (previewTexture == null || previewTexture.width != previewW || previewTexture.height != previewH)
            {
                if (previewTexture != null) Destroy(previewTexture);
                previewTexture = new Texture2D(previewW, previewH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            }
            previewMask = OutlineMask.FromTriangles(target.OutlineUV, target.OutlineTriangles, previewW, previewH, target.HiddenTop);
            preview.texture = previewTexture;
        }

        private void Hide()
        {
            if (panel == null || !panel.activeSelf) return;
            panel.SetActive(false);
            choiceList.SetActive(false);
            tooltip.transform.parent.gameObject.SetActive(false);
            target = null;
            // Release next frame, so the Enter or Escape that closed us can't also open chat or the menu
            unblockAfterFrame = Time.frameCount;
        }

        private void Apply()
        {
            if (target == null) return;
            if (target.Blocked != null)
            {
                status.text = target.Blocked;
                return;
            }
            string limit = BlazonLimits.Problem(input.text.Trim(), parsed);
            if (limit != null)
            {
                status.text = $"<color=#f04a40>Not applied: {limit}.</color>";
                return;
            }
            string unpaid = CostProblem();
            if (unpaid != null)
            {
                status.text = $"<color=#f04a40>Not applied: {unpaid}.</color>";
                return;
            }
            PayCost();
            string message = target.Apply(StoredText());
            Hide();
            if (message != null) MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, message);
        }

        private void PickChoice(int index)
        {
            choiceList.SetActive(false);
            var choices = target?.Choices;
            if (choices == null || index < 0 || index >= choices.Count) return;
            Load(target.Choose(index));
            SetupShape();
            ShowChoice();
            ShowPatterns();
            renderAt = 0f;
        }

        /// <summary>The target's patterns in the picker (just "Pattern 1", greyed out, when there's only one).</summary>
        private void ShowPatterns()
        {
            var list = target?.Patterns;
            patternNumbers = list != null && list.Count > 0 ? list.ToList() : new List<int> { target?.Pattern ?? 1 };
            syncingPattern = true;
            patternPicker.ClearOptions();
            patternPicker.AddOptions(patternNumbers.Select(n => "Pattern " + n).ToList());
            Set(patternPicker, patternNumbers.IndexOf(target?.Pattern ?? 1), patternNumbers.Count > 1);
            syncingPattern = false;
        }

        /// <summary>Another pattern: its own arms in the box and its own shape in the preview (unapplied edits are dropped).</summary>
        private void PickPattern(int index)
        {
            if (syncingPattern || target == null || index < 0 || index >= patternNumbers.Count) return;
            int number = patternNumbers[index];
            if (number == target.Pattern) return;
            Load(target.ChoosePattern(number));
            SetupShape();
            renderAt = 0f;
        }

        private void ShowChoice()
        {
            var choices = target?.Choices;
            if (choices == null) return;
            int i = Mathf.Max(0, target.Chosen);
            var icons = target.ChoiceIcons;
            choiceLabel.text = choices.Count == 0 ? "<color=#a0a0a0>nothing to paint in your inventory</color>" : choices[i];
            choiceIcon.sprite = choices.Count > 0 && icons != null && i < icons.Count ? icons[i] : null;
            choiceIcon.enabled = choiceIcon.sprite != null;
        }

        private void ToggleChoiceList()
        {
            if (choiceList.activeSelf) { choiceList.SetActive(false); return; }
            var choices = target?.Choices;
            if (choices == null || choices.Count == 0) return;
            var icons = target.ChoiceIcons;

            foreach (Transform child in choiceListContent) Destroy(child.gameObject);
            var gui = GUIManager.Instance;
            for (int k = 0; k < choices.Count; k++)
            {
                int index = k;
                var row = gui.CreateButton("", choiceListContent, Vector2.zero, Vector2.zero, Vector2.zero);
                row.AddComponent<LayoutElement>().preferredHeight = ChoiceRowH;
                row.GetComponent<Button>().onClick.AddListener(() => PickChoice(index));
                var icon = Icon(row.transform, 6, 3, ChoiceRowH - 6);
                icon.sprite = icons != null && k < icons.Count ? icons[k] : null;
                icon.enabled = icon.sprite != null;
                var text = row.GetComponentInChildren<Text>();
                text.text = k == target.Chosen ? $"<color=#ffbf40>{choices[k]}</color>" : choices[k];
                text.supportRichText = true;
                text.fontSize = 16;
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(ChoiceRowH + 6, 0); text.rectTransform.offsetMax = new Vector2(-6, 0);
            }

            float height = Mathf.Min(choices.Count, 8) * (ChoiceRowH + 2) + 10;
            Place(choiceList.GetComponent<RectTransform>(), RightX + 300, 98, ChoiceW, height);
            choiceList.SetActive(true);
            choiceList.transform.SetAsLastSibling();
            tooltip.transform.parent.SetAsLastSibling();
        }

        private static Image Icon(Transform parent, float x, float y, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            Place(img.rectTransform, x, y, size, size);
            return img;
        }

        // ================================================================== basic mode

        private void BuildBasic()
        {
            var gui = GUIManager.Instance;
            // Tabs, top right
            basicTabLabel = AddButton("Basic", RightX + 390, 12, 160, 36, () => SetMode(true, true));
            advancedTabLabel = AddButton("Advanced", RightX + 560, 12, 160, 36, () => SetMode(false, true));

            basicPanel = new GameObject("Basic", typeof(RectTransform));
            basicPanel.transform.SetParent(panel.transform, false);
            Place(basicPanel.GetComponent<RectTransform>(), 0, 0, PanelW, PanelH);
            var t = basicPanel.transform;

            float Row(int r) => BasicRowY + r * BasicRowStep;
            void RowLabel(int r, string text) => Label(text, RightX + ColLabel, Row(r) + 5, ColMain - 6, BasicRowH, 17, Beige, t);

            RowLabel(0, "Field");
            ddField = MakeDropdown(t, ColMain, Row(0), Col1 - ColMain - 6, BasicBlazon.Fields, i => { basic.Field = BasicBlazon.Fields[i].Value; });
            ddColour1 = MakeDropdown(t, Col1, Row(0), Col2 - Col1 - 6, BasicBlazon.Colours, i => { basic.Colour1 = BasicBlazon.Colours[i].Value; });
            ddColour2 = MakeDropdown(t, Col2, Row(0), Col3 - Col2 - 6, BasicBlazon.Colours, i => { basic.Colour2 = BasicBlazon.Colours[i].Value; });
            ddFieldLine = MakeDropdown(t, Col3, Row(0), RightW - Col3, BasicBlazon.Lines, i => { basic.FieldLine = BasicBlazon.Lines[i].Value; });

            RowLabel(1, "Ordinary");
            ddOrdinary = MakeDropdown(t, ColMain, Row(1), Col1 - ColMain - 6, BasicBlazon.Ordinaries, i => { basic.Ordinary = BasicBlazon.Ordinaries[i].Value; });
            ddOrdinaryColour = MakeDropdown(t, Col1, Row(1), Col2 - Col1 - 6, BasicBlazon.Colours, i => { basic.OrdinaryColour = BasicBlazon.Colours[i].Value; });
            ddOrdinaryLine = MakeDropdown(t, Col3, Row(1), RightW - Col3, BasicBlazon.Lines, i => { basic.OrdinaryLine = BasicBlazon.Lines[i].Value; });

            RowLabel(2, "Also");
            ddSecond = MakeDropdown(t, ColMain, Row(2), Col1 - ColMain - 6, BasicBlazon.SecondOrdinaries, i => { basic.Second = BasicBlazon.SecondOrdinaries[i].Value; });
            ddSecondColour = MakeDropdown(t, Col1, Row(2), Col2 - Col1 - 6, BasicBlazon.Colours, i => { basic.SecondColour = BasicBlazon.Colours[i].Value; });
            ddSecondLine = MakeDropdown(t, Col3, Row(2), RightW - Col3, BasicBlazon.Lines, i => { basic.SecondLine = BasicBlazon.Lines[i].Value; });

            RowLabel(3, "Charge");
            ddCharge = MakeDropdown(t, ColMain, Row(3), Col1 - ColMain - 6, new BasicBlazon.Option[0], i =>
            {
                if (i < 0 || i >= chargeChoices.Count || chargeChoices[i].IsDivider) return false;
                basic.Charge = chargeChoices[i];
                return true;
            });
            ddStyle = MakeDropdown(t, Col1, Row(3), Col2 - Col1 - 6, BasicBlazon.Styles, i => { basic.Style = BasicBlazon.Styles[i].Value; });
            ddChargeColour = MakeDropdown(t, Col2, Row(3), Col3 - Col2 - 6, BasicBlazon.Colours, i => { basic.ChargeColour = BasicBlazon.Colours[i].Value; });
            ddCount = MakeDropdown(t, Col3, Row(3), RightW - Col3, BasicBlazon.Counts, i => { basic.Count = i + 1; });

            RowLabel(4, "Where");
            ddPlacement = MakeDropdown(t, ColMain, Row(4), Col2 - ColMain - 6, BasicBlazon.Placements, i =>
            {
                if (!basic.PlacementAllowed(BasicBlazon.Placements[i].Value)) return false;
                basic.Placement = BasicBlazon.Placements[i].Value;
                return true;
            });
        }

        /// <summary>A dropdown in the basic panel. <paramref name="set"/> stores the choice (dividers are refused by the caller).</summary>
        private Dropdown MakeDropdown(Transform parent, float x, float y, float w, BasicBlazon.Option[] options, System.Action<int> set) =>
            MakeDropdown(parent, x, y, w, options, i =>
            {
                if (options[i].IsDivider) return false;
                set(i);
                return true;
            });

        private Dropdown MakeDropdown(Transform parent, float x, float y, float w, BasicBlazon.Option[] options, System.Func<int, bool> set)
        {
            var go = GUIManager.Instance.CreateDropDown(parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, DropdownFont, w, BasicRowH);
            Place(go.GetComponent<RectTransform>(), RightX + x, y, w, BasicRowH);
            go.AddComponent<CanvasGroup>();
            var dd = go.GetComponent<Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(options.Select(o => o.Label).ToList());
            // One line in the box, never wrapped onto two (the labels are kept short enough to fit)
            if (dd.captionText != null)
            {
                dd.captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
                dd.captionText.resizeTextForBestFit = false;
            }
            dd.onValueChanged.AddListener(i =>
            {
                if (syncingBasic) return;
                bool taken = set(i);
                if (taken)
                {
                    basic.Tidy();
                    input.text = basic.Build(); // the live blazon line; parsing, preview and Apply follow from it
                }
                SyncBasic(); // also puts a refused choice (a divider, "on" with no ordinary) back
            });
            return dd;
        }

        /// <summary>Built-in charges, then the charge packs in use (they change with the server).</summary>
        private void FillChargeList()
        {
            chargeChoices.Clear();
            chargeChoices.AddRange(BasicBlazon.BuiltInCharges);
            var packs = ChargeLibrary.Instance.Names();
            if (packs.Count > 0)
            {
                chargeChoices.Add(BasicBlazon.PackDivider);
                foreach (var name in packs) chargeChoices.Add(BasicBlazon.PackCharge(name));
            }
            ddCharge.ClearOptions();
            ddCharge.AddOptions(chargeChoices.Select(c => c.Label).ToList());
        }

        /// <summary>Shows the model's choices and greys out what doesn't apply.</summary>
        private void SyncBasic()
        {
            syncingBasic = true;
            try
            {
                Set(ddField, System.Array.FindIndex(BasicBlazon.Fields, o => o.Value == basic.Field), true);
                Set(ddColour1, ColourIndex(basic.Colour1), true);
                Set(ddColour2, ColourIndex(basic.Colour2), basic.FieldHasSecondColour);
                Set(ddFieldLine, LineIndex(basic.FieldLine), basic.FieldHasLine);

                Set(ddOrdinary, System.Array.FindIndex(BasicBlazon.Ordinaries, o => o.Value == basic.Ordinary), true);
                Set(ddOrdinaryColour, ColourIndex(basic.OrdinaryColour), basic.HasOrdinary);
                Set(ddOrdinaryLine, LineIndex(basic.OrdinaryLine), basic.HasOrdinary);

                Set(ddSecond, System.Array.FindIndex(BasicBlazon.SecondOrdinaries, o => o.Value == basic.Second), true);
                Set(ddSecondColour, ColourIndex(basic.SecondColour), basic.HasSecond);
                Set(ddSecondLine, LineIndex(basic.SecondLine), basic.HasSecond);

                Set(ddCharge, Mathf.Max(0, chargeChoices.IndexOf(basic.Charge)), true);
                Set(ddStyle, System.Array.FindIndex(BasicBlazon.Styles, o => o.Value == basic.Style), basic.HasCharge);
                Set(ddChargeColour, ColourIndex(basic.ChargeColour), basic.ChargeHasColour);
                Set(ddCount, basic.Count - 1, basic.HasCharge && !basic.Charge.OnlyOne);
                Set(ddPlacement, System.Array.FindIndex(BasicBlazon.Placements, o => o.Value == basic.Placement), basic.HasCharge);
            }
            finally
            {
                syncingBasic = false;
            }
        }

        /// <summary>
        ///     When the arms in the box didn't come from the dropdowns (written in Advanced, or already on the item),
        ///     the dropdowns are dimmed: they don't describe these arms, and touching one replaces them.
        /// </summary>
        private void ShowBasicStale()
        {
            string current = (input.text ?? "").Trim();
            bool stale = current.Length > 0 && current != basic.Build();
            var group = basicPanel.GetComponent<CanvasGroup>();
            if (group == null) group = basicPanel.AddComponent<CanvasGroup>();
            group.alpha = stale ? 0.45f : 1f;
            const string Note = "Changing a dropdown replaces these arms.";
            if (stale && status.text.Length == 0) status.text = Note;
            else if (!stale && status.text == Note) status.text = "";
        }

        private static int ColourIndex(string value) => Mathf.Max(0, System.Array.FindIndex(BasicBlazon.Colours, o => o.Value == value));
        private static int LineIndex(string value) => Mathf.Max(0, System.Array.FindIndex(BasicBlazon.Lines, o => o.Value == value));

        private static void Set(Dropdown dd, int index, bool enabled)
        {
            dd.SetValueWithoutNotify(Mathf.Max(0, index));
            dd.RefreshShownValue();
            dd.interactable = enabled;
            dd.GetComponent<CanvasGroup>().alpha = enabled ? 1f : 0.35f;
        }

        /// <summary>
        ///     Basic: the dropdowns, with the blazon they make shown read-only in a short text box below (same
        ///     underlines, swatches and tooltips as advanced mode, so it teaches the words). Advanced: the text box.
        /// </summary>
        private void SetMode(bool basicOn, bool remember)
        {
            basicMode = basicOn;
            if (remember) HeraldryConfig.LastEditorMode = basicOn ? "basic" : "advanced";
            basicPanel.SetActive(basicOn);
            input.readOnly = basicOn;
            if (basicOn)
            {
                Place(fieldRect, RightX, BasicFieldY, RightW, BasicFieldH);
                Place(blazonLabel.rectTransform, RightX + Col2, BasicRowY + 4 * BasicRowStep + 5, RightW - Col2, BasicRowH);
                blazonLabel.text = "The blazon this makes:";
                input.DeactivateInputField();
                ShowBasicStale();
            }
            else
            {
                Place(fieldRect, RightX, AdvancedFieldY, RightW, AdvancedFieldH);
                Place(blazonLabel.rectTransform, RightX, 70, 300, 26);
                blazonLabel.text = "Blazon";
                if (status.text == "Changing a dropdown replaces these arms.") status.text = "";
                input.ActivateInputField();
                input.MoveTextEnd(false);
            }
            basicTabLabel.color = basicOn ? GUIManager.Instance.ValheimOrange : Color.white;
            advancedTabLabel.color = basicOn ? Color.white : GUIManager.Instance.ValheimOrange;
            OnTextChanged(); // re-lay the underlines and swatches for the box's new size
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

            if (target == null || !target.IsAlive) { Hide(); return; } // destroyed while editing
            if (ZInput.GetKeyDown(KeyCode.Escape, false))
            {
                if (choiceList.activeSelf) choiceList.SetActive(false); // Escape closes the list first
                else Hide();
                return;
            }
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
            parsed = BlazonParser.Parse(StoredText()); // with the fingerprints, so others' charges count as known
            LayoutMarks(text);
            ShowProblems();
            RefreshCost();
            if (basicMode && basic != null) ShowBasicStale();
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
                Rgba c = t.Kind == TinctureKind.Hex || t.Kind == TinctureKind.Tartan ? t.Hex
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
            string limit = BlazonLimits.Problem((input.text ?? "").Trim(), parsed);
            if (limit != null) { sb.Append($"<color=#f04a40>{char.ToUpper(limit[0])}{limit.Substring(1)}.</color>\n"); lines++; }
            foreach (var w in parsed.Words)
            {
                if (w.Status == WordStatus.NotDrawnYet && w.Message == null) { later = true; continue; }
                if (w.Message == null || w.Status == WordStatus.Correct) continue;
                if (lines++ >= 5) { sb.Append("<color=#bbbbbb>…</color>\n"); break; }
                sb.Append(Colourise(w)).Append('\n');
            }
            foreach (var m in parsed.Messages)
                if (lines++ < 6) sb.Append($"<color=#f04a40>{m}</color>\n");
            if (later)
                sb.Append("<color=#a0a0a0>Charges are understood but not drawn yet.</color>\n");
            if (sb.Length == 0 && limit == null && (input.text ?? "").Trim().Length > 0)
                sb.Append(parsed.IsValid ? "<color=#a8d070>Good blazon.</color>" : "");
            problems.text = sb.ToString();
        }

        private static string Colourise(WordNote w)
        {
            string hex = w.Status == WordStatus.Wrong ? "#f04a40" : w.Status == WordStatus.Passable ? "#ffcc40" : "#a0a0a0";
            return $"<color={hex}>'{w.Word}': {w.Message}</color>";
        }

        // ================================================================== preview

        // ================================================================== the hidden fingerprints

        /// <summary>Which picture each [name] meant in the arms loaded into the box (BlazonText's hidden tail).</summary>
        private readonly Dictionary<string, string> knownCharges = new Dictionary<string, string>();

        /// <summary>Puts stored arms in the box: the visible text, remembering their pictures' fingerprints.</summary>
        private void Load(string stored)
        {
            knownCharges.Clear();
            var b = BlazonParser.Parse(stored ?? "");
            foreach (var c in b.Charges.Concat(b.Ordinaries.SelectMany(o => o.Charges)))
                if (c.Hash != null && c.PackName != null && !knownCharges.ContainsKey(c.PackName))
                    knownCharges[c.PackName] = c.Hash;
            input.text = BlazonText.Visible(stored ?? "");
        }

        /// <summary>
        ///     The text to store: what's in the box plus the fingerprint of each [charge] — the one the loaded arms
        ///     had under that name (so editing someone's arms keeps their picture), else the one this name finds here.
        /// </summary>
        private string StoredText()
        {
            string visible = (input.text ?? "").Trim();
            if (visible.IndexOf('[') < 0) return visible;
            var b = BlazonParser.Parse(visible);
            var charges = b.Charges.Concat(b.Ordinaries.SelectMany(o => o.Charges)).Where(c => c.PackIndex >= 0).ToList();
            if (charges.Count == 0) return visible;
            var hashes = new string[charges.Max(c => c.PackIndex) + 1];
            for (int i = 0; i < hashes.Length; i++) hashes[i] = "";
            foreach (var c in charges)
                hashes[c.PackIndex] = (c.PackName != null && knownCharges.TryGetValue(c.PackName, out var h) ? h : c.Hash) ?? "";
            return BlazonText.Join(visible, hashes);
        }

        private void StartPreview()
        {
            renderAt = -1f;
            var b = parsed;
            IconArt.EnsureFor(b);
            int w = previewW, h = previewH;
            var mask = previewMask;
            var arrangement = target?.Arrangement ?? FieldRenderer.Arrangement.Spread;
            float cu = target?.CentreU ?? -1f, cv = target?.CentreV ?? -1f;
            previewJob = Task.Run(() => FieldRenderer.Render(b, w, h, mask, arrangement, cu, cv));
        }

        private void FinishPreview()
        {
            var job = previewJob;
            previewJob = null;
            if (job.IsFaulted || previewTexture == null) return;
            var src = job.Result;
            if (src.Length != previewW * previewH) { renderAt = 0f; return; } // the shape changed while it rendered
            var dst = new Color32[previewW * previewH];
            for (int y = 0; y < previewH; y++)
            {
                int s = y * previewW, d = (previewH - 1 - y) * previewW;
                for (int x = 0; x < previewW; x++)
                {
                    var c = src[s + x];
                    bool cloth = previewMask == null || previewMask[s + x];
                    dst[d + x] = new Color32(c.R, c.G, c.B, cloth ? c.A : (byte)0);
                }
            }
            previewTexture.SetPixels32(dst);
            previewTexture.Apply(false);
        }

        // ================================================================== saved blazons

        private void SaveCurrent()
        {
            string blazon = StoredText();
            if (blazon.Length == 0) { status.text = "Nothing to save."; return; }
            string name = (saveName.text ?? "").Trim();
            if (name.Length == 0) name = SavedBlazons.SuggestName(BlazonText.Visible(blazon));
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
                var load = gui.CreateButton($"{saved.name}   <color=#a0a0a0>{Shorten(BlazonText.Visible(saved.blazon), 60)}</color>", row.transform,
                    Vector2.zero, Vector2.zero, Vector2.zero);
                load.GetComponentInChildren<Text>().supportRichText = true;
                load.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                load.GetComponentInChildren<Text>().fontSize = 15;
                load.AddComponent<LayoutElement>().flexibleWidth = 1;
                load.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (basicMode) SetMode(false, true); // saved arms go to the text box; basic can't show them
                    Load(s.blazon); // loads into the box; Apply still needed
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

        private Text Label(string text, float x, float y, float w, float h, int size, Color colour, Transform parent = null)
        {
            var gui = GUIManager.Instance;
            var go = gui.CreateText(text, parent ?? panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                gui.AveriaSerifBold, size, colour, true, Color.black, w, h, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            // Labels are only to read: they must never catch the mouse, or they eat clicks meant for the
            // controls under them (the "Blazon" label's box covered most of the pattern picker)
            var label = go.GetComponent<Text>();
            label.raycastTarget = false;
            return label;
        }

        private Text AddButton(string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick, Transform parent = null)
        {
            var go = GUIManager.Instance.CreateButton(text, parent ?? panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, w, h);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponentInChildren<Text>();
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
