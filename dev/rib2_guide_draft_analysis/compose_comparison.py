"""Package an isolated Unity hand render beside the unchanged source clip."""
from pathlib import Path
import csv, json, math, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageChops

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2GuideDraft_V0.1_20261026'
RENDER=ROOT/'Temp/rib2-guide-full-c'
WORK=ROOT/'Temp/rib2-guide-composite'
WORK.mkdir(exist_ok=True)
PANELS=WORK/'panels';PANELS.mkdir(exist_ok=True)
BG=(13,18,26); FG=(233,239,246); MUTED=(161,178,197); CYAN=(39,207,240); ORANGE=(255,157,77)
FONT=Path('C:/Windows/Fonts/malgun.ttf')
BOLD=Path('C:/Windows/Fonts/malgunbd.ttf')
def font(size,bold=False):return ImageFont.truetype(str(BOLD if bold else FONT),size)
def label(d,xy,text,size=24,fill=FG,bold=False):d.text(xy,text,font=font(size,bold),fill=fill)
def run(args):subprocess.run(['ffmpeg','-hide_banner','-loglevel','error',*args],check=True)

receipt=json.loads((RENDER/'capture-receipt.json').read_text(encoding='utf-8-sig'))
assert receipt['success'] and receipt['framesWritten']==248
frames=sorted(RENDER.glob('hand_*.png'))
boxes=[]
for path in frames:
    im=Image.open(path).convert('RGB')
    diff=np.max(np.abs(np.asarray(im,dtype=int)-np.asarray(im.getpixel((0,0)))),axis=2)>40
    yy,xx=np.where(diff)
    boxes.append((int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max())))
x0,y0,x1,y1=min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes)
cx,cy=(x0+x1)/2,(y0+y1)/2
cw=max((x1-x0)*1.28,(y1-y0)*1.28*4/3);ch=cw*3/4
crop=(round(cx-cw/2),round(cy-ch/2),round(cx+cw/2),round(cy+ch/2))

with (OUT/'guide_demo_draft.csv').open(encoding='utf-8',newline='') as f:
    rows=list(csv.DictReader(f))
roots=[np.array([float(r['WorldPos'+c]) for c in 'XYZ']) for r in rows if r['HandType']=='Left' and r['JointID']=='1']
assert len(roots)==len(frames)
yy,xx,zz=np.deg2rad([receipt['cameraYaw'],receipt['cameraPitch'],receipt['cameraRoll']])
ry=np.array([[math.cos(yy),0,math.sin(yy)],[0,1,0],[-math.sin(yy),0,math.cos(yy)]])
rx=np.array([[1,0,0],[0,math.cos(xx),-math.sin(xx)],[0,math.sin(xx),math.cos(xx)]])
rz=np.array([[math.cos(zz),-math.sin(zz),0],[math.sin(zz),math.cos(zz),0],[0,0,1]])
rot=ry@rx@rz
cam=np.array([receipt['cameraPosition'][c] for c in 'xyz'])
k=720/(2*receipt['orthographicSize'])
screen=(np.array(roots)-cam)@rot
path=[((480+v[0]*k-crop[0])*960/(crop[2]-crop[0]),(360-v[1]*k-crop[1])*720/(crop[3]-crop[1])) for v in screen]

header=Image.new('RGB',(1920,108),BG);d=ImageDraw.Draw(header)
label(d,(32,13),'제2늑골 · 시작 파지와 팔 움직임 한 주기',31,bold=True)
label(d,(1610,22),'별도 시연 초안 v0.1',23,fill=MUTED)
label(d,(32,68),'원본 3인칭 영상',25,bold=True)
label(d,(992,68),'가이드핸드 · 원본 손 포즈 + 보간',25,bold=True)
d.line((960,62,960,108),fill=(48,65,84),width=2)
header.save(WORK/'header.png')
footer=Image.new('RGB',(1920,72),BG);d=ImageDraw.Draw(footer)
label(d,(32,14),'영상 56.50–64.75초  |  시작 파지 → 팔 한 주기',22)
label(d,(992,7),'● 팔 이동 손',21,fill=CYAN)
label(d,(1200,7),'● 흉곽 지지 손',21,fill=ORANGE)
label(d,(992,36),'영상 대응 제작 타이밍 · 환자 기준 정렬과 깊이 미검증',19,fill=MUTED)
footer.save(WORK/'footer.png')

