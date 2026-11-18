#!/usr/bin/env python3
"""A synthetic chat.db with the columns imsg reads. Invented people, 555-01xx numbers only.

With a home directory as the second argument it also writes, under it, a local Contacts store
(names for most handles, no photos) and Messages' pin list, so the sidebar shows a pinned grid.
"""
import os, plistlib, sqlite3, sys
path = sys.argv[1]
home = sys.argv[2] if len(sys.argv) > 2 else None
if os.path.exists(path): os.remove(path)
con = sqlite3.connect(path)
con.executescript("""
CREATE TABLE chat (ROWID INTEGER PRIMARY KEY, chat_identifier TEXT, guid TEXT, display_name TEXT, room_name TEXT,
  service_name TEXT, group_id TEXT, original_group_id TEXT, style INTEGER);
CREATE TABLE handle (ROWID INTEGER PRIMARY KEY, id TEXT);
CREATE TABLE message (ROWID INTEGER PRIMARY KEY, guid TEXT, date INTEGER, text TEXT, attributedBody BLOB,
  is_from_me INTEGER, is_read INTEGER DEFAULT 0, error INTEGER DEFAULT 0, handle_id INTEGER, service TEXT,
  balloon_bundle_id TEXT, payload_data BLOB, item_type INTEGER DEFAULT 0, associated_message_type INTEGER DEFAULT 0,
  associated_message_guid TEXT, date_read INTEGER DEFAULT 0, date_delivered INTEGER DEFAULT 0,
  is_delivered INTEGER DEFAULT 1, cache_has_attachments INTEGER DEFAULT 0, thread_originator_guid TEXT,
  thread_originator_part TEXT, expressive_send_style_id TEXT, is_audio_message INTEGER DEFAULT 0, group_title TEXT,
  schedule_type INTEGER, schedule_state INTEGER, date_edited INTEGER DEFAULT 0, date_retracted INTEGER DEFAULT 0,
  message_summary_info BLOB);
CREATE TABLE chat_message_join (chat_id INTEGER, message_id INTEGER, message_date INTEGER);
CREATE TABLE chat_handle_join (chat_id INTEGER, handle_id INTEGER);
CREATE TABLE attachment (ROWID INTEGER PRIMARY KEY, filename TEXT, transfer_name TEXT, mime_type TEXT, uti TEXT, total_bytes INTEGER);
CREATE TABLE message_attachment_join (message_id INTEGER, attachment_id INTEGER);
CREATE TABLE chat_recoverable_message_join (message_id INTEGER);
INSERT INTO handle VALUES (1,'+15550100011'),(2,'+15550100022'),(3,'+15550100033'),(4,'+15550100044'),
  (5,'+15550100055'),(6,'+15550100066'),(7,'+15550100077');
INSERT INTO chat VALUES (1,'+15550100011','iMessage;-;+15550100011',NULL,NULL,'iMessage',NULL,NULL,45);
INSERT INTO chat VALUES (2,'chat900000000000000001','iMessage;+;chat900000000000000001','Trail Crew','chat900000000000000001','iMessage','G1',NULL,43);
INSERT INTO chat VALUES (3,'+15550100033','iMessage;-;+15550100033',NULL,NULL,'iMessage',NULL,NULL,45);
INSERT INTO chat VALUES (4,'+15550100044','iMessage;-;+15550100044',NULL,NULL,'iMessage',NULL,NULL,45);
INSERT INTO chat VALUES (5,'chat900000000000000005','iMessage;+;chat900000000000000005','Book Club','chat900000000000000005','iMessage','G5',NULL,43);
INSERT INTO chat VALUES (6,'+15550100066','iMessage;-;+15550100066',NULL,NULL,'iMessage',NULL,NULL,45);
INSERT INTO chat VALUES (7,'+15550100077','SMS;-;+15550100077',NULL,NULL,'SMS',NULL,NULL,45);
INSERT INTO chat VALUES (8,'chat900000000000000008','iMessage;+;chat900000000000000008','Ski Weekend','chat900000000000000008','iMessage','G8',NULL,43);
INSERT INTO chat_handle_join VALUES (1,1),(2,1),(2,2),(3,3),(4,4),(5,3),(5,4),(5,5),(6,6),(7,7),(8,2),(8,6);
INSERT INTO attachment VALUES (30,'~/Library/Messages/Attachments/ab/01/photo.png','photo.png','image/png','public.png',800);
INSERT INTO attachment VALUES (31,'~/Library/Messages/Attachments/ab/02/IMG_0042.jpeg','IMG_0042.jpeg','image/jpeg','public.jpeg',47270);
INSERT INTO message_attachment_join VALUES (10,30),(14,31);
""")
B = 800000000 * 10**9
S = 10**9
rows = [  # rowid, guid, date, text, from_me, read, handle, chat, assoc_type, assoc_guid
    (10, "g10", B, "hello there", 0, 1, 1, 1, 0, None),
    (11, "g11", B + 60*S, "hi back", 1, 1, 0, 1, 0, None),
    (13, "g13", B + 90*S, "Loved “hi back”", 0, 1, 1, 1, 2000, "p:0/g11"),
    (14, "g14", B + 100*S, "\ufffc", 0, 1, 1, 1, 0, None),   # a real photo, stored sideways (EXIF 6)
    (12, "g12", B + 120*S, "unread one", 0, 0, 1, 1, 0, None),
    (20, "g20", B + 30*S, "group msg", 0, 1, 2, 2, 0, None),
    # Emoji and services for the UI screenshots: ZWJ families, skin tones, flags, keycaps, VS16 hearts.
    (21, "g21", B + 200*S, "made it 🎉🥳 the view is unreal 🏔️", 0, 1, 1, 2, 0, None),
    (22, "g22", B + 210*S, "😂😂 love it ❤️ 👍🏽 👨‍👩‍👧 🇨🇦 #️⃣ 1️⃣\nsecond line stays a line", 1, 1, 0, 2, 0, None),
    (23, "g23", B + 220*S, "sent over RCS 🔥", 1, 1, 0, 2, 0, None),
    (24, "g24", B + 230*S, "sent over SMS ✅ (draws blue)", 1, 1, 0, 2, 0, None),
    # More people, so the sidebar has a pinned grid and a list under it.
    (40, "g40", B - 3600*S, "see you saturday", 1, 1, 0, 3, 0, None),
    (41, "g41", B + 150*S, "did you get the tickets?", 0, 0, 4, 4, 0, None),
    (42, "g42", B - 7200*S, "chapter 9 is wild 📚", 0, 1, 5, 5, 0, None),
    (43, "g43", B - 86400*S, "thanks again!", 0, 1, 6, 6, 0, None),
    (44, "g44", B - 2*86400*S, "Your code is 555 010", 0, 1, 7, 7, 0, None),
    (45, "g45", B - 3*86400*S, "who's driving? 🚗", 0, 1, 6, 8, 0, None),
]
SERVICE = {23: "RCS", 24: "SMS", 44: "SMS"}
for mid, guid, d, t, fm, rd, h, c, at, ag in rows:
    con.execute("""INSERT INTO message (ROWID,guid,date,text,is_from_me,is_read,handle_id,service,
                   associated_message_type,associated_message_guid,cache_has_attachments)
                   VALUES (?,?,?,?,?,?,?,?,?,?,?)""", (mid, guid, d, t, fm, rd, h, SERVICE.get(mid, "iMessage"), at, ag, 1 if mid in (10, 14) else 0))
    con.execute("INSERT INTO chat_message_join VALUES (?,?,?)", (c, mid, d))
