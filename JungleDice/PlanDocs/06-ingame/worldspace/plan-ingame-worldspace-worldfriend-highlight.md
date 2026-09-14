# WorldFriend 하이라이트 파티클 전환 계획

> 상위 문서: [WorldFriend 신설 계획](plan-ingame-worldspace-worldfriend.md) — 그 문서가 만든 `_highlightRenderer`(`SpriteRenderer`) 오버레이 방식을 파티클 시스템 기반으로 교체하는 후속 개선
> 의존 관계: `JungleDice.Core.Sprites.SpriteManager`(`{key}_shadow` 스프라이트 로드)
> 범위: `WorldFriend._highlightRenderer`의 렌더링 방식을 `SpriteRenderer` 색상 오버레이 → `ParticleSystem` 기반으로 교체한다. `SetHighlight(bool, Color)` 공개 시그니처는 그대로 유지하고, `SetKey(int)`에 카드별 하이라이트 이미지(`{key}_shadow`) 적용을 추가한다. `Friend.cs`(UGUI 버전)는 대상이 아니다 — 그대로 `Image` 오버레이를 쓴다.

---

## 배경

현재 작업 트리에는 이 개선을 시도한 흔적(`WorldFriend.prefab`에 파티클 컴포넌트가 루트로 추가됨, `WorldFriend_new.prefab`/`WorldFriend_old.prefab` 실험본, `Assets/Material/` 아래 임시 머티리얼·셰이더그래프)이 남아있다. 이 문서는 그 실험을 정리해 최종 구조를 확정하기 위한 것이다.

---

## 설계 목표

- `SetHighlight(bool on, Color color)` 시그니처는 그대로 — 호출부(`InGameSceneManager.cs` 6곳)를 건드리지 않는다
- 색상·이미지 모두 `ParticleSystemRenderer`의 머티리얼(`_BaseColor`/`_BaseMap`)로 제어 — 두 컴포넌트는 같은 자식 오브젝트에 붙는다(파티클 vertex color인 `main.startColor`는 이 프로젝트의 셰이더 조합에서 렌더링에 반영되지 않아 채택하지 않음, 아래 결정 2 참고)
- 카드마다 다른 하이라이트 이미지를 쓰므로 머티리얼은 인스턴스화해서 쓴다(공유 에셋을 직접 고치지 않는다)
- 하이라이트 이미지가 없는 카드(`{key}_shadow` 리소스 부재)는 `SpriteManager`가 이미 남기는 경고 로그로 충분 — 별도 예외 처리를 추가하지 않는다

---

## 핵심 설계 결정

### 1. `_highlightRenderer` 타입 교체: `SpriteRenderer` → `ParticleSystem` + `ParticleSystemRenderer` 필드 추가

```csharp
[SerializeField] private ParticleSystem _highlightRenderer;                 // 하이라이트 파티클, 기본 비활성화
[SerializeField] private ParticleSystemRenderer _highlightRendererMaterial; // 위와 같은 오브젝트 — 베이스맵 텍스처 제어용
```

두 필드 모두 같은 자식 오브젝트("HighLighter")를 가리킨다. `ParticleSystem`과 `ParticleSystemRenderer`는 같은 GameObject에 붙는 별개 컴포넌트라 `GetComponent`로 런타임에 찾기보다, 다른 필드들과 동일하게 Inspector에서 직접 연결한다(기존 코드가 초기화 메서드 없이 전부 `[SerializeField]`로 연결하는 관례를 그대로 따름).

### 2. 색상 변경: 파티클 vertex color 대신 머티리얼 `_BaseColor`

처음엔 `ParticleSystem.MainModule.startColor`(파티클 vertex color)로 시도했으나, 실기 테스트 결과 코드로 바꾸든 Inspector에서 직접 바꾸든 렌더링에 전혀 반영되지 않았다 — URP 파티클 셰이더의 `MixParticleColor`가 vertex color를 `_BaseColor`와 곱하는 구조([Particles.hlsl](../../../../Library/PackageCache/com.unity.render-pipelines.universal/ShaderLibrary/Particles.hlsl))라 이론상으로는 되어야 하지만, 이 프로젝트의 파티클 렌더러 vertex stream/셰이더 조합에서는 vertex color가 셰이더까지 전달되지 않는 것으로 확인됐다(원인을 셰이더 안까지 들어가 고치기보다, 이미 동작이 검증된 머티리얼 프로퍼티 경로로 우회). 그래서 색상도 `SetHighlightTexture`와 동일하게 머티리얼 프로퍼티(`_BaseColor`)를 직접 설정하는 방식으로 바꿨다.

