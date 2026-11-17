#!/usr/bin/env python3
"""A synthetic chat.db with the columns imsg reads. Invented people, 555-01xx numbers only."""
import os, sqlite3, sys
path = sys.argv[1]
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
INSERT INTO handle VALUES (1,'+15550100011'),(2,'+15550100022');
INSERT INTO chat VALUES (1,'+15550100011','iMessage;-;+15550100011',NULL,NULL,'iMessage',NULL,NULL,45);
INSERT INTO chat VALUES (2,'chat900000000000000001','iMessage;+;chat900000000000000001','Trail Crew','chat900000000000000001','iMessage','G1',NULL,43);
INSERT INTO chat_handle_join VALUES (1,1),(2,1),(2,2);
INSERT INTO attachment VALUES (30,'~/Library/Messages/Attachments/ab/01/photo.png','photo.png','image/png','public.png',800);
INSERT INTO message_attachment_join VALUES (10,30);
""")
B = 800000000 * 10**9
S = 10**9
rows = [  # rowid, guid, date, text, from_me, read, handle, chat, assoc_type, assoc_guid
    (10, "g10", B, "hello there", 0, 1, 1, 1, 0, None),
    (11, "g11", B + 60*S, "hi back", 1, 1, 0, 1, 0, None),
    (13, "g13", B + 90*S, "Loved “hi back”", 0, 1, 1, 1, 2000, "p:0/g11"),
    (12, "g12", B + 120*S, "unread one", 0, 0, 1, 1, 0, None),
    (20, "g20", B + 30*S, "group msg", 0, 1, 2, 2, 0, None),
    # Emoji and services for the UI screenshots: ZWJ families, skin tones, flags, keycaps, VS16 hearts.
    (21, "g21", B + 200*S, "made it 🎉🥳 the view is unreal 🏔️", 0, 1, 1, 2, 0, None),
    (22, "g22", B + 210*S, "😂😂 love it ❤️ 👍🏽 👨‍👩‍👧 🇨🇦 #️⃣ 1️⃣\nsecond line stays a line", 1, 1, 0, 2, 0, None),
    (23, "g23", B + 220*S, "sent over RCS 🔥", 1, 1, 0, 2, 0, None),
    (24, "g24", B + 230*S, "sent over SMS ✅ (draws blue)", 1, 1, 0, 2, 0, None),
]
SERVICE = {23: "RCS", 24: "SMS"}
for mid, guid, d, t, fm, rd, h, c, at, ag in rows:
    con.execute("""INSERT INTO message (ROWID,guid,date,text,is_from_me,is_read,handle_id,service,
                   associated_message_type,associated_message_guid,cache_has_attachments)
                   VALUES (?,?,?,?,?,?,?,?,?,?,?)""", (mid, guid, d, t, fm, rd, h, SERVICE.get(mid, "iMessage"), at, ag, 1 if mid == 10 else 0))
    con.execute("INSERT INTO chat_message_join VALUES (?,?,?)", (c, mid, d))
con.commit()
