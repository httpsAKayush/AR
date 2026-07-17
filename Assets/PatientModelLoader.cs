using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;
using GLTFast.Logging;
using MetaXR.LofiStudy.ARFoundation;

public class PatientModelLoader : MonoBehaviour
{
    [Header("Server Connection")]
    public string serverIP = "";
    public int tcpPort = 5012;

    [Header("Scene")]
    public Transform spawnParent;   // where the loaded model will be placed

    [Header("Spawn Position")]
    private float spawnDistance  = 0.69f;
    private float verticalOffset = 0f;

    [Header("References")]
    public Transform cameraTransform;

    [Header("UI Feedback")]
    public TMPro.TextMeshProUGUI statusText;  // optional, for showing status

    [Header("Interaction (optional)")]
    public TransformGizmo transformGizmo;
    public AnatomyController anatomyController;

    [Header("Layer Panel (optional)")]
    public LayerControlPanel layerControlPanel;

    private GameObject currentModel;
    private string lastPatientId;
    private float lastConfidence;
    private bool isMatching = false;

    // ── Discovery ─────────────────────────────────────────────────────────────
    private const int BroadcastPort = 5013;
    private UdpClient discoveryClient;
    private Thread discoveryThread;
    private volatile bool discoveryRunning = false;
    private volatile string discoveredIP = null;
    private volatile string pendingStatus = null;

    private GltfImport currentGltfImport;

    void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Start()
    {
        StartServerDiscovery();
    }

    void Update()
    {
        // Apply any status update queued from the background discovery thread
        if (pendingStatus != null)
        {
            SetStatus(pendingStatus);
            pendingStatus = null;
        }
    }

    void OnDestroy()
    {
        if (currentGltfImport != null)
        {
            currentGltfImport.Dispose();
            currentGltfImport = null;
        }

        StopServerDiscovery();
    }

    // ── Spawn positioning (same pattern as CameraFeedSpawner) ───────────────

    private void PositionSpawnParentInFrontOfCamera()
    {
        if (spawnParent == null || cameraTransform == null) return;

        var forward = cameraTransform.forward;
        forward.y   = 0;
        forward.Normalize();

        var spawnPosition = cameraTransform.position
                            + forward * spawnDistance
                            + Vector3.up * verticalOffset;

        var lookDir       = spawnPosition - cameraTransform.position;
        lookDir.y         = 0;
        var spawnRotation = lookDir.sqrMagnitude > 0.001f
                            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f)
                            : Quaternion.identity;

