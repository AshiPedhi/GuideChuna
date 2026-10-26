"""Create separate review-only guide CSVs from source poses and authored timing.

Default dry-run. --write creates new files here and refuses to overwrite.
--write --replace-draft replaces only these generated files after validating
their hashes against this generator's previous manifest.
No Assets, source tracking, scene, scenario, or pass condition is changed.
Only Python standard library is required.
"""
from pathlib import Path
import argparse, bisect, collections, csv, hashlib, json, math

OUT = Path(__file__).resolve().parent
SOURCE = Path('D:/전문가 영상 촬영본/트래킹 데이터/6. 제2늑골 교정기법.csv')
FIELDS = ['FrameIndex','HandType','JointID','LocalPosX','LocalPosY','LocalPosZ',
          'LocalRotX','LocalRotY','LocalRotZ','LocalRotW','Timestamp',
          'WorldPosX','WorldPosY','WorldPosZ','WorldRotX','WorldRotY','WorldRotZ','WorldRotW']
P = ['WorldPos'+c for c in 'XYZ']
Q = ['WorldRot'+c for c in 'XYZW']
LQ = ['LocalRot'+c for c in 'XYZW']
FPS = 30
VIDEO_START = 56.5
DEMO_END = 8.25
LABEL = '원본 키자세 + 영상 대응 제작/추정'

# Source timestamps are native-pose selections, not a constant sync offset.
# The 64.25 s candidate was rejected because it straddles a 117.9 mm / 56.2 deg
# raw tracking jump. Native 64.598 s has 8.4 mm / 2.24 deg and 13.6 mm / 2.09 deg
# immediately before/after it; still an unvalidated production pose.
KEYS = [
    dict(demo_s=0.0, raw_target_s=61.8, event='시작 파지 정지'),
    dict(demo_s=1.5, raw_target_s=61.8, event='시작 파지 정지 끝'),
    dict(demo_s=3.75, raw_target_s=64.598, event='팔 크게 펴기'),
    dict(demo_s=5.75, raw_target_s=65.8, event='발쪽으로 낮추기'),
    dict(demo_s=6.75, raw_target_s=66.75, event='팔 깊게 굽히기'),
    dict(demo_s=8.25, raw_target_s=64.598, event='다시 펴기 (제작상 루프 닫기)'),
]

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def normalize(q):
    length=math.sqrt(sum(v*v for v in q))
    if not math.isfinite(length) or length<1e-12:raise ValueError('Invalid source quaternion')
    return [v/length for v in q]

def qangle(a,b):
    dot=abs(sum(x*y for x,y in zip(normalize(a),normalize(b))))
    return math.degrees(2*math.acos(min(1,max(-1,dot))))

def slerp(a,b,u):
    a=normalize(a);b=normalize(b);dot=sum(x*y for x,y in zip(a,b))
    if dot<0:b=[-v for v in b];dot=-dot
    dot=min(1,max(-1,dot))
    if dot>.9995:return normalize([x+(y-x)*u for x,y in zip(a,b)])
    angle=math.acos(dot);den=math.sin(angle)
    aa=math.sin((1-u)*angle)/den;bb=math.sin(u*angle)/den
    return [aa*x+bb*y for x,y in zip(a,b)]

def distance(a,b):return math.sqrt(sum((x-y)**2 for x,y in zip(a,b)))

def read_source(path):
    frames=collections.OrderedDict()
    with path.open(encoding='utf-8-sig',newline='') as f:
        r=csv.DictReader(f)
        if r.fieldnames!=FIELDS:raise ValueError('Unexpected source columns')
        for row in r:
            fi=int(row['FrameIndex']);h=row['HandType'];j=int(row['JointID'])
            frame=frames.setdefault(fi,dict(time=float(row['Timestamp']),Left={},Right={}))
            if j in frame[h]:raise ValueError(f'Duplicate row: {fi}/{h}/{j}')
            frame[h][j]=row
    for fi,frame in frames.items():
        for h in ['Left','Right']:
            if set(frame[h])!=set(range(26)):raise ValueError(f'Incomplete source frame {fi}')
    return frames

def root(frame,hand):
    row=frame[hand][1]
    return [float(row[k]) for k in P],normalize([float(row[k]) for k in Q])

def select_keys(frames):
    indexes=list(frames);fs=list(frames.values());times=[v['time'] for v in fs]
    result=[]
    for key in KEYS:
        i=min(range(len(times)),key=lambda j:abs(times[j]-key['raw_target_s']))
        p,q=root(fs[i],'Left');prevp,prevq=root(fs[i-1],'Left');nextp,nextq=root(fs[i+1],'Left')
        result.append(dict(**key,source_frame=indexes[i],source_actual_s=times[i],video_s=VIDEO_START+key['demo_s'],
            left_root_position=p,left_root_rotation=q,
            preceding_step_m=distance(p,prevp),following_step_m=distance(p,nextp),
            preceding_rotation_deg=qangle(q,prevq),following_rotation_deg=qangle(q,nextq)))
    return result

