using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using Unity.Netcode.Transports.UTP;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace Unity.Netcode.Transports.Viverse
{
    /// <summary>
    /// Network driver constructor for Viverse WebRTC transport
    /// </summary>
    public class ViverseNetworkDriver : INetworkStreamDriverConstructor
    {
        private readonly ViverseNetworkParameter m_Parameters;

        public ViverseNetworkDriver(ViverseNetworkParameter parameters)
        {
            m_Parameters = parameters;
            Debug.Log($"[ViverseNetworkDriver] Created for WebRTC P2P - App: {parameters.AppId}, Room: {parameters.RoomId}, IsHost: {parameters.IsHost}");
        }

        /// <summary>
        /// Create the network driver with Viverse WebRTC interface
        /// </summary>
        public void CreateDriver(
            UnityTransport transport,
            out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            // Create network settings with Viverse parameters
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithViverseParameters(
                m_Parameters.AppId.ToString(),
                m_Parameters.RoomId.ToString(),
                m_Parameters.IsHost == 1,
                m_Parameters.ConnectTimeoutMS,
                m_Parameters.MaxConnectionAttempts,
                m_Parameters.EnableDebugLogging == 1);

            // Create Viverse network interface
            var networkInterface = new ViverseNetworkInterface();
            // Initialize with parameters directly via our internal method
            networkInterface.InitializeInternal(m_Parameters);
            
            // Still need to call the interface Initialize for Unity Transport compatibility
            int packetPadding = 0;
            networkInterface.Initialize(ref settings, ref packetPadding);

            // Create driver with Viverse interface
            driver = NetworkDriver.Create(networkInterface, settings);

            // Setup standard UTP pipelines for reliability, fragmentation, etc.
            SetupPipelines(driver, out unreliableFragmentedPipeline, out unreliableSequencedFragmentedPipeline, out reliableSequencedPipeline);

            Debug.Log("[ViverseNetworkDriver] Driver created with Viverse WebRTC interface and UTP pipelines");
        }

        /// <summary>
        /// Setup standard Unity Transport pipelines
        /// </summary>
        private void SetupPipelines(NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            // Create standard UTP pipelines
            unreliableFragmentedPipeline = driver.CreatePipeline(
                typeof(FragmentationPipelineStage)
#if UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED
                , typeof(SimulatorPipelineStage)
#endif
            );

            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(
                typeof(FragmentationPipelineStage),
                typeof(UnreliableSequencedPipelineStage)
#if UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED
                , typeof(SimulatorPipelineStage)
#endif
            );

            reliableSequencedPipeline = driver.CreatePipeline(
                typeof(ReliableSequencedPipelineStage)
#if UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED
                , typeof(SimulatorPipelineStage)
#endif
            );

            Debug.Log("[ViverseNetworkDriver] UTP pipelines created successfully");
        }

        /// <summary>
        /// Try to get the connection payload for a client ID
        /// </summary>
        public static bool TryGetConnectionPayload(ulong clientId, out byte[] payload)
        {
            return ViverseConnectionManager.TryGetConnectionPayload(clientId, out payload);
        }
    }

    /// <summary>
    /// Message data structure for Viverse transport
    /// </summary>
    public struct ViverseMessageData
    {
        public FixedBytes126 Data;
        public int Length;
        public FixedString64Bytes SenderId;
    }

    /// <summary>
    /// Viverse WebRTC network interface implementing Unity Transport's INetworkInterface
    /// </summary>
    public unsafe struct ViverseNetworkInterface : INetworkInterface
    {
        /// <summary>
        /// Broadcast endpoint for Viverse WebRTC
        /// </summary>
        public static readonly NetworkEndpoint s_BroadcastEndpoint = NetworkEndpoint.AnyIpv4;

        /// <summary>
        /// Maximum message size for Viverse transport
        /// </summary>
        private const int k_MaxMessageSize = 1200; // WebRTC typical MTU

        /// <summary>
        /// Internal data for the network interface
        /// </summary>
        private struct InternalData
        {
            public ViverseNetworkParameter Parameters;
            public double LastUpdateTime;
            public byte ConnectionPhase; // 0=disconnected, 1=connecting, 2=connected
            public byte IsConnected;
            public int LocalPlayerId;
        }

        private NativeReference<InternalData> m_InternalData;
        private NativeQueue<ViverseMessageData> m_MessageQueue;
        private static NativeQueue<ViverseMessageData> s_SharedMessageQueue;

        /// <summary>
        /// Initialize the Viverse network interface with Unity's INetworkInterface signature
        /// </summary>
        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            // Extract Viverse parameters from network settings if available
            ViverseNetworkParameter parameters = default;
            if (settings.TryGet<ViverseNetworkParameter>(out var viverseParams))
            {
                parameters = viverseParams;
            }
            
            InitializeInternal(parameters);
            return 0;
        }
        
        /// <summary>
        /// Internal initialization method
        /// </summary>
        public void InitializeInternal(ViverseNetworkParameter parameters)
        {
            m_InternalData = new NativeReference<InternalData>(Allocator.Persistent);
            m_MessageQueue = new NativeQueue<ViverseMessageData>(Allocator.Persistent);

            var internalData = new InternalData
            {
                Parameters = parameters,
                LastUpdateTime = Time.timeAsDouble,
                ConnectionPhase = 0,
                IsConnected = 0,
                LocalPlayerId = parameters.IsHost == 1 ? 0 : UnityEngine.Random.Range(1000, 9999)
            };

            m_InternalData.Value = internalData;

            // Initialize shared message queue if not already done
            if (!s_SharedMessageQueue.IsCreated)
            {
                s_SharedMessageQueue = new NativeQueue<ViverseMessageData>(Allocator.Persistent);
            }

            // Initialize connection manager
            ViverseConnectionManager.Initialize();

            // Setup Viverse broadcast receiver if we're in WebGL
#if UNITY_WEBGL && !UNITY_EDITOR
            SetupViverseBroadcastReceiver();
#endif

            Debug.Log($"[ViverseNetworkInterface] Initialized for WebRTC P2P with local player ID {internalData.LocalPlayerId}");
        }
        
        /// <summary>
        /// Get the local endpoint for this interface
        /// </summary>
        public NetworkEndpoint LocalEndpoint
        {
            get { return NetworkEndpoint.AnyIpv4; }
        }

        /// <summary>
        /// Setup the Viverse broadcast message receiver
        /// </summary>
        private void SetupViverseBroadcastReceiver()
        {
            // Find or create the broadcast receiver component
            var receiverGO = GameObject.Find("ViverseBroadcastReceiver");
            if (receiverGO == null)
            {
                receiverGO = new GameObject("ViverseBroadcastReceiver");
                GameObject.DontDestroyOnLoad(receiverGO);
            }

            var receiver = receiverGO.GetComponent<ViverseBroadcastReceiver>();
            if (receiver == null)
            {
                receiver = receiverGO.AddComponent<ViverseBroadcastReceiver>();
            }

            // Set the message queue for the receiver
            ViverseBroadcastReceiver.SetMessageQueue(s_SharedMessageQueue);
        }

        /// <summary>
        /// Handle incoming connection requests as host (main thread)
        /// </summary>
        public static void HandleHostConnectionRequest(string playerId, byte[] connectionPayload)
        {
            // Check if connection approval is needed
            if (ShouldApproveConnection(playerId, connectionPayload))
            {
                // Create and approve the connection
                var clientId = ViverseConnectionManager.CreateConnection(
                    playerId, 
                    ViverseConnectionRole.Client, 
                    connectionPayload);
                    
                ViverseConnectionManager.UpdateConnectionState(clientId, ViverseConnectionState.Connected);
                ViverseConnectionManager.MarkPlayerAnnounced(playerId);
                
                Debug.Log($"[ViverseNetworkInterface] Approved connection for player {playerId} as client {clientId}");
                
                // Send connection approval back to client if needed
                SendConnectionApproval(playerId, clientId, true);
            }
            else
            {
                Debug.LogWarning($"[ViverseNetworkInterface] Rejected connection for player {playerId}");
                SendConnectionApproval(playerId, 0, false);
            }
        }

        /// <summary>
        /// Handle incoming messages as client (main thread)
        /// </summary>
        public static void HandleClientConnectionResponse(string hostId, byte[] responsePayload)
        {
            // Process connection response from host
            if (responsePayload != null && responsePayload.Length > 0)
            {
                // Check if connection was approved
                var approved = responsePayload[0] == 1; // Simple approval flag
                
                if (approved)
                {
                    // Create connection for the host
                    var hostClientId = ViverseConnectionManager.CreateConnection(
                        hostId, 
                        ViverseConnectionRole.Server, 
                        responsePayload);
                        
                    ViverseConnectionManager.UpdateConnectionState(hostClientId, ViverseConnectionState.Connected);
                    
                    Debug.Log($"[ViverseNetworkInterface] Connection approved by host {hostId}");
                }
                else
                {
                    Debug.LogWarning($"[ViverseNetworkInterface] Connection rejected by host {hostId}");
                }
            }
        }

        /// <summary>
        /// Determine if a connection should be approved
        /// </summary>
        private static bool ShouldApproveConnection(string playerId, byte[] connectionPayload)
        {
            // Basic connection approval logic
            // This can be enhanced with custom approval callbacks
            
            // Check if player is already connected
            if (ViverseConnectionManager.TryGetClientId(playerId, out var existingClientId))
            {
                Debug.LogWarning($"[ViverseNetworkInterface] Player {playerId} already connected as {existingClientId}");
                return false;
            }
            
            // Check connection limits
            var activeConnections = ViverseConnectionManager.GetActiveConnections(Unity.Collections.Allocator.Temp);
            var connectionCount = activeConnections.Length;
            activeConnections.Dispose();
            
            if (connectionCount >= ViverseConnectionManager.MaxConnections)
            {
                Debug.LogWarning($"[ViverseNetworkInterface] Connection limit reached: {connectionCount}/{ViverseConnectionManager.MaxConnections}");
                return false;
            }
            
            // Basic validation passed
            return true;
        }

        /// <summary>
        /// Send connection approval/rejection to a player
        /// </summary>
        private static void SendConnectionApproval(string playerId, ulong clientId, bool approved)
        {
            // Create approval message
            var approvalData = new byte[9]; // 1 byte for approval + 8 bytes for client ID
            approvalData[0] = (byte)(approved ? 1 : 0);
            
            if (approved)
            {
                // Encode client ID
                var clientIdBytes = System.BitConverter.GetBytes(clientId);
                System.Array.Copy(clientIdBytes, 0, approvalData, 1, 8);
            }
            
            // Send via WebRTC bridge
#if UNITY_WEBGL && !UNITY_EDITOR
            var base64Data = System.Convert.ToBase64String(approvalData);
            ViverseWebRTCBridge.SendBroadcastMessage(base64Data, "host");
#else
            Debug.Log($"[ViverseNetworkInterface] Mock send approval to {playerId}: approved={approved}, clientId={clientId}");
#endif
        }

        /// <summary>
        /// Process connection management updates (should be called regularly)
        /// </summary>
        public static void UpdateConnectionManagement()
        {
            // Update connection timeouts and cleanup
            ViverseConnectionManager.UpdateConnections();
            
            // Additional connection management logic can be added here
            // such as heartbeat processing, reconnection attempts, etc.
        }

        public NetworkEndpoint CreateInterfaceEndPoint(NetworkEndpoint endpoint, out int baseOffset)
        {
            baseOffset = 0;
            return s_BroadcastEndpoint;
        }

        public NetworkEndpoint GetGenericEndPoint(NetworkEndpoint endpoint)
        {
            return NetworkEndpoint.AnyIpv4;
        }

        public int CreateSendInterface()
        {
            return 0; // Single interface for broadcast
        }

        public int CreateReceiveInterface()
        {
            return 0; // Single interface for broadcast
        }

        public void DestroyInterface(int interfaceId)
        {
            // Interface cleanup handled in Dispose
        }

        public unsafe int ReceiveErrorCode => 0;

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            return new ViverseReceiveJob
            {
                ReceiveQueue = arguments.ReceiveQueue,
                MessageQueue = s_SharedMessageQueue,
                InternalData = m_InternalData,
                Time = arguments.Time
            }.Schedule(dep);
        }

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            // For WebRTC, we'll handle sending on the main thread instead of in a Burst job
            // Since we need to call JavaScript bridge functions which require managed code
            
            // Process send queue on main thread
            var sendQueue = arguments.SendQueue;
            var internalData = m_InternalData.Value;
            
            for (int i = 0; i < sendQueue.Count; i++)
            {
                var packetProcessor = sendQueue[i];
                if (packetProcessor.Length == 0)
                    continue;
                    
                // Extract packet data
                unsafe
                {
                    var dataPtr = (byte*)packetProcessor.GetUnsafePayloadPtr() + packetProcessor.Offset;
                    var dataArray = new byte[packetProcessor.Length];
                    
                    for (int j = 0; j < packetProcessor.Length; j++)
                    {
                        dataArray[j] = dataPtr[j];
                    }
                    
                    // Send via WebRTC bridge on main thread
                    SendPacketViaWebRTC(dataArray, internalData.LocalPlayerId.ToString());
                }
            }
            
            // Return completed job handle since we processed synchronously
            return dep;
        }
        
        /// <summary>
        /// Send packet data via WebRTC bridge (main thread only)
        /// </summary>
        private void SendPacketViaWebRTC(byte[] data, string senderId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var base64Data = System.Convert.ToBase64String(data);
            ViverseWebRTCBridge.SendBroadcastMessage(base64Data, senderId);
#else
            Debug.Log($"[ViverseNetworkInterface] Mock send: {data.Length} bytes from {senderId}");
#endif
        }
        

        public int Bind(NetworkEndpoint endpoint)
        {
            // Viverse WebRTC doesn't use traditional binding
            var internalData = m_InternalData.Value;
            internalData.ConnectionPhase = 1; // Start connecting
            m_InternalData.Value = internalData;

            Debug.Log("[ViverseNetworkInterface] Bound to Viverse WebRTC broadcast");
            return 0;
        }

        public int Listen()
        {
            // Start listening for Viverse WebRTC connections
            var internalData = m_InternalData.Value;
            internalData.IsConnected = 1;
            internalData.ConnectionPhase = 2; // Connected
            m_InternalData.Value = internalData;

            Debug.Log("[ViverseNetworkInterface] Listening for Viverse WebRTC connections");
            return 0;
        }

        public void Dispose()
        {
            if (m_InternalData.IsCreated)
                m_InternalData.Dispose();
            if (m_MessageQueue.IsCreated)
                m_MessageQueue.Dispose();

            // Note: Don't dispose s_SharedMessageQueue here as it's shared
            Debug.Log("[ViverseNetworkInterface] Disposed");
        }

        /// <summary>
        /// Burst-compiled job for receiving Viverse WebRTC messages
        /// </summary>
        [BurstCompile]
        private unsafe struct ViverseReceiveJob : IJob
        {
            public PacketsQueue ReceiveQueue;
            public NativeQueue<ViverseMessageData> MessageQueue;
            public NativeReference<InternalData> InternalData;
            public double Time;

            public void Execute()
            {
                var internalData = InternalData.Value;
                var isHost = internalData.Parameters.IsHost == 1;

                // Process all queued messages from Viverse WebRTC
                while (MessageQueue.TryDequeue(out var messageData))
                {
                    // Generate client ID from sender hash (Burst-compatible)
                    var clientId = (ulong)(messageData.SenderId.GetHashCode() & 0x7FFFFFFF);
                    
                    // Handle connection management based on role
                    if (isHost)
                    {
                        ProcessHostReceive(messageData, clientId);
                    }
                    else
                    {
                        ProcessClientReceive(messageData, clientId);
                    }
                    
                    // Enqueue packet for Unity Transport processing
                    if (ReceiveQueue.EnqueuePacket(out var packetProcessor))
                    {
                        packetProcessor.EndpointRef = ViverseNetworkInterface.s_BroadcastEndpoint;

                        // Copy message data to packet payload
                        unsafe
                        {
                            var dataPtr = (byte*)UnsafeUtility.AddressOf(ref messageData.Data);
                            packetProcessor.AppendToPayload(dataPtr, messageData.Length);
                        }
                        
                        // Connection management will be handled at a higher level
                        // The packet is now available for Unity Transport to process
                        // Connection tracking is done via ViverseConnectionManager
                    }
                }

                // Update internal data
                internalData.LastUpdateTime = Time;
                InternalData.Value = internalData;
            }

            /// <summary>
            /// Process incoming message when acting as host
            /// </summary>
            private void ProcessHostReceive(ViverseMessageData messageData, ulong clientId)
            {
                // For hosts, track new client connections
                // This is simplified for Burst compatibility - detailed tracking happens on main thread
                // We focus on ensuring proper packet routing and connection state
                
                // Basic connection tracking - detailed logic handled on main thread
                // This method can be expanded later for specific host processing needs
            }

            /// <summary>
            /// Process incoming message when acting as client
            /// </summary>
            private void ProcessClientReceive(ViverseMessageData messageData, ulong clientId)
            {
                // For clients, ensure we're receiving from the host
                // Clients should primarily receive from server (clientId 0)
                
                // Basic message validation - detailed logic handled on main thread
                // This method can be expanded later for specific client processing needs
            }
        }

    }

    /// <summary>
    /// JavaScript bridge for Viverse WebRTC communication
    /// </summary>
    public static class ViverseWebRTCBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        public static extern void SendBroadcastMessage(string base64Data, string senderId);

        [DllImport("__Internal")]
        public static extern void JoinViverseRoom(string appId, string roomId);

        [DllImport("__Internal")]
        public static extern void LeaveViverseRoom();

        [DllImport("__Internal")]
        public static extern bool IsViverseConnected();
