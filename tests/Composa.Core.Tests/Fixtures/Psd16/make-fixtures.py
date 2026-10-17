"""Generate small independent 16-bit PSD fixtures from Adobe's published file layout."""
from pathlib import Path
import struct,zlib
out=Path(__file__).parent
W,H=13,11
u16=lambda v:struct.pack('>H',v)
u32=lambda v:struct.pack('>I',v)
planes=[[20000+x*301+y*19 for y in range(H) for x in range(W)], [40000+x*31+y*173 for y in range(H) for x in range(W)], [10000+x*113+y*7 for y in range(H) for x in range(W)]]
raw=lambda values:b''.join(u16(v) for v in values)
header=b'8BPS'+u16(1)+bytes(6)+u16(3)+u32(H)+u32(W)+u16(16)+u16(3)+u32(0)+u32(0)
composite=u16(0)+b''.join(raw(p) for p in planes)
(out/'flat-raw.psd').write_bytes(header+u32(0)+composite)
predicted=[]
for plane in planes:
 for y in range(H):
  previous=0
  for v in plane[y*W:(y+1)*W]:
   predicted.append((v-previous)&65535);previous=v
(out/'flat-zip-prediction.psd').write_bytes(header+u32(0)+u16(3)+zlib.compress(raw(predicted)))
# The layer's channel records are RGB; alpha is omitted (opaque).
channels=b''.join(struct.pack('>hI',c,2+W*H*2) for c in range(3))
name=b'Fine';pascal=bytes([len(name)])+name;pascal+=bytes((-len(pascal))%4)
extra=u32(0)+u32(0)+pascal
record=struct.pack('>iiiiH',0,0,H,W,3)+channels+b'8BIMnorm'+bytes([255,0,0,0])+u32(len(extra))+extra
layer_info=u16(1)+record # The actual per-layer channel payloads follow records.
layer_info+=b''.join(u16(0)+raw(p) for p in planes)
layer_info+=bytes(len(layer_info)%2)
for full in (False,True):
 payload=(u32(len(layer_info)) if full else b'')+layer_info
 tagged=b'8BIMLr16'+u32(len(payload))+payload
 section=u32(0)+u32(0)+tagged
 (out/('layer-lr16-with-length.psd' if full else 'layer-lr16.psd')).write_bytes(header+u32(len(section))+section+composite)
