"""Read-only initialization probe for the isolated Windows CI installation."""
import sqlite3
import sys
import time
from contextlib import closing
for _ in range(60):
    try:
        with closing(sqlite3.connect(sys.argv[1])) as connection:
            if connection.execute('SELECT count(*) FROM Rooms').fetchone()[0] == 24:
                sys.exit(0)
    except sqlite3.Error:
        pass
    time.sleep(0.5)
raise SystemExit('Installed application did not initialize 24 rooms.')