for i,src in enumerate(frames):
    im=Image.open(src).convert('RGB')
    # Path is projected from the same unchanged wrist roots and capture camera.
    # Keep the original rendered hand pixels in front of the explanatory trace.
    background=Image.new('RGB',im.size,im.getpixel((0,0)))
    trace=Image.new('RGB',(960,720),background.getpixel((0,0)))
    td=ImageDraw.Draw(trace)
    td.line(path,fill=(26,66,81),width=3)
    if i>0:td.line(path[:i+1],fill=(28,134,159),width=4)
    hand=im.crop(crop).resize((960,720),Image.Resampling.LANCZOS)
    mask=ImageChops.difference(hand,Image.new('RGB',hand.size,im.getpixel((0,0)))).convert('L').point(lambda v:255 if v>15 else 0)
    trace.paste(hand,(0,0),mask)
    d=ImageDraw.Draw(trace)
    stage='시작 파지' if i/30<1.5 else ('팔 펴기 준비' if i/30<3.75 else '팔 움직임 한 주기')
    label(d,(28,22),stage,25,bold=True)
    label(d,(28,59),f'{i/30:04.2f} / 8.25초',19,fill=MUTED)
    label(d,(28,673),'손가락 모양 고정 · 왼손 손목 위치/회전 보간',20,fill=MUTED)
    trace.save(PANELS/f'panel_{i:05d}.png')

source='D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV'
graph='[0:v]scale=960:540,pad=960:720:0:90:color=0x0d121a,setsar=1,fps=30[src];[1:v]setsar=1[guide];[src][guide]hstack=inputs=2[body];[2:v][body][3:v]vstack=inputs=3,fps=30[out]'
run(['-y','-ss','56.5','-i',source,'-framerate','30','-i',str(PANELS/'panel_%05d.png'),
     '-framerate','30','-loop','1','-i',str(WORK/'header.png'),'-framerate','30','-loop','1','-i',str(WORK/'footer.png'),
     '-filter_complex',graph,'-map','[out]','-map','0:a?','-t',str(248/30),
     '-r','30','-fps_mode','cfr','-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p',
     '-c:a','aac','-b:a','160k','-movflags','+faststart',str(OUT/'comparison.mp4')])
run(['-y','-framerate','30','-i',str(PANELS/'panel_%05d.png'),'-c:v','libx264','-preset','medium',
     '-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'guide_only.mp4')])
run(['-y','-ss','0','-i',str(OUT/'comparison.mp4'),'-frames:v','1','-q:v','2',str(OUT/'start_compare.jpg')])
sheet=Image.new('RGB',(1920,275*5),BG);d=ImageDraw.Draw(sheet)
for n,(t,title) in enumerate([(0,'시작 파지'),(3.75,'팔 펴기'),(5.75,'낮추기'),(6.75,'굽히기'),(8.20,'다시 펴기')]):
    still=WORK/f'event_{n}.jpg'
    run(['-y','-ss',str(t),'-i',str(OUT/'comparison.mp4'),'-frames:v','1','-q:v','2',str(still)])
    image=Image.open(still).crop((0,108,1920,828)).resize((1280,480))
    # Full comparison is preserved in video; this compact sheet is a 5-event overview.
    image=image.resize((640,240),Image.Resampling.LANCZOS)
    sheet.paste(image,(380,n*275+24))
    label(d,(32,n*275+45),f'{n+1}. {title}',26,bold=True)
    label(d,(32,n*275+90),f'시연 {t:.2f}초',21,fill=MUTED)
    label(d,(1055,n*275+45),'왼쪽: 원본 영상',23)
    label(d,(1055,n*275+84),'오른쪽: 가이드핸드',23,fill=CYAN)
sheet=sheet.crop((0,0,1500,1375));sheet.save(OUT/'keypose_compare.jpg',quality=93)
(OUT/'presentation_receipt.json').write_text(json.dumps(dict(render_receipt='render_receipt.json',
    source_segment_start_s=56.5,source_segment_requested_end_s=64.75,frame_count=len(frames),fps=30,
    encoded_duration_s=248/30,guide_crop_in_original_960x720=crop,
    trajectory='Projected from unchanged Left wrist roots with the same Unity camera, view-only. No patient registration.',
    original_video_audio='Preserved where present; original clip trimmed and scaled only.',
    timing='Authored event correspondence, not measured camera/tracker synchronization.'),ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'render_receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(video=str(OUT/'comparison.mp4'),crop=crop,frames=len(frames)),ensure_ascii=False))
