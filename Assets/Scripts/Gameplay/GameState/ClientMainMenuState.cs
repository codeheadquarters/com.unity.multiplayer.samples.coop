using System;
using System.Runtime.InteropServices;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.UnityServices.Auth;
using Unity.BossRoom.UnityServices.Lobbies;
using Unity.BossRoom.Utils;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Game Logic that runs when sitting at the MainMenu. This is likely to be "nothing", as no game has been started. But it is
    /// nonetheless important to have a game state, as the GameStateBehaviour system requires that all scenes have states.
    /// </summary>
    /// <remarks> OnNetworkSpawn() won't ever run, because there is no network connection at the main menu screen.
    /// Fortunately we know you are a client, because all players are clients when sitting at the main menu screen.
    /// </remarks>
    public class ClientMainMenuState : GameStateBehaviour
    {
        public override GameState ActiveState => GameState.MainMenu;

        [SerializeField]
        NameGenerationData m_NameGenerationData;
        [SerializeField]
        LobbyUIMediator m_LobbyUIMediator;
        [SerializeField]
        IPUIMediator m_IPUIMediator;
        [SerializeField]
        Button m_LobbyButton;
        [SerializeField]
        GameObject m_SignInSpinner;
        [SerializeField]
        UIProfileSelector m_UIProfileSelector;
        [SerializeField]
        UITooltipDetector m_UGSSetupTooltipDetector;

        [Inject]
        AuthenticationServiceFacade m_AuthServiceFacade;
        [Inject]
        LocalLobbyUser m_LocalUser;
        [Inject]
        LocalLobby m_LocalLobby;
        [Inject]
        ProfileManager m_ProfileManager;

        // VP1-Play configuration
        [SerializeField] private string m_VP1AppId = "example_game_boss";

#if UNITY_WEBGL && !UNITY_EDITOR
        // DllImport declarations for VP1-Play functions
        [DllImport("__Internal")]
        private static extern void initViverseClient();
        
        [DllImport("__Internal")]
        private static extern void initViverseMatchmaking(string appId, int debug);
        
        //[DllImport("__Internal")]
       // private static extern void setupMatchmakingEvents();
#endif

        protected override void Awake()
        {
            base.Awake();

            m_LobbyButton.interactable = false;
            m_LobbyUIMediator.Hide();

#if UNITY_WEBGL && !UNITY_EDITOR
            // For WebGL builds, use VP1-Play instead of Unity Authentication
            SetupVP1Play();
#else
            // For other platforms, use Unity Authentication
            if (string.IsNullOrEmpty(Application.cloudProjectId))
            {
                OnSignInFailed();
                return;
            }

            TrySignIn();
#endif
        }

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterComponent(m_NameGenerationData);
            builder.RegisterComponent(m_LobbyUIMediator);
            builder.RegisterComponent(m_IPUIMediator);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void SetupVP1Play()
        {
            Debug.Log("[ClientMainMenuState] Setting up VP1-Play for WebGL");
            
            try
            {
                // Initialize VP1-Play client components using DllImport functions
                initViverseClient();
                initViverseMatchmaking(m_VP1AppId, 0);
                //setupMatchmakingEvents();
                
                // Enable lobby functionality immediately for VP1-Play
                OnVP1PlayReady();
                
                Debug.Log("[ClientMainMenuState] VP1-Play setup complete");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientMainMenuState] VP1-Play setup failed: {e.Message}");
                OnSignInFailed();
            }
        }

        private void OnVP1PlayReady()
        {
            m_LobbyButton.interactable = true;
            m_UGSSetupTooltipDetector.enabled = false;
            m_SignInSpinner.SetActive(false);

            Debug.Log("[ClientMainMenuState] VP1-Play ready - Lobby functionality enabled");

            // Generate a VP1-Play compatible player ID
            string vp1PlayerId = $"vp1_player_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            m_LocalUser.ID = vp1PlayerId;

            // The local LobbyUser object will be hooked into UI before the LocalLobby is populated during lobby join
            m_LocalLobby.AddUser(m_LocalUser);
        }
#endif

        private async void TrySignIn()
        {
            try
            {
                var unityAuthenticationInitOptions =
                    m_AuthServiceFacade.GenerateAuthenticationOptions(m_ProfileManager.Profile);

                await m_AuthServiceFacade.InitializeAndSignInAsync(unityAuthenticationInitOptions);
                OnAuthSignIn();
                m_ProfileManager.onProfileChanged += OnProfileChanged;
            }
            catch (Exception)
            {
                OnSignInFailed();
            }
        }

        private void OnAuthSignIn()
        {
            m_LobbyButton.interactable = true;
            m_UGSSetupTooltipDetector.enabled = false;
            m_SignInSpinner.SetActive(false);

            Debug.Log($"Signed in. Unity Player ID {AuthenticationService.Instance.PlayerId}");

            m_LocalUser.ID = AuthenticationService.Instance.PlayerId;

            // The local LobbyUser object will be hooked into UI before the LocalLobby is populated during lobby join, so the LocalLobby must know about it already when that happens.
            m_LocalLobby.AddUser(m_LocalUser);
        }

        private void OnSignInFailed()
        {
            if (m_LobbyButton)
            {
                m_LobbyButton.interactable = false;
                m_UGSSetupTooltipDetector.enabled = true;
            }

            if (m_SignInSpinner)
            {
                m_SignInSpinner.SetActive(false);
            }
        }

        protected override void OnDestroy()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            m_ProfileManager.onProfileChanged -= OnProfileChanged;
#endif
            base.OnDestroy();
        }

        async void OnProfileChanged()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // VP1-Play doesn't use Unity profiles, so skip profile changes
            return;
#else
            m_LobbyButton.interactable = false;
            m_SignInSpinner.SetActive(true);
            await m_AuthServiceFacade.SwitchProfileAndReSignInAsync(m_ProfileManager.Profile);

            m_LobbyButton.interactable = true;
            m_SignInSpinner.SetActive(false);

            Debug.Log($"Signed in. Unity Player ID {AuthenticationService.Instance.PlayerId}");

            // Updating LocalUser and LocalLobby
            m_LocalLobby.RemoveUser(m_LocalUser);
            m_LocalUser.ID = AuthenticationService.Instance.PlayerId;
            m_LocalLobby.AddUser(m_LocalUser);
#endif
        }

        public void OnStartClicked()
        {
            m_LobbyUIMediator.ToggleJoinLobbyUI();
            m_LobbyUIMediator.Show();
        }

        public void OnDirectIPClicked()
        {
            m_LobbyUIMediator.Hide();
            m_IPUIMediator.Show();
        }

        public void OnChangeProfileClicked()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // VP1-Play doesn't use Unity profiles, so disable profile changes
            Debug.Log("[ClientMainMenuState] Profile changes not supported with VP1-Play");
            return;
#else
            m_UIProfileSelector.Show();
#endif
        }
    }
}