```csharp
public void SetHighlight(bool on, Color color)
{
    _highlightRendererMaterial.material.SetColor("_BaseColor", color);
    _highlightRenderer.gameObject.SetActive(on);
}
```

`SetActive`로 껐다 켜는 기존 방식은 유지 — 파티클은 `playOnAwake: 1`이라 `OnEnable`마다 자동 재생되므로, 공격 연출 중 하이라이트가 켜졌다 꺼졌다 반복되는 기존 호출 패턴(`InGameSceneManager.cs`)과 그대로 맞는다.

### 3. 이미지 변경: `SetKey`에서 `{key}_shadow` 로드 후 베이스맵에 적용

```csharp
_highlightRendererMaterial.material.SetTexture("_BaseMap", SpriteManager.GetCard($"{key}_shadow")?.texture);
```

- `SpriteManager.GetCard`는 `Resources/Sprite/Card/{name}`을 로드하는 기존 메서드를 그대로 재사용 — `{key}_shadow`도 같은 폴더에 놓는다(새 카테고리를 만들지 않음)
- `.material`(공유 에셋이 아닌 인스턴스)을 통해 접근 — Unity가 최초 접근 시 자동으로 머티리얼을 복제해주므로, 필드에 놓인 여러 `WorldFriend`가 서로 다른 카드 이미지를 가져도 프리팹의 공유 머티리얼 에셋이 오염되지 않는다
- 스프라이트가 없으면(`{key}_shadow` 리소스 부재) `SpriteManager`가 경고 로그를 남기고 `null`을 반환 — `SetTexture`에 `null`을 넘기면 베이스맵이 비워질 뿐 예외는 나지 않으므로 별도 null 체크 없이 그대로 둔다
- 프로젝트가 URP를 쓰므로 셰이더 프로퍼티명은 `_BaseMap`(Built-in RP였다면 `_MainTex`) — 실험 중이던 `Assets/Material/New Material.mat`도 `_BaseMap`/`_MainTex`를 모두 노출하는 URP 파티클 셰이더였다

---

## 클래스 구조

```
WorldFriend : MonoBehaviour
├── SetKey(int)                          ← _highlightRendererMaterial 베이스맵 설정 추가
├── SetHighlight(bool, Color)            ← 색상 대입 대상만 startColor로 교체, 시그니처 동일
├── _highlightRenderer : ParticleSystem [SerializeField]          (기존 SpriteRenderer 대체)
└── _highlightRendererMaterial : ParticleSystemRenderer [SerializeField]  (신규)
```

---

## 파일 구성

```
Assets/Scripts/InGame/WorldFriend.cs   ← 기존 파일 수정 (필드 타입 교체, SetKey/SetHighlight 수정)
Assets/Prefabs/WorldFriend.prefab      ← 기존 파일 수정 (아래 씬 구성 참고)
Assets/Resources/Sprite/Card/{key}_shadow.png  ← 카드별로 추후 추가(에셋 작업, 이 문서 범위 밖)
```

---

## Unity 씬/오브젝트 구성 (`WorldFriend.prefab`)

```
[Assets/Prefabs/WorldFriend.prefab]
└── WorldFriend (루트) — Transform + WorldFriend.cs
    ├── Image   — SpriteRenderer(카드 스프라이트) → _cardRenderer
    ├── Att     — SpriteRenderer(공격력 아이콘) / txt → _attText
    ├── Hp      — SpriteRenderer(체력 아이콘) / txt → _hpText
    └── HighLighter — ParticleSystem + ParticleSystemRenderer → _highlightRenderer / _highlightRendererMaterial
```

