using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.BossRoom.UnityServices.Lobbies;
using Unity.BossRoom.Infrastructure;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Service for discovering VP1-Play rooms and converting them to Unity's lobby format
    /// </summary>
    public class VP1PlayRoomDiscovery
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void getAvailableRooms();
#endif

        private TaskCompletionSource<ViverseRoom[]> m_RoomDiscoveryTcs;

        public VP1PlayRoomDiscovery()
        {
            // Subscribe to room discovery events
            ViverseCallbackHandler.OnRoomsDiscovered += OnRoomsDiscovered;
            ViverseCallbackHandler.OnViverseError += OnViverseError;
        }

        /// <summary>
        /// Retrieve available VP1-Play rooms asynchronously
        /// </summary>
        public async Task<ViverseRoom[]> GetAvailableRoomsAsync()
        {
            Debug.Log("[VP1PlayRoomDiscovery] Starting room discovery...");

            // Ensure callback handler exists
            EnsureCallbackHandlerExists();

            // Create TaskCompletionSource for async operation
            m_RoomDiscoveryTcs = new TaskCompletionSource<ViverseRoom[]>();

#if UNITY_WEBGL && !UNITY_EDITOR
            // Call JavaScript function to get available rooms
            getAvailableRooms();
#else
            // For non-WebGL builds, return empty list
            m_RoomDiscoveryTcs.SetResult(new ViverseRoom[0]);
#endif

            // Wait for callback with timeout
            var timeoutTask = Task.Delay(5000); // 5 second timeout
            var completedTask = await Task.WhenAny(m_RoomDiscoveryTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Debug.LogWarning("[VP1PlayRoomDiscovery] Room discovery timed out after 5 seconds");
                return new ViverseRoom[0];
            }

            var result = await m_RoomDiscoveryTcs.Task;
            Debug.Log($"[VP1PlayRoomDiscovery] Discovered {result.Length} rooms");
            return result;
        }

        /// <summary>
        /// Convert VP1-Play rooms to Unity LocalLobby format
        /// </summary>
        public List<LocalLobby> ConvertToLocalLobbies(ViverseRoom[] vp1Rooms)
        {
            var localLobbies = new List<LocalLobby>();

            Debug.Log($"[VP1PlayRoomDiscovery] Converting {vp1Rooms.Length} VP1-Play rooms to LocalLobby format");

            foreach (var room in vp1Rooms)
            {
                Debug.Log($"[VP1PlayRoomDiscovery] Processing room: ID='{room.id}', Name='{room.name}', CurrentPlayers={room.currentPlayers}, MaxPlayers={room.maxPlayers}, IsPrivate={room.isPrivate}, Host='{room.hostName}'");

                var localLobby = new LocalLobby
                {
                    LobbyID = room.id,
                    LobbyName = room.name,
                    LobbyCode = room.id, // Use room ID as lobby code for VP1-Play
                    MaxPlayerCount = room.maxPlayers,
                    Private = room.isPrivate
                };

                Debug.Log($"[VP1PlayRoomDiscovery] Created LocalLobby - ID: {localLobby.LobbyID}, Name: {localLobby.LobbyName}, MaxPlayers: {localLobby.MaxPlayerCount}");

                // Add dummy users to represent the current player count from VP1-Play
                // This ensures the UI shows the correct "currentPlayers/maxPlayers" display
                Debug.Log($"[VP1PlayRoomDiscovery] About to add {room.currentPlayers} dummy users for room '{room.name}'");
                
                for (int i = 0; i < room.currentPlayers; i++)
                {
                    var dummyUser = new LocalLobbyUser
                    {
                        ID = $"vp1_player_{room.id}_{i}",
                        DisplayName = i == 0 && !string.IsNullOrEmpty(room.hostName) 
                            ? room.hostName 
                            : $"Player {i + 1}",
                        IsHost = i == 0 // First player is typically the host
                    };
                    
                    Debug.Log($"[VP1PlayRoomDiscovery] Adding dummy user {i}: ID='{dummyUser.ID}', Name='{dummyUser.DisplayName}', IsHost={dummyUser.IsHost}");
                    localLobby.AddUser(dummyUser);
                }

                Debug.Log($"[VP1PlayRoomDiscovery] Final LocalLobby PlayerCount: {localLobby.PlayerCount}, MaxPlayerCount: {localLobby.MaxPlayerCount}");
                Debug.Log($"[VP1PlayRoomDiscovery] Created lobby '{room.name}' with {localLobby.PlayerCount}/{localLobby.MaxPlayerCount} players");
                localLobbies.Add(localLobby);
            }

            Debug.Log($"[VP1PlayRoomDiscovery] Conversion complete - returning {localLobbies.Count} LocalLobby objects");
            return localLobbies;
        }

        private void OnRoomsDiscovered(ViverseRoom[] rooms)
        {
            Debug.Log($"[VP1PlayRoomDiscovery] OnRoomsDiscovered callback received with {rooms.Length} rooms");
            m_RoomDiscoveryTcs?.SetResult(rooms);
        }

        private void OnViverseError(string error)
        {
            Debug.LogError($"[VP1PlayRoomDiscovery] VP1-Play error during room discovery: {error}");
            m_RoomDiscoveryTcs?.SetException(new Exception($"VP1-Play room discovery failed: {error}"));
        }

        private void EnsureCallbackHandlerExists()
        {
            // Use the centralized method that creates the VP1PlayCallbackHandler GameObject
            ViverseCallbackHandler.EnsureViverseCallbackHandler();
        }

        public void Dispose()
        {
            // Unsubscribe from events
            ViverseCallbackHandler.OnRoomsDiscovered -= OnRoomsDiscovered;
            ViverseCallbackHandler.OnViverseError -= OnViverseError;
        }
    }
} 