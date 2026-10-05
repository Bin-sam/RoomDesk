"""Integration test against the real HTTP process and SQLite. No browser/UI assertions.
Usage: python3 Tests/api_smoke.py /path/to/dotnet
Build Preview/Preview.csproj -c Release first; port 5188 must be unused.
"""
import json
import csv
import io
import pathlib
import sqlite3
from contextlib import closing
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import urllib.parse

root = pathlib.Path(__file__).resolve().parents[1]
dotnet = pathlib.Path(sys.argv[1]).resolve()
dll = root / "Preview/bin/Release/net8.0/Preview.dll"
base = "http://127.0.0.1:5188"
checks = 0
password = "HTTP-test-only-2026"


def check(value, name):
    global checks
    assert value, name
    checks += 1
    print("PASS:", name, flush=True)


def request(path, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["X-RoomDesk-Token"] = token
    req = urllib.request.Request(base + path, headers=headers,
                                 data=None if body is None else json.dumps(body).encode())
    try:
        with urllib.request.urlopen(req, timeout=10) as result:
            return result.status, result.read()
    except urllib.error.HTTPError as ex:
        return ex.code, ex.read()


try:
    request("/api/board")
except urllib.error.URLError:
    pass
else:
    raise RuntimeError("Port 5188 already occupied; refusing to use another server's data")

with tempfile.TemporaryDirectory(prefix="roomdesk-api-") as temp:
    dbpath = pathlib.Path(temp) / "rooms.db"
    log = open(pathlib.Path(temp) / "server.log", "w+")
    process = None

    def start():
        global process
        process = subprocess.Popen([str(dotnet), str(dll), "--data", str(dbpath)],
                                   cwd=root / "Preview", stdout=log, stderr=log)
        for _ in range(100):
            if process.poll() is not None:
                log.seek(0)
                raise RuntimeError(log.read())
            try:
                status, body = request("/api/board")
                if status == 200:
                    return json.loads(body)
            except urllib.error.URLError:
                pass
            time.sleep(0.1)
        raise RuntimeError("Server startup timed out")

    def stop():
        if process and process.poll() is None:
            process.terminate()
            process.wait(timeout=15)

    try:
        state = start()
        check(len(state["snapshot"]["rooms"]) == 24, "real HTTP server loads SQLite sample data")
        for url, text in [("/", "Mac 功能预览"), ("/app.js", "renderRooms"), ("/style.css", ".room-grid")]:
            status, body = request(url)
            check(status == 200 and text in body.decode(), "static resource " + url)
        token = state["token"]
        check(request("/api/stays/export")[0] == 405, "legacy unprotected GET export removed")
        check(request("/api/backup", {}, token)[0] == 409, "unset operation password blocks backup")
        check(request("/api/security/password", {"newPassword": password}, token)[0] == 200, "initialize operation password")
        room = next(r for r in state["snapshot"]["rooms"] if r["number"] == 101)
        route = f'/api/rooms/{room["id"]}/action'
        check(request(route, {"version": 0, "action": "CheckIn", "guest": {"name": "HTTP测试住客", "salePrice": 188.88, "phone": "TEST-PHONE", "documentType": "其他", "documentNumber": "TEST-ONLY-HTTP", "notes": "测试备注"}})[0] == 403, "mutation without local token blocked")
        check(request(route, {"version": 0, "action": "CheckIn", "guest": {"name": "HTTP测试住客", "salePrice": 188.88, "phone": "TEST-PHONE", "documentType": "其他", "documentNumber": "TEST-ONLY-HTTP", "notes": "测试备注"}}, token)[0] == 200, "check-in via HTTP")
        check(request(route, {"version": 1, "action": "CheckIn"}, token)[0] == 409, "bare check-in cannot bypass registration")
        guest = json.loads(request(f'/api/rooms/{room["id"]}/guest')[1])["guest"]
        check(guest["name"] == "HTTP测试住客" and guest["notes"] == "测试备注", "saved guest can be read through API")
        named_room = next(r for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"] if r["number"] == 101)
        check(named_room["guestName"] == "HTTP测试住客" and named_room["guestLabel"] == "入住人：HTTP测试住客", "board API exposes current guest name")
        check(request(route, {"version": 0, "action": "Reserve"}, token)[0] == 409, "stale HTTP request rejected")
        check(request(route, {"version": 1, "action": "CheckOut"}, token)[0] == 200, "checkout via HTTP")
        empty_room = next(r for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"] if r["number"] == 101)
        check(empty_room["guestName"] is None and empty_room["guestLabel"] == "", "checkout removes guest from board API")
        check(request(route, {"version": 2, "action": "CheckIn", "guest": {"name": "HTTP测试住客", "salePrice": 188.88, "phone": "TEST-PHONE", "documentType": "其他", "documentNumber": "TEST-ONLY-HTTP", "notes": "测试备注"}}, token)[0] == 409, "dirty room cannot check in through API")
        check(request("/api/rooms", {"number": 401, "floor": 4, "type": "Deluxe"}, token)[0] == 200, "add room through API")
        check(request("/api/rooms", {"number": 401, "floor": 4, "type": "Deluxe"}, token)[0] == 409, "duplicate room HTTP conflict")
        status, body = request("/api/backup", {"password":password}, token)
        backup = json.loads(body)["path"]
        with closing(sqlite3.connect(backup)) as connection:
            check(status == 200 and connection.execute("PRAGMA integrity_check").fetchone()[0] == "ok", "HTTP backup is valid SQLite")
            check(connection.execute('SELECT count(*) FROM Rooms').fetchone()[0] == 25, "backup contains newly added room")
        # Leave room 401 occupied to verify registration survives a full process restart.
        current = json.loads(request("/api/board")[1])["snapshot"]
        added = next(r for r in current["rooms"] if r["number"] == 401)
        request(f'/api/rooms/{added["id"]}/action', {"version": 0, "action": "Clean"}, token)
        request(f'/api/rooms/{added["id"]}/action', {"version": 1, "action": "CheckIn", "guest": {"name": "重启测试住客", "salePrice": 99.50}}, token)
        stop()
        reopened = start()
        rooms = reopened["snapshot"]["rooms"]
        check(len(rooms) == 25 and next(r for r in rooms if r["number"] == 101)["statusKey"] == "dirty", "state survives complete server process restart")
        check(len(reopened["snapshot"]["activities"]) == 5, "history persists and excludes rejected mutations")
        persisted = json.loads(request(f'/api/rooms/{added["id"]}/guest')[1])["guest"]
        check(persisted["name"] == "重启测试住客", "guest registration survives full process restart")
        check(request(route, {"version": 2, "action": "Clean"}, token)[0] == 403, "previous process token invalidated after restart")
        history = json.loads(request("/api/stays")[1])
        check(history["total"] == 2 and len(history["records"]) == 2, "history includes both current and checked-out guests")
        filtered = json.loads(request("/api/stays?search=HTTP&status=checkedout")[1])
        check(filtered["total"] == 1 and filtered["records"][0]["roomNumber"] == 101, "HTTP keyword and status filters combine")
        check(json.loads(request("/api/stays?search=no-match")[1])["total"] == 0, "history no-match response")
        check(request("/api/stays?status=invalid")[0] == 409, "invalid history filter rejected over HTTP")
        status_code, exported = request("/api/stays/export", {"search":"HTTP","status":"checkedout","password":password},reopened["token"])
        parsed = list(csv.reader(io.StringIO(exported.decode("utf-8-sig"))))
        check(status_code == 200 and exported.startswith(bytes([239,187,191])) and len(parsed) == 2, "downloadable UTF-8 CSV matches filtered history")
        check(parsed[0][2] == "入住人姓名" and parsed[1][2] == "HTTP测试住客" and parsed[1][8] == "已退房", "CSV round-trip headers, guest and checkout status")
        check(len(list(csv.reader(io.StringIO(request("/api/stays/export", {"search":"no-match","password":password},reopened["token"])[1].decode("utf-8-sig"))))) == 1, "empty export has header only")
        response = urllib.request.urlopen(urllib.request.Request(base+"/api/stays/export",data=json.dumps({"password":password}).encode(),headers={"Content-Type":"application/json","X-RoomDesk-Token":reopened["token"]}))
        check("attachment" in response.headers.get("Content-Disposition", "") and response.headers.get_content_type() == "text/csv", "CSV response has download disposition and MIME type")
        response.close()
        suggestions = json.loads(request("/api/guests/suggestions?query=HTTP&field=name")[1])
        check(len(suggestions) == 1 and suggestions[0]["name"] == "HTTP测试住客", "HTTP name suggestions include archived guest")
        suggestions = json.loads(request("/api/guests/suggestions?query=only-http&field=document")[1])
        check(len(suggestions) == 1 and suggestions[0]["phone"] == "TEST-PHONE", "HTTP partial document lookup returns saved fields")
        check(request("/api/guests/suggestions?query=HTTP&field=invalid")[0] == 409, "HTTP invalid suggestion field rejected")
        check(json.loads(request("/api/guests/suggestions?query=")[1]) == [], "HTTP blank query returns no candidates")
        reader_text = {"text": "姓名：读卡测试\n身份证号：00000020000101000X"}
        check(request("/api/identity/parse", reader_text)[0] == 403, "reader parsing requires local token")
        status_code, parsed = request("/api/identity/parse", reader_text, reopened["token"])
        check(status_code == 200 and json.loads(parsed) == {"name": "读卡测试", "documentNumber": "00000020000101000X"}, "HTTP reader input parses without saving")
        check(request("/api/identity/parse", {"text": "读卡未完成"}, reopened["token"])[0] == 409, "HTTP incomplete read fails explicitly")
        check(json.loads(request("/api/stays")[1])["total"] == 2, "reader parse leaves stay history unchanged")
        check(request("/api/stays/export", {},reopened["token"])[0] == 409, "record export cannot bypass password via API")
        check(request("/api/bills/export", {"start":"2000-01-01T00:00","end":"2099-12-31T23:59"},reopened["token"])[0] == 409, "bill export cannot bypass password")
        bill = json.loads(request("/api/bills?start=2000-01-01T00:00&end=2099-12-31T23:59")[1])
        check(bill["count"] == 2 and bill["totalCents"] == 28838, "HTTP bill sums exact persisted sale prices")
        exported_bill = request("/api/bills/export", {"start":"2000-01-01T00:00","end":"2099-12-31T23:59","password":password},reopened["token"])
        bill_rows = list(csv.reader(io.StringIO(exported_bill[1].decode("utf-8-sig"))))
        check(exported_bill[0] == 200 and bill_rows[-1][6] == "288.38", "protected bill CSV round-trips with exact total")
        check(request(f'/api/stays/{guest["id"]}/delete',{},reopened["token"])[0] == 409,"record deletion rejects missing password")
        check(request(f'/api/stays/{guest["id"]}/delete',{"password":password},reopened["token"])[0] == 200,"archived record deletion accepts correct password")
        check(json.loads(request("/api/stays")[1])["total"] == 1,"deleted record removed from history")
        print(f"RESULT: {checks} HTTP/process integration checks passed. No UI assertions.")
    finally:
        stop()
        log.close()
