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
port = int(sys.argv[2]) if len(sys.argv)>2 else 5188
base = f"http://127.0.0.1:{port}"
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
    raise RuntimeError("Test port already occupied; refusing to use another server's data")

with tempfile.TemporaryDirectory(prefix="roomdesk-api-") as temp:
    dbpath = pathlib.Path(temp) / "rooms.db"
    log = open(pathlib.Path(temp) / "server.log", "w+")
    process = None

    def start():
        global process
        process = subprocess.Popen([str(dotnet), str(dll), "--port", str(port), "--data", str(dbpath)],
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
        check(request("/api/identity/parse", {"text":"removed"}, reopened["token"])[0] == 404, "removed reader endpoint is unavailable")
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
        token = reopened["token"]
        current = next(r for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"] if r["number"] == 101)
        route = f'/api/rooms/{current["id"]}/action'
        check(request(route, {"version":current["version"],"action":"Clean"},token)[0] == 200,"prepare clean room for reservation")
        version=current["version"]+1
        check(request(route, {"version":version,"action":"Reserve"},token)[0] == 409,"reservation requires guest name")
        booking={"name":"HTTP预订测试","platform":"携程","phone":"TEST-RESERVATION"}
        check(request(route, {"version":version,"action":"Reserve","reservation":booking},token)[0] == 200,"reservation accepts name platform and phone")
        card=next(r for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"] if r["number"]==101)
        check(card["boardCaption"]==booking["name"] and card["reservationName"]==booking["name"],"reservation name appears in room card API")
        check(json.loads(request(f'/api/rooms/{current["id"]}/reservation')[1])["reservation"]==booking,"reservation information can be read for check-in")
        check(request(route,{"version":version+1,"action":"CheckIn","guest":{"name":booking["name"],"phone":booking["phone"],"platform":"飞猪","documentType":"身份证","salePrice":188}},token)[0]==200,"reservation can check in with edited platform")
        saved=json.loads(request(f'/api/rooms/{current["id"]}/guest')[1])["guest"]
        check(saved["platform"]=="飞猪" and saved["phone"]==booking["phone"],"check-in platform and phone saved")
        check(json.loads(request(f'/api/rooms/{current["id"]}/reservation')[1])["reservation"] is None,"consumed reservation no longer returned")
        presets=json.loads(request("/api/platform-presets")[1])
        check([p["name"] for p in presets]==["线下","携程","美团","飞猪"],"platform shortcuts start with four common words")
        check(request("/api/platform-presets",{"name":"抖音"})[0]==403,"adding platform shortcut requires local token")
        check(request("/api/platform-presets",{"name":" 抖音 "},token)[0]==200,"custom platform shortcut added")
        check(request("/api/platform-presets",{"name":"抖音"},token)[0]==409,"duplicate platform shortcut rejected")
        word=next(p for p in json.loads(request("/api/platform-presets")[1]) if p["name"]=="抖音")
        check(request(f'/api/platform-presets/{word["id"]}/delete',{},token)[0]==409,"shortcut deletion requires password")
        check(request(f'/api/platform-presets/{word["id"]}/delete',{"password":password},token)[0]==200,"shortcut deletion with correct password")
        check(all(p["name"]!="抖音" for p in json.loads(request("/api/platform-presets")[1])),"deleted shortcut removed from shared list")
        check(saved["platform"]=="飞猪" and json.loads(request(f'/api/rooms/{current["id"]}/guest')[1])["guest"]["platform"]=="飞猪","shortcut deletion does not alter existing guest platform")
        check(request("/api/platform-presets",{"name":"抖音"},token)[0]==200,"deleted shortcut can be restored")
        stop()
        reopened=start()
        check(any(p["name"]=="抖音" for p in json.loads(request("/api/platform-presets")[1])),"custom shortcut survives process restart")
        token=reopened["token"]
        batch={"numbers":"701-703、705","floor":7,"type":"亲子房","defaultPrice":299.50}
        before=json.loads(request("/api/board")[1])["snapshot"]["rooms"]
        check(request("/api/rooms/batch",batch)[0]==403,"batch creation requires local token")
        check(request("/api/rooms/batch",{**batch,"numbers":"701,101"},token)[0]==409,"batch with existing room rejected")
        check(len(json.loads(request("/api/board")[1])["snapshot"]["rooms"])==len(before),"HTTP conflict leaves all rooms unchanged")
        code,result=request("/api/rooms/batch",batch,token)
        check(code==200 and json.loads(result)["count"]==4,"HTTP batch adds four rooms")
        cards=json.loads(request("/api/board")[1])["snapshot"]["rooms"]
        check(all(r["type"]=="亲子房" and r["defaultPrice"]==299.50 and r["statusKey"]=="dirty" for r in cards if r["number"] in [701,702,703,705]),"HTTP batch fields saved accurately")
        check(request("/api/rooms/batch",{**batch,"numbers":"800-1000"},token)[0]==409,"oversized HTTP batch rejected")
        check(request("/api/rooms/batch",{**batch,"numbers":"800,800"},token)[0]==409,"repeated room in HTTP batch rejected")
        check(request("/api/rooms",{"number":706,"floor":7,"type":"影音房"},token)[0]==200,"single room accepts free text type")
        words=json.loads(request("/api/room-type-presets")[1]);check(len(words)==5,"room types have separate default shortcuts")
        check(request("/api/room-type-presets",{"name":"亲子房"},token)[0]==200,"custom room type shortcut added")
        word=next(p for p in json.loads(request("/api/room-type-presets")[1]) if p["name"]=="亲子房")
        check(request(f'/api/room-type-presets/{word["id"]}/delete',{},token)[0]==409,"room type shortcut deletion password protected")
        check(request(f'/api/room-type-presets/{word["id"]}/delete',{"password":password},token)[0]==200,"room type shortcut deletion succeeds with password")
        stop();reopened=start()
        check(not any(p["name"]=="亲子房" for p in json.loads(request("/api/room-type-presets")[1])),"deleted room type stays removed after process restart")
        cards=json.loads(request("/api/board")[1])["snapshot"]["rooms"]
        check(len(cards)==len(before)+5 and next(r for r in cards if r["number"]==701)["type"]=="亲子房","batch rooms and custom type persist after deleting shortcut and restarting")
        token=reopened["token"]
        check(request("/api/hotel/name",{"name":"HTTP酒店测试"},token)[0]==200,"hotel name saves through HTTP")
        vacant=next(r for r in cards if r["number"]==701)
        delete=f'/api/rooms/{vacant["id"]}/delete'
        check(request(delete,{"version":vacant["version"]},token)[0]==409,"room deletion rejects missing password")
        check(request(delete,{"version":vacant["version"],"password":password},token)[0]==200,"vacant room deletion accepted")
        check(not any(r["number"]==701 for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"]),"deleted room removed from board API")
        archived=next(r for r in json.loads(request("/api/rooms/manage")[1])["snapshot"]["rooms"] if r["number"]==701)
        check(archived["isDeleted"],"management exposes recoverable deleted room")
        check(request(f'/api/rooms/{vacant["id"]}/restore',{"version":archived["version"]},token)[0]==200,"room recovery accepted")
        storage=json.loads(request("/api/storage")[1]);check(storage["journalMode"]=="wal" and storage["synchronous"]==2,"HTTP connection uses WAL FULL")
        def card(number):
            return next(r for r in json.loads(request("/api/board")[1])["snapshot"]["rooms"] if r["number"]==number)
        room=card(701)
        edit={"version":room["version"],"room":{"floor":9,"type":"编辑房型","defaultPrice":388,"statusKey":"ready","isClean":True}}
        check(request(f'/api/rooms/{room["id"]}/edit',edit)[0]==403,"room edit requires local token")
        check(request(f'/api/rooms/{room["id"]}/edit',edit,token)[0]==200 and card(701)["floor"]==9,"room edit updates metadata and state through HTTP")
        check(request(f'/api/rooms/{room["id"]}/edit',edit,token)[0]==409,"HTTP stale room edit rejected")
        room=card(701)
        check(request(f'/api/rooms/{room["id"]}/action',{"version":room["version"],"action":"Reserve","reservation":{"name":"编辑前","platform":"线下"}},token)[0]==200,"edit test reservation created")
        room=card(701)
        check(request(f'/api/rooms/{room["id"]}/reservation/edit',{"version":room["version"],"reservation":{"name":"编辑后","platform":"美团","phone":"EDIT-PHONE"}},token)[0]==200 and card(701)["reservationName"]=="编辑后","reservation edit reflected on board API")
        room=card(701)
        check(request(f'/api/rooms/{room["id"]}/action',{"version":room["version"],"action":"CheckIn","guest":{"name":"住客编辑前","salePrice":300}},token)[0]==200,"edit test guest checked in")
        room=card(701)
        check(request(f'/api/rooms/{room["id"]}/guest/edit',{"version":room["version"],"guest":{"name":"住客编辑后","salePrice":350,"platform":"携程"}},token)[0]==200 and card(701)["guestName"]=="住客编辑后","guest edit reflected on board API")
        check(request('/api/security/unlock',{"password":password})[0]==403,"session issuance requires local token")
        session=json.loads(request('/api/security/unlock',{"password":password},token)[1])
        check(request('/api/backup',{"password":session["token"]},token)[0]==200,"session credential authorizes protected HTTP operation")
        check(json.loads(request('/api/security/session',{"password":session["token"]},token)[1])["valid"],"session status is valid after verification")
        check(request('/api/security/lock',{"password":session["token"]},token)[0]==200 and request('/api/backup',{"password":session["token"]},token)[0]==409,"explicit lock revokes HTTP authorization")
        # An acknowledged write must survive ungraceful termination; do not call graceful stop here.
        process.kill();process.wait(timeout=15)
        reopened=start();check(reopened["hotelName"]=="HTTP酒店测试" and any(r["number"]==701 for r in reopened["snapshot"]["rooms"]),"acknowledged hotel and room changes survive SIGKILL")
        stop()
        helper=root/"Benchmarks/bin/Release/net8.0/Benchmarks.dll"
        if helper.exists():
            pending=subprocess.Popen([str(dotnet),str(helper),str(dbpath),"--uncommitted"],stdout=subprocess.PIPE,text=True)
            try:
                check(pending.stdout.readline().strip()=="UNCOMMITTED_READY","test helper holds uncommitted transaction")
                pending.kill();pending.wait(timeout=15)
            finally:
                if pending.poll() is None: pending.kill();pending.wait(timeout=15)
            reopened=start();check(reopened["hotelName"]=="HTTP酒店测试","uncommitted transaction rolled back after SIGKILL")
            stop()
        with closing(sqlite3.connect(dbpath)) as integrity:
            check(integrity.execute("PRAGMA integrity_check").fetchone()[0]=="ok","database integrity survives crash tests")
        print(f"RESULT: {checks} HTTP/process integration checks passed. No UI assertions.")
    finally:
        stop()
        log.close()
