using System;
using System.IO;
using JungleDice.Core.Audio;
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.Settings
{
    public class SettingsSystem : Singleton<SettingsSystem>
    {
        private static string SettingsFilePath;

        private SettingsData _data;

        protected override void OnAwake()
        {
            SettingsFilePath = Path.Combine(Application.persistentDataPath, "save", "settings.json");
            _data = LoadFromDisk();
        }

        public void ApplyLoadedVolumes()
        {
            AudioSystem.Instance.SetVolume(AudioChannel.Master, _data.MasterVolume);
            AudioSystem.Instance.SetVolume(AudioChannel.BGM, _data.BgmVolume);
            AudioSystem.Instance.SetVolume(AudioChannel.SFX, _data.SfxVolume);
        }

        public void SetVolume(AudioChannel channel, float linear01)
        {
            AudioSystem.Instance.SetVolume(channel, linear01);
            Save();
            EventBus.Publish(new SettingsChanged());
        }

        public float GetVolume(AudioChannel channel) => AudioSystem.Instance.GetVolume(channel);

        public bool Vibration => _data.Vibration;

        public void SetVibration(bool enabled)
        {
            _data.Vibration = enabled;
            Save();
            EventBus.Publish(new SettingsChanged());
        }

        public SystemLanguage Language => _data.Language;

        public void SetLanguage(SystemLanguage language)
        {
            _data.Language = language;
            Save();
            EventBus.Publish(new SettingsChanged());
        }

        private SettingsData LoadFromDisk()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var data = JsonUtility.FromJson<SettingsData>(File.ReadAllText(SettingsFilePath));
                    if (data != null) return data;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsSystem] 설정 파일 로드 실패, 기본값 사용: {e.Message}");
            }

            return new SettingsData { Language = Application.systemLanguage };
        }

        private void Save()
        {
            _data.MasterVolume = AudioSystem.Instance.GetVolume(AudioChannel.Master);
            _data.BgmVolume = AudioSystem.Instance.GetVolume(AudioChannel.BGM);
            _data.SfxVolume = AudioSystem.Instance.GetVolume(AudioChannel.SFX);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                File.WriteAllText(SettingsFilePath, JsonUtility.ToJson(_data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsSystem] 설정 파일 저장 실패: {e.Message}");
            }
        }
    }
}
