using System;
using System.Collections;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Infrastructure;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server specialization of Character Select game state.
    /// </summary>
    [RequireComponent(typeof(NetcodeHooks), typeof(NetworkCharSelection))]
    public class ServerCharSelectState : GameStateBehaviour
    {
        [SerializeField]
        NetcodeHooks m_NetcodeHooks;

        public override GameState ActiveState => GameState.CharSelect;
        public NetworkCharSelection networkCharSelection { get; private set; }

        Coroutine m_WaitToEndLobbyCoroutine;

        [Inject]
        ConnectionManager m_ConnectionManager;

        protected override void Awake()
        {
            base.Awake();
            networkCharSelection = GetComponent<NetworkCharSelection>();

            m_NetcodeHooks.OnNetworkSpawnHook += OnNetworkSpawn;
            m_NetcodeHooks.OnNetworkDespawnHook += OnNetworkDespawn;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (m_NetcodeHooks)
            {
                m_NetcodeHooks.OnNetworkSpawnHook -= OnNetworkSpawn;
                m_NetcodeHooks.OnNetworkDespawnHook -= OnNetworkDespawn;
            }
        }

        void OnClientChangedSeat(ulong clientId, int newSeatIdx, bool lockedIn)
        {
            int idx = FindLobbyPlayerIdx(clientId);
            if (idx == -1)
            {
                throw new Exception($"OnClientChangedSeat: client ID {clientId} is not a lobby player and cannot change seats! Shouldn't be here!");
            }

            if (networkCharSelection.IsLobbyClosed.Value)
            {
                // The user tried to change their class after everything was locked in... too late! Discard this choice
                return;
            }

            if (newSeatIdx == -1)
            {
                // we can't lock in with no seat
                lockedIn = false;
            }
            else
            {
                // see if someone has already locked-in that seat! If so, too late... discard this choice
                foreach (NetworkCharSelection.LobbyPlayerState playerInfo in networkCharSelection.LobbyPlayers)
                {
                    if (playerInfo.ClientId != clientId && playerInfo.SeatIdx == newSeatIdx && playerInfo.SeatState == NetworkCharSelection.SeatState.LockedIn)
                    {
                        // somebody already locked this choice in. Stop!
                        // Instead of granting lock request, change this player to Inactive state.
                        networkCharSelection.LobbyPlayers[idx] = new NetworkCharSelection.LobbyPlayerState(clientId,
                            networkCharSelection.LobbyPlayers[idx].PlayerName,
                            networkCharSelection.LobbyPlayers[idx].PlayerNumber,
                            NetworkCharSelection.SeatState.Inactive);

                        // then early out
                        return;
                    }
                }
            }

            networkCharSelection.LobbyPlayers[idx] = new NetworkCharSelection.LobbyPlayerState(clientId,
                networkCharSelection.LobbyPlayers[idx].PlayerName,
                networkCharSelection.LobbyPlayers[idx].PlayerNumber,
                lockedIn ? NetworkCharSelection.SeatState.LockedIn : NetworkCharSelection.SeatState.Active,
                newSeatIdx,
                Time.time);

            if (lockedIn)
            {
                // to help the clients visually keep track of who's in what seat, we'll "kick out" any other players
                // who were also in that seat. (Those players didn't click "Ready!" fast enough, somebody else took their seat!)
                for (int i = 0; i < networkCharSelection.LobbyPlayers.Count; ++i)
                {
                    if (networkCharSelection.LobbyPlayers[i].SeatIdx == newSeatIdx && i != idx)
                    {
                        // change this player to Inactive state.
                        networkCharSelection.LobbyPlayers[i] = new NetworkCharSelection.LobbyPlayerState(
                            networkCharSelection.LobbyPlayers[i].ClientId,
                            networkCharSelection.LobbyPlayers[i].PlayerName,
                            networkCharSelection.LobbyPlayers[i].PlayerNumber,
                            NetworkCharSelection.SeatState.Inactive);
                    }
                }
            }

            CloseLobbyIfReady();
        }

        /// <summary>
        /// Returns the index of a client in the master LobbyPlayer list, or -1 if not found
        /// </summary>
        int FindLobbyPlayerIdx(ulong clientId)
        {
            for (int i = 0; i < networkCharSelection.LobbyPlayers.Count; ++i)
            {
                if (networkCharSelection.LobbyPlayers[i].ClientId == clientId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Looks through all our connections and sees if everyone has locked in their choice;
        /// if so, we lock in the whole lobby, save state, and begin the transition to gameplay
        /// </summary>
        void CloseLobbyIfReady()
        {
            foreach (NetworkCharSelection.LobbyPlayerState playerInfo in networkCharSelection.LobbyPlayers)
            {
                if (playerInfo.SeatState != NetworkCharSelection.SeatState.LockedIn)
                    return; // nope, at least one player isn't locked in yet!
            }

            // everybody's ready at the same time! Lock it down!
            networkCharSelection.IsLobbyClosed.Value = true;

            // remember our choices so the next scene can use the info
            SaveLobbyResults();

            // Delay a few seconds to give the UI time to react, then switch scenes
            m_WaitToEndLobbyCoroutine = StartCoroutine(WaitToEndLobby());
        }

        /// <summary>
        /// Cancels the process of closing the lobby, so that if a new player joins, they are able to chose a character.
        /// </summary>
        void CancelCloseLobby()
        {
            if (m_WaitToEndLobbyCoroutine != null)
            {
                StopCoroutine(m_WaitToEndLobbyCoroutine);
            }
            networkCharSelection.IsLobbyClosed.Value = false;
        }

        void SaveLobbyResults()
        {
            foreach (NetworkCharSelection.LobbyPlayerState playerInfo in networkCharSelection.LobbyPlayers)
            {
                var playerNetworkObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerInfo.ClientId);

                if (playerNetworkObject && playerNetworkObject.TryGetComponent(out PersistentPlayer persistentPlayer))
                {
                    // pass avatar GUID to PersistentPlayer
                    // it'd be great to simplify this with something like a NetworkScriptableObjects :(
                    persistentPlayer.NetworkAvatarGuidState.AvatarGuid.Value =
                        networkCharSelection.AvatarConfiguration[playerInfo.SeatIdx].Guid.ToNetworkGuid();
                }
            }
        }

        IEnumerator WaitToEndLobby()
        {
            yield return new WaitForSeconds(3);
            SceneLoaderWrapper.Instance.LoadScene("BossRoom", useNetworkSceneManager: true);
        }

        public void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectCallback;
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
            if (networkCharSelection)
            {
                networkCharSelection.OnClientChangedSeat -= OnClientChangedSeat;
            }
        }

        public void OnNetworkSpawn()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                enabled = false;
            }
            else
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
                networkCharSelection.OnClientChangedSeat += OnClientChangedSeat;

                NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
            }
        }

        void OnSceneEvent(SceneEvent sceneEvent)
        {
            // We need to filter out the event that are not a client has finished loading the scene
            if (sceneEvent.SceneEventType != SceneEventType.LoadComplete) return;
            // When the client finishes loading the Lobby Map, we will need to Seat it
            SeatNewPlayer(sceneEvent.ClientId);
        }

        int GetAvailablePlayerNumber()
        {
            for (int possiblePlayerNumber = 0; possiblePlayerNumber < m_ConnectionManager.MaxConnectedPlayers; ++possiblePlayerNumber)
            {
                if (IsPlayerNumberAvailable(possiblePlayerNumber))
                {
                    return possiblePlayerNumber;
                }
            }
            // we couldn't get a Player# for this person... which means the lobby is full!
            return -1;
        }

        bool IsPlayerNumberAvailable(int playerNumber)
        {
            bool found = false;
            foreach (NetworkCharSelection.LobbyPlayerState playerState in networkCharSelection.LobbyPlayers)
            {
                if (playerState.PlayerNumber == playerNumber)
                {
                    found = true;
                    break;
                }
            }

            return !found;
        }

        void SeatNewPlayer(ulong clientId)
        {
            // Check if this client is already in the lobby (for reconnections)
            int existingIdx = FindLobbyPlayerIdx(clientId);
            if (existingIdx != -1)
            {
                Debug.Log($"[ServerCharSelectState] Client {clientId} already in lobby at index {existingIdx} - skipping duplicate seating");
                return;
            }
            
            // If lobby is closing and waiting to start the game, cancel to allow that new player to select a character
            if (networkCharSelection.IsLobbyClosed.Value)
            {
                CancelCloseLobby();
            }

            SessionPlayerData? sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            if (sessionPlayerData.HasValue)
            {
                var playerData = sessionPlayerData.Value;
                if (playerData.PlayerNumber == -1 || !IsPlayerNumberAvailable(playerData.PlayerNumber))
                {
                    // If no player num already assigned or if player num is no longer available, get an available one.
                    playerData.PlayerNumber = GetAvailablePlayerNumber();
                }
                if (playerData.PlayerNumber == -1)
                {
                    // Sanity check. We ran out of seats... there was no room!
                    throw new Exception($"we shouldn't be here, connection approval should have refused this connection already for client ID {clientId} and player num {playerData.PlayerNumber}");
                }

                Debug.Log($"[ServerCharSelectState] Seating new player - Client {clientId}, Player: {playerData.PlayerName}, PlayerNumber: {playerData.PlayerNumber}");
                networkCharSelection.LobbyPlayers.Add(new NetworkCharSelection.LobbyPlayerState(clientId, playerData.PlayerName, playerData.PlayerNumber, NetworkCharSelection.SeatState.Inactive));
                SessionManager<SessionPlayerData>.Instance.SetPlayerData(clientId, playerData);
            }
            else
            {
                Debug.LogError($"[ServerCharSelectState] No session data found for client {clientId} - cannot seat player");
            }
        }

        void OnClientDisconnectCallback(ulong clientId)
        {
            Debug.Log($"[ServerCharSelectState] OnClientDisconnectCallback called for client {clientId}");
            Debug.Log($"[ServerCharSelectState] Current lobby players before removal: {networkCharSelection.LobbyPlayers.Count}");
            
            // Log current players
            for (int j = 0; j < networkCharSelection.LobbyPlayers.Count; ++j)
            {
                var player = networkCharSelection.LobbyPlayers[j];
                Debug.Log($"[ServerCharSelectState] Player {j}: ClientId={player.ClientId}, Name={player.PlayerName}, PlayerNumber={player.PlayerNumber}");
            }
            
            // clear this client's PlayerNumber and any associated visuals (so other players know they're gone).
            bool playerRemoved = false;
            for (int i = 0; i < networkCharSelection.LobbyPlayers.Count; ++i)
            {
                if (networkCharSelection.LobbyPlayers[i].ClientId == clientId)
                {
                    var removedPlayer = networkCharSelection.LobbyPlayers[i];
                    Debug.Log($"[ServerCharSelectState] Removing player from lobby: ClientId={removedPlayer.ClientId}, Name={removedPlayer.PlayerName}, PlayerNumber={removedPlayer.PlayerNumber}");
                    networkCharSelection.LobbyPlayers.RemoveAt(i);
                    playerRemoved = true;
                    break;
                }
            }

            if (!playerRemoved)
            {
                Debug.LogWarning($"[ServerCharSelectState] Client {clientId} not found in lobby players list - may have already been removed");
            }
            else
            {
                Debug.Log($"[ServerCharSelectState] Player removed successfully. Remaining lobby players: {networkCharSelection.LobbyPlayers.Count}");
                
                // Log remaining players
                for (int j = 0; j < networkCharSelection.LobbyPlayers.Count; ++j)
                {
                    var player = networkCharSelection.LobbyPlayers[j];
                    Debug.Log($"[ServerCharSelectState] Remaining Player {j}: ClientId={player.ClientId}, Name={player.PlayerName}, PlayerNumber={player.PlayerNumber}");
                }
            }

            if (!networkCharSelection.IsLobbyClosed.Value)
            {
                // If the lobby is not already closing, close if the remaining players are all ready
                Debug.Log($"[ServerCharSelectState] Lobby not closed, checking if remaining players are ready");
                CloseLobbyIfReady();
            }
            else
            {
                Debug.Log($"[ServerCharSelectState] Lobby is already closed, skipping ready check");
            }
        }

        void OnClientConnectedCallback(ulong clientId)
        {
            Debug.Log($"[ServerCharSelectState] Client {clientId} connected to lobby");
            
            // Small delay to allow Unity NetCode to fully initialize the client connection
            // before attempting to seat them in the lobby
            StartCoroutine(DelayedClientSeating(clientId));
        }
        
        System.Collections.IEnumerator DelayedClientSeating(ulong clientId)
        {
            // Wait a frame to ensure Unity NetCode has completed client initialization
            yield return null;
            
            // Check if client needs to be seated (for reconnections or late joins)
            int existingIdx = FindLobbyPlayerIdx(clientId);
            if (existingIdx == -1)
            {
                Debug.Log($"[ServerCharSelectState] Seating client {clientId} after connection");
                SeatNewPlayer(clientId);
            }
            else
            {
                Debug.Log($"[ServerCharSelectState] Client {clientId} already seated at index {existingIdx} - refreshing state");
                
                // For reconnecting clients, trigger a state update by temporarily modifying the player state
                // This forces Unity NetCode to resync the lobby state to the reconnecting client
                var currentState = networkCharSelection.LobbyPlayers[existingIdx];
                var tempState = new NetworkCharSelection.LobbyPlayerState(
                    currentState.ClientId,
                    currentState.PlayerName,
                    currentState.PlayerNumber,
                    currentState.SeatState,
                    currentState.SeatIdx,
                    Time.time // Update timestamp to force a network update
                );
                networkCharSelection.LobbyPlayers[existingIdx] = tempState;
            }
        }
    }
}
