using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MetaXR.LofiStudy.ARFoundation
{
    public class MicrusControlPanel : MonoBehaviour
    {
        [Header("Network")]
        [Tooltip("Discovery component for the ultrasound PC. Auto-fills IP/port via UDP broadcast.")]
        public ServerDiscovery ultrasoundDiscovery;

        public string pcIP        = "192.168.x.x";
        public int    commandPort = 5001;

        [Header("Panel Appearance")]
        public Color panelBgColor    = new Color(0.15f, 0.15f, 0.15f, 0.92f);
        public Color buttonColor     = new Color(0.25f, 0.25f, 0.28f, 1f);
        public Color buttonHighlight = new Color(0.35f, 0.55f, 0.80f, 1f);
        public Color scaleButtonColor = new Color(0.20f, 0.45f, 0.20f, 1f);
        public Color scaleButtonHighlight = new Color(0.30f, 0.65f, 0.30f, 1f);
        public Color labelColor      = Color.white;
        public float fontSize        = 14f;

        [Header("Scale Settings")]
        public float scaleStep = 0.1f;
        public float scaleMin  = 0.3f;
        public float scaleMax  = 3.0f;

        [Header("Grab Bar Settings")]
        public float grabBarHeight = 0.05f;
        public float grabBarGap    = 0.02f;   // gap between panel bottom and bar

        GameObject m_FeedRoot;
        GameObject m_LeftPanel;
        GameObject m_RightPanel;
        float      m_QuadX  = 1.60f;
        float      m_PanelW = 0.55f;

        UdpClient  m_Udp;
        IPEndPoint m_Endpoint;

        void Awake()
        {
            m_Udp = new UdpClient();
            RefreshEndpoint();
        }

        void RefreshEndpoint()
        {
            if (ultrasoundDiscovery != null && ultrasoundDiscovery.IsDiscovered)
            {
                pcIP = ultrasoundDiscovery.DiscoveredIP;
                int discoveredPort = ultrasoundDiscovery.GetPort("control");
                if (discoveredPort > 0) commandPort = discoveredPort;
            }
            m_Endpoint = new IPEndPoint(IPAddress.Parse(pcIP), commandPort);
        }

        void OnDestroy()
        {
            m_Udp?.Close();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public void BuildControlPanels(GameObject feedRoot)
        {
            m_FeedRoot = feedRoot;

            Vector3 screenWorldPos    = feedRoot.transform.position;
            Quaternion screenWorldRot = feedRoot.transform.rotation;

            float offsetX = (m_QuadX / 2f) + (m_PanelW / 2f) + 0.02f;

            Vector3 leftWorldPos  = screenWorldPos + screenWorldRot * new Vector3(-offsetX, 0f, 0.01f);
            Vector3 rightWorldPos = screenWorldPos + screenWorldRot * new Vector3( offsetX, 0f, 0.01f);

            m_LeftPanel  = BuildLeftPanel (leftWorldPos,  screenWorldRot, m_PanelW, 1.0f);
            m_RightPanel = BuildRightPanel(rightWorldPos, screenWorldRot, m_PanelW, 1.0f);
        }

        void ScaleScreen(float delta)
        {
            if (m_FeedRoot == null) return;

            var current = m_FeedRoot.transform.localScale;
            float newScale = Mathf.Clamp(current.x + delta, scaleMin, scaleMax);
            m_FeedRoot.transform.localScale = new Vector3(newScale, newScale, newScale);
        }

        // ── Panel builders ───────────────────────────────────────────────────────

        GameObject BuildLeftPanel(Vector3 worldPos, Quaternion worldRot, float w, float h)
        {
            var panel = CreatePanel("LeftControlPanel", worldPos, worldRot, w, h);
            var layout = AddVerticalLayout(panel);

            AddScaleButtons(panel);

            AddLabel(panel, "── FOCUS ──");
            AddButtonRow(panel, "focus_dec", "< Focus", "focus_inc", "Focus >");

            AddLabel(panel, "── DEPTH ──");
            AddButtonRow(panel, "depth_dec", "< Depth", "depth_inc", "Depth >");

            AddLabel(panel, "── GAIN ──");
            AddButtonRow(panel, "gain_dec", "< Gain", "gain_inc", "Gain >");

            AddLabel(panel, "── DYN RANGE ──");
            AddButtonRow(panel, "dynrange_dec", "< DR", "dynrange_inc", "DR >");

            AddLabel(panel, "── POWER ──");
            AddButtonRow(panel, "power_dec", "< Pwr", "power_inc", "Pwr >");

            AddLabel(panel, "── FREQUENCY ──");
            AddButtonRow(panel, "freq_dec", "< Freq", "freq_inc", "Freq >");

            AddLabel(panel, "── ANGLE ──");
            AddButtonRow(panel, "angle_dec", "< Angle", "angle_inc", "Angle >");

            AddSingleButton(panel, "scan_dir", "Scan Direction");

            return panel;
        }

        GameObject BuildRightPanel(Vector3 worldPos, Quaternion worldRot, float w, float h)
        {
            var panel = CreatePanel("RightControlPanel", worldPos, worldRot, w, h);
            var layout = AddVerticalLayout(panel);

            AddLabel(panel, "── F KEYS ──");
            AddFKeyRow(panel, 1, 6);
            AddFKeyRow(panel, 7, 12);

            AddLabel(panel, "── MEASURE ──");
            AddButtonRow(panel, "distance", "Distance", "length",    "Length");
            AddButtonRow(panel, "area",     "Area",     "trace",     "Trace");
            AddButtonRow(panel, "angle",    "Angle",    "angle2",    "Angle2");
            AddButtonRow(panel, "volume",   "Volume",   "volume2",   "Vol2");
            AddButtonRow(panel, "stenosis", "Sten%",    "stenosis2", "Sten2");
            AddButtonRow(panel, "ab_ratio", "A/B",      "ab_ratio2", "A/B2");

            AddLabel(panel, "── CONTROL ──");
            AddSingleButton(panel, "freeze", "❄ FREEZE");

            AddLabel(panel, "── MODES ──");
            AddModeRow(panel, 1, 5);
            AddModeRow(panel, 6, 9);

            return panel;
        }

        // ── Scale button row ─────────────────────────────────────────────────────

        void AddScaleButtons(GameObject panel)
        {
            AddLabel(panel, "── SCREEN SIZE ──");

            var row = new GameObject("Row_Scale");
            row.transform.SetParent(panel.transform, false);
            var rt  = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 50);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 6f;
            hlg.childControlWidth   = true;
            hlg.childControlHeight  = true;
            hlg.childForceExpandWidth = true;

            CreateScaleButton(row, -scaleStep, "▼  Shrink");
            CreateScaleButton(row, +scaleStep, "▲  Grow");
        }

        void CreateScaleButton(GameObject parent, float delta, string label)
        {
            var go = new GameObject("ScaleBtn_" + label);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.color = scaleButtonColor;

            var btn    = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = scaleButtonColor;
            colors.highlightedColor = scaleButtonHighlight;
            colors.pressedColor     = new Color(0.15f, 0.50f, 0.15f, 1f);
            btn.colors = colors;

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(2, 2);
            txtRt.offsetMax = new Vector2(-2, -2);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = fontSize + 2f;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            float d = delta;
            btn.onClick.AddListener(() => ScaleScreen(d));
        }

        // ── UI helpers ───────────────────────────────────────────────────────────

        VerticalLayoutGroup AddVerticalLayout(GameObject panel)
        {
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing                = 4f;
            layout.padding                = new RectOffset(6, 6, 6, 6);
            layout.childControlHeight     = false;
            layout.childControlWidth      = true;
            layout.childForceExpandHeight = false;

            // Auto-shrink panel height to fit its actual content — fixes the big
            // empty gap at the bottom that was pushing the grab bar way too low.
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return layout;
        }

        // Panels are independent root-level objects, positioned/rotated in world
        // space, grabbable via a minimal bar pinned to the BOTTOM edge.
        //
        // Fix #1 (bar was appearing at top): RectTransform anchors are now
        // explicitly pinned to the bottom-center of the panel rect
        // (anchorMin = anchorMax = (0.5, 0)), instead of relying on
        // anchoredPosition sign with default center-anchors — that ambiguity
        // was why the bar rendered at the top.
        //
        // Fix #2 (grab snapping to panel center instead of grab point):
        // useDynamicAttach = true makes XRGrabInteractable attach at the
        // actual point the interactor touched the collider, instead of
        // snapping the object's pivot straight to the controller.
        GameObject CreatePanel(string name, Vector3 worldPos, Quaternion worldRot, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.position   = worldPos;
            go.transform.rotation   = worldRot;
            go.transform.localScale = Vector3.one;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta  = new Vector2(w * 1000f, h * 1000f);   // starting size; ContentSizeFitter shrinks height after layout
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
            var bgLayoutElement = bg.AddComponent<LayoutElement>();
            bgLayoutElement.ignoreLayout = true;

            // Grab bar — small gap now (1.5cm), and driven by the panel's actual
            // RectTransform height at grab-setup time (called after content is
            // added and ContentSizeFitter has run), not the original fixed `h`.
            float smallGap = 0.015f;
            float barWidthUI  = w * 1000f * 0.5f;
            float barHeightUI = grabBarHeight * 1000f;
            float gapUI       = smallGap * 1000f;

            var grabBarVisual = new GameObject("GrabBarVisual");
            grabBarVisual.transform.SetParent(go.transform, false);
            var barRt = grabBarVisual.AddComponent<RectTransform>();
            barRt.anchorMin        = new Vector2(0.5f, 0f);
            barRt.anchorMax        = new Vector2(0.5f, 0f);
            barRt.pivot            = new Vector2(0.5f, 1f);
            barRt.sizeDelta        = new Vector2(barWidthUI, barHeightUI);
            barRt.anchoredPosition = new Vector2(0f, -gapUI);
            var barImg = grabBarVisual.AddComponent<Image>();
            barImg.color = new Color(0.5f, 0.5f, 0.55f, 0.85f);
            var barLayoutElement = grabBarVisual.AddComponent<LayoutElement>();
            barLayoutElement.ignoreLayout = true;

            // Grab collider — attached to a script that re-reads the panel's actual
            // height every frame for its first few frames (until ContentSizeFitter
            // settles), so it tracks the real shrunk panel instead of the original `h`.
            var grabHandle = new GameObject(name + "_GrabHandle");
            grabHandle.transform.SetParent(go.transform, false);
            grabHandle.transform.localPosition = Vector3.zero;
            grabHandle.transform.localRotation = Quaternion.identity;
            grabHandle.transform.localScale    = new Vector3(1000f, 1000f, 1000f);

            var box = grabHandle.AddComponent<BoxCollider>();
            box.size = new Vector3(w * 0.5f, grabBarHeight, 0.05f);
            // Positioned by PanelGrabBarTracker after layout settles (see below)

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity  = false;
            rb.isKinematic = true;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(box);
            grab.throwOnDetach    = false;
            grab.trackPosition    = true;
            grab.trackRotation    = true;
            grab.trackScale       = false;
            grab.movementType     = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.attachEaseInTime = 0f;

            // Runs for a couple frames after creation to snap the bar/collider to the
            // panel's real post-layout height, then stops (panel height is static
            // after that — content doesn't change at runtime).
            var tracker = go.AddComponent<PanelGrabBarTracker>();
            tracker.panelRect   = rt;
            tracker.barRect     = barRt;
            tracker.grabBox     = box;
            tracker.gapWorld    = smallGap;
            tracker.barHeightWorld = grabBarHeight;

            return go;
        }
        void AddLabel(GameObject panel, string text)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(panel.transform, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 28);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize - 2f;
            tmp.color     = new Color(0.7f, 0.7f, 0.7f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        void AddButtonRow(GameObject panel, string cmd1, string label1, string cmd2, string label2)
        {
            var row = new GameObject("Row_" + cmd1);
            row.transform.SetParent(panel.transform, false);
            var rt  = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 42);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 4f;
            hlg.childControlWidth   = true;
            hlg.childControlHeight  = true;
            hlg.childForceExpandWidth = true;
            CreateButton(row, cmd1, label1);
            CreateButton(row, cmd2, label2);
        }

        void AddSingleButton(GameObject panel, string cmd, string label)
        {
            var row = new GameObject("Row_" + cmd);
            row.transform.SetParent(panel.transform, false);
            var rt  = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 42);
            CreateButton(row, cmd, label);
        }

        void AddFKeyRow(GameObject panel, int from, int to)
        {
            var row = new GameObject($"FKeyRow_{from}_{to}");
            row.transform.SetParent(panel.transform, false);
            var rt  = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 42);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 3f;
            hlg.childControlWidth   = true;
            hlg.childControlHeight  = true;
            hlg.childForceExpandWidth = true;
            for (int i = from; i <= to; i++)
                CreateButton(row, $"f{i}", $"F{i}");
        }

        void AddModeRow(GameObject panel, int from, int to)
        {
            var row = new GameObject($"ModeRow_{from}_{to}");
            row.transform.SetParent(panel.transform, false);
            var rt  = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 42);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 3f;
            hlg.childControlWidth   = true;
            hlg.childControlHeight  = true;
            hlg.childForceExpandWidth = true;
            for (int i = from; i <= to; i++)
                CreateButton(row, $"mode{i}", $"{i}");
        }

        void CreateButton(GameObject parent, string command, string label)
        {
            var go = new GameObject("Btn_" + command);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();

            var img   = go.AddComponent<Image>();
            img.color = buttonColor;

            var btn    = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = buttonColor;
            colors.highlightedColor = buttonHighlight;
            colors.pressedColor     = new Color(0.15f, 0.35f, 0.60f, 1f);
            btn.colors = colors;

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(2, 2);
            txtRt.offsetMax = new Vector2(-2, -2);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = fontSize;
            tmp.color     = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;

            string cmd = command;
            btn.onClick.AddListener(() =>
            {
                Debug.Log($"[BUTTON CLICKED] {cmd}");
                SendCommand(cmd);
            });
        }

        // ── Network ──────────────────────────────────────────────────────────────

        void SendCommand(string command)
        {
            try
            {
                RefreshEndpoint();
                byte[] data = Encoding.UTF8.GetBytes(command);
                m_Udp.Send(data, data.Length, m_Endpoint);
                Debug.Log($"[MicrusControlPanel] Sent command: {command}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MicrusControlPanel] Failed to send '{command}': {e.Message}");
            }
        }
    }
}