        spawnParent.position = spawnPosition;
        spawnParent.rotation = spawnRotation;
    }

    private void StartServerDiscovery()
    {
        discoveryRunning = true;
        discoveryThread = new Thread(DiscoveryLoop);
        discoveryThread.IsBackground = true;
        discoveryThread.Start();
        SetStatus("Searching for server...");
    }

    private void DiscoveryLoop()
    {
        const string MulticastGroup = "239.255.42.42";
        try
        {
            discoveryClient = new UdpClient();
            discoveryClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);
            discoveryClient.Client.Bind(
                new IPEndPoint(IPAddress.Any, BroadcastPort));
            discoveryClient.JoinMulticastGroup(
                IPAddress.Parse(MulticastGroup));
            discoveryClient.Client.ReceiveTimeout = 5010;

            while (discoveryRunning)
            {
                try
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = discoveryClient.Receive(ref remoteEP);
                    string json = Encoding.UTF8.GetString(data);

                    var info = JsonUtility.FromJson<ServerAnnouncement>(json);
                    if (info != null && info.service == "ct_pipeline_server")
                    {
                        bool firstDiscovery = string.IsNullOrEmpty(discoveredIP);
                        discoveredIP = info.ip;
                        serverIP = info.ip;
                        tcpPort = info.tcp_port;

                        if (firstDiscovery)
                            pendingStatus = $"Server found: {serverIP}";

                        // Stop discovery — we have what we need
                        discoveryRunning = false;
                        break;
                    }
                }
                catch (SocketException)
                {
                    // timeout, loop again
                }
            }

            discoveryClient.DropMulticastGroup(IPAddress.Parse(MulticastGroup));
        }
        catch (Exception e)
        {
            pendingStatus = $"Discovery error: {e.Message}";
        }
        finally
        {
            discoveryClient?.Close();
        }
    }

    private void StopServerDiscovery()
    {
        discoveryRunning = false;
        discoveryClient?.Close();
    }

    [Serializable]
    private class ServerAnnouncement
    {
        public string service;
        public string ip;
        public int tcp_port;
    }

    // ── Matching ─────────────────────────────────────────────────────────────

    public async void OnMatchButtonPressed()
    {
        if (isMatching)
        {
            SetStatus("Already matching, please wait...");
            return;
        }
        isMatching = true;

        PositionSpawnParentInFrontOfCamera();

        if (currentGltfImport != null)
        {
            currentGltfImport.Dispose();
            currentGltfImport = null;
        }

        if (currentModel != null)
        {
            Destroy(currentModel);
            currentModel = null;
        }

        try
        {
            if (string.IsNullOrEmpty(discoveredIP))
            {
                SetStatus("No server found yet. Waiting...");
                return;
            }

            SetStatus($"Connecting to {serverIP}...");
            SetStatus("Requesting match...");
            byte[] glbData = await SendMatchRequestAndReceiveGlb();

            if (glbData == null || glbData.Length == 0)
            {
                SetStatus("Match failed: no data received");
                return;
            }

            SetStatus($"Received {glbData.Length} bytes, loading...");
            await LoadModelFromBytes(glbData);

            SetStatus($"Loaded: {lastPatientId} ({lastConfidence}%)");
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
            Debug.LogError($"PatientModelLoader error: {e}");
        }
        finally
        {
            isMatching = false;
        }
    }

    private async Task<byte[]> SendMatchRequestAndReceiveGlb()
    {
        using (TcpClient client = new TcpClient())
        {
            await client.ConnectAsync(serverIP, tcpPort);
            NetworkStream stream = client.GetStream();

            // Send request
            string request = "{\"command\":\"match\"}";
            byte[] requestBytes = Encoding.UTF8.GetBytes(request);
            await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

            // Read header line (JSON terminated by \n)
            StringBuilder headerBuilder = new StringBuilder();
            byte[] singleByte = new byte[1];
            while (true)
            {
                int n = await stream.ReadAsync(singleByte, 0, 1);
                if (n == 0) break;
                char c = (char)singleByte[0];
                if (c == '\n') break;
                headerBuilder.Append(c);
            }

            string headerJson = headerBuilder.ToString();
            Debug.Log($"Header: {headerJson}");
            var header = JsonUtility.FromJson<MatchHeader>(headerJson);

            if (header.status != "ok")
            {
                throw new Exception($"Server error: {headerJson}");
            }

            lastPatientId = header.patient_id;
            lastConfidence = header.confidence;

            // Read exact glb_size bytes
            byte[] glbData = new byte[header.glb_size];
            int totalRead = 0;
            while (totalRead < header.glb_size)
            {
                int read = await stream.ReadAsync(glbData, totalRead, header.glb_size - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            Debug.Log($"Received {totalRead}/{header.glb_size} bytes for {header.patient_id}");
            return glbData;
        }
    }

    private async Task LoadModelFromBytes(byte[] glbData)
    {
        var logger = new ConsoleLogger();
        var gltfImport = new GltfImport(logger: logger);

        // uri param optional agar GLB self-contained hai (embedded textures)
        bool success = await gltfImport.Load(glbData);

        if (!success)
            throw new Exception("Failed to parse GLB data");

        GameObject root = new GameObject($"PatientModel_{lastPatientId}");
        root.transform.SetParent(spawnParent != null ? spawnParent : transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        var instantiator = new GameObjectInstantiator(gltfImport, root.transform, logger: logger);
        success = await gltfImport.InstantiateMainSceneAsync(instantiator);

        if (!success)
            throw new Exception("Failed to instantiate GLB scene");

        // TEMPORARY DEBUG — remove after finding correct property name
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in renderer.materials)
            {
                Debug.Log($"=== Material: {mat.name} | Shader: {mat.shader.name} ===");
                Shader shader = mat.shader;
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    var propType = shader.GetPropertyType(i);
                    string propName = shader.GetPropertyName(i);
                    if (propType == UnityEngine.Rendering.ShaderPropertyType.Color)
                    {
                        Debug.Log($"  COLOR PROPERTY: '{propName}' = {mat.GetColor(propName)}");
                    }
                }
            }
        }

        FixMaterialsForQuest(root);
        //
        if (layerControlPanel != null)
            layerControlPanel.BuildLayerPanel(root.transform);

        //
        if (transformGizmo != null)
            transformGizmo.SetTarget(root.transform);

        if (anatomyController != null)
            anatomyController.SetBodyRoot(root.transform);
        //

        currentModel = root;
        currentGltfImport = gltfImport;
    }

    // Mirrors ct_pipeline/model/mesh_export.py's ORGAN_COLORS + MERGE_GROUPS
    // overrides, and raw_export.py's SKIN_COLOR — kept as an ordered list
    // (not Dictionary) so lookup order exactly matches Python's dict
    // iteration order, since get_color() there returns the FIRST key found
    // as a substring.
    private static readonly (string key, Color32 color)[] OrganColors = new (string, Color32)[]
    {
        ("liver",                        new Color32(194, 100,  60, 180)),
        ("spleen",                       new Color32(160,  60, 120, 180)),
        ("kidney_left",                  new Color32(210, 140,  80, 180)),
        ("kidney_right",                 new Color32(210, 140,  80, 180)),
        ("pancreas",                     new Color32(220, 180, 100, 180)),
        ("stomach",                      new Color32(180, 160, 120, 180)),
        ("gallbladder",                  new Color32(180, 200,  80, 180)),
        ("heart",                        new Color32(200,  60,  60, 200)),
        ("small_bowel",                  new Color32(200, 160, 140, 160)),
        ("colon",                        new Color32(180, 130, 100, 160)),
        ("duodenum",                     new Color32(190, 150, 110, 160)),
        ("urinary_bladder",              new Color32(100, 160, 200, 160)),
        ("esophagus",                    new Color32(160, 100, 100, 160)),
        ("trachea",                      new Color32(140, 180, 200, 160)),
        ("spinal_cord",                  new Color32(220, 220, 160, 180)),
        ("lung_upper_lobe_left",         new Color32(140, 180, 220, 150)),
        ("lung_lower_lobe_left",         new Color32(140, 180, 220, 150)),
        ("lung_upper_lobe_right",        new Color32(140, 180, 220, 150)),
        ("lung_lower_lobe_right",        new Color32(140, 180, 220, 150)),
        ("lung_middle_lobe_right",       new Color32(140, 180, 220, 150)),
        ("aorta",                        new Color32(220,  60,  60, 200)),
        ("inferior_vena_cava",           new Color32( 60,  60, 220, 200)),
        ("portal_vein_and_splenic_vein", new Color32( 80, 100, 200, 180)),
        ("pulmonary_vein",               new Color32(100, 100, 220, 180)),
        ("adrenal_gland_left",           new Color32(180, 200, 140, 160)),
        ("adrenal_gland_right",          new Color32(180, 200, 140, 160)),
        ("skeleton",                     new Color32(220, 210, 180, 200)),
        ("left_lung",                    new Color32(140, 180, 220, 150)),
        ("right_lung",                   new Color32(140, 180, 220, 150)),
        ("muscles",                      new Color32(180, 120, 100, 140)),
    };

    // raw_export.py's SKIN_COLOR — not in ORGAN_COLORS on the Python side
    // (separate constant there too), matched here by the fixed node name
    // merge_export.py always gives it: "raw_body_surface".
    private static readonly Color32 RawSurfaceColor = new Color32(210, 180, 140, 200);

    // Python's ORGAN_COLORS["__default__"]
    private static readonly Color32 DefaultOrganColor = new Color32(200, 200, 200, 160);

    /// <summary>
    /// Mirrors mesh_export.py's get_color(): first substring match wins.
    /// Walks up from the renderer's own GameObject to the model root, since
    /// glTFast sometimes puts the mesh on a child (e.g. "Primitive0") under
    /// a parent named after the real node ("liver") — checking ancestors
    /// finds the real name even when the renderer's own GameObject doesn't.
    /// Returns null if nothing matched, so the caller can fall back to a
    /// distinct-per-layer color instead of one flat default.
    /// </summary>
    private static Color32? GetColorForRenderer(Renderer renderer, Transform root)
    {
        Transform t = renderer.transform;
        while (t != null)
        {
            string lower = t.name.ToLowerInvariant();

            if (lower.Contains("raw_body_surface") || lower.Contains("raw_surface"))
                return RawSurfaceColor;

            foreach (var (key, color) in OrganColors)
            {
                if (lower.Contains(key))
                    return color;
            }

            if (t == root) break;
            t = t.parent;
        }
        return null;
    }

    /// <summary>
    /// Deterministic, visually distinct color for layers with no name match —
    /// golden-ratio hue stepping so consecutive indices land far apart on the
    /// color wheel instead of clustering.
    /// </summary>
    private static Color32 GetFallbackColor(int index)
    {
        const float goldenRatioConjugate = 0.61803398875f;
        float hue = (index * goldenRatioConjugate) % 1f;
        Color c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        return new Color32(
            (byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 220);
    }

    private void FixMaterialsForQuest(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");

        if (urpLit == null)
        {
            Debug.LogError("URP/Lit shader not found — check URP is installed correctly");
            return;
        }

        for (int r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];

            // TEMP DEBUG — prints the full ancestor chain checked for a
            // match, so if colors still look wrong you can see the actual
            // GameObject names glTFast produced and tell me what they are.
            {
                var names = new System.Collections.Generic.List<string>();
                Transform dt = renderer.transform;
                while (dt != null) { names.Add(dt.name); if (dt == root.transform) break; dt = dt.parent; }
                Debug.Log($"[FixMaterialsForQuest] renderer #{r} hierarchy: {string.Join(" < ", names)}");
            }

            Color32? matched = GetColorForRenderer(renderer, root.transform);
            Color32 organColor32 = matched ?? GetFallbackColor(r);
            if (matched == null)
                Debug.Log($"[FixMaterialsForQuest] renderer #{r} — no name match, using fallback color {organColor32}");

            Color organColor = new Color(
                organColor32.r / 255f, organColor32.g / 255f,
                organColor32.b / 255f, organColor32.a / 255f);

            var mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                mat.shader = urpLit;
                mat.enableInstancing = false;
                mat.SetColor("_BaseColor", organColor);

                // Colors carry alpha < 255 on purpose (semi-transparent
                // anatomy viewing) — URP Lit defaults to Opaque, which
                // ignores alpha unless surface type is set to Transparent.
                if (organColor32.a < 255)
                {
                    mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
                    mat.SetFloat("_Blend", 0f);   // 0 = Alpha blend
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
            renderer.materials = mats;   // each renderer gets its own unique material instances — no shared/batched color
        }
    }

    private void SetStatus(string msg)
    {
        Debug.Log($"[PatientModelLoader] {msg}");
        if (statusText != null)
        {
            statusText.text = msg;
        }
    }

    [Serializable]
    private class MatchHeader
    {
        public string status;
        public string patient_id;
        public float confidence;
        public bool fallback;
        public int glb_size;
    }
}