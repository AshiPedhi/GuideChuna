# 미사용 스크립트 후보 스캔 (2026-08-31 신설)
#
# 실행:
#   PYTHONIOENCODING=utf-8 "C:/Users/USER/AppData/Local/Python/pythoncore-3.14-64/python.exe" \
#       .claude/tools/deadscan.py
#   (bash의 python은 스토어 스텁이라 깨진다. 풀경로를 쓸 것.)
#
# ★판정은 두 조건을 <b>둘 다</b> 봐야 한다. 하나만 보면 틀린다.
#   1) 씬·프리팹·에셋에 그 스크립트의 guid가 없다
#   2) 다른 .cs에서 클래스 이름을 부르지 않는다 (AddComponent<T>·FindFirstObjectByType<T> 포함)
#
#   씬 검색만 하면 <b>런타임에 AddComponent로 붙는 것</b>을 죽었다고 오판한다.
#   실제 사례: CervicalRomHandAngleProbe는 씬에도 프리팹에도 없지만
#   CervicalRomScenarioBridge가 런타임에 붙인다. 살아 있는 코드다.
#
# ★에디터 전용(MenuItem·CustomEditor·빌드 후처리)은 원래 guid·이름 참조가 안 잡힌다.
#   전부 살아 있는 코드다. 아래에서 C 그룹으로 따로 분류하는 이유다.

import os
import re
import io

ROOT = 'Assets'

scripts = {}
for root, dirs, files in os.walk(ROOT):
    for f in files:
        if not f.endswith('.cs'):
            continue
        p = os.path.join(root, f)
        meta = p + '.meta'
        if not os.path.exists(meta):
            continue
        ms = io.open(meta, encoding='utf-8', errors='ignore').read()
        g = re.search(r'guid:\s*([0-9a-f]{32})', ms)
        if not g:
            continue
        s = io.open(p, encoding='utf-8', errors='ignore').read()
        cls = os.path.splitext(f)[0]
        m = re.search(r'\bclass\s+' + re.escape(cls) + r'\b\s*:\s*([A-Za-z0-9_.<>, ]+)', s)
        if not m:
            continue
        scripts[g.group(1)] = (cls, p, m.group(1).strip(), s)

used = set()
for root, dirs, files in os.walk('.'):
    if any(x in root for x in ('Library', '.git', '_backup', '_Recovery')):
        continue
    for f in files:
        if not f.endswith(('.unity', '.prefab', '.asset', '.controller', '.playable')):
            continue
        try:
            s = io.open(os.path.join(root, f), encoding='utf-8', errors='ignore').read()
        except Exception:
            continue
        used.update(re.findall(r'guid:\s*([0-9a-f]{32})', s))

allcode = {}
for root, dirs, files in os.walk(ROOT):
    for f in files:
        if f.endswith('.cs'):
            p = os.path.join(root, f)
            allcode[p] = io.open(p, encoding='utf-8', errors='ignore').read()

groups = {'runtime': [], 'editor': [], 'external': [], 'so': []}
for guid, (cls, path, base, src) in sorted(scripts.items(), key=lambda x: x[1][0]):
    if guid in used:
        continue
    if any(re.search(r'\b' + re.escape(cls) + r'\b', s) for p, s in allcode.items() if p != path):
        continue

    norm = path.replace('\\', '/')
    is_editor = (('/Editor/' in norm) or ('MenuItem' in src) or ('CustomEditor' in src)
                 or base in ('Editor', 'EditorWindow')
                 or 'IPostGenerateGradleAndroidProject' in base
                 or 'ScriptableWizard' in base)
    ext = ('Samples' in norm) or ('PackageCache' in norm)

    if ext:
        groups['external'].append((cls, path, base))
    elif is_editor:
        groups['editor'].append((cls, path, base))
    elif 'ScriptableObject' in base:
        groups['so'].append((cls, path, base))
    else:
        groups['runtime'].append((cls, path, base))


def show(title, items, note):
    print('\n===== %s (%d건) =====' % (title, len(items)))
    if note:
        print(note)
    for cls, path, base in items:
        sz = os.path.getsize(path) // 1024
        print('  %-32s %-24s %3dKB  %s' % (cls, base, sz, path))


show('A. 런타임 MonoBehaviour — 진짜 미사용 후보', groups['runtime'],
     '씬·프리팹에 없고 코드에서도 안 부른다. 파일 최상단에 [미사용] 배너를 달아 둘 것.')
show('B. ScriptableObject — 확인 필요', groups['so'],
     '.asset이 없으면 미사용. Resources.Load 문자열로 부르는지는 이 스캔이 못 잡는다.')
show('C. 에디터 전용 — 살아 있는 코드', groups['editor'],
     'MenuItem/CustomEditor/빌드 후처리라 참조가 원래 안 잡힌다. 지우지 말 것.')
show('D. 외부 샘플 — 건드리지 말 것', groups['external'], '')
