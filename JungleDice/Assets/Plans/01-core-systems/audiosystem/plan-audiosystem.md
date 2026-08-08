# AudioSystem 구현 계획

> 상위 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md) (시스템 목록 #3)
> Phase 2(데이터 계층) 항목이지만 `SaveSystem`/`SettingsSystem`보다 먼저 독립 구현 — 두 시스템 모두 아직 미구현이며, `AudioSystem`은 그 존재를 몰라도 완결되도록 설계(아래 "설정 저장 연동은 범위 밖" 참고)
> 의존 관계: `JungleDice.Core.Singleton<T>`(영속 싱글턴 베이스), `JungleDice.Core.Event.EventBus`(`AppPauseChanged`/`AppFocusChanged` 구독 — 새 이벤트 추가 없음), `DG.Tweening`(BGM 크로스페이드)
> 범위: BGM 재생/크로스페이드, SFX 재생(풀 기반, 동시 재생 제한), 채널별(Master/BGM/SFX) 볼륨 제어, 앱 포커스/일시정지 시 자동 뮤트. 실제 BGM/SFX 클립 목록 확정과 파일 배치, `SettingsSystem`과의 저장/복원 연동, 씬별로 "어떤 BGM을 틀 것인가"는 범위 밖.

---

## 배경 / 문제 인식

`plan-core-systems.md`가 스케치한 인터페이스는 다음과 같다:

```csharp
AudioSystem.Instance.PlayBGM(AudioID.MainTheme, fadeIn: 1.0f);
AudioSystem.Instance.PlaySFX(AudioID.ButtonClick);
AudioSystem.Instance.SetVolume(AudioChannel.BGM, 0.8f);
```

이 문서는 이 인터페이스를 실제로 구현하되, 로드맵이 제안한 "오디오 클립은 `ScriptableObject`로 등록(`AudioClipRegistry`)"은 채택하지 않는다 — 프로젝트에는 이미 [SpriteManager](../spritemanager/plan-spritemanager.md)가 "enum 값 이름 = `Resources` 하위 폴더/파일명"이라는 훨씬 가벼운 패턴으로 같은 문제(이름만으로 리소스 조회)를 풀어 두었다. `AudioClipRegistry`를 새로 만들면 enum과 별도로 에디터에서 매핑 애셋을 계속 동기화해야 하는 반면, `SpriteManager` 패턴은 파일을 정해진 폴더에 이름 그대로 넣기만 하면 끝난다. 아래 "핵심 설계 결정 5"에서 두 방식을 표로 비교한다.

`SaveSystem`/`SettingsSystem`은 아직 코드가 없다(`Assets/Scripts/Core/` 하위에 `Save/`, `Settings/` 폴더 없음). 로드맵의 우선순위 표는 `AudioSystem`의 의존 관계를 `GameManager` 하나로만 명시하고 있어(`SettingsSystem`이 오히려 `AudioSystem`에 의존하는 역방향), 이번 문서는 그 관계를 그대로 따른다 — `AudioSystem`은 `SetVolume`/`GetVolume`을 공개 API로 노출할 뿐, 그 값을 어디에 저장하고 앱 시작 시 어떻게 복원할지는 신경 쓰지 않는다(추후 `SettingsSystem`이 이 API를 호출하는 소비자가 된다).

앱 포커스/일시정지 처리는 이미 `GameManager`가 `OnApplicationPause`/`OnApplicationFocus`를 각각 `AppPauseChanged`/`AppFocusChanged`로 발행하고 있다([plan-gamemanager.md](../gamemanager/plan-gamemanager.md)) — 새 이벤트를 정의할 필요 없이 그대로 구독한다.

---

## 설계 목표

- 로드맵이 제시한 공개 인터페이스(`PlayBGM`/`PlaySFX`/`SetVolume`)를 그대로 구현 — 호출부(각 씬 매니저, UI 버튼 등)는 이 문서 이후 별도 설계 변경 없이 바로 사용 가능
- BGM 전환은 항상 크로스페이드 — 끊기거나 튀는 지점 없이 이전 트랙과 다음 트랙이 겹쳐 재생
- SFX는 동시에 너무 많이 겹치지 않도록 풀 크기로 자연스럽게 제한 — 풀이 가득 차면 예외 없이 조용히 무시
- 볼륨은 Master/BGM/SFX 세 채널을 독립적으로 제어 — `AudioMixer` 노출 파라미터로 실제 감쇠를 적용
- 앱이 백그라운드로 가거나 포커스를 잃으면 사용자가 설정한 볼륨 값을 잃지 않고 그대로 뮤트만 됐다가 복귀 시 원래 값으로 되돌아옴
- `Singleton<T>`을 상속해 씬 전환에도 유지 — [plan-singleton.md](../singleton/plan-singleton.md)가 이미 `AudioSystem`을 후속 채택 대상으로 명시해 둔 결정을 그대로 따름
- `AudioSystem`은 "어떤 씬에서 어떤 BGM을 트는가"를 모른다 — 상태-음악 매핑 테이블을 갖지 않고, 각 씬 매니저가 자신의 `OnAwake`에서 `PlayBGM`을 직접 호출하는 책임을 진다(결합도를 낮추기 위한 의도적 범위 축소, 아래 "이번 범위에서 제외" 참고)

---

## 핵심 설계 결정

### 1. `Singleton<AudioSystem>` 상속

```csharp
public class AudioSystem : Singleton<AudioSystem>
{
    protected override void OnAwake() { ... }
}
```

`GameManager`/`SceneLoader`와 동일한 영속 싱글턴 베이스를 그대로 재사용한다 — `plan-singleton.md`가 이미 "후속 시스템(AudioSystem 등)도 이 베이스를 사용"이라고 명시해 둔 결정이다. `Instance` 선언이나 중복 인스턴스 파괴 로직을 새로 작성하지 않는다.

### 2. BGM: 이중 `AudioSource` 크로스페이드

```csharp
[SerializeField] private AudioMixerGroup _bgmMixerGroup;
[SerializeField] private float _defaultBgmFadeDuration = 1f;

private AudioSource _bgmSourceA;
private AudioSource _bgmSourceB;
private AudioSource _activeBgmSource;
private AudioSource _inactiveBgmSource;
private AudioID? _currentBgmId;
private Tween _bgmFadeOutTween;
private Tween _bgmFadeInTween;

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
```

- 두 `AudioSource`가 항상 하나는 "현재 재생 중", 하나는 "다음 대기용" 역할을 번갈아 맡는다 — 매번 새 `AudioSource`를 만들거나 지우지 않고 참조만 스왑한다.
- `_currentBgmId == id`면 조기 반환 — 같은 트랙을 여러 번 요청해도 크로스페이드가 중첩되지 않는다(예: 같은 씬을 반복 재진입해도 매번 페이드가 다시 튀지 않음).
- `DOFade`가 이미 진행 중인 상태에서 `PlayBGM`이 다시 호출되는 경우(트랙을 빠르게 연달아 전환) `_bgmFadeInTween?.Kill()`/`_bgmFadeOutTween?.Kill()`로 이전 트윈을 먼저 정리 — `MainMenuTabSlideController`의 `Tween?.Kill()` 관례와 동일.
- `fadeIn`은 로드맵 인터페이스의 파라미터명을 그대로 따르되, 내부적으로는 페이드 인/아웃 두 방향 모두의 지속 시간으로 쓰인다(대칭 크로스페이드) — 인/아웃 시간을 따로 받는 오버로드는 지금 요구사항에 없으므로 추가하지 않는다(YAGNI).

### 3. SFX: 런타임 생성 `AudioSource` 풀, 동시 재생 제한은 "풀 소진 시 드롭"

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| 재생마다 `AudioSource.PlayClipAtPoint` 또는 임시 `GameObject` 생성 | 기각 — SFX가 몰릴 때마다 `Instantiate`/`Destroy`가 반복돼 GC 스파이크 발생, "풀링 기반"이라는 로드맵 요구사항과도 어긋남 |
| 인스펙터에 풀 크기만큼 `AudioSource` 오브젝트를 미리 배치 | 기각 — 풀 크기를 바꿀 때마다 에디터에서 자식 오브젝트를 수작업으로 추가/삭제해야 함 |
| **`OnAwake`에서 코드로 `_sfxPoolSize`개의 자식 `AudioSource`를 생성** | **채택** — 인스펙터에 남기는 연결은 `AudioMixerGroup` 하나뿐, 풀 크기는 필드 값만 바꾸면 됨 |

```csharp
[SerializeField] private AudioMixerGroup _sfxMixerGroup;
[SerializeField] private int _sfxPoolSize = 8;

private AudioSource[] _sfxPool;

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
```

- "동시 재생 제한"을 별도 카운터로 관리하지 않고 풀 크기 자체가 상한이 되게 한다 — `_sfxPoolSize`개가 모두 `isPlaying`이면 그 이상은 자연스럽게 재생되지 않는다.
- 풀이 가득 찼을 때 가장 오래된 소스를 가로채 재생하는(steal) 방식은 채택하지 않는다 — 재생 중이던 SFX가 끊기는 게 더 어색하므로, 이번 요청을 드롭하는 쪽을 택한다(요청자 확인 없이 내린 기본값, 필요해지면 정책만 교체 가능하도록 `GetIdleSfxSource` 한 곳에 로직이 모여 있음).
- `GetIdleSfxSource`는 `foreach` 선형 탐색이다 — `_sfxPoolSize`가 8~16 수준인 SFX 풀에서는 매 프레임 호출되는 것도 아니므로 별도 인덱스 캐싱/라운드로빈 최적화를 추가하지 않는다(YAGNI).

### 4. 채널 볼륨: `AudioMixer` 노출 파라미터 + Linear↔dB 변환

```csharp
public enum AudioChannel
{
    Master,
    BGM,
    SFX,
}

[SerializeField] private AudioMixer _mixer;

private float _masterVolume01 = 1f;
private float _bgmVolume01 = 1f;
private float _sfxVolume01 = 1f;

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

private const float MuteDb = -80f;

private static float LinearToDb(float linear01) =>
    linear01 <= 0.0001f ? MuteDb : Mathf.Log10(linear01) * 20f;
```

- `AudioMixer`는 데시벨(로그 스케일) 파라미터만 노출하므로, 슬라이더 등에서 다루기 쉬운 0~1 선형 값을 받아 변환한다 — Unity 공식 문서가 권장하는 `Mathf.Log10(x) * 20` 변환을 그대로 사용.
- `_masterVolume01`/`_bgmVolume01`/`_sfxVolume01`을 필드로 따로 들고 있는 이유는 두 가지: (1) `GetVolume`이 `AudioMixer.GetFloat` + dB→선형 역변환 없이 바로 값을 돌려줄 수 있고, (2) 아래 "핵심 설계 결정 6"의 뮤트 처리가 뮤트 해제 시 되돌아갈 "사용자가 실제로 설정한 값"을 알아야 하기 때문 — 뮤트 중에는 `AudioMixer`의 실제 파라미터 값이 `_masterVolume01`과 달라지므로(강제로 `-80dB`) `AudioMixer.GetFloat`만으로는 원래 값을 복원할 수 없다.
- 3채널 각각의 볼륨 필드를 `switch`로 분기하는 대신 `Dictionary<AudioChannel, float>`로 통합할 수도 있었지만, 채널이 3개로 고정돼 있고 앞으로도 크게 늘어날 여지가 없어(로드맵상 Master/BGM/SFX 셋뿐) 필드 3개 + `switch`가 오히려 더 읽기 쉽다(과잉 추상화 방지).

### 5. `AudioClip` 조회: `Resources.Load` 기반, `SpriteManager`와 동일 패턴

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `ScriptableObject` 레지스트리(`AudioClipRegistry`, 로드맵 원안) | 기각 — `AudioID` enum과 별도로 에디터에서 "enum 값 ↔ AudioClip" 매핑 애셋을 계속 손으로 동기화해야 함. 클립이 늘어날 때마다 enum 값 추가 + 레지스트리 항목 추가라는 두 곳을 건드려야 하는 중복 |
| **`Resources.Load<AudioClip>` + enum 값 이름을 파일명으로 그대로 사용** | **채택** — `SpriteManager.GetCard`와 완전히 동일한 패턴. 클립 추가 시 `AudioID`에 값 하나 추가하고 정해진 폴더에 같은 이름의 파일만 넣으면 끝, 별도 매핑 애셋 불필요 |

```csharp
private const string BgmFolder = "BGM";
private const string SfxFolder = "SFX";

private readonly Dictionary<AudioID, AudioClip> _clipCache = new();

private AudioClip GetClip(AudioID id, string folder)
{
    if (_clipCache.TryGetValue(id, out var cached)) return cached;

    var clip = Resources.Load<AudioClip>($"Audio/{folder}/{id}");
    if (clip == null)
        Debug.LogWarning($"[AudioSystem] AudioClip not found: Audio/{folder}/{id}");

    _clipCache[id] = clip; // null도 캐시 — 같은 이름을 반복 요청해도 매번 Resources.Load를 다시 타지 않음
    return clip;
}
```

- `SpriteManager`는 `Resources.Load` 자체의 내부 캐시를 신뢰해 별도 캐시를 두지 않았지만(YAGNI로 명시), `AudioSystem`은 `PlaySFX`가 짧은 시간에 반복 호출될 수 있는 경로라 `Dictionary` 캐시를 추가한다 — SFX는 버튼 클릭 등으로 프레임마다 여러 번 요청될 수 있어 스프라이트 조회보다 호출 빈도가 훨씬 높다는 차이가 있다.
- 조회 실패(`null`)도 캐시한다 — 오타난 `AudioID`를 매 프레임 반복 요청해도 `Resources.Load`가 매번 디스크를 다시 타지 않고, 경고 로그도 최초 1회만 남는다(첫 조회 실패 시점에만 `Debug.LogWarning` 호출, 캐시 히트 시점엔 로그 없음).
- `AudioID`는 BGM/SFX 구분 없이 하나의 enum이다 — `PlayBGM`/`PlaySFX` 호출부가 이미 카테고리(폴더)를 알고 있으므로(각각 `BgmFolder`/`SfxFolder`를 고정으로 넘김) enum 자체에 접두사를 두지 않는다. 같은 이름을 BGM/SFX 양쪽 폴더에 두는 것도 가능은 하지만 혼란을 피하려면 이름을 겹치지 않게 짓는 편이 좋다(강제하지는 않음, 구현 시 주의사항에 기록).

### 6. 앱 포커스/일시정지: 두 이벤트 모두 구독해 OR 조건으로 뮤트

```csharp
private bool _isPaused;
private bool _hasFocus = true;

protected override void OnAwake()
{
    EventBus.Subscribe<AppPauseChanged>(OnAppPauseChanged);
    EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged);
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
```

- `plan-gamemanager.md`가 이미 "Android에서 `OnApplicationPause`와 `OnApplicationFocus` 동작이 기기/OS 버전마다 상이할 수 있어 두 콜백 모두 처리"라고 명시해 둔 원칙을 그대로 따른다 — 둘 중 하나라도 "포커스 없음/일시정지"를 알리면 뮤트, 둘 다 정상으로 돌아와야 뮤트 해제.
- `_mixer.SetFloat`으로 `Master` 채널만 직접 조작하고 `_masterVolume01` 필드는 건드리지 않는다 — 뮤트는 "사용자가 설정한 값"을 잊어버리는 게 아니라 일시적으로 재생만 죽이는 것이므로, 복귀 시 `LinearToDb(_masterVolume01)`로 정확히 원래 값으로 돌아온다.
- `SetVolume(Master, ...)`이 뮤트 도중 호출되면(이론상 설정 화면이 InGame 밖에 있어 거의 발생하지 않음) `_masterVolume01`은 갱신되지만 실제 믹서 파라미터는 여전히 `-80dB`로 남는다 — 다음 `ApplyMuteState`(포커스 복귀 시)가 새 값으로 정상 반영하므로 값 자체가 유실되지는 않는다(엣지 케이스 표에서 재확인).

---

## 클래스 구조

```
AudioChannel                                      (신규, Core/Audio/, enum)
└── Master / BGM / SFX

AudioID                                           (신규, Core/Audio/, enum)
└── (실제 클립 확정 시 값 추가 — 값 이름이 곧 Resources/Audio/{BGM|SFX}/ 하위 파일명)

AudioSystem : Singleton<AudioSystem>              (신규, Core/Audio/)
├── PlayBGM(AudioID id, float fadeIn = -1f)                 ← 크로스페이드 전환, 동일 트랙 재요청은 무시
├── StopBGM(float fadeOut = -1f)                            ← 현재 BGM 페이드 아웃 후 정지
├── PlaySFX(AudioID id)                                     ← 풀에서 유휴 소스를 찾아 재생, 풀 소진 시 드롭
├── SetVolume(AudioChannel channel, float linear01)         ← 0~1 선형 → dB 변환 후 믹서 반영
├── GetVolume(AudioChannel channel) : float                 ← 마지막으로 설정된 선형 값 반환
├── BuildSfxPool()                                          ← private, OnAwake에서 1회, 코드로 SFX AudioSource 풀 생성
├── GetClip(AudioID id, string folder) : AudioClip           ← private, Resources.Load + 캐시
├── GetIdleSfxSource() : AudioSource                        ← private, 풀에서 재생 중이 아닌 소스 탐색
├── OnAppPauseChanged / OnAppFocusChanged                    ← private, EventBus 구독 핸들러
├── ApplyMuteState()                                         ← private, 두 플래그의 OR로 Master 채널 뮤트/복원
└── _mixer/_bgmMixerGroup/_sfxMixerGroup/_sfxPoolSize/_defaultBgmFadeDuration : [SerializeField]
```

---

## 파일 구성

```
Assets/
├── Resources/
│   └── Audio/                          ← 신규 폴더
│       ├── BGM/                        ← 실제 클립 확정 시 채움
│       └── SFX/                        ← 실제 클립 확정 시 채움
└── Scripts/
    └── Core/
        └── Audio/                      ← 신규
            ├── AudioSystem.cs
            ├── AudioChannel.cs
            └── AudioID.cs
```

`Core/Audio/`는 `Core/Sprites/`, `Core/Table/`, `Core/User/`와 동일하게 특정 하위 시스템에 속하지 않는 공용 시스템이므로 `Core/` 바로 아래 배치한다. `AudioChannel`은 3개 값으로 고정돼 거의 변하지 않아 `AudioSystem.cs` 안에 중첩할 수도 있었지만, `SetVolume`/`GetVolume`을 호출하는 쪽(향후 설정 UI 등)이 `AudioSystem`을 몰라도 타입만 참조할 수 있도록 `GameState`/`GameType`과 같은 관례로 별도 파일로 둔다. `AudioID`는 클립이 늘어날 때마다 값이 계속 추가될 것이 확실하므로 처음부터 별도 파일로 분리한다.

---

## 상세 구현 명세

### AudioChannel.cs

```csharp
namespace JungleDice.Core.Audio
{
    public enum AudioChannel
    {
        Master,
        BGM,
        SFX,
    }
}
```

### AudioID.cs

```csharp
namespace JungleDice.Core.Audio
{
    public enum AudioID
    {
        // 실제 BGM/SFX 클립 확정 시 여기에 값 추가
        // 값 이름이 곧 Resources/Audio/{BGM|SFX}/ 하위 파일명이 된다 (SpriteCategory와 동일 관례)
    }
}
```

### AudioSystem.cs

`OnAwake`는 위 "핵심 설계 결정" 각 절의 초기화를 한데 모은다 — 새로 도입되는 부분만 보인다(개별 메서드 본문은 위에 이미 제시):

```csharp
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

        // PlayBGM / StopBGM / PlaySFX / SetVolume / GetVolume / BuildSfxPool / GetClip /
        // GetIdleSfxSource / OnAppPauseChanged / OnAppFocusChanged / ApplyMuteState
        // → 위 "핵심 설계 결정" 2~6절에 이미 제시된 코드 그대로
    }
}
```

- `source.loop = true`는 BGM 전용 — SFX 풀의 `AudioSource`(`BuildSfxPool`)는 `loop` 기본값(`false`)을 그대로 둔다.
- `GameManager`처럼 `EventBus.Subscribe`의 반환 토큰(`IDisposable`)을 저장하지 않는다 — `AudioSystem`은 앱 생명주기 내내 유지되는 영속 싱글턴이라(`Singleton<T>`) `OnDestroy`가 정상 흐름에서 호출되지 않으므로, `InGameSceneManager`(`SceneSingleton`, 씬 전환마다 파괴됨)와 달리 `CompositeDisposable`로 해제 시점을 관리할 필요가 없다 — `GameManager.OnAwake`가 이미 같은 이유로 구독 해제를 하지 않는 것과 동일한 판단.

---

## Unity 씬/오브젝트 구성

```
[Logo.unity, GameManagers(DontDestroyOnLoad, 기존 오브젝트)]
└── GameManagers
    ├── GameManager.cs        ← 기존
    └── AudioSystem.cs        ← 신규 부착

[AudioSystem 인스펙터]
├── _mixer          ← 신규 AudioMixer 애셋
├── _bgmMixerGroup   ← _mixer 안의 BGM 그룹
└── _sfxMixerGroup   ← _mixer 안의 SFX 그룹
(BGM용 AudioSource 2개, SFX 풀은 모두 OnAwake가 코드로 생성 — 인스펙터에 미리 배치할 필요 없음)

[신규 AudioMixer 애셋, 예: Assets/Audio/MainMixer.mixer]
└── Master (그룹)
    ├── BGM (하위 그룹)
    └── SFX (하위 그룹)
노출 파라미터 3개: MasterVolume / BGMVolume / SFXVolume (각 그룹의 Volume을 우클릭 → Expose Parameter)
```

`AudioMixer` 애셋과 그 안의 그룹 3개, 노출 파라미터 3개는 Unity 에디터에서 직접 만들어야 한다(코드로 생성 불가) — 파라미터 이름은 반드시 `ChannelParam`이 반환하는 문자열(`"MasterVolume"`/`"BGMVolume"`/`"SFXVolume"`)과 정확히 일치해야 한다.

---

## 이번 범위에서 제외

- **실제 BGM/SFX 클립 목록 확정과 `Resources/Audio/` 파일 배치** — `AudioID`는 값이 비어 있는 채로 시작(`SpriteCategory`가 처음에 그랬던 것과 동일한 상태). 클립이 정해지는 대로 enum 값 추가 + 해당 폴더에 파일 배치
- **`SettingsSystem`과의 저장/복원 연동** — `SettingsSystem` 자체가 미구현. `AudioSystem`은 `SetVolume`/`GetVolume`만 노출하고, 앱 시작 시 저장된 값을 불러와 적용하는 것은 `SettingsSystem` 쪽 책임(추후 `SettingsSystem.OnAwake`에서 `AudioSystem.Instance.SetVolume(...)`를 로드된 값으로 호출)
- **씬별 BGM 매핑("Login 씬은 무슨 곡, MainMenu는 무슨 곡")** — 각 씬 매니저가 자신의 `OnAwake`에서 `AudioSystem.Instance.PlayBGM(...)`를 직접 호출. `AudioSystem`이 `GameState`를 구독해 자동으로 트랙을 고르는 매핑 테이블은 만들지 않는다(결합도 증가 방지 — 새 씬이 추가될 때마다 `AudioSystem`을 함께 고쳐야 하는 구조를 피함)
- **SFX 피치 랜덤화, 3D 공간음향, 볼륨 개별 오버라이드** — 로드맵 요구사항에 없음, 필요해지면 `PlaySFX` 시그니처를 확장
- **`AudioMixer` 스냅샷(뮤직 덕킹 등) 전환** — 지금은 `AudioSource.volume` 트윈으로만 크로스페이드를 구현. 스냅샷 기반 전환이 필요해지면 별도 검토
- **웹/PC 등 모바일 외 플랫폼의 포커스 처리 차이** — 로드맵이 플랫폼을 Android로 명시했으므로 `OnApplicationPause`/`OnApplicationFocus` 조합이 기준

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 이미 재생 중인 BGM을 같은 `AudioID`로 다시 `PlayBGM` 요청 | `_currentBgmId == id` → 조기 반환, 크로스페이드 재시작 없음 |
| 크로스페이드 진행 중 다른 트랙으로 다시 `PlayBGM` 호출 | 진행 중이던 `_bgmFadeInTween`/`_bgmFadeOutTween`을 `Kill()`한 뒤 새 크로스페이드 시작 — 겹쳐서 재생되지 않음 |
| 존재하지 않는 `AudioID`로 `PlayBGM`/`PlaySFX` 호출 | `GetClip`이 `null` + 경고 로그(최초 1회) 반환 → 각 메서드가 조기 반환, 예외 없음 |
| SFX 풀이 전부 재생 중일 때 `PlaySFX` 추가 호출 | `GetIdleSfxSource`가 `null` 반환 → 조용히 드롭(요청 자체가 무시됨, 에러 아님) |
| `PlayBGM`을 한 번도 호출하지 않은 상태에서 `StopBGM` 호출 | `_currentBgmId == null` → 조기 반환, 아무 일도 일어나지 않음 |
| 앱이 백그라운드로 가는 동시에(`AppPauseChanged(true)`) 포커스도 잃음(`AppFocusChanged(false)`) | 두 핸들러 모두 `ApplyMuteState` 호출, `shouldMute`는 두 번 다 `true`로 계산돼 결과는 동일(중복 호출 안전) |
| 뮤트 상태에서 `SetVolume(Master, ...)` 호출 | `_masterVolume01` 필드는 갱신되지만 믹서는 여전히 `-80dB` 유지 → 포커스/일시정지가 복귀하는 시점의 `ApplyMuteState`가 새 값을 정상 반영 |
| `_sfxPoolSize`를 0 이하로 설정 | `BuildSfxPool`이 빈 배열 생성 → `GetIdleSfxSource`가 항상 `null` → 모든 `PlaySFX` 호출이 조용히 드롭(크래시 없음, 다만 SFX가 아예 안 들리므로 구현 시 주의사항에 명시) |
| `_mixer`/`_bgmMixerGroup`/`_sfxMixerGroup` 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |

---

## 테스트 시나리오

`AudioID` 값이 하나 이상 추가되고 실제 클립이 `Resources/Audio/`에 배치된 이후 검증 가능:

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `PlayBGM(AudioID.A)` 최초 호출 | `_bgmSourceA`(또는 B)가 볼륨 0→1로 페이드 인하며 재생 시작, 반대쪽 소스는 정지 상태 유지 |
| 2 | 재생 중 `PlayBGM(AudioID.B)` 호출(다른 트랙) | 기존 소스는 1→0 페이드 아웃 후 정지, 다른 소스는 0→1 페이드 인 — 두 소스가 겹치는 구간 동안 크로스페이드 |
| 3 | 재생 중 `PlayBGM(AudioID.A)`를 같은 트랙으로 다시 호출 | 아무 변화 없음(페이드 재시작 안 됨) |
| 4 | `StopBGM()` 호출 | 현재 활성 소스가 페이드 아웃 후 정지, 이후 `PlayBGM`으로 재시작 가능 |
| 5 | `PlaySFX(AudioID.Click)`를 풀 크기(`_sfxPoolSize`)보다 많이 연속 호출 | 풀 크기만큼만 동시 재생, 초과분은 무음(에러 없음) |
| 6 | `SetVolume(AudioChannel.BGM, 0.5f)` 호출 | `GetVolume(AudioChannel.BGM) == 0.5f`, BGM 그룹 볼륨만 감쇠(SFX/Master는 그대로) |
| 7 | `SetVolume(AudioChannel.Master, 0f)` 호출 | 믹서 `MasterVolume` 파라미터가 `-80dB`로 설정, 전체 무음 |
| 8 | `EventBus.Publish(new AppFocusChanged(false))` | Master 채널이 즉시 뮤트(`-80dB`), `GetVolume(Master)`는 이전 값 그대로 유지 |
| 9 | 시나리오 8 이후 `EventBus.Publish(new AppFocusChanged(true))` | Master 채널이 `LinearToDb(_masterVolume01)`로 복원, 소리가 다시 들림 |
| 10 | `AppPauseChanged(true)`와 `AppFocusChanged(false)`가 동시에 발행됨 | 두 핸들러 모두 뮤트 적용, 하나만 `false`로 돌아와도(`AppPauseChanged(false)`만) 다른 하나가 여전히 `false`(`_hasFocus`)면 계속 뮤트 유지 |
| 11 | 존재하지 않는 `AudioID`로 `PlaySFX` 호출 | 콘솔에 경고 로그 1회, 이후 같은 `AudioID` 재요청 시 로그 추가로 남지 않음(캐시됨) |

---

## 구현 시 주의사항

- **`AudioMixer` 노출 파라미터 이름은 `ChannelParam`이 반환하는 문자열과 정확히 일치해야 한다**: `"MasterVolume"`/`"BGMVolume"`/`"SFXVolume"` — 오타가 있으면 `AudioMixer.SetFloat`이 조용히 실패(예외 없이 아무 효과도 없음)하므로 디버깅이 어렵다. 처음 볼륨 슬라이더를 연결할 때 반드시 콘솔 대신 실제 소리로 확인할 것.
- **`AudioID` 값 이름은 `Resources/Audio/{BGM|SFX}/` 하위 실제 파일명과 문자 그대로 일치시킨다**: `SpriteCategory`와 동일한 함정 — 어긋나면 컴파일 타임에 잡히지 않고 항상 `null` + 경고로 귀결된다.
- **BGM 크로스페이드 중 `_activeBgmSource`/`_inactiveBgmSource`를 직접 참조로 스왑한다**: 인덱스나 bool 플래그로 "어느 쪽이 활성인지"를 따로 추적하면 스왑 타이밍이 어긋날 여지가 생긴다 — 참조 자체를 튜플 스왑(`(a, b) = (b, a)`)하는 편이 실수할 여지가 적다.
- **뮤트는 `_masterVolume01` 필드를 절대 건드리지 않는다**: `ApplyMuteState`가 필드를 바꾸면 포커스 복귀 시 "뮤트 이전 값"을 잃어버려 항상 뮤트 상태로 고정되는 버그가 된다. 뮤트/복원은 오직 `_mixer.SetFloat` 호출로만 이뤄져야 한다.
- **SFX 클립과 BGM 클립 이름을 겹치지 않게 짓는다**: `AudioID`가 카테고리 구분 없는 단일 enum이라, 같은 값 이름을 실수로 BGM/SFX 양쪽 폴더에 서로 다른 클립으로 넣어도 컴파일러가 잡아주지 못한다(폴더가 다르므로 실제로는 충돌 없이 동작은 하지만, 하나의 `AudioID` 값이 맥락에 따라 다른 소리를 가리키게 되어 코드 가독성이 떨어짐).
- **`GetClip`의 `null` 캐시를 인지한다**: 오타로 존재하지 않는 `AudioID`를 넘기면 첫 호출에서만 경고가 뜨고 이후로는 조용히 `null`만 반환된다 — 클립을 나중에 추가해도 이미 실행 중인 프로세스에서는 캐시가 갱신되지 않으므로(에디터 플레이 모드 재시작 필요), 클립 추가 후 테스트할 땐 플레이 모드를 껐다 켤 것.

---

## 구현 후 체크리스트

- [x] `AudioChannel.cs` 작성 (`Assets/Scripts/Core/Audio/`)
- [x] `AudioID.cs` 작성 (빈 enum으로 시작)
- [x] `AudioSystem.cs` 작성 (`Singleton<AudioSystem>` 상속, BGM 크로스페이드/SFX 풀/볼륨 제어/자동 뮤트)
- [ ] `Assets/Resources/Audio/BGM/`, `Assets/Resources/Audio/SFX/` 폴더 생성
- [ ] `AudioMixer` 애셋 신규 생성, Master/BGM/SFX 그룹 3개 + 노출 파라미터 3개(`MasterVolume`/`BGMVolume`/`SFXVolume`) 구성 (Unity 에디터 작업)
- [ ] `GameManagers` 오브젝트(`Logo.unity`)에 `AudioSystem.cs` 부착, `_mixer`/`_bgmMixerGroup`/`_sfxMixerGroup` 인스펙터 연결 (Unity 에디터 작업)
- [ ] 첫 BGM/SFX 클립 확정 후 `AudioID` 값 추가 + `Resources/Audio/` 파일 배치
- [ ] 테스트 시나리오 11개 중 클립이 준비된 범위까지 검증
- [ ] (추후) `SettingsSystem` 구현 시 `SetVolume`/`GetVolume` 연동해 저장/복원 붙이기
- [ ] (추후) 각 씬 매니저에 `PlayBGM` 호출 추가(씬별 BGM 확정 후)