#else
        public static void SendBroadcastMessage(string base64Data, string senderId)
        {
            Debug.Log($"[ViverseWebRTCBridge] Mock send: {base64Data.Substring(0, Math.Min(20, base64Data.Length))}... from {senderId}");
        }

        public static void JoinViverseRoom(string appId, string roomId)
        {
            Debug.Log($"[ViverseWebRTCBridge] Mock join room: {appId}/{roomId}");
        }

        public static void LeaveViverseRoom()
        {
            Debug.Log("[ViverseWebRTCBridge] Mock leave room");
        }

        public static bool IsViverseConnected()
        {
            return true; // Mock connection for testing
        }
#endif
    }

    /// <summary>
    /// MonoBehaviour to receive broadcast messages from Viverse WebRTC
    /// </summary>
    public class ViverseBroadcastReceiver : MonoBehaviour
    {
        private static NativeQueue<ViverseMessageData> s_MessageQueue;

        public static void SetMessageQueue(NativeQueue<ViverseMessageData> messageQueue)
        {
            s_MessageQueue = messageQueue;
        }

        // This method will be called by the Viverse WebRTC bridge when broadcast messages are received
        public void OnViverseMessage(string messageJson)
        {
            try
            {
                if (s_MessageQueue.IsCreated)
                {
                    // Parse JSON message from Viverse WebRTC
                    var messageObj = JsonUtility.FromJson<ViverseTransportMessage>(messageJson);

                    // Process different message types
                    switch (messageObj.type)
                    {
                        case "unity_transport_broadcast":
                            ProcessTransportMessage(messageObj);
                            break;
                        case "connection_request":
                            ProcessConnectionRequest(messageObj);
                            break;
                        case "connection_response":
                            ProcessConnectionResponse(messageObj);
                            break;
                        default:
                            // Unknown message type, ignore
                            break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ViverseBroadcastReceiver] Error processing broadcast message: {e}");
            }
        }

        /// <summary>
        /// Process Unity Transport broadcast messages
        /// </summary>
        private void ProcessTransportMessage(ViverseTransportMessage messageObj)
        {
            var senderId = messageObj.senderId;
            var base64Message = messageObj.data;
            var messageBytes = Convert.FromBase64String(base64Message);

            var messageData = new ViverseMessageData
            {
                Length = Mathf.Min(messageBytes.Length, 126), // Limit to FixedBytes126 size
                SenderId = new FixedString64Bytes(senderId)
            };

            unsafe
            {
                // Copy bytes into FixedBytes126
                var dataPtr = (byte*)UnsafeUtility.AddressOf(ref messageData.Data);
                for (int i = 0; i < messageData.Length; i++)
                {
                    dataPtr[i] = messageBytes[i];
                }
            }

            s_MessageQueue.Enqueue(messageData);
            
            // Update connection activity
            if (ViverseConnectionManager.TryGetClientId(senderId, out var clientId))
            {
                ViverseConnectionManager.UpdateConnectionActivity(clientId);
            }

            Debug.Log($"[ViverseBroadcastReceiver] Received transport message from {senderId}, queued {messageData.Length} bytes");
        }

        /// <summary>
        /// Process connection request messages
        /// </summary>
        private void ProcessConnectionRequest(ViverseTransportMessage messageObj)
        {
            var senderId = messageObj.senderId;
            var connectionPayload = Convert.FromBase64String(messageObj.data);

            Debug.Log($"[ViverseBroadcastReceiver] Received connection request from {senderId}");

            // Handle on main thread
            ViverseNetworkInterface.HandleHostConnectionRequest(senderId, connectionPayload);
        }

        /// <summary>
        /// Process connection response messages
        /// </summary>
        private void ProcessConnectionResponse(ViverseTransportMessage messageObj)
        {
            var senderId = messageObj.senderId;
            var responsePayload = Convert.FromBase64String(messageObj.data);

            Debug.Log($"[ViverseBroadcastReceiver] Received connection response from {senderId}");

            // Handle on main thread
            ViverseNetworkInterface.HandleClientConnectionResponse(senderId, responsePayload);
        }
    }

    /// <summary>
    /// JSON message structure for Viverse WebRTC transport broadcasts
    /// </summary>
    [System.Serializable]
    public class ViverseTransportMessage
    {
        public string type;
        public string senderId;
        public string data;
    }
}