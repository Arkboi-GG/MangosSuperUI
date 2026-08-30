#!/usr/bin/env bash
# =============================================================================
#  superui-privileged.sh — the ONLY thing SuperUI is allowed to run as root.
# =============================================================================
#
#  SECURITY CONTRACT — read before changing anything here.
#
#  This script is invoked through a NOPASSWD sudoers grant. That means its
#  contents are, effectively, a list of things the SuperUI web application can
#  do as root. Two rules follow, and violating either turns a narrow grant into
#  full passwordless root for whoever can reach the web UI:
#
#    1. This file must be owned by root and NOT writable by the SuperUI user
#       (root:root, mode 0755). Same for every parent directory. If the SuperUI
#       user can rewrite this script, the sudoers line grants it everything.
#
#    2. Every argument is untrusted. It arrives from a web request. Units are
#       matched against a fixed allowlist and values against a numeric range —
#       never interpolated into a path or a shell command unchecked.
#
#  It deliberately does NOT restart anything. Applying a limit and restarting a
#  live world server are different decisions with different blast radii; restart
#  stays with the existing `systemctl restart` grant, driven by the operator.
#
#  Usage:
#    superui-privileged.sh show-limits <unit>
#    superui-privileged.sh set-nofile  <unit> <value>
# =============================================================================
set -euo pipefail

DROPIN_NAME="10-superui-limits.conf"

# Fixed allowlist. Anything not named here is refused, so a crafted unit name
# can never reach systemd or the filesystem.
is_allowed_unit() {
    case "$1" in
        mangosd|realmd|cmangos-mangosd|cmangos-realmd) return 0 ;;
        *) return 1 ;;
    esac
}

die() { echo "error: $*" >&2; exit 1; }

require_unit() {
    [ -n "${1:-}" ] || die "missing unit"
    is_allowed_unit "$1" || die "unit not allowed: $1"
}

# The running process is the only source of truth. A drop-in on disk proves
# nothing until the unit has been restarted, which this script never does.
cmd_show_limits() {
    local unit="$1"
    require_unit "$unit"

    echo "unit=${unit}"
    echo "dropin=/etc/systemd/system/${unit}.service.d/${DROPIN_NAME}"
    if [ -f "/etc/systemd/system/${unit}.service.d/${DROPIN_NAME}" ]; then
        echo "dropin_present=1"
        grep -E "^LimitNOFILE=" "/etc/systemd/system/${unit}.service.d/${DROPIN_NAME}" \
            | sed "s/^/dropin_/" || true
    else
        echo "dropin_present=0"
    fi

    systemctl show "${unit}.service" -p LimitNOFILE -p LimitNOFILESoft 2>/dev/null \
        | sed "s/^/configured_/" || true

    # Resolve the worker, not a wrapper: several pids can match the unit and the
    # small screen/launcher wrapper is usually first. Pick the largest RSS.
    local pid
    pid=$(systemctl show "${unit}.service" -p MainPID --value 2>/dev/null || echo 0)
    if [ "${pid:-0}" -gt 0 ] && [ -d "/proc/$pid" ]; then
        local best="$pid" best_rss=0 child rss
        for child in $(pgrep -P "$pid" 2>/dev/null || true) "$pid"; do
            [ -r "/proc/$child/status" ] || continue
            rss=$(awk '/^VmRSS:/{print $2}' "/proc/$child/status" 2>/dev/null || echo 0)
            if [ "${rss:-0}" -gt "$best_rss" ]; then best_rss=$rss; best=$child; fi
        done
        echo "running_pid=${best}"
        awk '/Max open files/{print "running_soft="$4"\nrunning_hard="$5}' "/proc/$best/limits" 2>/dev/null || true
    else
        echo "running_pid=0"
    fi
}

cmd_set_nofile() {
    local unit="$1" value="${2:-}"
    require_unit "$unit"

    # Digits only, then range-checked. Rejects negatives, "infinity", and
    # anything that could be read as a path or an option.
    case "$value" in
        ''|*[!0-9]*) die "value must be a positive integer" ;;
    esac
    [ "$value" -ge 1024 ] || die "value must be at least 1024"
    [ "$value" -le 1048576 ] || die "value must be at most 1048576"

    local dir="/etc/systemd/system/${unit}.service.d"
    install -d -m 0755 -o root -g root "$dir"

    local tmp
    tmp=$(mktemp "${dir}/.${DROPIN_NAME}.XXXXXX")
    cat > "$tmp" <<EOF
# Written by MangosSuperUI (superui-privileged.sh). Safe to edit or delete.
#
# One bridge socket per bot lives in this process, so descriptors scale with
# fleet size. systemd's default soft limit is 1024, which caps the fleet near
# 990 bots regardless of CPU or RAM.
[Service]
LimitNOFILE=${value}
EOF
    chown root:root "$tmp"
    chmod 0644 "$tmp"
    mv -f "$tmp" "${dir}/${DROPIN_NAME}"

    systemctl daemon-reload

    echo "ok=1"
    echo "unit=${unit}"
    echo "value=${value}"
    echo "dropin=${dir}/${DROPIN_NAME}"
    echo "note=applies on next start of ${unit}"
}

case "${1:-}" in
    show-limits) shift; cmd_show_limits "${1:-}" ;;
    set-nofile)  shift; cmd_set_nofile "${1:-}" "${2:-}" ;;
    *) die "usage: superui-privileged.sh {show-limits <unit>|set-nofile <unit> <value>}" ;;
esac
