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

    private void FixMaterialsForQuest(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");

        if (urpLit == null)
        {
            Debug.LogError("URP/Lit shader not found — check URP is installed correctly");
            return;
        }

        var propBlock = new MaterialPropertyBlock();

        foreach (var renderer in renderers)
        {
            var mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];

                Color baseColor = Color.white;
                string[] colorPropNames = { "baseColorFactor", "_BaseColor", "_Color", "BaseColor" };
                foreach (var propName in colorPropNames)
                {
                    if (mat.HasProperty(propName))
                    {
                        baseColor = mat.GetColor(propName);
                        break;
                    }
                }

                Texture baseTex = null;
                string[] texPropNames = { "baseColorTexture", "_BaseMap", "_MainTex" };
                foreach (var propName in texPropNames)
                {
                    if (mat.HasProperty(propName))
                    {
                        baseTex = mat.GetTexture(propName);
                        if (baseTex != null) break;
                    }
                }

                mat.shader = urpLit;
                mat.enableInstancing = false;   // force unique draw, avoid batching collapsing colors
                mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                mat.SetColor("_BaseColor", baseColor);
                if (baseTex != null)
                    mat.SetTexture("_BaseMap", baseTex);

                // Belt-and-suspenders: also push via MaterialPropertyBlock per-renderer
                renderer.GetPropertyBlock(propBlock, i);
                propBlock.SetColor("_BaseColor", baseColor);
                renderer.SetPropertyBlock(propBlock, i);
            }
            renderer.materials = mats;
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