#!/bin/bash
# The fast gates: every check that runs in seconds without Windows, the same ones CI runs (.github/workflows/build.yml),
# in one command - run before committing a batch and before packaging (Tools/dev package scripts stop on a failure).
#
#   Tools/Gates/fast.sh            all of them; exit 1 when one fails, with its name
#
# They had been run by CI only, and CI builds on a tag: a doc index behind the code and a product name in a comment
# stayed red on the migration branch unseen (2026-10-24). An edition adds its own (Typeleaf: its feature texts, the
# Word export) where they exist.
cd "$(dirname "$0")/../.." || exit 2
DOTNET=${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}
# No build server outlives the run: one that did (VBCSCompiler) kept the lock this was run under for good, and the
# next runs waited on it forever (2026-10-24).
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
failed=()
gate() {
  local name=$1; shift
  local out
  if out=$("$@" 2>&1); then echo "ok    $name"; else echo "FAIL  $name"; echo "$out" | tail -15 | sed 's/^/      /'; failed+=("$name"); fi
}
gate "translations (74 languages complete)" python3 Tools/Translations/check.py --strict
gate "brand names only in the brand files" python3 Tools/Branding/check-brand.py
gate "docs/testing.md lists the cases and test methods" python3 Tools/Docs/testing-index.py
gate "the Chinese and English user guides match" python3 Tools/Docs/user-guide-check.py
[ -f Tools/Docs/edition-testing-index.py ] && gate "the edition's case list" python3 Tools/Docs/edition-testing-index.py
[ -f Tools/Store/check-theme-count.py ] && gate "the texts count the supporter themes there are" python3 Tools/Store/check-theme-count.py
[ -f Tools/Translations/check-edition.py ] && gate "the edition's feature texts" python3 Tools/Translations/check-edition.py
[ -f Tools/Sync/core-sync.py ] && gate "each new commit is either common or the edition's own (core-sync gate)" python3 Tools/Sync/core-sync.py gate
# The tests run in UTC, as CI's do: a test that held a fixed +08:00 passed here and failed there (2026-10-25).
export TZ=UTC
gate "automation unit tests" "$DOTNET" test Dev/Typedown.Automation.Tests/Typedown.Automation.Tests.csproj -nologo -v q
gate "reliability tests" "$DOTNET" test Tests/Typedown.ReliabilityTests/Typedown.ReliabilityTests.csproj -nologo -v q
[ -f Tests/Typeleaf.WordExportTests/Typeleaf.WordExportTests.csproj ] && gate "Word export tests" "$DOTNET" test Tests/Typeleaf.WordExportTests/Typeleaf.WordExportTests.csproj -nologo -v q
[ -f Tests/Typeleaf.WritingStatsTests/Typeleaf.WritingStatsTests.csproj ] && gate "writing statistics tests" "$DOTNET" test Tests/Typeleaf.WritingStatsTests/Typeleaf.WritingStatsTests.csproj -nologo -v q
# The editor's unit tests need its packages; a checkout without them fails here rather than passing unseen (the
# Mermaid test went red on CI only, 2026-10-25). SKIP_EDITOR_TESTS=1 leaves them out on purpose, and says so.
editor_tests() {
  [ -x Dev/Typedown.Editor/node_modules/.bin/react-app-rewired ] || { echo "no packages: cd Dev/Typedown.Editor && yarn install --frozen-lockfile"; return 1; }
  (cd Dev/Typedown.Editor && CI=true yarn test --watchAll=false --runInBand)
}
if [ "${SKIP_EDITOR_TESTS:-}" = 1 ]; then echo "skip  editor unit tests (SKIP_EDITOR_TESTS=1)"; else gate "editor unit tests" editor_tests; fi
if [ ${#failed[@]} -gt 0 ]; then echo "${#failed[@]} gate(s) failed: ${failed[*]}"; exit 1; fi
echo "all gates passed"
