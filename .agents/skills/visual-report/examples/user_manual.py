import base64, sys

S = sys.argv[1]
b64 = lambda p, t='jpeg': f"data:image/{t};base64," + base64.b64encode(open(p, 'rb').read()).decode()
home, meas, ctrl = b64(S + '/f8.jpg'), b64(S + '/f21.jpg'), b64('D:/ctrl_photo.png', 'png')


def pins(img, marks):
    # marks: (x%, y%, 번호)
    p = ''.join(f"<span class='pin' style='left:{x}%;top:{y}%'>{n}</span>" for x, y, n in marks)
    return f"<div class='shot'><img src='{img}'>{p}</div>"


body = f"""
<div class="cover">
<h1>경추 ROM 선 실측 — 사용 매뉴얼</h1>
<p class="by">V0.1 · 2026-10-15 · 헤드셋 앱 v1.42 (선 실측) 기준 · 컨트롤러 하나로 조작</p>
</div>

<h2>1. 무엇을 재는 프로그램인가</h2>
<p>환자 얼굴의 두 점을 컨트롤러 끝(촉)으로 찍어 <b>선</b>을 만들고, 자세마다 그 선이 얼마나 돌았는지를 각도로 잰다.</p>
<div class="flow3"><div><b>중립</b><br>정면을 본 편안한 자세</div><span>→</span><div><b>능동</b><br>환자 스스로 끝까지</div><span>→</span><div><b>압박</b><br>측정자가 같은 방향으로 조금 더</div></div>
<p>한 동작에서 직선 셋(중립·능동·압박)을 찍는다. 결과는 <b>중립선과 능동선 사이 각</b>, <b>중립선과 압박선 사이 각</b>이다. 여섯 동작(굴곡·신전·좌우 측굴·좌우 회전)을 재면 한 사람 측정이 끝난다.</p>

<h2>2. 화면 구성</h2>
{pins(home, [(55, 12, 1), (64, 35, 2), (24, 41, 3), (68, 45, 4), (52, 70, 5), (88, 84, 6)])}
<table class="legend">
<tr><td class="n">1</td><td><b>벽면UI · 결과표</b></td><td>멀리 서 있는 큰 화면. 동작마다 능동 · 압박 값과 각도기 그림이 채워진다</td></tr>
<tr><td class="n">2</td><td><b>탭 줄</b></td><td>홈 · 굴곡·신전 · 측굴 · 회전 · 자유 측정(지금 방식 이름이 보임: 3점 / 2선 / 연속). 파란 칸 = 지금 탭, 초록 글자 = 다 잰 탭</td></tr>
<tr><td class="n">3</td><td><b>벽면UI 안내 바</b></td><td>지금 할 일과 버튼 설명 한 줄</td></tr>
<tr><td class="n">4</td><td><b>컨트롤러UI</b></td><td>컨트롤러 바로 위에 따라다니는 작은 안내. 탭 줄 + 지금 찍을 점(●○) + 버튼 설명</td></tr>
<tr><td class="n">5</td><td><b>끝 구슬</b></td><td>찍히는 자리. <span class="y">노랑</span> = 찍을 수 있음 · <span class="r">빨강</span> = 추적을 놓침(찍히지 않음)</td></tr>
<tr><td class="n">6</td><td><b>바닥 축</b></td><td>바닥의 노란 십자와 «환자 시선 방향» 화살표. 환자 정면을 여기에 맞춘다</td></tr>
</table>

<h2>3. 컨트롤러</h2>
<div class="two"><div class="ctrl"><img src='{ctrl}'><p class="cap">오른손 컨트롤러. 트리거는 링 아래 검지 자리. 그립은 쓰지 않는다. 왼손으로 쥐면 X = A, Y = B.</p></div>
<table class="btn">
<tr><th>버튼</th><th>홈</th><th>굴곡·신전 · 측굴 · 회전</th><th>자유 측정</th></tr>
<tr><td>트리거</td><td>컨트롤러 끝점 보정 시작 / 끝</td><td>점 찍기 · 세 직선을 다 찍은 뒤 한 번 더 = <b>확인(기록)</b></td><td>점 찍기 · 3점 · 2선은 다 찍은 뒤 한 번 더 = 확인</td></tr>
<tr><td>A</td><td>리셋(확인 창에서 A = 지우기, B = 취소)</td><td>—</td><td>—</td></tr>
<tr><td>B 짧게</td><td>자유 측정 방식 바꾸기 3점 → 2선 → 연속</td><td>직선 하나 취소(찍다 만 점이 있으면 그 점)</td><td>마지막 점 · 묶음 취소</td></tr>
<tr><td>B 길게 (1초)</td><td>방식 바꾸기</td><td>이 탭 지우기(재다가 그만둘 때도)</td><td>이 방식의 점 지우기</td></tr>
<tr><td>스틱 ◀ ▶</td><td colspan="3">탭 옮기기. 재는 중인 탭에서는 잠긴다(B 길게로 지우면 풀림)</td></tr>
<tr><td>스틱 누르고 밀기</td><td colspan="3">바닥 축 다시 잡기 — 헤드셋이 환자 쪽을 보게 두고, 누른 뒤 8초 안에 아무 방향으로 민다</td></tr>
<tr><td>∞ 길게</td><td colspan="3">뷰 재설정(Quest 기능) — 지금 선 자리로 원점을 다시 잡는다. 벽면UI와 바닥 축이 그 앞에 다시 선다</td></tr>
</table></div>

<h2>4. 측정 순서</h2>
<ol class="steps">
<li><b>준비</b> — 앱을 켜면 홈 탭에서 시작한다. ∞ 버튼을 길게 눌러 뷰를 재설정하고, 환자의 시선을 바닥의 «환자 시선 방향» 화살표에 맞춘다.</li>
<li><b>촉 확인</b> — 촉을 새로 달았거나 바꿨으면 홈에서 트리거 → 촉 끝을 한 점에 댄 채 컨트롤러를 여러 방향으로 크게 돌리기 → 트리거. 보정값은 기기에 저장돼 다음 실행에도 쓰인다.</li>
<li><b>탭 고르기</b> — 스틱 ▶로 «굴곡·신전»부터. 순서대로 재면 다음 동작 · 다음 탭으로 저절로 넘어간다.</li>
<li><b>찍기</b> — 중립 · 능동 · 압박 자세마다 두 점을 순서대로 찍는다(아래 표). 컨트롤러UI의 ○가 하나씩 ●로 바뀐다. 잘못 찍었으면 B 짧게.</li>
<li><b>확인</b> — 세 직선을 다 찍으면 컨트롤러UI에 능동 · 압박 각이 뜬다. 환자를 중립으로 돌린 뒤 트리거를 당기면 결과표에 기록된다. 마지막 점을 찍고 0.5초 동안은 확인을 받지 않는다(연달아 눌림 방지).</li>
<li><b>끝</b> — 세 탭(여섯 동작)을 다 재면 홈으로 돌아온다. 값은 벽면UI 결과표와 각 탭의 컨트롤러UI에 남는다. 다시 재려면 그 탭에서 B 길게, 전부 지우려면 홈에서 A.</li>
</ol>
<div class="two">
<table class="pts"><tr><th>동작</th><th>찍는 두 점 (순서대로)</th></tr>
<tr><td>굴곡 · 신전</td><td>미간점 → 턱끝점</td></tr>
<tr><td>좌측굴 · 우측굴</td><td>미간점 → 턱끝점</td></tr>
<tr><td>우회전</td><td>미간점 → 왼쪽 이주점</td></tr>
<tr><td>좌회전</td><td>미간점 → 오른쪽 이주점</td></tr></table>
<div class="ctrl">{pins(meas, [(23, 50, 'a'), (76, 58, 'b'), (60, 67, 'c')])}<p class="cap">회전 탭 · 우회전 중립을 찍기 전. ⓐ 벽면UI 안내 «우회전 · 중립 ○○» ⓑ 컨트롤러UI ⓒ 다음에 찍을 점 이름(사진은 10-16 순서 변경 전 — 지금 회전은 미간점부터 찍는다)</p></div>
</div>

<h2>5. 자유 측정</h2>
<p>여섯 동작과 별개로, 원하는 곳의 각을 잰다. 홈에서 B를 눌러 방식을 고른 뒤 스틱으로 자유 측정 탭에 들어간다.</p>
<table><tr><th>방식</th><th>찍는 법</th><th>나오는 값</th></tr>
<tr><td><b>3점</b></td><td>세 점을 이어 찍는다 → 트리거로 확인</td><td>가운데 점에서 두 선이 벌어진 각. 둘째 묶음부터는 첫 묶음 · 바로 앞 묶음과의 차도 보인다</td></tr>
<tr><td><b>2선</b></td><td>두 점씩 선 둘(1-2 · 3-4) → 트리거로 확인. 안내는 «●● ●○»처럼 선마다 띄어 보인다</td><td>두 선의 방향이 이루는 각</td></tr>
<tr><td><b>연속</b></td><td>점을 이어 가며 찍는다(확인 없이 바로 기록)</td><td>셋째 점부터, 앞 꼭짓점에서 꺾인 각이 하나씩</td></tr></table>

<h2>6. 결과와 기록</h2>
<ul>
<li>결과는 벽면UI 결과표(동작별 능동 · 압박)에 보인다. 다 잰 탭에서는 컨트롤러UI에도 그 탭의 기록이 뜬다.</li>
<li>찍은 선과 값은 앱을 켤 때마다 CSV 파일 하나로 기기에 저장된다 — <code>Android/data/com.CoCo.CHUNA/files/romline_날짜_시각.csv</code>. PC에 USB로 연결해 꺼낸다.</li>
<li>값은 «면 각»이다: 굴곡·신전은 옆면, 측굴은 앞면, 회전은 바닥면에 두 선을 내려 잰 각. 두 선 사이 3차원 각(«직접 각»)도 CSV에 함께 남는다. 두 값의 차이는 별도 문서 «직접 각과 면 각 비교»에 있다.</li>
</ul>

<h2>7. 정확하게 재려면</h2>
<ul>
<li><b>화면의 노란 점이 아니라 실물 촉 끝을 기준으로 찍는다.</b> 컨트롤러를 가까이 들면 패스스루 화면에서 촉과 노란 점이 몇 cm 어긋나 보이지만, 기록되는 점은 실물 촉 자리에 있다(1.2m 세로 모서리 시험에서 수직 0.2~0.6° · 별도 문서 «끝점 정밀도 시험»).</li>
<li>컨트롤러를 헤드셋 <b>정면 시야 안</b>에 두고, <b>윗면(링 쪽)이 헤드셋을 향하게</b> 한다. 헤드셋 뒤 · 머리 위 · 링이 반대쪽을 볼 때 추적을 놓친다.</li>
<li>촉을 점에 대고 <b>잠깐 멈춘 뒤</b> 트리거를 당긴다. 빠르게 움직인 직후에는 추적을 놓치기 쉽다.</li>
<li>점 1mm 어긋남은 각으로 약 1.25°다(점 간격 기준 계산). 같은 점을 같은 자리에 찍는 것이 가장 중요하다.</li>
</ul>

<h2>8. 문제가 생기면</h2>
<table><tr><th>증상</th><th>원인</th><th>할 일</th></tr>
<tr><td>끝 구슬이 빨갛고 점이 안 찍힌다</td><td>헤드셋이 컨트롤러를 못 보고 있다</td><td>컨트롤러를 시야 안으로, 링을 헤드셋 쪽으로</td></tr>
<tr><td>노란 점이 컨트롤러 끝에서 떨어져 보인다</td><td>패스스루 화면의 어긋남(값은 정상)</td><td>실물 촉 끝을 기준으로 찍는다</td></tr>
<tr><td>스틱으로 탭이 안 넘어간다</td><td>재는 중인 탭은 잠긴다</td><td>끝까지 재거나, B 길게로 그 탭을 지운다</td></tr>
<tr><td>트리거를 당겨도 기록이 안 된다</td><td>마지막 점 직후 0.5초 · 또는 아직 직선을 다 안 찍었다</td><td>컨트롤러UI의 ●○를 보고 남은 점을 찍은 뒤 다시 트리거</td></tr>
<tr><td>벽면UI · 바닥 축이 엉뚱한 곳에 있다</td><td>원점이 다른 자리에 잡혔다</td><td>∞ 길게로 뷰 재설정</td></tr>
<tr><td>바닥 축 방향이 환자와 안 맞는다</td><td>축이 다른 방향으로 잡혔다</td><td>스틱 누르고 환자 쪽으로 밀기</td></tr>
<tr><td>촉을 바꾼 뒤 값이 이상하다</td><td>끝점 보정값이 예전 촉 기준</td><td>홈에서 트리거로 끝점 보정</td></tr>
</table>
<p class="foot">이 매뉴얼은 2026-10-15 기준 헤드셋 앱(커밋 7171a01)의 코드와 기기 녹화로 확인한 동작을 적었다. 화면 사진은 같은 날 기기 녹화에서 잘랐다.</p>
"""
css = """@page{size:A4;margin:14mm 13mm}body{font-family:'Malgun Gothic',sans-serif;font-size:10.3pt;line-height:1.6;color:#1a1a1a;max-width:820px;margin:auto;background:#fff}
h1{font-size:21pt;margin:0}.by{color:#666;margin:2px 0 6px;font-size:9.5pt}.cover{border-bottom:4px solid #FF6B05;padding-bottom:6px;margin-bottom:6px}
h2{font-size:13.5pt;margin:20px 0 8px;padding-bottom:3px;border-bottom:2px solid #FF6B05;break-after:avoid}
table{border-collapse:collapse;width:100%;margin:8px 0;font-size:9.4pt;break-inside:avoid}th,td{border:1px solid #ccc;padding:5px 7px;text-align:left;vertical-align:top}th{background:#f3f3f3}
.flow3{display:flex;align-items:center;gap:8px;margin:8px 0}.flow3 div{flex:1;background:#FFF4EC;border:1px solid #F5C9A6;border-radius:6px;padding:6px 8px;text-align:center;font-size:9.6pt}.flow3 span{color:#FF6B05;font-weight:700}
.shot{position:relative;break-inside:avoid;margin:6px 0}.shot img{width:100%;display:block;border-radius:4px}
.pin{position:absolute;transform:translate(-50%,-50%);width:22px;height:22px;border-radius:50%;background:#FF6B05;color:#fff;font-weight:700;font-size:12px;text-align:center;line-height:22px;border:2px solid #fff;box-shadow:0 0 3px #000}
.two>.shot{width:62%;margin:auto}
.legend td.n{width:22px;text-align:center;font-weight:700;color:#fff;background:#FF6B05}.legend td:nth-child(2){width:120px}
.y{color:#B8860B;font-weight:700}.r{color:#C0392B;font-weight:700}
.two{display:flex;gap:12px;align-items:flex-start;break-inside:avoid}.two .ctrl{flex:0 0 34%}.two .ctrl img{width:100%}.two table{flex:1;margin-top:0}
.cap{font-size:8.8pt;color:#555;margin:3px 0}
.btn td:first-child{font-weight:700;white-space:nowrap}
ol.steps{padding-left:22px}ol.steps li{margin:4px 0}
code{font-size:8.8pt;background:#f3f3f3;padding:0 3px}
.foot{font-size:8.8pt;color:#666;margin-top:16px;border-top:1px solid #ddd;padding-top:6px}"""
open(S + '/manual.html', 'w', encoding='utf-8').write(
    f"<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>경추 ROM 선 실측 매뉴얼</title><style>{css}</style></head><body>{body}</body></html>")
print('ok')
