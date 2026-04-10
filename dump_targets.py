import sys

def dump_target_packets(filename):
    with open(filename, "rb") as f:
        data = f.read()

    idx = 0
    results = []
    while idx < len(data) - 91:
        # Looking for Class 04 Cmd 06 or 86
        if data[idx+7] == 0x04 and (data[idx+8] == 0x06 or data[idx+8] == 0x86):
            # Check for Razer anchor: 00 00 at start, 00 00 00 at 3-5
            if data[idx:idx+2] == b'\x00\x00' and data[idx+3:idx+6] == b'\x00\x00\x00':
                packet = data[idx:idx+91]
                results.append(f"OFFSET: {idx:08X} | DATA: {packet.hex(' ')}")
        idx += 1
    
    with open("target_packets.txt", "w") as f:
        f.write("\n".join(results))

if __name__ == "__main__":
    if len(sys.argv) > 1:
        dump_target_packets(sys.argv[1])
