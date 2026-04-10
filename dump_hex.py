import sys

def dump_packets(filename, count=5):
    with open(filename, "rb") as f:
        data = f.read()

    idx = 0
    found = 0
    while idx < len(data) - 91 and found < count:
        # Looking for the Razer pattern
        if data[idx+3:idx+6] == b'\x00\x00\x00' and data[idx] <= 0x03:
            packet = data[idx:idx+91] # Including report ID or leading byte
            print(f"PACKET {found} at OFFSET {idx:08X}:")
            print(f"  {packet.hex(' ')}")
            found += 1
        idx += 1

if __name__ == "__main__":
    if len(sys.argv) > 1:
        dump_packets(sys.argv[1])
