#!/usr/bin/env bash
# Stand up a STUB Mac for Blip.Core.E2E on a Linux box (CI or a dev VM), as root.
#
# A local sshd on 127.0.0.1:2222 whose authorized_keys pins a fresh key to the REAL
# blip-dispatch, which runs the REAL imsg against a synthetic chat.db (invented people,
# 555-01xx numbers). imsg-send and imsg-read are stubbed (no AppleScript off a Mac):
# they append what arrived (argv + stdin) to ~blipmac/sent.log so tests can check that
# message text crossed on stdin byte for byte.
#
#   sudo tests/e2e/setup-stub-mac.sh /path/to/blip      # the Blip repo (for bridge/mac)
#   eval "$(sudo tests/e2e/setup-stub-mac.sh --env)"
#   dotnet test tests/Blip.Core.E2E
set -euo pipefail
WORK=${BLIP_E2E_WORK:-/tmp/blip-e2e}
H=/home/blipmac
if [[ ${1:-} == --env ]]; then
  fp=$(ssh-keygen -lf "$WORK/host_ed25519.pub" | awk '{print $2}')
  cat <<ENV
export BLIP_E2E_HOST=blipmac@127.0.0.1 BLIP_E2E_PORT=2222 BLIP_E2E_KEY=$WORK/client_ed25519
export BLIP_E2E_SENT_LOG=$H/sent.log BLIP_E2E_CHATDB=$H/Library/Messages/chat.db BLIP_E2E_HOSTKEY=$fp
ENV
  exit 0
fi
BLIP=${1:?path to the Blip repo}
HERE=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
id blipmac >/dev/null 2>&1 || useradd -m -s /bin/bash blipmac
usermod -p '*' blipmac                     # unlocked, no password: key auth only
mkdir -p "$WORK" "$H/Library/Messages/Attachments/ab/01" "$H/.blip/bin" "$H/.ssh" /run/sshd
python3 "$HERE/make_chat_db.py" "$H/Library/Messages/chat.db"
printf 'PNGDATA-%.0s' {1..100} > "$H/Library/Messages/Attachments/ab/01/photo.png"
find "$BLIP/bridge/mac" -maxdepth 1 -type f -exec cp {} "$H/.blip/bin/" \;
for t in imsg-send imsg-read; do cp "$HERE/stub-tool.py" "$H/.blip/bin/$t"; done
chmod +x "$H/.blip/bin/"*
[[ -f $WORK/host_ed25519 ]] || ssh-keygen -q -t ed25519 -N "" -f "$WORK/host_ed25519"
[[ -f $WORK/client_ed25519 ]] || ssh-keygen -q -t ed25519 -N "" -C blip-windows-e2e -f "$WORK/client_ed25519"
echo "restrict,command=\"\$HOME/.blip/bin/blip-dispatch\" $(cat "$WORK/client_ed25519.pub")" > "$H/.ssh/authorized_keys"
rm -f "$H/sent.log"
chown -R blipmac:blipmac "$H"; chmod 700 "$H/.ssh"; chmod 600 "$H/.ssh/authorized_keys"
cat > "$WORK/sshd_config" <<CFG
Port 2222
ListenAddress 127.0.0.1
HostKey $WORK/host_ed25519
PidFile $WORK/sshd.pid
PasswordAuthentication no
KbdInteractiveAuthentication no
UsePAM no
AllowUsers blipmac
CFG
[[ -f $WORK/sshd.pid ]] && kill "$(cat "$WORK/sshd.pid")" 2>/dev/null || true
/usr/sbin/sshd -f "$WORK/sshd_config" -E "$WORK/sshd.log"
echo "stub Mac on 127.0.0.1:2222. Now: eval \"\$(sudo $0 --env)\""
