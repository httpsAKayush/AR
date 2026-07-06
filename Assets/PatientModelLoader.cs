using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

public class PatientModelLoader : MonoBehaviour
{
    [Header("Server Connection")]
    public string serverIP = "";
    public int tcpPort = 5012;

    [Header("Scene")]
    public Transform spawnParent;   // where the loaded model will be placed
    public Camera arCamera;

    [Header("UI Feedback")]
    public TMPro.TextMeshProUGUI statusText;  // optional, for showing status

    [Header("Anatomy Systems")]
    public AnatomyController anatomyController;
    public TransformGizmo transformGizmo;

    private GameObject currentModel;
    private string lastPatientId;
    private float lastConfidence;

    // ── Discovery ─────────────────────────────────────────────────────────────
    private const int BroadcastPort = 5013;
    private UdpClient discoveryClient;
    private Thread discoveryThread;
    private volatile bool discoveryRunning = false;
    private volatile string discoveredIP = null;
    private volatile string pendingStatus = null;

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
        if (string.IsNullOrEmpty(discoveredIP))
        {
            SetStatus("No server found yet. Waiting...");
            return;
        }

        SetStatus($"Connecting to {serverIP}...");

        try
        {
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
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        var gltfImport = new GltfImport();
        bool success = await gltfImport.LoadGltfBinary(glbData);

        if (!success)
        {
            throw new Exception("Failed to parse GLB data");
        }

        GameObject root = new GameObject($"PatientModel_{lastPatientId}");
        root.transform.SetParent(spawnParent != null ? spawnParent : transform);

        var instantiator = new GameObjectInstantiator(gltfImport, root.transform);
        success = await gltfImport.InstantiateMainSceneAsync(instantiator);

        if (!success)
        {
            throw new Exception("Failed to instantiate GLB scene");
        }

        currentModel = root;

        if (arCamera != null)
        {
            Vector3 spawnPos = arCamera.transform.position +
                               arCamera.transform.forward * 1.5f;
            spawnPos.y = arCamera.transform.position.y - 0.5f;
            root.transform.position = spawnPos;
        }
        // Wire anatomy systems to the newly loaded model
        if (anatomyController != null)
        {
            anatomyController.bodyRoot = root.transform;
            anatomyController.enabled = true;
        }
        if (transformGizmo != null)
        {
            transformGizmo.bodyRoot = root.transform;
            transformGizmo.enabled = true;
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