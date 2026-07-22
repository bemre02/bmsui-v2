"""HV BMS USB CDC simulator.

Runs on one half of a virtual serial port pair (VSPE / com0com, e.g. COM10 <-> COM11):
the UI connects to COM10 while this script holds COM11.

Firmware behaviour is mirrored: commands are dispatched on the LENGTH of the incoming
byte burst (main.cpp USB_Task -> switch (usblen)).
"""
import argparse
import math
import random
import struct
import sys
import time

import serial

CMD_VOLTAGES = 0x29
CMD_TEMPS = 0x2A
CMD_BALANCE = 0x2B
PING = bytes([0x17, 0x71])

CELL_COUNT = 96
SEGMENTS = 6
CELLS_PER_SEGMENT = 16


def crc8(data: bytes) -> int:
    """CRC-8/SMBUS — identical to the firmware calculateCRC8()."""
    crc = 0x00
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = ((crc << 1) ^ 0x07) & 0xFF if crc & 0x80 else (crc << 1) & 0xFF
    return crc


def to_u16(signed_value: int) -> int:
    """Converts a signed value to its uint16 bit pattern (what the firmware union does)."""
    return struct.unpack("<H", struct.pack("<h", int(signed_value)))[0]


class PackState:
    """Simple pack model that produces realistic drift."""

    def __init__(self, fault_bits=0):
        self.voltages = [random.uniform(3.85, 3.95) for _ in range(CELL_COUNT)]
        self.temps = [random.uniform(24.0, 30.0) for _ in range(CELL_COUNT)]
        # a few hot cells
        for i in (7, 42, 83):
            self.temps[i] += random.uniform(12.0, 18.0)
        self.current = 0.0
        self.t0 = time.time()
        self.fault_bits = fault_bits
        self.registers = [0] * 50
        self.registers[30] = 20      # ALLOWED_DISBALANCE, mV
        self.registers[32] = 95      # PRECHARGE_PERCENTAGE
        self.registers[33] = 5000    # PRECHARGE_TIMEOUT

    def tick(self):
        t = time.time() - self.t0
        # Current: slow oscillation (-120 A .. +80 A), charge/discharge
        self.current = 80.0 * math.sin(t / 7.0) - 40.0 * math.sin(t / 3.0)
        for i in range(CELL_COUNT):
            # internal resistance drop + noise
            sag = self.current * 0.00012
            self.voltages[i] += random.uniform(-0.0015, 0.0015) - sag * 0.01
            self.voltages[i] = min(4.19, max(3.30, self.voltages[i]))
            self.temps[i] += random.uniform(-0.05, 0.05) + abs(self.current) * 0.00008
            self.temps[i] = min(78.0, max(15.0, self.temps[i]))

        avg_v = sum(self.voltages) / CELL_COUNT
        avg_t = sum(self.temps) / CELL_COUNT
        pack_v = sum(self.voltages)

        outputs = 0
        if t > 2.0:
            outputs |= 1 << 1          # PRE
        if t > 5.0:
            outputs |= 1 << 0          # AIR
        if self.fault_bits:
            outputs |= 1 << 2          # ERR / SDC

        r = self.registers
        r[0] = self.fault_bits
        r[1] = outputs
        r[7] = int(pack_v * 100) & 0xFFFF
        r[8] = to_u16(self.current * 10)
        r[9] = int(max(self.voltages) * 100)
        r[10] = int(min(self.voltages) * 100)
        r[11] = int(pack_v * 100) & 0xFFFF
        r[12] = to_u16(max(self.temps) * 100)
        r[13] = to_u16(min(self.temps) * 100)
        r[14] = int(avg_v * 100)
        r[15] = to_u16(avg_t * 100)
        r[16] = to_u16(52.0 * 100)
        r[17] = int(min(1.0, max(0.0, (avg_v - 3.2) / (4.15 - 3.2))) * 10000)

    def balance_bitmaps(self):
        """Cells more than ALLOWED_DISBALANCE above the mean are balancing."""
        avg = sum(self.voltages) / CELL_COUNT
        threshold = avg + max(1, self.registers[30]) / 1000.0
        maps = []
        for ic in range(SEGMENTS):
            dcc = 0
            for c in range(CELLS_PER_SEGMENT):
                if self.voltages[ic * CELLS_PER_SEGMENT + c] > threshold:
                    dcc |= 1 << c
            maps.append(dcc)
        return maps


