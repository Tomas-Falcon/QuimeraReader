import sqlite3
import sys

try:
    conn = sqlite3.connect('QuimeraReader.API/quimera.db')
    cursor = conn.cursor()
    cursor.execute('SELECT COUNT(*) FROM Categories')
    print('Categories count:', cursor.fetchone()[0])
except Exception as e:
    print('ERROR:', e)
