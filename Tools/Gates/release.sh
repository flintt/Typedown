#!/bin/bash
# The release gate: everything CI runs without Windows, before a tag is pushed - the fast gates, then the editor bundle
# built and every page check of CI's "Test editor integration" step (read from .github/workflows/build.yml, so a check
# added there is run here too). Exit 1 when one fails.
#
#   Tools/Gates/release.sh           JOBS=1 on this 7.7 GB machine, the default 4 elsewhere (vm103's Checks)
#
# The page checks had run in CI only, and CI builds main and tags, not the branches work happens on: v1.4.2's tag build
# was the first to run them since 1.4.1 and found a paste that lost the blank line before a code block (2026-11-09).
cd "$(dirname "$0")/../.." || exit 2
root=$PWD
Tools/Gates/fast.sh || { echo "release gate: the fast gates failed"; exit 1; }
echo "== the editor bundle"
# Mermaid's own file first, as package.json's prebuild does: a bundle built without it draws no diagram (a fresh
# checkout's Typedown gate, 2026-11-09).
(cd Dev/Typedown.Editor && node scripts/copy-mermaid.js && GENERATE_SOURCEMAP=false npx react-app-rewired build > /tmp/release-gate-build.$$.log 2>&1) \
  || { tail -20 /tmp/release-gate-build.$$.log; echo "release gate: the editor bundle did not build"; exit 1; }
rm -f /tmp/release-gate-build.$$.log
# The step's script: the lines of its "run: |" block, up to the next step.
script=$(awk '
  /- name: Test editor integration/ { step = 1; next }
  step && /^      - / { exit }
  step && /run: \|/ { run = 1; next }
  step && run { sub(/^          /, ""); print }
' .github/workflows/build.yml)
[ -n "$script" ] || { echo "release gate: no \"Test editor integration\" step in build.yml"; exit 2; }
echo "== the page checks ($(grep -c '^node ' <<<"$script") of them)"
cd Tools/EditorBench || exit 2
export NODE_PATH="$root/Dev/Typedown.Editor/node_modules"
export CHROME=${CHROME:-$(command -v google-chrome || command -v google-chrome-stable || true)}
# The step's other lines (its CHROME, a test of it) first; then the checks, JOBS at a time (default 4; 1 on a small
# machine). One that fails beside others is run again alone and fails only if it fails then too: a few checks time
# things (plain-text-check's caret cost), and other checks' Chrome on the same cores slows them.
while IFS= read -r line; do
  case "$line" in node\ *) ;; *) eval "$line" || { echo "release gate: \"$line\" failed"; exit 2; } ;; esac
done <<<"$script"
JOBS=${JOBS:-4}
out=$(mktemp -d)
i=0
while IFS= read -r line; do
  case "$line" in node\ *) ;; *) continue ;; esac
  i=$((i + 1))
  while [ "$(jobs -rp | wc -l)" -ge "$JOBS" ]; do wait -n; done
  ( if eval "$line" > "$out/$i.log" 2>&1; then echo ok > "$out/$i.result"; else echo FAIL > "$out/$i.result"; fi; echo "$line" > "$out/$i.line" ) &
done <<<"$script"
wait
failed=()
for n in $(seq 1 "$i"); do
  line=$(cat "$out/$n.line")
  if [ "$(cat "$out/$n.result")" = ok ]; then echo "ok    $line"; continue; fi
  if [ "$JOBS" -gt 1 ] && eval "$line" > "$out/$n.again.log" 2>&1; then echo "ok    $line (failed beside others, passed alone)"; continue; fi
  echo "FAIL  $line"
  grep -E 'FAIL|missing|got|Error' "$out/$n.log" | head -8 | sed 's/^/      /'
  failed+=("$line")
done
rm -rf "$out"
if [ ${#failed[@]} -gt 0 ]; then echo "${#failed[@]} page check(s) failed: ${failed[*]}"; exit 1; fi
echo "release gate passed"
