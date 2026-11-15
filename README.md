# Blip for Windows

iMessage on Windows, through the Blip bridge on your Mac.

This is a native Windows client (.NET 10, WPF with the Fluent theme) for the Mac-side bridge
from [Blip](https://github.com/nixfred/blip). It reads your conversations, sends texts and
files, shows attachments, tapbacks, replies and read receipts, and updates in about two
seconds when a message lands. As with Blip on Linux, the Mac does the iMessage work and
Windows is a thin client. There's no server on the Mac, and nothing changes on the Mac beyond
enrolling one more ssh key. The only message text kept on Windows is an optional
DPAPI-encrypted startup snapshot (see [Startup cache](#startup-cache)).

It lives outside the Blip repository on purpose. The bridge stays upstream, and this client
pins the bridge contract it was tested against (see [Compatibility](#compatibility)).

## How it works

```
 Windows (this app)                                   Mac (Blip bridge, unchanged)
 ┌───────────────────────────┐   one SSH connection   ┌──────────────────────────────────┐
 │ Blip.Windows (WPF)        │   many channels        │ authorized_keys:                 │
 │  └ Blip.Core              │ ─────────────────────▶ │  restrict,command="…/blip-       │
 │     ├ SshBridgeTransport  │  imsg serve  ×2 (pool) │  dispatch" <this key>            │
 │     │  (SSH.NET, pinned   │  imsg watch  (push)    │      │                           │
 │     │   host key)         │  one-shot: imsg-send,  │      ▼                           │
 │     ├ BlipClient          │  imsg-read, attachment │ imsg / imsg-send / imsg-read …   │
 │     └ BridgeMonitor       │                        │ chat.db (read-only) + Messages   │
 └───────────────────────────┘                        └──────────────────────────────────┘
```

- **Transport.** One persistent SSH.NET connection with several channels on it. Windows
  OpenSSH has no ControlMaster, so calling `ssh.exe` for each request would pay a full
  handshake every time.
- **Reads** go over two pooled `imsg serve` channels, which keep one chat.db handle open per
  channel. A query costs the SQL, not ~90 ms of ssh and Python startup. If a channel dies,
  the call falls back to a one-shot run.
- **Push.** `imsg watch` prints a timestamp whenever chat.db changes. That's an
  invalidation, never message content. A heartbeat every 30 s means silence longer than 75 s
  counts as a dead link, and the client reconnects with backoff.
- **Sends** use `imsg-send --yes --text-stdin`. Message text, captions, search queries and
  reply bodies always travel on stdin, never in argv, as on Linux.

## Setup

### On the Mac (once)

If you already use Blip from Linux, this is done: the tools are in `~/.blip/bin` and the
Full Disk Access and Automation grants are in place. Skip to Windows.

Otherwise, from a checkout of Blip on the Mac:

```sh
bridge/mac/install.sh      # copies the tools to ~/.blip/bin, turns on Remote Login, checks TCC
```

Then grant the two permissions that can't be scripted, as Blip's README describes. Full Disk
Access goes to `/usr/libexec/sshd-keygen-wrapper`, and the Automation prompt to let it control
Messages appears on the Mac's screen. `python3 ~/.blip/bin/blip-check` tells you what's
still missing.

### On Windows

1. Build and run (see below). On first launch the settings window opens.
2. **Your Mac.** Enter `user@your-mac`, the same target you'd use with ssh.
3. **Key.** Click *Create key*. This runs Windows' own `ssh-keygen` to make a dedicated
   ed25519 key at `%USERPROFILE%\.ssh\blip_ed25519`. Don't use your everyday key.
4. **Enroll.** Click *Copy command* and paste it into Terminal on the Mac. It appends one
   line to `~/.ssh/authorized_keys`:
   `restrict,command="$HOME/.blip/bin/blip-dispatch" ssh-ed25519 AAAA… blip-windows@PC`.
   That key can run the eight bridge tools and nothing else, with no shell and no forwarding.
5. **Test connection.** The first connection shows the Mac's host key fingerprint. Compare
   it with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on the Mac and accept. From then
   on the key is pinned, and a changed key is refused, never silently re-trusted.
6. Save.

Settings live in `%APPDATA%\Blip\bridge.conf`. It uses the same `key=value` format and keys
as the Linux `bridge.conf`, plus `port`, `host_key`, `push_read` and `local_cache`.

### Startup cache

The bridge has no incremental feed (`imsg watch` only says *something* changed), so the
client makes startup fast locally instead:

- **Photos** are cached in `%LOCALAPPDATA%\Blip\avatars`, named by a hash of the chat id,
  and refreshed at most once a day. "No photo" is cached too. Each photo costs the Mac a
  fresh process that scans the address book, so they're fetched two at a time, behind
  thread loads and image previews.
- **Snapshot** (`local_cache=on`, the default). The last sidebar and the newest 80 messages
  of up to 20 recent conversations are saved to `%LOCALAPPDATA%\Blip\snapshot.bin`,
  encrypted with DPAPI for the current Windows user. The window paints from it at launch,
  then live data replaces it within a second or two. A snapshot from another Mac or other
  hide filters is ignored. Turning the option off in Settings deletes the file.
- **Warm threads.** After the first live load, the newest 8 conversations are fetched one
  at a time, so opening them is instant. Opening any conversation shows its last known copy
  at once while the live copy loads.

Deleting any of these files is always safe: everything comes back from the Mac.

## Build, test, run

Requires the .NET 10 SDK.

```powershell
dotnet build                     # whole solution (also builds on Linux/macOS for CI)
dotnet test tests/Blip.Core.Tests
dotnet run --project src/Blip.Windows
# a single self-contained exe:
dotnet publish src/Blip.Windows -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### End-to-end tests

`tests/Blip.Core.E2E` runs over real SSH against a **stub Mac**: a local `sshd` on Linux
whose `authorized_keys` pins a fresh key to the real `blip-dispatch`, which runs the real
`imsg` against a synthetic chat.db with invented people and 555-01xx numbers. Only
`imsg-send` and `imsg-read` are stubbed, because AppleScript can't run off a Mac. The stubs
record argv and stdin, so the tests prove message text crossed byte for byte on stdin.

```sh
sudo tests/e2e/setup-stub-mac.sh /path/to/blip
eval "$(sudo tests/e2e/setup-stub-mac.sh --env)"
dotnet test tests/Blip.Core.E2E
```

Never point the E2E tests at a real Mac, because the send tests would send real messages.
They skip themselves unless the stub's environment variables are set.

## What works

| | |
|---|---|
| Conversation list | Every conversation, Messages' pins first in Messages' order, unread dots from the Mac's own read state (shared with your iPhone and the Linux client), contact and group photos, re-keyed groups folded into one |
| Thread | Text, inline image previews (HEIC converted on the Mac), file chips, tapbacks, inline-reply quotes, edited and unsent markers, send effects, link titles, "Read 4:42 PM" / "Delivered", failed-delivery flags |
| Send | Text (Enter; Shift+Enter for a newline), files by button, drag and drop or Ctrl+V (images and copied files), caption with the last file, SMS/RCS chosen by the same rules as Linux (`SendServiceSelector`), groups by their AppleScript GUID, new conversation to a number or email |
| Live | Push within ~2 s, toasts for new messages (one per conversation, click to open; muted conversations stay quiet), tray icon with unread count, close-to-tray |
| Opt-in | Tapbacks and threaded replies (right-click a bubble). These drive Messages through Accessibility, so they're off by default as on Linux. Mark-read-on-open (`push_read`) brings Messages to the front on the Mac, so it's off by default too, and opening a conversation clears its dot locally only. |
| Startup | Opens from an encrypted snapshot, cached photos and prefetched threads, then refreshes live ([Startup cache](#startup-cache)) |
| Safety | Host key pinning, a confined key, text on stdin only, message text on disk only DPAPI-encrypted (and only with `local_cache`), Mark-of-the-Web on every downloaded attachment, and program files (`.exe`, `.lnk`, `.js`, …) shown in Explorer but never launched |

### Not yet

- Message search UI. `BlipClient.SearchAsync` exists, but there's no screen for it yet.
- Playing voice messages. iPhone audio is CAF, which Windows can't play by default; Linux
  re-encodes it with ffmpeg. For now it opens in your default app.
- Sticker overlays, custom-emoji tapbacks, contact cards, Shared City, the security-code
  autofill.
- The self-thread duplicate fold (`dedupeSelfEcho` in collector.ts).

## Compatibility

Tested against Blip `63f42e1` (bridge `claude-on-mac 3f93270`, v2.0.0 + Blip extensions; see
`bridge/BRIDGE-VERSION` in Blip). The wire contract is the bridge's argv plus its JSON. The
fixtures in `tests/Blip.Core.Tests/Fixtures` are real `imsg` output, and the E2E suite runs
the real tools, so rerun both after updating the Mac side.

## Layout

```
src/Blip.Core        transport, wire models, client API, push monitor, attachment cache (net10.0, cross-platform)
src/Blip.Windows     the WPF app
tests/Blip.Core.Tests   unit + contract tests (no network)
tests/Blip.Core.E2E     real SSH against a stub Mac
tests/e2e               stub-Mac setup script, synthetic chat.db, send stub
```
