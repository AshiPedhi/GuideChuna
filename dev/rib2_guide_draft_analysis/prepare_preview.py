"""Read-only source analysis; writes only separate preview request and probe CSV."""
from pathlib import Path
import csv, json, math
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Captures/Rib2GuideDraft_V0.1_20261026'
HERE = Path(__file__).resolve().parent
manifest = json.loads((OUT/'draft_manifest.json').read_text(encoding='utf-8'))
keys = manifest['selected_keys']
chosen = [keys[i] for i in [0,2,3,4]]
with (OUT/'guide_start_draft.csv').open(encoding='utf-8',newline='') as f:
    rows = list(csv.DictReader(f))
right = next(r for r in rows if r['FrameIndex']=='0' and r['HandType']=='Right' and r['JointID']=='1')
right_p = np.array([float(right['WorldPos'+c]) for c in 'XYZ'])
p = np.array([k['left_root_position'] for k in chosen])-right_p
# Approximate operator wrist observations on 960x540 third-person frames.
# Camera-only production framing; this does not register patient space or scale.
q = np.array([[225,52],[275,-45],[205,135],[170,70]],float)

def rotation(angles):
    y,x,z = np.deg2rad(angles)
    ry=np.array([[math.cos(y),0,math.sin(y)],[0,1,0],[-math.sin(y),0,math.cos(y)]])
    rx=np.array([[1,0,0],[0,math.cos(x),-math.sin(x)],[0,math.sin(x),math.cos(x)]])
    rz=np.array([[math.cos(z),-math.sin(z),0],[math.sin(z),math.cos(z),0],[0,0,1]])
    return ry@rx@rz

def cost(a):
    screen=(p@rotation(a))[:,:2]*[1,-1]
    scale=np.sum(screen*q)/np.sum(screen*screen)
    return np.sqrt(np.mean((screen*scale-q)**2)) if scale>0 else 1e9,scale,screen*scale

angles=[-97.7,131.8,-174.9]
err,scale,projected=cost(angles)
camera=dict(cameraYaw=angles[0],cameraPitch=angles[1],cameraRoll=angles[2])
alignment=dict(method='Camera-only weak-perspective fit to approximate manually observed wrist positions; authored framing, not measured source-camera calibration.',
               observation_frame_size=[960,540],manual_left_minus_right_wrist_pixels=q.tolist(),
               selected_video_times_s=[k['video_s'] for k in chosen],
               selected_source_times_s=[k['source_actual_s'] for k in chosen],
               camera=camera,fit_rms_pixels=float(err),fitted_pixels_per_source_unit=float(scale),
               projected_relative_pixels=projected.tolist(),
               warning='Approximate 2D fit only. Camera roll/yaw/pitch do not modify CSV poses. No patient registration, metric scale validation, or depth validation.')
(OUT/'camera_framing.json').write_text(json.dumps(alignment,ensure_ascii=False,indent=2),encoding='utf-8')

with (OUT/'guide_demo_draft.csv').open(encoding='utf-8',newline='') as f:
    reader=csv.DictReader(f); fields=reader.fieldnames; allrows=list(reader)
frames=[0,112,172,202,247]
probe=HERE/'keypose_probe.csv'
with probe.open('w',encoding='utf-8',newline='') as f:
    w=csv.DictWriter(f,fieldnames=fields);w.writeheader()
    for new,old in enumerate(frames):
        for row in allrows:
            if int(row['FrameIndex'])!=old:continue
            row=dict(row);row['FrameIndex']=str(new);row['Timestamp']=f'{new/30:.8f}';w.writerow(row)

request=dict(csvPath=str(probe),outputDir=str(ROOT/'Temp/rib2-guide-keyposes-b'),startFrame=0,endFrame=-1,
             frameStep=1,width=960,height=720,fps=30,framingMargin=1.15,**camera)
(ROOT/'Temp/rib2-guide-draft-request.json').write_text(json.dumps(request,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(request=request,fit_rms_pixels=err),ensure_ascii=False))
