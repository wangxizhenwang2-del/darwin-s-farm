"""Verify the V6.1 delivery archive and its exact source graph bytes."""

from hashlib import sha256
from pathlib import Path
from zipfile import ZipFile

archive_path = Path(__file__).resolve().parents[1] / "DarwinFarm_V61_Integration.zip"
with ZipFile(archive_path) as archive:
    assert archive.testzip() is None, "ZIP CRC error"
    manifest = archive.read("manifest.sha256").decode("utf-8")
    checked = 0
    for line in manifest.splitlines():
        expected, name = line.split("  ", 1)
        actual = sha256(archive.read(name)).hexdigest()
        assert actual == expected, f"Manifest mismatch: {name}"
        checked += 1
    expected_graph = "763fdd910f882df696688cf96a794155daa468cc34543a6077cd120df65c3dd5"
    for name in (
        "new-files/Assets/Resources/V61Bioweb13.json",
        "evidence/Source/bioweb13.json",
    ):
        assert sha256(archive.read(name)).hexdigest() == expected_graph, name
    assert archive.read("new-files/.gitattributes").startswith(
        b"Assets/Resources/V61Bioweb13.json -text"
    )
print(f"PASS archive CRC, {checked} manifest entries and both exact graph copies")
