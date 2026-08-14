using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 두개골 부위별 색상을 입힐 때 <b>원래 머티리얼을 기억해 두는 그릇</b>.
///
/// 런타임 동작은 없다(Update 없음). 에디터 도구 <c>GuideChuna/두개골 부위별 색상</c>이
/// 색을 입히면서 여기에 (렌더러 → 원래 머티리얼)을 적어 두고, [되돌리기]가 이걸 읽어 복원한다.
///
/// ★프로젝트 에셋(ScriptableObject)에는 씬 오브젝트 참조를 담을 수 없어서 컴포넌트로 둔다.
/// ★이 프로젝트에서 에디터 도구가 사용자 수작업을 날린 사고가 있었다 —
///   그래서 색상 적용은 Ctrl+Z 말고도 항상 되돌릴 수 있게 원본을 남긴다.
/// </summary>
[DisallowMultipleComponent]
public class SkullBoneColorizer : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string part;               // 부위 이름(전두골 등) — 사람이 읽는 용도
        public Renderer renderer;
        public Material[] original;       // 색을 입히기 전의 sharedMaterials
    }

    [Tooltip("색을 입힌 렌더러와 그 원래 머티리얼. [되돌리기]가 이걸 사용한다.")]
    public List<Entry> entries = new List<Entry>();

    [Tooltip("현재 부위별 색상이 적용된 상태인가")]
    public bool applied;
}
