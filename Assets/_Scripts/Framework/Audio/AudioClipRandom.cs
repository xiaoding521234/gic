using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 音频片段随机选择器
    /// 支持权重，不会连续两次返回相同的Clip（除非只有一个Clip）
    /// </summary>
    [Serializable]
    public class AudioClipRandom
    {
        [Serializable]
        public class WeightedClip
        {
            [SerializeField] private AudioClip clip;
            [SerializeField] private float weight = 1f;

            public AudioClip Clip => clip;
            public float Weight => Mathf.Max(0f, weight); // 权重不能为负

            public WeightedClip(AudioClip clip, float weight = 1f)
            {
                this.clip = clip;
                this.weight = Mathf.Max(0f, weight);
            }
        }

        [SerializeField] private List<WeightedClip> clips = new List<WeightedClip>();

        private AudioClip lastClip;

        /// <summary>
        /// 添加一个音频片段
        /// </summary>
        public void AddClip(AudioClip clip, float weight = 1f)
        {
            clips.Add(new WeightedClip(clip, weight));
        }

        /// <summary>
        /// 批量添加音频片段
        /// </summary>
        public void AddClips(IEnumerable<AudioClip> clips, float weight = 1f)
        {
            foreach (var clip in clips)
            {
                this.clips.Add(new WeightedClip(clip, weight));
            }
        }

        /// <summary>
        /// 移除音频片段
        /// </summary>
        public bool RemoveClip(AudioClip clip)
        {
            int index = clips.FindIndex(c => c.Clip == clip);
            if (index >= 0)
            {
                clips.RemoveAt(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清空所有音频片段
        /// </summary>
        public void Clear()
        {
            clips.Clear();
            lastClip = null;
        }

        /// <summary>
        /// 获取所有片段数量
        /// </summary>
        public int Count => clips.Count;

        /// <summary>
        /// 随机获取一个音频片段
        /// 不会连续两次返回相同的Clip（除非只有一个Clip）
        /// </summary>
        public AudioClip GetRandomClip()
        {
            if (clips.Count == 0)
            {
                GICLog.Warn("AudioClipRandomSelector: 没有可用的音频片段");
                return null;
            }

            // 如果只有一个Clip，直接返回
            if (clips.Count == 1)
            {
                lastClip = clips[0].Clip;
                return lastClip;
            }

            // 计算可用Clip的权重（排除上次使用的）
            List<WeightedClip> availableClips = new List<WeightedClip>();
            float totalWeight = 0f;

            foreach (var weightedClip in clips)
            {
                // 跳过上次使用的Clip（除非没有其他选择）
                if (weightedClip.Clip == lastClip && clips.Count > 1)
                {
                    continue;
                }

                availableClips.Add(weightedClip);
                totalWeight += weightedClip.Weight;
            }

            // 如果过滤后没有可用Clip（理论上不会发生），回退到包含所有Clip
            if (availableClips.Count == 0)
            {
                availableClips = new List<WeightedClip>(clips);
                totalWeight = 0f;
                foreach (var wc in availableClips)
                {
                    totalWeight += wc.Weight;
                }
            }

            // 根据权重随机选择
            float randomValue = Random.Range(0f, totalWeight);
            float cumulativeWeight = 0f;

            foreach (var weightedClip in availableClips)
            {
                cumulativeWeight += weightedClip.Weight;
                if (randomValue <= cumulativeWeight)
                {
                    lastClip = weightedClip.Clip;
                    return lastClip;
                }
            }

            // 兜底：返回最后一个
            lastClip = availableClips[availableClips.Count - 1].Clip;
            return lastClip;
        }

        /// <summary>
        /// 重置"上次播放"记录（允许下一次出现与上次相同的Clip）
        /// </summary>
        public void ResetLastClip()
        {
            lastClip = null;
        }

        /// <summary>
        /// 获取所有Clip（只读）
        /// </summary>
        public List<AudioClip> GetAllClips()
        {
            List<AudioClip> result = new List<AudioClip>();
            foreach (var weightedClip in clips)
            {
                result.Add(weightedClip.Clip);
            }
            return result;
        }

        /// <summary>
        /// 获取指定索引的权重
        /// </summary>
        public float GetWeight(int index)
        {
            if (index >= 0 && index < clips.Count)
            {
                return clips[index].Weight;
            }
            return 0f;
        }

        /// <summary>
        /// 设置指定索引的权重
        /// </summary>
        public void SetWeight(int index, float weight)
        {
            if (index >= 0 && index < clips.Count)
            {
                clips[index] = new WeightedClip(clips[index].Clip, Mathf.Max(0f, weight));
            }
        }
    }
}

