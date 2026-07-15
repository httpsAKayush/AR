using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MetaXR.LofiStudy.ARFoundation
{
    /// <summary>
    /// Builds a floating, grabbable panel listing every mesh/segment under a
    /// loaded patient model (e.g. "raw_body_surface", "Segment_5", ...), with
    /// per-layer Show/Hide toggle and Highlight buttons.
    ///
    /// Call BuildLayerPanel(modelRoot) after a new model finishes loading —
    /// same pattern as MicrusControlPanel.BuildControlPanels().
    /// </summary>
    public class LayerControlPanel : MonoBehaviour
    {
        [Header("Panel Appearance")]
        public Color panelBgColor       = new Color(0.15f, 0.15f, 0.15f, 0.92f);
        public Color rowBgColor         = new Color(0.22f, 0.22f, 0.25f, 1f);
        public Color toggleOnColor      = new Color(0.20f, 0.55f, 0.20f, 1f);
        public Color toggleOffColor     = new Color(0.45f, 0.20f, 0.20f, 1f);
        public Color highlightBtnColor  = new Color(0.25f, 0.35f, 0.55f, 1f);
        public Color labelColor         = Color.white;
        public float fontSize           = 13f;
        public float panelWidth         = 0.55f;

        [Header("Highlight")]
        public Color highlightTintColor = new Color(1f, 0.9f, 0f, 1f);

        [Header("Placement")]
        public float sideOffset = 0.35f;   // distance to the side of the model
        public float heightOffset = 0.3f;  // raised above model center

        [Header("Grab Bar Settings")]
        public float grabBarHeight = 0.05f;
        public float grabBarGap    = 0.015f;

        GameObject m_Panel;
        Transform  m_ModelRoot;

        // Per-layer state, keyed by the renderer itself (unique per model instance)
        readonly Dictionary<Renderer, LayerEntry> m_Layers = new Dictionary<Renderer, LayerEntry>();

        class LayerEntry
        {
            public Renderer  renderer;
            public Material  originalMaterial;
            public bool      visible = true;
            public bool      highlighted = false;
            public Image     toggleButtonImage;
            public TextMeshProUGUI toggleButtonText;
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public void BuildLayerPanel(Transform modelRoot)
        {
            // Clear any previous panel/state — a new model was loaded
            if (m_Panel != null)
                Destroy(m_Panel);
            m_Layers.Clear();

            m_ModelRoot = modelRoot;

            Vector3 worldPos = modelRoot.position
                                + modelRoot.right * -sideOffset   // to the left of the model
                                + Vector3.up * heightOffset;
            Quaternion worldRot = modelRoot.rotation;

            m_Panel = CreatePanel("LayerControlPanel", worldPos, worldRot, panelWidth);

            var layout = m_Panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing                = 4f;
            layout.padding                = new RectOffset(6, 6, 6, 6);
            layout.childControlHeight     = false;
            layout.childControlWidth      = true;
            layout.childForceExpandHeight = false;

            var fitter = m_Panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            AddTitleLabel(m_Panel, "── LAYERS ──");
            AddGlobalButtons(m_Panel);

            var renderers = modelRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
                AddLayerRow(m_Panel, r);

            // Grab bar needs to track the panel's real post-layout height
            AttachGrabTracking(m_Panel, panelWidth);
        }

        // ── Global show/hide-all buttons ─────────────────────────────────────────

        void AddGlobalButtons(GameObject panel)
        {
            var row = new GameObject("Row_Global");
            row.transform.SetParent(panel.transform, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 40);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;

            CreateSimpleButton(row, "Show All", () => SetAllVisible(true));
            CreateSimpleButton(row, "Hide All", () => SetAllVisible(false));
        }

        void SetAllVisible(bool visible)
        {
            foreach (var kvp in m_Layers)
            {
                kvp.Value.visible = visible;
                kvp.Key.enabled = visible;
                UpdateToggleVisual(kvp.Value);
            }
        }

        // ── Per-layer row ────────────────────────────────────────────────────────

        void AddLayerRow(GameObject panel, MeshRenderer renderer)
        {
            var entry = new LayerEntry
            {
                renderer = renderer,
                originalMaterial = renderer.material,   // instance copy, safe to tint/restore
                visible = renderer.enabled,
            };
            m_Layers[renderer] = entry;

            var row = new GameObject("Row_" + renderer.name);
            row.transform.SetParent(panel.transform, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 38);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            // Layer name label — takes most of the row width
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(row.transform, false);
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
            var labelRt = labelGo.AddComponent<RectTransform>();
            var labelImg = labelGo.AddComponent<Image>();
            labelImg.color = rowBgColor;
            var labelTextGo = new GameObject("Text");
            labelTextGo.transform.SetParent(labelGo.transform, false);
            var labelTextRt = labelTextGo.AddComponent<RectTransform>();
            labelTextRt.anchorMin = Vector2.zero;
            labelTextRt.anchorMax = Vector2.one;
            labelTextRt.offsetMin = new Vector2(6, 2);
            labelTextRt.offsetMax = new Vector2(-6, -2);
            var labelTmp = labelTextGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = FormatLayerName(renderer.name);
            labelTmp.fontSize = fontSize;
            labelTmp.color = labelColor;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Highlight button
            CreateFixedButton(row, "★", highlightBtnColor, 34f, () => ToggleHighlight(entry));

            // Show/Hide toggle button
            var toggleGo = CreateFixedButton(row, "ON", toggleOnColor, 46f, () => ToggleVisible(entry));
            entry.toggleButtonImage = toggleGo.GetComponent<Image>();
            entry.toggleButtonText  = toggleGo.GetComponentInChildren<TextMeshProUGUI>();

            UpdateToggleVisual(entry);
        }

        string FormatLayerName(string raw)
        {
            // "raw_body_surface" -> "Raw Body Surface", "Segment_5" -> "Segment 5"
            string s = raw.Replace("_", " ");
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }

        void ToggleVisible(LayerEntry entry)
        {
            entry.visible = !entry.visible;
            entry.renderer.enabled = entry.visible;
            UpdateToggleVisual(entry);
        }

        void UpdateToggleVisual(LayerEntry entry)
        {
            if (entry.toggleButtonImage != null)
                entry.toggleButtonImage.color = entry.visible ? toggleOnColor : toggleOffColor;
            if (entry.toggleButtonText != null)
                entry.toggleButtonText.text = entry.visible ? "ON" : "OFF";
        }

        void ToggleHighlight(LayerEntry entry)
        {
            entry.highlighted = !entry.highlighted;

            if (entry.highlighted)
            {
                // Tint via material color — works with the URP/Lit materials
                // set up by FixMaterialsForQuest on the loaded model.
                if (entry.renderer.material.HasProperty("_BaseColor"))
                    entry.renderer.material.SetColor("_BaseColor", highlightTintColor);
            }
            else
            {
                if (entry.renderer.material.HasProperty("_BaseColor") &&
                    entry.originalMaterial.HasProperty("_BaseColor"))
                    entry.renderer.material.SetColor("_BaseColor", entry.originalMaterial.GetColor("_BaseColor"));
            }
        }

        // ── Panel scaffold (same pattern as MicrusControlPanel) ─────────────────

        GameObject CreatePanel(string name, Vector3 worldPos, Quaternion worldRot, float w)
        {
            var go = new GameObject(name);
            go.transform.position   = worldPos;
            go.transform.rotation   = worldRot;
            go.transform.localScale = Vector3.one;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta  = new Vector2(w * 1000f, 200f);   // starting height; ContentSizeFitter will shrink/grow to fit
            rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);

            var raycaster = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            raycaster.checkFor3DOcclusion = false;

            var bg = new GameObject("Background");
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = panelBgColor;
            var bgLe = bg.AddComponent<LayoutElement>();
            bgLe.ignoreLayout = true;

            return go;
        }

        void AttachGrabTracking(GameObject panel, float w)
        {
            var rt = panel.GetComponent<RectTransform>();

            float barWidthUI  = w * 1000f * 0.5f;
            float barHeightUI = grabBarHeight * 1000f;
            float gapUI       = grabBarGap * 1000f;

            var grabBarVisual = new GameObject("GrabBarVisual");
            grabBarVisual.transform.SetParent(panel.transform, false);
            var barRt = grabBarVisual.AddComponent<RectTransform>();
            barRt.anchorMin        = new Vector2(0.5f, 0f);
            barRt.anchorMax        = new Vector2(0.5f, 0f);
            barRt.pivot            = new Vector2(0.5f, 1f);
            barRt.sizeDelta        = new Vector2(barWidthUI, barHeightUI);
            barRt.anchoredPosition = new Vector2(0f, -gapUI);
            var barImg = grabBarVisual.AddComponent<Image>();
            barImg.color = new Color(0.5f, 0.5f, 0.55f, 0.85f);
            var barLe = grabBarVisual.AddComponent<LayoutElement>();
            barLe.ignoreLayout = true;

            var grabHandle = new GameObject("GrabHandle");
            grabHandle.transform.SetParent(panel.transform, false);
            grabHandle.transform.localPosition = Vector3.zero;
            grabHandle.transform.localRotation = Quaternion.identity;
            grabHandle.transform.localScale    = new Vector3(1000f, 1000f, 1000f);

            var box = grabHandle.AddComponent<BoxCollider>();
            box.size = new Vector3(w * 0.5f, grabBarHeight, 0.05f);

            var rb = panel.AddComponent<Rigidbody>();
            rb.useGravity  = false;
            rb.isKinematic = true;

            var grab = panel.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(box);
            grab.throwOnDetach    = false;
            grab.trackPosition    = true;
            grab.trackRotation    = true;
            grab.trackScale       = false;
            grab.movementType     = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.attachEaseInTime = 0f;

            var tracker = panel.AddComponent<PanelGrabBarTracker>();
            tracker.panelRect      = rt;
            tracker.barRect        = barRt;
            tracker.grabBox        = box;
            tracker.gapWorld       = grabBarGap;
            tracker.barHeightWorld = grabBarHeight;
        }

        // ── UI helpers ───────────────────────────────────────────────────────────

        void AddTitleLabel(GameObject panel, string text)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(panel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 30);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize + 2f;
            tmp.color     = new Color(0.8f, 0.8f, 0.8f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
        }

        void CreateSimpleButton(GameObject parent, string label, System.Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = rowBgColor;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = rowBgColor;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.4f, 1f);
            btn.colors = colors;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(2, 2);
            textRt.offsetMax = new Vector2(-2, -2);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;

            btn.onClick.AddListener(() => onClick());
        }

        GameObject CreateFixedButton(GameObject parent, string label, Color color, float width, System.Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth  = 0f;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = color;
            colors.highlightedColor = color * 1.2f;
            btn.colors = colors;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(2, 2);
            textRt.offsetMax = new Vector2(-2, -2);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            btn.onClick.AddListener(() => onClick());
            return go;
        }
    }
}