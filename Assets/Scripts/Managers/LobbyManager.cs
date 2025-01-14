using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Models;
using Services;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using ColorUtility = UnityEngine.ColorUtility;
using Random = UnityEngine.Random;

namespace Managers
{
    public sealed class LobbyManager : MonoBehaviour
    {
        /// <summary>
        /// The lobby this player is currently in
        /// </summary>
        [CanBeNull] private Lobby _joinedLobby;

        //There is only one lobby manager in the game per player
        /// <summary>
        /// The singleton instance of this class
        /// </summary>
        public static LobbyManager Instance { get; private set; }

        [CanBeNull]
        public Lobby JoinedLobby
        {
            get => _joinedLobby;
            private set
            {
                _joinedLobby = value;
                OnJoinedLobbyChanged();
            }
        }
    
        private Queue<Task> _lobbyTasks = new Queue<Task>();
    
        [CanBeNull] public Player ThisPlayer => JoinedLobby?.Players.
            Find(player => player.Id == AuthenticationService.Instance.PlayerId);
        [CanBeNull] public Player HostPlayer => JoinedLobby?.Players.
            Find(player => player.Id == JoinedLobby.HostId);
    
        public LobbyState State => Enum.Parse<LobbyState>(JoinedLobby?.Data[LobbyStateProperty].Value);
        public bool GameStarted => State is LobbyState.Started or LobbyState.Starting;
    
        public bool IsHost => AuthenticationService.Instance.PlayerId == JoinedLobby?.HostId;

        public Player GetPlayer(string playerId) => JoinedLobby?.Players.Find(player => player.Id == playerId);

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy || State is LobbyState.Starting;
            set
            {
                _isBusy = value;
                if (_isBusy)
                    OnBusy();
                else
                    OnNoLongerBusy();
            }
        }
    
        /*Lobbies are stored somewhere in the internet and so all users need to constantly poll for updates
     The limit for requests by unity are 1 per second*/
        private const float PollingForLobbyUpdatesInterval = 1.1f;
        private float _pollingForLobbyUpdatesTimer = PollingForLobbyUpdatesInterval;
        
        private const float HeartbeatLobbyInterval = 15f;
        private float _heartbeatLobbyTimer = HeartbeatLobbyInterval;
    
        public const string LobbyStateProperty = "state";
        public const string PlayerNameProperty = "name";
        public const string PlayerIsReadyProperty = "isReady";
        public const string PlayerColorProperty = "color";
        public const string RelayCodeProperty = "relayCode";
    
        public const int MaxPlayerCount = 4;
        

        //Assigns the singleton instance of this class and makes sure it is not destroyed on scene change
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private async void Update()
        {
            HandlePollingForLobbyUpdates();
            HandleHeartbeatLobby();
            if(_lobbyTasks.Count == 0 || IsBusy) return;
            IsBusy = true;
            await _lobbyTasks.Dequeue();
            IsBusy = false;
        }

        private void HandleHeartbeatLobby()
        {
            if (JoinedLobby == null || !IsHost) return;
            _heartbeatLobbyTimer -= Time.deltaTime;
            if (!(_heartbeatLobbyTimer < 0)) return;
            _heartbeatLobbyTimer = HeartbeatLobbyInterval;
            HeartbeatLobby(JoinedLobby.Id);
        }


