#!/usr/bin/env python3
"""Mutes the microphone while the avatar's AFK switch is on, and gives it back after.

    tools/away_mic.py                 # run beside VRChat, with OSC enabled in its menu
    tools/away_mic.py --with-afk      # also when VRChat itself marks you away (headset off, End key)
    tools/away_mic.py --test          # check the rules below, without VRChat

An avatar cannot mute its wearer: VRChat lets it read whether the microphone
is muted (the MuteSelf parameter) and never change it. A program on the same
computer can, over OSC, by pressing the voice button. This is that program,
for the away feature of net.wirenook.party-tricks (the "AFK" menu switch,
WN/Away).

The rules, in the order they matter:

- Switching AFK on mutes you, if you were not muted already.
- Switching it off unmutes you only if this program did the muting, the
  mute was seen to take, and nothing has changed the microphone since. Mute
  or unmute by hand while away and the microphone is yours again: it stays
  however you left it.
- If you were muted before, you stay muted after.

It needs "Toggle Voice" on in VRChat's audio settings, so that the voice
button toggles. With push-to-talk there is nothing to do: you are muted
whenever you are not holding the button. The press would then open the
microphone for a twentieth of a second; the program notices that no mute
followed and leaves the microphone alone from then on.

Only the standard library is used. VRChat sends to port 9001 and listens on
9000; if another OSC program already has 9001, give it and this one
different ports through an OSC router and pass --listen.
"""

import argparse
import socket
import struct
import sys
import time

AWAY = "/avatar/parameters/WN/Away"
AFK = "/avatar/parameters/AFK"
MUTED = "/avatar/parameters/MuteSelf"
VOICE = "/input/Voice"

# Seconds VRChat is given to report that a press of the voice button took.
CONFIRM = 2.0


class MicGuard:
    """The rules, with no network in them: events in, presses of the voice button out."""

    def __init__(self):
        self.away = False
        self.muted = None  # Unknown until VRChat says.
        self.ours = False  # The microphone is muted, and it was this program that muted it.
        self.waiting = None  # ("mute" | "unmute", deadline) while a press is unconfirmed.

    def set_away(self, away, now):
        """Returns True when the voice button should be pressed."""
        if away == self.away:
            return False
        self.away = away
        if away:
            self.ours = False
            if self.muted is False:
                self.waiting = ("mute", now + CONFIRM)
                return True
            return False
        if self.ours and self.muted is True:
            self.ours = False
            self.waiting = ("unmute", now + CONFIRM)
            return True
        self.ours = False
        return False

    def set_muted(self, muted, now):
        if muted == self.muted:
            return
        self.muted = muted
        self.expire(now)
        if self.waiting and self.waiting[0] == "mute" and muted:
            self.ours = True
        elif not (self.waiting and self.waiting[0] == "unmute" and not muted):
            # Not the change a press of ours was waiting for: someone else touched it.
            self.ours = False
        self.waiting = None

    def expire(self, now):
        if self.waiting and now > self.waiting[1]:
            self.waiting = None


def pad(data):
    return data + b"\0" * (4 - len(data) % 4)


def encode(address, value):
    """An OSC message with one integer argument."""
    return pad(address.encode()) + pad(b",i") + struct.pack(">i", value)


