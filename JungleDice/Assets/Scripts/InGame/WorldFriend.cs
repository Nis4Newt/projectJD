using DG.Tweening;
using JungleDice.Core.Sprites;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;

namespace JungleDice.InGame
{
    // 필드에 배치되는 친구카드의 world space 버전 — Friend(UI)와 로직은 동일하고 렌더링 컴포넌트만 다르다.
    // Friend는 MainMenu 덱 미리보기(FriendDeckDisplay)에서 별도로 쓰이고 있어 그대로 두고, 필드는 이 컴포넌트를 쓴다.
    public enum FriendState { Spawn, Idle, Attack, Dead, Destroying }

    public class WorldFriend : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _cardRenderer;
        [SerializeField] private TextMeshPro _attText;
        [SerializeField] private TextMeshPro _hpText;
        [SerializeField] private ParticleSystem _highlightRenderer; // 카드 전체를 덮는 하이라이트 오버레이(파티클), 기본 비활성화
        [SerializeField] private ParticleSystemRenderer _highlightRendererMaterial; // _highlightRenderer와 같은 오브젝트 — 베이스맵 텍스처 제어용
        [SerializeField] private float _deathShakeDuration = 0.2f;
        [SerializeField] private float _deathShakeStrength = 0.15f;

        private void Awake()
        {
            SetHighlight(false, Color.clear); // 프리팹 기본 상태가 활성화라 코드에서 명시적으로 꺼둔다
        }

        public FriendState State { get; private set; } = FriendState.Spawn;