def cell_frame(values, cmd_id, signed):
    fmt = "<h" if signed else "<H"
    payload = b"".join(struct.pack(fmt, int(v * 100)) for v in values)
    frame = bytearray(payload)
    frame.append(cmd_id)
    frame.append(crc8(bytes(frame)))
    return bytes(frame)


def balance_frame(bitmaps):
    frame = bytearray(b"".join(struct.pack("<H", m) for m in bitmaps))
    frame.append(CMD_BALANCE)
    frame.append(crc8(bytes(frame)))
    return bytes(frame)


def register_frame(idx, value):
    frame = bytearray(struct.pack("<H", value & 0xFFFF))
    frame.append(idx)
    frame.append(crc8(bytes(frame)))
    return bytes(frame)


def send(ser, data, chunked, latency_ms):
    if latency_ms:
        time.sleep(latency_ms / 1000.0)
    if chunked and len(data) > 8:
        # mimic the chunked delivery of CDC
        for off in range(0, len(data), 64):
            ser.write(data[off:off + 64])
            ser.flush()
            time.sleep(0.001)
    else:
        ser.write(data)
        ser.flush()


def main():
    ap = argparse.ArgumentParser(description="HV BMS USB simulator")
    ap.add_argument("--port", default="COM11")
    ap.add_argument("--baud", type=int, default=115200)
    ap.add_argument("--fault", type=int, action="append", default=[],
                    help="FAULTS bit number (may be given more than once)")
    ap.add_argument("--chunked", action="store_true",
                    help="send responses split into 64-byte chunks")
    ap.add_argument("--latency", type=int, default=0, help="response delay (ms)")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    fault_mask = 0
    for b in args.fault:
        fault_mask |= 1 << b

    print("=" * 52)
    print("        HIGH VOLTAGE BMS USB SIMULATOR")
    print("=" * 52)
    print(f"Port: {args.port} | Baud: {args.baud} | Fault mask: 0x{fault_mask:04X}")

    try:
        ser = serial.Serial(args.port, args.baud, timeout=0.05)
    except Exception as e:
        print(f"ERROR: could not open {args.port}: {e}")
        print("Make sure a virtual port pair exists (VSPE / com0com).")
        return 1

    state = PackState(fault_mask)
    print("Waiting for the UI to connect... (Ctrl+C to stop)")

    try:
        while True:
            first = ser.read(1)
            if not first:
                state.tick()
                continue
            # The firmware sees one USB packet as a whole; collect the burst
            time.sleep(0.002)
            extra = ser.read(ser.in_waiting or 0)
            packet = first + extra
            state.tick()

            if len(packet) == 1:
                cmd = packet[0]
                if cmd == CMD_VOLTAGES:
                    send(ser, cell_frame(state.voltages, CMD_VOLTAGES, False),
                         args.chunked, args.latency)
                elif cmd == CMD_TEMPS:
                    send(ser, cell_frame(state.temps, CMD_TEMPS, True),
                         args.chunked, args.latency)
                elif cmd == CMD_BALANCE:
                    send(ser, balance_frame(state.balance_bitmaps()),
                         args.chunked, args.latency)
                elif cmd < 50:
                    send(ser, register_frame(cmd, state.registers[cmd]),
                         args.chunked, args.latency)
                else:
                    if args.verbose:
                        print(f"idx {cmd} >= 50 — dropped silently, like the firmware")
            elif len(packet) == 2:
                if packet == PING:
                    send(ser, PING, False, args.latency)
                    print("Ping received -> echo sent")
            elif len(packet) == 3:
                idx, lsb, msb = packet[0], packet[1], packet[2]
                if idx < 50:
                    state.registers[idx] = (msb << 8) | lsb
                    send(ser, register_frame(idx, state.registers[idx]),
                         args.chunked, args.latency)
                    print(f"WRITE MAINBUFFER[{idx}] = {state.registers[idx]}")

            if args.verbose:
                print(f"packet={packet.hex(' ')} len={len(packet)}")
    except KeyboardInterrupt:
        print("\nSimulator stopped.")
    finally:
        ser.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
