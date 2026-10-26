"""Independent delivery checks, including original file preservation."""
from pathlib import Path
import csv, hashlib, json, math, subprocess

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2GuideDraft_V0.1_20261026'
def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(4*1024*1024),b''):h.update(block)
    return h.hexdigest()

before=json.loads((OUT/'original_hashes_before.json').read_text(encoding='utf-8'))
originals=[]
for path,expected in before.items():
    actual=digest(Path(path))
    originals.append(dict(path=path,before_sha256=expected,after_sha256=actual,unchanged=expected==actual))
assert all(r['unchanged'] for r in originals),'Original file changed'
csvs=[]
for name,expected in [('guide_start_draft.csv',61),('guide_cycle_draft.csv',136),('guide_demo_draft.csv',248)]:
    with (OUT/name).open(encoding='utf-8',newline='') as f:
        r=csv.DictReader(f);fields=r.fieldnames;rows=list(r)
    assert len(fields)==18
    frames={}
    for row in rows:
        assert len(row)==18
        assert all(math.isfinite(float(v)) for key,v in row.items() if key not in ['HandType'] and v!='')
        frame=frames.setdefault(int(row['FrameIndex']),{})
        key=(row['HandType'],int(row['JointID']))
        assert key not in frame
        frame[key]=row
    assert list(frames)==list(range(expected))
    for fi,frame in frames.items():
        assert set(frame)=={(h,j) for h in ['Left','Right'] for j in range(26)}
        assert all(abs(float(v['Timestamp'])-fi/30)<1e-6 for v in frame.values())
    pose_fields=[s for s in fields if s not in ['FrameIndex','Timestamp','HandType','JointID']]
    right_fixed=all(all(row[k]==frames[0][key][k] for k in pose_fields) for frame in frames.values() for key,row in frame.items() if key[0]=='Right')
    local_fixed=all(all(row[k]==frames[0][key][k] for k in fields if k.startswith('Local')) for frame in frames.values() for key,row in frame.items())
    assert right_fixed and local_fixed
    record=dict(file=name,frames=expected,rows=len(rows),fps=30,joints_per_hand=26,finite_values=True,
                right_pose_fixed=right_fixed,local_joint_poses_fixed=local_fixed,sha256=digest(OUT/name))
    if name=='guide_cycle_draft.csv':
        record['first_last_poses_identical']=all(all(row[k]==frames[expected-1][key][k] for k in pose_fields) for key,row in frames[0].items())
        assert record['first_last_poses_identical']
    csvs.append(record)

info=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(OUT/'comparison.mp4')],text=True,encoding='utf-8'))
video=next(s for s in info['streams'] if s['codec_type']=='video')
assert (video['width'],video['height'])==(1920,900)
assert int(video['nb_frames'])==248
receipt=json.loads((OUT/'render_receipt.json').read_text(encoding='utf-8'))
assert receipt['success'] and receipt['leftJointCount']==26 and receipt['rightJointCount']==26 and receipt['framesWritten']==248
report=dict(date='2026-10-26',scope='Separate review-only demonstration; no runtime scenario integration.',
    original_files=originals,originals_unchanged=all(r['unchanged'] for r in originals),draft_csvs=csvs,
    video=dict(file='comparison.mp4',width=1920,height=900,frames=248,frame_rate=video['r_frame_rate'],
               duration_s=float(info['format']['duration']),audio_present=any(s['codec_type']=='audio' for s in info['streams']),
               sha256=digest(OUT/'comparison.mp4')),
    isolated_unity_capture=dict(success=True,left_joints=26,right_joints=26,frames=248,meshes=receipt['meshes']),
    compilation=dict(runtime='Assembly-CSharp: 0 errors, 14 pre-existing warnings.',editor='Assembly-CSharp-Editor: 0 errors, 15 pre-existing warnings; no warning in Rib2GuideDraftPreview.cs.'),
    unverified=['Precise video/tracking synchronization','Actual patient registration and metric/depth accuracy',
                'Clinical suitability of tracked poses','Dynamic finger pose reconstruction','Quest Play and runtime integration'])
(OUT/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(originals_unchanged=len(originals),draft_csvs=csvs,video=report['video']),ensure_ascii=False))
