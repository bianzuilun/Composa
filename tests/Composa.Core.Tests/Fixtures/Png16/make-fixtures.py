"""Generate synthetic PNG fixtures with every row predictor and Adam7 (stdlib only)."""
from pathlib import Path
import struct
import zlib

HERE = Path(__file__).resolve().parent
WIDTH, HEIGHT = 13, 11

def chunk(name, data):
    return struct.pack('>I', len(data)) + name + data + struct.pack('>I', zlib.crc32(name + data))

def paeth(a, b, c):
    p = a + b - c
    distances = [abs(p-a), abs(p-b), abs(p-c)]
    return [a, b, c][distances.index(min(distances))]

def sample(x, y):
    return 20000+x*301+y*19, 40000+x*31+y*173, 10000+x*113+y*7

def write(name, color, interlace, transparent=False):
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    passes = [(0,0,1,1)] if not interlace else [(0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2)]
    output = bytearray()
    for sx, sy, dx, dy in passes:
        previous = None
        for row_index, y in enumerate(range(sy, HEIGHT, dy)):
            raw = bytearray()
            for x in range(sx, WIDTH, dx):
                r, g, b = sample(x, y)
                a = 32768 if (x+y)%3 else 65535
                values = {0: (r,), 2: (r,g,b), 4: (r,a), 6: (r,g,b,a)}[color]
                raw.extend(struct.pack('>'+'H'*channels, *values))
            if not raw:
                continue
            if previous is None:
                previous = bytes(len(raw))
            predictor = row_index%5
            encoded = bytearray(len(raw))
            for i, value in enumerate(raw):
                a = raw[i-channels*2] if i>=channels*2 else 0
                b = previous[i]
                c = previous[i-channels*2] if i>=channels*2 else 0
                encoded[i] = (value-[0,a,b,(a+b)//2,paeth(a,b,c)][predictor])%256
            output.extend(bytes([predictor])+encoded)
            previous = raw
    header = struct.pack('>IIBBBBB', WIDTH, HEIGHT, 16, color, 0, 0, interlace)
    data = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', header)
    if transparent:
        data += chunk(b'tRNS', struct.pack('>3H', *sample(0,0)))
    data += chunk(b'IDAT', zlib.compress(output))+chunk(b'IEND', b'')
    (HERE/name).write_bytes(data)

write('gray.png', 0, 0)
write('rgb-adam7.png', 2, 1)
write('gray-alpha-adam7.png', 4, 1)
write('rgba.png', 6, 0)
write('rgb-transparent.png', 2, 0, transparent=True)
