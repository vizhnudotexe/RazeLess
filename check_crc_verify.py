
# From target_packets.txt, first packet:
# 00 00 1a 00 00 00 0a 04 06 01 01 01 00 00 c8 00 c8 00 00 00 ... (zeros to 88) 09 00
# CRC byte is at index 88, value is 0x09

d = [0x00, 0x00, 0x1a, 0x00, 0x00, 0x00, 0x0a, 0x04, 0x06,
     0x01, 0x01, 0x01, 0x00, 0x00, 0xc8, 0x00, 0xc8, 0x00, 0x00] + [0x00]*69

print(f"Packet length: {len(d)}")

# Calculate from index 2 (OpenRazer standard):
crc2 = 0
for i in range(2, 88): crc2 ^= d[i]
print(f"CRC from index 2 (OpenRazer standard): 0x{crc2:02x}")

# Calculate from index 3 (our recent change):
crc3 = 0
for i in range(3, 88): crc3 ^= d[i]
print(f"CRC from index 3 (our fix):            0x{crc3:02x}")

print(f"Expected CRC from PCAP:                0x09")
print()

# Now check the Apply packet (2nd packet) - CRC should be 0xd3
# 00 00 1b 00 00 00 50 04 86 01 00 00...
d2 = [0x00, 0x00, 0x1b, 0x00, 0x00, 0x00, 0x50, 0x04, 0x86,
      0x01] + [0x00]*78

crc2_apply = 0
for i in range(2, 88): crc2_apply ^= d2[i]
print(f"Apply CRC from index 2: 0x{crc2_apply:02x}")

crc3_apply = 0
for i in range(3, 88): crc3_apply ^= d2[i]
print(f"Apply CRC from index 3: 0x{crc3_apply:02x}")

print(f"Expected Apply CRC:     0xd3")
