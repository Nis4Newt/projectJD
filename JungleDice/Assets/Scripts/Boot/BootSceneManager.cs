using System.Collections;
using UnityEngine;
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Boot
{
    public class BootSceneManager : SceneSingleton<BootSceneManager>
    {
        [SerializeField] private float _minSplashDuration = 1.5f;

        protected override void OnAwake()
        {
            StartCoroutine(BootRoutine());
        }

        private IEnumerator BootRoutine()
        {
            // 스플래시/로고 연출, 버전 표시 등 Bootstrap 씬 전용 연출
            yield return new WaitForSeconds(_minSplashDuration);

            // 준비 완료를 이벤트로만 알림 — GameManager를 직접 참조하지 않음
            EventBus.Publish(new BootSceneReady());
        }
    }
}
