#!/usr/bin/env bash
# 퀘스트 부하 · 발열 기록 (11-13 신설 — 사용자 «동기 방식으로 바꿨을 때 성능이랑 발열 기기 부담 체크»)
#   쓰는 법: quest-perf.sh <이름표> [초=300] [간격=5] [--serial=…]
#   N초 동안 간격마다: 배터리 온도 · 잔량 · 충전 여부 · 열 상태 · 앱 CPU%(top) · GPU 사용률(kgsl, 읽히면) · 앱 fps/미러 진단(로그)
#   결과: D:/추나녹화/<오늘>/perf_<이름표>_<시각>.csv + 같은 이름 .log(그 시간의 [웹미러] 로그)
# 읽기 전용 — 기기 설정을 바꾸지 않는다(로그 버퍼도 지우지 않는다).
set -u
ADB="${LOCALAPPDATA:-/c/Users/USER/AppData/Local}/Android/Sdk/platform-tools/adb.exe"
PKG=com.CoCo.CHUNA
LABEL="${1:?이름표를 준다(예: async · sync)}"; DUR="${2:-300}"; STEP="${3:-5}"
SER=""; for a in "$@"; do case "$a" in --serial=*) SER="-s ${a#--serial=}";; esac; done
A="$ADB $SER"
$A get-state >/dev/null 2>&1 || { echo "★기기 없음"; exit 1; }
DAY=$(date +%Y%m%d); OUT="/d/추나녹화/$DAY"; mkdir -p "$OUT"
BASE="$OUT/perf_${LABEL}_$(date +%H%M%S)"
PID=$($A shell pidof $PKG | tr -d '\r')
[ -z "$PID" ] && { echo "★앱이 안 떠 있다($PKG)"; exit 1; }
echo "t,batt_temp_C,level,plugged,thermal,app_cpu_pct,gpu_busy_pct" > "$BASE.csv"
$A logcat -T 1 -v time Unity:I '*:S' > "$BASE.log" 2>/dev/null &
LP=$!
t0=$(date +%s)
while :; do
  t=$(( $(date +%s) - t0 )); [ $t -gt "$DUR" ] && break
  B=$($A shell dumpsys battery | tr -d '\r')
  temp=$(echo "$B" | awk -F': ' '/temperature/{printf "%.1f",$2/10}')
  lvl=$(echo "$B" | awk -F': ' '/^  level/{print $2}')
  plug=$(echo "$B" | awk -F': ' '/AC powered|USB powered/{if($2=="true")p=1} END{print p+0}')
  th=$($A shell dumpsys thermalservice 2>/dev/null | tr -d '\r' | awk -F': ' '/Thermal Status/{print $2; exit}')
  cpu=$($A shell top -b -n 1 -p "$PID" 2>/dev/null | tr -d '\r' | awk -v p="$PID" '$1==p{print $9}')
  gpu=$($A shell cat /sys/class/kgsl/kgsl-3d0/gpu_busy_percentage 2>/dev/null | tr -d '\r %')
  echo "$t,$temp,$lvl,$plug,$th,$cpu,$gpu" | tee -a "$BASE.csv"
  sleep "$STEP"
done
kill $LP 2>/dev/null
echo "끝 — $BASE.csv · $BASE.log"
grep -c "영상 진단" "$BASE.log" | xargs echo "미러 진단 줄:"
