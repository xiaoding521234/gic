using System;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public partial class AudioManager
    {
        /// <summary>
        /// 音乐轨道数据结构
        /// </summary>
        public struct MusicTrack
        {
            public AudioClip clip;
            public MusicType musicType;
            public float intervalBefore;
            public float intervalAfter;
            public bool loop;
            public float fadeInTime;          public Action onComplete; // 播放完毕回调（仅loop=false时触发）

            public MusicTrack(
                AudioClip clip,
                MusicType musicType = MusicType.Relaxed,
                float intervalBefore = 0f,
                float intervalAfter = 0f,
                bool loop = true,
                float fadeInTime = 0f,
                Action onComplete = null)
            {
                this.clip = clip;
                this.musicType = musicType;
                this.intervalBefore = intervalBefore;
                this.intervalAfter = intervalAfter;
                this.loop = loop;
                this.fadeInTime = fadeInTime;
                this.onComplete = onComplete;
            }
        }
    }  
}

