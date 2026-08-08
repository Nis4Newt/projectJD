using System;
using UnityEngine;

namespace JungleDice.Core.Settings
{
    [Serializable]
    public class SettingsData
    {
        public float MasterVolume = 1f;
        public float BgmVolume = 1f;
        public float SfxVolume = 1f;
        public bool Vibration = true;
        public SystemLanguage Language = SystemLanguage.Unknown;
    }
}
