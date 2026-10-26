"""Video-only input preparation. Never opens tracking or previous draft pose data."""
from pathlib import Path
import hashlib,json,subprocess
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Captures/Rib2VideoTextOnly_V0.1_20261026'
WORK=ROOT/'Temp/rib2-video-text-only'
OUT.mkdir(parents=True,exist_ok=True);WORK.mkdir(exist_ok=True)
SOURCE=Path('D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV')
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(4194304),b''):h.update(b)
 return h.hexdigest()
protected=list((ROOT/'Assets/Resources/HandPoseData').glob('제2늑골*.csv'))+[
 ROOT/'Assets/Resources/Scenarios/제2늑골_상방변위.csv',ROOT/'Assets/Resources/ScenarioConfigs/제2늑골_상방변위.asset',
 ROOT/'Assets/Scenes/TrainingScene.unity',ROOT/'Assets/Scenes/lobby.unity']
snapshot=OUT/'preservation_before.json'
if not snapshot.exists():snapshot.write_text(json.dumps({str(p):sha(p) for p in protected},ensure_ascii=False,indent=2),encoding='utf-8')
evidence=WORK/'source_frames';evidence.mkdir(exist_ok=True)
times=[56.5,58,59,60.25,61,62.25,63.25,64,64.75]
for t in times:
 p=evidence/f'{t:.2f}.png'
 if not p.exists():subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-n','-ss',str(t),'-i',str(SOURCE),'-frames:v','1',str(p)],check=True)
 # Higher-resolution hands-only crops are evidence inspection, not pose sources.
 im=Image.open(p)
 im.crop((650,750,1550,1080)).save(evidence/f'{t:.2f}_hands.png')
(OUT/'video_input.json').write_text(json.dumps(dict(source=str(SOURCE),sha256=sha(SOURCE),
    start_s=56.5,end_s=64.75,observation_frames_s=times,
    method='Manual observation of source pixels. No recorded tracking CSV or earlier draft pose/trajectory is read.',
    protected_data_hashes='preservation_before.json: opaque integrity hashing only, never an animation input.'),ensure_ascii=False,indent=2),encoding='utf-8')
print(str(evidence))
