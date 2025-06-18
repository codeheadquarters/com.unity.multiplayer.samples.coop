using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.Viverse;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Manager component to integrate Viverse Transport with the existing connection system
    /// </summary>
    public class ViverseTransportManager : MonoBehaviour
    {
        [Header("Viverse Configuration")]
        [SerializeField] private string m_ViverseAppId = "your_viverse_app_id";
        [SerializeField] private string m_DefaultRoomId = "bossroom_lobby";
        [SerializeField] private bool m_UseViverseTransport = false;

        private ViverseTransport m_ViverseTransport;
        private NetworkManager m_NetworkManager;
        
        /// <summary>
        /// Event triggered when Viverse transport is ready
        /// </summary>
        public System.Action<bool> OnViverseTransportReady;

        private void Awake()
        {
            m_NetworkManager = FindObjectOfType<NetworkManager>();
            
            if (m_UseViverseTransport)
            {
                SetupViverseTransport();
            }
        }

        /// <summary>
        /// Setup Viverse transport to replace Unity Transport
        /// </summary>
        private void SetupViverseTransport()
        {
            if (m_NetworkManager == null)
            {
                Debug.LogError("[ViverseTransportManager] NetworkManager not found!");
                return;
            }

            // Check if ViverseTransport already exists
            m_ViverseTransport = m_NetworkManager.GetComponent<ViverseTransport>();
            
            if (m_ViverseTransport == null)
            {
                // Remove existing UnityTransport and add ViverseTransport
                var unityTransport = m_NetworkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (unityTransport != null)
                {
                    Debug.Log("[ViverseTransportManager] Replacing UnityTransport with ViverseTransport");
#if UNITY_EDITOR
                    DestroyImmediate(unityTransport);
#else
                    Destroy(unityTransport);
#endif
                }

                // Add ViverseTransport component
                m_ViverseTransport = m_NetworkManager.gameObject.AddComponent<ViverseTransport>();
                
                // Set as the network transport
                m_NetworkManager.NetworkConfig.NetworkTransport = m_ViverseTransport;
            }

            // Setup event handlers
            m_ViverseTransport.OnViverseConnected += OnViverseConnected;
            m_ViverseTransport.OnViverseDisconnected += OnViverseDisconnected;

            Debug.Log("[ViverseTransportManager] Viverse transport setup complete");
        }

        /// <summary>
        /// Start host with Viverse transport
        /// </summary>
        public void StartViverseHost(string roomId = null)
        {
            if (!m_UseViverseTransport)
            {
                Debug.LogWarning("[ViverseTransportManager] Viverse transport is not enabled");
                return;
            }

            var targetRoomId = roomId ?? m_DefaultRoomId;
            StartCoroutine(StartViverseHostCoroutine(targetRoomId));
        }

        /// <summary>
        /// Start client with Viverse transport
        /// </summary>
        public void StartViverseClient(string roomId = null)
        {
            if (!m_UseViverseTransport)
            {
                Debug.LogWarning("[ViverseTransportManager] Viverse transport is not enabled");
                return;
            }

            var targetRoomId = roomId ?? m_DefaultRoomId;
            StartCoroutine(StartViverseClientCoroutine(targetRoomId));
        }

        /// <summary>
        /// Coroutine to start host with Viverse connection
        /// </summary>
        private IEnumerator StartViverseHostCoroutine(string roomId)
        {
            Debug.Log($"[ViverseTransportManager] Starting Viverse host for room: {roomId}");

            // Connect to Viverse room as host
            m_ViverseTransport.ConnectToViverse(m_ViverseAppId, roomId, true);

            // Wait for Viverse connection
            yield return new WaitUntil(() => m_ViverseTransport.IsViverseConnected);

            // Start NetworkManager as host
            if (m_NetworkManager.StartHost())
            {
                Debug.Log("[ViverseTransportManager] Host started successfully with Viverse transport");
            }
            else
            {
                Debug.LogError("[ViverseTransportManager] Failed to start host");
            }
        }

        /// <summary>
        /// Coroutine to start client with Viverse connection
        /// </summary>
        private IEnumerator StartViverseClientCoroutine(string roomId)
        {
            Debug.Log($"[ViverseTransportManager] Starting Viverse client for room: {roomId}");

            // Connect to Viverse room as client
            m_ViverseTransport.ConnectToViverse(m_ViverseAppId, roomId, false);

            // Wait for Viverse connection
            yield return new WaitUntil(() => m_ViverseTransport.IsViverseConnected);

            // Start NetworkManager as client
            if (m_NetworkManager.StartClient())
            {
                Debug.Log("[ViverseTransportManager] Client started successfully with Viverse transport");
            }
            else
            {
                Debug.LogError("[ViverseTransportManager] Failed to start client");
            }
        }

        /// <summary>
        /// Stop Viverse networking
        /// </summary>
        public void StopViverseNetworking()
        {
            if (m_ViverseTransport != null && m_ViverseTransport.IsViverseConnected)
            {
                m_ViverseTransport.DisconnectFromViverse();
            }

            if (m_NetworkManager != null)
            {
                m_NetworkManager.Shutdown();
            }
        }

        /// <summary>
        /// Get current Viverse connection information
        /// </summary>
        public string GetViverseConnectionInfo()
        {
            if (m_ViverseTransport == null)
                return "Viverse transport not available";

            return m_ViverseTransport.GetConnectionStats();
        }

        /// <summary>
        /// Check if Viverse transport is available and configured
        /// </summary>
        public bool IsViverseTransportReady()
        {
            return m_UseViverseTransport && 
                   m_ViverseTransport != null && 
                   !string.IsNullOrEmpty(m_ViverseAppId);
        }

        private void OnViverseConnected()
        {
            Debug.Log("[ViverseTransportManager] Viverse connection established");
            OnViverseTransportReady?.Invoke(true);
        }

        private void OnViverseDisconnected()
        {
            Debug.Log("[ViverseTransportManager] Viverse connection lost");
            OnViverseTransportReady?.Invoke(false);
        }

        private void OnDestroy()
        {
            if (m_ViverseTransport != null)
            {
                m_ViverseTransport.OnViverseConnected -= OnViverseConnected;
                m_ViverseTransport.OnViverseDisconnected -= OnViverseDisconnected;
            }
        }

#if UNITY_EDITOR
        [Header("Editor Testing")]
        [SerializeField] private string m_TestRoomId = "test_room";

        [ContextMenu("Test Start Host")]
        private void TestStartHost()
        {
            if (Application.isPlaying)
            {
                StartViverseHost(m_TestRoomId);
            }
        }

        [ContextMenu("Test Start Client")]
        private void TestStartClient()
        {
            if (Application.isPlaying)
            {
                StartViverseClient(m_TestRoomId);
            }
        }

        [ContextMenu("Test Stop Networking")]
        private void TestStopNetworking()
        {
            if (Application.isPlaying)
            {
                StopViverseNetworking();
            }
        }

        [ContextMenu("Get Connection Info")]
        private void TestGetConnectionInfo()
        {
            if (Application.isPlaying)
            {
                Debug.Log(GetViverseConnectionInfo());
            }
        }
#endif
    }
}