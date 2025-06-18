using System;
using Unity.Collections;
using Unity.Networking.Transport;

namespace Unity.Netcode.Transports.Viverse
{
    /// <summary>
    /// Parameters for the Viverse WebRTC transport
    /// </summary>
    public struct ViverseNetworkParameter : INetworkParameter
    {
        /// <summary>
        /// Viverse application ID
        /// </summary>
        public FixedString64Bytes AppId;
        
        /// <summary>
        /// Room ID for the session
        /// </summary>
        public FixedString64Bytes RoomId;
        
        /// <summary>
        /// Whether this instance is the host
        /// </summary>
        public byte IsHost;
        
        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectTimeoutMS;
        
        /// <summary>
        /// Maximum number of connection attempts
        /// </summary>
        public int MaxConnectionAttempts;
        
        /// <summary>
        /// Whether to enable debug logging
        /// </summary>
        public byte EnableDebugLogging;

        public bool Validate()
        {
            return !AppId.IsEmpty && !RoomId.IsEmpty && ConnectTimeoutMS > 0 && MaxConnectionAttempts > 0;
        }
    }

    /// <summary>
    /// Extension methods for Viverse network settings
    /// </summary>
    public static class ViverseNetworkSettingsExtensions
    {
        /// <summary>
        /// Sets the Viverse parameters for the network settings
        /// </summary>
        public static ref NetworkSettings WithViverseParameters(
            ref this NetworkSettings settings,
            string appId,
            string roomId,
            bool isHost,
            int connectTimeoutMS = 10000,
            int maxConnectionAttempts = 3,
            bool enableDebugLogging = false)
        {
            var parameters = new ViverseNetworkParameter
            {
                AppId = new FixedString64Bytes(appId),
                RoomId = new FixedString64Bytes(roomId),
                IsHost = (byte)(isHost ? 1 : 0),
                ConnectTimeoutMS = connectTimeoutMS,
                MaxConnectionAttempts = maxConnectionAttempts,
                EnableDebugLogging = (byte)(enableDebugLogging ? 1 : 0)
            };

            settings.AddRawParameterStruct(ref parameters);
            return ref settings;
        }

        /// <summary>
        /// Gets the Viverse parameters from network settings
        /// </summary>
        public static ViverseNetworkParameter GetViverseParameters(this ref NetworkSettings settings)
        {
            if (settings.TryGet<ViverseNetworkParameter>(out var parameters))
                return parameters;

            return new ViverseNetworkParameter
            {
                AppId = new FixedString64Bytes("default_app"),
                RoomId = new FixedString64Bytes("default_room"),
                IsHost = 0,
                ConnectTimeoutMS = 10000,
                MaxConnectionAttempts = 3,
                EnableDebugLogging = 0
            };
        }
    }
}