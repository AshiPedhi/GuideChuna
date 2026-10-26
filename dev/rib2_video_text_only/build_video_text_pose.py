"""Author a hand animation from VIDEO annotations + text + SDK bind geometry.

No recorded tracking or earlier draft pose is an input. Inputs are allowlisted.
Outputs are isolated CSV containers for the existing renderer, not recordings.
Default dry-run; --write creates new outputs, --replace-own replaces only this
generator's named outputs in this separate demonstration folder.
"""
from pathlib import Path
import argparse,bisect,csv,hashlib,json,math
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2VideoTextOnly_V0.1_20261026'
INPUT_NAMES=['source_annotations.json','rig_bind_geometry.json','text_evidence.json']
FPS=30
PIXELS_PER_MODEL_UNIT=550.0 # View-only choice matching approximate visible glove size; not calibration.
REFERENCE_PIXEL=np.array([450.,318.])
FIELDS=['FrameIndex','HandType','JointID','LocalPosX','LocalPosY','LocalPosZ','LocalRotX','LocalRotY','LocalRotZ','LocalRotW','Timestamp','WorldPosX','WorldPosY','WorldPosZ','WorldRotX','WorldRotY','WorldRotZ','WorldRotW']

def norm(v):
 v=np.asarray(v,float);return v/np.linalg.norm(v)
def qmul(a,b):
 x,y,z,w=a;X,Y,Z,W=b
 return np.array([w*X+x*W+y*Z-z*Y,w*Y-x*Z+y*W+z*X,w*Z+x*Y-y*X+z*W,w*W-x*X-y*Y-z*Z])
def axisq(axis,angle):return np.r_[norm(axis)*math.sin(math.radians(angle)/2),math.cos(math.radians(angle)/2)]
def rotate(q,v):return qmul(qmul(q,np.r_[v,0]),np.r_[-np.array(q[:3]),q[3]])[:3]
def matrixq(m):
 # Rotation matrix -> quaternion via symmetric eigenproblem, avoiding branch/sign guesses.
 m00,m01,m02=m[0];m10,m11,m12=m[1];m20,m21,m22=m[2]
 k=np.array([[m00-m11-m22,m01+m10,m02+m20,m21-m12],
             [m01+m10,m11-m00-m22,m12+m21,m02-m20],
             [m02+m20,m12+m21,m22-m00-m11,m10-m01],
             [m21-m12,m02-m20,m10-m01,m00+m11+m22]])/3
 vals,vecs=np.linalg.eigh(k);q=vecs[:,np.argmax(vals)]
 return norm(q if q[3]>=0 else -q)
def slerp(a,b,u):
 a=norm(a);b=norm(b);dot=float(a@b)
 if dot<0:b=-b;dot=-dot
 if dot>.9995:return norm(a+(b-a)*u)
 theta=math.acos(min(1,dot));return (math.sin((1-u)*theta)*a+math.sin(u*theta)*b)/math.sin(theta)
def pchip_slopes(ts,vs):
 # Shape-preserving cubic: avoids the corners of straight key-to-key paths.
 vs=np.asarray(vs,float);h=np.diff(ts);d=np.diff(vs,axis=0)/h[:,None];m=np.zeros_like(vs)
 for i in range(1,len(ts)-1):
  for j in range(vs.shape[1]):
   if d[i-1,j]*d[i,j]>0:
    w1=2*h[i]+h[i-1];w2=h[i]+2*h[i-1]
    m[i,j]=(w1+w2)/(w1/d[i-1,j]+w2/d[i,j])
 # Zero endpoint velocity is authored easing, not a measured velocity.
 return m
