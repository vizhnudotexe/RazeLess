import sys

def calculate_crc(packet):
    # packet length 90
    crc = 0
    # C# code says i from 2 to 87
    for i in range(2, 88):
        crc ^= packet[i]
    return crc

def parse_pcap(filename):
    with open(filename, "rb") as f:
        data = f.read()

    # Searching for pattern: 00 [Status] [TransID] 00 00 00 [Size] [Class] [Cmd]
    
    idx = 0
    while idx < len(data) - 90:
        # Many PCAP captures might have extra headers.
        # But Razer feature reports usually have 0x00 0x00 at the start (report ID 0, status 0).
        if data[idx] == 0x00 and data[idx+1] == 0x00 and data[idx+3] == 0x00:
            packet = data[idx:idx+90]
            size = packet[6]
            cls = packet[7]
            cmd = packet[8]
            
            # Check for Class 04
            if cls == 0x04 or cls == 0x03 or cls == 0x01:
                calc_crc = calculate_crc(packet)
                real_crc = packet[88]
                print(f"OFFSET {idx:08X} | CMD {cls:02X}:{cmd:02X} | TRANS {packet[2]:02X} | SIZE {size:02X} | PCAP_CRC: {real_crc:02X} | CALC_CRC: {calc_crc:02X} | {'MATCH' if calc_crc == real_crc else 'FAIL'}")
                if calc_crc != real_crc:
                    # Let's try different CRC starting points
                    for start in range(9):
                        test_crc = 0
                        for i in range(start, 88):
                            test_crc ^= packet[i]
                        if test_crc == real_crc:
                            print(f"    >>> CRC MATCHES if starting from OFFSET {start}")
        idx += 1

if __name__ == "__main__":
    if len(sys.argv) > 1:
        parse_pcap(sys.argv[1])
