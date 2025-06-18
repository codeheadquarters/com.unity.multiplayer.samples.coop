using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.BossRoom.Utils;
using Unity.Netcode.Transports.Viverse;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// VP1-Play connection method that uses VP1-Play for both matchmaking and transport
    /// </summary>
    public class ConnectionMethodViverse : ConnectionMethodBase, IDisposable
    {
        private string m_AppId;
        private string m_RoomId;
        private string m_RoomName;
        private int m_MaxPlayers;
        private Unity.Netcode.Transports.Viverse.ViverseTransport m_ViverseTransport;
        
        // Runtime configuration that survives Unity's serialization
        private TaskCompletionSource<bool> m_InitializationTcs;
        private TaskCompletionSource<RoomInfo> m_RoomCreationTcs;
        private TaskCompletionSource<bool> m_RoomJoinTcs;
        
        // Cleanup tracking
        private bool m_IsDisposed = false;
        
        // Track if we're currently in a VP1-Play room
        private bool m_IsInVP1PlayRoom = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // DllImport declarations for VP1-Play functions
        [DllImport("__Internal")]
        private static extern void initViverseClient();
        
        [DllImport("__Internal")]
        private static extern void initViverseMatchmaking(string appId, int debug);
        
       // [DllImport("__Internal")]
       // private static extern void setupMatchmakingEvents();
        
        [DllImport("__Internal")]
        private static extern void setActor(string actorJson);
        
        [DllImport("__Internal")]
        private static extern void createRoom(string roomConfigJson);
        
        [DllImport("__Internal")]
        private static extern void getAvailableRooms();
        
        [DllImport("__Internal")]
        private static extern void joinRoom(string roomId);
        
        [DllImport("__Internal")]
        private static extern int vp1PlayGetConnectionStatus();

        [DllImport("__Internal")]
        private static extern void LeaveViverseRoom();

        [DllImport("__Internal")]
        private static extern void checkVP1PlayClientReady();

        // Add DllImport for multiplayer client initialization
        [DllImport("__Internal")]
        private static extern void viverseInitMultiplayer(string roomId, string appId);
#endif

        public ConnectionMethodViverse(string appId, string roomName, int maxPlayers, 
            ConnectionManager connectionManager, ProfileManager profileManager, string playerName)
            : base(connectionManager, profileManager, playerName)
        {
            m_AppId = appId;
            m_RoomName = roomName;
            m_MaxPlayers = maxPlayers;
        }

        /// <summary>
        /// Try to get the connection payload for a given client ID
        /// </summary>
        /// <param name="clientId">The client ID to get payload for</param>
        /// <param name="payload">The connection payload if found</param>
        /// <returns>True if payload was found, false otherwise</returns>
        public bool TryGetConnectionPayload(ulong clientId, out byte[] payload)
        {
            return Unity.Netcode.Transports.Viverse.ViverseConnectionManager.TryGetConnectionPayload(clientId, out payload);
        }
        
        /// <summary>
        /// Ensure ViverseBroadcastReceiver GameObject exists for WebGL
        /// </summary>
        private void EnsureViverseBroadcastReceiver()
        {
            var receiverGO = UnityEngine.GameObject.Find("ViverseBroadcastReceiver");
            if (receiverGO == null)
            {
                receiverGO = new UnityEngine.GameObject("ViverseBroadcastReceiver");
                UnityEngine.GameObject.DontDestroyOnLoad(receiverGO);
                
                var receiver = receiverGO.AddComponent<Unity.Netcode.Transports.Viverse.ViverseBroadcastReceiver>();
                Debug.Log("[VP1Play] Created ViverseBroadcastReceiver GameObject for WebGL messaging");
            }
            else
            {
                Debug.Log("[VP1Play] ViverseBroadcastReceiver GameObject already exists");
            }
        }

        public ConnectionMethodViverse(string appId, string roomId, 
            ConnectionManager connectionManager, ProfileManager profileManager, string playerName)
            : base(connectionManager, profileManager, playerName)
        {
            m_AppId = appId;
            m_RoomId = roomId;
        }

        public override async Task SetupHostConnectionAsync()
        {
            Debug.Log("[VP1Play] Setting up host connection...");
            
            SetConnectionPayload(GetPlayerId(), m_PlayerName);

            try
            {
                // Setup custom transport FIRST - before any VP1-Play operations
                // This ensures Unity has a valid transport before validation
                Debug.Log("[VP1Play] Step 0: Setting up VP1-Play transport early...");
                // Use a temporary room ID for initial transport setup
                var tempRoomId = $"temp_room_{GetPlayerId()}";
                m_RoomId = tempRoomId;
                SetupVP1PlayTransport(true);
                Debug.Log("[VP1Play] Step 0: Transport setup complete");
                
                // Initialize VP1-Play client
                Debug.Log("[VP1Play] Step 1: Initializing VP1-Play client...");
                await InitializeVP1PlayClient();
                Debug.Log("[VP1Play] Step 1: VP1-Play client initialization complete");
                
                // Setup actor info
                Debug.Log("[VP1Play] Step 2: Setting up actor...");
                await SetupActor();
                Debug.Log("[VP1Play] Step 2: Actor setup complete");
                
                // Create room
                Debug.Log("[VP1Play] Step 3: Creating room...");
                var roomInfo = await CreateRoom();
                var actualRoomId = roomInfo.id;
                Debug.Log($"[VP1Play] Step 3: Room creation complete - Room ID: {actualRoomId}");
                
                // Update transport with the actual room ID (in case it changed)
                Debug.Log("[VP1Play] Step 4: Updating transport with final room ID...");
                m_RoomId = actualRoomId;
                var connectionData = new ViverseConnectionData
                {
                    AppId = m_AppId,
                    RoomId = m_RoomId,
                    IsHost = true,
                    ConnectTimeoutMS = 10000,
                    MaxConnectionAttempts = 3,
                    EnableDebugLogging = true
                };
                m_ViverseTransport.SetViverseConnectionData(connectionData);
                Debug.Log("[VP1Play] Step 4: Transport update complete");
                
                Debug.Log("[VP1Play] Host setup complete - ready to start NetworkManager");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Failed to setup host: {e.Message}");
                Debug.LogError($"[VP1Play] Stack trace: {e.StackTrace}");
                
                // Cleanup VP1-Play state on error
                await CleanupVP1PlayConnection();
                throw;
            }
        }

        public override async Task SetupClientConnectionAsync()
        {
            Debug.Log("[VP1Play] Setting up client connection...");
            
            SetConnectionPayload(GetPlayerId(), m_PlayerName);

            try
            {
                // Initialize VP1-Play client
                Debug.Log("[VP1Play] Step 1: Initializing VP1-Play client...");
                await InitializeVP1PlayClient();
                Debug.Log("[VP1Play] Step 1: VP1-Play client initialization complete");
                
                // Setup actor info
                Debug.Log("[VP1Play] Step 2: Setting up actor...");
                await SetupActor();
                Debug.Log("[VP1Play] Step 2: Actor setup complete");
                
                // Join existing room
                Debug.Log("[VP1Play] Step 3: Joining room...");
                await JoinRoom();
                Debug.Log($"[VP1Play] Step 3: Joined room: {m_RoomId}");
                
                // Setup custom transport
                Debug.Log("[VP1Play] Step 4: Setting up transport...");
                SetupVP1PlayTransport(false);
                Debug.Log("[VP1Play] Step 4: Transport setup complete");
                
                Debug.Log("[VP1Play] Client setup complete");
            }
            catch (TimeoutException te)
            {
                Debug.LogError($"[VP1Play] Client setup timed out: {te.Message}");
                Debug.LogError($"[VP1Play] This usually indicates VP1-Play callback not received");
                
                // Cleanup VP1-Play state on timeout
                await CleanupVP1PlayConnection();
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Failed to setup client: {e.Message}");
                Debug.LogError($"[VP1Play] Stack trace: {e.StackTrace}");
                
                // Cleanup VP1-Play state on error
                await CleanupVP1PlayConnection();
                throw;
            }
        }

        public override async Task<(bool success, bool shouldTryAgain)> SetupClientReconnectionAsync()
        {
            Debug.Log("[VP1Play] Attempting reconnection...");
            
            try
            {
                // Try to rejoin the same room
                await JoinRoom();
                return (true, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Reconnection failed: {e.Message}");
                
                // Cleanup VP1-Play state on reconnection failure
                await CleanupVP1PlayConnection();
                
                return (false, true); // Failed but should try again
            }
        }

        private async Task InitializeVP1PlayClient()
        {
            Debug.Log("[VP1Play] Initializing Play client...");
            
            // Ensure callback handler exists
            EnsureCallbackHandlerExists();
            
            // Check if already initialized
            bool isInitialized = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                int status = vp1PlayGetConnectionStatus();
                isInitialized = status > 0;
                Debug.Log($"[VP1Play] Current connection status: {status}, isInitialized: {isInitialized}");
            }
            catch
            {
                // Not initialized yet
                Debug.Log("[VP1Play] Connection status check failed - not initialized yet");
            }
            
            // Only do cleanup for CLIENTS joining existing rooms, not HOSTS creating new rooms
            // Hosts are identified by having a room NAME (m_RoomName), clients have a room ID (m_RoomId)
            bool isClientJoiningExistingRoom = !string.IsNullOrEmpty(m_RoomId) && string.IsNullOrEmpty(m_RoomName);
            
            // Check if we need to do cleanup - only if we were previously in a room
            bool needsCleanup = false;
            if (isClientJoiningExistingRoom && isInitialized)
            {
                // Check if we're actually in a room and need cleanup
                try
                {
                    Debug.Log("[VP1Play] Client is joining existing room and VP1-Play is initialized");
                    Debug.Log($"[VP1Play] Currently in VP1-Play room: {m_IsInVP1PlayRoom}");
                    
                    // Only cleanup if we were previously in a room
                    needsCleanup = m_IsInVP1PlayRoom;
                    
                    if (needsCleanup)
                    {
                        Debug.Log("[VP1Play] Client was in a previous room - cleanup needed");
                    }
                    else
                    {
                        Debug.Log("[VP1Play] Client was not in a room - no cleanup needed");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VP1Play] Error checking if cleanup needed: {e.Message}");
                    needsCleanup = false;
                }
            }
            
            if (needsCleanup)
            {
                Debug.Log("[VP1Play] Client needs cleanup - ensuring clean state by leaving any previous room");
                
                try
                {
                    Debug.Log("[VP1Play] Starting cleanup with timeout protection...");
                    // Add timeout to prevent hanging
                    var cleanupTask = EnsureCleanVP1PlayState();
                    var timeoutTask = Task.Delay(3000); // 3 second timeout for cleanup
                    var completedTask = await Task.WhenAny(cleanupTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        Debug.LogWarning("[VP1Play] EnsureCleanVP1PlayState timed out after 3 seconds - continuing anyway");
                    }
                    else
                    {
                        await cleanupTask; // Wait for actual completion
                        Debug.Log("[VP1Play] Clean state operation completed successfully");
                    }
                    
                    Debug.Log("[VP1Play] Cleanup operation finished (with or without timeout)");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VP1Play] Error during clean state operation: {e.Message} - continuing anyway");
                    Debug.LogError($"[VP1Play] Error stack trace: {e.StackTrace}");
                }
            }
            else if (isInitialized)
            {
                Debug.Log("[VP1Play] VP1-Play client already initialized - no cleanup needed (fresh connection)");
            }
            
            if (!isInitialized)
            {
                Debug.Log("[VP1Play] Starting VP1-Play client initialization...");
                
                // Create TaskCompletionSource to wait for initialization
                m_InitializationTcs = new TaskCompletionSource<bool>();
                
                // Subscribe to callback events for initialization
                void OnInitComplete()
                {
                    Debug.Log("[VP1Play] Initialization complete callback received");
                    ViverseCallbackHandler.OnViverseInitComplete -= OnInitComplete;
                    ViverseCallbackHandler.OnViverseError -= OnInitError;
                    m_InitializationTcs?.SetResult(true);
                }
                
                void OnInitError(string error)
                {
                    Debug.LogError($"[VP1Play] Initialization error callback received: {error}");
                    ViverseCallbackHandler.OnViverseInitComplete -= OnInitComplete;
                    ViverseCallbackHandler.OnViverseError -= OnInitError;
                    m_InitializationTcs?.SetException(new Exception($"VP1-Play initialization failed: {error}"));
                }
                
                ViverseCallbackHandler.OnViverseInitComplete += OnInitComplete;
                ViverseCallbackHandler.OnViverseError += OnInitError;
                
                try
                {
                    // Initialize global play client using DllImport
                    Debug.Log("[VP1Play] Calling initPlayClient...");
                    initViverseClient();
                    
                    // Initialize matchmaking client
                    Debug.Log("[VP1Play] Calling initMatchmakingClient...");
                    initViverseMatchmaking(m_AppId, 1); // debug = 1
                    
                    // Setup event listeners
                    Debug.Log("[VP1Play] Calling setupMatchmakingEvents...");
                    //setupMatchmakingEvents();
                    
                    Debug.Log("[VP1Play] All DllImport calls completed, waiting for async initialization...");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VP1Play] DllImport calls failed: {e.Message}");
                    ViverseCallbackHandler.OnViverseInitComplete -= OnInitComplete;
                    ViverseCallbackHandler.OnViverseError -= OnInitError;
                    throw new Exception($"VP1-Play DllImport initialization failed: {e.Message}");
                }
                
                // Wait for async initialization completion with timeout
                var timeoutTask = Task.Delay(10000); // 10 second timeout for initialization
                var completedTask = await Task.WhenAny(m_InitializationTcs.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Cleanup on timeout
                    Debug.LogError("[VP1Play] Initialization timed out after 10 seconds");
                    ViverseCallbackHandler.OnViverseInitComplete -= OnInitComplete;
                    ViverseCallbackHandler.OnViverseError -= OnInitError;
                    throw new TimeoutException("VP1-Play initialization timed out after 10 seconds");
                }
                
                var success = await m_InitializationTcs.Task;
                if (!success)
                {
                    Debug.LogError("[VP1Play] Initialization returned false");
                    throw new Exception("VP1-Play initialization failed");
                }
                
                Debug.Log("[VP1Play] VP1-Play initialization completed successfully via callback");
                
                // Skip post-initialization delay for now - Task.Delay seems problematic in WebGL
                // The state validation in SetupActor should be sufficient
                Debug.Log("[VP1Play] Skipping post-initialization delay - proceeding directly");
            }
            else
            {
                Debug.Log("[VP1Play] VP1-Play client already initialized, skipping initialization");
                
                // Skip stability delays for now - they seem to cause hangs in WebGL
                // The cleanup logic now properly handles when delays are actually needed
                Debug.Log("[VP1Play] Skipping stability delay - proceeding directly to next step");
            }
