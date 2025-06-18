using System;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.UnityServices.Lobbies;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using Unity.Netcode;
using UnityEngine;
using VContainer;
using System.Collections.Generic;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to a listening host. Handles incoming client connections. When shutting down or
    /// being timed out, transitions to the Offline state.
    /// </summary>
    class HostingState : OnlineState
    {
        [Inject]
        LobbyServiceFacade m_LobbyServiceFacade;
        [Inject]
        IPublisher<ConnectionEventMessage> m_ConnectionEventPublisher;

        // used in ApprovalCheck. This is intended as a bit of light protection against DOS attacks that rely on sending silly big buffers of garbage.
        const int k_MaxConnectPayload = 1024;
        
        // Store Viverse connection method for proper cleanup on shutdown
        private ConnectionMethodViverse m_ViverseConnectionMethod;

        /// <summary>
        /// Sets the Viverse connection method for this hosting state to manage
        /// </summary>
        /// <param name="viverseConnectionMethod">The Viverse connection method to manage</param>
        public void SetViverseConnectionMethod(ConnectionMethodViverse viverseConnectionMethod)
        {
            m_ViverseConnectionMethod = viverseConnectionMethod;
            Debug.Log("[HostingState] Viverse connection method set for proper cleanup management");
        }

        public override void Enter()
        {
            //The "BossRoom" server always advances to CharSelect immediately on start. Different games
            //may do this differently.
            SceneLoaderWrapper.Instance.LoadScene("CharSelect", useNetworkSceneManager: true);

            if (m_LobbyServiceFacade.CurrentUnityLobby != null)
            {
                m_LobbyServiceFacade.BeginTracking();
            }
        }

        public override void Exit()
        {
            SessionManager<SessionPlayerData>.Instance.OnServerEnded();
            
            // Clean up VP1-Play connection if not already done
            if (m_ViverseConnectionMethod != null)
            {
                Debug.Log("[HostingState] Cleaning up VP1-Play connection on state exit");
                try
                {
                    // Perform synchronous cleanup
                    m_ViverseConnectionMethod.ExplicitCleanupAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HostingState] Error during VP1-Play cleanup on exit: {e.Message}");
                }
                
                m_ViverseConnectionMethod.Dispose();
                m_ViverseConnectionMethod = null;
            }
        }

        public override void OnClientConnected(ulong clientId)
        {
            var playerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            if (playerData != null)
            {
                m_ConnectionEventPublisher.Publish(new ConnectionEventMessage() { ConnectStatus = ConnectStatus.Success, PlayerName = playerData.Value.PlayerName });
            }
            else
            {
                // This should not happen since player data is assigned during connection approval
                Debug.LogError($"No player data associated with client {clientId}");
                var reason = JsonUtility.ToJson(ConnectStatus.GenericDisconnect);
                m_ConnectionManager.NetworkManager.DisconnectClient(clientId, reason);
            }

        }

        public override void OnClientDisconnect(ulong clientId)
        {
            if (clientId != m_ConnectionManager.NetworkManager.LocalClientId)
            {
                var playerId = SessionManager<SessionPlayerData>.Instance.GetPlayerId(clientId);
                if (playerId != null)
                {
                    var sessionData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(playerId);
                    if (sessionData.HasValue)
                    {
                        m_ConnectionEventPublisher.Publish(new ConnectionEventMessage() { ConnectStatus = ConnectStatus.GenericDisconnect, PlayerName = sessionData.Value.PlayerName });
                    }
                    SessionManager<SessionPlayerData>.Instance.DisconnectClient(clientId);
                }
            }
        }

        public override void OnUserRequestedShutdown()
        {
            // If we have a VP1-Play connection method, clean it up properly
            if (m_ViverseConnectionMethod != null)
            {
                Debug.Log("[HostingState] Cleaning up VP1-Play connection on host shutdown");
                // Use the explicit cleanup method that actually leaves the VP1-Play room
                try
                {
                    // Perform synchronous cleanup since this is called during shutdown
                    m_ViverseConnectionMethod.ExplicitCleanupAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HostingState] Error during VP1-Play cleanup: {e.Message}");
                }
                
                // Now dispose the connection method
                m_ViverseConnectionMethod.Dispose();
                m_ViverseConnectionMethod = null;
                
                Debug.Log("[HostingState] VP1-Play connection cleanup complete");
            }
            
            var reason = JsonUtility.ToJson(ConnectStatus.HostEndedSession);
            for (var i = m_ConnectionManager.NetworkManager.ConnectedClientsIds.Count - 1; i >= 0; i--)
            {
                var id = m_ConnectionManager.NetworkManager.ConnectedClientsIds[i];
                if (id != m_ConnectionManager.NetworkManager.LocalClientId)
                {
                    m_ConnectionManager.NetworkManager.DisconnectClient(id, reason);
                }
            }
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
        }

        public override void OnServerStopped()
        {
            // Clean up VP1-Play connection if not already done
            if (m_ViverseConnectionMethod != null)
            {
                Debug.Log("[HostingState] Cleaning up VP1-Play connection on server stopped");
                try
                {
                    // Perform synchronous cleanup
                    m_ViverseConnectionMethod.ExplicitCleanupAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HostingState] Error during VP1-Play cleanup on server stopped: {e.Message}");
                }
                
                m_ViverseConnectionMethod.Dispose();
                m_ViverseConnectionMethod = null;
            }
            
            m_ConnectStatusPublisher.Publish(ConnectStatus.GenericDisconnect);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
        }

        /// <summary>
        /// This logic plugs into the "ConnectionApprovalResponse" exposed by Netcode.NetworkManager. It is run every time a client connects to us.
        /// The complementary logic that runs when the client starts its connection can be found in ClientConnectingState.
        /// </summary>
        /// <remarks>
        /// Multiple things can be done here, some asynchronously. For example, it could authenticate your user against an auth service like UGS' auth service. It can
        /// also send custom messages to connecting users before they receive their connection result (this is useful to set status messages client side
        /// when connection is refused, for example).
        /// Note on authentication: It's usually harder to justify having authentication in a client hosted game's connection approval. Since the host can't be trusted,
        /// clients shouldn't send it private authentication tokens you'd usually send to a dedicated server.
        /// </remarks>
        /// <param name="request"> The initial request contains, among other things, binary data passed into StartClient. In our case, this is the client's GUID,
        /// which is a unique identifier for their install of the game that persists across app restarts.
        ///  <param name="response"> Our response to the approval process. In case of connection refusal with custom return message, we delay using the Pending field.
        public override void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var connectionData = request.Payload;
            var clientId = request.ClientNetworkId;
            
            Debug.Log($"[HostingState] ApprovalCheck called for client {clientId} with payload length: {connectionData.Length}");
            
            if (connectionData.Length > k_MaxConnectPayload)
            {
                Debug.LogWarning($"[HostingState] Rejecting client {clientId} - payload too large: {connectionData.Length} > {k_MaxConnectPayload}");
                response.Approved = false;
                return;
            }

            // Try to get Viverse connection payload first
            if (m_ViverseConnectionMethod.TryGetConnectionPayload(clientId, out var viversePayload))
            {
                Debug.Log($"[HostingState] Found Viverse connection payload for client {clientId}");
                connectionData = viversePayload;
            }

            var payload = System.Text.Encoding.UTF8.GetString(connectionData);
            var connectionPayload = JsonUtility.FromJson<ConnectionPayload>(payload);

            // Check if server is full
            if (m_ConnectionManager.NetworkManager.ConnectedClientsIds.Count >= m_ConnectionManager.MaxConnectedPlayers)
            {
                Debug.LogWarning($"[HostingState] Rejecting client {clientId} - server is full");
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Position = null;
                response.Rotation = null;
                response.Pending = false;
                return;
            }

            // Check if player is already connected
            if (SessionManager<SessionPlayerData>.Instance.IsDuplicateConnection(connectionPayload.playerId))
            {
                Debug.LogWarning($"[HostingState] Rejecting client {clientId} - duplicate connection");
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Position = null;
                response.Rotation = null;
                response.Pending = false;
                return;
            }

            // Approve the connection
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = null;
            response.Rotation = null;
            response.Pending = false;

            // Store the player data
            var sessionPlayerData = new SessionPlayerData(clientId, connectionPayload.playerName, new NetworkGuid(), 0, true);
            SessionManager<SessionPlayerData>.Instance.SetupConnectingPlayerSessionData(clientId, connectionPayload.playerId, sessionPlayerData);

            Debug.Log($"[HostingState] Approved connection for client {clientId} with player ID {connectionPayload.playerId}");
        }

        ConnectStatus GetConnectStatus(ConnectionPayload connectionPayload)
        {
            if (m_ConnectionManager.NetworkManager.ConnectedClientsIds.Count >= m_ConnectionManager.MaxConnectedPlayers)
            {
                Debug.LogWarning($"[HostingState] Server full - connected: {m_ConnectionManager.NetworkManager.ConnectedClientsIds.Count}, max: {m_ConnectionManager.MaxConnectedPlayers}");
                return ConnectStatus.ServerFull;
            }

            // TODO: Temporarily disabled for VP1-Play development - re-enable for production
            // VP1-Play clients may have different build types during development
            /*
            if (connectionPayload.isDebug != Debug.isDebugBuild)
            {
                return ConnectStatus.IncompatibleBuildType;
            }
            */
            
            // Log the debug mismatch for debugging purposes
            if (connectionPayload.isDebug != Debug.isDebugBuild)
            {
                Debug.LogWarning($"[HostingState] Debug build mismatch - Host: {Debug.isDebugBuild}, Client: {connectionPayload.isDebug} (allowing connection for VP1-Play development)");
            }

            // Check if this is a Viverse transport
            var transport = m_ConnectionManager.NetworkManager.NetworkConfig.NetworkTransport;
            if (transport is Unity.Netcode.Transports.Viverse.ViverseTransport)
            {
                Debug.Log("[HostingState] Viverse transport detected in GetConnectStatus - allowing connection");
                // For Viverse, we allow all connections since the transport handles its own management
                // Duplicate detection is handled in the main ApprovalCheck method
                return ConnectStatus.Success;
            }
            else
            {
                // Original duplicate detection logic for non-VP1-Play transports
                var isDuplicate = SessionManager<SessionPlayerData>.Instance.IsDuplicateConnection(connectionPayload.playerId);
                if (isDuplicate)
                {
                    Debug.LogWarning($"[HostingState] Duplicate connection detected for playerId {connectionPayload.playerId}");
                    return ConnectStatus.LoggedInAgain;
                }
            }
            
            return ConnectStatus.Success;
        }

        void DisconnectPlayer(ulong clientId, ConnectStatus disconnectReason)
        {
            var sessionData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            if (sessionData.HasValue)
            {
                m_ConnectionEventPublisher.Publish(new ConnectionEventMessage() { ConnectStatus = disconnectReason, PlayerName = sessionData.Value.PlayerName });
            }

            var reason = JsonUtility.ToJson(disconnectReason);
            m_ConnectionManager.NetworkManager.DisconnectClient(clientId, reason);
        }
        
        /// <summary>
        /// Cleans up NetworkObjects owned by a client to prevent stale object state synchronization issues on reconnection
        /// </summary>
        /// <param name="clientId">The client ID whose NetworkObjects should be cleaned up</param>
        private void CleanupNetworkObjectsForClient(ulong clientId)
        {
            try
            {
                Debug.Log($"[HostingState] Cleaning up NetworkObjects for client {clientId}");
                
                var networkManager = m_ConnectionManager.NetworkManager;
                if (networkManager == null || !networkManager.IsServer)
                {
                    Debug.LogWarning($"[HostingState] Cannot cleanup NetworkObjects - NetworkManager not available or not server");
                    return;
                }
                
                // Get all spawned NetworkObjects and find ones owned by this client
                var spawnedObjects = new List<NetworkObject>();
                foreach (var spawnedObject in networkManager.SpawnManager.SpawnedObjects.Values)
                {
                    if (spawnedObject.OwnerClientId == clientId)
                    {
                        spawnedObjects.Add(spawnedObject);
                    }
                }
                
                // Despawn NetworkObjects owned by this client
                foreach (var networkObject in spawnedObjects)
                {
                    Debug.Log($"[HostingState] Despawning NetworkObject {networkObject.name} (ID: {networkObject.NetworkObjectId}) owned by client {clientId}");
                    
                    // Despawn the object
                    try
                    {
                        networkObject.Despawn(true); // destroyGameObject = true
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[HostingState] Failed to despawn NetworkObject {networkObject.name}: {e.Message}");
                    }
                }
                
                Debug.Log($"[HostingState] Cleaned up {spawnedObjects.Count} NetworkObjects for client {clientId}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HostingState] Error during NetworkObject cleanup for client {clientId}: {e.Message}");
            }
        }
    }
}
