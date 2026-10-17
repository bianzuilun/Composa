#!/usr/bin/env python3
"""Repack the MIT-licensed Compositor v1.5.3 Camera Raw data for the BCL gzip reader.

Usage: python scripts/import-compositor-tables.py /path/to/Compositor-checkout
The source hashes pin the exact data; generated gzip streams have no timestamp.
"""
import gzip
import hashlib
import lzma
from pathlib import Path
import sys

source = Path(sys.argv[1]) / "Compositor" / "Resources"
destination = Path(__file__).resolve().parents[1] / "src/Composa.Core/CameraRawData"
destination.mkdir(exist_ok=True)
for name, expected in [
    ("CameraRawTables.bin", "e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190"),
    ("CameraRawShadows.bin", "76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705"),
]:
    data = (source / name).read_bytes()
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError(f"Unexpected upstream data: {name}")
    if name == "CameraRawTables.bin":
        data = gzip.compress(data[:10] + lzma.decompress(data[10:]), compresslevel=9, mtime=0)
        name = "CameraRawTables.gz"
    (destination / name).write_bytes(data)
    print(f"{name}: {len(data):,} bytes")
