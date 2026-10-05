"""Single front desk acceptance scenarios. Synthetic isolated database only.
Build Preview first; stop localhost:5188. Usage: python3 Tests/front_desk_scenarios.py /path/to/dotnet
"""
import concurrent.futures
import csv
import io
import json
import pathlib
import socket
import sqlite3
from contextlib import closing
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOTNET = str(pathlib.Path(sys.argv[1]).resolve())
BASE = 'http://127.0.0.1:5188'
PWD = 'Synthetic-frontdesk-2026'
results = []
process = None
token = ''

def http(path, body=None):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
        headers={'Content-Type':'application/json', 'X-RoomDesk-Token':token})
    try:
        with urllib.request.urlopen(req, timeout=15) as r: return r.status, r.read()
    except urllib.error.HTTPError as e: return e.code, e.read()

def sql_scalar(path, query, parameters=()):
    with closing(sqlite3.connect(path)) as connection:
        return connection.execute(query, parameters).fetchone()[0]

def get(path):
    code, data = http(path)
    assert code == 200, (path, code, data)
    return json.loads(data)

def post(path, body, expected=200):
    code, data = http(path, body)
    assert code == expected, (path, code, data)
    return json.loads(data) if code == 200 else data

def test(name, fn):
    started = time.perf_counter()
    try: fn()
    except Exception as e:
        results.append({'case':name,'result':'FAIL','detail':str(e)})
        print('FAIL:',name,str(e),flush=True)
        raise
    results.append({'case':name,'result':'PASS','ms':round((time.perf_counter()-started)*1000,2)})
    print('PASS:',name,flush=True)

def ensure(condition, message='unexpected result'):
    assert condition, message

def board(): return get('/api/board')['snapshot']
def room(number): return next(r for r in board()['rooms'] if r['number']==number)
def action(number, act, guest=None, expected=200, version=None):
    r=room(number)
    return post(f'/api/rooms/{r["id"]}/action',{'version':r['version'] if version is None else version,'action':act,'guest':guest},expected)
def guest(name='前台测试甲',price=168.50,doc='FD-ONLY-A'):
    return {'name':name,'salePrice':price,'documentType':'其他','documentNumber':doc,'phone':'TEST-PHONE','notes':'仅测试数据'}
def stays(): return get('/api/stays')['records']
def price(number, value):
    r=room(number);return post(f'/api/rooms/{r["id"]}/price',{'version':r['version'],'price':value})
def bill(start='2026-10-04T23:59',end='2026-10-05T00:00'):
    return get('/api/bills?'+urllib.parse.urlencode({'start':start,'end':end}))
def exported(path, body):
    code, data=http(path,body);ensure(code==200);ensure(data.startswith(b'\xef\xbb\xbf'))
    return list(csv.reader(io.StringIO(data.decode('utf-8-sig'))))

with socket.socket() as s:
    if s.connect_ex(('127.0.0.1',5188))==0: raise RuntimeError('5188 occupied; refusing to touch existing data')
