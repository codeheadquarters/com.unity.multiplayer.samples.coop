using System;
using Unity.BossRoom.Gameplay.Configuration;
using TMPro;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.UnityServices.Auth;
using Unity.BossRoom.UnityServices.Lobbies;
using Unity.Services.Core;
using UnityEngine;
using VContainer;
using System.Collections.Generic;

namespace Unity.BossRoom.Gameplay.UI
{
    public class LobbyUIMediator : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] LobbyJoiningUI m_LobbyJoiningUI;
        [SerializeField] LobbyCreationUI m_LobbyCreationUI;
        [SerializeField] UITinter m_JoinToggleHighlight;
        [SerializeField] UITinter m_JoinToggleTabBlocker;
        [SerializeField] UITinter m_CreateToggleHighlight;
        [SerializeField] UITinter m_CreateToggleTabBlocker;
        [SerializeField] TextMeshProUGUI m_PlayerNameLabel;
        [SerializeField] GameObject m_LoadingSpinner;

        AuthenticationServiceFacade m_AuthenticationServiceFacade;
        LobbyServiceFacade m_LobbyServiceFacade;
        LocalLobbyUser m_LocalUser;
        LocalLobby m_LocalLobby;
        NameGenerationData m_NameGenerationData;
        ConnectionManager m_ConnectionManager;
        ISubscriber<ConnectStatus> m_ConnectStatusSubscriber;
        IPublisher<LobbyListFetchedMessage> m_LobbyListFetchedPub;

        const string k_DefaultLobbyName = "no-name";

        [Inject]
        void InjectDependenciesAndInitialize(
            AuthenticationServiceFacade authenticationServiceFacade,
            LobbyServiceFacade lobbyServiceFacade,
            LocalLobbyUser localUser,
            LocalLobby localLobby,
            NameGenerationData nameGenerationData,
            ISubscriber<ConnectStatus> connectStatusSub,
            ConnectionManager connectionManager,
            IPublisher<LobbyListFetchedMessage> lobbyListFetchedPub
        )
        {
            m_AuthenticationServiceFacade = authenticationServiceFacade;
            m_NameGenerationData = nameGenerationData;
            m_LocalUser = localUser;
            m_LobbyServiceFacade = lobbyServiceFacade;
            m_LocalLobby = localLobby;
            m_ConnectionManager = connectionManager;
            m_ConnectStatusSubscriber = connectStatusSub;
            m_LobbyListFetchedPub = lobbyListFetchedPub;
            RegenerateName();

            m_ConnectStatusSubscriber.Subscribe(OnConnectStatus);
        }

        void OnConnectStatus(ConnectStatus status)
        {
            if (status is ConnectStatus.GenericDisconnect or ConnectStatus.StartClientFailed)
            {
                UnblockUIAfterLoadingIsComplete();
            }
        }

        void OnDestroy()
        {
            if (m_ConnectStatusSubscriber != null)
            {
                m_ConnectStatusSubscriber.Unsubscribe(OnConnectStatus);
            }
        }

        //Lobby and Relay calls done from UI

        public async void CreateLobbyRequest(string lobbyName, bool isPrivate)
        {
            // before sending request to lobby service, populate an empty lobby name, if necessary
            if (string.IsNullOrEmpty(lobbyName))
            {
                lobbyName = k_DefaultLobbyName;
            }

            BlockUIWhileLoadingIsInProgress();

#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play directly instead of Unity Services
            Debug.Log($"[LobbyUIMediator] Creating VP1-Play room: {lobbyName}");
            
            // Set up local user as host for VP1-Play
            m_LocalUser.IsHost = true;
            
            // Use VP1-Play connection method directly
            m_ConnectionManager.StartHostLobby(m_LocalUser.DisplayName);
#else
            // For other platforms, use Unity Authentication and Lobby services
            bool playerIsAuthorized = await m_AuthenticationServiceFacade.EnsurePlayerIsAuthorized();

            if (!playerIsAuthorized)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            var lobbyCreationAttempt = await m_LobbyServiceFacade.TryCreateLobbyAsync(lobbyName, m_ConnectionManager.MaxConnectedPlayers, isPrivate);

            if (lobbyCreationAttempt.Success)
            {
                m_LocalUser.IsHost = true;
                m_LobbyServiceFacade.SetRemoteLobby(lobbyCreationAttempt.Lobby);

                Debug.Log($"Created lobby with ID: {m_LocalLobby.LobbyID} and code {m_LocalLobby.LobbyCode}");
                m_ConnectionManager.StartHostLobby(m_LocalUser.DisplayName);
            }
            else
            {
                UnblockUIAfterLoadingIsComplete();
            }
#endif
        }

        public async void QueryLobbiesRequest(bool blockUI)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play room discovery
            Debug.Log("[LobbyUIMediator] Starting VP1-Play room discovery...");
            
            if (blockUI)
            {
                BlockUIWhileLoadingIsInProgress();
            }

            try
            {
                // Use VP1-Play room discovery service
                var roomDiscovery = new Unity.BossRoom.ConnectionManagement.VP1PlayRoomDiscovery();
                var vp1Rooms = await roomDiscovery.GetAvailableRoomsAsync();
                
                // Convert VP1-Play rooms to Unity LocalLobby format
                var localLobbies = roomDiscovery.ConvertToLocalLobbies(vp1Rooms);
                
                // Publish the lobby list using the same system as Unity Lobby service
                m_LobbyListFetchedPub.Publish(new LobbyListFetchedMessage(localLobbies));
                
                Debug.Log($"[LobbyUIMediator] VP1-Play room discovery complete - found {localLobbies.Count} rooms");
                
                // Cleanup
                roomDiscovery.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LobbyUIMediator] VP1-Play room discovery failed: {e.Message}");
                // Publish empty list on error
                m_LobbyListFetchedPub.Publish(new LobbyListFetchedMessage(new List<LocalLobby>()));
            }
            finally
            {
                if (blockUI)
                {
                    UnblockUIAfterLoadingIsComplete();
                }
            }