def pose_at(t,keys):
    if t<=keys[0]['demo_s']:k=keys[0];return k['left_root_position'],k['left_root_rotation']
    if t>=keys[-1]['demo_s']:k=keys[-1];return k['left_root_position'],k['left_root_rotation']
    i=bisect.bisect_right([k['demo_s'] for k in keys],t)-1
    a,b=keys[i],keys[i+1];u=(t-a['demo_s'])/(b['demo_s']-a['demo_s'])
    p=[x+(y-x)*u for x,y in zip(a['left_root_position'],b['left_root_position'])]
    return p,slerp(a['left_root_rotation'],b['left_root_rotation'],u)

def make_rows(start,end,keys,base,static=False):
    count=math.floor((end-start)*FPS+1e-8)+1
    allrows=[]
    for fi in range(count):
        t=start+fi/FPS;leftp,leftq=pose_at(0 if static else t,keys)
        for h in ['Left','Right']:
            rp,rq=(leftp,leftq) if h=='Left' else root(base,'Right')
            for j in range(26):
                src=base[h][j];row={k:src[k] for k in FIELDS}
                row.update(FrameIndex=str(fi),HandType=h,JointID=str(j),Timestamp=f'{fi/FPS:.9f}')
                for k,v in zip(LQ,normalize([float(src[k]) for k in LQ])):row[k]=f'{v:.9f}'
                for k in P+Q:row[k]=''
                if j==1:
                    for k,v in zip(P+Q,rp+rq):row[k]=f'{v:.9f}'
                allrows.append(row)
    return allrows

