// PlayerRowView.cs - 玩家行 UI（房间名册行）
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
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
        [Autowired] private PlayerManager _playerManager;
        private bool _isHost;
        private bool _isSelf;

        public string PlayerID => _player?.PlayerID;

        private const int ACTION_REMIND = 1;
        private const int ACTION_KICK = 2;

        /// <summary>出生点可选数（当前地图出生区数，Setup 传入；出生选项=Random+1..N，2026-10-11 联机批）</summary>
        private int _spawnZoneCount = 8;

        private bool _isInitializing = false;

        /// <summary>Setup（名册行装配）。spawnZoneCount=当前地图出生区数（出生下拉按图裁剪）</summary>
        public void Setup(PlayerInfo player, int spawnZoneCount = 8)
        {
            _player = player;
            _spawnZoneCount = Mathf.Max(0, spawnZoneCount);
            Wargame.Instance.Context.Inject(this);

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

            // 重新初始化名称 dropdown（名称/行动项可能已更新——AI 增删与就绪态在此列）
            InitNameDropdown();

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
            readyButton.interactable = _isSelf; // AI 行恒不可点（AI 恒就绪=登记时置位）
        }

        void InitNameDropdown()
        {
            nameDropdown.ClearOptions();
            nameDropdown.RemoveAllListeners();

            if (_isHost && !_isSelf)
            {
                if (_player.IsAI)
                {
                    // AI 行（2026-10-11 联机批）：点名字=移除（Kick 链对空连接已有守卫，走同一路径）
                    var removeText = GetLocalizedString("Coop_RemoveAI");
                    var nameOptions = new List<TextEntry>
                    {
                        new TextEntry(null, _player.PlayerName),
                        new TextEntry(null, removeText),
                    };
                    nameDropdown.SetOptionsFromEntries(nameOptions);
                    nameDropdown.SetInteractable(true);
                    nameDropdown.AddListener(OnNameActionSelected);
                    _kickActionIndex = ACTION_REMOVE_AI; // AI 行只有移除项（无提醒）
                }
                else
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
                    _kickActionIndex = ACTION_KICK;
                }
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

        // 点名字行动项索引（真人行=2 踢出；AI 行=1 移除——2026-10-11 联机批）
        private int _kickActionIndex = ACTION_KICK;
        private const int ACTION_REMOVE_AI = 1;

        void InitSpawnDropdown()
        {
            var spawnEntries = new List<TextEntry>();

            var randomLocalized = new LocalizedString(TableName.UIText.ToString(), "Random");
            spawnEntries.Add(new TextEntry(randomLocalized, ""));

            // 出生选项数=当前地图出生区数（2026-10-11 联机批：房间选点=进局内落位 RA2 式）
            for (int i = 1; i <= _spawnZoneCount; i++)
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
            // 队伍仅 A/B（2026-10-11 联机拍板：开局限两方——引擎按异队=敌对，
            // 3+ 队=混战语义未实测；旧全枚举 9 队选项退役）
            var teamEntries = new List<TextEntry>
            {
                new TextEntry(null, TeamType.A.ToString()),
                new TextEntry(null, TeamType.B.ToString()),
            };

            teamDropdown.SetOptionsFromEntries(teamEntries);

            int teamValue = (int)_player.Team;
            teamDropdown.SetValueWithoutNotify(teamValue <= 1 ? teamValue : 1); // 旧 C..I 值回落 B（防御）
            teamDropdown.RemoveAllListeners();
            teamDropdown.AddListener(OnTeamSelected);
        }

        void OnNameActionSelected(int index)
        {
            if (_isInitializing) return;

            if (index == ACTION_REMIND && _kickActionIndex == ACTION_KICK)
            {
                GICLog.Info($"提醒玩家: {_player.PlayerName}");
            }
            else if (index == _kickActionIndex)
            {
                EventBusHub.Instance.Send(new KickPlayerRequestEvent { TargetPlayerID = _player.PlayerID });
            }

            // 重置为玩家名称（选项 0）
            nameDropdown.SetValueWithoutNotify(0);
        }

        void OnSpawnSelected(int index)
        {
            if (_isInitializing) return;

            var spawn = (SpawnPositionType)index;
            EventBusHub.Instance.Send(new SetSpawnRequestEvent
            {
                PlayerID = _player.PlayerID,
                SpawnPosition = spawn,
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
                Color = color,
            });
        }

        void OnTeamSelected(int index)
        {
            if (_isInitializing) return;

            var team = (TeamType)index;
            EventBusHub.Instance.Send(new SetTeamRequestEvent
            {
                TargetPlayerID = _player.PlayerID,
                Team = team,
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

        void OnDestroy()
        {
            if (nameDropdown != null) nameDropdown.RemoveAllListeners();
            if (spawnDropdown != null) spawnDropdown.RemoveAllListeners();
            if (colorDropdown != null) colorDropdown.RemoveAllListeners();
            if (teamDropdown != null) teamDropdown.RemoveAllListeners();
            if (readyButton != null) readyButton.onClick.RemoveAllListeners();
        }
    }
}
