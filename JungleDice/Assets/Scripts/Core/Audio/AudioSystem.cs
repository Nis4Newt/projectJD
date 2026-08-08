using System.Collections.Generic;
using DG.Tweening;
using JungleDice.Core.Event;
using UnityEngine;
using UnityEngine.Audio;

namespace JungleDice.Core.Audio
{
    public class AudioSystem : Singleton<AudioSystem>
    {
        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioMixerGroup _bgmMixerGroup;
        [SerializeField] private AudioMixerGroup _sfxMixerGroup;
        [SerializeField] private int _sfxPoolSize = 8;
        [SerializeField] private float _defaultBgmFadeDuration = 1f;

        private const string BgmFolder = "BGM";
        private const string SfxFolder = "SFX";
        private const float MuteDb = -80f;

        private AudioSource _bgmSourceA;
        private AudioSource _bgmSourceB;
        private AudioSource _activeBgmSource;
        private AudioSource _inactiveBgmSource;
        private AudioID? _currentBgmId;
        private Tween _bgmFadeOutTween;
        private Tween _bgmFadeInTween;

        private AudioSource[] _sfxPool;

        private readonly Dictionary<AudioID, AudioClip> _clipCache = new();

        private float _masterVolume01 = 1f;
        private float _bgmVolume01 = 1f;
        private float _sfxVolume01 = 1f;

        private bool _isPaused;
        private bool _hasFocus = true;

        protected override void OnAwake()
        {
            _bgmSourceA = CreateBgmSource("BgmSourceA");
            _bgmSourceB = CreateBgmSource("BgmSourceB");
            _activeBgmSource = _bgmSourceA;
            _inactiveBgmSource = _bgmSourceB;

            BuildSfxPool();

            EventBus.Subscribe<AppPauseChanged>(OnAppPauseChanged);
            EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged);

            // 코드 기본값(1f)을 믹서에도 명시적으로 반영 — 믹서 애셋 자체의 저장된 값에 의존하지 않음
            SetVolume(AudioChannel.Master, _masterVolume01);
            SetVolume(AudioChannel.BGM, _bgmVolume01);
            SetVolume(AudioChannel.SFX, _sfxVolume01);
        }

        private AudioSource CreateBgmSource(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform);
            source.outputAudioMixerGroup = _bgmMixerGroup;
            source.loop = true;
            source.playOnAwake = false;
            return source;
        }

        public void PlayBGM(AudioID id, float fadeIn = -1f)
        {
            if (_currentBgmId == id) return; // 이미 재생 중인 트랙 재요청은 무시

            var clip = GetClip(id, BgmFolder);
            if (clip == null) return; // GetClip이 이미 경고 로그를 남김

            _currentBgmId = id;
            float duration = fadeIn < 0f ? _defaultBgmFadeDuration : fadeIn;

            _bgmFadeInTween?.Kill();
            _bgmFadeOutTween?.Kill();

            var incoming = _inactiveBgmSource;
            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();
            _bgmFadeInTween = incoming.DOFade(1f, duration);

            var outgoing = _activeBgmSource;
            _bgmFadeOutTween = outgoing.DOFade(0f, duration).OnComplete(outgoing.Stop);

            (_activeBgmSource, _inactiveBgmSource) = (_inactiveBgmSource, _activeBgmSource);
        }

        public void StopBGM(float fadeOut = -1f)
        {
            if (_currentBgmId == null) return;
            _currentBgmId = null;

            float duration = fadeOut < 0f ? _defaultBgmFadeDuration : fadeOut;
            _bgmFadeInTween?.Kill();
            _bgmFadeOutTween?.Kill();

            var source = _activeBgmSource;
            _bgmFadeOutTween = source.DOFade(0f, duration).OnComplete(source.Stop);
        }

        private void BuildSfxPool()
        {
            _sfxPool = new AudioSource[_sfxPoolSize];
            for (int i = 0; i < _sfxPoolSize; i++)
            {
                var source = new GameObject($"SfxSource{i}").AddComponent<AudioSource>();
                source.transform.SetParent(transform);
                source.outputAudioMixerGroup = _sfxMixerGroup;
                source.playOnAwake = false;
                _sfxPool[i] = source;
            }
        }

        public void PlaySFX(AudioID id)
        {
            var source = GetIdleSfxSource();
            if (source == null) return; // 풀 전부 사용 중 — 동시 재생 제한, 조용히 드롭

            var clip = GetClip(id, SfxFolder);
            if (clip == null) return;

            source.clip = clip;
            source.Play();
        }

        private AudioSource GetIdleSfxSource()
        {
            foreach (var source in _sfxPool)
                if (!source.isPlaying) return source;
            return null;
        }

        public void SetVolume(AudioChannel channel, float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            SetChannelField(channel, linear01);
            _mixer.SetFloat(ChannelParam(channel), LinearToDb(linear01));
        }

        public float GetVolume(AudioChannel channel) => channel switch
        {
            AudioChannel.Master => _masterVolume01,
            AudioChannel.BGM => _bgmVolume01,
            AudioChannel.SFX => _sfxVolume01,
            _ => 1f,
        };

        private void SetChannelField(AudioChannel channel, float linear01)
        {
            switch (channel)
            {
                case AudioChannel.Master: _masterVolume01 = linear01; break;
                case AudioChannel.BGM: _bgmVolume01 = linear01; break;
                case AudioChannel.SFX: _sfxVolume01 = linear01; break;
            }
        }

        private static string ChannelParam(AudioChannel channel) => channel switch
        {
            AudioChannel.Master => "MasterVolume",
            AudioChannel.BGM => "BGMVolume",
            AudioChannel.SFX => "SFXVolume",
            _ => "MasterVolume",
        };

        private static float LinearToDb(float linear01) =>
            linear01 <= 0.0001f ? MuteDb : Mathf.Log10(linear01) * 20f;

        private AudioClip GetClip(AudioID id, string folder)
        {
            if (_clipCache.TryGetValue(id, out var cached)) return cached;

            var clip = Resources.Load<AudioClip>($"Audio/{folder}/{id}");
            if (clip == null)
                Debug.LogWarning($"[AudioSystem] AudioClip not found: Audio/{folder}/{id}");

            _clipCache[id] = clip; // null도 캐시 — 같은 이름을 반복 요청해도 매번 Resources.Load를 다시 타지 않음
            return clip;
        }

        private void OnAppPauseChanged(AppPauseChanged e)
        {
            _isPaused = e.IsPaused;
            ApplyMuteState();
        }

        private void OnAppFocusChanged(AppFocusChanged e)
        {
            _hasFocus = e.HasFocus;
            ApplyMuteState();
        }

        private void ApplyMuteState()
        {
            bool shouldMute = _isPaused || !_hasFocus;
            _mixer.SetFloat(ChannelParam(AudioChannel.Master), shouldMute ? MuteDb : LinearToDb(_masterVolume01));
        }
    }
}
