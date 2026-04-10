import sys
import os

def calculate_crc(packet):
    crc = 0
    for i in range(2, 88):
        crc ^= packet[i]
    return crc

def parse_pcap(filename, out_filename):
    with open(filename, "rb") as f:
        data = f.read()

    idx = 0
    results = []
    while idx < len(data) - 90:
        if data[idx:idx+2] == b'\x00\x00' and data[idx+3:idx+6] == b'\x00\x00\x00':
            packet = data[idx:idx+90]
            size = packet[6]
            cls = packet[7]
            cmd = packet[8]
            
            trans = packet[2]
            payload = packet[9:9+size]
            real_crc = packet[88]
            calc_crc = calculate_crc(packet)
            
            results.append(f"OFFSET: {idx:08X} | TRANS: {trans:02x} | SIZE: {size:02x} | CMD: {cls:02x}:{cmd:02x} | CRC: {real_crc:02x} (CALC: {calc_crc:02x}) | {'MATCH' if real_crc == calc_crc else 'FAIL'} | PAYLOAD: {payload.hex(' ')}")
        idx += 1
    
    with open(out_filename, "w") as f:
        f.write("\n".join(results))

if __name__ == "__main__":
    if len(sys.argv) > 2:
        parse_pcap(sys.argv[1], sys.argv[2])
