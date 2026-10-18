"""Build a temporary runtime project without editing Unity's generated csproj.

The checked-in working tree currently has newly added and removed C# files,
while Assembly-CSharp.csproj still reflects an earlier Unity refresh.
"""
from pathlib import Path
import os
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
source = Path(os.environ.get('V61_CSPROJ_SOURCE', root / 'Assembly-CSharp.csproj'))
temporary = root / 'V61Compile.csproj'
tree = ET.parse(source)
project = tree.getroot()
if source.parent != root:
    for hint in project.iter('HintPath'):
        if hint.text and not Path(hint.text).is_absolute():
            hint.text = str(source.parent / hint.text)
seen = set()
removed = []
for group in project.findall('ItemGroup'):
    for item in list(group):
        if item.tag != 'Compile':
            continue
        relative = item.get('Include', '').replace('\\', '/')
        if not (root / relative).is_file():
            group.remove(item)
            removed.append(relative)
        else:
            seen.add(relative.casefold())

new = []
group = ET.SubElement(project, 'ItemGroup')
for path in (root / 'Assets').rglob('*.cs'):
    relative = path.relative_to(root).as_posix()
    if 'Editor' in path.parts or relative.casefold() in seen:
        continue
    ET.SubElement(group, 'Compile', Include=relative.replace('/', '\\'))
    new.append(relative)

tree.write(temporary, encoding='utf-8', xml_declaration=True)
print('Removed stale includes:', *removed, sep='\n  ')
print('Added current runtime scripts:', *new, sep='\n  ')
try:
    result = subprocess.run(['dotnet', 'build', str(temporary), '-v:q'], cwd=root)
    raise SystemExit(result.returncode)
finally:
    temporary.unlink(missing_ok=True)
