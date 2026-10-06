"""Synthetic upgrade fixture: no real guest data."""
import sys, sqlite3, json, hashlib, base64
from contextlib import closing
from pathlib import Path
mode, path, snapshot, backup = sys.argv[1:]
def records(db):
    return {table: db.execute('SELECT * FROM '+table+' ORDER BY '+key).fetchall() for table,key in [('Rooms','Id'),('BoardStates','RoomId'),('GuestRegistrations','Id'),('HotelSettings','Id'),('SecuritySettings','Id')]}
with closing(sqlite3.connect(path)) as db:
    if mode=='prepare':
        db.execute("UPDATE HotelSettings SET Name='升级验证酒店' WHERE Id=1")
        room=db.execute('SELECT Id FROM Rooms ORDER BY RoomNumber LIMIT 1').fetchone()[0]
        db.execute('UPDATE BoardStates SET DefaultPriceCents=32100,Occupancy=2,Version=Version+1 WHERE RoomId=?',(room,))
        db.execute("INSERT INTO GuestRegistrations(RoomId,Name,Phone,DocumentType,DocumentNumber,Notes,Platform,CheckedInAtUtc,SalePriceCents) VALUES(?,?,?,?,?,?,?,?,?)",(room,'升级测试住客','TEST-PHONE','其他','UPGRADE-DOC','测试备注','携程','2026-10-01 01:23:00',29900))
        salt=b'isolated-upgrade-test-salt-32-byte'
        digest=hashlib.pbkdf2_hmac('sha256',b'upgrade-only-2026',salt,210000)
        db.execute('INSERT OR REPLACE INTO SecuritySettings(Id,Salt,Hash,FailedAttempts,LockedUntilUtc) VALUES(1,?,?,0,NULL)',(base64.b64encode(salt).decode(),base64.b64encode(digest).decode()))
        db.commit()
        Path(snapshot).write_text(json.dumps(records(db)),encoding='utf-8')
        with closing(sqlite3.connect(backup)) as dest: db.backup(dest)
        print('PASS: synthetic old-version data and online backup created')
    else:
        assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
        assert json.loads(json.dumps(records(db)))==json.loads(Path(snapshot).read_text(encoding='utf-8'))
        with closing(sqlite3.connect(backup)) as copy:
            assert copy.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
            assert json.loads(json.dumps(records(copy)))==json.loads(Path(snapshot).read_text(encoding='utf-8'))
        print('PASS: upgrade preserves rooms, prices, live guest, hotel name, password hash and backup')
