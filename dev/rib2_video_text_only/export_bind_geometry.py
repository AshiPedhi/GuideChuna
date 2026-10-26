"""Read only Meta prefab bind transforms. Never reads motion/tracking data."""
from pathlib import Path
import hashlib
import json
import math
import re

PROJECT = Path(__file__).resolve().parents[2]
PACKAGE = PROJECT / 'Library/PackageCache/com.meta.xr.sdk.interaction@33d664ec3528/Runtime/Prefabs/Hands'
OUTPUT = PROJECT / 'Captures/Rib2VideoTextOnly_V0.1_20261026/rig_bind_geometry.json'


def add(a, b):
    return [x + y for x, y in zip(a, b)]


def sub(a, b):
    return [x - y for x, y in zip(a, b)]


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def unit(a):
    n = math.sqrt(sum(x*x for x in a))
    return [x/n for x in a]


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx,
            aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz]


def rotate(q, v):
    q = unit(q)
    return qmul(qmul(q, v + [0]), [-q[0], -q[1], -q[2], q[3]])[:3]


def vec(body, key, components):
    result = re.search(r'^  ' + key + r': \{([^}]+)\}', body, re.M)
    if result is None:
        raise ValueError(key)
    values = dict(re.findall(r'([xyzw]):[ \t]*([^,}\s]+)', result.group(1)))
    return [float(values[c]) for c in components]


def extract(path):
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    blocks = {int(m.group(2)): (int(m.group(1)), m.group(3))
              for m in re.finditer(r'^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}
    hand = next(body for typ, body in blocks.values() if '_openXRJointTransforms:' in body)
    root_id = int(re.search(r'_openXRRoot: \{fileID: (\d+)\}', hand).group(1))
    joint_section = re.search(r'_openXRJointTransforms:\n((?:  - \{fileID: \d+\}\n)+)', hand).group(1)
    ids = [int(v) for v in re.findall(r'fileID: (\d+)', joint_section)]
    if len(ids) != 26:
        raise ValueError('Expected 26 OpenXR joints')
    transforms = {}
    for fid, (typ, body) in blocks.items():
        if typ != 4:
            continue
        goid = int(re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1))
        parent = int(re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1))
        name = re.search(r'^  m_Name:[ \t]*(.*)$', blocks[goid][1], re.M).group(1)
        transforms[fid] = dict(name=name, parent=parent, p=vec(body, 'm_LocalPosition', 'xyz'),
                               q=vec(body, 'm_LocalRotation', 'xyzw'), s=vec(body, 'm_LocalScale', 'xyz'))

    cache = {root_id: ([0., 0., 0.], [0., 0., 0., 1.], [1., 1., 1.])}

    def root_pose(fid):
        if fid not in cache:
            item = transforms[fid]
            pp, pq, ps = root_pose(item['parent'])
            pos = add(pp, rotate(pq, [x*y for x, y in zip(ps, item['p'])]))
            cache[fid] = (pos, qmul(pq, item['q']), [x*y for x, y in zip(ps, item['s'])])
        return cache[fid]

    joints = []
    for i, fid in enumerate(ids):
        item = transforms[fid]
        p, q, s = root_pose(fid)
        joints.append(dict(index=i, fileID=fid, name=item['name'],
                           parentIndex=ids.index(item['parent']) if item['parent'] in ids else -1,
                           parentIsOpenXRRoot=item['parent'] == root_id,
                           localPosition=item['p'], localRotation=item['q'], localScale=item['s'],
                           rootPosition=p, rootRotation=q, rootScale=s,
                           rootBasis={axis: rotate(q, v) for axis, v in [('x', [1, 0, 0]), ('y', [0, 1, 0]), ('z', [0, 0, 1])] }))

    wrist = joints[1]['rootPosition']
    index_base = joints[7]['rootPosition']
    little_base = joints[22]['rootPosition']
    middle_base = joints[12]['rootPosition']
    finger = unit(sub(middle_base, wrist))
    index_to_little = unit(sub(little_base, index_base))
    # Geometric normal only. Its anatomical sign is deliberately not asserted.
    geometric_normal = unit(cross(finger, index_to_little))
    tips = {}
    for name, chain in [('thumb', [2, 3, 4, 5]), ('index', [6, 7, 8, 9, 10]),
                        ('middle', [11, 12, 13, 14, 15]), ('ring', [16, 17, 18, 19, 20]),
                        ('little', [21, 22, 23, 24, 25])]:
        tips[name] = dict(indices=chain, wristToTip=unit(sub(joints[chain[-1]]['rootPosition'], wrist)),
                          segments=[dict(parent=a, child=b,
                                         parentLocalChildDirection=unit(joints[b]['localPosition']),
                                         rootDirection=unit(sub(joints[b]['rootPosition'], joints[a]['rootPosition'])),
                                         length=math.sqrt(sum(v*v for v in joints[b]['localPosition'])))
                                    for a, b in zip(chain, chain[1:])])
    return dict(prefab=str(path.relative_to(PROJECT)).replace('\\', '/'),
                sha256=hashlib.sha256(raw).hexdigest(), openXRRootFileID=root_id,
                openXRRootLocal={k: transforms[root_id][k] for k in ['name', 'parent', 'p', 'q', 's']},
                joints=joints, handGeometry=dict(wristToMiddleProximal=finger,
                                                indexProximalToLittleProximal=index_to_little,
                                                geometricCrossNormal=geometric_normal,
                                                geometricCrossNormalDefinition='cross(wristToMiddleProximal, indexProximalToLittleProximal); no anatomical normal sign asserted'),
                fingers=tips)


def main():
    primitives = PACKAGE.parent.parent / 'OpenXR/OpenXRHandPrimitives.cs'
    data = dict(purpose='Meta original hand prefab rest/bind model geometry only; no recorded motion or tracking file read.',
                provenance='Read-only parsing of two SDK prefab YAML files. No Unity scene, prefab, Resources data, or assessment condition edited.',
                coordinateSpace='OpenXRRoot local; vectors composed through actual parent quaternions and scales. Units are prefab units (Unity metres at scale 1).',
                quaternionOrder='x,y,z,w',
                inputs=[str(PACKAGE / name) for name in ['OVRLeftHandVisual.prefab', 'OVRRightHandVisual.prefab']],
                axisDefinitionSource=dict(path=str(primitives.relative_to(PROJECT)).replace('\\', '/'),
                                          sha256=hashlib.sha256(primitives.read_bytes()).hexdigest(),
                                          lines=[77, 107], distal=[0, 0, 1], palmar=[0, -1, 0], dorsal=[0, 1, 0],
                                          thumbSideLeft=[1, 0, 0], thumbSideRight=[-1, 0, 0],
                                          scope='SDK OpenXR convention for both hands. Legacy OVR convention is different and is not used.'),
                hands={side: extract(PACKAGE / f'OVR{side}HandVisual.prefab') for side in ['Left', 'Right']})
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(OUTPUT)
    for side, hand in data['hands'].items():
        print(side, 'parents', [j['parentIndex'] for j in hand['joints']])
        print(side, 'geometry', hand['handGeometry'])
        print(side, 'finger proximal children', {k: v['segments'][1 if k != 'thumb' else 0]['parentLocalChildDirection'] for k,v in hand['fingers'].items()})


if __name__ == '__main__':
    main()
