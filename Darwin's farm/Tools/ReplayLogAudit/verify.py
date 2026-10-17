"""Verify and compare Darwin's Farm JSONL test submissions (schema 4).

Usage: python Tools/ReplayLogAudit/verify.py file.jsonl [more.jsonl ...]
"""

import hashlib
import json
import re
import sys
from pathlib import Path


HASH_RE = re.compile(r"^[0-9a-f]{64}$", re.I)
IGNORED_ACTIONS = {"checkpoint", "speed"}


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def digest(value):
    return hashlib.sha256(compact(value).encode("utf-8")).hexdigest()


def normalized_snapshot(record):
    result = dict(record)
    result.pop("secondsPerDay", None)
    result.pop("speed", None)
    result.pop("paused", None)
    result["tiles"] = sorted((dict(tile) for tile in result.get("tiles", [])),
                             key=lambda tile: (tile["x"], tile["y"]))
    for tile in result["tiles"]:
        tile["populations"] = sorted(tile.get("populations", []),
                                     key=lambda population: population.get("id", ""))
    return result


def normalized_action(record):
    if record.get("operation") in IGNORED_ACTIONS or record.get("source") in {"unity", "map"}:
        return None
    return {key: record.get(key) for key in
            ("day", "source", "operation", "x", "y", "detail")}


def load(path):
    raw = Path(path).read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise ValueError("UTF-8 BOM is not supported")
    lines = raw.decode("utf-8").splitlines()
    if not lines or any(not line for line in lines):
        raise ValueError("empty or missing JSONL records")
    records = [json.loads(line) for line in lines]
    header, end = records[0], records[-1]
    if header.get("type") != "header" or end.get("type") != "end":
        raise ValueError("header or end record missing")
    if header.get("schema") != 4 or not header.get("deterministic"):
        raise ValueError("requires a deterministic schema-4 export")
    if any(record.get("type") in {"header", "end"} for record in records[1:-1]):
        raise ValueError("duplicate header or premature end")
    if not any(record.get("type") == "snapshot" and
               record.get("reason") == "initial" for record in records[1:-1]):
        raise ValueError("initial snapshot missing")
    for field in ("logSha256", "replaySha256"):
        if not HASH_RE.fullmatch(end.get(field, "")):
            raise ValueError(f"invalid {field}")

    # Matches the editor's StreamReader/AppendHash contract: every JSONL row
    # before end gets one LF; end is hashed with its digest fields blank.
    blank_end = re.sub(r'("(?:logSha256|replaySha256)":)"[0-9a-fA-F]{64}"',
                       r'\1""', lines[-1])
    if blank_end == lines[-1]:
        raise ValueError("end digest fields could not be found")
    actual_log = hashlib.sha256(("\n".join(lines[:-1] + [blank_end]) + "\n")
                                .encode("utf-8")).hexdigest()
    if actual_log.lower() != end["logSha256"].lower():
        raise ValueError("logSha256 mismatch")

    initial = next(record for record in records if record.get("type") == "snapshot"
                   and record.get("reason") == "initial")
    context = {key: header.get(key) for key in
               ("schema", "scene", "unity", "sampling", "landRules", "seed",
                "randomVersion", "buildHash", "deterministic")}
    actions = [item for record in records if record.get("type") == "action"
               if (item := normalized_action(record)) is not None]
    inputs = {"context": context, "initial": normalized_snapshot(initial),
              "actions": actions}
    outcomes = []
    for record in records[1:-1]:
        kind = record.get("type")
        if kind == "snapshot":
            outcomes.append(normalized_snapshot(record))
        elif kind in {"transition", "ecology"}:
            outcomes.append(record)
        elif kind != "action":
            raise ValueError(f"unknown record type: {kind}")
    return {"path": str(path), "header": header, "end": end,
            "input_hash": digest(inputs), "output_hash": digest(outcomes),
            "outcomes": outcomes, "actions": actions}


def describe(record):
    return f"{record.get('type')} day={record.get('day')} reason={record.get('reason', record.get('kind', ''))}"


def first_difference(left, right, path=""):
    if type(left) is not type(right):
        return path or "$", left, right
    if isinstance(left, dict):
        for key in sorted(left.keys() | right.keys()):
            child = f"{path}.{key}" if path else key
            if key not in left or key not in right:
                return child, left.get(key, "<missing>"), right.get(key, "<missing>")
            found = first_difference(left[key], right[key], child)
            if found:
                return found
    elif isinstance(left, list):
        for index, (a, b) in enumerate(zip(left, right)):
            found = first_difference(a, b, f"{path}[{index}]")
            if found:
                return found
        if len(left) != len(right):
            return path + ".length", len(left), len(right)
    elif left != right:
        return path or "$", left, right
    return None


def main(paths):
    reports = []
    failed = False
    for path in paths:
        try:
            report = load(path)
            reports.append(report)
            print(f"OK {path}\n  input={report['input_hash']}\n"
                  f"  result={report['end']['replaySha256']}\n"
                  f"  actions={len(report['actions'])}")
        except (OSError, UnicodeError, ValueError, json.JSONDecodeError) as error:
            failed = True
            print(f"FAIL {path}: {error}")
    groups = {}
    for report in reports:
        groups.setdefault(report["input_hash"], []).append(report)
    for input_hash, group in groups.items():
        if len(group) < 2:
            continue
        print(f"\nShared input {input_hash}: {len(group)} submissions")
        baseline = group[0]
        for other in group[1:]:
            if (baseline["output_hash"] == other["output_hash"] and
                    baseline["end"]["replaySha256"] == other["end"]["replaySha256"]):
                print(f"MATCH {other['path']}")
                continue
            failed = True
            if baseline["output_hash"] == other["output_hash"]:
                print(f"DIVERGED {other['path']}: snapshots/events match, but the "
                      "recorded replaySha256 differs; inspect canonical export fields")
                continue
            left, right = baseline["outcomes"], other["outcomes"]
            index = next((i for i, pair in enumerate(zip(left, right))
                          if pair[0] != pair[1]), min(len(left), len(right)))
            first = right[index] if index < len(right) else left[index]
            print(f"DIVERGED {other['path']}: first output #{index + 1}, "
                  f"{describe(first)}")
            if index < len(left) and index < len(right):
                field, before, after = first_difference(left[index], right[index])
                print(f"  field={field}\n  reference={before!r}\n  submitted={after!r}")
            else:
                print(f"  reference outputs={len(left)}, submitted outputs={len(right)}")
    if len(groups) > 1:
        print("\nDifferent input hashes: these submissions used different builds, "
              "initial states, or operation sequences and cannot be compared as one run.")
    return 1 if failed else 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__.strip())
        sys.exit(2)
    sys.exit(main(sys.argv[1:]))