con.commit()

if home:
    # Contacts "On My Mac": one source, no account behind it. +15550100077 stays a bare number.
    src = os.path.join(home, "Library/Application Support/AddressBook/Sources/LOCAL")
    os.makedirs(src, exist_ok=True)
    ab = os.path.join(src, "AddressBook-v22.abcddb")
    if os.path.exists(ab): os.remove(ab)
    a = sqlite3.connect(ab)
    a.executescript("""
    CREATE TABLE ZABCDRECORD (Z_PK INTEGER PRIMARY KEY, ZFIRSTNAME TEXT, ZLASTNAME TEXT, ZORGANIZATION TEXT,
      ZNICKNAME TEXT, ZTHUMBNAILIMAGEDATA BLOB, ZIMAGEDATA BLOB);
    CREATE TABLE ZABCDPHONENUMBER (Z_PK INTEGER PRIMARY KEY, ZOWNER INTEGER, ZFULLNUMBER TEXT);
    CREATE TABLE ZABCDEMAILADDRESS (Z_PK INTEGER PRIMARY KEY, ZOWNER INTEGER, ZADDRESS TEXT);
    INSERT INTO ZABCDRECORD (Z_PK, ZFIRSTNAME, ZLASTNAME) VALUES
      (1,'Maya','Okafor'),(2,'Jonas','Feld'),(3,'Leo','Brandt'),(4,'Priya','Natarajan'),(5,'Ines','Moreau'),(6,'Sam','Rivera');
    INSERT INTO ZABCDPHONENUMBER (ZOWNER, ZFULLNUMBER) VALUES
      (1,'+1 (555) 010-0011'),(2,'+1 (555) 010-0022'),(3,'+1 (555) 010-0033'),(4,'+1 (555) 010-0044'),
      (5,'+1 (555) 010-0055'),(6,'+1 (555) 010-0066');
    """)
    a.commit()
    # Messages' pins, in pin order: DMs by handle, groups by group id.
    prefs = os.path.join(home, "Library/Preferences")
    os.makedirs(prefs, exist_ok=True)
    with open(os.path.join(prefs, "com.apple.messages.pinning.plist"), "wb") as f:
        plistlib.dump({"pD": {"pP": ["+15550100011", "G1", "+15550100033", "+15550100044", "G5"]}}, f, fmt=plistlib.FMT_BINARY)
