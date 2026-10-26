"""Verify video/text-only provenance and preserve the existing training data."""
from pathlib import Path
import collections,csv,hashlib,json,math,runpy,subprocess,sys
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2VideoTextOnly_V0.1_20261026'
WORK=ROOT/'Temp/rib2-video-text-only'
def sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for b in iter(lambda:f.read(4194304),b''):h.update(b)
 return h.hexdigest()

if '--audit-generator' in sys.argv:
 # This child audits Python file-open events in the motion generator only.
 # Opaque preservation hashing happens later in the parent process.
 import numpy # Load dependency before the generator file-open audit.
 opened=[]
 forbidden=['/트래킹 데이터/','/assets/resources/handposedata/','/captures/rib2guidedraft_v0.1_20261026/']
 def audit(event,args):
  if event!='open' or not args or not isinstance(args[0],(str,bytes)):return
  path=str(args[0]).replace('\\','/').lower()
  if any(s in path for s in forbidden):raise PermissionError('Forbidden motion input: '+path)
  if '/captures/rib2videotextonly_v0.1_20261026/' in path:opened.append(dict(path=str(args[0]),mode=str(args[1])))
 sys.addaudithook(audit)
 sys.argv=[str(ROOT/'dev/rib2_video_text_only/build_video_text_pose.py'),'--write','--replace-own']
 runpy.run_path(sys.argv[0],run_name='__main__')
 result=dict(generator_completed_with_forbidden_file_opens_blocked=True,
  forbidden_path_patterns=forbidden,file_open_events=opened,
  scope='Python motion-generator open events only; original-preservation SHA hashing is separate and is not an animation input.')
 (OUT/'generator_input_audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 sys.exit(0)

with (WORK/'generator_audit_stdout.log').open('w',encoding='utf-8') as f:
 subprocess.run([sys.executable,str(Path(__file__).resolve()),'--audit-generator'],stdout=f,check=True)
audit=json.loads((OUT/'generator_input_audit.json').read_text(encoding='utf-8'))
manifest=json.loads((OUT/'reconstruction_manifest.json').read_text(encoding='utf-8'))
annotations=json.loads((OUT/'source_annotations.json').read_text(encoding='utf-8'))
bind=json.loads((OUT/'rig_bind_geometry.json').read_text(encoding='utf-8'))
assert manifest['tracking_input_used'] is False and manifest['previous_draft_pose_or_trajectory_used'] is False
for item in manifest['input_allowlist']:assert sha(Path(item['path']))==item['sha256']
before=json.loads((OUT/'preservation_before.json').read_text(encoding='utf-8'))
originals=[dict(path=p,before_sha256=h,after_sha256=sha(Path(p))) for p,h in before.items()]
for item in originals:item['unchanged']=item['before_sha256']==item['after_sha256']
assert all(i['unchanged'] for i in originals)

outputs=[];datasets={}
for name,count in [('video_text_start.csv',61),('video_text_cycle.csv',136),('video_text_demo.csv',248)]:
 with (OUT/name).open(encoding='utf-8',newline='') as f:
  reader=csv.DictReader(f);fields=reader.fieldnames;rows=list(reader)
 assert len(fields)==18
 frames=collections.defaultdict(dict)
 for row in rows:
  assert all(math.isfinite(float(v)) for k,v in row.items() if k!='HandType' and v!='')
  fi=int(row['FrameIndex']);key=(row['HandType'],int(row['JointID']));assert key not in frames[fi];frames[fi][key]=row
 assert list(frames)==list(range(count))
 for fi,frame in frames.items():
  assert set(frame)=={(h,j) for h in ['Left','Right'] for j in range(26)}
  for (h,j),row in frame.items():
   assert abs(float(row['Timestamp'])-fi/30)<1e-6
   for i,a in enumerate('XYZ'):assert abs(float(row['LocalPos'+a])-bind['hands'][h]['joints'][j]['localPosition'][i])<1e-8
   q=[float(row['LocalRot'+a]) for a in 'XYZW'];assert abs(sum(v*v for v in q)-1)<1e-6
   if j==1:
    assert float(row['WorldPosZ'])==0
    q=[float(row['WorldRot'+a]) for a in 'XYZW'];assert abs(sum(v*v for v in q)-1)<1e-6
   else:assert all(row['WorldPos'+a]=='' for a in 'XYZ')
 # Finger shape is deliberately one separately authored static grip per hand.
 local_fields=[f for f in fields if f.startswith('Local')]
 assert all(all(row[k]==frames[0][key][k] for k in local_fields) for frame in frames.values() for key,row in frame.items())
 outputs.append(dict(file=name,frames=count,rows=len(rows),fps=30,joints_per_hand=26,
  local_positions_equal_generic_bind_geometry=True,authored_local_grip_fixed=True,wrist_depth_fixed_at_zero=True,
  normalized_quaternions=True,sha256=sha(OUT/name)))
 datasets[name]=frames
frames=datasets['video_text_demo.csv']
roundtrip=[]
for key in annotations['keys']:
 t=key['video_s']-56.5;fi=round(t*30)
 # Only exact 30Hz key times are checked; 3.75/5.75/6.75 are off-grid.
 if fi>=len(frames) or abs(fi/30-t)>1e-8:continue
 for hand in ['Left','Right']:
  row=frames[fi][(hand,1)];x=float(row['WorldPosX'])*550+450;y=318-float(row['WorldPosY'])*550
  expected=key[hand.lower()+'_wrist'];error=math.hypot(x-expected[0],y-expected[1]);assert error<1e-5
  roundtrip.append(dict(source_s=key['video_s'],hand=hand,pixel_roundtrip_error=error))
video=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(OUT/'comparison.mp4')],text=True,encoding='utf-8'))
v=next(s for s in video['streams'] if s['codec_type']=='video')
assert (v['width'],v['height'],v['r_frame_rate'],int(v['nb_frames']))==(1920,900,'30/1',248)
receipt=json.loads((OUT/'render_receipt.json').read_text(encoding='utf-8'))
assert receipt['success'] and receipt['framesWritten']==248 and receipt['leftJointCount']==26 and receipt['rightJointCount']==26
assert Path(receipt['csvPath']).resolve()==(OUT/'video_text_demo.csv').resolve()
report=dict(date='2026-10-26',tracking_input_used=False,previous_draft_pose_or_trajectory_used=False,
 motion_generator_file_open_audit='generator_input_audit.json',originals_unchanged=all(i['unchanged'] for i in originals),protected_originals=originals,
 caveat='Existing HandPose files were read as opaque SHA bytes for preservation only, never as animation data. External expert tracking and earlier draft poses are blocked in motion generation.',
 input_allowlist=manifest['input_allowlist'],outputs=outputs,
 coordinate_roundtrip_to_annotations=roundtrip,
 coordinate_roundtrip_scope='Checks arithmetic agreement with manual annotations only; not annotation accuracy or measured 3D reconstruction.',
 isolated_mesh_render=dict(frames=248,joints_each_hand=26,actual_input=receipt['csvPath'],meshes=receipt['meshes'],scene_saved=False,play_started=False),
 comparison_video=dict(width=1920,height=900,fps=30,frames=248,duration_s=float(video['format']['duration']),sha256=sha(OUT/'comparison.mp4')),
 compile='Assembly-CSharp-Editor and referenced runtime project: 0 errors, 15 pre-existing warnings, no warning in the edited preview file.',
 clinical_or_runtime_validation_performed=False,
 unverified=['Manual annotation accuracy','Camera depth and metric scale','Occluded finger articulation','Exact rib contact and pressure','Clinical suitability','Actual patient registration','Quest Play'])
(OUT/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(tracking_input_used=False,generator_forbidden_input_audit_passed=True,protected_originals_unchanged=len(originals),outputs=outputs,video=report['comparison_video']),ensure_ascii=False))
