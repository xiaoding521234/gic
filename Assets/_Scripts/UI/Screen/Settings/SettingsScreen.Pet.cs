// ==================== SettingsScreen.Pet.cs（派蒙设置：形态切换 + 连带关闭，docs/19 §6.4） ====================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.Tool; // TextEntry（同 Display/Account 各 partial 的 using 约定）

namespace GIC.UI
{
    public partial class SettingsScreen
    {
        /// <summary>派蒙形态枚举值（与 PlayerSaveData.pet.petForm 对应：0=桌面版，1=游戏画面内版）</summary>
        private const int PET_FORM_DESKTOP = 0;
        private const int PET_FORM_INGAME = 1;

        /// <summary>派蒙形态下拉的选项索引缓存（形态变更事件刷新用——非 Windows 平台无桌面项）</summary>
        private int _petFormDesktopIdx = -1, _petFormIngameIdx = -1;
        /// <summary>形态变更事件处理器（持引用；ScreenBase.OnDestroy 自动 UnsubscribeOwner(this)）</summary>
        private PetFormChangedHandler _petFormHandler;

        /// <summary>当前平台是否可选桌面版（Win32 专属形态；安卓等平台恒游戏内版）</summary>
        public static bool DesktopFormAvailable =>
#if UNITY_STANDALONE_WIN
            true;
#else
            false;
#endif

        private void InitPetSettings()
        {
            InitPetFormSetting();
            InitPetCloseSetting();
            InitPetProviderSetting();
            InitPetApiKeySetting();
            InitPetVoiceSettings();
        }

        // ---- 派蒙语音（2026-09-16，docs/19 §6.5.11）：三模式=关闭/本地侧车/云端TTS+克隆 ----
        // 设置只存 pet.json（学 quickMessages 先例——单一事实源零双写；桌面进程双进程铁律下天然可达）；
        // 读取全走 PetPrefs 直读通道（另一进程改动立即可见），不进主存档。

        /// <summary>语音区五件套聚合（模式/侧车地址/侧车程序路径/云端key/音色ID/音量）</summary>
        private void InitPetVoiceSettings()
        {
            InitVoiceModeSetting();
            InitVoiceSidecarUrlSetting();
            InitVoiceSidecarPathSetting();
            InitVoiceCloudKeySetting();
            InitVoiceCloudVoiceIdSetting();
            InitVoiceVolumeSetting();
        }

        /// <summary>语音模式三选：0=关闭，1=本地侧车（玩家自建 GPT-SoVITS），2=云端 TTS+克隆（玩家自填 key）。
        /// 默认关闭——零进包设计下语音完全是玩家自配的进阶功能（游戏构建不含任何 TTS 引擎/模型）。</summary>
        private void InitVoiceModeSetting()
        {
            var options = new List<TextEntry>
            {
                new TextEntry(new LocalizedString("UIText", "PetVoiceModeOff"), ""),
                new TextEntry(new LocalizedString("UIText", "PetVoiceModeSidecar"), ""),
                new TextEntry(new LocalizedString("UIText", "PetVoiceModeCloud"), ""),
            };
            int current = GIC.Pet.PetPrefs.ReadVoiceMode();
            voiceModeSetting.Setup("PetVoiceMode", options, current, (index) =>
            {
                GIC.Pet.PetPrefs.WriteVoiceMode(index);
            });
            voiceModeSetting.Initialize();
        }

        /// <summary>侧车地址（ButtonSettingItem+输入弹窗=项目文本值既有模式）：默认 http://127.0.0.1:9880
        ///（GPT-SoVITS api_v2 默认端口）——玩家自建侧车改这个。显示原值（非机密）。</summary>
        private void InitVoiceSidecarUrlSetting()
        {
            voiceSidecarUrlSetting.Setup("PetVoiceSidecarUrl", "",
                onClick: () =>
                {
                    ShowInputPanel(voiceSidecarUrlSetting, GIC.Pet.PetPrefs.ReadVoiceSidecarUrl(), (v) =>
                    {
                        v = v.Trim();
                        GIC.Pet.PetPrefs.WriteVoiceSidecar(v, GIC.Pet.PetPrefs.ReadVoiceSidecarPath());
                        voiceSidecarUrlSetting.UpdateValue(v);
                    }, "PetVoiceSidecarUrlInput", 200);
                },
                onValueConfirmed: null,
                placeholderKey: null);
            voiceSidecarUrlSetting.Initialize();
            voiceSidecarUrlSetting.UpdateValue(GIC.Pet.PetPrefs.ReadVoiceSidecarUrl());
        }

