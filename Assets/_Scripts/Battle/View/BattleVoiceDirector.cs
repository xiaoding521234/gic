using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{
    /// <summary>战斗语音语义（表现层触发源）。分层依据=「这句台词该不该此刻盖过别的」，
    /// 而非玩法归属；与老 mod 的 VOICE/SUB_VOICE 二分同源，但通道与权重按即时制重划。</summary>
    public enum BattleVoiceKind
    {
        Deploy = 0,   // 登场/部署
        Skill = 1,    // 战技（含项目里被做成战技的原神爆发技，语义由调用方决定）
        Burst = 2,    // 元素爆发
        HitLight = 3, // 受轻击
        HitHeavy = 4, // 受重击
        Death = 5,    // 倒下
    }

    /// <summary>
    /// 战斗语音参数（BattlePlayer Inspector「战斗语音」段）。
    /// 设计参考：Wwise/FMOD 的 Event Limiting（Max Instance / Priority / Cooldown）
    /// + RTS 的批量应答合并 + 「同屏只强调玩家相关单位」的行业惯例。
    /// </summary>
    [Serializable]
    public class BattleVoiceSettings
    {
        [Header("总开关")]
        [Tooltip("关=战斗内完全不播角色语音（UI 语音不受影响）")]
        public bool 启用 = true;

        [Header("通道节流（秒）")]
        [Tooltip("主语音（战技/爆发/倒下/部署）两次播放的最小间隔——即时制多单位同时施法时，这是压制\"太吵\"的第一道闸")]
        public float 主语音最小间隔 = 1.1f;

        [Tooltip("子语音（受击）两次播放的最小间隔——受击是最高频事件，间隔必须明显大于主语音")]
        public float 子语音最小间隔 = 0.55f;

        [Header("同窗合并（秒）")]
        [Tooltip("同一时间窗内的多个候选只播分数最高的一个（RTS 框选多单位只应答一次的通行做法）；0=不合并")]
        public float 合并窗口 = 0.12f;

        [Header("每单位同类冷却（秒）")]
        [Tooltip("同一单位连续受击/施法不重复喊，避免单个单位刷屏")]
        public float 战技冷却 = 2f;
        public float 爆发冷却 = 3f;
        public float 受击冷却 = 4f;
        public float 部署冷却 = 3f;

        [Header("主体权重（决定同窗内谁抢到通道）")]
        [Tooltip("本机玩家操控的单位")]
        public float 本机权重 = 1f;
        [Tooltip("同队队友/AI 单位")]
        public float 队友权重 = 0.5f;
        [Tooltip("敌方单位")]
        public float 敌方权重 = 0.15f;

        [Tooltip("敌方只播爆发/倒下：即时制里敌方受击/普攻喊声累积最吵，默认只保留有信息量的关键语音")]
        public bool 敌方只播关键语音 = true;

        [Header("距离衰减（格）")]
        [Tooltip("离相机小于该距离=满权重")]
        public float 近距离 = 6f;
        [Tooltip("离相机大于该距离=只剩下限倍率")]
        public float 远距离 = 20f;
        [Range(0.01f, 1f)]
        [Tooltip("远距离时的分数下限倍率（不给 0：远处爆发仍可能抢到通道）")]
        public float 远距离保留 = 0.15f;

        [Header("抢占")]
        [Range(0f, 1f)]
        [Tooltip("新语音优先级更高时，在播语音至少已播过该比例才允许打断（避免句句被切碎）")]
        public float 抢占最小已播比例 = 0.4f;

        [Header("音高微扰")]
        [Range(0f, 0.2f)]
        [Tooltip("同一条语音反复播放的音高随机幅度，减少机械感")]
        public float 音高随机 = 0.04f;

        [Header("音量")]
        [Range(0f, 2f)]
        public float 音量 = 1f;
    }

    /// <summary>
    /// 战斗语音调度器（纯客户端表现层，不进模拟/命令流/回放）。
    /// 职责：把「某单位发生了某事」翻译成「此刻该不该播、播谁的一句」。
    /// 三道闸：通道节流 → 同窗合并 → 优先级抢占；另有主体/距离权重与每单位冷却。
    /// </summary>
    public class BattleVoiceDirector
    {
        private class Candidate
        {
            public string UnitId;
            public UnitName Unit;
            public BattleVoiceKind Kind;
            public SkillName Skill;
            public AudioClipRandom Pool;
            public float Priority;
            public float Score;
        }

        private class ChannelState
        {
            public float NextFreeTime;
            public float PlayingUntil;
            public float StartTime;
            public float CurrentPriority = -1f;
        }

        private readonly BattleVoiceSettings _cfg;
        private readonly UnitConfig _config;
        private readonly Func<string, UnitName?> _nameOf;
        private readonly Func<string, Vector3?> _positionOf;
        private readonly Func<string, int> _relationOf;   // 0=本机 1=队友 2=敌方
        private readonly Func<Camera> _cameraOf;

        private readonly ChannelState _main = new ChannelState();
        private readonly ChannelState _sub = new ChannelState();

        private readonly Dictionary<string, float> _cooldown = new Dictionary<string, float>();
        private readonly List<string> _cooldownGc = new List<string>();

        private Candidate _pendingMain;
        private Candidate _pendingSub;
        private float _pendingUntil;
        private bool _pendingActive;

        public BattleVoiceDirector(BattleVoiceSettings cfg, UnitConfig config,
            Func<string, UnitName?> nameOf, Func<string, Vector3?> positionOf,
            Func<string, int> relationOf, Func<Camera> cameraOf)
        {
            _cfg = cfg ?? new BattleVoiceSettings();
            _config = config;
            _nameOf = nameOf;
            _positionOf = positionOf;
            _relationOf = relationOf;
            _cameraOf = cameraOf;
        }

        /// <summary>战斗结束/重开时清空残留状态（不播的队列与冷却不跨局）</summary>
        public void Reset()
        {
            _cooldown.Clear();
            _pendingMain = null;
            _pendingSub = null;
            _pendingActive = false;
            _main.NextFreeTime = _main.PlayingUntil = 0f;
            _sub.NextFreeTime = _sub.PlayingUntil = 0f;
            _main.CurrentPriority = _sub.CurrentPriority = -1f;
        }

        /// <summary>请求播放（命令消费点调用，任意频率安全——内部节流）</summary>
        public void Request(string unitId, BattleVoiceKind kind, SkillName skill = SkillName.None)
        {
            if (!_cfg.启用 || string.IsNullOrEmpty(unitId)) return;

            var unitName = _nameOf?.Invoke(unitId);
            if (unitName == null || _config == null) return;
            if (!_config.TryGetUnitData(unitName.Value, out var data) || data.voices == null) return;

            int relation = _relationOf?.Invoke(unitId) ?? 0;
            // 敌方降噪：即时制里敌方受击喊声最容易被听成噪声，默认只留关键语音
            if (relation == 2 && _cfg.敌方只播关键语音 &&
                kind != BattleVoiceKind.Burst && kind != BattleVoiceKind.Death) return;

            var pool = ResolvePool(data, kind, skill);
            if (pool == null || pool.Count == 0) return;

            float now = Time.unscaledTime;
            string cdKey = unitId + "|" + (int)kind;
            if (_cooldown.TryGetValue(cdKey, out float readyAt) && now < readyAt) return;

            float priority = PriorityOf(kind);
            float relationWeight = relation == 0 ? _cfg.本机权重 : relation == 1 ? _cfg.队友权重 : _cfg.敌方权重;
            float score = priority * Mathf.Max(0.01f, relationWeight) * DistanceWeight(unitId);

            var cand = new Candidate
            {
                UnitId = unitId, Unit = unitName.Value, Kind = kind, Skill = skill,
                Pool = pool, Priority = priority, Score = score,
            };

            if (IsMainChannel(kind)) KeepBest(ref _pendingMain, cand, now);
            else KeepBest(ref _pendingSub, cand, now);
        }

        /// <summary>每帧驱动（BattlePlayer.Update）：合并窗口到期后按分数决定谁播</summary>
        public void Tick()
        {
            if (!_pendingActive) return;
            float now = Time.unscaledTime;
            if (now < _pendingUntil) return;

            _pendingActive = false;
            var main = _pendingMain;
            var sub = _pendingSub;
            _pendingMain = null;
            _pendingSub = null;

            if (main != null) TryPlay(main, _main, now);
            if (sub != null) TryPlay(sub, _sub, now);
        }

        private void KeepBest(ref Candidate slot, Candidate cand, float now)
        {
            if (!_pendingActive)
            {
                _pendingActive = true;
                _pendingUntil = now + Mathf.Max(0f, _cfg.合并窗口);
            }
            if (slot == null || cand.Score > slot.Score) slot = cand;
        }

        private void TryPlay(Candidate c, ChannelState ch, float now)
        {
            if (now < ch.NextFreeTime) return;

            bool occupied = ch.PlayingUntil > now;
            if (occupied)
            {
                if (ch.CurrentPriority > c.Priority) return;                     // 在播的更关键：丢弃
                float played = ch.StartTime > 0f ? now - ch.StartTime : 1f;
                float need = _cfg.抢占最小已播比例 * Mathf.Max(0.05f, ch.PlayingUntil - ch.StartTime);
                if (c.Priority <= ch.CurrentPriority && played < need) return;   // 同级且没播够：等它说完
                // 更高优先级：允许提前打断（避免「寒冰之棱」这类爆发被连续的受击喊声挡住）
            }

            var clip = c.Pool.GetRandomClip();
            if (clip == null) return;

            var audio = AudioManager.Instance;
            if (audio == null) return;

            float pitch = 1f;
            if (_cfg.音高随机 > 0.0001f)
                pitch = 1f + UnityEngine.Random.Range(-_cfg.音高随机, _cfg.音高随机);
            audio.PlayVoiceOneShot(clip, _cfg.音量, pitch);

            ch.CurrentPriority = c.Priority;
            ch.StartTime = now;
            ch.PlayingUntil = now + Mathf.Max(0.1f, clip.length);
            ch.NextFreeTime = now + (ReferenceEquals(ch, _main) ? _cfg.主语音最小间隔 : _cfg.子语音最小间隔);

            _cooldown[c.UnitId + "|" + (int)c.Kind] = now + CooldownOf(c.Kind);
            SweepCooldown(now);
        }

        private void SweepCooldown(float now)
        {
            if (_cooldown.Count < 256) return;
            _cooldownGc.Clear();
            foreach (var kv in _cooldown)
                if (kv.Value < now) _cooldownGc.Add(kv.Key);
            foreach (var k in _cooldownGc) _cooldown.Remove(k);
        }

        private float DistanceWeight(string unitId)
        {
            var cam = _cameraOf?.Invoke();
            var pos = _positionOf?.Invoke(unitId);
            if (cam == null || pos == null) return 1f;

            float d = Vector3.Distance(cam.transform.position, pos.Value);
            if (d <= _cfg.近距离) return 1f;
            if (d >= _cfg.远距离) return Mathf.Clamp(_cfg.远距离保留, 0.01f, 1f);
            float t = (d - _cfg.近距离) / Mathf.Max(0.01f, _cfg.远距离 - _cfg.近距离);
            return Mathf.Lerp(1f, Mathf.Clamp(_cfg.远距离保留, 0.01f, 1f), t);
        }

        private static bool IsMainChannel(BattleVoiceKind kind)
            => kind == BattleVoiceKind.Burst || kind == BattleVoiceKind.Skill
               || kind == BattleVoiceKind.Death || kind == BattleVoiceKind.Deploy;

        private static float PriorityOf(BattleVoiceKind kind)
        {
            switch (kind)
            {
                case BattleVoiceKind.Burst: return 100f;
                case BattleVoiceKind.Death: return 85f;
                case BattleVoiceKind.Skill: return 70f;
                case BattleVoiceKind.Deploy: return 55f;
                case BattleVoiceKind.HitHeavy: return 40f;
                default: return 28f; // HitLight
            }
        }

        private float CooldownOf(BattleVoiceKind kind)
        {
            switch (kind)
            {
                case BattleVoiceKind.Skill: return _cfg.战技冷却;
                case BattleVoiceKind.Burst: return _cfg.爆发冷却;
                case BattleVoiceKind.Deploy: return _cfg.部署冷却;
                case BattleVoiceKind.Death: return 0f;
                default: return _cfg.受击冷却;
            }
        }

        private AudioClipRandom ResolvePool(UnitConfig.UnitData data, BattleVoiceKind kind, SkillName skill)
        {
            switch (kind)
            {
                case BattleVoiceKind.Skill:
                case BattleVoiceKind.Burst:
                    return skill != SkillName.None ? data.voices.GetSkillVoices(skill) : null;
                case BattleVoiceKind.HitLight: return data.voices.onHitLight;
                case BattleVoiceKind.HitHeavy: return data.voices.onHitHeavy;
                case BattleVoiceKind.Death: return data.voices.onDie;
                case BattleVoiceKind.Deploy: return data.voices.onGoWar;
                default: return null;
            }
        }
    }
}
