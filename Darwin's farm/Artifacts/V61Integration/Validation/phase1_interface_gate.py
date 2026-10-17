"""Check the V6.1 integration against the authorized post-merge baseline."""

from pathlib import Path
import re
import subprocess

project = Path(__file__).resolve().parents[3]
repository = project.parent
prefix = project.name + "/"
base = "3286061c50661bbc796ce3c57009c662be6dc600"


def git(*args):
    return subprocess.check_output(["git", *args], cwd=repository)


def declarations(source):
    result = set()
    for line in source.splitlines():
        line = line.strip()
        if not re.match(r"(?:public|protected)\s", line):
            continue
        result.add(re.split(r"\s*(?:\(|=>|\{|;|=)", line, maxsplit=1)[0].strip())
    return result


baseline_paths = set(git("ls-tree", "-r", "--name-only", "-z", base).decode("utf-8").split("\0"))
changed = [path for path in git("diff", base, "--name-only", "-z").decode("utf-8").split("\0")
           if path.startswith(prefix + "Assets/Scripts/") and path.endswith(".cs") and
           path in baseline_paths]
removed = []
removed_serialized = []
for path in changed:
    before = git("show", base + ":" + path).decode("utf-8-sig", errors="replace")
    after = (repository / path).read_text(encoding="utf-8-sig")
    removed.extend((path, item) for item in sorted(declarations(before) - declarations(after)))
    old_fields = {line.strip() for line in before.splitlines() if "[SerializeField]" in line}
    new_fields = {line.strip() for line in after.splitlines() if "[SerializeField]" in line}
    removed_serialized.extend((path, item) for item in sorted(old_fields - new_fields))

resource_diff = [path for path in git("diff", base, "--name-only", "-z", "--",
                 prefix + "Assets/Scenes", prefix + "Assets/Prefabs",
                 prefix + "ProjectSettings", prefix + "Assets/Resources/GameUI").decode("utf-8").split("\0")
                 if path]

print("Baseline:", base)
print("Changed C# files:", len(changed))
print("Removed public/protected declarations:", len(removed))
print("Removed serialized declarations:", len(removed_serialized))
print("Scene, Prefab, ProjectSettings and GameUI resource changes:", len(resource_diff))
for category, entries in (("PUBLIC", removed), ("SERIALIZED", removed_serialized)):
    for path, item in entries:
        print("FAIL", category, path, item)
for path in resource_diff:
    print("FAIL RESOURCE", path)
raise SystemExit(bool(removed or removed_serialized or resource_diff))