        /// <summary>侧车程序路径（可选）：填了=游戏检测侧车未运行时自动拉起一次（bat/exe/lnk 均可）；
        /// 空=不拉起（玩家自己开）。占位引导文案走 PetVoiceSidecarPathNotSet。</summary>
        private void InitVoiceSidecarPathSetting()
        {
            voiceSidecarPathSetting.Setup("PetVoiceSidecarPath", "",
                onClick: () =>
                {
                    ShowInputPanel(voiceSidecarPathSetting, GIC.Pet.PetPrefs.ReadVoiceSidecarPath(), (v) =>
                    {
                        v = v.Trim();
                        GIC.Pet.PetPrefs.WriteVoiceSidecar(GIC.Pet.PetPrefs.ReadVoiceSidecarUrl(), v);
                        voiceSidecarPathSetting.UpdateValue(v);
                    }, "PetVoiceSidecarPathInput", 400);
                },
                onValueConfirmed: null,
                placeholderKey: "PetVoiceSidecarPathNotSet");
            voiceSidecarPathSetting.Initialize();
        }

        /// <summary>云端 TTS Key（同对话 API Key 全套：输入弹窗回显明文→AES 加密存 pet.json
        /// voiceCloudKeyCipher→显示脱敏。MiniMax JWT——客户端自动从中解 GroupId，玩家只填这一个）。</summary>
        private void InitVoiceCloudKeySetting()
        {
            voiceCloudKeySetting.Setup("PetVoiceCloudKey", "",
                onClick: () =>
                {
                    ShowInputPanel(voiceCloudKeySetting, CurrentVoiceKeyPlain(), (v) =>
                    {
                        v = v.Trim();
                        string cipher = GIC.Pet.PetApiKeyCrypto.Encrypt(v);
                        GIC.Pet.PetPrefs.WriteVoiceCloud(cipher, GIC.Pet.PetPrefs.ReadVoiceCloudVoiceId());
                        voiceCloudKeySetting.UpdateValue(GIC.Pet.PetApiKeyCrypto.MaskKey(v));
                    }, "PetVoiceCloudKeyInput", 400);
                },
                onValueConfirmed: null,
                placeholderKey: "PetApiKeyNotSet");
            voiceCloudKeySetting.Initialize();
            RefreshVoiceKeyDisplay();
        }

        /// <summary>当前云端 key 明文（解密；未设置=空串）</summary>
        private string CurrentVoiceKeyPlain()
        {
            string cipher = GIC.Pet.PetPrefs.ReadVoiceCloudKeyCipher();
            return string.IsNullOrEmpty(cipher) ? "" : GIC.Pet.PetApiKeyCrypto.Decrypt(cipher);
        }

        /// <summary>刷新云端 key 行显示（已设置=脱敏；未设置=占位）</summary>
        private void RefreshVoiceKeyDisplay()
        {
            string plain = CurrentVoiceKeyPlain();
            voiceCloudKeySetting.UpdateValue(string.IsNullOrEmpty(plain)
                ? ""
                : GIC.Pet.PetApiKeyCrypto.MaskKey(plain));
        }

        /// <summary>云端音色 ID：填玩家在供应商侧克隆的音色 ID（如 MiniMax 声音复刻）；
        /// 空=供应商默认预置少女声。显示原值（非机密）。</summary>
        private void InitVoiceCloudVoiceIdSetting()
        {
            voiceCloudVoiceIdSetting.Setup("PetVoiceCloudVoiceId", "",
                onClick: () =>
                {
                    ShowInputPanel(voiceCloudVoiceIdSetting, GIC.Pet.PetPrefs.ReadVoiceCloudVoiceId(), (v) =>
                    {
                        v = v.Trim();
                        GIC.Pet.PetPrefs.WriteVoiceCloud(GIC.Pet.PetPrefs.ReadVoiceCloudKeyCipher(), v);
                        voiceCloudVoiceIdSetting.UpdateValue(v);
                    }, "PetVoiceCloudVoiceIdInput", 200);
                },
                onValueConfirmed: null,
                placeholderKey: "PetVoiceCloudVoiceIdNotSet");
            voiceCloudVoiceIdSetting.Initialize();
        }

        /// <summary>语音音量（0..1 步进 10%）：起播时直读——设置改动下一句生效。</summary>
        private void InitVoiceVolumeSetting()
        {
            petVoiceVolumeSetting.Setup("PetVoiceVolume", 0f, 1f, GIC.Pet.PetPrefs.ReadVoiceVolume(),
                v => GIC.Pet.PetPrefs.WriteVoiceVolume(v), true, 0.1f);
            petVoiceVolumeSetting.Initialize();
        }