#else
            // For other platforms, use Unity Lobby services
            if (Unity.Services.Core.UnityServices.State != ServicesInitializationState.Initialized)
            {
                return;
            }

            if (blockUI)
            {
                BlockUIWhileLoadingIsInProgress();
            }

            bool playerIsAuthorized = await m_AuthenticationServiceFacade.EnsurePlayerIsAuthorized();

            if (blockUI && !playerIsAuthorized)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            await m_LobbyServiceFacade.RetrieveAndPublishLobbyListAsync();

            if (blockUI)
            {
                UnblockUIAfterLoadingIsComplete();
            }
#endif
        }

        public async void JoinLobbyWithCodeRequest(string lobbyCode)
        {
            BlockUIWhileLoadingIsInProgress();

#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play to join room by code
            Debug.Log($"[LobbyUIMediator] Joining VP1-Play room with code: {lobbyCode}");
            
            // Set up local user as client
            m_LocalUser.IsHost = false;
            
            // For VP1-Play, treat the lobby code as the room ID
            m_LocalLobby.RelayJoinCode = lobbyCode; // VP1-Play room ID
            m_LocalLobby.LobbyID = lobbyCode;
            m_LocalLobby.LobbyCode = lobbyCode;
            
            // Use VP1-Play connection method directly
            m_ConnectionManager.StartClientLobby(m_LocalUser.DisplayName);
#else
            // For other platforms, use Unity Authentication and Lobby services
            bool playerIsAuthorized = await m_AuthenticationServiceFacade.EnsurePlayerIsAuthorized();

            if (!playerIsAuthorized)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            var result = await m_LobbyServiceFacade.TryJoinLobbyAsync(null, lobbyCode);

            if (result.Success)
            {
                OnJoinedLobby(result.Lobby);
            }
            else
            {
                UnblockUIAfterLoadingIsComplete();
            }
#endif
        }

        public async void JoinLobbyRequest(LocalLobby lobby)
        {
            BlockUIWhileLoadingIsInProgress();

#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play to join room
            Debug.Log($"[LobbyUIMediator] Joining VP1-Play room: {lobby.LobbyName} (ID: {lobby.LobbyID})");
            
            // Set up local user as client
            m_LocalUser.IsHost = false;
            
            // For VP1-Play, set the room ID as the RelayJoinCode so OfflineState can use it
            m_LocalLobby.RelayJoinCode = lobby.LobbyID; // VP1-Play room ID
            m_LocalLobby.LobbyID = lobby.LobbyID;
            m_LocalLobby.LobbyName = lobby.LobbyName;
            
            // Use VP1-Play connection method directly
            m_ConnectionManager.StartClientLobby(m_LocalUser.DisplayName);
#else
            // For other platforms, use Unity Authentication and Lobby services
            bool playerIsAuthorized = await m_AuthenticationServiceFacade.EnsurePlayerIsAuthorized();

            if (!playerIsAuthorized)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            var result = await m_LobbyServiceFacade.TryJoinLobbyAsync(lobby.LobbyID, lobby.LobbyCode);

            if (result.Success)
            {
                OnJoinedLobby(result.Lobby);
            }
            else
            {
                UnblockUIAfterLoadingIsComplete();
            }
#endif
        }

        public async void QuickJoinRequest()
        {
            BlockUIWhileLoadingIsInProgress();

#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play quick join
            Debug.Log("[LobbyUIMediator] VP1-Play quick join not implemented yet");
            UnblockUIAfterLoadingIsComplete();
#else
            // For other platforms, use Unity Authentication and Lobby services
            bool playerIsAuthorized = await m_AuthenticationServiceFacade.EnsurePlayerIsAuthorized();

            if (!playerIsAuthorized)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            var result = await m_LobbyServiceFacade.TryQuickJoinLobbyAsync();

            if (result.Success)
            {
                OnJoinedLobby(result.Lobby);
            }
            else
            {
                UnblockUIAfterLoadingIsComplete();
            }
#endif
        }

        void OnJoinedLobby(Unity.Services.Lobbies.Models.Lobby remoteLobby)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // Only for non-WebGL platforms
            m_LobbyServiceFacade.SetRemoteLobby(remoteLobby);

            Debug.Log($"Joined lobby with code: {m_LocalLobby.LobbyCode}, Internal Relay Join Code{m_LocalLobby.RelayJoinCode}");
            m_ConnectionManager.StartClientLobby(m_LocalUser.DisplayName);
