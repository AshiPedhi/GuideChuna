"""Compose video-only authored hand render with the original source segment.

Inputs are the original MOV, this draft's annotations, this draft's authored CSV,
and this draft's isolated render. No previous draft or recorded tracking input.
"""
from pathlib import Path
import bisect,csv,hashlib,json,math,subprocess
import numpy as np
from PIL import Image,ImageChops,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2VideoTextOnly_V0.1_20261026'
WORK=ROOT/'Temp/rib2-video-text-only'
RENDER=WORK/'mesh-full-a'
PANELS=WORK/'panels';PANELS.mkdir(exist_ok=True)
BG=(14,19,27);FG=(233,240,246);MUTED=(161,179,198);CYAN=(37,210,237);ORANGE=(255,160,79)
def font(size,bold=False):return ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf' if bold else 'C:/Windows/Fonts/malgun.ttf',size)
def text(d,xy,value,size=24,fill=FG,bold=False):d.text(xy,value,font=font(size,bold),fill=fill)
def run(args):subprocess.run(['ffmpeg','-hide_banner','-loglevel','error',*args],check=True)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

annotations=json.loads((OUT/'source_annotations.json').read_text(encoding='utf-8'))
manifest=json.loads((OUT/'reconstruction_manifest.json').read_text(encoding='utf-8'))
receipt=json.loads((RENDER/'capture-receipt.json').read_text(encoding='utf-8-sig'))
assert receipt['success'] and receipt['framesWritten']==248
assert [receipt[k] for k in ['cameraYaw','cameraPitch','cameraRoll']]==[0,0,0]
frames=sorted(RENDER.glob('hand_*.png'));assert len(frames)==248
cam=np.array([receipt['cameraPosition'][a] for a in 'xyz']);factor=720/(2*receipt['orthographicSize'])
ref=np.array(manifest['reference_pixel']);ratio=manifest['pixels_per_model_unit']
def to_screen_world(p):return (float(480+(p[0]-cam[0])*factor),float(360-(p[1]-cam[1])*factor))
def to_screen_pixel(p):return to_screen_world([(p[0]-ref[0])/ratio,(ref[1]-p[1])/ratio,0])
with (OUT/'video_text_demo.csv').open(encoding='utf-8',newline='') as f:rows=list(csv.DictReader(f))
roots=[np.array([float(r['WorldPos'+a]) for a in 'XYZ']) for r in rows if r['HandType']=='Left' and r['JointID']=='1']
trajectory=[to_screen_world(p) for p in roots]
keys=annotations['keys'];times=np.array([k['video_s']-56.5 for k in keys])
def observed_points(t,key):
 i=max(0,min(len(times)-2,bisect.bisect_right(times,t)-1));u=(t-times[i])/(times[i+1]-times[i])
 return np.array(keys[i][key])*(1-u)+np.array(keys[i+1][key])*u

header=Image.new('RGB',(1920,108),BG);d=ImageDraw.Draw(header)
text(d,(30,12),'제2늑골 · 영상·텍스트만으로 만든 가이드핸드',32,bold=True)
text(d,(1568,20),'트래킹 미사용 · v0.1',24,fill=CYAN)
text(d,(30,68),'원본 영상',25,bold=True)
text(d,(990,68),'새로 제작한 3D 손 · 화면에서 관찰한 이동 경로',25,bold=True)
header.save(WORK/'header.png')
footer=Image.new('RGB',(1920,72),BG);d=ImageDraw.Draw(footer)
text(d,(30,15),'시작 파지 → 팔 움직임 한 주기  |  원본 56.50–64.75초',22)
text(d,(990,6),'● 팔 이동 손',21,fill=CYAN);text(d,(1200,6),'● 흉곽 지지 손',21,fill=ORANGE)
text(d,(990,37),'회색 선: 환자 팔의 화면 위치  ·  깊이·숨은 손가락은 제작 가정',18,fill=MUTED)
footer.save(WORK/'footer.png')
for i,path in enumerate(frames):
 im=Image.open(path).convert('RGB');bg=im.getpixel((0,0));panel=Image.new('RGB',im.size,bg);d=ImageDraw.Draw(panel)
 # Explanatory 2D patient-arm line, explicitly distinct from the 3D hand mesh.
 arm=[to_screen_pixel(observed_points(i/30,k)) for k in ['patient_shoulder','patient_elbow','patient_wrist']]
 d.line(arm,fill=(43,55,68),width=24,joint='curve')
 d.line(arm,fill=(94,111,129),width=3,joint='curve')
 for p in arm:
  x,y=p;d.ellipse((x-5,y-5,x+5,y+5),fill=(130,145,159))
 d.line(trajectory,fill=(24,63,74),width=2)
 if i>0:d.line(trajectory[:i+1],fill=(32,124,146),width=3)
 mask=ImageChops.difference(im,Image.new('RGB',im.size,bg)).convert('L').point(lambda v:255 if v>15 else 0)
 panel.paste(im,(0,0),mask);d=ImageDraw.Draw(panel)
 stage='시작 파지' if i/30<1.5 else ('팔 펼침으로 전환' if i/30<3.75 else '팔 움직임 한 주기')
 text(d,(24,19),stage,24,bold=True);text(d,(24,54),f'{i/30:.2f} / 8.25초',18,fill=MUTED)
 text(d,(24,666),'관찰한 화면 경로 + 제작한 손 모양 · 실제 깊이 미확정',19,fill=MUTED)
 panel.save(PANELS/f'panel_{i:05d}.png')