        public int Key { get; private set; }
        public int Att { get; private set; }
        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }
        public bool HasShield { get; private set; }
        public bool HasRevived { get; private set; }
        public bool IsDead => CurrentHp <= 0;

        // 사망 시 새 카드를 낳는 예약(부활/포자감염 공용) — key/att/hp는 spawn+key,att=n,hp=n 조각에서 옴
        public SpawnMarkInfo SpawnMark { get; } = new SpawnMarkInfo();

        public class SpawnMarkInfo
        {
            public bool HasMark { get; private set; }
            public int Key { get; private set; }
            public int Att { get; private set; }
            public int Hp { get; private set; }

            public void Set(int key, int att, int hp)
            {
                HasMark = true;
                Key = key;
                Att = att;
                Hp = hp;
            }
        }

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null)
            {
                State = FriendState.Idle; // CardTable.Get이 이미 LogError를 남김 — 상태 전이는 그대로 진행
                return;
            }

            Att = data.att;
            CurrentHp = data.hp;
            MaxHp = data.hp;

            _cardRenderer.sprite = SpriteManager.GetCard(key.ToString());
            SetHighlightTexture(key);

            _attText.text = Att.ToString();
            _attText.color = Color.white;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = Color.white;

            State = FriendState.Idle; // 스폰 연출 없음 — 즉시 전이. 연출이 생기면 이 한 줄만 지연시키면 됨
        }

        public void EnterAttack() => State = FriendState.Attack;
        public void EnterIdle() => State = FriendState.Idle;

        // 사망 확정된 카드를 파괴한다 — 이미 Dead/Destroying이면 무시(같은 프레임에 중복 호출되는 것을 방어)
        public void Die()
        {
            if (State == FriendState.Dead || State == FriendState.Destroying) return;

            State = FriendState.Dead;
            transform.DOKill();
            transform.DOShakePosition(_deathShakeDuration, _deathShakeStrength)
                .OnComplete(EnterDestroying);
        }

        private void EnterDestroying()
        {
            State = FriendState.Destroying;
            Destroy(gameObject);
        }

        public void TakeDamage(int amount)
        {
            if (HasShield)
            {
                HasShield = false;
                return; // 이번 피해 전부 무효, 텍스트/색 변화 없음
            }

            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp - amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void MergeWith(int addAtt, int addHp)
        {
            int previousAtt = Att;
            int previousHp = CurrentHp;

            Att += addAtt;
            CurrentHp += addHp;
            MaxHp += addHp;

            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previousAtt);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void DoubleAtt()
        {
            int previousAtt = Att;
            Att *= 2;
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previousAtt);
        }

        public void AddAtt(int amount)
        {
            int previous = Att;
            Att = Mathf.Max(0, Att + amount);
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        public void MultiplyAtt(int factor)
        {
            int previous = Att;
            Att *= factor;
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        public void DivideAtt(int divisor)
        {
            int previous = Att;
            Att = Mathf.Max(0, Att / divisor);
            _attText.text = Att.ToString();
            _attText.color = GetStatColor(Att, previous);
        }

        // 스탯 증감(성장/저하) — MaxHp도 같이 바뀜. 전투 피해(TakeDamage)·회복(Heal)과 달리 방어막과 무관하고 최대치 자체가 변한다
        public void AddHp(int amount)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp + amount);
            MaxHp = Mathf.Max(1, MaxHp + amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        // 고정량 회복 — MaxHp를 넘지 않고, MaxHp 자체는 바꾸지 않는다(성장인 AddHp와 구분)
        public void Heal(int amount)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void MultiplyHp(int factor)
        {
            int previousHp = CurrentHp;
            CurrentHp *= factor;
            MaxHp *= factor;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void DivideHp(int divisor)
        {
            int previousHp = CurrentHp;
            CurrentHp = Mathf.Max(0, CurrentHp / divisor);
            MaxHp = Mathf.Max(1, MaxHp / divisor);
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void HealToMax()
        {
            int previousHp = CurrentHp;
            CurrentHp = MaxHp;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = GetStatColor(CurrentHp, previousHp);
        }

        public void AddShield() => HasShield = true; // 이미 있어도 그대로 유지(스택 없음)

        public void ApplySpawnMark(int key, int att, int hp) => SpawnMark.Set(key, att, hp);

        // 사망 시 1회 부활(CardCondition.Die) — att/hp는 자신의 effect(spawn+key,att=n,hp=n)에서 읽어온 값
        public bool TryRevive(int att, int hp)
        {
            if (HasRevived) return false;
            HasRevived = true;
            OverrideStats(att, hp);
            return true;
        }

        // SetKey로 채워진 기본 스탯을 명시적인 값으로 덮어쓴다 — 부활/포자감염처럼 카드 기본값이 아닌 수치로 등장할 때 사용
        public void OverrideStats(int att, int hp)
        {
            Att = att;
            CurrentHp = hp;
            MaxHp = hp;
            _attText.text = Att.ToString();
            _attText.color = Color.white;
            _hpText.text = CurrentHp.ToString();
            _hpText.color = Color.white;
        }

        // 직전 값 대비로 판정 — 오르면 초록, 떨어지면 빨강, 변화 없으면 흰색(최초값 같은 고정 기준값과 비교하지 않음)
        private static Color GetStatColor(int current, int previous)
        {
            if (current == previous) return Color.white;
            return current > previous ? Color.green : Color.red;
        }

        // 파티클 vertex color(main.startColor)는 이 프로젝트의 파티클 머티리얼 조합에서 렌더링에 반영되지 않아,
        // 색상은 SetHighlightTexture와 동일하게 머티리얼의 _BaseColor로 직접 적용한다.
        public void SetHighlight(bool on, Color color)
        {
            _highlightRendererMaterial.material.SetColor("_BaseColor", color);
            _highlightRenderer.gameObject.SetActive(on);
        }

        // 카드별 하이라이트 이미지를 파티클 머티리얼 베이스맵에 적용 — {key}_shadow 리소스가 없으면 경고 로그만 남고 베이스맵은 비워짐
        private void SetHighlightTexture(int key)
        {
            var shadowSprite = SpriteManager.GetCard($"{key}_shadow");
            _highlightRendererMaterial.material.SetTexture("_BaseMap", shadowSprite != null ? shadowSprite.texture : null);
        }

        // vibrato를 1로 둬 "커졌다 바로 돌아오는" 단일 펀치로 — 기본값(10)은 여러 번 진동해 목적에 맞지 않음
        public void PunchScale(float strength, float duration)
        {
            transform.DOKill();
            transform.DOPunchScale(Vector3.one * strength, duration, vibrato: 1, elasticity: 0.3f);
        }

        public void MoveTo(Vector3 worldPosition, float duration, Ease ease)
        {
            transform.DOKill();
            worldPosition.z = transform.position.z; // 깊이는 SnapDepthToParent가 정한 값을 그대로 유지 — 이동은 x/y만
            transform.DOMove(worldPosition, duration).SetEase(ease);
        }

        public void SetParent(Transform parent)
        {
            transform.SetParent(parent, worldPositionStays: true);
            SnapDepthToParent();
        }

        // 슬롯/공격 레이어 등 어디에 붙든 parent보다 z -1만큼 앞(카메라 쪽)에 그려지도록 강제
        public void SnapDepthToParent()
        {
            var localPosition = transform.localPosition;
            localPosition.z = -1f;
            transform.localPosition = localPosition;
        }
    }
}
