#!/bin/bash
# The release gate: everything CI runs without Windows, before a tag is pushed - the fast gates, then the editor bundle
# built and every page check of CI's "Test editor integration" step (read from .github/workflows/build.yml, so a check
# added there is run here too). Exit 1 when one fails.
#
#   Tools/Gates/release.sh
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
failed=()
while IFS= read -r line; do
  case "$line" in
    node\ *) if out=$(eval "$line" 2>&1); then echo "ok    $line"; else echo "FAIL  $line"; echo "$out" | grep -E 'FAIL|missing|got|Error' | head -8 | sed 's/^/      /'; failed+=("$line"); fi ;;
    *) eval "$line" || { echo "release gate: \"$line\" failed"; exit 2; } ;;
  esac
done <<<"$script"
if [ ${#failed[@]} -gt 0 ]; then echo "${#failed[@]} page check(s) failed: ${failed[*]}"; exit 1; fi
echo "release gate passed"
