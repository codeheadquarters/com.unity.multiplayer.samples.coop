mergeInto(LibraryManager.library, {

  // Core Viverse SDK initialization
  initViverseClient: function () {
    console.log('[ViverseBridge] Initializing Viverse client...');
    try {
      if (typeof globalThis.viverse !== 'undefined') {
        if (globalThis.playClient) {
          console.log('[ViverseBridge] Viverse client already initialized');
          return;
        }
        globalThis.playClient = new globalThis.viverse.play();
        window.playClient = globalThis.playClient; // Make available as global
        console.log('[ViverseBridge] Viverse client initialized');
      } else {
        console.error('[ViverseBridge] Viverse SDK not loaded');
      }
    } catch (error) {
      console.error('[ViverseBridge] Failed to initialize Viverse client:', error);
    }
  },

  initViverseMatchmaking: function (appId, debug) {
    console.log('[ViverseBridge] Initializing Viverse matchmaking: ' + UTF8ToString(appId) + ', debug: ' + debug);
    
    try {
      if (!globalThis.playClient) {
        console.error('[ViverseBridge] Viverse client not initialized');
        return;
      }

      var appIdStr = UTF8ToString(appId);
      var debugBool = debug === 1;
      
      // Call newMatchmakingClient - handle both sync and async patterns
      var result = globalThis.playClient.newMatchmakingClient(appIdStr, debugBool);
      
      // Store result or handle async
      if (result && typeof result.then === 'function') {
        // Async result
        result.then(function(client) {
          console.log('[ViverseBridge] Matchmaking client initialized (async)');
          window.currentAppId = appIdStr;
          if (client) {
            globalThis.matchmakingClient = client;
            
            // Wait for client to be ready
            setTimeout(function() {
              console.log('[ViverseBridge] Matchmaking client ready - notifying Unity');
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                try {
                  myGameInstance.SendMessage('ViverseCallbackHandler', 'OnInitComplete', 'initialized');
                } catch (error) {
                  console.error('[ViverseBridge] Error notifying Unity of initialization:', error);
                }
              }
            }, 1000);
          }
        }).catch(function(error) {
          console.error('[ViverseBridge] Failed to initialize matchmaking client:', error);
          if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
            myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Matchmaking init failed: ' + error.toString());
          }
        });
      } else {
        // Sync result
        console.log('[ViverseBridge] Matchmaking client initialized (sync)');
        window.currentAppId = appIdStr;
        if (result) {
          globalThis.matchmakingClient = result;
        }
        
        setTimeout(function() {
          console.log('[ViverseBridge] Sync client ready - notifying Unity');
          if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
            try {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'OnInitComplete', 'initialized');
            } catch (error) {
              console.error('[ViverseBridge] Error notifying Unity of sync initialization:', error);
            }
          }
        }, 1000);
      }
    } catch (error) {
      console.error('[ViverseBridge] Error initializing matchmaking client:', error);
    }
  },

  // Room management for Viverse Transport
  JoinViverseRoom: function(appIdPtr, roomIdPtr) {
    var appId = UTF8ToString(appIdPtr);
    var roomId = UTF8ToString(roomIdPtr);
    
    console.log('[ViverseBridge] Joining Viverse room:', roomId, 'with app:', appId);
    
    try {
      // Initialize if needed
     // if (!globalThis.viverseClient) {
      //  _initViverseClient();
     // }
      
      // Initialize matchmaking if needed
     // if (!globalThis.viverseMatchmakingClient) {
      //  _initViverseMatchmaking(stringToNewUTF8(appId), 1);
     // }
      
      // Store current room info
      window.currentAppId = appId;
      window.currentRoomId = roomId;
      
      // Initialize multiplayer client for this room
    //  _viverseInitMultiplayer(stringToNewUTF8(roomId), stringToNewUTF8(appId));
      
    } catch (error) {
      console.error('[ViverseBridge] Error joining Viverse room:', error);
    }
  },

  LeaveViverseRoom: function() {
    console.log('[ViverseBridge] Leaving Viverse room');
    
    try {
      // Try multiple access patterns for the client
      var client = globalThis.matchmakingClient || globalThis.playClient;
      
      if (client) {
        
        if (client.leaveRoom && typeof client.leaveRoom === 'function') {
          console.log('[ViverseBridge] Using leaveRoom method');
          var result = client.leaveRoom();
          methodFound = true;
          
          // Handle async result
          if (result && typeof result.then === 'function') {
            result.then(function(resolvedResult) {
              console.log('[ViverseBridge] leaveRoom Promise resolved:', resolvedResult);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnLeaveRoomComplete', 'success');
              }
            }).catch(function(error) {
              console.error('[VP1PlayBridge] leaveRoom Promise rejected:', error);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Leave room failed: ' + error.toString());
              }
            });
          } else {
            // Sync result
            console.log('[VP1PlayBridge] leaveRoom completed (sync):', result);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseBroadcastReceiver', 'OnLeaveRoomComplete', 'success');
            }
          }
        } 
      } else {
        console.warn('[VP1PlayBridge] No client available for leaving room');
        if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
          myGameInstance.SendMessage('ViverseCallbackHandler', 'OnLeaveRoomComplete', 'no_client');
        }
      }
    } catch (error) {
      console.error('[VP1PlayBridge] Error leaving room:', error);
      if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
        myGameInstance.SendMessage('ViverseBroadcastReceiver', 'ViverseError', 'Leave room exception: ' + error.toString());
      }
    }
  },

  IsViverseConnected: function() {
    return globalThis.multiplayerClient !== null ? 1 : 0;
  },

  // WebRTC P2P multiplayer for Viverse Transport
  viverseInitMultiplayer: function (roomId, appId) {
    var roomIdStr = UTF8ToString(roomId);
    var appIdStr = UTF8ToString(appId);
    console.log('[ViverseBridge] Initializing Viverse multiplayer for transport - Room: ' + roomIdStr + ', App: ' + appIdStr);
    
    try {
      // Try alternative ways to access the MultiplayerClient
      if (typeof globalThis.play !== 'undefined') {
        globalThis.multiplayerClient = new globalThis.play.MultiplayerClient(roomIdStr, appIdStr);
      }
      
      globalThis.multiplayerClient.init().then(function(info) {
        console.log('[ViverseBridge] Viverse multiplayer client initialized:', info);
        
        if (globalThis.multiplayerClient.onConnected) {
          globalThis.multiplayerClient.onConnected(function() {
            console.log("[ViverseBridge] Viverse multiplayer connected - WebRTC ready");
            
            // Notify Unity that WebRTC is ready
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              try {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnBroadcastTransportReady', 'connected');
// Also notify that initialization is complete for the broadcast transport
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnInitComplete', 'ready');
              } catch (error) {
                console.error("[ViverseBridge] Error notifying Unity of WebRTC connection:", error);
              }
            }
          });
        }
        
        // Setup disconnection callback
        if (globalThis.multiplayerClient.onDisconnected) {
          globalThis.multiplayerClient.onDisconnected(function(reason) {
            console.log("[ViverseBridge] Viverse WebRTC connection lost - reason:", reason);
            
            // Notify Unity of disconnection
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              try {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnMultiplayerDisconnected', reason || 'connection_lost');
              } catch (error) {
                console.error("[ViverseBridge] Error notifying Unity of WebRTC disconnection:", error);
              }
            }
          });
        }
        
        // Setup message listener for Unity Transport
        if (globalThis.multiplayerClient.general && globalThis.multiplayerClient.general.onMessage) {
          globalThis.multiplayerClient.general.onMessage(function(message) {
            try {
              // Forward all messages to Unity Transport receiver
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseBroadcastReceiver', 'OnViverseMessage', message);
              }
            } catch (error) {
              console.error("[ViverseBridge] Error forwarding message to Unity:", error);
            }
          });
        }
        
      }).catch(function(error) {
        console.error("[ViverseBridge] Failed to initialize Viverse multiplayer:", error);
        
        // Notify Unity of connection failure
        if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
          try {
            myGameInstance.SendMessage('ViverseCallbackHandler', 'OnMultiplayerConnectionFailed', error.toString());
          } catch (sendError) {
            console.error("[ViverseBridge] Error notifying Unity of connection failure:", sendError);
          }
        }
      });
      
    } catch (error) {
      console.error("[ViverseBridge] Error in viverseInitMultiplayer:", error);
      
      // Notify Unity of initialization error
      if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
        try {
          myGameInstance.SendMessage('ViverseBroadcastReceiver', 'OnViverseTransportFailed', error.toString());
        } catch (sendError) {
          console.error("[ViverseBridge] Error notifying Unity of init error:", sendError);
        }
      }
    }
  },

  // Message sending for Unity Transport
  SendBroadcastMessage: function(base64DataPtr, senderIdPtr) {
    var base64Data = UTF8ToString(base64DataPtr);
    var senderId = UTF8ToString(senderIdPtr);
    
    // Create JSON message format for Unity Transport broadcasts
    var transportMessage = {
      type: "unity_transport_broadcast",
      senderId: senderId,
      data: base64Data
    };
    
    var messageJson = JSON.stringify(transportMessage);
    
    // Send via Viverse WebRTC
    if (globalThis.multiplayerClient && globalThis.multiplayerClient.general && globalThis.multiplayerClient.general.sendMessage) {
      try {
        globalThis.multiplayerClient.general.sendMessage(messageJson);
        // Only log non-heartbeat messages to reduce spam
        if (base64Data.length < 100) { // Assume short messages are not transport data
          console.log("[ViverseBridge] Sent Unity Transport broadcast from", senderId, "length:", base64Data.length);
        }
      } catch (error) {
        console.error("[ViverseBridge] Error sending Unity Transport broadcast:", error);
      }
    } else {
      console.error("[ViverseBridge] Viverse multiplayer client not ready for broadcast");
    }
  },

  viverseDisconnect: function () {
    console.log("[ViverseBridge] Disconnecting from Viverse multiplayer");
    
    try {
      if (globalThis.multiplayerClient) {
        if (globalThis.multiplayerClient.disconnect) {
          globalThis.multiplayerClient.disconnect();
        }
        globalThis.multiplayerClient = null;
      }
      
    } catch (error) {
      console.error("[ViverseBridge] Error disconnecting:", error);
    }
  },

  // Legacy room management (for existing UI)
  createRoom: function (roomConfigJson) {
    var configStr = UTF8ToString(roomConfigJson);
    console.log('[ViverseBridge] Creating room with config:', configStr);
    
    try {
      var roomConfig = JSON.parse(configStr);
      var client = globalThis.matchmakingClient || globalThis.playClient;
      
      if (client && client.createRoom && typeof client.createRoom === 'function') {
        var result = client.createRoom(roomConfig);
        
        if (result && typeof result.then === 'function') {
          result.then(function(resolvedResult) {
            console.log('[ViverseBridge] createRoom resolved:', resolvedResult);
            
            var roomId = null;
            if (resolvedResult && resolvedResult.success && resolvedResult.room && resolvedResult.room.id) {
              roomId = resolvedResult.room.id;
            } else if (resolvedResult && resolvedResult.roomId) {
              roomId = resolvedResult.roomId;
            } else if (resolvedResult && resolvedResult.id) {
              roomId = resolvedResult.id;
            } else if (typeof resolvedResult === 'string') {
              roomId = resolvedResult;
            }
            
            if (roomId) {
              console.log('[ViverseBridge] Room created with ID:', roomId);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnCreateRoomComplete', roomId);
              }
            }
          }).catch(function(error) {
            console.error('[ViverseBridge] createRoom failed:', error);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Room creation failed: ' + error.toString());
            }
          });
        }
      }
    } catch (error) {
      console.error('[ViverseBridge] Error in createRoom:', error);
    }
  },

  getAvailableRooms: function () {
    console.log('[ViverseBridge] Getting available rooms...');
    
    try {
      var client = globalThis.matchmakingClient || globalThis.playClient;
      
      if (client && client.getAvailableRooms && typeof client.getAvailableRooms === 'function') {
        var result = client.getAvailableRooms();
        
        if (result && typeof result.then === 'function') {
          result.then(function(resolvedResult) {
            console.log('[ViverseBridge] getAvailableRooms resolved:', resolvedResult);
            
            var rooms = null;
            if (resolvedResult && resolvedResult.success && resolvedResult.rooms) {
              rooms = resolvedResult.rooms;
            } else if (resolvedResult && Array.isArray(resolvedResult)) {
              rooms = resolvedResult;
            } else if (resolvedResult && resolvedResult.data && Array.isArray(resolvedResult.data)) {
              rooms = resolvedResult.data;
            }
            
            var roomsJson = JSON.stringify(rooms || []);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'RoomsDiscovered', roomsJson);
            }
          }).catch(function(error) {
            console.error('[ViverseBridge] getAvailableRooms failed:', error);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Room discovery failed: ' + error.toString());
            }
          });
        }
      }
    } catch (error) {
      console.error('[ViverseBridge] Error in getAvailableRooms:', error);
    }
  },

  setActor: function (actorJsonPtr) {
    var actorJson = UTF8ToString(actorJsonPtr);
    console.log('[ViverseBridge] setActor:', actorJson);
    
    try {
      var actorData = JSON.parse(actorJson);
	    window.currentActor = actorData;
	    var client = globalThis.matchmakingClient || globalThis.playClient;
	    if (client.setActor && typeof client.setActor === 'function') {
          console.log('[VP1PlayBridge] Using setActor method');
	        var result = client.setActor(actorData);
	        methodFound = true;
          
          // Handle async result and notify Unity via SendMessage
          if (result && typeof result.then === 'function') {
            result.then(function(res) {
              console.log('[VP1PlayBridge] setActor completed:', res);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'OnSetActorComplete', JSON.stringify(res));
              }
            }).catch(function(err) {
              console.error('[VP1PlayBridge] setActor failed:', err);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'SetActor failed: ' + err.toString());
              }
            });
          } else {
            // Not a Promise - handle synchronously
            console.log('[VP1PlayBridge] setActor completed (sync):', result);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'OnSetActorComplete', JSON.stringify(result || {success: true}));
            }
          }
        }
    } catch (error) {
      console.error('[ViverseBridge] Error setting actor:', error);
      if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
        myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Set actor failed: ' + error.toString());
      }
    }
  },

  joinRoom: function (roomIdPtr) {
    var roomId = UTF8ToString(roomIdPtr);
    console.log('[ViverseBridge] joinRoom:', roomId);
    
    try {
      var client = globalThis.matchmakingClient || globalThis.playClient;
      
      if (client && client.joinRoom && typeof client.joinRoom === 'function') {
        var result = client.joinRoom(roomId);
        
        if (result && typeof result.then === 'function') {
          result.then(function(resolvedResult) {
            console.log('[ViverseBridge] joinRoom resolved:', resolvedResult);
            
            // Store current room
            window.currentRoomId = roomId;
            if (resolvedResult && (resolvedResult.success || resolvedResult.room || resolvedResult.id)) {
                console.log('[VP1PlayBridge] Room joined successfully');
                if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                  myGameInstance.SendMessage('ViverseCallbackHandler', 'OnJoinRoomComplete', 'success');
                }
              } else {
                console.log('[VP1PlayBridge] Join room result unclear:', resolvedResult);
                // Still send success since VP1-Play might have weird response format
                if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                  myGameInstance.SendMessage('ViverseCallbackHandler', 'OnJoinRoomComplete', 'success');
                }
              }
            }).catch(function(error) {
              console.error('[VP1PlayBridge] joinRoom Promise rejected:', error);
              if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
                myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Join room Promise failed: ' + error.toString());
              }
            });
          } else {
            // Not a Promise - handle synchronously
            console.log('[VP1PlayBridge] joinRoom returned sync result:', result);
            if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
              myGameInstance.SendMessage('ViverseCallbackHandler', 'OnJoinRoomComplete', 'success');
            }
          }
        } else {
          console.error('[ViverseBridge] No joinRoom method available');
          if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
            myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Join room method not available');
          }
        }
    } catch (error) {
      console.error('[ViverseBridge] Error in joinRoom:', error);
      if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
        myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Join room error: ' + error.toString());
      }
    }
  },

  vp1PlayGetConnectionStatus: function () {
    // Return connection status: 0=disconnected, 1=connecting, 2=connected
    if (globalThis.multiplayerClient) {
      return 2; // Connected
    } else if (globalThis.matchmakingClient) {
      return 1; // Connecting (matchmaking ready but not multiplayer)
    } else {
      return 0; // Disconnected
    }
  },

  checkVP1PlayClientReady: function () {
    console.log('[ViverseBridge] checkVP1PlayClientReady - checking client readiness');
    
    // Check if client is ready and notify Unity
    setTimeout(function() {
      var isReady = (globalThis.viverseClient !== null && globalThis.viverseClient !== undefined);
      
      if (typeof myGameInstance !== 'undefined' && myGameInstance && myGameInstance.SendMessage) {
        if (isReady) {
          myGameInstance.SendMessage('ViverseCallbackHandler', 'OnViverseClientReady', 'client_ready');
        } else {
          myGameInstance.SendMessage('ViverseCallbackHandler', 'ViverseError', 'Client not ready');
        }
      }
    }, 500); // Small delay to ensure proper initialization
  },

});