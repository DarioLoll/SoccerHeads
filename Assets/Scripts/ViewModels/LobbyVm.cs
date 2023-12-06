using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.U2D;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// Provides access to UI elements in the Lobby Scene
/// </summary>
public class LobbyVm : MonoBehaviour
{
    #region fields
    //Placeholders and buttons in the lobby scene

    [SerializeField] private GameObject mainCanvas;
    
    [SerializeField] private TextMeshProUGUI lobbyName;

    [SerializeField] private GameObject lobbyCodeField;

    [SerializeField] private GameObject btnReady;

    [SerializeField] private GameObject btnStartGame;

    [SerializeField] private Transform playerTable;

    [SerializeField] private Transform btnChangeColor;
    private const float SpaceBetweenTableRows = 160f;
    
    private TMP_InputField _lobbyCodeInputField;

    private int _thisPlayer;
    private const int PlayerCount = 4;
    private List<LobbyPlayerVm> _players = new(PlayerCount);
    private LobbyPlayerVm ThisPlayerVm => _players[_thisPlayer];
    private Color _thisPlayerColor;
    private bool _thisPlayerIsReady;
    
    //When something about the player changed, all other players in the lobby need to be notified
    //This requires sending the update info into the internet
    //Unity limits update player requests to 5 requests in 5 seconds, so to be safe, this timer limits
    //update requests to 1 request per 1.1 seconds
    //https://docs.unity.com/ugs/en-us/manual/lobby/manual/rate-limits
    private const float UpdatePlayerInterval = 1.1f;
    private float _updatePlayerTimer = UpdatePlayerInterval;
    private const float BtnChangeColorY = 320f;

    #endregion

    #region methods
    private void Start()
    {
        _lobbyCodeInputField = lobbyCodeField.GetComponent<TMP_InputField>();
        for (int i = 0; i < PlayerCount; i++)
        {
            _players.Add(playerTable.GetChild(i).GetComponent<LobbyPlayerVm>());
        }
        _thisPlayer = LobbyManager.Instance.JoinedLobby!.Players.IndexOf(LobbyManager.Instance.ThisPlayer);
        _players[_thisPlayer].SetPlayer(LobbyManager.Instance.ThisPlayer);
        _thisPlayerColor = ThisPlayerVm.Color;
        _thisPlayerIsReady = ThisPlayerVm.IsReady;
        ErrorDisplay.Instance.mainCanvas = mainCanvas;
        
        UpdateLobby();
        LobbyManager.Instance.JoinedLobbyChanged += UpdateLobby;
    }

    public void ChangeColor()
    {
        Color newColor = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);
        _thisPlayerColor = newColor;
        ThisPlayerVm.Color = newColor;
    }
    
    /// <summary>
    /// Checks if there are any changes to the player data every 1.1s and sends them to the cloud
    /// </summary>
    private void Update()
    {
        Lobby lobby = LobbyManager.Instance.JoinedLobby;
        if (lobby == null) return;
        
        if (_updatePlayerTimer <= 0)
        {
            Dictionary<string, PlayerDataObject> changes = GetPlayerChanges();
            if(changes.Count > 0) LobbyManager.Instance.UpdatePlayer(changes);
            //Reset the timer
            _updatePlayerTimer = UpdatePlayerInterval;
        }
        //Count down the timer
        else _updatePlayerTimer -= Time.deltaTime;
    }

    /// <summary>
    /// Compares the local data of this player with the data in the cloud and returns the differences
    /// </summary>
    private Dictionary<string, PlayerDataObject> GetPlayerChanges()
    {
        Dictionary<string, PlayerDataObject> changes = new();
        
        //If the color of this player in the cloud is different from the local color, add it to the changes
        string playerColor = "#" + ColorUtility.ToHtmlStringRGB(ThisPlayerVm.Color);
        if(LobbyManager.Instance.ThisPlayer!.Data[LobbyManager.PlayerColorProperty].Value != playerColor)
        {
            changes.Add(LobbyManager.PlayerColorProperty, new PlayerDataObject
                (PlayerDataObject.VisibilityOptions.Member, playerColor));
        }
        //If this is the joined player, and the ready status in the cloud
        //is different from the local ready status, add it to the changes
        if(!LobbyManager.Instance.IsHost && LobbyManager.Instance.ThisPlayer.Data[LobbyManager.PlayerIsReadyProperty].Value != 
           ThisPlayerVm.IsReady.ToString())
        {
            changes.Add(LobbyManager.PlayerIsReadyProperty, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                ThisPlayerVm.IsReady.ToString()));
        }
        return changes;
    }

    public void LeaveLobby() => LobbyManager.Instance.LeaveLobby();
    
    public void StartGame()
    {
        Lobby lobby = LobbyManager.Instance.JoinedLobby;
        if (lobby == null) return;
        if (lobby.Players.Count != 2) return;
        if (LobbyManager.Instance.JoinedLobby!.Players.Any
                (player => player.Data[LobbyManager.PlayerIsReadyProperty].Value != true.ToString()))
        {
            return;
        }
        //Start the game, pass in both players
        //Leave the lobbies with both players, load the match scene
        Debug.Log("Starting game");
    }

    /// <summary>
    /// Changes the local ready status
    /// </summary>
    public void SetReady()
    {
        Lobby lobby = LobbyManager.Instance.JoinedLobby;
        if(lobby == null) return;
        //The host doesn't have a ready status, they just click on start when they're ready
        if (LobbyManager.Instance.IsHost) return;
        _thisPlayerIsReady = !_thisPlayerIsReady;
        ThisPlayerVm.IsReady = _thisPlayerIsReady;
    }
    
    /// <summary>
    /// Called after something in the lobby changed (after every poll = every 1.1s by default)
    /// </summary>
    private void UpdateLobby(object sender = null, EventArgs e = default)
    {
        Lobby lobby = LobbyManager.Instance.JoinedLobby;
        if (lobby == null || LobbyManager.Instance.ThisPlayer == null) return;
        
        _thisPlayer = LobbyManager.Instance.JoinedLobby!.Players.IndexOf(LobbyManager.Instance.ThisPlayer);
        
        //Updating the UI:
        lobbyName.text = lobby.Name;
        //Updating the position of the change color button
        var position = btnChangeColor.localPosition;
        position = new Vector3(position.x, _thisPlayer * -SpaceBetweenTableRows + BtnChangeColorY, position.z);
        btnChangeColor.localPosition = position;
        
        _lobbyCodeInputField.text = lobby.LobbyCode;
        //The host doesn't have a ready status, they just click on start when they're ready
        btnReady.SetActive(LobbyManager.Instance.IsHost == false);
        //Only the host can start the game
        btnStartGame.SetActive(LobbyManager.Instance.IsHost);
        UpdatePlayers();
    }

    private void UpdatePlayers()
    {
        for (var i = 0; i < _players.Count; i++)
        {
            var player = _players[i];
            if (LobbyManager.Instance.JoinedLobby!.Players.Count <= i)
            {
                player.gameObject.SetActive(false);
            }
            else
            {
                player.gameObject.SetActive(true);
                player.SetPlayer(LobbyManager.Instance.JoinedLobby!.Players[i]);
            }

            if (i != _thisPlayer) continue;
            player.Color = _thisPlayerColor;
            player.IsReady = _thisPlayerIsReady;
        }
    }

    private void OnDestroy()
    {
        LobbyManager.Instance.JoinedLobbyChanged -= UpdateLobby;
    }

    #endregion
}