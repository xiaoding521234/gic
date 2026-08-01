// PlayerRowUI.cs - 玩家行 UI
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public class PlayerRowView : MonoBehaviour
    {
        [Header("玩家信息")]
        public LocalizedDropdown nameDropdown;
        public LocalizedDropdown spawnDropdown;
        public LocalizedDropdown colorDropdown;
        public LocalizedDropdown teamDropdown;

        [Header("准备")]
        public Button readyButton;
        public TextCombiner readyButtonText;

        private PlayerInfo _player;
        private PlayerManager _playerManager;
        private bool _isHost;
        private bool _isSelf;

        public string PlayerID => _player?.PlayerID;

        private const int ACTION_REMIND = 1;
        private const int ACTION_KICK = 2;

        private bool _isInitializing = false;

        public void Setup(PlayerInfo player)
        {
            _player = player;
            _playerManager = Wargame.Instance.PlayerManager;
            
            _isSelf = _playerManager.IsSelfPlayer(player.PlayerID);
            _isHost = Mirror.NetworkServer.active;

            _isInitializing = true;

            InitNameDropdown();
            InitSpawnDropdown();
            InitColorDropdown();
            InitTeamDropdown();

            RefreshUI();
            BindButtons();

            _isInitializing = false;
        }

        public void RefreshFromPlayerInfo(PlayerInfo updatedInfo)
        {
            _player = updatedInfo;
            _isInitializing = true;

            spawnDropdown.SetValueWithoutNotify(GetCurrentSpawnIndex());
            colorDropdown.SetValueWithoutNotify((int)_player.Color - 1);
            teamDropdown.SetValueWithoutNotify((int)_player.Team);

            UpdateCaptionColor(_player.Color);

            RefreshUI();
            _isInitializing = false;
        }

        void RefreshUI()
        {
            readyButtonText.ClearAllEntries();
            if (_player.IsReady)
            {
                readyButtonText.AddStaticEntry("√");
            }
            else
            {
                readyButtonText.AddStaticEntry("×");
            }

            bool canEdit = _isSelf || _isHost;
            spawnDropdown.SetInteractable(canEdit);
            colorDropdown.SetInteractable(canEdit);
            teamDropdown.SetInteractable(canEdit);
            readyButton.interactable = _isSelf;
        }

        void InitNameDropdown()
        {
            nameDropdown.ClearOptions();
            nameDropdown.RemoveAllListeners();

            if (_isHost && !_isSelf)
            {
                string remindText = GetLocalizedString("Remind");
                string kickText = GetLocalizedString("Kick");
                
                var nameOptions = new List<TextEntry>
                {
                    new TextEntry(null, _player.PlayerName),
                    new TextEntry(null, remindText),
                    new TextEntry(null, kickText)
                };
                nameDropdown.SetOptionsFromEntries(nameOptions);
                nameDropdown.SetInteractable(true);
                nameDropdown.AddListener(OnNameActionSelected);
            }
            else
            {
                var nameOptions = new List<TextEntry>
                {
                    new TextEntry(null, _player.PlayerName)
                };
                nameDropdown.SetOptionsFromEntries(nameOptions);
                nameDropdown.SetInteractable(false);
            }

            nameDropdown.SetValueWithoutNotify(0);
        }

        void InitSpawnDropdown()
        {
            var spawnEntries = new List<TextEntry>();
            
            var randomLocalized = new LocalizedString(TableName.UIText.ToString(), "Random");
            spawnEntries.Add(new TextEntry(randomLocalized, ""));
            
            for (int i = 1; i <= 8; i++)
            {
                spawnEntries.Add(new TextEntry(null, i.ToString()));
            }
            
            spawnDropdown.SetOptionsFromEntries(spawnEntries);
            
            spawnDropdown.SetValueWithoutNotify(GetCurrentSpawnIndex());
            spawnDropdown.RemoveAllListeners();
            spawnDropdown.AddListener(OnSpawnSelected);
        }

        int GetCurrentSpawnIndex()
        {
            return (int)_player.SpawnPosition;
        }

        void InitColorDropdown()
        {
            colorDropdown.SetOptionsFromEnum<PlayerColor>();
            
            colorDropdown.SetValueWithoutNotify((int)_player.Color - 1);
            colorDropdown.RemoveAllListeners();
            colorDropdown.AddListener(OnColorSelected);

            UpdateCaptionColor(_player.Color);
        }

        void InitTeamDropdown()
        {
            var teamEntries = new List<TextEntry>();
            
            var teamValues = System.Enum.GetValues(typeof(TeamType));
            foreach (TeamType team in teamValues)
            {
                teamEntries.Add(new TextEntry(null, team.ToString()));
            }
            
            teamDropdown.SetOptionsFromEntries(teamEntries);
            
            teamDropdown.SetValueWithoutNotify((int)_player.Team);
            teamDropdown.RemoveAllListeners();
            teamDropdown.AddListener(OnTeamSelected);
        }

        void OnNameActionSelected(int index)
        {
            if (_isInitializing) return;

            if (index == ACTION_REMIND)
            {
                Debug.Log($"提醒玩家: {_player.PlayerName}");
            }
            else if (index == ACTION_KICK)
            {
                EventBusHub.Instance.Send(new KickPlayerRequestEvent { TargetPlayerID = _player.PlayerID });
            }

            nameDropdown.SetValueWithoutNotify(0);
        }

        void OnSpawnSelected(int index)
        {
            if (_isInitializing) return;

            var spawn = (SpawnPositionType)index;
            EventBusHub.Instance.Send(new SetSpawnRequestEvent 
            { 
                PlayerID = _player.PlayerID, 
                SpawnPosition = spawn 
            });
        }

        void OnColorSelected(int index)
        {
            if (_isInitializing) return;

            var color = (PlayerColor)(index + 1);
            UpdateCaptionColor(color);

            EventBusHub.Instance.Send(new SetColorRequestEvent 
            { 
                TargetPlayerID = _player.PlayerID, 
                Color = color 
            });
        }

        void OnTeamSelected(int index)
        {
            if (_isInitializing) return;

            var team = (TeamType)index;
            EventBusHub.Instance.Send(new SetTeamRequestEvent 
            { 
                TargetPlayerID = _player.PlayerID, 
                Team = team 
            });
        }

        private void UpdateCaptionColor(PlayerColor color)
        {
            if (colorDropdown != null)
            {
                colorDropdown.SetCaptionColor(color.ToColor());
            }
        }

        void BindButtons()
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyClick);
        }

        void OnReadyClick()
        {
            EventBusHub.Instance.Send(new ToggleReadyRequestEvent { TargetPlayerID = _player.PlayerID });
        }

        private string GetLocalizedString(string key)
        {
            var localizedString = new LocalizedString(TableName.UIText.ToString(), key);
            return localizedString.GetLocalizedString();
        }
    }
}


