using System;
using UnityEngine;

namespace JungleDice.InGame
{
    // Canvas 루트에 부착 — hand 패널처럼 화면 크기에 반응해야 하는 컴포넌트에 이벤트로 알린다.
    // hand 자신이 아니라 Canvas 루트에 붙여야 한다: hand의 sizeDelta를 코드로 바꾸면 그 변경이 다시 hand 자신의
    // OnRectTransformDimensionsChange를 발동시켜 재귀 호출 위험이 생기지만, Canvas 루트 크기는 화면 해상도에 의해서만 바뀐다.
    public class ScreenSizeChangeNotifier : MonoBehaviour
    {
        public event Action OnScreenSizeChanged;

        private void OnRectTransformDimensionsChange() => OnScreenSizeChanged?.Invoke();
    }
}
