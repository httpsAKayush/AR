using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MetaXR.LofiStudy.ARFoundation
{
    /// <summary>
    /// Singleton that receives JPEG frames from a PC server over TCP and exposes
    /// them as a live Texture2D. Any number of CameraFeedMaterialUpdater instances
    /// on placed prefabs can sample this texture without knowing about networking.
    ///
    /// Network protocol (matches the Python sender):
    ///   [4 bytes big-endian frame size] [N bytes JPEG data]
    ///
    /// Inspector tip:
    ///   - For mobile hotspot testing set ServerIP to the PC's hotspot-assigned IP.
    ///   - For LAN testing later, just change ServerIP to the PC's LAN IP. Nothing else changes.
    /// </summary>
    public class CameraFeedReceiver : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        public static CameraFeedReceiver Instance { get; private set; }

        // ── Inspector fields ─────────────────────────────────────────────────────
        [Header("Network")]
        [Tooltip("Discovery component for the ultrasound PC. Auto-fills IP/port via UDP broadcast.")]
        public ServerDiscovery ultrasoundDiscovery;

        [Tooltip("Fallback IP if discovery hasn't found the server yet (used only on first attempt).")]
        public string serverIP   = "192.168.x.x";
        public int    serverPort = 5000;

        [Tooltip("Seconds to wait before retrying a failed connection.")]
        public float  reconnectDelaySec = 2f;

        [Header("Texture")]
        [Tooltip("Initial texture dimensions. LoadImage will resize automatically.")]
        public int initialWidth  = 1280;
        public int initialHeight = 720;

        [Header("Status (read-only)")]
        public bool  isConnected    = false;
        public int   framesReceived = 0;

        // ── Public live texture ──────────────────────────────────────────────────
        /// <summary>
        /// The latest decoded camera frame. Assign this to any renderer material
        /// or RawImage. Updated on the main thread every frame new data arrives.
        /// </summary>
        public Texture2D LiveTexture { get; private set; }

        // ── Private state ────────────────────────────────────────────────────────
        TcpClient     m_Client;
        NetworkStream m_Stream;
        Thread        m_Thread;
        byte[]        m_PendingFrame;
        bool          m_NewFrameReady;
        bool          m_Running;

        // ── Unity lifecycle ──────────────────────────────────────────────────────
        void Awake()
        {
            // Enforce singleton
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject); // survives scene loads if you ever add more scenes

            LiveTexture = new Texture2D(initialWidth, initialHeight, TextureFormat.RGB24, true);
            LiveTexture.filterMode  = FilterMode.Trilinear;
            LiveTexture.anisoLevel  = 8;
        }

        void Start()
        {
            m_Running = true;
            m_Thread  = new Thread(ConnectionLoop) { IsBackground = true };
            m_Thread.Start();
        }

        
        void Update()
        {
            if (!m_NewFrameReady || m_PendingFrame == null)
                return;

            m_NewFrameReady = false;
            LiveTexture.LoadImage(m_PendingFrame);
            LiveTexture.Apply(true); // generates mipmaps — fixes distance flickering
            framesReceived++;
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void OnApplicationQuit()
        {
            Shutdown();
        }

        // ── Networking (background thread) ───────────────────────────────────────
        void ConnectionLoop()
        {
            while (m_Running)
            {
                //
                while (m_Running &&
                    (ultrasoundDiscovery == null || !ultrasoundDiscovery.IsDiscovered))
                {
                    // Debug.Log("[CameraFeedReceiver] Waiting for server discovery...");
                    Thread.Sleep(500);
                }

                if (!m_Running)
                    break;

                //

                try
                {
                    // Pull latest discovered IP/port if available
                    // if (ultrasoundDiscovery != null && ultrasoundDiscovery.IsDiscovered)
                    // {
                    //     serverIP   = ultrasoundDiscovery.DiscoveredIP;
                    //     int discoveredPort = ultrasoundDiscovery.GetPort("video");
                    //     if (discoveredPort > 0) serverPort = discoveredPort;
                    // }
                    serverIP = ultrasoundDiscovery.DiscoveredIP;
                    int discoveredPort = ultrasoundDiscovery.GetPort("video");

                    Debug.Log($"DISCOVERED IP = {serverIP}");
                    Debug.Log($"DISCOVERED PORT = {discoveredPort}");

                    if (discoveredPort > 0)
                        serverPort = discoveredPort;

                    Debug.Log($"[CameraFeedReceiver] Connecting to {serverIP}:{serverPort}…");

                    m_Client = new TcpClient();
                    m_Client.Connect(serverIP, serverPort);
                    m_Stream     = m_Client.GetStream();
                    isConnected  = true;

                    Debug.Log("[CameraFeedReceiver] Connected.");
                    ReceiveLoop();
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    isConnected = false;
                    Debug.LogWarning($"[CameraFeedReceiver] {ex.Message} — retrying in {reconnectDelaySec}s…");
                    CleanupSocket();
                    Thread.Sleep(TimeSpan.FromSeconds(reconnectDelaySec));
                }
            }
        }

        void ReceiveLoop()
        {
            var header = new byte[4];

            while (m_Running)
            {
                // 1. Read 4-byte big-endian frame size
                ReadExact(header, 4);

                int size = (header[0] << 24) |
                           (header[1] << 16) |
                           (header[2] <<  8) |
                            header[3];

                if (size <= 0 || size > 10_000_000) // 10 MB sanity cap
                {
                    Debug.LogWarning($"[CameraFeedReceiver] Suspicious frame size {size} — skipping.");
                    continue;
                }

                // 2. Read full JPEG frame
                var frame = new byte[size];
                ReadExact(frame, size);

                // Hand off to main thread (simple double-buffer: last frame wins)
                m_PendingFrame  = frame;
                m_NewFrameReady = true;
            }
        }

        void ReadExact(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = m_Stream.Read(buffer, total, count - total);
                if (read == 0)
                    throw new Exception("Server closed connection.");
                total += read;
            }
        }

        void CleanupSocket()
        {
            try { m_Stream?.Close(); } catch { /* ignore */ }
            try { m_Client?.Close(); } catch { /* ignore */ }
            m_Stream = null;
            m_Client = null;
        }

        void Shutdown()
        {
            m_Running = false;
            CleanupSocket();
            m_Thread?.Abort();
        }
    }
}