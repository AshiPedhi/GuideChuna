#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
경추ROM 실측 기준틀 부호 검증.

★왜 있는가 — 2026-09-01에 이 부호를 추론으로 맞히려다 다섯 번 왕복했다.
  한 면을 고치면 다른 면이 딸려 틀어졌는데, 기저 셋이 서로 엮여 있어서였다.
  Play 없이 여기서 전 조합을 돌려 보면 한 번에 끝난다.

Unity 좌표계를 그대로 쓴다 — 왼손계, Cross(right, up) = forward,
Quaternion.AngleAxis는 축을 향해 볼 때 시계 방향.

기준 상황: 환자는 앉아 있고 앞 = +Z, 오른쪽 = +X, 위 = +Y.

  python check.py             전 조합 표
  python check.py --current   지금 코드 설정만 자세히
"""
import math
import sys

UP = (0.0, 1.0, 0.0)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def neg(a):
    return (-a[0], -a[1], -a[2])


def norm(a):
    m = math.sqrt(sum(x * x for x in a))
    return tuple(x / m for x in a)


def rot(v, axis, deg):
    """Unity Quaternion.AngleAxis(deg, axis) * v."""
    a = math.radians(deg)
    c, s = math.cos(a), -math.sin(a)          # 왼손계라 각도 부호가 반대다
    dot = sum(axis[i] * v[i] for i in range(3))
    cr = cross(axis, v)
    return tuple(v[i] * c + cr[i] * s + axis[i] * dot * (1 - c) for i in range(3))


def label(v):
    known = {(0, 0, 1): '환자앞', (0, 0, -1): '환자뒤',
             (1, 0, 0): '환자오른쪽', (-1, 0, 0): '환자왼쪽',
             (0, 1, 0): '위', (0, -1, 0): '아래'}
    return known.get(tuple(round(x) for x in v), str(tuple(round(x, 2) for x in v)))


# 방향별로 +30도 지점이 어느 쪽으로 가야 하는가 (성분, 부호)
EXPECT = {
    '굴곡':   ('z', +1),   # 앞으로 숙인다
    '신전':   ('z', -1),   # 뒤로 젖힌다
    '우측굴': ('x', +1),
    '좌측굴': ('x', -1),
    '우회전': ('x', +1),
    '좌회전': ('x', -1),
}
IDX = {'x': 0, 'y': 1, 'z': 2}


def build(stance, use_l_minus_r, fwd_neg, pair_swapped, transverse_zero_neg=False):
    """한 조합의 기준틀과 방향별 축을 만든다."""
    if stance == '마주':          # 시술자 오른손 -> 환자 왼어깨
        l, r = (0.2, 1.3, 0.0), (-0.2, 1.3, 0.0)
    else:                         # 뒤     시술자 오른손 -> 환자 오른어깨
        l, r = (-0.2, 1.3, 0.0), (0.2, 1.3, 0.0)

    ref_right = norm(tuple(l[i] - r[i] for i in range(3))) if use_l_minus_r \
        else norm(tuple(r[i] - l[i] for i in range(3)))

    ref_fwd = cross(ref_right, UP)
    if fwd_neg:
        ref_fwd = neg(ref_fwd)
    ref_up = UP                   # ★언제나 월드 수직. 외적으로 뽑으면 전후축에 딸려 뒤집힌다.

    aR, aU, aF = ref_right, ref_up, ref_fwd
    if not pair_swapped:
        axes = {'굴곡': neg(aR), '신전': aR,
                '우측굴': aF, '좌측굴': neg(aF),
                '우회전': neg(aU), '좌회전': aU}
    else:
        axes = {'굴곡': aR, '신전': neg(aR),
                '우측굴': neg(aF), '좌측굴': aF,
                '우회전': aU, '좌회전': neg(aU)}

    # 각도기의 0도 — 회전이면 Torso.forward, 나머지는 Torso.up.
    # ★Torso.forward는 refFwd와 <별개로> 뒤집을 수 있다. 관상면 축은 axFwd를 쓰므로
    #   여기만 뒤집으면 횡단면 0도만 바뀌고 다른 면은 안 건드린다.
    torso_fwd = neg(ref_fwd) if transverse_zero_neg else ref_fwd
    return ref_right, ref_up, ref_fwd, torso_fwd, axes


def failures(stance, use_l, fwd_neg, swapped, tz_neg=False):
    ref_right, ref_up, ref_fwd, torso_fwd, axes = build(
        stance, use_l, fwd_neg, swapped, tz_neg)
    bad = []
    for d, ax in axes.items():
        zero = torso_fwd if '회전' in d else ref_up
        p = rot(zero, ax, 30.0)
        comp, sign = EXPECT[d]
        if p[IDX[comp]] * sign <= 0.05:
            bad.append(d)
    if torso_fwd[2] <= 0:
        bad.append('횡단0°(뒤)')
    return bad


def table():
    print('기준: 환자 앞=+Z · 오른쪽=+X · 위=+Y\n')
    print(f"     {'시술자':4s} {'refRight':9s} {'refFwd':8s} {'짝':5s}  틀린 항목")
    for stance in ('마주', '뒤'):
        for use_l in (True, False):
            for fwd_neg in (False, True):
                for swapped in (False, True):
                    bad = failures(stance, use_l, fwd_neg, swapped)
                    tag = 'OK  ' if not bad else '    '
                    print(f"{tag} {stance:4s} {'l-r' if use_l else 'r-l':9s} "
                          f"{'-cross' if fwd_neg else 'cross':8s} "
                          f"{'swap' if swapped else 'base':5s}  "
                          f"{'전부 맞음' if not bad else ', '.join(bad)}")
    print('\n★ OK가 두 줄뿐이고 둘 다 refFwd=cross · 짝=base다.')
    print('  갈리는 건 시술자가 어디 서느냐 하나뿐이다 — 그게 refRight의 부호를 정한다.')
    print('  refFwd를 뒤집으면 측굴·회전 네 방향이 같이 깨진다(위 표의 -cross 줄).')
    print('  횡단면 0°만 바꾸려면 Torso.forward만 뒤집어야 한다 — --current로 확인할 것.')


def current(stance='뒤', use_l=False, fwd_neg=False, swapped=False, tz_neg=False):
    ref_right, ref_up, ref_fwd, torso_fwd, axes = build(
        stance, use_l, fwd_neg, swapped, tz_neg)
    print(f'시술자 위치: 환자 {stance}')
    print(f'  refRight = {label(ref_right)}   (기대: 환자오른쪽)')
    print(f'  refUp    = {label(ref_up)}   (기대: 위)')
    print(f'  refFwd   = {label(ref_fwd)}   (기대: 환자앞)')
    print(f'  각도기 0°(회전) = {label(torso_fwd)}   (기대: 환자앞)\n')
    print('  방향별 +30° 지점')
    for d, ax in axes.items():
        zero = torso_fwd if '회전' in d else ref_up
        p = rot(zero, ax, 30.0)
        comp, sign = EXPECT[d]
        ok = 'OK ' if p[IDX[comp]] * sign > 0.05 else '★틀림'
        print(f'    {d:5s} 0°={label(zero):9s} '
              f'+30°=({p[0]:+.2f},{p[1]:+.2f},{p[2]:+.2f})  {ok}')


if __name__ == '__main__':
    if '--current' in sys.argv:
        # 지금 코드 설정. 바뀌면 여기도 같이 고친다.
        current(stance='뒤', use_l=False, fwd_neg=False, swapped=False, tz_neg=False)
    else:
        table()
