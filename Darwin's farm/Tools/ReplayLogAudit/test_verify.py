"""Small protocol checks for integrity and first-divergence reporting."""

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import verify


def write_log(path, *, count=3, action="harvest", tamper=False):
    rows = [
        {"type": "header", "schema": 4, "deterministic": True,
         "scene": "Whitebox", "unity": "6000.2.9f1", "seed": 17,
         "randomVersion": "DF-FNV64-1", "buildHash": "build"},
        {"type": "snapshot", "reason": "initial", "day": 0,
         "modelHash": "initial", "tiles": []},
        {"type": "action", "day": 0, "source": "player_economy",
         "operation": "commit", "x": 0, "y": 0, "detail": action},
        {"type": "snapshot", "reason": "after_action", "day": 0,
         "modelHash": "outcome", "tiles": [{"x": 0, "y": 0,
                                           "populations": [{"id": "p", "count": count}]}]},
    ]
    end = {"type": "end", "reason": "manual_finish", "day": 0,
           "recordedDays": 0, "logSha256": "", "replaySha256": ""}
    serialized = [json.dumps(row, separators=(",", ":")) for row in rows]
    serialized.append(json.dumps(end, separators=(",", ":")))
    end["logSha256"] = hashlib.sha256(("\n".join(serialized) + "\n")
                                      .encode()).hexdigest()
    end["replaySha256"] = "0" * 64
    serialized[-1] = json.dumps(end, separators=(",", ":"))
    if tamper:
        serialized[3] = serialized[3].replace("outcome", "changed")
    path.write_text("\n".join(serialized) + "\n", encoding="utf-8")


class AuditTests(unittest.TestCase):
    def test_same_input_and_result(self):
        with tempfile.TemporaryDirectory() as folder:
            a, b = Path(folder) / "a.jsonl", Path(folder) / "b.jsonl"
            write_log(a)
            write_log(b)
            self.assertEqual(verify.main([a, b]), 0)

    def test_first_divergence(self):
        with tempfile.TemporaryDirectory() as folder:
            a, b = Path(folder) / "a.jsonl", Path(folder) / "b.jsonl"
            write_log(a)
            write_log(b, count=4)
            self.assertEqual(verify.load(a)["input_hash"],
                             verify.load(b)["input_hash"])
            self.assertEqual(verify.main([a, b]), 1)
            self.assertEqual(verify.first_difference(
                verify.load(a)["outcomes"][1],
                verify.load(b)["outcomes"][1])[0],
                "tiles[0].populations[0].count")

    def test_tampering_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bad.jsonl"
            write_log(path, tamper=True)
            with self.assertRaisesRegex(ValueError, "logSha256 mismatch"):
                verify.load(path)


if __name__ == "__main__":
    unittest.main()