def hermite(ts,vs,ms,t):
 i=max(0,min(len(ts)-2,bisect.bisect_right(ts,t)-1));h=ts[i+1]-ts[i];u=(t-ts[i])/h
 return (2*u**3-3*u*u+1)*vs[i]+(u**3-2*u*u+u)*h*ms[i]+(-2*u**3+3*u*u)*vs[i+1]+(u**3-u*u)*h*ms[i+1]

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--write',action='store_true');parser.add_argument('--replace-own',action='store_true');args=parser.parse_args()
 inputs={name:json.loads((OUT/name).read_text(encoding='utf-8')) for name in INPUT_NAMES}
 annotations=inputs['source_annotations.json'];rig=inputs['rig_bind_geometry.json'];keys=annotations['keys']
 ts=np.array([k['video_s']-56.5 for k in keys]);poses={};local={};observed={}
 # The long axis follows visible wrist->palm direction. The unobserved twist is
 # an explicit authored display assumption. Wrist path depth is fixed at zero.
 twists={'Left':[30,30,35,45,40,30,10,30,45],'Right':[0]*len(keys)}
 curls={'Left':{'Index':[30,65,35],'Middle':[40,70,40],'Ring':[45,75,40],'Little':[50,75,45]},
        'Right':{'Index':[12,15,5],'Middle':[15,20,8],'Ring':[20,25,10],'Little':[22,28,12]}}
 for hand in ['Left','Right']:
  js=rig['hands'][hand]['joints'];lp=np.array([j['localPosition'] for j in js]);lq=np.array([norm(j['localRotation']) for j in js])
  for finger,start in [('Index',7),('Middle',12),('Ring',17),('Little',22)]:
   for j,angle in zip(range(start,start+3),curls[hand][finger]):lq[j]=norm(qmul(lq[j],axisq([1,0,0],angle)))
  # Thumb closes toward the wrist grip using the model's own local thumb axes.
  thumb_angles=[18,28,20] if hand=='Left' else [3,5,5]
  for j,angle in zip([2,3,4],thumb_angles):lq[j]=norm(qmul(lq[j],axisq([1,0,0],angle)))
  local[hand]=(lp,lq)
  wrist=np.array([k[hand.lower()+'_wrist'] for k in keys],float)
  palm=np.array([k[hand.lower()+'_palm'] for k in keys],float)
  observed[hand]=dict(wrist=wrist.tolist(),palm=palm.tolist())
  roots=np.c_[(wrist[:,0]-REFERENCE_PIXEL[0])/PIXELS_PER_MODEL_UNIT,(REFERENCE_PIXEL[1]-wrist[:,1])/PIXELS_PER_MODEL_UNIT,np.zeros(len(keys))]
  rots=[]
  for wi,pa,twist in zip(wrist,palm,twists[hand]):
   dz=norm([pa[0]-wi[0],wi[1]-pa[1],0])
   dy=rotate(axisq(dz,twist),[0,0,-1])
   dx=norm(np.cross(dy,dz));dy=norm(np.cross(dz,dx))
   mat=np.column_stack([dx,dy,dz]);assert np.linalg.det(mat)>.99999
   q=matrixq(mat)
   assert np.linalg.norm(rotate(q,[0,0,1])-dz)<1e-6
   rots.append(q)
  poses[hand]=(roots,np.array(rots),pchip_slopes(ts,roots))
 def frame(t):
  result={}
  for h,(positions,rots,slopes) in poses.items():
   pos=hermite(ts,positions,slopes,t);i=max(0,min(len(ts)-2,bisect.bisect_right(ts,t)-1));u=(t-ts[i])/(ts[i+1]-ts[i])
   result[h]=(pos,slerp(rots[i],rots[i+1],u),*local[h])
  return result
 def export(name,times):
  target=OUT/name
  if target.exists() and not args.replace_own:raise FileExistsError(target)
  with target.open('w',encoding='utf-8',newline='') as f:
   w=csv.writer(f);w.writerow(FIELDS)
   for fi,t in enumerate(times):
    for h,(p,q,lp,lq) in frame(float(t)).items():
     for j in range(26):
      row=[fi,h,j,*lp[j],*lq[j],fi/FPS]
      row.extend([*p,*q] if j==1 else ['']*7)
      w.writerow([f'{v:.9f}' if isinstance(v,(float,np.floating)) else v for v in row])
  return dict(file=name,frames=len(times),sha256=hashlib.sha256(target.read_bytes()).hexdigest())
 manifest=dict(label='영상·텍스트만으로 수작업 재구성한 가이드핸드 초안',tracking_input_used=False,previous_draft_pose_or_trajectory_used=False,
  input_allowlist=[dict(path=str(OUT/n),sha256=hashlib.sha256((OUT/n).read_bytes()).hexdigest()) for n in INPUT_NAMES],
  source_observation='Manual video wrist/palm annotations; no automatic motion/hand model was run.',
  text_role='Scenario instruction text and internal specification describe contact/support roles, not numeric joints or assessment thresholds.',
  static_model_role='SDK hand prefab rest/bind geometry is a generic model, not an expert recording.',
  reference_pixel=REFERENCE_PIXEL.tolist(),pixels_per_model_unit=PIXELS_PER_MODEL_UNIT,
  view_space='Image-plane x right / y up. All wrist roots z=0. Hands have 3D model geometry and authored finger flexion/twist. No measured depth or patient registration.',
  authored_finger_flexion_deg=curls,authored_thumb_flexion_deg={'Left':[18,28,20],'Right':[3,5,5]},authored_dorsal_twist_deg=twists,
  trajectory_interpolation='Shape-preserving cubic Hermite/PCHIP on observed image-plane wrist keys; endpoints eased to zero velocity. Wrist rotation shortest-path SLERP.',
  finger_animation='Each hand keeps one separately authored static grip shape during the movement. No occluded articulation reconstruction claimed.',
  cycle='Original video 60.25–64.75 s. Both endpoint observations retained; no forced closure/infinite loop.',
  clinical_angles='No clinical pass angle, pressure, contact collider, or existing assessment condition was generated or changed.',
  fps=FPS,source_start_s=56.5,source_end_s=64.75,key_times_s=ts.tolist(),selected_observations=observed,
  limitations=['Manual annotation uncertainty estimated at ~10–20 pixels; not independently measured','Depth and hidden joints are authored assumptions','View scale is not a metric calibration','Clinical pose correctness and actual patient alignment unverified','Quest Play unverified'])
 if args.write:
  manifest['outputs']=[export('video_text_demo.csv',np.arange(248)/FPS),export('video_text_start.csv',np.zeros(61)),export('video_text_cycle.csv',3.75+np.arange(136)/FPS)]
  manifest['probe']=export('video_text_keypose_probe.csv',[0,3.75,5.75,6.75,8.25])
  (OUT/'reconstruction_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(manifest,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
