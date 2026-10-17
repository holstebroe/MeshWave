#!/usr/bin/env bash
# Sets up a small NAT-realistic network lab for the P2P protocol review's H3 ("all integration tests run on
# 127.0.0.1; no real NAT simulation"). One shared Linux bridge (br-mwnat) plays "the public internet". Each named
# node is either:
#   public    - attached directly to the bridge with its own address: a node with a public IP, or a router that
#               correctly forwards its port. Run the bootstrap (and any "seed" peer for a C4 fallback-source test)
#               with this mode.
#   cone      - lives on a private 192.168.<n>.0/24 behind its own router network namespace, which MASQUERADEs its
#               traffic onto ONE fixed bridge address. The kernel's conntrack NAT often reuses that mapping across
#               destinations, approximating a (address- or port-restricted) cone NAT.
#   symmetric - the same private-network-behind-a-router shape, but the router's MASQUERADE uses --random-fully: a
#               fresh, unpredictable external port per outbound flow from the SAME bridge address. This is what
#               actually defeats naive hole punching, and is what C4 is about.
#
# Each router also drops any *new* (non-reply) connection arriving from the bridge toward its private node, so an
# unsolicited inbound connection (e.g. the bootstrap's dial-back probe, or another peer's first hole-punch packet)
# is refused exactly like a home router with nothing port-forwarded. Every "public" and NAT'd node still ends up
# with one address on the shared bridge, the same way every real NAT'd or public host on the internet is reachable
# through one address — the earlier, simpler design (one point-to-point veth per node straight to the host) gave
# each node a *different* apparent address depending on who observed it, which made hole punching impossible even
# in principle; this shape is what makes it actually testable.
#
# Usage: setup.sh <name>:<mode> [<name>:<mode> ...]
# Must run as root, on Linux, with iproute2 and iptables installed. Run teardown.sh with the same arguments first
# if re-running after a failed setup.
set -euo pipefail

readonly BRIDGE=br-mwnat

if [[ $EUID -ne 0 ]]; then
  echo "setup.sh: must run as root" >&2
  exit 1
fi
if [[ $# -eq 0 ]]; then
  echo "usage: setup.sh <name>:<mode> [<name>:<mode> ...]" >&2
  exit 1
fi

sysctl -w net.ipv4.ip_forward=1 >/dev/null

if ! ip link show "$BRIDGE" &>/dev/null; then
  ip link add "$BRIDGE" type bridge
  ip link set "$BRIDGE" up
fi

idx=0
for spec in "$@"; do
  name="${spec%%:*}"
  mode="${spec##*:}"
  idx=$((idx + 1))

  if [[ ${#name} -gt 10 ]]; then
    echo "setup.sh: node name '$name' is too long (max 10 chars; it is used in veth interface names)" >&2
    exit 1
  fi

  pub_ip="10.99.0.$idx"

  case "$mode" in
    public)
      ns="mwns-$name"
      vh="vh-$name"
      vp="vp-$name"

      ip netns add "$ns"
      ip link add "$vh" type veth peer name "$vp"
      ip link set "$vp" netns "$ns"
      ip link set "$vh" master "$BRIDGE"
      ip link set "$vh" up

      ip netns exec "$ns" ip addr add "$pub_ip/24" dev "$vp"
      ip netns exec "$ns" ip link set "$vp" up
      ip netns exec "$ns" ip link set lo up

      echo "setup.sh: node '$name' ready — namespace=$ns address=$pub_ip mode=public"
      ;;

    cone | symmetric)
      rt="mwrt-$name"
      ns="mwns-$name"
      vbh="vbh-$name"  # host side of the router's bridge uplink
      vbr="vbr-$name"  # router side of the bridge uplink (gets the public bridge address)
      vpr="vpr-$name"  # router side of the private link to the node
      vpp="vpp-$name"  # node side of the private link to the router
      private="192.168.$idx"

      ip netns add "$rt"
      ip netns add "$ns"

      ip link add "$vbh" type veth peer name "$vbr"
      ip link set "$vbr" netns "$rt"
      ip link set "$vbh" master "$BRIDGE"
      ip link set "$vbh" up

      ip link add "$vpr" type veth peer name "$vpp"
      ip link set "$vpr" netns "$rt"
      ip link set "$vpp" netns "$ns"

      ip netns exec "$rt" ip addr add "$pub_ip/24" dev "$vbr"
      ip netns exec "$rt" ip link set "$vbr" up
      ip netns exec "$rt" ip addr add "$private.1/24" dev "$vpr"
      ip netns exec "$rt" ip link set "$vpr" up
      ip netns exec "$rt" ip link set lo up
      ip netns exec "$rt" sysctl -w net.ipv4.ip_forward=1 >/dev/null

      ip netns exec "$ns" ip addr add "$private.2/24" dev "$vpp"
      ip netns exec "$ns" ip link set "$vpp" up
      ip netns exec "$ns" ip link set lo up
      ip netns exec "$ns" ip route add default via "$private.1"

      if [[ "$mode" == "cone" ]]; then
        ip netns exec "$rt" iptables -t nat -A POSTROUTING -s "$private.0/24" -o "$vbr" -j MASQUERADE
      else
        ip netns exec "$rt" iptables -t nat -A POSTROUTING -s "$private.0/24" -o "$vbr" -j MASQUERADE --random-fully
      fi

      # No DNAT / port-forward rule exists, so an inbound connection with no existing conntrack entry (a fresh
      # dial-back probe, or another peer's first hole-punch packet before this node has punched back) is dropped.
      ip netns exec "$rt" iptables -A FORWARD -o "$vpr" -m state --state ESTABLISHED,RELATED -j ACCEPT
      ip netns exec "$rt" iptables -A FORWARD -o "$vpr" -m state --state NEW -j DROP

      echo "setup.sh: node '$name' ready — namespace=$ns private-ip=$private.2 bridge-ip=$pub_ip mode=$mode"
      ;;

    *)
      echo "setup.sh: unknown mode '$mode' for node '$name' (expected public|cone|symmetric)" >&2
      exit 1
      ;;
  esac
done
