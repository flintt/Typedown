#!/usr/bin/env python3
"""Checks transcripts from scenario.py against the v1 schema, normalizes what legitimately differs (ids, paths,
versions) and compares them: exit 0 when every platform answered the same.

    python compare.py windows.json uno.json

docs/automation-fixtures/equivalence-windows.json is the Windows answer recorded by E2E EQ01 (refresh it when the
scenario changes), so the Uno edition can be compared without a Windows machine.
"""
import json, re, sys, os

HERE = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(HERE, "..", "..", "..", "docs", "automation-schema", "v1.json")
# settingsRevision counts every settings change since the store was made: its value is each platform's own.
VOLATILE = {"documentId", "windowId", "operationId", "instanceId", "version", "commit", "platform", "sessionId", "path",
            "settingsRevision", "baseSettingsRevision"}


def validate(transcript, name):
    try:
        import jsonschema
    except ImportError:
        print("jsonschema not installed: replies not checked against the schema")
        return 0
    schema = json.load(open(SCHEMA, encoding="utf-8"))
    problems = 0
    for entry in transcript:
        method = entry.get("method")
        if not method:
            continue
        kind = "result" if "result" in entry else "error"
        definition = f"{method}.result" if kind == "result" else "error"
        if definition not in schema.get("$defs", {}):
            continue
        sub = {"$ref": f"#/$defs/{definition}", "$defs": schema["$defs"]}
        try:
            jsonschema.validate(entry[kind], sub)
        except jsonschema.ValidationError as e:
            problems += 1
            print(f"{name}: {entry['step']}: {kind} does not match {definition}: {e.message}")
    return problems


# What legitimately differs between platforms or builds, beyond ids: the build type (test host or application), and
# for an untitled document its localized title and the platform's default line ending.
def normalize(value, key=None):
    if isinstance(value, dict):
        value = dict(value)
        if "path" in value and value.get("path") is None:
            if "title" in value: value["title"] = "<untitled>"
            if "lineEnding" in value: value["lineEnding"] = "<platform default>"
        if key == "server" and "buildType" in value:
            value["buildType"] = "<buildType>"
        return {k: normalize(v, k) for k, v in sorted(value.items()) if k not in ("capabilities", "methods")}
    if isinstance(value, list):
        return [normalize(v) for v in value]
    if key in VOLATILE and value is not None:
        return f"<{key}>"
    if isinstance(value, str):
        return re.sub(r"[0-9a-f]{32}", "<id>", value)
    return value


def main():
    names = sys.argv[1:]
    transcripts = [json.load(open(n, encoding="utf-8")) for n in names]
    problems = sum(validate(t, n) for t, n in zip(transcripts, names))
    base = [normalize(e) for e in transcripts[0]]
    for other, name in zip(transcripts[1:], names[1:]):
        steps = [normalize(e) for e in other]
        for i in range(max(len(base), len(steps))):
            a = base[i] if i < len(base) else None
            b = steps[i] if i < len(steps) else None
            if a != b:
                problems += 1
                print(f"step {i} ({(a or b).get('step')}) differs:\n  {names[0]}: {json.dumps(a, ensure_ascii=False)[:600]}\n  {name}: {json.dumps(b, ensure_ascii=False)[:600]}")
    print(f"{len(base)} steps, {problems} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
