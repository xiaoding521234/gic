using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public partial class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Mixer")]
        [SerializeField] private AudioMixer audioMixer;

        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource voiceSource;

        [Header("Volume Settings")]
        [Range(0f, 1f)]
        private float masterVolume = 1f;
        [Range(0f, 1f)]
        private float musicVolume = 1f;
        [Range(0f, 1f)]
        private float sfxVolume = 1f;
        [Range(0f, 1f)]
        private float voiceVolume = 1f;

        [Header("Music Reduction Settings")]
        private float reducedMusicVolumeScale = 0.5f;

        // Mixer参数名
        private const string MASTER_VOLUME_PARAM = "MasterVolume";
        private const string MUSIC_VOLUME_PARAM = "MusicVolume";
        private const string SFX_VOLUME_PARAM = "SFXVolume";
        private const string VOICE_VOLUME_PARAM = "VoiceVolume";

        // 音乐播放相关
        private Coroutine musicFadeCoroutine;
        private Coroutine musicLoopCoroutine;
        private Coroutine musicVolumeTransitionCoroutine;
        private MusicTrack currentMusicTrack;
        private bool isMusicPaused = false;

        // 间隔冷却等待（intervalBefore/intervalAfter）的结束时间戳（Time.time，与 Wait.Seconds 同为缩放时间）
        // -1 = 无活跃间隔等待；生命周期协程进入等待时写入，等待结束或 StopCurrentMusic 时清除
        private float intervalEndTime = -1f;

        // 音乐状态保存栈（用于临时切换音乐后恢复）
        private Stack<MusicState> musicStateStack = new Stack<MusicState>();

        // 音乐降低管理
        private int musicVolumeReductionCount = 0;

        // 音效源池
        private List<AudioSource> sfxSourcePool = new List<AudioSource>();
        private const int SFX_POOL_SIZE = 10;

        // 语音源池
        private List<AudioSource> voiceSourcePool = new List<AudioSource>();
        private const int VOICE_POOL_SIZE = 8;

        // 追踪每个角色正在使用的语音源
        private Dictionary<UnitName, AudioSource> unitVoiceMap = new Dictionary<UnitName, AudioSource>();

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeAudioManager();
                
                // AudioManager 持有全局唯一的 AudioListener（DontDestroyOnLoad 永不消失）
                if (GetComponent<AudioListener>() == null)
                    gameObject.AddComponent<AudioListener>();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // 禁用场景里摄像机上的 AudioListener，避免和 AudioManager 上的重复
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (var l in listeners)
            {
                if (l.transform != transform)
                    l.enabled = false;
            }
        }

        void Start()
        {
            LoadVolumeSettings();
        }

        void Update()
        {
            CleanupUnusedSFXSources();
            CleanupFinishedVoices();
        }

        private void InitializeAudioManager()
        {
            if (audioMixer == null)
            {
                GICLog.Warn("AudioMixer未指定，请在Inspector中指定AudioMixer");
            }

            SetAudioSource2D(musicSource);
            SetAudioSource2D(sfxSource);
            SetAudioSource2D(voiceSource);

            if (audioMixer != null && voiceSource != null)
            {
                var groups = audioMixer.FindMatchingGroups("Voice");
                if (groups.Length > 0)
                    voiceSource.outputAudioMixerGroup = groups[0];
            }

            for (int i = 0; i < SFX_POOL_SIZE; i++)
            {
                CreatePooledSFXSource();
            }

            for (int i = 0; i < VOICE_POOL_SIZE; i++)
            {
                CreatePooledVoiceSource();
            }

            ApplyAllVolumes();
        }

        private void SetAudioSource2D(AudioSource source)
        {
            if (source != null)
            {
                source.spatialBlend = 0f;
                source.spread = 0f;
                source.dopplerLevel = 0f;
            }
        }

        private void ApplyVolume(string paramName, float volume)
        {
            if (audioMixer != null)
            {
                float dB = volume > 0.0001f ? Mathf.Log10(volume) * 20f : -80f;
                audioMixer.SetFloat(paramName, dB);
            }
        }

        private void ApplyAllVolumes()
        {
            ApplyVolume(MASTER_VOLUME_PARAM, masterVolume);
            ApplyVolume(MUSIC_VOLUME_PARAM, musicVolume);
            ApplyVolume(SFX_VOLUME_PARAM, sfxVolume);
            ApplyVolume(VOICE_VOLUME_PARAM, voiceVolume);
        }

        public void SetAllMute(bool mute)
        {
            if (audioMixer != null)
            {
                float dB = mute ? -80f : 0f;
                audioMixer.SetFloat(MASTER_VOLUME_PARAM, dB);
            }
        }
    }
}