#else
            // For editor testing, simulate successful initialization
            Debug.Log("[VP1Play] Editor mode - simulating successful initialization");
            // No delay needed in editor mode
            Debug.Log("[VP1Play] Editor mode initialization complete");
#endif
            
            Debug.Log("[VP1Play] Play client initialization complete - proceeding to next step");
        }

        private async Task EnsureCleanVP1PlayState()
        {
            Debug.Log("[VP1Play] Ensuring clean VP1-Play state before joining new room...");
            
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                // Check VP1-Play connection status first
                int connectionStatus = 0;
                try
                {
                    connectionStatus = vp1PlayGetConnectionStatus();
                    Debug.Log($"[VP1Play] Current VP1-Play connection status: {connectionStatus}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VP1Play] Could not check VP1-Play connection status: {e.Message}");
                    // If we can't check status, assume we don't need cleanup
                    Debug.Log("[VP1Play] Skipping cleanup due to status check failure");
                    return;
                }
                
                // Only try to leave room if we have a valid connection
                if (connectionStatus > 0)
                {
                    Debug.Log("[VP1Play] VP1-Play is connected - attempting to leave any current room to ensure clean state...");
                    
                    try
                    {
                        // Use existing vp1PlayLeaveRoom function
                        LeaveViverseRoom();
                        Debug.Log("[VP1Play] Leave room call completed");
                        
                        // Clear the room tracking flag since we left the room
                        m_IsInVP1PlayRoom = false;
                        Debug.Log("[VP1Play] Marked as no longer in room");
                        
                        // VP1-Play leaveRoom is async - no need for manual delay
                        Debug.Log("[VP1Play] Leave room operation initiated - VP1-Play will handle async completion");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[VP1Play] Leave room call failed (this may be expected if not in a room): {e.Message}");
                        // Don't treat this as a fatal error
                    }
                }
                else
                {
                    Debug.Log("[VP1Play] VP1-Play not connected or in invalid state - skipping leave room operation");
                }
                
                Debug.Log("[VP1Play] VP1-Play state cleanup complete");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Error during VP1-Play state cleanup: {e.Message}");
                Debug.LogError($"[VP1Play] Stack trace: {e.StackTrace}");
                // Continue anyway since this is cleanup - the main operation might still work
            }
