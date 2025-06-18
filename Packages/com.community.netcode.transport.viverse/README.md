# Viverse Transport for Unity Netcode

A Unity Netcode transport implementation for Viverse WebRTC P2P networking.

## Overview

The Viverse Transport provides seamless integration between Unity Netcode for GameObjects and Viverse's WebRTC-based peer-to-peer networking system. This allows developers to create multiplayer games that work directly in web browsers without requiring dedicated servers.

## Features

- **WebRTC P2P Networking**: Direct peer-to-peer connections using Viverse WebRTC
- **Unity Netcode Integration**: Drop-in replacement for Unity Transport
- **Cross-Platform**: Works on WebGL and other Unity platforms
- **Connection Management**: Automatic handling of client connections and disconnections
- **Fallback Support**: Can fallback to standard Unity Transport when needed
- **Debug Logging**: Comprehensive logging for development and debugging

## Installation

1. Add this package to your Unity project by copying the `com.community.netcode.transport.viverse` folder to your `Packages` directory
2. The package will automatically appear in your Package Manager
3. Ensure you have Unity Netcode for GameObjects installed as a dependency

## Quick Start

### 1. Replace UnityTransport with ViverseTransport

Replace your existing `UnityTransport` component with `ViverseTransport`:

```csharp
// Remove existing UnityTransport component
var unityTransport = GetComponent<UnityTransport>();
if (unityTransport != null)
    DestroyImmediate(unityTransport);

// Add ViverseTransport component
var viverseTransport = gameObject.AddComponent<ViverseTransport>();
```

### 2. Configure Viverse Settings

Set up your Viverse connection parameters:

```csharp
var connectionData = new ViverseConnectionData
{
    AppId = "your_viverse_app_id",
    RoomId = "game_room_123",
    IsHost = true, // Set to false for clients
    ConnectTimeoutMS = 10000,
    MaxConnectionAttempts = 3,
    EnableDebugLogging = true
};

viverseTransport.SetViverseConnectionData(connectionData);
```

### 3. Connect to Viverse Room

For host:
```csharp
// Connect as host
viverseTransport.ConnectToViverse("your_app_id", "room_123", true);

// Wait for connection, then start host
viverseTransport.OnViverseConnected += () =>
{
    NetworkManager.Singleton.StartHost();
};
```

For client:
```csharp
// Connect as client
viverseTransport.ConnectToViverse("your_app_id", "room_123", false);

// Wait for connection, then start client
viverseTransport.OnViverseConnected += () =>
{
    NetworkManager.Singleton.StartClient();
};
```

## WebGL Integration

For WebGL builds, you need to integrate the Viverse JavaScript SDK:

### 1. Add JavaScript Bridge

Create or modify your WebGL template to include the Viverse WebRTC bridge:

```javascript
// viverse-bridge.js
var ViverseUnityBridge = {
    SendBroadcastMessage: function(base64DataPtr, senderIdPtr) {
        var base64Data = UTF8ToString(base64DataPtr);
        var senderId = UTF8ToString(senderIdPtr);
        
        // Send message via Viverse WebRTC
        if (window.ViverseSDK) {
            window.ViverseSDK.sendMessage({
                type: 'unity_transport_broadcast',
                senderId: senderId,
                data: base64Data
            });
        }
    },
    
    JoinViverseRoom: function(appIdPtr, roomIdPtr) {
        var appId = UTF8ToString(appIdPtr);
        var roomId = UTF8ToString(roomIdPtr);
        
        if (window.ViverseSDK) {
            window.ViverseSDK.joinRoom(appId, roomId);
        }
    },
    
    LeaveViverseRoom: function() {
        if (window.ViverseSDK) {
            window.ViverseSDK.leaveRoom();
        }
    },
    
    IsViverseConnected: function() {
        return window.ViverseSDK ? window.ViverseSDK.isConnected() : false;
    }
};

mergeInto(LibraryManager.library, ViverseUnityBridge);
```

