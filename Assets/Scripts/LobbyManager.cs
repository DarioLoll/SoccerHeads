using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class LobbyManager : MonoBehaviour
{
    #region field
    /// <summary>
    /// The lobby this player is currently in
    /// </summary>
    [CanBeNull]
    private Lobby _joinedLobby;
    #endregion

    #region properties
    //There is only one lobby manager in the game per player
    /// <summary>
    /// The singleton instance of this class
    /// </summary>
    public static LobbyManager Instance { get; private set; }

    [CanBeNull]
    public Lobby JoinedLobby
    {
        get => _joinedLobby;
        set
        {
            _joinedLobby = value;
            OnJoinedLobbyChanged();
        }
    }
    #endregion

    #region event
    /// <summary>
    /// Is called when the player joins or leaves a lobby
    /// </summary>
    public event EventHandler JoinedLobbyChanged;
    #endregion

    #region constants
    private const string LobbySceneName = "Lobby";
    private const string MainMenuSceneName = "MainMenu";
    
    /*Lobbies are stored somewhere in the internet and so all users need to constantly poll for updates
     The limit for requests by unity are 1 per second*/
    private const float PollingForLobbyUpdatesInterval = 1.1f;
    private float _pollingForLobbyUpdatesTimer = PollingForLobbyUpdatesInterval;
    
    public const string PlayerNameProperty = "name";
    public const string PlayerIsReadyProperty = "isReady";
    #endregion

    #region methods
    //Assigns the singleton instance of this class and makes sure it is not destroyed on scene change
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(Instance.gameObject);
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        HandlePollingForLobbyUpdates();
    }

    /// <summary>
    /// Creates a new lobby with the name given in the input field
    /// </summary>
    /// <param name="lobbyName">The name of the lobby to be created</param>
    /// <param name="isPrivate">If the lobby is private: <c>true</c> or public: <c>false</c></param>
    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
        int maxPlayers = 2;
        CreateLobbyOptions options = new CreateLobbyOptions
        {
            IsPrivate = isPrivate,
            Player = GetPlayer()
        };

        try
        {
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            JoinedLobby = lobby;

            //Calls the HeartbeatLobby method every 15s to keep the lobby active
            StartCoroutine(HeartbeatLobbyCoroutine(JoinedLobby!.Id, 15));
            SceneManager.LoadScene(LobbySceneName);
            Debug.Log($"Created lobby {lobbyName} with id {lobby.Id} and code {lobby.LobbyCode}");
        }
        catch(LobbyServiceException e) { Debug.LogException(e); }
    }
    
    /// <summary>
    /// Joins a lobby with the given lobby code in the input field
    /// </summary>
    public async void JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            JoinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, 
                new JoinLobbyByCodeOptions()
                {
                    Player = GetPlayer()
                });
            SceneManager.LoadScene(LobbySceneName);
            Debug.Log("Joined lobby " + JoinedLobby!.Name + "with code " + lobbyCode);
        }
        catch(LobbyServiceException e) { Debug.LogException(e); }
    }

    /// <summary>
    /// Joins a random public lobby
    /// </summary>
    public async void QuickJoinLobby()
    {
        try
        {
            JoinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions()
            {
                Player = GetPlayer()
            });
            SceneManager.LoadScene(LobbySceneName);
            Debug.Log("Joined lobby " + JoinedLobby!.Name);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogException(e);
        }
    }
    
    /// <summary>
    /// Leaves the lobby this player is currently in
    /// </summary>
    public async void LeaveLobby()
    {
        try
        {
            if (JoinedLobby == null) return;
            await LobbyService.Instance.RemovePlayerAsync(JoinedLobby.Id, AuthenticationService.Instance.PlayerId);
            JoinedLobby = null;
            Debug.Log($"Left the lobby");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogException(e);
        }
    }
    
    /// <summary>
    /// Kicks the player with the given id from the lobby if this player is the host of the lobby
    /// </summary>
    /// <param name="playerId">The id of the player to be kicked from the lobby</param>
    public async void KickPlayer(string playerId)
    {
        try
        {
            if (JoinedLobby == null || AuthenticationService.Instance.PlayerId != JoinedLobby.HostId) return;
            string playerName = JoinedLobby.Players.Find(player => player.Id == playerId).Data[PlayerNameProperty].Value;
            await LobbyService.Instance.RemovePlayerAsync(JoinedLobby!.Id, playerId);
            Debug.Log($"Successfully kicked the player {playerName}");
        }
        catch (LobbyServiceException e)
        {
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
        if (_pollingForLobbyUpdatesTimer < 0)
        {
            _pollingForLobbyUpdatesTimer = PollingForLobbyUpdatesInterval;
            PollForLobbyUpdates();
        }
    }
    
    private async void PollForLobbyUpdates()
    {
        try
        {
            JoinedLobby = await LobbyService.Instance.GetLobbyAsync(JoinedLobby!.Id);
            if (JoinedLobby != null && 
                !JoinedLobby.Players.Exists(player => player.Id == AuthenticationService.Instance.PlayerId))
                JoinedLobby = null;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// Updates the given data of this player in the cloud
    /// </summary>
    /// <param name="updates">A dictionary containing the properties to be updated,
    /// where the key is the property name to be updated,
    /// and the value the new value for that property</param>
    public async void UpdatePlayer(Dictionary<string, PlayerDataObject> updates)
    {
        try
        {
            if (JoinedLobby == null) return;
            JoinedLobby = await LobbyService.Instance.UpdatePlayerAsync(JoinedLobby.Id, AuthenticationService.Instance.PlayerId,
                new UpdatePlayerOptions()
                {
                    Data = updates
                });
        }
        catch (LobbyServiceException e)
        {
            Debug.LogException(e);
        }
    }
    
    /// <summary>
    /// Creates a <see cref="Player"/> object with the current player's id, name and additional properties
    /// </summary>
    private Player GetPlayer()
    {
        return new Player(
            id: AuthenticationService.Instance.PlayerId,
            data: new Dictionary<string, PlayerDataObject>()
            {
                {PlayerNameProperty, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, 
                    AuthenticationService.Instance.PlayerName)},
                {PlayerIsReadyProperty, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, false.ToString())}
            }
        );
    }
    
    //Resets the 30s timer for the lobby with the given id to keep it in the active state
    private IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);

        while (true)
        {
            try
            {
                LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            } catch(LobbyServiceException e) { Debug.LogException(e); }

            yield return delay;
        }
    }
    
    protected virtual void OnJoinedLobbyChanged()
    {
        if (JoinedLobby == null)
        {
            SceneManager.LoadScene(MainMenuSceneName);
            return;
        }
        if(JoinedLobby != null && SceneManager.GetActiveScene().name != LobbySceneName)
            SceneManager.LoadScene(LobbySceneName);
        
        JoinedLobbyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnApplicationQuit()
    {
        LeaveLobby();
    }
    #endregion
}
