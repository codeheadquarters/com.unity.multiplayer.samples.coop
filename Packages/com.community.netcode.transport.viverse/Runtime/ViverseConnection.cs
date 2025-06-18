using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Netcode.Transports.Viverse
{
    /// <summary>
    /// Connection states for Viverse transport
    /// </summary>
    public enum ViverseConnectionState : byte
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Disconnecting = 3
    }

    /// <summary>
    /// Connection role in Viverse transport
    /// </summary>
    public enum ViverseConnectionRole : byte
    {
        Client = 0,
        Server = 1
    }

    /// <summary>
    /// Represents a connection in the Viverse transport system
    /// </summary>
    public struct ViverseConnectionInfo
    {
        /// <summary>
        /// Unique client ID for this connection
        /// </summary>
        public ulong ClientId;
        
        /// <summary>
        /// Current connection state
        /// </summary>
        public ViverseConnectionState State;
        
        /// <summary>
        /// Connection role (client or server)
        /// </summary>
        public ViverseConnectionRole Role;
        
        /// <summary>
        /// Timestamp when connection was created
        /// </summary>
        public double CreateTimeStamp;
        
        /// <summary>
        /// Timestamp of last activity on this connection
        /// </summary>
        public float LastActivity;
        
        /// <summary>
        /// Viverse player ID associated with this connection
        /// </summary>
        public FixedString64Bytes PlayerId;
        
        /// <summary>
        /// Last activity timestamp for connection timeout detection
        /// </summary>
        public double LastActivityTime;
        
        /// <summary>
        /// Connection payload data
        /// </summary>
        public NativeArray<byte> ConnectionPayload;

        /// <summary>
        /// Check if this connection is valid and active
        /// </summary>
        public bool IsValid => State == ViverseConnectionState.Connected;

        /// <summary>
        /// Check if this connection has timed out
        /// </summary>
        public bool HasTimedOut(double currentTime, double timeoutSeconds = 30.0)
        {
            return currentTime - LastActivityTime > timeoutSeconds;
        }

        /// <summary>
        /// Update the last activity time
        /// </summary>
        public void UpdateActivity(double currentTime)
        {
            LastActivityTime = currentTime;
        }

        /// <summary>
        /// Set the connection payload
        /// </summary>
        public void SetConnectionPayload(byte[] payload)
        {
            if (ConnectionPayload.IsCreated)
                ConnectionPayload.Dispose();
                
            if (payload != null && payload.Length > 0)
            {
                ConnectionPayload = new NativeArray<byte>(payload.Length, Allocator.Persistent);
                for (int i = 0; i < payload.Length; i++)
                {
                    ConnectionPayload[i] = payload[i];
                }
            }
        }

        /// <summary>
        /// Dispose connection resources
        /// </summary>
        public void Dispose()
        {
            if (ConnectionPayload.IsCreated)
                ConnectionPayload.Dispose();
        }
    }

    /// <summary>
    /// Manages connections for the Viverse transport
    /// </summary>
    public static class ViverseConnectionManager
    {
        /// <summary>
        /// Maximum number of concurrent connections
        /// </summary>
        public const int MaxConnections = 64;
        
        /// <summary>
        /// Mapping from Viverse player IDs to client IDs
        /// </summary>
        private static NativeHashMap<FixedString64Bytes, ulong> s_PlayerIdToClientId 
            = new NativeHashMap<FixedString64Bytes, ulong>(MaxConnections, Allocator.Persistent);
        
        /// <summary>
        /// Mapping from client IDs to connection info
        /// </summary>
        private static NativeHashMap<ulong, ViverseConnectionInfo> s_ConnectionInfo 
            = new NativeHashMap<ulong, ViverseConnectionInfo>(MaxConnections, Allocator.Persistent);
        
        /// <summary>
        /// Set to track announced player connections
        /// </summary>
        private static readonly NativeHashSet<FixedString64Bytes> s_AnnouncedPlayers
            = new NativeHashSet<FixedString64Bytes>(MaxConnections, Allocator.Persistent);

        /// <summary>
        /// Next available client ID
        /// </summary>
        private static ulong s_NextClientId = 1000; // Start from 1000 to avoid conflicts

        /// <summary>
        /// Initialize the connection manager
        /// </summary>
        public static void Initialize()
        {
            if (!s_PlayerIdToClientId.IsCreated)
            {
                // Collections are already initialized above
                Debug.Log("[ViverseConnectionManager] Initialized");
            }
        }

        /// <summary>
        /// Cleanup the connection manager
        /// </summary>
        public static void Cleanup()
        {
            // Dispose all connection payloads first
            if (s_ConnectionInfo.IsCreated)
            {
                foreach (var kvp in s_ConnectionInfo)
                {
                    var connectionInfo = kvp.Value;
                    connectionInfo.Dispose();
                }
                s_ConnectionInfo.Clear();
            }

            if (s_PlayerIdToClientId.IsCreated)
                s_PlayerIdToClientId.Dispose();
            if (s_ConnectionInfo.IsCreated)
                s_ConnectionInfo.Dispose();
            if (s_AnnouncedPlayers.IsCreated)
                s_AnnouncedPlayers.Dispose();
                
            Debug.Log("[ViverseConnectionManager] Cleaned up");
        }

        /// <summary>
        /// Create a new connection for a Viverse player
        /// </summary>
        public static ulong CreateConnection(string playerId, ViverseConnectionRole role, byte[] connectionPayload = null)
        {
            var playerIdFixed = new FixedString64Bytes(playerId);
            
            // Check if player already has a connection
            if (s_PlayerIdToClientId.TryGetValue(playerIdFixed, out var existingClientId))
            {
                Debug.LogWarning($"[ViverseConnectionManager] Player {playerId} already has connection {existingClientId}");
                return existingClientId;
            }

            var clientId = s_NextClientId++;
            var connectionInfo = new ViverseConnectionInfo
            {
                ClientId = clientId,
                State = ViverseConnectionState.Connecting,
                Role = role,
                CreateTimeStamp = Time.timeAsDouble,
                PlayerId = playerIdFixed,
                LastActivityTime = Time.timeAsDouble
            };

            if (connectionPayload != null)
            {
                connectionInfo.SetConnectionPayload(connectionPayload);
            }

            s_PlayerIdToClientId[playerIdFixed] = clientId;
            s_ConnectionInfo[clientId] = connectionInfo;

            Debug.Log($"[ViverseConnectionManager] Created connection {clientId} for player {playerId} as {role}");
            return clientId;
        }

        /// <summary>
        /// Get connection info by client ID
        /// </summary>
        public static bool TryGetConnection(ulong clientId, out ViverseConnectionInfo connectionInfo)
        {
            return s_ConnectionInfo.TryGetValue(clientId, out connectionInfo);
        }

        /// <summary>
        /// Get client ID by player ID
        /// </summary>
        public static bool TryGetClientId(string playerId, out ulong clientId)
        {
            var playerIdFixed = new FixedString64Bytes(playerId);
            return s_PlayerIdToClientId.TryGetValue(playerIdFixed, out clientId);
        }

        /// <summary>
        /// Update connection state
        /// </summary>
        public static bool UpdateConnectionState(ulong clientId, ViverseConnectionState newState)
        {
            if (s_ConnectionInfo.TryGetValue(clientId, out var connectionInfo))
            {
                connectionInfo.State = newState;
                connectionInfo.UpdateActivity(Time.timeAsDouble);
                s_ConnectionInfo[clientId] = connectionInfo;
                
                Debug.Log($"[ViverseConnectionManager] Updated connection {clientId} state to {newState}");
                return true;
            }
            
            Debug.LogWarning($"[ViverseConnectionManager] Failed to update state for unknown connection {clientId}");
            return false;
        }

        /// <summary>
        /// Remove a connection
        /// </summary>
        public static bool RemoveConnection(ulong clientId)
        {
            if (s_ConnectionInfo.TryGetValue(clientId, out var connectionInfo))
            {
                // Remove from player ID mapping
                s_PlayerIdToClientId.Remove(connectionInfo.PlayerId);
                s_AnnouncedPlayers.Remove(connectionInfo.PlayerId);
                
                // Dispose connection resources
                connectionInfo.Dispose();
                
                // Remove from connection info
                s_ConnectionInfo.Remove(clientId);
                
                Debug.Log($"[ViverseConnectionManager] Removed connection {clientId}");
                return true;
            }
            
            Debug.LogWarning($"[ViverseConnectionManager] Failed to remove unknown connection {clientId}");
            return false;
        }

        /// <summary>
        /// Check if a player connection has been announced
        /// </summary>
        public static bool IsPlayerAnnounced(string playerId)
        {
            var playerIdFixed = new FixedString64Bytes(playerId);
            return s_AnnouncedPlayers.Contains(playerIdFixed);
        }

        /// <summary>
        /// Mark a player as announced
        /// </summary>
        public static void MarkPlayerAnnounced(string playerId)
        {
            var playerIdFixed = new FixedString64Bytes(playerId);
            s_AnnouncedPlayers.Add(playerIdFixed);
        }

        /// <summary>
        /// Get all active connection IDs
        /// </summary>
        public static NativeArray<ulong> GetActiveConnections(Allocator allocator)
        {
            var activeConnections = new NativeList<ulong>(s_ConnectionInfo.Count, allocator);
            
            foreach (var kvp in s_ConnectionInfo)
            {
                if (kvp.Value.IsValid)
                {
                    activeConnections.Add(kvp.Key);
                }
            }
            
            return activeConnections.AsArray();
        }

        /// <summary>
        /// Update all connections and remove timed out ones
        /// </summary>
        public static void UpdateConnections()
        {
            var currentTime = Time.timeAsDouble;
            var connectionsToRemove = new NativeList<ulong>(Allocator.Temp);
            
            foreach (var kvp in s_ConnectionInfo)
            {
                var connectionInfo = kvp.Value;
                if (connectionInfo.HasTimedOut(currentTime))
                {
                    connectionsToRemove.Add(kvp.Key);
                }
            }
            
            // Remove timed out connections
            for (int i = 0; i < connectionsToRemove.Length; i++)
            {
                RemoveConnection(connectionsToRemove[i]);
            }
            
            connectionsToRemove.Dispose();
        }

        /// <summary>
        /// Get connection payload for a client
        /// </summary>
        public static bool TryGetConnectionPayload(ulong clientId, out byte[] payload)
        {
            payload = null;
            
            if (s_ConnectionInfo.TryGetValue(clientId, out var connectionInfo) && 
                connectionInfo.ConnectionPayload.IsCreated)
            {
                payload = new byte[connectionInfo.ConnectionPayload.Length];
                for (int i = 0; i < connectionInfo.ConnectionPayload.Length; i++)
                {
                    payload[i] = connectionInfo.ConnectionPayload[i];
                }
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Update connection activity timestamp
        /// </summary>
        public static void UpdateConnectionActivity(ulong clientId)
        {
            if (s_ConnectionInfo.TryGetValue(clientId, out var connectionInfo))
            {
                connectionInfo.LastActivity = UnityEngine.Time.time;
                s_ConnectionInfo[clientId] = connectionInfo;
            }
        }
        
        /// <summary>
        /// Get the local player ID
        /// </summary>
        public static string GetLocalPlayerId()
        {
            // Return a default local player ID - this could be enhanced to use actual player data
            return UnityEngine.Random.Range(1000, 9999).ToString();
        }
    }
}