#endif
        }

        //show/hide UI

        public void Show()
        {
            m_CanvasGroup.alpha = 1f;
            m_CanvasGroup.blocksRaycasts = true;
        }

        public void Hide()
        {
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.blocksRaycasts = false;
            m_LobbyCreationUI.Hide();
            m_LobbyJoiningUI.Hide();
        }

        public void ToggleJoinLobbyUI()
        {
            m_LobbyJoiningUI.Show();
            m_LobbyCreationUI.Hide();
            m_JoinToggleHighlight.SetToColor(1);
            m_JoinToggleTabBlocker.SetToColor(1);
            m_CreateToggleHighlight.SetToColor(0);
            m_CreateToggleTabBlocker.SetToColor(0);
        }

        public void ToggleCreateLobbyUI()
        {
            m_LobbyJoiningUI.Hide();
            m_LobbyCreationUI.Show();
            m_JoinToggleHighlight.SetToColor(0);
            m_JoinToggleTabBlocker.SetToColor(0);
            m_CreateToggleHighlight.SetToColor(1);
            m_CreateToggleTabBlocker.SetToColor(1);
        }

        public void RegenerateName()
        {
            m_LocalUser.DisplayName = m_NameGenerationData.GenerateName();
            m_PlayerNameLabel.text = m_LocalUser.DisplayName;
        }

        void BlockUIWhileLoadingIsInProgress()
        {
            m_CanvasGroup.interactable = false;
            m_LoadingSpinner.SetActive(true);
        }

        void UnblockUIAfterLoadingIsComplete()
        {
            //this callback can happen after we've already switched to a different scene
            //in that case the canvas group would be null
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.interactable = true;
                m_LoadingSpinner.SetActive(false);
            }
        }
    }
}
