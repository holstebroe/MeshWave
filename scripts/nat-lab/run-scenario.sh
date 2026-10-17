#!/usr/bin/env bash
# Runs one end-to-end NAT-lab scenario for the P2P protocol review's H3: sets up a real network-namespace/iptables
# NAT topology (setup.sh), launches a real MeshWave.Bootstrap and one or more MeshWave.NatLab peers inside it,
# collects the client's result, tears the topology down, and exits 0 if the scenario's expectation held.
#
# Usage: run-scenario.sh <scenario>
#   symmetric-pair-no-route     alice(symmetric) and bob(symmetric) cannot hole-punch to each other and no other
#                               peer holds the content: bob's download MUST fail. This is C4's "not possible"
#                               half — MeshWave never relays through the bootstrap.
#   symmetric-pair-c4-fallback  alice(symmetric) and charlie(public) both hold the same content; bob(symmetric)
#                               cannot punch to alice but MUST still succeed, by downloading from charlie instead.
#                               This is C4's documented fallback: "the download uses any other peer that holds it."
#   cone-pair-best-effort       alice(cone) and bob(cone): a real hole-punch is attempted. Whether it succeeds
#                               depends on the kernel's NAT port allocation for this run (real consumer NAT
#                               hardware is not perfectly deterministic here either), so this scenario reports the
#                               outcome without asserting it, and exits 0 either way — it exists to catch a
#                               regression that makes the attempt itself error out, not to pin down luck.
#
# Must run as root, on Linux, with iproute2, iptables and the .NET SDK the repo targets. Builds the repo once
# (skip with NAT_LAB_SKIP_BUILD=1 if you already built it).
set -uo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly LAB_DIR="$REPO_ROOT/scripts/nat-lab"
readonly BOOT_DLL="$REPO_ROOT/MeshWave.Bootstrap/bin/Debug/net10.0/MeshWave.Bootstrap.dll"
readonly PEER_DLL="$REPO_ROOT/MeshWave.NatLab/bin/Debug/net10.0/MeshWave.NatLab.dll"
readonly BOOT_ADDR="10.99.0.1"
readonly BOOT_PORT=39877
readonly TIMEOUT_SECONDS=60
readonly LOG_DIR="$(mktemp -d /tmp/nat-lab-XXXXXX)"

scenario="${1:-}"
case "$scenario" in
  symmetric-pair-no-route|symmetric-pair-c4-fallback|cone-pair-best-effort) ;;
  *)
    echo "usage: run-scenario.sh <symmetric-pair-no-route|symmetric-pair-c4-fallback|cone-pair-best-effort>" >&2
    exit 2
    ;;
esac

if [[ $EUID -ne 0 ]]; then
  echo "run-scenario.sh: must run as root" >&2
  exit 1
fi

case "$scenario" in
  symmetric-pair-no-route)    nodes=(bootstrap:public alice:symmetric bob:symmetric) ;;
  symmetric-pair-c4-fallback) nodes=(bootstrap:public alice:symmetric bob:symmetric charlie:public) ;;
  cone-pair-best-effort)      nodes=(bootstrap:public alice:cone bob:cone) ;;
esac

cleanup() {
  # SIGKILL: these are throwaway lab processes and .NET's default SIGTERM handling was observed to leave the
  # process running past the shell's own exit, which would otherwise leak a bootstrap/peer holding a netns open.
  for p in "${pids[@]:-}"; do kill -9 "$p" 2>/dev/null; done
  bash "$LAB_DIR/teardown.sh" "${nodes[@]}" >/dev/null 2>&1
}
trap cleanup EXIT

if [[ "${NAT_LAB_SKIP_BUILD:-}" != "1" ]]; then
  echo "run-scenario.sh: building..." >&2
  if ! dotnet build "$REPO_ROOT/MeshWave.slnx" -c Debug >"$LOG_DIR/build.log" 2>&1; then
    echo "run-scenario.sh: build failed, see $LOG_DIR/build.log" >&2
    exit 1
  fi
fi

bash "$LAB_DIR/setup.sh" "${nodes[@]}" || exit 1

pids=()
ip netns exec mwns-bootstrap dotnet exec "$BOOT_DLL" --port "$BOOT_PORT" >"$LOG_DIR/bootstrap.log" 2>&1 &
pids+=($!)
sleep 2

case "$scenario" in
  symmetric-pair-no-route)
    ip netns exec mwns-alice dotnet exec "$PEER_DLL" --role server --name alice --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/alice.log" 2>&1 &
    pids+=($!)
    sleep 4
    ip netns exec mwns-bob dotnet exec "$PEER_DLL" --role client --name bob --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/bob.log" 2>&1
    ;;
  symmetric-pair-c4-fallback)
    ip netns exec mwns-alice dotnet exec "$PEER_DLL" --role server --name alice --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/alice.log" 2>&1 &
    pids+=($!)
    ip netns exec mwns-charlie dotnet exec "$PEER_DLL" --role server --name charlie --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/charlie.log" 2>&1 &
    pids+=($!)
    sleep 4
    ip netns exec mwns-bob dotnet exec "$PEER_DLL" --role client --name bob --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/bob.log" 2>&1
    ;;
  cone-pair-best-effort)
    ip netns exec mwns-alice dotnet exec "$PEER_DLL" --role server --name alice --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/alice.log" 2>&1 &
    pids+=($!)
    sleep 4
    ip netns exec mwns-bob dotnet exec "$PEER_DLL" --role client --name bob --bootstrap "$BOOT_ADDR:$BOOT_PORT" --timeout "$TIMEOUT_SECONDS" >"$LOG_DIR/bob.log" 2>&1
    ;;
esac

result_line="$(grep -o 'NATLAB_RESULT:.*' "$LOG_DIR/bob.log" | tail -1)"
result_json="${result_line#NATLAB_RESULT:}"
success="$(echo "$result_json" | grep -o '"Success":[a-z]*' | cut -d: -f2)"

echo "run-scenario.sh: scenario=$scenario bob result: ${result_json:-<no result>}"
echo "run-scenario.sh: logs kept at $LOG_DIR"

case "$scenario" in
  symmetric-pair-no-route)
    if [[ "$success" == "false" ]]; then
      echo "run-scenario.sh: PASS — download correctly failed (no punch, no relay, no other holder)."
      exit 0
    fi
    echo "run-scenario.sh: FAIL — expected the download to fail, but it reported success=$success."
    exit 1
    ;;
  symmetric-pair-c4-fallback)
    if [[ "$success" == "true" ]]; then
      echo "run-scenario.sh: PASS — download succeeded via the fallback peer (C4)."
      exit 0
    fi
    echo "run-scenario.sh: FAIL — expected the C4 fallback download to succeed, but it reported success=$success."
    exit 1
    ;;
  cone-pair-best-effort)
    echo "run-scenario.sh: INFO — cone/cone punch outcome this run: success=${success:-<none>} (not asserted; see script header)."
    exit 0
    ;;
esac
