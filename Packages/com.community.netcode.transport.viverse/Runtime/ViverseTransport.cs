using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Netcode.Transports.Viverse
{
    /// <summary>
    /// Connection data for Viverse transport initialization
    /// </summary>
    [System.Serializable]
    public struct ViverseConnectionData
    {
        /// <summary>
        /// Viverse application ID
        /// </summary>
        public string AppId;
        
        /// <summary>
        /// Room ID for the session
        /// </summary>
        public string RoomId;
        
        /// <summary>
        /// Whether this instance is the host
        /// </summary>
        public bool IsHost;
        
        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectTimeoutMS;
        
        /// <summary>
        /// Maximum number of connection attempts
        /// </summary>
        public int MaxConnectionAttempts;
        
        /// <summary>
        /// Whether to enable debug logging
        /// </summary>
        public bool EnableDebugLogging;
    }

    /// <summary>
    /// Unity Netcode transport for Viverse WebRTC P2P networking
    /// Custom NetworkTransport implementation using Unity Transport with Viverse WebRTC driver
    /// </summary>
    [DisallowMultipleComponent]
    public class ViverseTransport : NetworkTransport
    {
        [Header("Viverse Configuration")]
        [SerializeField] private string m_ViverseAppId = "your_viverse_app_id";
        [SerializeField] private string m_ViverseRoomId = "default_room";
        [SerializeField] private int m_ViverseConnectTimeoutMS = 10000;
        [SerializeField] private int m_ViverseMaxConnectionAttempts = 3;
        [SerializeField] private bool m_ViverseEnableDebugLogging = true;

        [SerializeField] private bool m_IsHost = false;
        [SerializeField] private ViverseConnectionData m_ConnectionData;

        /// <summary>
        /// Event triggered when Viverse connection is established
        /// </summary>
        public event System.Action OnViverseConnected;
        
        /// <summary>
        /// Event triggered when Viverse connection is lost
        /// </summary>
        public event System.Action OnViverseDisconnected;

        /// <summary>
        /// Whether Viverse transport is currently connected
        /// </summary>
        public bool IsViverseConnected { get; private set; }

        /// <summary>
        /// Current room ID
        /// </summary>
        public string CurrentRoomId => m_ConnectionData.RoomId;

        /// <summary>
        /// Whether this instance is the host
        /// </summary>
        public bool IsHost => m_ConnectionData.IsHost;

        void Awake()
        {
            // Initialize connection data with serialized values
            m_ConnectionData = new ViverseConnectionData
            {
                AppId = m_ViverseAppId,
                RoomId = m_ViverseRoomId,
                IsHost = m_IsHost,
                ConnectTimeoutMS = m_ViverseConnectTimeoutMS,
                MaxConnectionAttempts = m_ViverseMaxConnectionAttempts,
                EnableDebugLogging = m_ViverseEnableDebugLogging
            };

            Debug.Log($"[ViverseTransport] Initialized with App ID: {m_ViverseAppId}, Room: {m_ViverseRoomId}");
            
            // Initialize Viverse connection manager early
            ViverseConnectionManager.Initialize();
        }

        void Start()
        {
            // Ensure ViverseBroadcastReceiver exists early for WebGL
#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureViverseBroadcastReceiver();
#endif
            
            // Start connection management updates
            InvokeRepeating(nameof(UpdateConnectionManagement), 1.0f, 1.0f);
        }
        
        /// <summary>
        /// Ensure ViverseBroadcastReceiver GameObject exists
        /// </summary>
        private void EnsureViverseBroadcastReceiver()
        {
            var receiverGO = GameObject.Find("ViverseBroadcastReceiver");
            if (receiverGO == null)
            {
                receiverGO = new GameObject("ViverseBroadcastReceiver");
                GameObject.DontDestroyOnLoad(receiverGO);
                
                var receiver = receiverGO.AddComponent<ViverseBroadcastReceiver>();
                Debug.Log("[ViverseTransport] Created ViverseBroadcastReceiver GameObject");
            }
        }

        void OnDestroy()
        {
            // Stop connection management updates
            CancelInvoke(nameof(UpdateConnectionManagement));
            
            // Cleanup Viverse resources
            ViverseConnectionManager.Cleanup();
        }
        
        /// <summary>
        /// Update connection management (called periodically)
        /// </summary>
        private void UpdateConnectionManagement()
        {
            ViverseNetworkInterface.UpdateConnectionManagement();
        }
        
        void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Update the network driver for WebGL P2P
            if (m_NetworkDriver.IsCreated)
            {
                m_NetworkDriver.ScheduleUpdate().Complete();
            }
#endif
        }
        
        // Internal Unity Transport instance for handling the actual networking
        private UnityTransport m_InternalTransport;
        private NetworkDriver m_NetworkDriver;
        private NetworkPipeline m_ReliablePipeline;
        private NetworkPipeline m_UnreliablePipeline;
        
        /// <summary>
        /// Server client ID for this transport
        /// </summary>
        public override ulong ServerClientId => 0;

        /// <summary>
        /// Initialize the transport
        /// </summary>
        public override void Initialize(NetworkManager networkManager = null)
        {
            Debug.Log("[ViverseTransport] Initializing Viverse transport");
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, skip internal UnityTransport creation
            Debug.Log("[ViverseTransport] Initializing for WebGL P2P mode");
#else
            // Create internal Unity Transport instance for non-WebGL platforms
            var transportGO = new GameObject("ViverseTransport_Internal");
            transportGO.transform.SetParent(transform);
            m_InternalTransport = transportGO.AddComponent<UnityTransport>();
            
            // Configure the internal transport with dummy data
            m_InternalTransport.SetConnectionData("127.0.0.1", 7777);
            m_InternalTransport.Initialize(networkManager);
#endif
            
            // Create Viverse network driver
            CreateViverseDriver();
        }
        
        /// <summary>
        /// Create the Viverse network driver
        /// </summary>
        private void CreateViverseDriver()
        {
            var parameters = new ViverseNetworkParameter
            {
                AppId = new Unity.Collections.FixedString64Bytes(m_ConnectionData.AppId),
                RoomId = new Unity.Collections.FixedString64Bytes(m_ConnectionData.RoomId),
                IsHost = (byte)(m_ConnectionData.IsHost ? 1 : 0),
                ConnectTimeoutMS = m_ConnectionData.ConnectTimeoutMS,
                MaxConnectionAttempts = m_ConnectionData.MaxConnectionAttempts,
                EnableDebugLogging = (byte)(m_ConnectionData.EnableDebugLogging ? 1 : 0)
            };
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, create driver directly using Viverse interface (no UnityTransport dependency)
            CreateViverseDriverForWebGL(parameters);
#else
            // For other platforms, use the hybrid approach with UnityTransport
            var driverConstructor = new ViverseNetworkDriver(parameters);
            driverConstructor.CreateDriver(
                m_InternalTransport,
                out m_NetworkDriver,
                out var unreliableFragmentedPipeline,
                out var unreliableSequencedFragmentedPipeline,
                out m_ReliablePipeline);
                
            m_UnreliablePipeline = unreliableFragmentedPipeline;
#endif
            
            Debug.Log($"[ViverseTransport] Created Viverse network driver for room {m_ConnectionData.RoomId}");
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Create Viverse driver specifically for WebGL builds
        /// </summary>
        private void CreateViverseDriverForWebGL(ViverseNetworkParameter parameters)
        {
            // Create network settings with Viverse parameters
            var settings = new Unity.Networking.Transport.NetworkSettings(Unity.Collections.Allocator.Temp);
            settings.WithViverseParameters(
                parameters.AppId.ToString(),
                parameters.RoomId.ToString(),
                parameters.IsHost == 1,
                parameters.ConnectTimeoutMS,
                parameters.MaxConnectionAttempts,
                parameters.EnableDebugLogging == 1);

            // Create Viverse network interface directly
            var networkInterface = new ViverseNetworkInterface();
            networkInterface.InitializeInternal(parameters);
            
            // Still need to call the interface Initialize for Unity Transport compatibility
            int packetPadding = 0;
            networkInterface.Initialize(ref settings, ref packetPadding);

            // Create driver with Viverse interface (WebGL-compatible)
            m_NetworkDriver = Unity.Networking.Transport.NetworkDriver.Create(networkInterface, settings);

            // Setup pipelines manually for WebGL
            SetupPipelinesForWebGL();
            
            Debug.Log("[ViverseTransport] Created WebGL-specific Viverse driver");
        }

        /// <summary>
        /// Setup pipelines for WebGL builds
        /// </summary>
        private void SetupPipelinesForWebGL()
        {
            // Create standard UTP pipelines for WebGL
            m_UnreliablePipeline = m_NetworkDriver.CreatePipeline(
                typeof(Unity.Networking.Transport.FragmentationPipelineStage)
            );

            var unreliableSequencedPipeline = m_NetworkDriver.CreatePipeline(
                typeof(Unity.Networking.Transport.FragmentationPipelineStage),
                typeof(Unity.Networking.Transport.UnreliableSequencedPipelineStage)
            );

            m_ReliablePipeline = m_NetworkDriver.CreatePipeline(
                typeof(Unity.Networking.Transport.ReliableSequencedPipelineStage)
            );

            Debug.Log("[ViverseTransport] WebGL pipelines created successfully");
        }
#endif

        public override bool StartClient()
        {
            Debug.Log($"[ViverseTransport] StartClient called (IsHost from config: {m_ConnectionData.IsHost})");
            
            // IMPORTANT: Check if we're actually supposed to be a host
            // This handles the case where the UI incorrectly calls StartClient for the host
            if (m_ConnectionData.IsHost)
            {
                Debug.LogWarning("[ViverseTransport] StartClient called but we're configured as host! Redirecting to StartServer");
                return StartServer();
            }
            
            ConnectToViverse(m_ViverseAppId, m_ViverseRoomId, false);
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, we handle client/server roles through WebRTC
            // No traditional server/client distinction in P2P
            return StartViverseP2P(false);
#else
            return m_InternalTransport.StartClient();
#endif
        }

        public override bool StartServer()
        {
            Debug.Log($"[ViverseTransport] Starting Viverse server (IsHost from config: {m_ConnectionData.IsHost})");
            
            // Ensure we're configured as host
            m_ConnectionData.IsHost = true;
            m_IsHost = true;
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL hosts, mark as connected immediately since we don't have traditional server sockets
            m_IsConnected = true;
            m_HasSentConnectionEvent = true; // Don't send connection event as host
#endif
            
            ConnectToViverse(m_ViverseAppId, m_ViverseRoomId, true);
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, we handle host role through WebRTC P2P
            return StartViverseP2P(true);
#else
            return m_InternalTransport.StartServer();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Start Viverse P2P networking for WebGL
        /// </summary>
        private bool StartViverseP2P(bool isHost)
        {
            if (!m_NetworkDriver.IsCreated)
            {
                Debug.LogError("[ViverseTransport] Network driver not created for WebGL P2P");
                return false;
            }

            // For WebGL P2P, we just need to bind the driver
            var bindResult = m_NetworkDriver.Bind(Unity.Networking.Transport.NetworkEndpoint.AnyIpv4);
            if (bindResult != 0)
            {
                Debug.LogError($"[ViverseTransport] Failed to bind WebGL P2P driver: {bindResult}");
                return false;
            }

            if (isHost)
            {
                // Host starts listening
                var listenResult = m_NetworkDriver.Listen();
                if (listenResult != 0)
                {
                    Debug.LogError($"[ViverseTransport] Failed to listen on WebGL P2P driver: {listenResult}");
                    return false;
                }
                Debug.Log("[ViverseTransport] WebGL P2P host started successfully");
            }
            else
            {
                Debug.Log("[ViverseTransport] WebGL P2P client started successfully");
            }

            return true;
        }
#endif

        public override void Shutdown()
        {
            Debug.Log("[ViverseTransport] Shutting down Viverse transport");
            DisconnectFromViverse();
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, just dispose the driver
            if (m_NetworkDriver.IsCreated)
                m_NetworkDriver.Dispose();
#else
            m_InternalTransport?.Shutdown();
            if (m_NetworkDriver.IsCreated)
                m_NetworkDriver.Dispose();
#endif
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            Debug.Log($"[ViverseTransport] Disconnecting remote client: {clientId}");
            ViverseConnectionManager.RemoveConnection(clientId);
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL P2P, handle disconnection through WebRTC
            // Connection removal is handled by ViverseConnectionManager
#else
            m_InternalTransport?.DisconnectRemoteClient(clientId);
#endif
        }

        public override void DisconnectLocalClient()
        {
            Debug.Log("[ViverseTransport] DisconnectLocalClient called");
            Debug.LogWarning($"[ViverseTransport] Stack trace: {System.Environment.StackTrace}");
            
            DisconnectFromViverse();
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL P2P, disconnect from Viverse room
            m_IsConnected = false;
            m_HasSentConnectionEvent = false;
#else
            m_InternalTransport?.DisconnectLocalClient();
#endif
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL P2P, return mock RTT or implement WebRTC ping
            return 50; // Mock RTT for WebGL
#else
            return m_InternalTransport?.GetCurrentRtt(clientId) ?? 0;
#endif
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL P2P, check if we're connected before sending
            if (!m_IsConnected)
            {
                Debug.LogWarning($"[ViverseTransport] Attempted to send {payload.Count} bytes but not connected yet");
                return;
            }
#endif

            // For WebRTC P2P, we broadcast to all connected clients
            var base64Data = System.Convert.ToBase64String(payload.Array, payload.Offset, payload.Count);
            var senderId = m_ConnectionData.IsHost ? "host" : ViverseConnectionManager.GetLocalPlayerId();
            
#if UNITY_WEBGL && !UNITY_EDITOR
            ViverseWebRTCBridge.SendBroadcastMessage(base64Data, senderId);
            Debug.Log($"[ViverseTransport] Sent {payload.Count} bytes to client {clientId} via WebRTC");
#else
            Debug.Log($"[ViverseTransport] Mock send: {payload.Count} bytes from {senderId}");
#endif
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // Initialize out parameters
            clientId = 0;
            payload = default;
            receiveTime = Time.time;
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, poll events directly from the Viverse driver
            return PollViverseEvents(out clientId, out payload, out receiveTime);
#else
            // Delegate to internal transport if available
            if (m_InternalTransport != null)
            {
                return m_InternalTransport.PollEvent(out clientId, out payload, out receiveTime);
            }
            
            return NetworkEvent.Nothing;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Track connection state for WebGL P2P
        private bool m_HasSentConnectionEvent = false;
        private float m_ConnectionEventDelay = 0.5f; // Delay before sending connection event
        private float m_ConnectionEventTimer = 0f;
        private bool m_IsConnected = false;
        private Queue<(ulong clientId, ArraySegment<byte> data)> m_PendingDataEvents = new Queue<(ulong, ArraySegment<byte>)>();
        
        /// <summary>
        /// Poll events from Viverse WebRTC for WebGL
        /// </summary>
        private NetworkEvent PollViverseEvents(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = Time.time;
            
            if (!m_NetworkDriver.IsCreated)
                return NetworkEvent.Nothing;

            // For clients, send connection event after a delay
            if (!m_ConnectionData.IsHost && !m_HasSentConnectionEvent)
            {
                // Increment timer
                m_ConnectionEventTimer += Time.deltaTime;
                
                // Check if we should send the connection event
                if (m_ConnectionEventTimer >= m_ConnectionEventDelay && IsViverseConnected)
                {
                    // Send connection event for client
                    m_HasSentConnectionEvent = true;
                    m_IsConnected = true; // Mark as connected
                    clientId = ServerClientId; // Client connecting to server (ID 0)
                    Debug.Log("[ViverseTransport] Sending client connection event to Unity Netcode");
                    
                    // Important: After this event, Unity Netcode will:
                    // 1. Call Send() to send connection request data
                    // 2. Call PollEvent() expecting approval data
                    // We need to handle this flow properly
                    
                    return NetworkEvent.Connect;
                }
            }
            
            // For connected clients, ensure we don't disconnect immediately
            // Unity Netcode will handle the connection flow after we send the Connect event
            
            // If connected, check for pending data events
            if (m_IsConnected && m_PendingDataEvents.Count > 0)
            {
                var (pendingClientId, pendingData) = m_PendingDataEvents.Dequeue();
                clientId = pendingClientId;
                payload = pendingData;
                return NetworkEvent.Data;
            }
            
            // For hosts, simulate receiving connection events from clients
            if (m_ConnectionData.IsHost && m_IsConnected)
            {
                // Check ViverseBroadcastReceiver for incoming messages
                // In a real implementation, you'd check for new client connections via Viverse events
                
                // For now, we'll let the normal data flow handle everything
                // The connection approval will happen when the client sends its first message
            }
            
            return NetworkEvent.Nothing;
        }
#endif

        /// <summary>
        /// Get connection statistics for debugging/monitoring
        /// </summary>
        public string GetConnectionStats()
        {
            var stats = $"Viverse Transport Stats:\n";
            stats += $"- Connected to Viverse: {IsViverseConnected}\n";
            stats += $"- App ID: {m_ConnectionData.AppId}\n";
            stats += $"- Room ID: {m_ConnectionData.RoomId}\n";
            stats += $"- Is Host: {m_ConnectionData.IsHost}\n";
            stats += $"- Network Driver Created: {m_NetworkDriver.IsCreated}\n";
            
            if (m_InternalTransport != null)
            {
                stats += $"- Internal Transport: Available\n";
            }
            else
            {
                stats += $"- Internal Transport: Not Available\n";
            }
            
            return stats;
        }

        /// <summary>
        /// Set Viverse connection data for runtime configuration
        /// </summary>
        public void SetViverseConnectionData(ViverseConnectionData connectionData)
        {
            m_ConnectionData = connectionData;
            m_ViverseAppId = connectionData.AppId;
            m_ViverseRoomId = connectionData.RoomId;
            m_IsHost = connectionData.IsHost;
            m_ViverseConnectTimeoutMS = connectionData.ConnectTimeoutMS;
            m_ViverseMaxConnectionAttempts = connectionData.MaxConnectionAttempts;
            m_ViverseEnableDebugLogging = connectionData.EnableDebugLogging;

            Debug.Log($"[ViverseTransport] Updated connection data - App: {m_ViverseAppId}, Room: {m_ViverseRoomId}, IsHost: {m_IsHost}");
        }

        /// <summary>
        /// Connect to Viverse room and start the transport
        /// </summary>
        public bool ConnectToViverse(string appId, string roomId, bool isHost)
        {
            var connectionData = new ViverseConnectionData
            {
                AppId = appId,
                RoomId = roomId,
                IsHost = isHost,
                ConnectTimeoutMS = m_ViverseConnectTimeoutMS,
                MaxConnectionAttempts = m_ViverseMaxConnectionAttempts,
                EnableDebugLogging = m_ViverseEnableDebugLogging
            };

            SetViverseConnectionData(connectionData);
            StartCoroutine(ConnectToViverseCoroutine());
            return true;
        }

        /// <summary>
        /// Disconnect from Viverse room
        /// </summary>
        public void DisconnectFromViverse()
        {
            if (!IsViverseConnected)
            {
                Debug.LogWarning("[ViverseTransport] Not connected to Viverse");
                return;
            }

            try
            {
                ViverseWebRTCBridge.LeaveViverseRoom();
                IsViverseConnected = false;
                
                Debug.Log("[ViverseTransport] Disconnected from Viverse");
                OnViverseDisconnected?.Invoke();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ViverseTransport] Error disconnecting from Viverse: {e.Message}");
            }
        }

        /// <summary>
        /// Coroutine to handle Viverse connection
        /// </summary>
        private IEnumerator ConnectToViverseCoroutine()
        {
            Debug.Log($"[ViverseTransport] Connecting to Viverse room {m_ConnectionData.RoomId}...");

            // Join Viverse room via WebRTC bridge
            try
            {
                ViverseWebRTCBridge.JoinViverseRoom(m_ConnectionData.AppId, m_ConnectionData.RoomId);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ViverseTransport] Error joining Viverse room: {e.Message}");
                yield break;
            }

            // Wait for connection with timeout (yield statements outside try-catch)
            var startTime = Time.time;
            var timeoutSeconds = m_ConnectionData.ConnectTimeoutMS / 1000f;

            while (!ViverseWebRTCBridge.IsViverseConnected() && Time.time - startTime < timeoutSeconds)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // Handle connection result
            if (ViverseWebRTCBridge.IsViverseConnected())
            {
                IsViverseConnected = true;
                Debug.Log($"[ViverseTransport] Successfully connected to Viverse room {m_ConnectionData.RoomId}");
                OnViverseConnected?.Invoke();
            }
            else
            {
                Debug.LogError($"[ViverseTransport] Failed to connect to Viverse room {m_ConnectionData.RoomId} within {timeoutSeconds} seconds");
            }
        }
    }
}