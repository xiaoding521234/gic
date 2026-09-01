using System.Collections.Generic;
using GIC.UI;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 玩家手抽观察者（2026-08-31，用户需求"玩家自己抽卡派蒙也应有回复和动作"）：玩家自己点
    /// "祈愿 1 次/10 次"按钮抽卡时，派蒙对玩家的手气做出反应——与派蒙代抽严格区分：
    /// 事件描述以"旅行者自己抽卡"框架书写（LLM 据此切换成喝彩/心疼的观赛语气，而非"我抽到了"
    /// 的得意语气）；LLM 历史注记同步区分（之后问"我刚才手气怎么样"答得对得上）。
    /// 防串场：射击/完成事件里校验 IsAutoDraw——AI 代抽轮的反应归 PetWishAutoRunner，观察者跳过
    ///（同一控制器先玩家抽后代抽的少见序列也安全）。
    /// 挂接：WishScreen.StartDraw 玩家路径（TryStartAutoDraw 不挂——那是 AI 路径）。
    /// </summary>
    public static class PetWishPlayerObserver
    {
        static WishDrawController _watch;
        static int _count, _gold, _purple, _bad, _badStreak;
        static readonly List<string> _goldNames = new();

        /// <summary>开始观察一轮玩家手抽（WishScreen.StartDraw 调用；重复调用自动换靶重置）</summary>
        public static void Observe(WishDrawController controller, int count)
        {
            Detach();
            if (controller == null) return;
            _watch = controller;
            _count = count;
            _gold = _purple = _bad = _badStreak = 0;
            _goldNames.Clear();
            controller.OnShotPlanned += HandleShot;
            controller.OnWishComplete += HandleComplete;
        }

        static void Detach()
        {
            if (_watch != null)
            {
                _watch.OnShotPlanned -= HandleShot;
                _watch.OnWishComplete -= HandleComplete;
                _watch = null;
            }
        }

        static void HandleShot(WishShotResult shot)
        {
            if (_watch == null || _watch.IsAutoDraw) return; // AI 代抽轮：反应归 PetWishAutoRunner
            int shotNo = shot.shotIndex + 1;
            int star = shot.finalStarLevel;
            if (star >= 5)
            {
                _gold++;
                _badStreak = 0;
                string cardName = PetWishAutoRunner.CardName(shot.finalCardId);
                _goldNames.Add(cardName);
                PetReactionChannel.Push(
                    $"旅行者自己抽卡，第{shotNo}发抽到了五星「{cardName}」！",
                    PetWishAutoRunner.AnimGold,
                    PetWishAutoRunner.Localize("PetWishGold", cardName)); // 逐发兜底与代抽共用（语境中性）
            }
            else if (star == 4)
            {
                _purple++;
                _badStreak = 0;
                string cardName = PetWishAutoRunner.CardName(shot.finalCardId);
                PetReactionChannel.Push(
                    $"旅行者自己抽卡，第{shotNo}发抽到了四星「{cardName}」",
                    PetWishAutoRunner.AnimPurple,
                    PetWishAutoRunner.Localize("PetWishPurple", cardName));
            }
            else if (star <= 2)
            {
                _bad++;
                _badStreak++;
                if (_badStreak == PetWishAutoRunner.BadStreakThreshold1 || _badStreak == PetWishAutoRunner.BadStreakThreshold2)
                    PetReactionChannel.Push(
                        $"旅行者已经连续{_badStreak}发都是两星以下的烂卡了",
                        PetWishAutoRunner.AnimBadStreak,
                        PetWishAutoRunner.Localize("PetWishBadStreak", _badStreak));
            }
            else
            {
                _badStreak = 0; // 3★ 中性：重置连击
            }
        }

        static void HandleComplete()
        {
            if (_watch == null || _watch.IsAutoDraw) return;
            Detach(); // 先摘订阅（本轮结束）再推事件
            string names = string.Join("、", _goldNames);
            if (_gold >= 1)
            {
                PetReactionChannel.Push(
                    $"旅行者自己抽完了{_count}发卡：出金{_gold}次（{names}）、四星{_purple}次、烂卡{_bad}次",
                    PetWishAutoRunner.AnimDoneGold,
                    PetWishAutoRunner.Localize("PetWishPlayerDoneGold", _count, _gold, _purple),
                    $"旅行者自己抽了{_count}次卡：出金{_gold}次（{names}）、四星{_purple}次、烂卡{_bad}次");
            }
            else if (_purple >= 1)
            {
                PetReactionChannel.Push(
                    $"旅行者自己抽完了{_count}发卡：没出金，四星{_purple}次、烂卡{_bad}次",
                    PetWishAutoRunner.AnimDonePurple,
                    PetWishAutoRunner.Localize("PetWishPlayerDonePurple", _count, _purple),
                    $"旅行者自己抽了{_count}次卡：没出金，四星{_purple}次、烂卡{_bad}次");
            }
            else
            {
                PetReactionChannel.Push(
                    $"旅行者自己抽完了{_count}发卡：全部是烂卡（两星以下），没出金也没四星",
                    PetWishAutoRunner.AnimDoneBad,
                    PetWishAutoRunner.Localize("PetWishPlayerDoneBad", _count),
                    $"旅行者自己抽了{_count}次卡：全部是烂卡（≤2星），没出金也没四星");
            }
        }
    }
}
