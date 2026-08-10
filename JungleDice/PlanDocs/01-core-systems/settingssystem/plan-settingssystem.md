# SettingsSystem 구현 계획

> 상위 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md) (시스템 목록 #9)
> Phase 2(데이터 계층) 항목, `AudioSystem` 이후 순서 — `SaveSystem`은 아직 미구현(`Assets/Scripts/Core/`에 `Save/` 폴더 없음)이므로 로드맵이 명시한 "SaveSystem 사용" 저장 방식은 `SaveSystem` 도입 전까지 `SettingsSystem`이 직접 JSON 파일 I/O로 대체 구현한다(아래 "핵심 설계 결정 2" 참고). `AudioSystem`은 이미 구현되어 있으며 `SetVolume`/`GetVolume`을 소비자에게 열어 두고 저장은 신경 쓰지 않는다고 명시해 두었다([plan-audiosystem.md](../audiosystem/plan-audiosystem.md) "이번 범위에서 제외") — `SettingsSystem`이 그 소비자가 된다.
> 의존 관계: `JungleDice.Core.Singleton<T>`(영속 싱글턴 베이스), `JungleDice.Core.Audio.AudioSystem`(볼륨 forwarding 대상, `GameManagers` 오브젝트에 이미 부착됨), `JungleDice.Core.Event.EventBus`(`SettingsChanged` 발행, 새 이벤트 1개 추가)
> 범위: BGM/SFX/Master 볼륨 설정 저장·복원(`AudioSystem` forwarding), 진동 On/Off 저장, 언어 선택값 저장, 앱 시작 시 저장된 설정 자동 로드/적용. 실제 설정 UI(팝업 화면), 진동을 실제로 발생시키는 호출부, 언어 변경을 실제 텍스트에 반영하는 처리(`LocalizationSystem`)는 범위 밖.

---

## 배경 / 문제 인식

`plan-core-systems.md`가 스케치한 데이터 구조는 다음과 같다:

```csharp
public class SettingsData
{
    public float MasterVolume;
    public float BgmVolume;
    public float SfxVolume;
    public bool  Vibration;
    public string Language;
}
```

로드맵은 `SettingsSystem`의 책임을 "변경 즉시 적용 + 자동 저장(`SaveSystem` 사용)", "앱 시작 시 저장된 설정 자동 로드/적용"으로 명시한다. `AudioSystem` 문서는 `SaveSystem`/`SettingsSystem` 둘 다 미구현 상태에서도 "그 존재를 몰라도 완결"되도록 스스로를 설계했지만, `SettingsSystem`은 같은 회피가 불가능하다 — 이 시스템이 존재하는 이유 자체가 "저장"이기 때문에, 저장을 범위 밖으로 미루면 남는 것은 `AudioSystem.SetVolume`을 대신 호출해 주는 얇은 래퍼뿐이라 문서의 존재 의미가 없어진다.

그래서 이 문서는 `SaveSystem`의 부재를 다음과 같이 다룬다: `SaveSystem`이 도입되기 전까지 `SettingsSystem`이 직접 파일 I/O를 수행하되, 저장 위치와 포맷을 로드맵의 `SaveSystem` 절이 이미 명시해 둔 경로(`Application.persistentDataPath/save/settings.json`)에 맞춘다. 이렇게 하면 나중에 `SaveSystem`이 실제로 구현될 때 `SettingsSystem`의 파일 I/O 두 메서드(`LoadFromDisk`/`Save`)만 `SaveSystem.Load<T>`/`Save<T>` 호출로 바꾸면 되고, 이미 저장돼 있던 `settings.json`을 마이그레이션 없이 그대로 읽어들일 수 있다.

`GameManager.cs`는 이미 이 연결 지점을 코드에 남겨 두었다:

```csharp
// GameManager.LogoSequence()
yield return null; // 코어 시스템이 Awake 완료될 때까지 1프레임 대기

// SaveSystem에서 설정 로드
// (SaveSystem 구현 후 연결)
```

이 문서는 이 자리를 `SettingsSystem.Instance.ApplyLoadedVolumes()` 호출로 채운다(아래 "핵심 설계 결정 5"). `plan-gamemanager.md`가 별도로 제안했던 Script Execution Order 방식(-100/-90/-80… 순서 지정)은 실제 구현에서 채택되지 않았다 — `ProjectSettings.asset`에 `scriptExecutionOrder` 설정이 없고, `GameManager`/`AudioSystem` 모두 "1프레임 대기 코루틴"만으로 초기화 순서를 해결하고 있다. 이 문서도 그 실제 관례를 따른다.

---

## 설계 목표

- 로드맵의 `SettingsData` 필드(`MasterVolume`/`BgmVolume`/`SfxVolume`/`Vibration`/`Language`)를 그대로 반영
- 볼륨은 `AudioSystem`이 실행 중 유일한 진실 소스 — `SettingsSystem`은 값을 중복 보관하지 않고 순수 forwarding + 영속화 레이어로만 동작(상태 이원화로 인한 드리프트 방지)
- 진동/언어는 다른 시스템이 없으므로 `SettingsSystem`이 유일한 소유자
- 앱 시작 시 저장된 값을 자동 복원하되, `AudioSystem.Instance`가 아직 없을 수 있는 `Awake` 시점을 피해 `GameManager.LogoSequence`의 1프레임 대기 이후 지점에서만 적용
- `SaveSystem` 도입 전까지도 실제로 동작하는 영속성을 제공한다 — 빈 껍데기 API가 아니라 앱을 껐다 켜도 값이 남는 기능이어야 함
- 저장 경로/포맷은 `SaveSystem` 로드맵이 이미 정해 둔 위치를 선점 사용 — 이후 `SaveSystem`으로 교체할 때 마이그레이션 불필요
- 값 변경 시 `EventBus`로 `SettingsChanged`를 발행 — 향후 설정 UI/`LocalizationSystem`이 폴링 없이 반응 가능(`UserData`가 모든 setter에서 `UserDataChanged`를 발행하는 관례를 재사용)

---

## 핵심 설계 결정

### 1. `Singleton<SettingsSystem>` 상속, `OnAwake`는 파일 로드만 — `AudioSystem` 적용은 별도 공개 메서드로 분리

```csharp
public class SettingsSystem : Singleton<SettingsSystem>
{
    private SettingsData _data;

    protected override void OnAwake()
    {
        _data = LoadFromDisk();
    }

    // GameManager.LogoSequence가 1프레임 대기 이후 호출 — AudioSystem.Instance가 이미 존재함을 보장
    public void ApplyLoadedVolumes()
    {
        AudioSystem.Instance.SetVolume(AudioChannel.Master, _data.MasterVolume);
        AudioSystem.Instance.SetVolume(AudioChannel.BGM, _data.BgmVolume);
        AudioSystem.Instance.SetVolume(AudioChannel.SFX, _data.SfxVolume);
    }
}
```

`OnAwake`(다른 싱글턴들처럼 `Awake` 프레임에 실행됨)에서는 `AudioSystem.Instance`를 건드리지 않는다 — `Singleton<T>.Awake`는 오브젝트별 실행 순서가 보장되지 않으므로, `SettingsSystem`이 `AudioSystem`보다 먼저 `Awake`될 경우 `AudioSystem.Instance`가 아직 `null`이다. 파일 읽기 자체는 다른 싱글턴에 의존하지 않으므로 `OnAwake`에서 안전하게 끝내고, `AudioSystem`을 실제로 건드리는 부분만 별도 공개 메서드로 분리해 `GameManager`가 안전한 시점에 호출하도록 위임한다(`AudioSystem`이 `EventBus.Subscribe`만으로 `GameManager` 이벤트를 받는 것과 달리, 볼륨 적용은 반환값 없는 즉시 호출이 필요해 이벤트 구독으로는 대체하지 않았다).

### 2. 저장 방식: `SaveSystem` 예정 경로를 선점하는 직접 JSON 파일 I/O

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `PlayerPrefs` (Unity 내장 key-value 저장소) | 기각 — 안드로이드에서는 XML로 저장되어 `SaveSystem` 로드맵이 최종적으로 목표하는 형식(JSON, `persistentDataPath/save/*.json`)과 전혀 다른 별도 저장소가 된다. `SaveSystem` 도입 시 `PlayerPrefs`에 있던 값을 파일로 옮기는 마이그레이션 작업이 추가로 필요해짐 |
| **`Application.persistentDataPath/save/settings.json`에 `JsonUtility`로 직접 파일 I/O** | **채택** — `SaveSystem` 로드맵이 이미 명시한 저장 경로("저장 경로" 절: `Application.persistentDataPath/save/settings.json`)를 그대로 선점한다. `SaveSystem` 구현 후에는 `SettingsSystem`의 파일 I/O 두 메서드(`LoadFromDisk`/`Save`) 내부만 `SaveSystem.Load<SettingsData>(SlotKey.Settings)`/`Save<SettingsData>(...)` 호출로 교체하면 되고, 기존에 쌓인 `settings.json`을 그대로 읽어들일 수 있어 마이그레이션이 필요 없다 |

```csharp
private static readonly string SettingsFilePath =
    Path.Combine(Application.persistentDataPath, "save", "settings.json");

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
```

- 파일이 없거나(최초 실행) 손상돼 파싱에 실패하면 기본값으로 폴백한다 — `SaveSystem`이 도입되면 갖게 될 "백업 파일 복원"([plan-gamemanager.md](../gamemanager/plan-gamemanager.md) 엣지 케이스: "`OnApplicationQuit`에서 저장 실패 → `SaveSystem`이 백업 파일로 복원 처리, `SaveSystem` 책임")은 이 문서의 범위가 아니다 — 지금은 손상된 파일을 감지해 조용히 기본값으로 대체하는 수준까지만 처리한다.
- 로드맵이 `SaveSystem`에 요구한 AES 암호화("모바일 부정행위 방지 기초")는 적용하지 않는다 — 암호화 대상은 재화/점수 등 치트 방지가 필요한 유저 진행 데이터이고, 볼륨/진동/언어 설정은 조작돼도 실질적 피해가 없는 값이라 평문 JSON으로 충분하다.
- 로드맵이 `SaveSystem`에 요구한 "비동기 저장(메인 스레드 블로킹 방지)"도 적용하지 않는다 — 설정 파일은 수십 바이트 수준이라 동기 `File.WriteAllText`의 프레임 비용은 무시할 만한 수준이며, 진짜 비동기 저장이 필요해지면 `SaveSystem` 자체가 그 책임을 진다.

### 3. 볼륨: `AudioSystem` forwarding, 중복 상태 없음

```csharp
public void SetVolume(AudioChannel channel, float linear01)
{
    AudioSystem.Instance.SetVolume(channel, linear01);
    Save();
    EventBus.Publish(new SettingsChanged());
}

public float GetVolume(AudioChannel channel) => AudioSystem.Instance.GetVolume(channel);
```

`SettingsSystem`은 `_masterVolume01` 같은 필드를 별도로 갖지 않는다 — 현재 볼륨 값의 유일한 진실 소스는 `AudioSystem`이고, `SettingsSystem`은 (1) 호출을 그대로 전달하고 (2) 전달 직후 `AudioSystem`이 실제로 반영한 값을 `Save()`가 다시 읽어 파일에 반영하는 역할만 한다. 두 시스템이 각자 볼륨 값을 들고 있으면 어느 한쪽만 갱신되는 경로(예: 향후 다른 코드가 `AudioSystem.Instance.SetVolume`을 직접 호출)에서 값이 어긋날 수 있으므로, 상태는 한 곳(`AudioSystem`)에만 두고 `SettingsSystem`은 그 값을 읽어 영속화만 한다.

### 4. 진동/언어: `SettingsSystem`이 유일한 소유자, 실제 적용은 각 소비자 책임

```csharp
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
```

- `UserData`의 관례(읽기 전용 프로퍼티 + 명시적 `SetX` 메서드, 변경 시마다 이벤트 발행)를 그대로 따른다.
- `Vibration`을 실제로 기기에서 진동시키는 호출(`Handheld.Vibrate()` 등)은 이 시스템의 책임이 아니다 — `AudioSystem`이 "어떤 씬에서 어떤 BGM을 트는가"를 모르는 것과 같은 이유로, `SettingsSystem`은 "언제 진동해야 하는가"를 모른다. 버튼 클릭 등 진동을 유발하는 호출부가 `if (SettingsSystem.Instance.Vibration) Handheld.Vibrate();` 형태로 직접 확인한다(아직 그런 호출부 자체가 없음 — "이번 범위에서 제외" 참고).
- `Language`는 `string`이 아니라 Unity 내장 `SystemLanguage` enum을 사용한다 — 로드맵 원안은 `string Language`였지만, 이 프로젝트는 문자열 대신 enum으로 값의 범위를 고정하는 관례를 이미 여러 곳에서 쓰고 있다(`GameState`, `AudioChannel`, `SpriteCategory`). 다만 아직 `LocalizationSystem`이 없어 "이 게임이 지원하는 언어 목록"이 정해지지 않았으므로, 프로젝트 자체 enum을 새로 만들어 값 목록을 지금 확정 짓는 대신 이미 모든 언어를 포괄하는 Unity 내장 enum을 그대로 재사용한다. 실제 지원 언어를 제한하는 로직(예: 지원하지 않는 언어면 영어로 폴백)은 `LocalizationSystem` 구현 시 결정한다.
- 기본값은 `SystemLanguage.Unknown`이 아니라 `Application.systemLanguage`(기기 설정 언어)로 초기화한다 — `LoadFromDisk()`가 파일이 없을 때 반환하는 기본 `SettingsData`에서 `Language` 필드만 명시적으로 채워 넣는다. 한 번이라도 저장이 이뤄지면(`Save()`가 항상 `_data.Language`를 그대로 써 넣으므로) 이후로는 파일에 구체적인 값이 남아 `Unknown`이 다시 나타날 일이 없다.

### 5. `GameManager` 통합: 기존 `LogoSequence` 주석을 실제 호출로 교체

`GameManager.cs`(기존 파일, 수정 필요)의 `LogoSequence`는 이미 이 지점을 주석으로 남겨 두었다:

```csharp
// 기존 (GameManager.cs, 35~36번째 줄)
// SaveSystem에서 설정 로드
// (SaveSystem 구현 후 연결)
```

이를 다음으로 교체한다:

```csharp
private IEnumerator LogoSequence()
{
    // 코어 시스템이 Awake 완료될 때까지 1프레임 대기
    yield return null;

    // 저장된 설정을 불러와 AudioSystem에 반영
    SettingsSystem.Instance.ApplyLoadedVolumes();

    // 초기화 완료 → Logo 상태 진입
    ChangeState(GameState.Logo);
}
```

`using JungleDice.Core.Settings;`를 `GameManager.cs` 상단에 추가해야 한다. `SettingsSystem`도 `AudioSystem`과 마찬가지로 `GameManagers` 오브젝트에 부착되므로, `Awake` 순서상 `GameManager`의 `Awake`(따라서 `StartCoroutine(LogoSequence)`도)가 먼저 실행되더라도 코루틴이 1프레임을 대기한 뒤에 실행되므로 `SettingsSystem.Instance`/`AudioSystem.Instance` 모두 이미 존재함이 보장된다(`AudioSystem` 문서가 별도 순서 보장 장치 없이도 동작해 온 것과 동일한 근거).

### 6. 변경 알림: `SettingsChanged` 이벤트

```csharp
// GameEvents.cs에 추가
public record SettingsChanged();
```

`UserData`의 모든 setter가 `UserDataChanged()`를 발행하는 관례를 그대로 따른다 — 어떤 필드가 바뀌었는지 구분하지 않는 단일 이벤트로, 구독자(향후 설정 UI)는 이벤트를 받으면 전체 화면을 다시 그리는 정도로 충분하다는 전제다(개별 필드마다 이벤트를 나누는 세분화는 지금 구독자가 없어 과설계).

---

## 클래스 구조

```
SettingsData                                        (신규, Core/Settings/, [Serializable] 클래스)
├── MasterVolume / BgmVolume / SfxVolume : float     (기본값 1f, JsonUtility 직렬화 대상)
├── Vibration : bool                                 (기본값 true)
└── Language : SystemLanguage                        (기본값 Unknown — 최초 미저장 상태의 sentinel)

SettingsSystem : Singleton<SettingsSystem>           (신규, Core/Settings/)
├── ApplyLoadedVolumes()                                      ← GameManager.LogoSequence가 1프레임 대기 후 호출
├── SetVolume(AudioChannel channel, float linear01)           ← AudioSystem.SetVolume forwarding + Save + SettingsChanged
├── GetVolume(AudioChannel channel) : float                   ← AudioSystem.GetVolume forwarding (상태 비보유)
├── Vibration : bool (get)                                    ← _data.Vibration
├── SetVibration(bool enabled)                                ← Save + SettingsChanged
├── Language : SystemLanguage (get)                           ← _data.Language
├── SetLanguage(SystemLanguage language)                      ← Save + SettingsChanged
├── LoadFromDisk() : SettingsData                              ← private, OnAwake에서 1회, JSON 파일 읽기 + 실패 시 기본값
├── Save()                                                     ← private, AudioSystem 현재 볼륨 재수집 + JSON 파일 쓰기
└── (SerializeField 없음 — 인스펙터 연결 불필요)
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Core/
        └── Settings/                       ← 신규
            ├── SettingsSystem.cs
            └── SettingsData.cs
```

`Core/Settings/`는 `Core/Audio/`, `Core/Sprites/`, `Core/User/`와 동일하게 특정 하위 시스템에 속하지 않는 공용 시스템이므로 `Core/` 바로 아래 배치한다(로드맵 "폴더 구조" 절이 제안한 위치 그대로).

---

## 상세 구현 명세

### SettingsData.cs

```csharp
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
```

### SettingsSystem.cs

```csharp
using System;
using System.IO;
using JungleDice.Core.Audio;
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.Settings
{
    public class SettingsSystem : Singleton<SettingsSystem>
    {
        private static readonly string SettingsFilePath =
            Path.Combine(Application.persistentDataPath, "save", "settings.json");

        private SettingsData _data;

        protected override void OnAwake()
        {
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
```

### GameEvents.cs (기존 파일, 추가)

```csharp
// 설정 시스템
public record SettingsChanged();
```

### GameManager.cs (기존 파일, 수정)

`LogoSequence`의 "SaveSystem에서 설정 로드 (SaveSystem 구현 후 연결)" 주석 두 줄을 위 "핵심 설계 결정 5"의 코드로 교체하고, 상단에 `using JungleDice.Core.Settings;`를 추가한다.

---

## Unity 씬/오브젝트 구성

```
[Logo.unity, GameManagers(DontDestroyOnLoad, 기존 오브젝트)]
└── GameManagers
    ├── GameManager.cs        ← 기존
    ├── AudioSystem.cs        ← 기존
    └── SettingsSystem.cs     ← 신규 부착
```

`SettingsSystem`은 `SerializeField`가 하나도 없다 — 믹서 그룹 연결이 필요했던 `AudioSystem`과 달리, 컴포넌트를 `GameManagers`에 부착하는 것 외에 에디터에서 추가로 연결할 것이 없다.

---

## 이번 범위에서 제외

- **실제 설정 UI(설정 팝업/화면)** — `UIManager`가 아직 미구현. `SettingsSystem`은 백엔드 API(`SetVolume`/`SetVibration`/`SetLanguage`)만 제공하고, 슬라이더/토글을 이 API에 연결하는 것은 `UIManager` 및 실제 설정 화면 구현 시점의 작업
- **진동을 실제로 발생시키는 호출** — `Vibration` 플래그만 보관/노출한다. `Handheld.Vibrate()` 등을 호출하는 지점(버튼 클릭 피드백 등) 자체가 아직 프로젝트에 없으므로, 그 호출부가 생길 때 `SettingsSystem.Instance.Vibration`을 확인하도록 만드는 것은 해당 기능 구현 시점의 책임
- **언어 변경을 실제 텍스트에 반영하는 처리(`LocalizationSystem`)** — `SettingsSystem`은 선택된 `SystemLanguage` 값만 저장한다. 이 값을 읽어 UI 텍스트를 실제로 갈아 끼우는 것은 `LocalizationSystem` 몫(로드맵상 별도 시스템, `SaveSystem`에 의존)
- **지원하지 않는 언어에 대한 폴백 정책** — 예: 기기 언어가 태국어인데 게임이 한국어/영어만 지원하는 경우 무엇으로 대체할지는 `LocalizationSystem`이 지원 언어 목록을 확정한 이후 결정
- **`SaveSystem` 도입 후 마이그레이션 코드** — 이 문서의 파일 I/O는 `SaveSystem`이 쓸 경로/포맷을 그대로 선점했으므로, `SaveSystem` 구현 시 `LoadFromDisk`/`Save` 내부만 교체하면 되고 별도 마이그레이션 스크립트는 필요 없다는 전제. `SaveSystem`이 실제로 다른 포맷(예: 암호화)을 강제한다면 이 전제가 깨질 수 있음 — 그 시점에 재검토
- **옵션 창 슬라이더의 드래그 중 저장 흐름** — 실제 흐름은 이미 확정돼 있다: 드래그하는 동안은 `AudioSystem.Instance.SetVolume`을 직접 호출해 믹서 값만 실시간으로 바꾸고(파일 쓰기 없음), 옵션 창을 닫는 시점에 `SettingsSystem.Instance.SetVolume`을 채널별로 1회씩 호출해 그제서야 저장을 확정한다(아래 "구현 시 주의사항" 참고). 다만 이 흐름을 실제로 코드로 잇는 옵션 창 UI 자체가 아직 없으므로, 이번 문서에서는 규칙만 남겨 두고 구현은 옵션 창 작업 시점으로 미룬다
- **볼륨 외 설정 항목의 확장(예: 자막, 밝기, 콘트롤러 진동 강도)** — 로드맵이 명시한 4개 항목(볼륨/진동/언어, 볼륨은 3채널)까지만 다룬다

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 앱 최초 실행(설정 파일 없음) | `File.Exists` false → 기본값(`MasterVolume`/`BgmVolume`/`SfxVolume` = 1f, `Vibration` = true) 사용, `Language`는 `Application.systemLanguage`로 채움 |
| 설정 파일이 손상되어 JSON 파싱 실패 | `catch` → 경고 로그 1회 + 기본값으로 폴백. 다음 `Save()` 호출 시 손상된 파일이 정상 값으로 덮어써짐 |
| `SetVolume`/`SetVibration`/`SetLanguage`를 `AudioSystem`이 아직 `Awake`되지 않은 시점에 호출(`OnAwake` 안에서 직접 호출 등) | `Save()`가 내부에서 `AudioSystem.Instance.GetVolume`을 호출하므로 `NullReferenceException` — 방어 코드 없음, 기존 관례(인스펙터 연결 누락과 동일하게 즉시 드러냄)와 동일 |
| `ApplyLoadedVolumes()`를 `GameManager`의 1프레임 대기 이전에 호출 | 위와 동일하게 `AudioSystem.Instance`가 `null`이면 `NullReferenceException` — 반드시 `LogoSequence`의 `yield return null` 이후 지점에서만 호출 |
| `Save()`의 파일 쓰기 실패(디스크 공간 부족 등) | `catch` + 경고 로그. 메모리상의 `_data`/`AudioSystem`의 실제 볼륨은 최신 값을 유지하므로 앱 동작 자체는 정상, 다음 성공적인 `Save()`에서 파일에 반영됨 |
| 옵션 창 슬라이더 드래그 중 `SettingsSystem.SetVolume`을 그대로 연결(잘못된 사용) | 매 프레임 동기 파일 쓰기 발생 — 정해진 흐름이 아니다. 드래그 중엔 `AudioSystem.Instance.SetVolume` 직접 호출, 옵션 창 닫힘 시점에만 `SettingsSystem.Instance.SetVolume` 호출이 맞는 사용법(아래 "구현 시 주의사항" 참고) |
| `settings.json`을 텍스트 에디터로 열어 `Language` 필드를 임의 문자열로 손상시킨 뒤 재실행 | `JsonUtility.FromJson`이 알 수 없는 enum 값을 만나면 예외 없이 기본값(0, 즉 `SystemLanguage.Afrikaans`)으로 역직렬화한다 — `Unknown`으로 폴백되지 않으므로 사용자가 의도치 않은 언어로 보일 수 있음(Unity `JsonUtility`의 알려진 한계, 파일을 직접 수정하는 비정상 경로에서만 발생) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 앱 최초 실행(설정 파일 없음) | `SettingsSystem.Instance.Vibration == true`, `GetVolume(AudioChannel.Master) == 1f`, `Language == Application.systemLanguage` |
| 2 | `SetVolume(AudioChannel.BGM, 0.3f)` 호출 | `GetVolume(AudioChannel.BGM) == 0.3f`(AudioSystem에 즉시 반영), `settings.json` 파일의 `BgmVolume`도 `0.3`으로 갱신 |
| 3 | 시나리오 2 이후 앱 재시작(에디터 플레이 모드 재시작) 후 `LogoSequence` 진행 | `ApplyLoadedVolumes()` 호출 후 `AudioSystem.GetVolume(BGM) == 0.3f` — 재시작 전 값이 그대로 복원됨 |
| 4 | `SetVibration(false)` 호출 | `Vibration == false`, `settings.json`의 `Vibration`도 `false`로 갱신, `SettingsChanged` 이벤트 발행 확인 |
| 5 | `SetLanguage(SystemLanguage.English)` 호출 | `Language == SystemLanguage.English`, `settings.json`에 반영, `SettingsChanged` 발행 |
| 6 | `settings.json`을 깨진 텍스트(JSON 아님)로 수동 편집 후 재실행 | 콘솔에 경고 로그 1회, 이후 기본값으로 정상 동작(크래시 없음) |
| 7 | `EventBus.Subscribe<SettingsChanged>`로 구독 중인 상태에서 `SetVolume`/`SetVibration`/`SetLanguage`를 각각 호출 | 매 호출마다 구독자에게 `SettingsChanged` 1회씩 전달 |

---

## 구현 시 주의사항

- **`AudioSystem`이 `Awake`된 이후에만 `SettingsSystem`의 공개 API를 호출한다**: `SetVolume`/`GetVolume`은 물론 `SetVibration`/`SetLanguage`도 내부에서 `Save()`를 거치며 `AudioSystem.Instance.GetVolume`을 호출하므로, `AudioSystem`이 아직 없으면 어떤 setter를 호출해도 `NullReferenceException`이 난다. `ApplyLoadedVolumes()`는 반드시 `GameManager.LogoSequence`의 1프레임 대기 이후 지점에서만 호출할 것.
- **볼륨을 영구 반영(저장)할 때는 `SettingsSystem.SetVolume`을 거친다 — 옵션 창 드래그 중의 실시간 프리뷰만 예외**: 옵션 창 슬라이더를 드래그하는 동안에는 `AudioSystem.Instance.SetVolume`을 직접 호출해 믹서 값만 즉시 바꾸고 파일 쓰기는 하지 않는다. 옵션 창을 닫는 시점(또는 슬라이더 값이 확정되는 시점)에 채널별로 `SettingsSystem.Instance.SetVolume`을 한 번씩 호출해야 그제서야 `settings.json`에 저장된다 — 이 마지막 호출을 빼먹으면 드래그로 바꾼 값이 다음 앱 실행 시 이전 값으로 되돌아간다. 드래그 UI가 아닌 다른 경로(예: 코드로 기본값을 강제 설정)에서는 예외 없이 `SettingsSystem.SetVolume`을 거친다. `SettingsSystem`은 `AudioSystem`의 현재 값을 상태로 들고 있지 않으므로 이 규칙은 코드로 강제되지 않고 호출부 규율로만 지켜진다.
- **옵션 창 슬라이더의 `OnValueChanged`를 프레임마다 `SettingsSystem.SetVolume`에 직접 연결하지 않는다**: 그렇게 연결하면 드래그 중 매 프레임 동기 파일 쓰기가 반복돼 저사양 기기에서 프레임 드랍을 유발할 수 있다. `OnValueChanged`는 `AudioSystem.Instance.SetVolume`에 연결해 실시간 프리뷰만 담당시키고, 저장은 옵션 창을 닫는 콜백 한 곳에서 `SettingsSystem.Instance.SetVolume`으로 확정한다 — 이 연결은 옵션 창 UI 구현 시점의 작업이다.
- **`Language` 필드가 `SystemLanguage.Unknown`인 경우는 정상 상태다**: `LoadFromDisk()`가 파일이 없을 때만 명시적으로 `Application.systemLanguage`를 채워 넣는다는 점을 기억할 것 — `SettingsData`의 필드 기본값 자체는 `Unknown`이므로, `new SettingsData()`를 직접 생성해 쓰면 `Unknown`이 그대로 남는다.
- **`SaveSystem` 구현 시 교체 지점은 `LoadFromDisk`/`Save` 두 메서드뿐이다**: 공개 API(`SetVolume`/`GetVolume`/`Vibration`/`SetVibration`/`Language`/`SetLanguage`/`ApplyLoadedVolumes`)의 시그니처는 바꾸지 않는다 — 이미 이 API를 호출하는 코드(설정 UI 등)가 있다면 그대로 유지되도록.

---

## 구현 후 체크리스트

- [x] `SettingsData.cs` 작성 (`Assets/Scripts/Core/Settings/`)
- [x] `SettingsSystem.cs` 작성 (`Singleton<SettingsSystem>` 상속, JSON 파일 로드/저장, 볼륨 forwarding, 진동/언어 보관)
- [x] `GameEvents.cs`에 `SettingsChanged` 레코드 추가
- [x] `GameManager.cs`의 `LogoSequence` 주석을 `SettingsSystem.Instance.ApplyLoadedVolumes()` 호출로 교체, `using JungleDice.Core.Settings;` 추가
- [x] `GameManagers` 오브젝트(`Logo.unity`)에 `SettingsSystem.cs` 부착 (Unity 에디터 작업, 인스펙터 연결 불필요)
- [ ] 테스트 시나리오 7개 검증
- [ ] (추후) `SaveSystem` 구현 시 `LoadFromDisk`/`Save`의 파일 I/O를 `SaveSystem.Load<SettingsData>`/`Save<SettingsData>` 호출로 교체
- [ ] (추후) `LocalizationSystem` 구현 시 `SettingsChanged` 구독해 언어 변경을 실제 텍스트 갱신에 연결
- [x] (추후) 실제 설정 UI(`SettingsPopup` 등) 구현 시 슬라이더/토글을 `SetVolume`/`SetVibration`/`SetLanguage`에 연결 → [기본 옵션 구현 개요](../../07-option/plan-option.md)(볼륨 슬라이더만 우선 연결, 진동/언어는 여전히 미연결)
- [ ] (추후) 진동을 유발하는 호출부(버튼 클릭 등) 구현 시 `SettingsSystem.Instance.Vibration` 확인 로직 추가