def validate(rows):
    frames=collections.OrderedDict()
    for row in rows:
        fi=int(row['FrameIndex']);h=row['HandType'];j=int(row['JointID'])
        f=frames.setdefault(fi,dict(time=float(row['Timestamp']),Left={},Right={}))
        if j in f[h]:raise ValueError('Duplicate generated joint')
        f[h][j]=row
        for col in FIELDS[3:]:
            if row[col] and not math.isfinite(float(row[col])):raise ValueError('Non-finite output')
        if j!=1 and any(row[k] for k in P+Q):raise ValueError('Unexpected world data outside wrist1')
    if list(frames)!=list(range(len(frames))):raise ValueError('Non-contiguous frame index')
    fs=list(frames.values());report=dict(frame_count=len(fs),row_count=len(rows),fps=FPS,
        first_timestamp_s=fs[0]['time'],last_timestamp_s=fs[-1]['time'],
        app_fixed_30hz_buffer_duration_s=len(fs)/FPS,
        timestamp_step_sec_min=min(fs[i+1]['time']-fs[i]['time'] for i in range(len(fs)-1)),
        timestamp_step_sec_max=max(fs[i+1]['time']-fs[i]['time'] for i in range(len(fs)-1)),
        hands={})
    for h in ['Left','Right']:
        for f in fs:
            if set(f[h])!=set(range(26)):raise ValueError('Missing generated joint')
        poses=[root(f,h) for f in fs];ps=[v[0] for v in poses];qs=[v[1] for v in poses]
        dps=[distance(a,b) for a,b in zip(ps,ps[1:])];dqs=[qangle(a,b) for a,b in zip(qs,qs[1:])]
        report['hands'][h]=dict(joints_per_frame=26,
            root_position_min=[min(p[i] for p in ps) for i in range(3)],
            root_position_max=[max(p[i] for p in ps) for i in range(3)],
            maximum_frame_step_m=max(dps),maximum_root_speed_m_s=max(dps)*FPS,
            maximum_frame_rotation_deg=max(dqs),maximum_root_angular_speed_deg_s=max(dqs)*FPS,
            start_end_root_distance_m=distance(ps[0],ps[-1]),start_end_root_rotation_deg=qangle(qs[0],qs[-1]))
    return report

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--write',action='store_true')
    ap.add_argument('--replace-draft',action='store_true',help='Replace only hash-verified outputs belonging to the previous draft manifest')
    args=ap.parse_args()
    source_hash=sha(SOURCE);frames=read_source(SOURCE);keys=select_keys(frames)
    base=frames[keys[0]['source_frame']]
    outputs={
        'guide_demo_draft.csv':make_rows(0,DEMO_END,keys,base),
        'guide_start_draft.csv':make_rows(0,2.0,keys,base,static=True),
        'guide_cycle_draft.csv':make_rows(3.75,8.25,keys,base),
    }
    files={name:validate(rows) for name,rows in outputs.items()}
    manifest=dict(version='0.1',created_date='2026-10-26',label=LABEL,
        scope='별도 시연용 초안. 기존 데이터·시나리오·통과 조건은 수정하지 않음.',
        source_tracking_path=str(SOURCE),source_tracking_sha256=source_hash,
        source_tracking_frame_count=len(frames),
        source_video_path='D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV',
        reference_video_start_s=56.5,reference_video_requested_end_s=64.75,
        reference_video_requested_duration_s=8.25,
        video_tracking_sync_status='미확정. 이벤트를 눈으로 대응시킨 제작용 키 타이밍이며 정밀 동기화 계측이 아님.',
        selected_keys=keys,
        source_key_change=dict(rejected_target_s=64.25,selected_source_s=64.598,
            reason='64.223→64.264초에117.9mm/56.2도점프. 64.598초앞뒤는8.4/13.6mm,2.24/2.09도로연속적이어서제작포즈후보로선택. 추적정확도확정의의미는아님.'),
        fixed_right_hand=dict(source_frame=keys[0]['source_frame'],source_s=keys[0]['source_actual_s'],
            reason='흉곽을지지하는손. 영상에서정지한모습을기준으로전체시연에같은원본포즈를고정한제작초안.'),
        fixed_left_finger_pose=dict(source_frame=keys[0]['source_frame'],source_s=keys[0]['source_actual_s'],
            reason='왼손26관절의로컬포즈는시작파지모양을유지. 손목root위치와회전만키사이보간. 영상의손가락변화를복원했다고주장하지않음.'),
        interpolation='Root위치선형보간,root회전정규화최단경로QuaternionSLERP. 양손로컬관절은시작포즈고정.',
        coordinate_system='원본CSV root좌표계그대로. 환자기준변환·평행이동·회전·스케일수정없음. 원본patientReference는미확인.',
        conventions=dict(hand_labels='CSV Left=팔을움직이는손, CSV Right=흉곽지지손. 영상에대응한초안으로최종임상검수필요.',
            columns=FIELDS,root_joint_id=1,palm_joint_id=0,world_columns_only_on_joint=1,
            openxr_parent_indices=[1,-1,1,2,3,4,1,6,7,8,9,1,11,12,13,14,1,16,17,18,19,1,21,22,23,24]),
        sampling_note='30Hz일정간격.8.25초는247.5간격이므로전체248프레임의마지막Timestamp는8.233333초.앱고정재생버퍼길이는8.266667초.키끝8.25초는수학적보간종점으로유지.',
        cycle_note='cycle은 전체 시연 3.75→8.25초를 잘라 Timestamp를 0으로 재설정. 0초와 4.5초 끝 샘플을 모두 포함한 136프레임이며 두 경계 포즈가 동일함.',
        authored_loop_closure=dict(
            label='영상 대응을 위해 복귀 endpoint를 시작 wide pose로 설정한 authored loop closure이며 측정값 아님',
            reason='원본 영상 60.25초와 64.75초를 팔을 편 같은 주기 경계로 보고, 시연 8.25초의 복귀 포즈를 시연 3.75초와 동일한 원본 64.598초 포즈로 제작상 설정함.',
            demo_start_key_s=3.75,demo_end_key_s=8.25,source_pose_s=64.598,source_pose_frame=1549,
            replaced_candidate_source_s=67.890,replaced_candidate_source_frame=1628,
            original_tracking_untouched=True,
            note='이전 후보 67.890초 포즈를 정확한 복귀 계측으로 확정하지 않고, 영상의 반복 경계에 맞춘 별도 제작 보정을 적용함. 경계의 위치와 회전은 연속이지만 속도까지 연속으로 맞춘 곡선은 아님.'),
        limitations=['영상-트래킹정밀동기화미검증','3D키포즈의실제환자정렬미검증','고정한손가락모양의동작중적합성미검증',
            '압박력·환자해부각·접촉판정·통과조건미추정','원본추적의점프를그대로재생하지않는제작용보간초안','전문가의정확한연속계측복원으로사용하지않음'],
        validation=files)
    if sha(SOURCE)!=source_hash:raise ValueError('Source changed during processing')
    print(json.dumps(manifest,ensure_ascii=False,indent=2))
    if not args.write:return
    # Replacing existing draft outputs is allowed only with an explicit flag,
    # and only if their current hashes still match our prior provenance file.
    if args.replace_draft:
        oldpath=OUT/'draft_manifest.json'
        old=json.loads(oldpath.read_text(encoding='utf-8'))
        if old['source_tracking_sha256']!=source_hash:raise ValueError('Source hash differs from previous draft')
        for name in outputs:
            path=(OUT/name).resolve()
            if not path.is_relative_to(OUT) or sha(path)!=old['validation'][name]['sha256']:
                raise ValueError(f'Existing output changed independently: {path}')
    names=list(outputs)+['draft_manifest.json']
    for name in names:
        path=(OUT/name).resolve()
        if not path.is_relative_to(OUT) or (path.exists() and not args.replace_draft):raise FileExistsError(f'Output exists or outside destination: {path}')
    for name,rows in outputs.items():
        path=OUT/name
        with path.open('w',encoding='utf-8',newline='') as f:
            writer=csv.DictWriter(f,fieldnames=FIELDS);writer.writeheader();writer.writerows(rows)
        files[name]['sha256']=sha(path)
    manifest['source_unchanged_after_export']=sha(SOURCE)==source_hash
    (OUT/'draft_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__':main()
