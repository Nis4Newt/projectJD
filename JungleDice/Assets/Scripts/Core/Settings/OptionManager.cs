using JungleDice.Core.Audio;
using UnityEngine.UI;

namespace JungleDice.Core.Settings
{
    public static class OptionManager
    {
        public static void BindVolumeSliders(Slider bgmSlider, Slider sfxSlider)
        {
            bgmSlider.onValueChanged.AddListener(v => AudioSystem.Instance.SetVolume(AudioChannel.BGM, v));
            sfxSlider.onValueChanged.AddListener(v => AudioSystem.Instance.SetVolume(AudioChannel.SFX, v));
        }

        public static void SyncVolumeSliders(Slider bgmSlider, Slider sfxSlider)
        {
            bgmSlider.SetValueWithoutNotify(SettingsSystem.Instance.GetVolume(AudioChannel.BGM));
            sfxSlider.SetValueWithoutNotify(SettingsSystem.Instance.GetVolume(AudioChannel.SFX));
        }

        public static void CommitVolumeSliders(Slider bgmSlider, Slider sfxSlider)
        {
            SettingsSystem.Instance.SetVolume(AudioChannel.BGM, bgmSlider.value);
            SettingsSystem.Instance.SetVolume(AudioChannel.SFX, sfxSlider.value);
        }

        public static void BindVibrationToggle(Toggle vibrationToggle)
        {
            vibrationToggle.onValueChanged.AddListener(v => SettingsSystem.Instance.SetVibration(v));
        }

        public static void SyncVibrationToggle(Toggle vibrationToggle)
        {
            vibrationToggle.SetIsOnWithoutNotify(SettingsSystem.Instance.Vibration);
        }
    }
}
