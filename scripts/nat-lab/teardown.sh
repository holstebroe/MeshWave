#!/usr/bin/env bash
# Reverses setup.sh: removes the namespaces, veth pairs and bridge it created.
# Usage: teardown.sh <name>:<mode> [<name>:<mode> ...]   (same arguments as the matching setup.sh call)
set -uo pipefail

readonly BRIDGE=br-mwnat

if [[ $EUID -ne 0 ]]; then
  echo "teardown.sh: must run as root" >&2
  exit 1
fi

for spec in "$@"; do
  name="${spec%%:*}"
  mode="${spec##*:}"

  case "$mode" in
    public)
      ip netns del "mwns-$name" 2>/dev/null
      ip link del "vh-$name" 2>/dev/null
      ;;
    cone | symmetric)
      # Deleting a namespace destroys every interface still inside it, including the peer end of a veth pair
      # whose other end lives in a different (still-alive) namespace, so both netns deletions are enough for
      # everything except the host-side bridge uplink.
      ip netns del "mwrt-$name" 2>/dev/null
      ip netns del "mwns-$name" 2>/dev/null
      ip link del "vbh-$name" 2>/dev/null
      ;;
  esac

  echo "teardown.sh: node '$name' removed"
done

ip link del "$BRIDGE" 2>/dev/null

exit 0
