using System;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Handles callbacks from Viverse JavaScript operations via Unity's SendMessage
    /// This allows proper async coordination between Unity and Viverse
    /// </summary>
    public class ViverseCallbackHandler : MonoBehaviour
    {
        // Events for async operation completion
        public static event Action<bool> OnActorSetupComplete;
        public static event Action<string> OnRoomCreated; // Room ID
        public static event Action<bool> OnRoomJoined;
        public static event Action<ViverseRoom[]> OnRoomsDiscovered; // Room discovery
        public static event Action<string> OnViverseError;
        public static event Action OnViverseInitComplete; // Viverse initialization completion
        public static event Action OnViverseClientReady; // Viverse client ready for operations

        // Events for broadcast transport system
        public static event Action<string> OnBroadcastTransportConnected; // Transport is ready for P2P communication
        public static event Action<string> OnBroadcastTransportDisconnected; // Transport disconnected
        public static event Action<string> OnBroadcastTransportError; // Transport connection failed

        private static ViverseCallbackHandler s_Instance;

        /// <summary>
        /// Ensures a ViverseCallbackHandler GameObject exists for all Viverse callbacks
        /// Simplified to use one consistent GameObject since the bridge was fixed
        /// </summary>
        public static void EnsureViverseCallbackHandler()
        {
            // Check if instance already exists and is valid
            if (s_Instance != null && s_Instance.gameObject != null)
            {
                Debug.Log("[ViverseCallbackHandler] ViverseCallbackHandler GameObject already exists and is valid");
                return;
            }

            // Find existing GameObject by name
            var existingGO = GameObject.Find("ViverseCallbackHandler");
            if (existingGO != null)
            {
                s_Instance = existingGO.GetComponent<ViverseCallbackHandler>();
                if (s_Instance != null)
                {
                    Debug.Log("[ViverseCallbackHandler] Found existing ViverseCallbackHandler GameObject");
                    return;
                }
            }

            // Create new GameObject - using consistent name that matches the bridge
            var callbackGO = new GameObject("ViverseCallbackHandler");
            DontDestroyOnLoad(callbackGO);
            s_Instance = callbackGO.AddComponent<ViverseCallbackHandler>();
            Debug.Log("[ViverseCallbackHandler] Created new ViverseCallbackHandler GameObject for all bridge callbacks");
        }

        void Awake()
        {
            // Simplified singleton logic for ViverseCallbackHandler
            if (s_Instance == null)
            {
                s_Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ViverseCallbackHandler] ViverseCallbackHandler initialized as singleton");
            }
            else if (s_Instance != this)
            {
                Debug.Log("[ViverseCallbackHandler] Duplicate ViverseCallbackHandler - destroying this instance");
                Destroy(gameObject);
                return;
            }
            
            // Ensure GameObject name is correct for bridge consistency
            if (gameObject.name != "ViverseCallbackHandler")
            {
                gameObject.name = "ViverseCallbackHandler";
                Debug.Log("[ViverseCallbackHandler] Renamed GameObject to ViverseCallbackHandler for bridge consistency");
            }
        }

        // Called from JavaScript when setActor completes
        public void OnSetActorComplete(string result)
        {
            Debug.Log($"[ViverseCallbackHandler] SetActor completed: {result}");
            
            try
            {
                // Parse result to check success
                var success = result.Contains("success") && result.Contains("true");
                OnActorSetupComplete?.Invoke(success);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Error processing SetActor result: {e.Message}");
                OnActorSetupComplete?.Invoke(false);
            }
        }

        // Called from JavaScript when createRoom completes
        public void OnCreateRoomComplete(string roomId)
        {
            Debug.Log($"[ViverseCallbackHandler] CreateRoom completed with room ID: {roomId}");
            
            try
            {
                // Use the actual room ID from VP1-Play
                if (!string.IsNullOrEmpty(roomId))
                {
                    OnRoomCreated?.Invoke(roomId);
                }
                else
                {
                    OnViverseError?.Invoke($"Room creation completed but no room ID provided");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Error processing CreateRoom result: {e.Message}");
                OnViverseError?.Invoke($"Room creation error: {e.Message}");
            }
        }

        // Called from JavaScript when joinRoom completes
        public void OnJoinRoomComplete(string result)
        {
            Debug.Log($"[ViverseCallbackHandler] JoinRoom completed: {result}");
            
            try
            {
                // Check for various success indicators
                var success = result.Contains("success") || 
                             (result.Contains("success") && result.Contains("true")) ||
                             result.Equals("success", System.StringComparison.OrdinalIgnoreCase);
                
                Debug.Log($"[ViverseCallbackHandler] Parsed join result as success: {success}");
                OnRoomJoined?.Invoke(success);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Error processing JoinRoom result: {e.Message}");
                OnRoomJoined?.Invoke(false);
            }
        }

        // Called from JavaScript when room discovery completes
        public void RoomsDiscovered(string roomsJson)
        {
            Debug.Log($"[ViverseCallbackHandler] RoomsDiscovered raw JSON: {roomsJson}");
            
            try
            {
                // First, let's try to see if it's a direct array or needs wrapping
                ViverseRoom[] rooms = null;
                
                // Try parsing as direct array first
                try
                {
                    rooms = JsonUtility.FromJson<ViverseRoom[]>(roomsJson);
                    Debug.Log($"[ViverseCallbackHandler] Successfully parsed as direct array, {rooms.Length} rooms");
                }
                catch
                {
                    // If that fails, try wrapping in rooms object
                    var roomsData = JsonUtility.FromJson<ViverseRoomsResponse>("{\"rooms\":" + roomsJson + "}");
                    rooms = roomsData.rooms;
                    Debug.Log($"[ViverseCallbackHandler] Successfully parsed as wrapped array, {rooms.Length} rooms");
                }
                
                // Log detailed information about each room
                for (int i = 0; i < rooms.Length; i++)
                {
                    var room = rooms[i];
                    Debug.Log($"[ViverseCallbackHandler] Room {i}: ID='{room.id}', Name='{room.name}', CurrentPlayers={room.currentPlayers}, MaxPlayers={room.maxPlayers}, Host='{room.hostName}'");
                }
                
                OnRoomsDiscovered?.Invoke(rooms);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Failed to parse rooms data: {e.Message}");
                Debug.LogError($"[ViverseCallbackHandler] Raw JSON was: {roomsJson}");
                OnRoomsDiscovered?.Invoke(new ViverseRoom[0]); // Empty array as fallback
            }
        }
        
        // Called from JavaScript when Viverse error occurs
        public void ViverseError(string errorMessage)
        {
            Debug.LogError($"[ViverseCallbackHandler] ViverseError: {errorMessage}");
            OnViverseError?.Invoke(errorMessage);
        }

        // Called from JavaScript when Viverse initialization completes
        public void OnInitComplete(string result)
        {
            Debug.Log($"[ViverseCallbackHandler] Viverse initialization completed: {result}");
            
            try
            {
                // Check if initialization was successful
                var success = result.Contains("success") || 
                             result.Contains("initialized") || 
                             result.Equals("complete", System.StringComparison.OrdinalIgnoreCase) ||
                             result.Equals("ready", System.StringComparison.OrdinalIgnoreCase);
                
                Debug.Log($"[ViverseCallbackHandler] Parsed initialization result as success: {success}");
                
                if (success)
                {
                    OnViverseInitComplete?.Invoke();
                }
                else
                {
                    OnViverseError?.Invoke($"Viverse initialization failed: {result}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Error processing initialization result: {e.Message}");
                OnViverseError?.Invoke($"Initialization processing error: {e.Message}");
            }
        }

        // Called from JavaScript when Viverse client is ready for operations
        public void OnClientReady(string result)
        {
            Debug.Log($"[ViverseCallbackHandler] Viverse client ready notification: {result}");
            
            try
            {
                // Check if client is ready for operations
                var isReady = result.Contains("ready") || 
                             result.Contains("connected") ||
                             result.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
                             result.Equals("success", System.StringComparison.OrdinalIgnoreCase);
                
                Debug.Log($"[ViverseCallbackHandler] Parsed client ready result as ready: {isReady}");
                
                if (isReady)
                {
                    OnViverseClientReady?.Invoke();
                }
                else
                {
                    OnViverseError?.Invoke($"Viverse client not ready: {result}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ViverseCallbackHandler] Error processing client ready result: {e.Message}");
                OnViverseError?.Invoke($"Client ready processing error: {e.Message}");
            }
        }

        // *** Transport-specific callbacks for P2P multiplayer ***
        
        // Called by viverse-bridge.jslib when multiplayer connection is established
        public void OnMultiplayerConnected(string message)
        {
            Debug.Log($"[ViverseCallbackHandler] OnMultiplayerConnected: {message}");
            // The Viverse connection is now ready for bidirectional communication
            // Unity Transport should now be able to send packets back to clients
            OnBroadcastTransportConnected?.Invoke(message);
        }

        // Called by viverse-bridge.jslib when multiplayer connection is lost
        public void OnMultiplayerDisconnected(string reason)
        {
            Debug.Log($"[ViverseCallbackHandler] OnMultiplayerDisconnected: {reason}");
            // Handle disconnection if needed
            OnBroadcastTransportDisconnected?.Invoke(reason);
        }

        // Called by viverse-bridge.jslib when multiplayer connection fails
        public void OnMultiplayerConnectionFailed(string error)
        {
            Debug.LogError($"[ViverseCallbackHandler] OnMultiplayerConnectionFailed: {error}");
            // Handle connection failure if needed
            OnBroadcastTransportError?.Invoke(error);
        }

        // Called by viverse-bridge.jslib when broadcast transport is ready
        public void OnBroadcastTransportReady(string status)
        {
            Debug.Log($"[ViverseCallbackHandler] OnBroadcastTransportReady: {status}");
            // Broadcast transport is now ready for sending/receiving messages
            OnBroadcastTransportConnected?.Invoke(status);
        }

        // Called by viverse-bridge.jslib when a broadcast message is received
        // This is handled by ViverseBroadcastReceiver directly, but we can add logging here
        public void OnBroadcastMessageReceived(string messageInfo)
        {
            Debug.Log($"[ViverseCallbackHandler] Broadcast message received: {messageInfo}");
            // This is primarily for debugging - actual message processing happens in ViverseBroadcastReceiver
        }

        void OnDestroy()
        {
            Debug.Log("[ViverseCallbackHandler] Callback handler destroyed");
        }
    }

    /// <summary>
    /// Data structure representing a Viverse room for discovery
    /// </summary>
    [System.Serializable]
    public class ViverseRoom
    {
        public string id;
        public string app_id;
        public string mode;
        public string name;
        public ViverseActor[] actors;
        public int max_players;
        public int min_players;
        public bool is_closed;
        public object properties; // Can be expanded later
        public string master_client_id;
        public string game_session;
        
        // Helper properties for easier access
        public int currentPlayers => actors?.Length ?? 0;
        public int maxPlayers => max_players;
        public bool isPrivate => is_closed;
        public string hostName
        {
            get
            {
                if (actors != null)
                {
                    foreach (var actor in actors)
                    {
                        if (actor.is_master_client)
                            return actor.name;
                    }
                }
                return null;
            }
        }
    }

    /// <summary>
    /// Data structure representing a Viverse actor/player
    /// </summary>
    [System.Serializable]
    public class ViverseActor
    {
        public string session_id;
        public string name;
        public ViverseActorProperties properties;
        public bool is_master_client;
    }

    /// <summary>
    /// Data structure for Viverse actor properties
    /// </summary>
    [System.Serializable]
    public class ViverseActorProperties
    {
        public int level;
        public bool ready;
    }

    /// <summary>
    /// Container for room discovery response
    /// </summary>
    [System.Serializable]
    public class ViverseRoomsResponse
    {
        public ViverseRoom[] rooms;
    }
} 