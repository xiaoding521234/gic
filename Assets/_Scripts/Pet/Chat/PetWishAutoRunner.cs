using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Data;
using GIC.UI;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// AI 自动抽卡执行器（2026-08-30，PetChatIntent.auto_wish 的异步执行体）：主进程常驻隐藏对象。
    /// 流程：导航到祈愿界面（PetGameBridge，与手动同路径）→ 等界面就绪（默认卡池选中）→
    /// TryStartAutoDraw（自动射击模式）→ 订阅 OnShotPlanned/OnWishComplete 监听结果 →
    /// 按"好卡/连续烂卡"规则经 PetReactionChannel 推送派蒙反应（文本+动作+LLM 注记，
    /// 桌面宠/游戏内宠两形态同通道消费）。
    ///
    /// 反应规则（阈值与动作在此一处定义，两形态共享）：
    /// - 5★（金）：Clap01 拍手庆祝；4★（紫）：Show_1 得意展示
    /// - 连续 ≤2★ 烂卡达 5/9 张：ShakeHead01 失望摇头（3★ 中性重置连击）
    /// - 结算：有金 Show_2 / 有紫无金 Nod01 / 全烂 Sneer01，附 LLM 注记（下次对话可引用结果）
    /// </summary>
    public class PetWishAutoRunner : MonoBehaviour
    {
        // 反应动作（GI 官方 clip 全名；均为单次动作——与行为层随机小动作同池；PetWishPlayerObserver 共用）
        internal const string AnimMagic = "Ani_NPC_Kanban_Paimon_Domagic";   // 开抽仪式
        internal const string AnimGold = "Ani_NPC_Kanban_Paimon_Clap01";     // 出金庆祝
        internal const string AnimPurple = "Ani_NPC_Kanban_Paimon_Show_1";   // 四星得意
        internal const string AnimBadStreak = "Ani_NPC_Kanban_Paimon_ShakeHead01"; // 连续烂卡失望
        internal const string AnimDoneGold = "Ani_NPC_Kanban_Paimon_Show_2"; // 结算-有金
        internal const string AnimDonePurple = "Ani_NPC_Kanban_Paimon_Nod01";// 结算-有紫无金
        internal const string AnimDoneBad = "Ani_NPC_Kanban_Paimon_Sneer01"; // 结算-全烂
        internal const string AnimError = "Ani_NPC_Kanban_Paimon_Confuse01AS"; // 各类失败

        internal const int BadStreakThreshold1 = 5; // 连续烂卡反应阈值（第一档）
        internal const int BadStreakThreshold2 = 9; // 连续烂卡反应阈值（第二档）

        static PetWishAutoRunner _instance;

        bool _running;
        WishDrawController _watch;
        bool _completed;
        int _count, _gold, _purple, _bad, _badStreak;
        readonly List<string> _goldNames = new();

        /// <summary>一次自动抽卡执行中（含导航期）——并发请求直接拒绝</summary>
        public static bool IsBusy => _instance != null && _instance._running;

        /// <summary>取常驻实例（懒创建，DontDestroyOnLoad）</summary>
        public static PetWishAutoRunner Ensure()
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<PetWishAutoRunner>();
            if (_instance == null)
            {
                var go = new GameObject("[PetWishAutoRunner]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PetWishAutoRunner>();
            }
            return _instance;
        }

        void OnDestroy()
        {
            Detach();
            if (_instance == this) _instance = null;
        }

        /// <summary>开始一次自动抽卡（返回 false=已有执行中）</summary>
        public static bool Begin(int count)
        {
            var runner = Ensure();
            if (runner._running) return false;
            runner._running = true;
            runner.StartCoroutine(runner.RunRoutine(count));
            return true;
        }

        IEnumerator RunRoutine(int count)
        {
            var gs = GameScene.Instance;

            // 1. 祈愿界面未开 → 与手动同路径导航
            if (!SceneManager.GetSceneByName(SceneType.WishScreen.SceneName).isLoaded)
            {
                Debug.Log($"[PetWish] 祈愿界面未开，开始导航（count={count}）");
                yield return PetGameBridge.NavigateToScreen(SceneType.WishScreen);
                float appear = Time.unscaledTime + 5f;
                while (FindWishScreen() == null && Time.unscaledTime < appear) yield return null;
            }

            // 2. 等界面基础就绪（Start 初始化+默认角色首选完成）。注意：默认选中的首个角色
            //    未必绑定卡池（当前场景 Columbina 无卡池，唯一卡池在 Venti）——基础就绪≠卡池可用
            WishScreen wish = null;
            float ready = Time.unscaledTime + 8f;
            while (Time.unscaledTime < ready)
            {
                wish = FindWishScreen();
                if (wish != null && wish.IsBaseReady) break;
                yield return null;
            }
            if (wish == null || !wish.IsBaseReady)
            {
                Debug.LogWarning($"[PetWish] 祈愿界面未就绪（超时）wish={wish != null}");
                Reaction(AnimError, "你想帮旅行者抽卡，但没能打开祈愿界面", "PetWishFailed");
                _running = false;
                yield break;
            }

            // 3. 当前卡池为空 → 自动选第一个绑定卡池的角色（与手动点击角色按钮同路径），
            //    等切换协程完成（含面板淡出淡入）后卡池就位
            if (!wish.EnsurePoolSelected())
            {
                Debug.LogWarning("[PetWish] 无可用卡池（所有角色均未绑定卡池）");
                Reaction(AnimError, "你想帮旅行者抽卡，但现在的卡池都还没开放", "PetWishPoolEmpty");
                _running = false;
                yield break;
            }
            float poolReady = Time.unscaledTime + 5f;
            while (Time.unscaledTime < poolReady && !wish.IsAutoDrawReady) yield return null;
            if (!wish.IsAutoDrawReady)
            {
                Debug.LogWarning("[PetWish] 卡池选择后仍未就绪（超时）");
                Reaction(AnimError, "你想帮旅行者抽卡，但没能打开祈愿界面", "PetWishFailed");
                _running = false;
                yield break;
            }

            // 4. 启动（内含原石/进行中/卡池守卫——导航期间状态可能已变化）
            var start = wish.TryStartAutoDraw(count);
            Debug.Log($"[PetWish] TryStartAutoDraw({count}) → {start}");
            if (start != WishScreen.AutoDrawStartResult.Started)
            {
                switch (start)
                {
                    case WishScreen.AutoDrawStartResult.Busy:
                        Reaction(AnimError, "旅行者想让你抽卡，但抽卡已经在进行中了", "PetWishBusy");
                        break;
                    case WishScreen.AutoDrawStartResult.NoPrimogem:
                        int have = Wargame.Instance?.Context?.Get<SaveManager>()?.CurrentSave?.GetItemCount(ItemName.Primogem) ?? 0;
                        Reaction(AnimError,
                            $"旅行者想让你抽{count}次卡，但原石不够：需要{WishManager.SingleWishCost * count}，只有{have}",
                            "PetWishNoPrimogem", count, WishManager.SingleWishCost * count, have);
                        break;
                    default:
                        Reaction(AnimError, "你想帮旅行者抽卡，但现在的卡池都还没开放", "PetWishPoolEmpty");
                        break;
                }
                _running = false;
                yield break;
            }

            // 5. 监听结果 → 反应（订阅在启动之后：只收本次自动抽卡的射击）
            _watch = wish.DrawController;
            _count = count; _gold = _purple = _bad = _badStreak = 0;
            _goldNames.Clear();
            _completed = false;
            _watch.OnShotPlanned += HandleShot;
            _watch.OnWishComplete += HandleComplete;

            Reaction(AnimMagic, $"你刚开始帮旅行者抽卡（共{count}发）", "PetWishStart", count);

            // 6. 等完成（OnWishComplete=最终展示开始；异常路径=界面被销毁）+5 分钟安全兜底
            float done = Time.unscaledTime + 300f;
            while (!_completed && Time.unscaledTime < done)
            {
                if (_watch == null) break; // 场景被强制卸载
                yield return null;
            }
            Detach();
            _running = false;
            Debug.Log($"[PetWish] 流程结束 completed={_completed} gold={_gold} purple={_purple} bad={_bad}");
        }

        void HandleShot(WishShotResult shot)
        {
            int shotNo = shot.shotIndex + 1; // 发数（1-based，反应文案用）
            int star = shot.finalStarLevel;
            if (star >= 5)
            {
                _gold++;
                _badStreak = 0;
                string cardName = CardName(shot.finalCardId);
                _goldNames.Add(cardName);
                Reaction(AnimGold, $"你刚帮旅行者抽卡，第{shotNo}发抽到了五星「{cardName}」！", "PetWishGold", cardName);
            }
            else if (star == 4)
            {
                _purple++;
                _badStreak = 0;
                Reaction(AnimPurple, $"你刚帮旅行者抽卡，第{shotNo}发抽到了四星「{CardName(shot.finalCardId)}」", "PetWishPurple", CardName(shot.finalCardId));
            }
            else if (star <= 2)
            {
                _bad++;
                _badStreak++;
                if (_badStreak == BadStreakThreshold1 || _badStreak == BadStreakThreshold2)
                    Reaction(AnimBadStreak, $"你已经连续{_badStreak}发都是两星以下的烂卡了", "PetWishBadStreak", _badStreak);
            }
            else
            {
                _badStreak = 0; // 3★ 中性：重置连击
            }
        }

        void HandleComplete()
        {
            _completed = true;
            string names = string.Join("、", _goldNames);
            if (_gold >= 1)
            {
                string desc = $"你刚帮旅行者抽完了{_count}发卡：出金{_gold}次（{names}）、四星{_purple}次、烂卡{_bad}次";
                Reaction(AnimDoneGold, desc,
                    $"派蒙刚帮旅行者抽了{_count}次卡：出金{_gold}次（{names}）、四星{_purple}次、烂卡{_bad}次",
                    "PetWishDoneGold", _count, _gold, _purple);
            }
            else if (_purple >= 1)
            {
                string desc = $"你刚帮旅行者抽完了{_count}发卡：没出金，四星{_purple}次、烂卡{_bad}次";
                Reaction(AnimDonePurple, desc,
                    $"派蒙刚帮旅行者抽了{_count}次卡：没出金，四星{_purple}次、烂卡{_bad}次",
                    "PetWishDonePurple", _count, _purple);
            }
            else
            {
                string desc = $"你刚帮旅行者抽完了{_count}发卡：全部是烂卡（两星以下），没出金也没四星";
                Reaction(AnimDoneBad, desc,
                    $"派蒙刚帮旅行者抽了{_count}次卡：全部是烂卡（≤2星），没出金也没四星",
                    "PetWishDoneBad", _count);
            }
        }

        void Detach()
        {
            if (_watch != null)
            {
                _watch.OnShotPlanned -= HandleShot;
                _watch.OnWishComplete -= HandleComplete;
                _watch = null;
            }
        }

        // ==================== 工具 ====================

        static WishScreen FindWishScreen() =>
            FindFirstObjectByType<WishScreen>(FindObjectsInactive.Exclude);

        /// <summary>推送一条反应：anim=动作（消费侧即时播），desc=LLM 生成素材（结构化短句，
        /// 宠进程 LLM 现编话语），fallbackKey/args=本地化兜底模板（LLM 失败/无 key 时直出）</summary>
        static void Reaction(string anim, string desc, string fallbackKey, params object[] fallbackArgs)
        {
            PetReactionChannel.Push(desc, anim, Localize(fallbackKey, fallbackArgs));
        }

        /// <summary>结算反应（多带 LLM 历史注记 note——之后问"抽得怎么样"能答上）</summary>
        static void Reaction(string anim, string desc, string note, string fallbackKey, params object[] fallbackArgs)
        {
            PetReactionChannel.Push(desc, anim, Localize(fallbackKey, fallbackArgs), note);
        }

        internal static string Localize(string key, params object[] args)
        {
            var table = UnityEngine.Localization.Settings.LocalizationSettings.Instance.GetStringDatabase()
                .GetTable("UIText") as UnityEngine.Localization.Tables.StringTable;
            string template = table?.GetEntry(key)?.GetLocalizedString();
            if (string.IsNullOrEmpty(template)) return key;
            try { return args.Length > 0 ? string.Format(template, args) : template; }
            catch (System.FormatException)
            {
                Debug.LogWarning($"[PetWish] Localize 模板格式化失败（键={key}，参数数={args.Length}），回退原模板");
                return template;
            }
        }

        /// <summary>卡牌显示名（UnitName/ItemName 本地化表；查不到回退枚举名）</summary>
        internal static string CardName(CardId id)
        {
            bool isUnit = id.cardType == CardType.Unit;
            string tableName = isUnit ? "UnitName" : "ItemName";
            string key = isUnit ? id.AsUnitName().ToString() : id.AsItemName().ToString();
            var table = UnityEngine.Localization.Settings.LocalizationSettings.Instance.GetStringDatabase()
                .GetTable(tableName) as UnityEngine.Localization.Tables.StringTable;
            return table?.GetEntry(key)?.GetLocalizedString() ?? key;
        }
    }

}
