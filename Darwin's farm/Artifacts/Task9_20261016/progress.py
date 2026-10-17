"""Read-only Task 9 status page; Unity and the runner write status.json."""
import json
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
STATUS = ROOT / "status.json"

PAGE = """<!doctype html><html lang="zh"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Task 9 Unity 测试进度</title><style>
body{font:16px system-ui;background:#10151e;color:#e7edf8;max-width:900px;margin:36px auto;padding:0 20px}
h1{font-size:28px}.card{background:#1b2432;border-radius:14px;padding:22px;margin:15px 0}
.big{font-size:36px;font-weight:700}.muted{color:#aab7c9}.bad{color:#ff8e8e}
progress{width:100%;height:24px;accent-color:#53d6a5}li{margin:8px 0}
code{color:#9bd5ff;overflow-wrap:anywhere}</style>
<h1>Task 9 · Unity 实时进度</h1><p class="muted">每 3 秒读取本地执行记录；页面不会启动或修改测试。</p>
<div class="card"><div class="big" id="overall">读取中</div>
<progress id="bar" max="100" value="0"></progress><p id="state"></p></div>
<div class="card"><h2>正在执行</h2><p id="active">读取中</p>
<p class="muted" id="fresh"></p></div>
<div class="card"><h2>阶段</h2><ol id="stages"></ol></div>
<p class="muted" id="updated"></p><script>
async function refresh(){try{
 const r=await fetch('/api/status',{cache:'no-store'});const s=await r.json();
 const stages=s.stages||[];const done=stages.filter(x=>['PASS','FAIL','BLOCKED','NOT TESTED'].includes(x.status)).length;
 const pct=stages.length?Math.round(100*(done+(s.stageFraction||0))/stages.length):0;
 document.getElementById('overall').textContent=`${pct}% · ${done}/${stages.length} 阶段已结案${s.stageRuns?' · 当前阶段 '+s.stageRuns:''}`;
 document.getElementById('bar').value=pct;
 document.getElementById('state').textContent=`总状态：${s.state||'准备中'}${s.message?' · '+s.message:''}`;
 document.getElementById('active').textContent=s.active||'等待下一项';
 document.getElementById('fresh').textContent=s.day==null?'':`当前模拟日 ${s.day}${s.targetDay?' / '+s.targetDay:''}；日志 ${s.logAgeSeconds??'?'} 秒前更新`;
 const list=document.getElementById('stages');list.replaceChildren();
 for(const x of stages){const li=document.createElement('li');li.textContent=`${x.name}：${x.status}${x.detail?' · '+x.detail:''}`;if(x.status==='FAIL'||x.status==='BLOCKED')li.className='bad';list.appendChild(li)}
 document.getElementById('updated').textContent=`更新时间：${s.updated||'未知'}；页面读取：${new Date().toLocaleTimeString()}`;
}catch(e){document.getElementById('updated').textContent='进度服务无法连接：'+e}}
refresh();setInterval(refresh,3000);</script></html>"""


def read_status():
    try:
        value = json.loads(STATUS.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        return {"state": "ERROR", "message": str(error), "stages": []}
    value["updated"] = datetime.fromtimestamp(STATUS.stat().st_mtime).astimezone().isoformat(timespec="seconds")
    stages = value.get("stages", [])
    if len(stages) > 2 and stages[2].get("status") == "RUNNING":
        completed = sum(1 for path in (ROOT / "runs").glob("stage2-civet*-day*.execution.json")
                        if _successful_run(path))
        value["stageRuns"] = f"{completed}/14 组长期回放"
        value["stageFraction"] = min(1, completed / 14)
    elif len(stages) > 3 and stages[3].get("status") == "RUNNING":
        completed = sum(1 for path in (ROOT / "runs").glob("stage3-*.execution.json")
                        if _successful_run(path))
        value["stageRuns"] = f"{completed}/30 组生境与灾害回放"
        value["stageFraction"] = min(1, completed / 30)
    elif len(stages) > 4 and stages[4].get("status") == "RUNNING":
        completed = sum(1 for path in (ROOT / "runs").glob("stage4-*.execution.json")
                        if _successful_run(path))
        value["stageRuns"] = f"{completed}/26 组玩家工具回放"
        value["stageFraction"] = min(1, completed / 26)
    log = value.get("activeLog")
    if log:
        path = Path(log)
        if path.exists():
            value["logAgeSeconds"] = max(0, round(datetime.now().timestamp() - path.stat().st_mtime))
    stream = value.get("activeJsonl")
    if stream:
        path = Path(stream)
        if path.exists():
            with path.open("rb") as source:
                source.seek(max(0, source.seek(0, 2) - 32768))
                for line in reversed(source.read().splitlines()):
                    if line.startswith(b"{"):
                        try:
                            value["day"] = json.loads(line)["day"]
                            break
                        except (ValueError, KeyError, TypeError):
                            pass
    marker = value.get("activeProgress")
    if marker:
        path = Path(marker)
        if path.exists():
            try:
                value["day"] = int(path.read_text(encoding="ascii").strip())
            except (OSError, ValueError):
                pass
    return value


def _successful_run(path):
    try:
        entry = json.loads(path.read_text(encoding="utf-8"))
        return entry.get("replayCompleted") and entry.get("exitCode") == 0
    except (OSError, ValueError):
        return False


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/":
            payload, kind = PAGE.encode(), "text/html; charset=utf-8"
        elif self.path == "/api/status":
            payload = json.dumps(read_status(), ensure_ascii=False).encode()
            kind = "application/json; charset=utf-8"
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