with tempfile.TemporaryDirectory(prefix='roomdesk-frontdesk-') as temp:
    db=pathlib.Path(temp)/'rooms.db'
    log=open(pathlib.Path(temp)/'server.log','w+')
    def start():
        global process,token
        process=subprocess.Popen([DOTNET,str(ROOT/'Preview/bin/Release/net8.0/Preview.dll'),'--data',str(db)],cwd=ROOT/'Preview',stdout=log,stderr=log)
        for _ in range(100):
            if process.poll() is not None: raise RuntimeError('server exited')
            try:
                token=get('/api/board')['token'];return
            except urllib.error.URLError: time.sleep(.1)
        raise RuntimeError('startup timed out')
    def stop(crash=False):
        if process and process.poll() is None:
            process.kill() if crash else process.terminate()
            process.wait(timeout=15)
    try:
        start()
        test('FD01 开班查看24间房及未设置密码状态',lambda:(ensure(len(board()['rooms'])==24),ensure(not get('/api/security')['configured'])))
        test('FD02 未设密码不能导出和备份',lambda:(post('/api/backup',{},409),post('/api/stays/export',{},409)))
        test('FD03 设置操作密码',lambda:post('/api/security/password',{'newPassword':PWD}))
        test('FD04 新增601房默认188.88元且先待清扫',lambda:(post('/api/rooms',{'number':601,'floor':6,'type':'Deluxe','defaultPrice':188.88}),ensure(room(601)['statusKey']=='dirty'),ensure(room(601)['defaultPriceCents']==18888)))
        test('FD05 重复房号不增加房间',lambda:(post('/api/rooms',{'number':601,'floor':6,'type':'Deluxe'},409),ensure(len(board()['rooms'])==25)))
        test('FD06 脏房拒绝入住和预订',lambda:(action(601,'CheckIn',guest(),409),action(601,'Reserve',expected=409),ensure(len(stays())==0)))
        test('FD07 清扫后预订、取消预订恢复可入住',lambda:(action(601,'Clean'),action(601,'Reserve'),ensure(room(601)['statusKey']=='reserved'),action(601,'CancelReservation'),ensure(room(601)['statusKey']=='ready')))
        test('FD08 姓名空白、价格缺失/负数/三位小数不落库',lambda:([action(601,'CheckIn',g,409) for g in [guest('  '),guest(price=None),guest(price=-1),guest(price=1.001)]],ensure(len(stays())==0)))
        test('FD09 实际168.50入住，显示住客且默认价保留188.88',lambda:(action(601,'CheckIn',guest()),ensure(room(601)['guestName']=='前台测试甲'),ensure(stays()[0]['salePriceCents']==16850),ensure(room(601)['defaultPriceCents']==18888)))
        first_id=stays()[0]['id']
        test('FD10 在住房禁止再次入住、维修、停用',lambda:([action(601,a,guest('前台测试乙'),409) for a in ['CheckIn','StartMaintenance','Disable']],ensure(len(stays())==1)))
        test('FD11 在住清扫不丢失住客或生成新账单',lambda:(action(601,'MarkDirty'),ensure(room(601)['guestName']=='前台测试甲'),action(601,'Clean'),ensure(len(stays())==1)))
        test('FD12 修改默认价不会改写已登记168.50',lambda:(price(601,199),ensure(stays()[0]['salePriceCents']==16850)))
        test('FD13 在住记录禁止删除',lambda:post(f'/api/stays/{first_id}/delete',{'password':PWD},409))
        test('FD14 退房后去除住客、记录归档、房间待清扫',lambda:(action(601,'CheckOut'),ensure(room(601)['guestName'] is None),ensure(room(601)['statusKey']=='dirty'),ensure(stays()[0]['checkedOutAtUtc'] is not None)))
        test('FD15 连点退房不生成多条记录；未清扫不能换下一位',lambda:(action(601,'CheckOut',expected=409),action(601,'CheckIn',guest('前台测试乙'),409),ensure(len(stays())==1)))
        test('FD16 老客姓名及证件片段匹配到同一个人',lambda:(ensure(get('/api/guests/suggestions?'+urllib.parse.urlencode({'query':'前台测试甲','field':'name'}))[0]['documentNumber']=='FD-ONLY-A'),ensure(get('/api/guests/suggestions?query=ONLY-A&field=document')[0]['name']=='前台测试甲')))
        action(601,'Clean')
        stale=room(601)['version']
        def concurrent_checkin():
            r=room(601);payload={'version':stale,'action':'CheckIn','guest':guest('前台测试乙',199,'FD-ONLY-B')}
            with concurrent.futures.ThreadPoolExecutor(2) as pool:
                codes=list(pool.map(lambda _:http(f'/api/rooms/{r["id"]}/action',payload)[0],range(2)))
            ensure(sorted(codes)==[200,409]);ensure(len(stays())==2)
        test('FD17 双击/两个旧页面同时入住只有一笔成功',concurrent_checkin)
        test('FD18 旧页面退房不能误退新客',lambda:(action(601,'CheckOut',expected=409,version=stale),ensure(room(601)['guestName']=='前台测试乙')))
        test('FD19 同房两位客人历史独立、姓名和状态搜索正确',lambda:(ensure(get('/api/stays?'+urllib.parse.urlencode({'search':'前台测试甲','status':'checkedout'}))['total']==1),ensure(get('/api/stays?'+urllib.parse.urlencode({'search':'前台测试乙','status':'current'}))['total']==1)))
        # Add a zero-price stay and a CSV-sensitive guest; these are explicit test fixtures.
        for n,who,amount in [(602,'赠房测试',0),(603,'=测试,"客人"\n换行',88.88)]:
            post('/api/rooms',{'number':n,'floor':6,'type':'Deluxe','defaultPrice':100})
            action(n,'Clean');action(n,'CheckIn',guest(who,amount,str(n)))
        test('FD20 零元赠房计为已登记，分毫准确合计456.38',lambda:(ensure(bill('2000-01-01T00:00','2099-12-31T23:59')['totalCents']==45638),ensure(bill('2000-01-01T00:00','2099-12-31T23:59')['unpricedCount']==0)))
        saved=board();old_token=token
        stop(crash=True);start()
        test('FD21 强制结束进程后房态住客金额恢复，旧令牌失效',lambda:(ensure(board()['rooms']==saved['rooms']),ensure(len(stays())==4),ensure(old_token!=token)))
        # Clock fixtures: 23:59:00, 23:59:59.999, 00:00:00, 00:01:00 Beijing.
        stop()
        with closing(sqlite3.connect(db)) as c, c:
            ids=[r[0] for r in c.execute('SELECT Id FROM GuestRegistrations ORDER BY Id')]
            for ident,stamp in zip(ids,['2026-10-04 15:59:00','2026-10-04 15:59:59.999','2026-10-04 16:00:00','2026-10-04 16:01:00']):
                c.execute('UPDATE GuestRegistrations SET CheckedInAtUtc=? WHERE Id=?',(stamp,ident))
        start()
        test('FD22 单分钟包含00秒和最后一毫秒，合计367.50',lambda:(ensure(bill(end='2026-10-04T23:59')['count']==2),ensure(bill(end='2026-10-04T23:59')['totalCents']==36750)))
        test('FD23 跨午夜含结束分钟，排除下一分钟',lambda:(ensure(bill()['count']==3),ensure(bill()['totalCents']==36750)))
        test('FD24 空时段0笔0元，倒置/无效日期被拒绝',lambda:(ensure(bill('2026-10-06T00:00','2026-10-06T23:59')['count']==0),ensure(bill('2026-10-06T00:00','2026-10-06T23:59')['totalCents']==0),ensure(http('/api/bills?start=2026-10-05T01:00&end=2026-10-05T00:00')[0]==409),ensure(http('/api/bills?start=2026-02-30T00:00&end=2026-10-05T00:00')[0]==409)))
        test('FD25 错误密码不能导出或删除，账单不变',lambda:(post('/api/bills/export',{'start':'2026-10-04T23:59','end':'2026-10-05T00:00','password':'wrong'},409),post(f'/api/stays/{first_id}/delete',{'password':'wrong'},409),ensure(bill()['totalCents']==36750)))
        rows=exported('/api/bills/export',{'start':'2026-10-04T23:59','end':'2026-10-05T00:00','password':PWD})
        test('FD26 交班CSV与页面3笔367.50一致',lambda:(ensure(len(rows)==5),ensure(rows[-1][6]=='367.50'),ensure(sum(round(float(r[6])*100) for r in rows[1:-1])==36750)))
        rows=exported('/api/stays/export',{'password':PWD})
        test('FD27 CSV中文/逗号/换行完整、公式前缀安全',lambda:(ensure(len(rows)==5),ensure(any(r[2]=='\'=测试,"客人"\n换行' for r in rows[1:]))))
        backup=post('/api/backup',{'password':PWD})['path']
        test('FD28 备份包含4笔记录且SQLite完整',lambda:ensure(sql_scalar(backup, 'PRAGMA integrity_check')=='ok'))
        test('FD29 删除归档记录后历史/匹配/账单同步移除，在住不变',lambda:(post(f'/api/stays/{first_id}/delete',{'password':PWD}),ensure(len(stays())==3),ensure(get('/api/guests/suggestions?query=ONLY-A&field=document')==[]),ensure(bill()['totalCents']==19900),ensure(room(601)['guestName']=='前台测试乙')))
        test('FD30 已删除数据保留审计标记',lambda:ensure(sql_scalar(db, 'SELECT DeletedAtUtc FROM GuestRegistrations WHERE Id=?', (first_id,)) is not None))
        stop()
        with closing(sqlite3.connect(backup)) as source, closing(sqlite3.connect(db)) as target: source.backup(target)
        start()
        test('FD31 备份实际恢复后4笔记录和367.50账单一致',lambda:(ensure(len(stays())==4),ensure(bill()['totalCents']==36750),ensure(get('/api/security')['configured'])))
        action(601,'CheckOut');action(601,'StartMaintenance')
        test('FD32 维修不能入住，恢复后须清扫再入住',lambda:(action(601,'CheckIn',guest(),409),action(601,'Restore'),ensure(room(601)['statusKey']=='dirty'),action(601,'Clean'),ensure(room(601)['statusKey']=='ready')))
        test('FD33 停用不能入住，恢复后待清扫',lambda:(action(601,'Disable'),action(601,'CheckIn',guest(),409),action(601,'Restore'),ensure(room(601)['statusKey']=='dirty')))
        # Requests missing a price must never silently change the default to zero.
        r=room(601)
        test('FD34 缺失售价字段不能意外把默认价改为零',lambda:(post(f'/api/rooms/{r["id"]}/price',{'version':r['version']},409),ensure(room(601)['defaultPriceCents']==19900)))
        samples=[]
        for _ in range(30):
            t=time.perf_counter();board();samples.append((time.perf_counter()-t)*1000)
        print(f'PERF: 27 rooms HTTP board median={sorted(samples)[15]:.2f}ms p95={sorted(samples)[28]:.2f}ms',flush=True)
        print(f'RESULT: {len(results)} single-front-desk scenarios passed.',flush=True)
    finally:
        stop();log.close()
        (ROOT/'validation/front-desk-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2))