def decode(packet):
    """Yields (address, first argument as a bool) for each message in a packet; bundles are opened."""
    if packet.startswith(b"#bundle\0"):
        offset = 16
        while offset + 4 <= len(packet):
            size = struct.unpack_from(">i", packet, offset)[0]
            yield from decode(packet[offset + 4:offset + 4 + size])
            offset += 4 + size
        return
    try:
        end = packet.index(b"\0")
        address = packet[:end].decode()
        offset = (end // 4 + 1) * 4
        tags_end = packet.index(b"\0", offset)
        tags = packet[offset:tags_end].decode()
        offset = (tags_end // 4 + 1) * 4
    except (ValueError, UnicodeDecodeError):
        return
    if len(tags) < 2:
        return
    kind = tags[1]
    if kind == "T":
        yield address, True
    elif kind == "F":
        yield address, False
    elif kind == "i" and offset + 4 <= len(packet):
        yield address, struct.unpack_from(">i", packet, offset)[0] != 0
    elif kind == "f" and offset + 4 <= len(packet):
        yield address, struct.unpack_from(">f", packet, offset)[0] != 0.0


def run(options):
    listen = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    listen.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listen.bind((options.host, options.listen))
    listen.settimeout(0.25)
    send = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (options.host, options.send)

    def press():
        # The button toggles on its way down and has to come up again before it can be pressed again.
        send.sendto(encode(VOICE, 1), target)
        time.sleep(0.05)
        send.sendto(encode(VOICE, 0), target)

    guard = MicGuard()
    switch = afk = False
    print(f"away-mic: listening on {options.host}:{options.listen}, pressing the voice button at port {options.send}")
    while True:
        try:
            packet, _ = listen.recvfrom(65536)
        except socket.timeout:
            guard.expire(time.monotonic())
            continue
        for address, value in decode(packet):
            now = time.monotonic()
            if address == MUTED:
                was = guard.ours
                guard.set_muted(value, now)
                if guard.ours and not was:
                    print("away-mic: muted")
                elif was and not guard.ours:
                    print("away-mic: the microphone was changed by hand; leaving it alone")
                continue
            if address == AWAY:
                switch = value
            elif address == AFK and options.with_afk:
                afk = value
            else:
                continue
            if guard.set_away(switch or afk, now):
                print("away-mic: " + ("away, muting" if guard.away else "back, unmuting"))
                press()


def test():
    def scenario(name, steps, presses):
        guard = MicGuard()
        pressed = 0
        for now, (kind, value) in enumerate(steps):
            if kind == "away":
                pressed += guard.set_away(value, float(now))
            else:
                guard.set_muted(value, float(now))
        assert pressed == presses, f"{name}: pressed {pressed} times, expected {presses}"

    scenario("mutes on leaving and unmutes on return",
             [("muted", False), ("away", True), ("muted", True), ("away", False), ("muted", False)], 2)
    scenario("already muted: never touched",
             [("muted", True), ("away", True), ("away", False)], 0)
    scenario("unmuted by hand while away: left alone",
             [("muted", False), ("away", True), ("muted", True), ("muted", False), ("away", False)], 1)
    scenario("unmuted and muted again by hand: still left alone",
             [("muted", False), ("away", True), ("muted", True), ("muted", False), ("muted", True), ("away", False)], 1)
    scenario("the mute never took (push-to-talk): no unmute",
             [("muted", False), ("away", True), ("away", False)], 1)
    scenario("mute state unknown: nothing pressed",
             [("away", True), ("away", False)], 0)

    # A mute reported after the wait is somebody else's, not ours.
    guard = MicGuard()
    guard.set_muted(False, 0.0)
    assert guard.set_away(True, 0.0)
    guard.set_muted(True, CONFIRM + 1.0)
    assert not guard.ours
    assert not guard.set_away(False, CONFIRM + 2.0)

    # The wire format survives a round trip, alone and in a bundle.
    message = encode(MUTED, 1)
    assert list(decode(message)) == [(MUTED, True)]
    bundle = b"#bundle\0" + b"\0" * 8 + struct.pack(">i", len(message)) + message
    assert list(decode(bundle)) == [(MUTED, True)]
    assert list(decode(pad(AWAY.encode()) + pad(b",F"))) == [(AWAY, False)]
    print("away-mic: all rules hold")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--with-afk", action="store_true", help="also mute while VRChat itself marks you away")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--listen", type=int, default=9001, help="port VRChat sends to")
    parser.add_argument("--send", type=int, default=9000, help="port VRChat listens on")
    parser.add_argument("--test", action="store_true", help="check the rules and exit")
    options = parser.parse_args()
    if options.test:
        test()
        return 0
    try:
        run(options)
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