        /// <summary>对话模型供应商（2026-08-29 多供应商支持，Spring AI 式"选供应商+填自己的 key"）：
        /// PetChatProviders 注册表（DeepSeek/Kimi/GLM/通义/OpenAI，全部 OpenAI 兼容）——玩家选一家、
        /// 填该家的 key 即可对话，模型名/端点对玩家不可见。
        /// 2026-09-13 分槽（用户拍板"每个 LLM 对应一个 key，持久保存"）：key 按供应商分槽持久，
        /// 切换**不清 key**——显示直接切到该家自己的 key（已填=脱敏回显，未填=占位引导）。
        /// 注意 Setup 的 defaultValue 框架语义=Initialize() 的显示值——必须传当前存档值（2026-08-27 首测踩坑）。</summary>
        private void InitPetProviderSetting()
        {
            var options = new List<TextEntry>();
            foreach (var p in GIC.Pet.Chat.PetChatProviders.table)
                options.Add(new TextEntry(new UnityEngine.Localization.LocalizedString("UIText", "PetProvider_" + p.id), ""));

            int current = Mathf.Clamp(_saveManager.CurrentSave.pet.petChatProvider, 0, options.Count - 1);

            petProviderSetting.Setup("PetChatProvider", options, current, (index) =>
            {
                if (index == _saveManager.CurrentSave.pet.petChatProvider) return; // 同项重选不触发刷新
                _saveManager.Modify(s => s.pet.petChatProvider = index);
                GIC.Pet.PetPrefs.WriteChatProvider(index);
                RefreshPetApiKeyDisplay(); // 显示切到该家自己的 key（已填=脱敏；未填=占位引导）
            });
            petProviderSetting.Initialize();
        }

        /// <summary>对话 API Key（2026-08-28 用户拍板：玩家自输自己的 key，不花开发者钱）：
        /// 按钮→输入弹窗回显脱敏 key→确认后 AES 加密存**当前供应商槽位**（明文永不落盘，PetApiKeyCrypto）。
        /// 显示=脱敏（前6+****+后4）；空=占位"未设置"。输入弹窗空值不触发回调（OnConfirm 拒空）——
        /// 清除 key 走删除存档或后续右键菜单，一期不做。
        /// 2026-08-29：落盘改走 PetPrefs.WriteChatCipher（磁盘读改写+编辑器也生效——旧走
        /// PetPrefs.Save() 在编辑器恒跳过=密文从未落盘，且跨进程陈旧缓存整体覆写会抹密文，
        /// "重启后设置里有 key 但对话报未设置"两根因）；上限 64→200（OpenAI key 可超百字符）。
        /// 2026-09-13：key 按供应商分槽（切供应商各家 key 各自持久，不再清空重输）。</summary>
        private void InitPetApiKeySetting()
        {
            petApiKeySetting.Setup("PetApiKey", "",
                onClick: () =>
                {
                    // 回显当前明文（弹窗内可见全 key——本机用户自己输的，回显方便核对改错）
                    ShowInputPanel(petApiKeySetting, CurrentPetApiKeyPlain(), (newValue) =>
                    {
                        newValue = newValue.Trim();
                        string cipher = GIC.Pet.PetApiKeyCrypto.Encrypt(newValue);
                        _saveManager.Modify(s => s.pet.SetChatCipher(s.pet.petChatProvider, cipher));   // 统一变更入口（2026-09-05 Modify 迁移）：写当前供应商槽
                        // 同步密文到 pet.json 同一供应商槽位（桌面桌宠进程永不读主存档——靠这条共享通道取 key，
                        // 密文传输安全；WriteChatCipher 磁盘读改写保留其它字段+编辑器也生效）
                        GIC.Pet.PetPrefs.WriteChatCipher(_saveManager.CurrentSave.pet.petChatProvider, cipher);
                        petApiKeySetting.UpdateValue(GIC.Pet.PetApiKeyCrypto.MaskKey(newValue));
                    }, "PetApiKeyInput", 200); // 上限 200：DeepSeek sk-35 字符，OpenAI sk-proj- 可超百字符（旧 64 会截断）
                },
                onValueConfirmed: null,
                placeholderKey: "PetApiKeyNotSet");
            petApiKeySetting.Initialize();
            // 初始显示：当前供应商槽的 key（已设置=脱敏；未设置=占位）
            RefreshPetApiKeyDisplay();
        }

        /// <summary>当前供应商槽位的明文 key（解密；未设置=空串）</summary>
        private string CurrentPetApiKeyPlain()
        {
            var petData = _saveManager.CurrentSave.pet;
            return GIC.Pet.PetApiKeyCrypto.Decrypt(petData.GetChatCipher(petData.petChatProvider));
        }

        /// <summary>刷新 API Key 行显示=当前供应商槽位的 key（已设置=脱敏回显；未设置=占位引导）。
        /// 初始进界面与供应商切换两处共用。</summary>
        private void RefreshPetApiKeyDisplay()
        {
            string plain = CurrentPetApiKeyPlain();
            petApiKeySetting.UpdateValue(string.IsNullOrEmpty(plain)
                ? ""
                : GIC.Pet.PetApiKeyCrypto.MaskKey(plain));
        }

