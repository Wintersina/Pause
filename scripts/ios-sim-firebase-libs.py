#!/usr/bin/env python3
"""Make the Firebase Unity SDK static libraries linkable into an arm64 iOS Simulator build.

The Firebase Unity SDK ships each libFirebaseCpp*.a as a fat file of x86_64 (simulator) and
arm64 (DEVICE only). Modern simulator runtimes are arm64 only, so the link fails with
"building for 'iOS-simulator', but linking in object file (...) built for 'iOS'".
The device arm64 code runs unchanged on an Apple Silicon simulator, so this keeps the arm64
slice and rewrites each object's LC_BUILD_VERSION platform from iOS (2) to iOS Simulator (7)
in place (no size change). Only touches the files you pass (the Xcode export copies, never
the Assets/ originals). Idempotent.

  python3 -I scripts/ios-sim-firebase-libs.py Pause/Builds/iOS-sim/Libraries/Plugins/iOS/Firebase/*.a
"""
import struct, subprocess, sys, tempfile, os, shutil

MH_MAGIC_64, LC_BUILD_VERSION = 0xFEEDFACF, 0x32
PLATFORM_IOS, PLATFORM_IOSSIMULATOR = 2, 7

def patch_object(buf, off):
    magic, = struct.unpack_from("<I", buf, off)
    if magic != MH_MAGIC_64:
        return 0
    ncmds, = struct.unpack_from("<I", buf, off + 16)
    p, n = off + 32, 0
    for _ in range(ncmds):
        cmd, size = struct.unpack_from("<II", buf, p)
        if cmd == LC_BUILD_VERSION:
            plat, = struct.unpack_from("<I", buf, p + 8)
            if plat == PLATFORM_IOS:
                struct.pack_into("<I", buf, p + 8, PLATFORM_IOSSIMULATOR)
                n += 1
        p += size
    return n

def patch_archive(path):
    archs = subprocess.check_output(["lipo", "-archs", path]).decode().split()
    tmp = tempfile.mkdtemp()
    try:
        thin = path
        if len(archs) > 1:
            if "arm64" not in archs:
                raise SystemExit(path + ": no arm64 slice")
            thin = os.path.join(tmp, "arm64.a")
            subprocess.check_call(["lipo", "-thin", "arm64", path, "-output", thin])
        elif archs != ["arm64"]:
            raise SystemExit(path + ": unexpected archs " + " ".join(archs))
        buf = bytearray(open(thin, "rb").read())
        if buf[:8] != b"!<arch>\n":
            raise SystemExit(path + ": not an ar archive")
        p, patched = 8, 0
        while p + 60 <= len(buf):
            name, size = bytes(buf[p:p + 16]).decode("ascii"), int(bytes(buf[p + 48:p + 58]))
            data = p + 60
            if name.startswith("#1/"):
                data += int(name[3:].strip())   # BSD long name stored in front of the data
            patched += patch_object(buf, data)
            p += 60 + size + (size & 1)
        open(path, "wb").write(buf)
        print("%s: %d objects retagged for the simulator" % (os.path.basename(path), patched))
    finally:
        shutil.rmtree(tmp)

for f in sys.argv[1:]:
    patch_archive(f)
