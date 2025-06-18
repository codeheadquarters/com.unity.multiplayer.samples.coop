using System;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.UnityServices.Lobbies;
using Unity.BossRoom.Utils;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to when the NetworkManager is shut down. From this state we can transition to the
    /// ClientConnecting sate, if starting as a client, or the StartingHost state, if starting as a host.
    /// </summary>
    class OfflineState : ConnectionState
    {
        [Inject]
        LobbyServiceFacade m_LobbyServiceFacade;
        [Inject]
        ProfileManager m_ProfileManager;
        [Inject]
        LocalLobby m_LocalLobby;

        // Viverse configuration
        [SerializeField] private string m_ViverseAppId = "example_game_boss"; // Set this in inspector or config
        
        const string k_MainMenuSceneName = "MainMenu";

        public override void Enter()
        {
            m_LobbyServiceFacade.EndTracking();
            m_ConnectionManager.NetworkManager.Shutdown();
            if (SceneManager.GetActiveScene().name != k_MainMenuSceneName)
            {
                SceneLoaderWrapper.Instance.LoadScene(k_MainMenuSceneName, useNetworkSceneManager: false);
            }
        }

        public override void Exit() { }

        public override void StartClientIP(string playerName, string ipaddress, int port)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, use VP1-Play instead of direct IP
            StartClientVP1Play(playerName, ipaddress); // Use ipaddress as roomId for VP1-Play
#else
            var connectionMethod = new ConnectionMethodIP(ipaddress, (ushort)port, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
#endif
        }

        public override void StartClientLobby(string playerName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, use VP1-Play instead of Unity Relay
            StartClientVP1Play(playerName, m_LocalLobby.RelayJoinCode); // Use join code as roomId
#else
            var connectionMethod = new ConnectionMethodRelay(m_LobbyServiceFacade, m_LocalLobby, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
#endif
        }

        public override void StartHostIP(string playerName, string ipaddress, int port)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, use VP1-Play instead of direct IP
            StartHostVP1Play(playerName, $"room_{playerName}_{UnityEngine.Random.Range(1000, 9999)}", 4);
#else
            var connectionMethod = new ConnectionMethodIP(ipaddress, (ushort)port, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
#endif
        }

        public override void StartHostLobby(string playerName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL, use VP1-Play instead of Unity Relay
            StartHostVP1Play(playerName, m_LocalLobby.LobbyName, m_LocalLobby.MaxPlayerCount);
#else
            var connectionMethod = new ConnectionMethodRelay(m_LobbyServiceFacade, m_LocalLobby, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void StartClientVP1Play(string playerName, string roomId)
        {
            Debug.Log($"[OfflineState] Starting VP1-Play client for room: {roomId}");
            var connectionMethod = new ConnectionMethodViverse(m_ViverseAppId, roomId, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
        }

        private void StartHostVP1Play(string playerName, string roomName, int maxPlayers)
        {
            // Provide fallback values if room parameters are empty
            if (string.IsNullOrEmpty(roomName))
            {
                roomName = $"Room_{playerName}_{UnityEngine.Random.Range(1000, 9999)}";
            }
            
            if (maxPlayers <= 0)
            {
                maxPlayers = 4; // Default max players for Boss Room
            }
            
            Debug.Log($"[OfflineState] Starting VP1-Play host for room: {roomName} (max players: {maxPlayers})");
            var connectionMethod = new ConnectionMethodViverse(m_ViverseAppId, roomName, maxPlayers, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
        }
#endif
    }
}