#else
            Debug.Log("[VP1Play] Editor mode - simulating state cleanup");
            // No delay needed in editor mode
            Debug.Log("[VP1Play] Editor mode cleanup complete");
#endif
        }

        private void EnsureCallbackHandlerExists()
        {
            // Use the centralized method that creates the ViverseCallbackHandler GameObject
            ViverseCallbackHandler.EnsureViverseCallbackHandler();
        }

        private async Task SetupActor()
        {
            Debug.Log($"[VP1Play] Setting up actor: {m_PlayerName}");
            
            // Wait for VP1-Play client to be ready for operations
            await WaitForVP1PlayClientReady();
            
            var playerId = GetPlayerId();
            Debug.Log($"[VP1Play] Player ID: {playerId}");
            Debug.Log($"[VP1Play] Player Name: {m_PlayerName}");
            
            var actorData = new ActorData
            {
                session_id = playerId,
                name = m_PlayerName,
                properties = new ActorProperties
                {
                    level = 1,
                    ready = false
                }
            };
            
            var actorJson = JsonUtility.ToJson(actorData);
            Debug.Log($"[VP1Play] Actor JSON: {actorJson}");
            
            // Create TaskCompletionSource to wait for async callback
            var tcs = new TaskCompletionSource<bool>();
            
            // Subscribe to callback events
            void OnActorComplete(bool success)
            {
                Debug.Log($"[VP1Play] OnActorComplete callback received with success: {success}");
                ViverseCallbackHandler.OnActorSetupComplete -= OnActorComplete;
                ViverseCallbackHandler.OnViverseError -= OnActorError;
                tcs.SetResult(success);
            }
            
            void OnActorError(string error)
            {
                Debug.LogError($"[VP1Play] OnActorError callback received: {error}");
                ViverseCallbackHandler.OnActorSetupComplete -= OnActorComplete;
                ViverseCallbackHandler.OnViverseError -= OnActorError;
                tcs.SetException(new Exception($"VP1-Play actor setup failed: {error}"));
            }
            
            ViverseCallbackHandler.OnActorSetupComplete += OnActorComplete;
            ViverseCallbackHandler.OnViverseError += OnActorError;

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[VP1Play] About to call setActor via DllImport...");
            
            try
            {
                Debug.Log("[VP1Play] Calling setActor - will wait for callback");
                setActor(actorJson);
                Debug.Log("[VP1Play] setActor DllImport call completed - waiting for async callback");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] setActor DllImport call failed: {e.Message}");
                ViverseCallbackHandler.OnActorSetupComplete -= OnActorComplete;
                ViverseCallbackHandler.OnViverseError -= OnActorError;
                throw new Exception($"setActor DllImport failed: {e.Message}");
            }
