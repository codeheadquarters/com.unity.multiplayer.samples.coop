using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to when a client is attempting to connect to a server. Starts the client when
    /// entering. If successful, transitions to the ClientConnected state. If not, transitions to the Offline state.
    /// </summary>
    class ClientConnectingState : OnlineState
    {
        protected ConnectionMethodBase m_ConnectionMethod;

        public ClientConnectingState Configure(ConnectionMethodBase baseConnectionMethod)
        {
            m_ConnectionMethod = baseConnectionMethod;
            return this;
        }

        public override void Enter()
        {
#pragma warning disable 4014
            ConnectClientAsync();
#pragma warning restore 4014
        }

        public override void Exit() 
        { 
            // Dispose of connection method when exiting to ensure proper cleanup
            if (m_ConnectionMethod is IDisposable disposableConnectionMethod)
            {
                disposableConnectionMethod.Dispose();
            }
        }

        public override void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ClientConnectingState] OnClientConnected called with clientId: {clientId}");
            m_ConnectStatusPublisher.Publish(ConnectStatus.Success);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnected);
        }

        public override void OnClientDisconnect(ulong _)
        {
            // client ID is for sure ours here
            StartingClientFailed();
        }

        void StartingClientFailed()
        {
            var disconnectReason = m_ConnectionManager.NetworkManager.DisconnectReason;
            if (string.IsNullOrEmpty(disconnectReason))
            {
                m_ConnectStatusPublisher.Publish(ConnectStatus.StartClientFailed);
            }
            else
            {
                var connectStatus = JsonUtility.FromJson<ConnectStatus>(disconnectReason);
                m_ConnectStatusPublisher.Publish(connectStatus);
            }
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
        }


        internal async Task ConnectClientAsync()
        {
            try
            {
                // Setup NGO with current connection method
                Debug.Log("[ClientConnectingState] Starting SetupClientConnectionAsync...");
                await m_ConnectionMethod.SetupClientConnectionAsync();
                Debug.Log("[ClientConnectingState] SetupClientConnectionAsync completed successfully");

                // NGO's StartClient launches everything
                Debug.Log("[ClientConnectingState] About to call NetworkManager.StartClient()...");
                var startClientResult = m_ConnectionManager.NetworkManager.StartClient();
                Debug.Log($"[ClientConnectingState] NetworkManager.StartClient() returned: {startClientResult}");
                
                if (!startClientResult)
                {
                    throw new Exception("NetworkManager StartClient failed");
                }
                
                Debug.Log("[ClientConnectingState] StartClient succeeded, waiting for OnClientConnected callback...");
            }
            catch (Exception e)
            {
                Debug.LogError("Error connecting client, see following exception");
                Debug.LogException(e);
                StartingClientFailed();
                throw;
            }
        }
    }
}