graph='[0:v]scale=960:540,pad=960:720:0:90:color=0x0e131b,setsar=1,fps=30[src];[1:v]setsar=1[guide];[src][guide]hstack=inputs=2[body];[2:v][body][3:v]vstack=inputs=3,fps=30[out]'
run(['-y','-ss','56.5','-i',annotations['source'],'-framerate','30','-i',str(PANELS/'panel_%05d.png'),
 '-framerate','30','-loop','1','-i',str(WORK/'header.png'),'-framerate','30','-loop','1','-i',str(WORK/'footer.png'),
 '-filter_complex',graph,'-map','[out]','-map','0:a?','-t',str(248/30),'-r','30','-fps_mode','cfr',
 '-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart',str(OUT/'comparison.mp4')])
run(['-y','-framerate','30','-i',str(PANELS/'panel_%05d.png'),'-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'guide_only.mp4')])
run(['-y','-i',str(OUT/'comparison.mp4'),'-frames:v','1','-q:v','2',str(OUT/'start_compare.jpg')])

# Compact 5-scene comparison. These are actual encoded-video frames.
sheet=Image.new('RGB',(1280,5*400),BG);d=ImageDraw.Draw(sheet)
for n,(t,label) in enumerate([(0,'시작 파지'),(3.75,'팔 펼침'),(5.75,'아래쪽 이동'),(6.75,'몸통 쪽 굽힘'),(8.20,'다시 펼침')]):
 p=WORK/f'comparison_event_{n}.jpg';run(['-y','-ss',str(t),'-i',str(OUT/'comparison.mp4'),'-frames:v','1','-q:v','2',str(p)])
 still=Image.open(p).crop((0,108,1920,828)).resize((1280,480),Image.Resampling.LANCZOS)
 still=still.resize((960,360),Image.Resampling.LANCZOS);sheet.paste(still,(300,n*400+25))
 text(d,(20,n*400+50),f'{n+1}. {label}',25,bold=True);text(d,(20,n*400+92),f'시연 {t:.2f}초',20,fill=MUTED)
sheet.save(OUT/'keypose_compare.jpg',quality=93)

# Audit image: raw source pixels and only the manual points used as inputs.
audit=Image.open(WORK/'source_frames/56.50.png').resize((960,540));d=ImageDraw.Draw(audit)
first=keys[0]
for key,color in [('left_wrist',CYAN),('left_palm',CYAN),('right_wrist',ORANGE),('right_palm',ORANGE)]:
 x,y=first[key];d.ellipse((x-5,y-5,x+5,y+5),fill=color)
text(d,(20,20),'입력 근거: 원본 화면에서 손목·손바닥 위치를 수동 표시',21,fill=(255,255,255),bold=True)
audit.save(OUT/'source_annotation_example.jpg',quality=94)
(OUT/'render_receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'presentation_receipt.json').write_text(json.dumps(dict(source_video=annotations['source'],source_start_s=56.5,requested_end_s=64.75,
 actual_duration_s=248/30,fps=30,frames=248,comparison_resolution=[1920,900],
 animation_input='This draft video_text_demo.csv generated from manual original-video annotations, text evidence and generic SDK bind geometry only.',
 gray_arm_line='2D explanatory line from manually observed patient shoulder/elbow/wrist, linear between keyframes; not a 3D patient model or joint-angle result.',
 trajectory='View-only path projected from the newly authored wrist positions using the same orthographic camera.',
 original_video='Trimmed, scaled and padded; original audio retained. No landmark overlay in the comparison source panel.',
 output_sha256=sha(OUT/'comparison.mp4')),ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(comparison=str(OUT/'comparison.mp4'),frames=248,tracking_used=False),ensure_ascii=False))