#else
            // For editor testing, simulate successful actor setup
            Debug.Log("[VP1Play] Editor mode - simulating successful actor setup");
            // No delay needed in editor mode
            OnActorComplete(true);
            return;
#endif
            
            Debug.Log("[VP1Play] Waiting for VP1-Play setActor callback...");
            
            // Wait for async callback - let VP1-Play handle its own timeouts
            var success = await tcs.Task;
            if (!success)
            {
                Debug.LogError("[VP1Play] setActor operation returned false");
                throw new Exception("VP1-Play setActor operation failed");
            }
            
            Debug.Log("[VP1Play] Actor setup completed successfully via callback");
        }

        private async Task WaitForVP1PlayClientReady()
        {
            Debug.Log("[VP1Play] Waiting for VP1-Play client to be ready for operations...");
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // Check if the client is already ready
            try
            {
                int connectionStatus = vp1PlayGetConnectionStatus();
                Debug.Log($"[VP1Play] Current VP1-Play connection status: {connectionStatus}");
                
                if (connectionStatus > 0)
                {
                    Debug.Log("[VP1Play] VP1-Play client already ready");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VP1Play] Could not check VP1-Play status: {e.Message}");
            }
            
            // If not ready, wait using callback-based approach without Task.Delay
            var readinessTcs = new TaskCompletionSource<bool>();
            
            // Subscribe to readiness callback
            void OnClientReady()
            {
                Debug.Log("[VP1Play] VP1-Play client ready callback received");
                ViverseCallbackHandler.OnViverseClientReady -= OnClientReady;
                ViverseCallbackHandler.OnViverseError -= OnReadinessError;
                readinessTcs.SetResult(true);
            }
            
            void OnReadinessError(string error)
            {
                Debug.LogError($"[VP1Play] VP1-Play readiness error: {error}");
                ViverseCallbackHandler.OnViverseClientReady -= OnClientReady;
                ViverseCallbackHandler.OnViverseError -= OnReadinessError;
                readinessTcs.SetException(new Exception($"VP1-Play readiness failed: {error}"));
            }
            
            ViverseCallbackHandler.OnViverseClientReady += OnClientReady;
            ViverseCallbackHandler.OnViverseError += OnReadinessError;
            
            try
            {
                Debug.Log("[VP1Play] Requesting VP1-Play client readiness check...");
                // Call a JavaScript function to check readiness and fire callback when ready
                checkVP1PlayClientReady();
                Debug.Log("[VP1Play] VP1-Play readiness check initiated");
                
                // Wait for readiness with timeout
                var timeoutTask = Task.Delay(5000); // 5 second timeout for readiness
                var completedTask = await Task.WhenAny(readinessTcs.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Cleanup on timeout
                    ViverseCallbackHandler.OnViverseClientReady -= OnClientReady;
                    ViverseCallbackHandler.OnViverseError -= OnReadinessError;
                    Debug.LogWarning("[VP1Play] VP1-Play readiness check timed out - proceeding anyway");
                    return; // Don't throw, just proceed
                }
                
                var isReady = await readinessTcs.Task;
                Debug.Log($"[VP1Play] VP1-Play client readiness confirmed: {isReady}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Error during readiness check: {e.Message}");
                ViverseCallbackHandler.OnViverseClientReady -= OnClientReady;
                ViverseCallbackHandler.OnViverseError -= OnReadinessError;
                // Don't throw, just proceed - the error will be caught in setActor if client isn't ready
                Debug.LogWarning("[VP1Play] Proceeding despite readiness check error");
            }
#else
            // Editor mode - simulate readiness
            Debug.Log("[VP1Play] Editor mode - simulating client readiness");
#endif
            
            Debug.Log("[VP1Play] VP1-Play client readiness check complete");
        }

        private async Task<RoomInfo> CreateRoom()
        {
            Debug.Log($"[VP1Play] Creating room: {m_RoomName}");
            
            var roomConfig = new RoomConfig
            {
                name = m_RoomName,
                mode = "team",
                maxPlayers = m_MaxPlayers,
                minPlayers = 1,
                properties = new RoomProperties { }
            };
            
            var configJson = JsonUtility.ToJson(roomConfig);
            Debug.Log($"[VP1Play] Room config JSON: {configJson}");
            
            // Create TaskCompletionSource to wait for async callback
            var tcs = new TaskCompletionSource<string>();
            
            // Subscribe to callback events
            void OnRoomCreated(string roomId)
            {
                ViverseCallbackHandler.OnRoomCreated -= OnRoomCreated;
                ViverseCallbackHandler.OnViverseError -= OnRoomError;
                tcs.SetResult(roomId);
            }
            
            void OnRoomError(string error)
            {
                ViverseCallbackHandler.OnRoomCreated -= OnRoomCreated;
                ViverseCallbackHandler.OnViverseError -= OnRoomError;
                tcs.SetException(new Exception($"VP1-Play room creation failed: {error}"));
            }
            
            ViverseCallbackHandler.OnRoomCreated += OnRoomCreated;
            ViverseCallbackHandler.OnViverseError += OnRoomError;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Create room via VP1-Play using DllImport
            Debug.Log("[VP1Play] Calling createRoom via JavaScript bridge...");
            createRoom(configJson);
            Debug.Log("[VP1Play] createRoom call completed");
#endif
            
            Debug.Log("[VP1Play] Waiting for VP1-Play createRoom callback...");
            
            // Wait for async callback with timeout
            var timeoutTask = Task.Delay(10000); // 10 second timeout for room creation
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                // Cleanup on timeout
                ViverseCallbackHandler.OnRoomCreated -= OnRoomCreated;
                ViverseCallbackHandler.OnViverseError -= OnRoomError;
                throw new TimeoutException("VP1-Play createRoom operation timed out after 10 seconds");
            }
            
            var roomId = await tcs.Task;
            Debug.Log($"[VP1Play] Room created successfully with ID: {roomId}");
            
            // Mark that we're now in a VP1-Play room
            m_IsInVP1PlayRoom = true;
            
            var roomInfo = new RoomInfo
            {
                id = roomId,
                name = m_RoomName,
                maxPlayers = m_MaxPlayers
            };
            
            // Initialize VP1-Play multiplayer client for broadcast transport after room creation
            await InitializeVP1PlayMultiplayer(roomId);
            
            Debug.Log($"[VP1Play] Returning room info: {JsonUtility.ToJson(roomInfo)} - marked as in room");
            return roomInfo;
        }

        private async Task JoinRoom()
        {
            Debug.Log($"[VP1Play] Joining room: {m_RoomId}");
            
            // Create TaskCompletionSource to wait for async callback (use class field)
            m_RoomJoinTcs = new TaskCompletionSource<bool>();
            
            // Subscribe to callback events
            void OnRoomJoined(bool success)
            {
                ViverseCallbackHandler.OnRoomJoined -= OnRoomJoined;
                ViverseCallbackHandler.OnViverseError -= OnJoinError;
                m_RoomJoinTcs?.SetResult(success);
            }
            
            void OnJoinError(string error)
            {
                ViverseCallbackHandler.OnRoomJoined -= OnRoomJoined;
                ViverseCallbackHandler.OnViverseError -= OnJoinError;
                m_RoomJoinTcs?.SetException(new Exception($"VP1-Play room join failed: {error}"));
            }
            
            ViverseCallbackHandler.OnRoomJoined += OnRoomJoined;
            ViverseCallbackHandler.OnViverseError += OnJoinError;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Join room via VP1-Play using DllImport
            Debug.Log("[VP1Play] Calling joinRoom via JavaScript bridge...");
            joinRoom(m_RoomId);
            Debug.Log("[VP1Play] joinRoom call completed");
#else
            // For editor testing, simulate successful join
            await Task.Delay(100);
            OnRoomJoined(true);
            return;
#endif
            
            Debug.Log("[VP1Play] Waiting for VP1-Play joinRoom callback...");
            
            // Wait for async callback with timeout
            var timeoutTask = Task.Delay(10000); // 10 second timeout for room join
            var completedTask = await Task.WhenAny(m_RoomJoinTcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                // Cleanup on timeout
                ViverseCallbackHandler.OnRoomJoined -= OnRoomJoined;
                ViverseCallbackHandler.OnViverseError -= OnJoinError;
                throw new TimeoutException("VP1-Play joinRoom operation timed out after 10 seconds");
            }
            
            var success = await m_RoomJoinTcs.Task;
            if (!success)
            {
                throw new Exception("VP1-Play joinRoom operation failed");
            }
            
            // Mark that we're now in a VP1-Play room
            m_IsInVP1PlayRoom = true;
            Debug.Log("[VP1Play] Room join completed successfully via callback - marked as in room");
            
            // Initialize VP1-Play multiplayer client for broadcast transport after room join
            await InitializeVP1PlayMultiplayer(m_RoomId);
        }

        private void SetupVP1PlayTransport(bool isHost)
        {
            Debug.Log($"[VP1Play] Setting up transport (isHost: {isHost})");
            
            // Check if we already have a transport instance and can reuse it
            var existingTransport = m_ConnectionManager.NetworkManager?.NetworkConfig?.NetworkTransport as ViverseTransport;
            
            if (existingTransport != null)
            {
                Debug.Log("[VP1Play] Reusing existing ViverseTransport and updating configuration");
                m_ViverseTransport = existingTransport;
                
                // Update the existing transport with the current room configuration
                var connectionData = new ViverseConnectionData
                {
                    AppId = m_AppId,
                    RoomId = m_RoomId,
                    IsHost = isHost,
                    ConnectTimeoutMS = 10000,
                    MaxConnectionAttempts = 3,
                    EnableDebugLogging = true
                };
                m_ViverseTransport.SetViverseConnectionData(connectionData);
            }
            else
            {
                Debug.Log("[VP1Play] Creating new ViverseTransport");
                
                // Check if NetworkManager exists and get or add the component
                if (m_ConnectionManager.NetworkManager == null)
                {
                    Debug.LogError("[VP1Play] NetworkManager is null!");
                    return;
                }
                
                // Try to get existing ViverseTransport component or add one
                m_ViverseTransport = m_ConnectionManager.NetworkManager.gameObject.GetComponent<ViverseTransport>();
                if (m_ViverseTransport == null)
                {
                    m_ViverseTransport = m_ConnectionManager.NetworkManager.gameObject.AddComponent<ViverseTransport>();
                }
                
                // Configure Viverse transport with the room created by ConnectionMethodViverse
                var connectionData = new ViverseConnectionData
                {
                    AppId = m_AppId,
                    RoomId = m_RoomId,
                    IsHost = isHost,
                    ConnectTimeoutMS = 10000,
                    MaxConnectionAttempts = 3,
                    EnableDebugLogging = true
                };
                m_ViverseTransport.SetViverseConnectionData(connectionData);
            }
            
            // Ensure transport is valid before assignment
            if (m_ViverseTransport == null)
            {
                Debug.LogError("[VP1Play] Failed to create ViverseTransport!");
                return;
            }
            
            // Check if NetworkManager exists
            if (m_ConnectionManager.NetworkManager == null)
            {
                Debug.LogError("[VP1Play] NetworkManager is null!");
                return;
            }
            
            // Check if NetworkConfig exists
            if (m_ConnectionManager.NetworkManager.NetworkConfig == null)
            {
                Debug.LogError("[VP1Play] NetworkConfig is null!");
                return;
            }
            
            // Set as the network transport (only if it's not already set to avoid recreating)
            if (m_ConnectionManager.NetworkManager.NetworkConfig.NetworkTransport != m_ViverseTransport)
            {
                Debug.Log("[VP1Play] About to assign transport to NetworkManager...");
                m_ConnectionManager.NetworkManager.NetworkConfig.NetworkTransport = m_ViverseTransport;
                Debug.Log("[VP1Play] Transport assignment complete");
            }
            else
            {
                Debug.Log("[VP1Play] Transport already assigned to NetworkManager, skipping assignment");
            }
            
            // Verify the transport was set correctly
            var setTransport = m_ConnectionManager.NetworkManager.NetworkConfig.NetworkTransport;
            if (setTransport == m_ViverseTransport)
            {
                Debug.Log($"[VP1Play] ViverseTransport successfully assigned to NetworkManager");
                Debug.Log($"[VP1Play] Transport type: {setTransport.GetType().Name}");
                Debug.Log($"[VP1Play] Transport IsSupported: {setTransport.IsSupported}");
                Debug.Log($"[VP1Play] Configured for room: {m_RoomId}");
            }
            else
            {
                Debug.LogError($"[VP1Play] Transport assignment failed! Set: {setTransport?.GetType().Name}, Expected: {m_ViverseTransport?.GetType().Name}");
            }
        }

        // Callback methods that could be called from JavaScript (for future enhancement)
        public void OnVP1PlayRoomCreated(string roomDataJson)
        {
            Debug.Log($"[VP1Play] Room created callback: {roomDataJson}");
            
            try
            {
                var roomData = JsonUtility.FromJson<RoomInfo>(roomDataJson);
                m_RoomCreationTcs?.SetResult(roomData);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] Failed to parse room creation data: {e.Message}");
                m_RoomCreationTcs?.SetException(e);
            }
        }

        public void OnVP1PlayRoomJoined(string roomId)
        {
            Debug.Log($"[VP1Play] Room joined callback: {roomId}");
            m_RoomJoinTcs?.SetResult(true);
        }

        public void OnVP1PlayError(string errorMessage)
        {
            Debug.LogError($"[VP1Play] Error callback: {errorMessage}");
            
            m_InitializationTcs?.SetException(new Exception(errorMessage));
            m_RoomCreationTcs?.SetException(new Exception(errorMessage));
            m_RoomJoinTcs?.SetException(new Exception(errorMessage));
        }

        // Override GetPlayerId to ensure host and client get different IDs when running on same machine
        protected new string GetPlayerId()
        {
            var baseId = base.GetPlayerId();
            
            // Add suffix to differentiate between host and client instances
            // This prevents duplicate player ID conflicts when testing on same machine
            if (!string.IsNullOrEmpty(m_RoomName))
            {
                // If we have a room name, we're setting up as host
                return $"{baseId}_host";
            }
            else if (!string.IsNullOrEmpty(m_RoomId))
            {
                // If we have a room ID (for joining), we're setting up as client
                return $"{baseId}_client_{UnityEngine.Random.Range(1000, 9999)}";
            }
            
            // Fallback - shouldn't happen in normal flow
            return $"{baseId}_{System.DateTime.UtcNow.Ticks % 10000}";
        }

        private async Task CleanupVP1PlayConnection()
        {
            Debug.Log("[VP1Play] Cleaning up VP1-Play connection...");
            
            // Clear the room tracking flag since we're cleaning up
            m_IsInVP1PlayRoom = false;
            Debug.Log("[VP1Play] Cleared room tracking flag during cleanup");
            
            // Clean up VP1-Play state
            await EnsureCleanVP1PlayState();
            
            // Clean up transport
            m_ViverseTransport = null;
            
            Debug.Log("[VP1Play] VP1-Play connection cleanup complete");
        }

        [Serializable]
        public class ActorData
        {
            public string session_id;
            public string name;
            public ActorProperties properties;
        }

        [Serializable]
        public class ActorProperties
        {
            public int level;
            public bool ready;
        }

        [Serializable]
        public class RoomConfig
        {
            public string name;
            public string mode;
            public int maxPlayers;
            public int minPlayers;
            public RoomProperties properties;
        }

        [Serializable]
        public class RoomProperties
        {
            // Empty for now, can be expanded later
        }

        [Serializable]
        public class RoomInfo
        {
            public string id;
            public string name;
            public int maxPlayers;
        }

        public void Dispose()
        {
            if (!m_IsDisposed)
            {
                m_IsDisposed = true;
                // Don't automatically cleanup VP1-Play connection on disposal
                // Only cleanup on explicit shutdown/disconnect operations
                Debug.Log("[VP1Play] ConnectionMethodVP1Play disposed (no automatic cleanup)");
            }
        }
        
        // Remove destructor to prevent automatic cleanup
        // ~ConnectionMethodVP1Play()
        // {
        //     Dispose();
        // }
        
        // Add explicit cleanup method for when we actually want to disconnect
        public async Task ExplicitCleanupAsync()
        {
            Debug.Log("[VP1Play] Performing explicit VP1-Play cleanup...");
            await CleanupVP1PlayConnection();
            m_IsInVP1PlayRoom = false;
            Debug.Log("[VP1Play] Explicit cleanup complete - marked as not in room");
        }
        
        private void CleanupVP1PlayConnectionSync()
        {
            // Only log that sync cleanup was requested, don't actually leave room
            Debug.Log("[VP1Play] Sync cleanup requested (skipping automatic leave room)");
            
            // Clean up transport reference but don't leave VP1-Play room
            m_ViverseTransport = null;
            
            Debug.Log("[VP1Play] VP1-Play connection sync cleanup complete (no room leave)");
        }

        private async Task InitializeVP1PlayMultiplayer(string roomId)
        {
            Debug.Log("[VP1Play] Initializing VP1-Play multiplayer client...");
            
            // Ensure callback handler exists
            EnsureCallbackHandlerExists();
            
            // Create TaskCompletionSource to wait for initialization
            m_InitializationTcs = new TaskCompletionSource<bool>();
            
            // Subscribe to callback events for initialization
            void OnMultiplayerInitComplete()
            {
                Debug.Log("[VP1Play] Multiplayer initialization complete callback received");
                ViverseCallbackHandler.OnViverseInitComplete -= OnMultiplayerInitComplete;
                ViverseCallbackHandler.OnViverseError -= OnMultiplayerInitError;
                m_InitializationTcs?.SetResult(true);
            }
            
            void OnMultiplayerInitError(string error)
            {
                Debug.LogError($"[VP1Play] Multiplayer initialization error callback received: {error}");
                ViverseCallbackHandler.OnViverseInitComplete -= OnMultiplayerInitComplete;
                ViverseCallbackHandler.OnViverseError -= OnMultiplayerInitError;
                m_InitializationTcs?.SetException(new Exception($"VP1-Play multiplayer initialization failed: {error}"));
            }
            
            ViverseCallbackHandler.OnViverseInitComplete += OnMultiplayerInitComplete;
            ViverseCallbackHandler.OnViverseError += OnMultiplayerInitError;
            
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // Ensure ViverseBroadcastReceiver exists before multiplayer initialization
                EnsureViverseBroadcastReceiver();
                
                // Initialize multiplayer client using DllImport
                Debug.Log("[VP1Play] Calling viverseInitMultiplayer...");
                viverseInitMultiplayer(roomId, m_AppId);
                
                Debug.Log("[VP1Play] All DllImport calls completed, waiting for async initialization...");
#else
                // For editor testing, simulate successful multiplayer initialization
                Debug.Log("[VP1Play] Editor mode - simulating successful multiplayer initialization");
                OnMultiplayerInitComplete();
                return;
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"[VP1Play] DllImport calls failed: {e.Message}");
                ViverseCallbackHandler.OnViverseInitComplete -= OnMultiplayerInitComplete;
                ViverseCallbackHandler.OnViverseError -= OnMultiplayerInitError;
                throw new Exception($"VP1-Play multiplayer DllImport initialization failed: {e.Message}");
            }
            
            // Wait for async initialization completion with timeout
            var timeoutTask = Task.Delay(10000); // 10 second timeout for initialization
            var completedTask = await Task.WhenAny(m_InitializationTcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                // Cleanup on timeout
                Debug.LogError("[VP1Play] Multiplayer initialization timed out after 10 seconds");
                ViverseCallbackHandler.OnViverseInitComplete -= OnMultiplayerInitComplete;
                ViverseCallbackHandler.OnViverseError -= OnMultiplayerInitError;
                throw new TimeoutException("VP1-Play multiplayer initialization timed out after 10 seconds");
            }
            
            var success = await m_InitializationTcs.Task;
            if (!success)
            {
                Debug.LogError("[VP1Play] Multiplayer initialization returned false");
                throw new Exception("VP1-Play multiplayer initialization failed");
            }
            
            Debug.Log("[VP1Play] VP1-Play multiplayer initialization completed successfully via callback");
            
            // Notify Unity Netcode that the broadcast transport is ready for connection
            // This will trigger the connection simulation in the transport layer
            // VP1PlayConnectionNotifier not available in Viverse - connection ready handled by transport
            Debug.Log("[ConnectionMethodViverse] Client connection ready");
            Debug.Log("[VP1Play] Notified Unity Netcode that VP1-Play broadcast transport is ready");
        }
    }
} 