`Square`(기존 `SpriteRenderer` 하이라이트 오브젝트)는 제거하고 `HighLighter`로 대체한다. 현재 작업 트리에 남아있는 `WorldFriend_new.prefab`(이 구조와 동일)을 기준으로 정리하고, `WorldFriend_old.prefab`과 `WorldFriend.prefab`에 루트로 잘못 추가된 `ParticleSystem`(커밋되지 않은 실험)은 삭제한다. `Assets/Material/`의 임시 머티리얼(`New Material*.mat`, `New Shader Graph.shadergraph` 등) 중 실제로 채택한 것만 `HighLighter`의 `ParticleSystemRenderer`에 연결하고 나머지는 정리한다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `{key}_shadow` 리소스가 아직 없는 카드 | `SpriteManager`가 경고 로그만 남기고 베이스맵 미설정 — 새 카드 이미지가 추가되면 자동으로 반영됨 |
| 공격 연출 중 `SetHighlight`가 빠르게 on→off 반복 호출 | `playOnAwake: 1` + `SetActive` 조합으로 매번 처음부터 재생 — 기존 스프라이트 오버레이의 "즉시 표시/숨김"과 체감상 동일 |
| 같은 프리팹에서 파생된 여러 `WorldFriend`가 서로 다른 하이라이트 이미지를 가짐 | `.material`(인스턴스) 접근이라 서로 영향 없음 — `.sharedMaterial`을 쓰면 안 됨 |
| 카드 스프라이트가 아틀라스로 패킹된 경우 | 이 프로젝트는 `Sprite/Card/` 스프라이트를 아틀라스로 패킹하지 않음(개별 텍스처) — `sprite.texture`가 곧 해당 이미지 전체이므로 UV 왜곡 없음. 추후 아틀라스를 도입하면 이 방식이 깨지므로 재검토 필요 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `SetKey(1001)` 호출, `Resources/Sprite/Card/1001_shadow.png` 존재 | `HighLighter`의 머티리얼 베이스맵이 해당 텍스처로 바뀜 |
| 2 | `SetKey(9999)` 호출, `9999_shadow` 리소스 없음 | 콘솔에 `SpriteManager` 경고 로그, 예외 없이 진행 |
| 3 | `SetHighlight(true, Color.red)` → `SetHighlight(false, Color.clear)` | 파티클이 빨간색으로 재생되었다가 비활성화됨 |
| 4 | 같은 필드에 서로 다른 key를 가진 카드 2장 배치 | 두 카드의 하이라이트 이미지가 서로 다르게 표시(머티리얼 인스턴스 분리 확인) |

---

## 구현 시 주의사항

- `Friend.cs`(UGUI 버전)는 건드리지 않는다 — 이번 변경은 `WorldFriend` 전용이다.
- `_highlightRendererMaterial.material`은 **읽을 때마다** 인스턴스를 새로 만들지 않는다(Unity가 최초 접근 시 1회만 복제해 캐싱) — 매 프레임 호출하는 게 아니라 `SetKey` 시점에만 접근하므로 문제 없다.
- 정리 대상 스크래치 파일: `WorldFriend_new.prefab`, `WorldFriend_old.prefab`(둘 다 untracked), `WorldFriend.prefab`에 루트로 붙은 실험용 `ParticleSystem`, `Assets/Material/`의 미채택 임시 에셋. 최종 구조를 `HighLighter` 자식 오브젝트로 확정한 뒤 함께 정리한다.

---

## 구현 후 체크리스트

- [x] `WorldFriend.cs`: `_highlightRenderer` 타입을 `ParticleSystem`으로 교체, `_highlightRendererMaterial`(`ParticleSystemRenderer`) 필드 추가
- [x] `SetHighlight`: `main.startColor` 대입으로 교체
- [x] `SetKey`: `{key}_shadow` 로드 후 베이스맵 적용 추가
- [x] `WorldFriend.prefab`: `Square` → `HighLighter`(ParticleSystem+Renderer) 구조로 정리, 루트 실험용 컴포넌트 제거
- [x] 스크래치 프리팹/머티리얼(`WorldFriend_new/_old.prefab`, 미채택 `Assets/Material/*`) 정리 — 실제 채택된 `New Material.mat`/`1001highlight.png`만 남김
- [ ] 테스트 시나리오 검증 — Unity 에디터에서 직접 플레이 확인 필요(이 세션에서는 미실행)
- [ ] (추후) 카드별 `{key}_shadow` 이미지 에셋 제작 — 이 문서 범위 밖
