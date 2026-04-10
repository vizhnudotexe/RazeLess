import sys

def search_mode(filename):
    with open(filename, "rb") as f:
        data = f.read()

    idx = 0
    while idx < len(data) - 10:
        if data[idx:idx+2] == b'\x00\x00' and data[idx+3:idx+6] == b'\x00\x00\x00':
            cls = data[idx+7]
            cmd = data[idx+8]
            if cls == 0x00 and cmd == 0x04:
                print(f"FOUND Device Mode (00:04) at {idx:08X}: {data[idx:idx+20].hex(' ')}")
        idx += 1

if __name__ == "__main__":
    if len(sys.argv) > 1:
        search_mode(sys.argv[1])
