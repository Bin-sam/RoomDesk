"""Read-only initialization probe for the isolated Windows CI installation."""
import sqlite3
import sys
import time
from contextlib import closing
expected = int(sys.argv[2]) if len(sys.argv)>2 else 33
for _ in range(60):
    try:
        with closing(sqlite3.connect(sys.argv[1])) as connection:
            if connection.execute('SELECT count(*) FROM Rooms').fetchone()[0] == expected:
                if expected == 33:
                    groups = {
                        '标准双人间':[8510,8402,8512,8410,8502,8602,8610],
                        '豪华单人房':[8403,8611,8505,8608,8605,8503,8603,8508,8411,8405,8511,8408],
                        '豪华双人房':[8606,8506,8406],
                        '商务双人房【棋牌】':[8507,8509,8401,8609,8409,8407,8607,8601,8501],
                        '商务套房':[8612,8412]}
                    actual=connection.execute('SELECT r.RoomNumber,r.FloorNumber,s.TypeName,s.Occupancy,s.IsClean,s.DefaultPriceCents FROM Rooms r JOIN BoardStates s ON s.RoomId=r.Id').fetchall()
                    desired=[(n,n//100%10,t,0,0,None) for t,ns in groups.items() for n in ns]
                    assert sorted(actual)==sorted(desired), 'Installed room defaults differ from photo manifest'
                sys.exit(0)
    except sqlite3.Error:
        pass
    time.sleep(0.5)
raise SystemExit(f'Installed application did not initialize {expected} rooms.')