### 2. Handle Viverse Messages

Set up message handling in your HTML template:

```javascript
// Initialize Viverse SDK and handle incoming messages
window.ViverseSDK.onMessage = function(message) {
    if (message.type === 'unity_transport_broadcast') {
        // Send to Unity via GameObject message
        SendMessage('ViverseBroadcastReceiver', 'OnViverseMessage', JSON.stringify(message));
    }
};
```

## API Reference

### ViverseTransport

Main transport component that extends UnityTransport.

#### Properties

- `IsViverseConnected`: Whether transport is connected to Viverse
- `CurrentRoomId`: Current room ID
- `IsHost`: Whether this instance is the host

#### Methods

- `SetViverseConnectionData(ViverseConnectionData)`: Configure connection parameters
- `ConnectToViverse(string, string, bool)`: Connect to Viverse room
- `DisconnectFromViverse()`: Disconnect from Viverse room
- `TryGetConnectionPayload(ulong, out byte[])`: Get connection payload for client
- `GetConnectionStats()`: Get connection statistics for debugging

#### Events

- `OnViverseConnected`: Triggered when Viverse connection is established
- `OnViverseDisconnected`: Triggered when Viverse connection is lost

### ViverseConnectionData

Configuration structure for Viverse connections.

```csharp
public struct ViverseConnectionData
{
    public string AppId;                    // Viverse application ID
    public string RoomId;                   // Room ID for the session
    public bool IsHost;                     // Whether this instance is the host
    public int ConnectTimeoutMS;            // Connection timeout in milliseconds
    public int MaxConnectionAttempts;       // Maximum number of connection attempts
    public bool EnableDebugLogging;         // Whether to enable debug logging
}
```

## Best Practices

### Connection Management

1. **Always check connection status** before starting NetworkManager:
```csharp
if (viverseTransport.IsViverseConnected)
{
    NetworkManager.Singleton.StartHost();
}
```

2. **Handle connection events** for robust networking:
```csharp
viverseTransport.OnViverseConnected += OnConnected;
viverseTransport.OnViverseDisconnected += OnDisconnected;
```

3. **Implement reconnection logic** for unstable connections:
```csharp
void OnDisconnected()
{
    StartCoroutine(ReconnectAfterDelay(5f));
}
```

### Performance Optimization

1. **Set appropriate timeouts** based on your target network conditions
2. **Use debug logging only in development** builds
3. **Limit connection attempts** to avoid endless retry loops

### Error Handling

1. **Always wrap Viverse calls** in try-catch blocks
2. **Provide fallback to Unity Transport** when Viverse is unavailable
3. **Log connection issues** for debugging

## Troubleshooting

### Common Issues

**Q: Clients can't connect to host**
- Verify both host and clients use the same App ID and Room ID
- Check that Viverse WebRTC is properly initialized on WebGL
- Ensure firewall/NAT doesn't block WebRTC connections

**Q: Messages are not being received**
- Verify the ViverseBroadcastReceiver GameObject exists
- Check that JavaScript bridge is properly implemented
- Enable debug logging to trace message flow

**Q: Connection timeout errors**
- Increase ConnectTimeoutMS value
- Check network connectivity
- Verify Viverse SDK is properly loaded

### Debug Information

Enable debug logging and check Unity Console for detailed information:

```csharp
var connectionData = new ViverseConnectionData
{
    // ... other settings
    EnableDebugLogging = true
};
```

Use `GetConnectionStats()` to get runtime connection information:

```csharp
Debug.Log(viverseTransport.GetConnectionStats());
```

## Platform Support

- **WebGL**: Full support with JavaScript bridge
- **Standalone**: Mock implementation for testing
- **Mobile**: Future support planned

## Dependencies

- Unity Netcode for GameObjects 1.0.0+
- Unity Transport 2.0.0+
- Unity 2022.3+

## License

MIT License - see LICENSE file for details.

## Contributing

Contributions are welcome! Please follow the Unity community transport contribution guidelines.