        /// <summary>派蒙形态：桌面版（仅 Windows）/ 游戏画面内版。切换即时生效（PetInGameHost 热切换；
        /// 桌面版在非 Windows 平台不进选项，读档侧对非法值钳为游戏内版）。
        /// 注意：Setup 的 defaultValue 框架语义=Initialize() 的显示值（非"新玩家默认"）——必须传当前存档值；
        /// 传错则下拉打开即显示错项，再点同项不触发 onValueChanged=点了没反应（2026-08-27 首测踩坑实证）。</summary>
        private void InitPetFormSetting()
        {
            var options = new List<TextEntry>();
            int desktopIdx = -1, ingameIdx = -1;
            if (DesktopFormAvailable)
            {
                desktopIdx = options.Count;
                options.Add(new TextEntry(new LocalizedString("UIText", "PetFormDesktop"), ""));
            }
            ingameIdx = options.Count;
            options.Add(new TextEntry(new LocalizedString("UIText", "PetFormInGame"), ""));
            _petFormDesktopIdx = desktopIdx;
            _petFormIngameIdx = ingameIdx;

            int current = GetEffectiveForm();
            int currentIndex = current == PET_FORM_DESKTOP ? desktopIdx : ingameIdx;

            petFormSetting.Setup("PetForm", options, currentIndex, (index) =>
            {
                int form = index == desktopIdx ? PET_FORM_DESKTOP : PET_FORM_INGAME;
                _saveManager.Modify(s => s.pet.petForm = form);   // 统一变更入口（2026-09-05 Modify 迁移）
                GIC.Pet.PetInGameHost.HotSwitchForm(form);
            });
            petFormSetting.Initialize();

            // 订阅形态变更事件（2026-09-01）：三连击手势/桌宠 IPC 接管等外部切换后刷新下拉显示——
            // 否则显示旧值且点同项不触发 onValueChanged（点了没反应陷阱）。ScreenBase.OnDestroy 自动退订。
            _petFormHandler = new PetFormChangedHandler(this);
            EventBusHub.Instance.Subscribe(_petFormHandler, this);
        }

        /// <summary>外部形态切换（OnPetFormChangedEvent）刷新下拉显示。SetValue=SetValueWithoutNotify
        /// 不回触发 onValueChanged，无回环。</summary>
        private void RefreshPetFormDropdown()
        {
            int idx = GetEffectiveForm() == PET_FORM_DESKTOP ? _petFormDesktopIdx : _petFormIngameIdx;
            if (idx >= 0) petFormSetting.SetValue(idx);
        }

        /// <summary>形态变更事件处理器（gic-eventbus 标准写法：CanHandle 判 activeInHierarchy 防已销毁回调）</summary>
        private class PetFormChangedHandler : IEventHandler<OnPetFormChangedEvent>
        {
            private readonly SettingsScreen _screen;
            public PetFormChangedHandler(SettingsScreen s) => _screen = s;
            public bool CanHandle(OnPetFormChangedEvent evt) => _screen != null && _screen.gameObject.activeInHierarchy;
            public void Handle(OnPetFormChangedEvent evt) => _screen.RefreshPetFormDropdown();
        }

        /// <summary>读档侧钳制：非 Windows 平台/非法值恒游戏内版（存 0 的老档在安卓上跑=钳 1）</summary>
        private int GetEffectiveForm()
        {
            int form = _saveManager.CurrentSave.pet.petForm;
            if (form != PET_FORM_DESKTOP && form != PET_FORM_INGAME) form = PET_FORM_INGAME;
            if (form == PET_FORM_DESKTOP && !DesktopFormAvailable) form = PET_FORM_INGAME;
            return form;
        }

        /// <summary>关闭游戏连带关闭派蒙（仅桌面形态有意义——游戏内形态天然随进程销毁）。
        /// 选项与 PlayerSaveData.pet.closePetOnExit 对应：0=开（随游戏退出），1=关（独立存活）。
        /// 2026-08-27 从"其它"栏挪入"派蒙"栏。</summary>
        private void InitPetCloseSetting()
        {
            var options = new List<TextEntry>
            {
                new TextEntry(new LocalizedString("UIText", "On"), ""),
                new TextEntry(new LocalizedString("UIText", "Off"), ""),
            };

            int current = _saveManager.CurrentSave.pet.closePetOnExit ? 0 : 1;

            petCloseSetting.Setup("ClosePetOnExit", options, current, (index) =>
            {
                _saveManager.Modify(s => s.pet.closePetOnExit = index == 0);   // 统一变更入口（2026-09-05 Modify 迁移）
            });
            petCloseSetting.Initialize();
        }
    }
}
