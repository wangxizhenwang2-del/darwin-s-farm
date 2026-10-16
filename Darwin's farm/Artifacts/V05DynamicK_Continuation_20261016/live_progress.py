"""Read-only live status page for the ongoing real-Unity replay batch."""
import json
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

BASE = Path(__file__).resolve().parent
RUNS = BASE / "runs"
CASES = json.loads((BASE / "FORMAL_CASES.json").read_text(encoding="utf-8"))
TOTAL = len(CASES) * 3


def latest_day(path):
    if not path.exists():
        return None
    with path.open("rb") as stream:
        stream.seek(0, 2)
        end = stream.tell()
        if not end:
            return None
        stream.seek(max(0, end - 16384))
        lines = stream.read().splitlines()
    for line in reversed(lines):
        if line.startswith(b"{"):
            try:
                return json.loads(line)["day"]
            except (ValueError, KeyError):
                pass
    return None


def status():
    complete = []
    pending = []
    failures = []
    durations = []
    for case in CASES:
        for run in range(1, 4):
            stem = f"{case}-run{run}"
            evidence = RUNS / f"{stem}.execution.json"
            if evidence.exists():
                try:
                    record = json.loads(evidence.read_text(encoding="utf-8"))
                    done = record.get("replayCompleted", False)
                    if done:
                        complete.append(stem)
                        durations.append(record.get("seconds", 0))
                    else:
                        failures.append(stem)
                except (OSError, ValueError):
                    failures.append(stem)
            else:
                pending.append(stem)
    active = None
    logs = [RUNS / f"{stem}.log" for stem in pending]
    logs = [path for path in logs if path.exists()]
    if logs:
        path = max(logs, key=lambda item: item.stat().st_mtime)
        stem = path.stem
        case = stem.rsplit("-run", 1)[0]
        elapsed = round(datetime.now().timestamp() - path.stat().st_ctime)
        target = json.loads((BASE / "inputs" / f"{case}.json").read_text(encoding="utf-8"))["endDay"]
        active = {"name": stem, "day": latest_day(RUNS / f"{stem}.jsonl"),
                  "targetDay": target, "elapsedSeconds": elapsed,
                  "logUpdatedSecondsAgo": round(datetime.now().timestamp() - path.stat().st_mtime)}
    average = sum(durations) / len(durations) if durations else None
    return {"updated": datetime.now().astimezone().isoformat(timespec="seconds"),
            "completed": len(complete), "total": TOTAL, "failed": len(failures),
            "failures": failures, "active": active, "next": pending[0] if pending else None,
            "meanSeconds": round(average, 1) if average else None,
            "estimatedRemainingMinutes": round(len(pending) * average / 60) if average else None,
            "recentCompleted": complete[-5:]}


PAGE = """<!doctype html><html lang="zh"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Task 5 Unity 测试进度</title><style>
body{font:16px system-ui;background:#10151e;color:#e7edf8;max-width:850px;margin:40px auto;padding:0 20px}
h1{font-size:28px}p{line-height:1.6}.card{background:#1b2432;border-radius:14px;padding:22px;margin:15px 0}
.big{font-size:36px;font-weight:700}.muted{color:#aab7c9}progress{width:100%;height:24px;accent-color:#53d6a5}
code{color:#9bd5ff;overflow-wrap:anywhere}.bad{color:#ff8e8e}</style>
<h1>Task 5 · Unity 实时测试进度</h1><p class="muted">每 3 秒读取真实 Unity 输出；页面不会启动或修改测试。</p>
<div class="card"><div class="big" id="count">加载中</div><progress id="bar" max="90" value="0"></progress>
<p id="eta"></p></div><div class="card"><h2>当前运行</h2><p id="active">读取中</p><p class="muted" id="fresh"></p></div>
<div class="card"><h2>检查</h2><p id="failure"></p><p id="recent"></p></div><p class="muted" id="updated"></p>
<script>async function refresh(){try{const r=await fetch('/api/status',{cache:'no-store'});const s=await r.json();
document.getElementById('count').textContent=`${s.completed} / ${s.total} 次完成`;
document.getElementById('bar').max=s.total;document.getElementById('bar').value=s.completed;
document.getElementById('eta').textContent=s.estimatedRemainingMinutes===null?'正在计算平均耗时':`平均 ${s.meanSeconds} 秒/次，按当前速度约剩 ${s.estimatedRemainingMinutes} 分钟`;
document.getElementById('active').innerHTML=s.active?`<code>${s.active.name}</code> · 第 ${s.active.day??'加载中'} / ${s.active.targetDay} 日 · 已运行 ${s.active.elapsedSeconds} 秒`:(s.completed===s.total?'全部完成':`等待 ${s.next}`);
document.getElementById('fresh').textContent=s.active?`日志 ${s.active.logUpdatedSecondsAgo} 秒前更新`:'';
document.getElementById('failure').className=s.failed?'bad':'';
document.getElementById('failure').textContent=s.failed?`异常记录 ${s.failed}：${s.failures.join(', ')}`:'异常记录 0';
document.getElementById('recent').textContent=`最近完成：${s.recentCompleted.join('、')||'无'}`;
document.getElementById('updated').textContent=`更新时间：${s.updated}`;
}catch(e){document.getElementById('updated').textContent='进度服务暂时无法连接：'+e} }
refresh();setInterval(refresh,3000);</script></html>"""


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/api/status":
            payload = json.dumps(status(), ensure_ascii=False).encode("utf-8")
            kind = "application/json; charset=utf-8"
        elif self.path == "/":
            payload = PAGE.encode("utf-8")
            kind = "text/html; charset=utf-8"
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", 8765), Handler).serve_forever()
