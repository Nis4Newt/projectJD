using System.Collections;
using UnityEngine;
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Logo
{
    public class LogoSceneManager : SceneSingleton<LogoSceneManager>
    {
        [SerializeField] private float _minSplashDuration = 1.5f;

        protected override void OnAwake()
        {
            StartCoroutine(LogoRoutine());
        }

        private IEnumerator LogoRoutine()
        {
            // 스플래시/로고 연출, 버전 표시 등 Logo 씬 전용 연출
            yield return new WaitForSeconds(_minSplashDuration);

            // 준비 완료를 이벤트로만 알림 — GameManager를 직접 참조하지 않음
            EventBus.Publish(new LogoSceneReady());
        }
    }
}
