#!/usr/bin/env python3
"""Static native-binary check (PLAT-04 support). Reads PE headers and import tables of native DLLs in
build outputs and flags what is known to block Windows 7 SP1 (6.1) / 8.1 (6.3):
  * MajorSubsystemVersion above the target OS (the loader refuses the file),
  * dependencies on the Universal CRT / VC++ runtime (latest VC++ redistributable is Windows 10+ only),
  * a list of Windows 8+ / Windows 10+ API imports.
It is a heuristic: it can find blockers, it cannot prove compatibility. Runtime evidence on the real OS decides.
Usage: python tools/check-native-imports.py <dir> [<dir> ...]     (exit 1 if a Windows 7 blocker is found)
"""
import os
import struct
import sys

WIN8_PLUS = {
    "GetSystemTimePreciseAsFileTime", "CreateFile2", "PrefetchVirtualMemory", "GetCurrentThreadStackLimits",
    "WaitOnAddress", "WakeByAddressSingle", "WakeByAddressAll", "SetThreadDescription", "GetThreadDescription",
    "CreateFileMappingFromApp", "MapViewOfFileFromApp", "GetOverlappedResultEx", "SetThreadInformation",
    "GetProcessMitigationPolicy", "SetProcessMitigationPolicy", "VirtualAlloc2", "MapViewOfFile3",
    "DiscardVirtualMemory", "OfferVirtualMemory", "GetSystemCpuSetInformation", "GetDpiForWindow",
    "GetDpiForMonitor", "SetProcessDpiAwarenessContext", "RoInitialize", "WindowsCreateString", "IsWow64Process2",
    "InitializeSynchronizationBarrier", "EnterSynchronizationBarrier", "GetFirmwareType",
}
CRT_PREFIXES = ("vcruntime", "msvcp", "api-ms-win-crt", "ucrtbase", "concrt", "vccorlib")
MANAGED_MARKER = b"BSJB"


def rva_to_off(sections, rva):
    for va, vsize, raw, rsize in sections:
        if va <= rva < va + max(vsize, rsize):
            return rva - va + raw
    return None


def cstr(b, o):
    return b[o:b.index(b"\0", o)].decode("latin1")


def analyze(b):
    pe = struct.unpack_from("<I", b, 0x3C)[0]
    if b[pe:pe + 4] != b"PE\0\0":
        return None
    machine, nsec = struct.unpack_from("<HH", b, pe + 4)
    opt = pe + 24
    magic = struct.unpack_from("<H", b, opt)[0]
    ss_major, ss_minor = struct.unpack_from("<HH", b, opt + 48)
    dd = opt + (96 if magic == 0x10B else 112)
    imp_rva = struct.unpack_from("<I", b, dd + 8)[0]
    clr_rva = struct.unpack_from("<I", b, dd + 14 * 8)[0]
    delay_rva = struct.unpack_from("<I", b, dd + 13 * 8)[0]
    size_opt = struct.unpack_from("<H", b, pe + 20)[0]
    s0 = opt + size_opt
    sections = []
    for i in range(nsec):
        vsize, va, rsize, raw = struct.unpack_from("<IIII", b, s0 + i * 40 + 8)
        sections.append((va, vsize, raw, rsize))
    dlls = {}
    width = 4 if magic == 0x10B else 8

    def walk(rva, delay):
        o = rva_to_off(sections, rva)
        while o is not None:
            if delay:
                f = struct.unpack_from("<8I", b, o)
                if f[1] == 0:
                    break
                name_rva, int_rva = f[1], f[4]
            else:
                f = struct.unpack_from("<5I", b, o)
                if f[3] == 0:
                    break
                name_rva, int_rva = f[3], f[0] or f[4]
            name = cstr(b, rva_to_off(sections, name_rva))
            funcs = []
            t = rva_to_off(sections, int_rva)
            while t is not None:
                v = struct.unpack_from("<I" if width == 4 else "<Q", b, t)[0]
                if v == 0:
                    break
                if not (v >> (31 if width == 4 else 63)) & 1:
                    funcs.append(cstr(b, rva_to_off(sections, v & 0x7FFFFFFF) + 2))
                t += width
            dlls[name + (" (delay)" if delay else "")] = funcs
            o += 32 if delay else 20

    if imp_rva:
        walk(imp_rva, False)
    if delay_rva:
        walk(delay_rva, True)
    arch = {0x14C: "x86", 0x8664: "x64", 0xAA64: "arm64"}.get(machine, hex(machine))
    return arch, (ss_major, ss_minor), dlls, clr_rva != 0


def main(dirs):
    blockers = 0
    seen = set()
    for d in dirs:
        for dp, _, files in os.walk(d):
            for f in files:
                if not f.lower().endswith(".dll"):
                    continue
                p = os.path.join(dp, f)
                b = open(p, "rb").read()
                try:
                    r = analyze(b)
                except Exception:
                    continue
                if r is None:
                    continue
                arch, ss, dlls, managed = r
                if managed or arch == "arm64":
                    continue
                key = (f.lower(), arch, len(b))
                if key in seen:
                    continue
                seen.add(key)
                crt = [k for k in dlls if k.lower().startswith(CRT_PREFIXES)]
                win8 = sorted({fn for fs in dlls.values() for fn in fs if fn in WIN8_PLUS})
                win7_block = ss > (6, 1) or bool(win8)
                win81_block = ss > (6, 3)
                if win7_block:
                    blockers += 1
                print(f"{f}\t{arch}\tsubsystem={ss[0]}.{ss[1]}\timports={sorted(dlls)}\tcrt={'yes' if crt else 'static/none'}\t"
                      f"win8plus_apis={win8}\twindows7={'BLOCKER' if win7_block else ('needs UCRT' if crt else 'no known blocker')}\t"
                      f"windows81={'BLOCKER' if win81_block else ('needs UCRT' if crt else 'no known blocker')}\t{p}")
    print(f"WINDOWS7_BLOCKERS={blockers}")
    return 1 if blockers else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or ["."]))
