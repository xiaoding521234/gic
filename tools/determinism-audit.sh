#!/usr/bin/env bash
# 确定性审计（F0-1，docs/active/50 §6）
# 用途：扫描模拟层的非确定性来源，作为帧同步改造的回归闸门。
# 用法：tools/determinism-audit.sh [--strict]
#   --strict：发现硬违规（Random/Time/DateTime/Guid）时返回非 0，供 CI 使用。
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1

SIM_DIRS=(
  "Assets/_Scripts/Battle/Flow"
  "Assets/_Scripts/Battle/Skill"
  "Assets/_Scripts/Battle/Buff"
  "Assets/_Scripts/Battle/Unit"
  "Assets/_Scripts/Data/Battle"
)

# 注意：BSD grep 要求选项在路径操作数之前，模式也必须在目录前。
GREP_ARGS=(-rn --include="*.cs" -E)

STRICT=0
[[ "${1:-}" == "--strict" ]] && STRICT=1

hard=0
warn=0

section() { printf '\n=== %s ===\n' "$1"; }

hard_check() {
  local name="$1" pattern="$2"
  local out
  out=$(grep "${GREP_ARGS[@]}" "$pattern" "${SIM_DIRS[@]}" 2>/dev/null | grep -v "DETERMINISM-ALLOW" | grep -vE ':[0-9]+:[[:space:]]*///?')
  section "$name"
  if [[ -n "$out" ]]; then
    echo "$out"
    hard=1
  else
    echo "OK（无命中）"
  fi
}

warn_check() {
  local name="$1" pattern="$2"
  local out
  out=$(grep "${GREP_ARGS[@]}" "$pattern" "${SIM_DIRS[@]}" 2>/dev/null | grep -v "DETERMINISM-ALLOW" | grep -vE ':[0-9]+:[[:space:]]*///?')
  section "$name"
  if [[ -n "$out" ]]; then
    echo "$out"
    warn=1
  else
    echo "OK（无命中）"
  fi
}

hard_check "硬违规：UnityEngine.Random" 'UnityEngine\.Random|Random\.(Range|value|insideUnit)'
hard_check "硬违规：墙钟/帧时间" 'DateTime\.(Now|UtcNow)|Time\.(deltaTime|time|realtimeSinceStartup|fixedDeltaTime)|Guid\.NewGuid'
hard_check "硬违规：GetHashCode 参与逻辑" 'GetHashCode\(\)'

warn_check "浮点超越函数（需逐点确认；比较/取整前转整数）" 'Mathf\.(Sqrt|Sin|Cos|Tan|Atan2|Pow|Exp|Log)'
warn_check "字典遍历（结算路径需显式排序）" 'foreach \(var .* in (sim|_sim|_units|_state)\.(Units|ActiveBuffs|ResourceNodes)'
warn_check "排序（确认全序 + tie-break）" '\.(Sort|OrderBy|OrderByDescending|ThenBy)\('

printf '\n=== 汇总 ===\n'
if [[ $hard -eq 1 ]]; then
  echo "FAIL：存在硬违规（Random/Time/DateTime/Guid/GetHashCode）"
else
  echo "PASS：无硬违规"
fi
if [[ $warn -eq 1 ]]; then
  echo "WARN：存在需人工确认的浮点/遍历/排序点（见上）"
fi

if [[ $STRICT -eq 1 && $hard -eq 1 ]]; then
  exit 1
fi
exit 0