        /// <summary>
        /// Creates a new lobby with the name given in the input field
        /// </summary>
        /// <param name="lobbyName">The name of the lobby to be created</param>
        /// <param name="isPrivate">If the lobby is private: <c>true</c> or public: <c>false</c></param>
        public async Task CreateLobby(string lobbyName, bool isPrivate)
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetPlayer(),
                Data = new Dictionary<string, DataObject>()
                {
                    {LobbyStateProperty, new DataObject(DataObject.VisibilityOptions.Public, LobbyState.Waiting.ToString())}
                }
            };

            try
            {
                OnCreatingLobby();
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MaxPlayerCount, options);
                Debug.Log($"Created lobby {lobbyName} with id {lobby.Id} and code {lobby.LobbyCode}");
                JoinedLobby = lobby;
                OnLobbyCreated();
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }

        public async Task<List<Lobby>> GetOpenLobbies()
        {
            try
            {
                QueryLobbiesOptions options = new QueryLobbiesOptions();
                options.Count = 25;
                // Order by newest lobbies first
                options.Order = new List<QueryOrder>()
                {
                    new QueryOrder(
                        asc: false,
                        field: QueryOrder.FieldOptions.Created)
                };

                QueryResponse lobbies = await Lobbies.Instance.QueryLobbiesAsync(options);
                return lobbies.Results;
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.Log(e);
                return new List<Lobby>();
            }
        }

        private async Task<Allocation> AllocateRelay()
        {
            if (!IsHost) return default;
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayerCount - 1);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            await SetRelayCode(relayCode);
            Debug.Log($"Allocated relay with code {relayCode}");
            return allocation;
        }
        
        private async Task JoinRelay(string relayCode)
        {
            try
            {
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
                UnityTransport unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                unityTransport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
                Debug.Log($"Relay joined {relayCode}");
            }
            catch (RelayServiceException e)
            {
                OnFailedToStartGame();
                Debug.LogException(e);
            }
        }

        public async Task JoinLobby(Lobby lobby)
        {
            try
            {
                OnJoiningLobby();
                JoinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id,
                    new JoinLobbyByIdOptions()
                    {
                        Player = GetPlayer()
                    });
                OnLobbyJoined();
                Debug.Log("Joined lobby " + JoinedLobby!.Name);
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }
    
        /// <summary>
        /// Joins a lobby with the given lobby code in the input field
        /// </summary>
        public async Task JoinLobbyByCode(string lobbyCode)
        {
            try
            {
                OnJoiningLobby();
                JoinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode,
                    new JoinLobbyByCodeOptions()
                    {
                        Player = GetPlayer()
                    });
                OnLobbyJoined();
                Debug.Log("Joined lobby " + JoinedLobby!.Name + "with code " + lobbyCode);
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Joins a random public lobby
        /// </summary>
        public async Task QuickJoinLobby()
        {
            try
            {
                OnJoiningLobby();
                JoinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions()
                {
                    Player = GetPlayer()
                });
                OnLobbyJoined();
                Debug.Log("Joined lobby " + JoinedLobby!.Name);
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Leaves the lobby this player is currently in
        /// </summary>
        public void LeaveLobby() => _lobbyTasks.Enqueue(GetLeaveLobbyTask());
    
        private async Task GetLeaveLobbyTask()
        {
            if (JoinedLobby == null || ThisPlayer == null) return;
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(JoinedLobby!.Id, AuthenticationService.Instance.PlayerId);
                _joinedLobby = null;
                OnLobbyLeft();
                Debug.Log($"Left the lobby");
            }
            catch(LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }
    
        /// <summary>
        /// Kicks the player with the given id from the lobby if this player is the host of the lobby
        /// </summary>
        /// <param name="playerId">The id of the player to be kicked from the lobby</param>
        public void KickPlayer(string playerId) => _lobbyTasks.Enqueue(GetKickPlayerTask(playerId));

        private async Task GetKickPlayerTask(string playerId)
        {
            if (JoinedLobby == null || !IsHost) return;
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(JoinedLobby!.Id, playerId);
                string playerName = JoinedLobby.Players.
                    Find(player => player.Id == playerId).Data[PlayerNameProperty].Value;
                Debug.Log($"Successfully kicked the player {playerName}");
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }
    
        /// <summary>
        /// Polls for lobby updates every <see cref="PollingForLobbyUpdatesInterval"/> seconds
        /// </summary>
        private void HandlePollingForLobbyUpdates()
        {
            if (JoinedLobby == null) return;
            _pollingForLobbyUpdatesTimer -= Time.deltaTime;
            if (!(_pollingForLobbyUpdatesTimer < 0)) return;
            _pollingForLobbyUpdatesTimer = PollingForLobbyUpdatesInterval;
            PollForLobbyUpdates();
        }
    
        private async void PollForLobbyUpdates()
        {
            try
            {
                JoinedLobby = await LobbyService.Instance.GetLobbyAsync(JoinedLobby!.Id);
                if(ThisPlayer == null) //Player was kicked
                {
                    JoinedLobby = null;
                    OnPlayerKicked();
                }
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Updates the given data of this player in the cloud
        /// </summary>
        /// <param name="updates">A dictionary containing the properties to be updated,
        /// where the key is the property name to be updated,
        /// and the value the new value for that property</param>
        /// <param name="playerId">The id of the player to update</param>
        public async void UpdatePlayer(Dictionary<string, PlayerDataObject> updates, string playerId = null)
        {
            try
            {
                if (JoinedLobby == null) return;
                JoinedLobby = await LobbyService.Instance.UpdatePlayerAsync
                (JoinedLobby.Id, playerId ?? AuthenticationService.Instance.PlayerId,
                    new UpdatePlayerOptions()
                    {
                        Data = updates
                    });
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Updates the state of the lobby in the cloud
        /// </summary>
        public async Task UpdateLobbyState(LobbyState newState)
        {
            await UpdateLobbyData(new Dictionary<string, DataObject>()
            {
                {LobbyStateProperty, new DataObject(DataObject.VisibilityOptions.Public, newState.ToString())}
            });
            OnLobbyStateChanged();
        }

        private async Task SetRelayCode(string relayCode)
        {
            await UpdateLobbyData(new Dictionary<string, DataObject>()
            {
                {RelayCodeProperty, new DataObject(DataObject.VisibilityOptions.Member, relayCode)}
            });
        }

        private async Task UpdateLobbyData(Dictionary<string, DataObject> updates)
        {
            try
            {
                if (JoinedLobby == null) return;
                JoinedLobby = await LobbyService.Instance.UpdateLobbyAsync(JoinedLobby.Id,
                    new UpdateLobbyOptions()
                    {
                        Data = updates
                    });
            }
            catch (LobbyServiceException e)
            {
                PopupBox.Instance.DisplayLobbyError(e);
                Debug.LogException(e);
            }    
        }
    
        /// <summary>
        /// Creates a <see cref="Player"/> object with the current player's id, name and additional properties
        /// </summary>
        private Player GetPlayer()
        {
            string name = "";
            if(PlayFabManager.Instance != null && PlayFabManager.Instance.Player != null)
                name = PlayFabManager.Instance.Player.DisplayName;
            name = string.IsNullOrEmpty(name) ? AuthenticationService.Instance.PlayerId : name;
            return new Player(
                id: AuthenticationService.Instance.PlayerId,
                data: new Dictionary<string, PlayerDataObject>()
                {
                    {PlayerNameProperty, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, 
                        name)},
                    {PlayerIsReadyProperty, new PlayerDataObject
                        (PlayerDataObject.VisibilityOptions.Member, false.ToString())},
                    {PlayerColorProperty, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, 
                        "#" + ColorUtility.ToHtmlStringRGB(
                            Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f)))}
                }
            );
        }
    
        private void HeartbeatLobby(string lobbyId)
        {
            try
            {
                LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            } catch(LobbyServiceException e) { Debug.LogException(e); }
        }
        
        public void StartSinglePlayerGame()
        {
            UnityTransport unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            unityTransport.SetConnectionData(NetworkEndPoint.LoopbackIpv4);
            NetworkManager.Singleton.StartHost();
            SceneLoader.LoadScene(Scenes.Game);
        }
    

        public async void BeginStartingGameAsHost()
        {
            //can't start game if you're: not in a lobby, already hosting or are not the lobby host
            try
            {
                if(JoinedLobby == null || NetworkManager.Singleton.IsHost || !IsHost)
                    return;
                IsBusy = true;
                Allocation allocation = await AllocateRelay();
                UnityTransport unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                unityTransport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
                Debug.Log($"Set Host relay data {unityTransport.ConnectionData.Address}");
                
                NetworkManager.Singleton.StartHost();
                Debug.Log("NetCode: Started host");
                
                await UpdateLobbyState(LobbyState.Starting);
                OnGameStarting();
            }
            catch (Exception e)
            {
                OnFailedToStartGame();
                Debug.LogException(e);
            }
        }
    

        public async void OnGameStarted(string scenename, LoadSceneMode loadscenemode, 
            List<ulong> clientscompleted, List<ulong> clientstimedout)
        {
            await UpdateLobbyState(LobbyState.Started);
            IsBusy = false;
            Debug.Log("Game started!");
        }

        /// <summary>
        /// Is called when the player joins or leaves a lobby
        /// </summary>
        public event Action JoinedLobbyChanged;
        public event Action LobbyStateChanged;
        public event Action CreatingLobby;
        public event Action LobbyCreated;
        public event Action JoiningLobby;
        public event Action LobbyJoined;
        public event Action LobbyLeft;
        public event Action PlayerKicked;
        public event Action Busy;
        public event Action NoLongerBusy;
        public event Action GameStarting;
        public event Action FailedToStartGame;
    
    
        private void OnJoinedLobbyChanged()
        {
            JoinedLobbyChanged?.Invoke();
        }

        public async void TryConnectToGame()
        {
            if (NetworkManager.Singleton == null) return;
            if(NetworkManager.Singleton.IsClient) return;
            IsBusy = true;
            await JoinRelay(JoinedLobby!.Data[RelayCodeProperty].Value);
            OnGameStarting();
            Debug.Log("Client started.");
        }
        

        private void OnApplicationQuit()
        {
            LeaveLobby();
        }

        private void OnLobbyStateChanged()
        {
            Debug.Log("Changed lobby state to " + State);
            LobbyStateChanged?.Invoke();
        }
    
        private void OnLobbyCreated()
        {
            LobbyCreated?.Invoke();
        }

        private void OnLobbyJoined()
        {
            LobbyJoined?.Invoke();
        }

        private void OnLobbyLeft()
        {
            LobbyLeft?.Invoke();
        }

        private void OnPlayerKicked()
        {
            PlayerKicked?.Invoke();
        }

        private void OnBusy()
        {
            Busy?.Invoke();
        }

        private void OnNoLongerBusy()
        {
            NoLongerBusy?.Invoke();
        }

        private void OnCreatingLobby()
        {
            CreatingLobby?.Invoke();
        }

        private void OnJoiningLobby()
        {
            JoiningLobby?.Invoke();
        }

        private void OnGameStarting()
        {
            GameStarting?.Invoke();
        }

        private void OnFailedToStartGame()
        {
            FailedToStartGame?.Invoke();
        }
    }
}
