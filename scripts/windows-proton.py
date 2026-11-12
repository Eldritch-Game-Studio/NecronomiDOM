#!/usr/bin/env python3
"""Build genuine Windows NativeAOT; qualify Windows stock Godot under Proton.

The JSON provider is provisioned and qualified separately. This helper never
installs tools, modifies a provider, or represents compatibility as native Windows.
All Windows processes live in one disposable, resource-bounded PID namespace.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import resource
import select
import shlex
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ROOT_KEYS = ("proton_root", "dotnet_root", "msvc_root", "windows_sdk_root")
SHA256 = re.compile(r"[0-9a-fA-F]{64}")
SIGNING_ROOT_SHA256 = frozenset({
    "3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c",
    "2399561127a57125de8cefea610ddf2fa078b5c8067f4e828290bfb860e84b3c",
})
SIGNING_BUNDLES = ("codesignctl.pem", "timestampctl.pem")
PEM_CERTIFICATE = re.compile(
    r"-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----", re.DOTALL)


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def absolute_directory(value):
    path = Path(value)
    if not path.is_absolute() or not path.is_dir():
        raise ValueError(f"Expected an absolute existing directory: {value}")
    return path.resolve()


def contained(path, root):
    return path == root or root in path.parents


def windows_file(root, relative):
    """Resolve Windows spellings without accepting ambiguous Linux siblings."""
    path = root
    for part in Path(relative).parts:
        matches = [entry for entry in path.iterdir() if entry.name.casefold() == part.casefold()]
        if len(matches) != 1:
            raise ValueError(f"Missing or ambiguous Windows tool path: {root / relative}")
        path = matches[0]
    path = path.resolve()
    if not path.is_file() or not contained(path, root):
        raise ValueError(f"Tool must be a real provider file: {path}")
    return path


def checked_hash(path, expected):
    if not isinstance(expected, str) or not SHA256.fullmatch(expected) or digest(path) != expected.lower():
        raise ValueError(f"SHA-256 integrity failure: {path}")


def load_provider(path):
    config = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(config, dict) or type(config.get("schema")) is not int or config["schema"] != 1:
        raise ValueError("Expected Windows toolchain provider schema 1")
    roots = {key: absolute_directory(config[key]) for key in ROOT_KEYS}
    work = absolute_directory(config["work_root"])
    for root in roots.values():
        if contained(work, root) or contained(root, work):
            raise ValueError("Writable work_root must not overlap any read-only provider root")
    version = config["windows_sdk_version"]
    if not isinstance(version, str) or not re.fullmatch(r"10\.0\.\d+\.\d+", version):
        raise ValueError("Invalid Windows SDK version")
    dotnet_version = config["dotnet_sdk_version"]
    if not isinstance(dotnet_version, str) or not re.fullmatch(r"10\.0\.\d+", dotnet_version):
        raise ValueError("Invalid .NET SDK version")
    required = {
        "wine": windows_file(roots["proton_root"], "files/bin/wine"),
        "wineserver": windows_file(roots["proton_root"], "files/bin/wineserver"),
        "proton_version": windows_file(roots["proton_root"], "version"),
        "dotnet": windows_file(roots["dotnet_root"], "dotnet.exe"),
    }
    windows_file(roots["dotnet_root"], f"sdk/{dotnet_version}/dotnet.dll")
    for name in SIGNING_BUNDLES:
        required[name] = windows_file(roots["dotnet_root"], f"sdk/{dotnet_version}/trustedroots/{name}")
    for name in ("cl.exe", "link.exe", "lib.exe"):
        required[name] = windows_file(roots["msvc_root"], f"bin/Hostx64/x64/{name}")
    for name in ("oldnames.lib", "libcmt.lib", "libvcruntime.lib"):
        required[name] = windows_file(roots["msvc_root"], f"lib/x64/{name}")
    for kind, name in (("um", "kernel32.lib"), ("ucrt", "ucrt.lib")):
        required[name] = windows_file(roots["windows_sdk_root"], f"Lib/{version}/{kind}/x64/{name}")
    # Headers and Windows SDK executables must also be real; no installer fallback.
    for relative in (f"Include/{version}/um/Windows.h", f"Include/{version}/ucrt/stdio.h",
                     f"Include/{version}/shared/sdkddkver.h", f"bin/{version}/x64/rc.exe"):
        windows_file(roots["windows_sdk_root"], relative)
    windows_file(roots["msvc_root"], "include/vcruntime.h")
    proton_version = required["proton_version"].read_text().strip()
    if not re.fullmatch(r"[0-9]+ proton-11\.[^ ]+-x86_64", proton_version):
        raise ValueError("Provider must use official Proton 11 x86_64")
    if not (roots["proton_root"] / "files/share/default_pfx").is_dir():
        raise ValueError("Provider has no vendor default prefix")
    fingerprints = config["fingerprints"]
    if not isinstance(fingerprints, list) or not fingerprints:
        raise ValueError("Provider fingerprints are required")
    covered = set()
    for item in fingerprints:
        file = Path(item["path"])
        if not file.is_absolute() or not file.is_file():
            raise ValueError("Fingerprint paths must be absolute real files")
        file = file.resolve()
        if not any(contained(file, root) for root in roots.values()):
            raise ValueError("Fingerprint is outside the provider roots")
        if file in covered:
            raise ValueError("Duplicate provider fingerprint")
        checked_hash(file, item["sha256"])
        covered.add(file)
    # wineserver is required; its hash may supplement the schema's minimum set.
    missing = set(required.values()) - {required["wineserver"]} - covered
    if missing:
        raise ValueError(f"Missing required fingerprints: {sorted(map(str, missing))}")
    qualification = config["qualification"]
    if qualification["kind"] != "windows-proton":
        raise ValueError("Provider must carry windows-proton qualification")
    receipt = Path(qualification["receipt"])
    if not receipt.is_absolute() or not receipt.is_file() or contained(receipt.resolve(), work):
        raise ValueError("Qualification receipt must be an absolute file outside work_root")
    checked_hash(receipt, qualification["sha256"])
    proof = json.loads(receipt.read_text(encoding="utf-8"))
    if not isinstance(proof, dict) or proof.get("kind", "windows-proton") != "windows-proton":
        raise ValueError("Invalid Windows-Proton proof receipt")
    return {**config, **{key: str(value) for key, value in roots.items()},
            "work_root": str(work), "tools": {key: str(value) for key, value in required.items()}}


def extract_signing_roots(config, destination):
    """Extract only the approved DER roots, present in both authenticated bundles."""
    fingerprints = {str(Path(item["path"]).resolve()): item["sha256"]
                    for item in config["fingerprints"]}
    approved = {}
    for name in SIGNING_BUNDLES:
        path = Path(config["tools"][name])
        expected = fingerprints.get(str(path.resolve()))
        bundle = path.read_bytes()
        if (not isinstance(expected, str) or not SHA256.fullmatch(expected)
                or hashlib.sha256(bundle).hexdigest() != expected.lower()):
            raise ValueError(f"SHA-256 integrity failure: {path}")
        text = bundle.decode("ascii")
        found = {}
        for certificate in PEM_CERTIFICATE.finditer(text):
            der = base64.b64decode(re.sub(r"\s+", "", certificate.group(1)), validate=True)
            if not der:
                raise ValueError(f"Empty PEM certificate: {path}")
            fingerprint = hashlib.sha256(der).hexdigest()
            if fingerprint in SIGNING_ROOT_SHA256:
                if fingerprint in found:
                    raise ValueError(f"Duplicate approved signing root: {path}: {fingerprint}")
                found[fingerprint] = der
        remainder = PEM_CERTIFICATE.sub("", text)
        if "-----BEGIN" in remainder or "-----END" in remainder:
            raise ValueError(f"Malformed PEM certificate bundle: {path}")
        missing = SIGNING_ROOT_SHA256 - found.keys()
        if missing:
            raise ValueError(f"Missing approved signing roots: {path}: {sorted(missing)}")
        approved.update(found)
    # Never leave a partial set when either bundle fails authentication/extraction.
    destination.mkdir(mode=0o700)
    for fingerprint, der in sorted(approved.items()):
        with (destination / f"{fingerprint}.cer").open("xb") as output:
            output.write(der)


def restore_signing_roots(config, work):
    extract_signing_roots(config, work / "signing-roots")
    commands = (
        [config["tools"]["wine"], r"M:\bin\Hostx64\x64\cl.exe", "/W4", "/WX",
         r"/Fe:W:\signing-roots.exe", r"W:\source\scripts\windows-signing-roots.c",
         "/link", "crypt32.lib"],
        [config["tools"]["wine"], r"W:\signing-roots.exe", r"W:\signing-roots"],
    )
    for command in commands:
        require_private_prefix(work / "prefix", work)
        try:
            wait_command(command, 180)
        finally:
            require_private_prefix(work / "prefix", work)


def validate_storage(work):
    # No payloads on tmpfs or on HOME's filesystem (which has its own promises).
    if work.stat().st_dev == Path.home().stat().st_dev:
        raise ValueError("work_root must use a separate disk-backed Storage filesystem from HOME")
    mounts = []
    for line in Path("/proc/self/mountinfo").read_text().splitlines():
        fields = line.split()
        mount = Path(re.sub(r"\\([0-7]{3})", lambda m: chr(int(m[1], 8)), fields[4]))
        if contained(work, mount):
            mounts.append((len(mount.parts), fields[fields.index("-") + 1]))
    if not mounts or max(mounts)[1] in {"tmpfs", "ramfs", "devtmpfs"}:
        raise ValueError("work_root must be disk-backed")
    if shutil.disk_usage(work).free < 12 * 1024**3:
        raise ValueError("Windows job requires at least 12 GiB free on Storage")


def materialize_prefix(proton, destination):
    source = proton / "files/share/default_pfx"
    # Do not follow dosdevices/z: or userdata symlinks. Every copied vendor link
    # must resolve inside the read-only provider; copytree then dereferences it.
    def ignore(directory, names):
        skipped = {"dosdevices"} if Path(directory) == source else set()
        for name in set(names) - skipped:
            path = Path(directory) / name
            if path.is_symlink() and not contained(path.resolve(strict=True), proton):
                raise ValueError(f"Vendor prefix link escapes provider: {path}")
        return skipped
    shutil.copytree(source, destination, symlinks=False, ignore=ignore)
    # The materialized Windows tree contains no route back into shared binaries.
    if any(path.is_symlink() for path in destination.rglob("*")):
        raise ValueError("Prefix was not fully materialized")
    (destination / "dosdevices").mkdir()
    (destination / "dosdevices/c:").symlink_to("../drive_c", target_is_directory=True)


def require_private_prefix(prefix, work):
    """Wine startup must not recreate shared DLL or user-profile links."""
    if not (prefix / "drive_c").is_dir():
        raise ValueError("Owned prefix has no Windows drive")
    host_drive = prefix / "dosdevices/z:"
    if host_drive.exists() or host_drive.is_symlink():
        raise ValueError("Wine unexpectedly exposed the host Z drive")
    for root in (prefix / "drive_c", work / "home"):
        for path in root.rglob("*"):
            if path.is_symlink() and not contained(path.resolve(), work):
                raise ValueError(f"Writable Windows/profile link escapes owned job: {path}")


def provision_fonts(config, work):
    """Materialize the complete font set before Wine builds its font registry.

    Wine scans C:\\windows\\Fonts first. Fonts outside a mapped Windows drive
    otherwise become NT /??/unix paths, which stock Godot cannot open. Keep the
    host's fontconfig aliases and fallback coverage, but enumerate owned copies
    instead of those external paths; never disable system font fallback.
    """
    require_private_prefix(work / "prefix", work)
    proton = Path(config["proton_root"])
    sources = set(Path(line) for line in subprocess.check_output(
        ["fc-list", "--format=%{file}\\n"], text=True, timeout=60).splitlines() if line)
    # Include Wine's builtins and Proton's genuine Windows-named replacement
    # fonts (MS Gothic, SimSun, Arial, etc.) used by DirectWrite fallback.
    vendors = (proton / "files/share/wine/fonts", proton / "files/share/fonts")
    for vendor in vendors:
        if vendor.is_dir():
            sources.update(path for path in vendor.iterdir()
                           if path.suffix.lower() in {".ttf", ".otf", ".ttc", ".otc", ".fon"})
    if not sources:
        raise ValueError("No fonts available for the owned Windows prefix")
    allowed = (Path("/usr/share/fonts"), Path("/usr/local/share/fonts"), proton)
    fonts = work / "prefix/drive_c/windows/Fonts"
    fonts.mkdir(exist_ok=True)
    for source in sorted(sources):
        resolved = source.resolve(strict=True)
        if (not resolved.is_file() or not any(contained(resolved, root) for root in allowed)
                or not os.statvfs(resolved).f_flag & os.ST_RDONLY):
            raise ValueError(f"Font source must be a read-only system/provider file: {source}")
        # Preserve vendor names used by Wine's builtin font links. Host names
        # are content-addressed to retain distinct fonts with the same basename.
        name = source.name if any(contained(source, vendor) for vendor in vendors) else digest(resolved) + source.suffix.lower()
        target = fonts / name
        if target.exists():
            if target.is_symlink() or digest(target) != digest(resolved):
                raise ValueError(f"Owned font filename collision: {target}")
        else:
            shutil.copyfile(resolved, target)
    fontconfig = work / "fonts.conf"
    fontconfig.write_text(
        '<?xml version="1.0"?>\n<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">\n'
        '<fontconfig>\n<include>/etc/fonts/fonts.conf</include>\n<reset-dirs/>\n'
        f'<dir>{escape(str(fonts))}</dir>\n'
        f'<cachedir>{escape(str(work / "home/.cache/fontconfig"))}</cachedir>\n'
        '</fontconfig>\n', encoding="utf-8")
    os.environ["FONTCONFIG_FILE"] = str(fontconfig)


def register_fonts(config, work):
    """Register real copied font faces for DirectWrite, not just Wine's GDI."""
    fonts = work / "prefix/drive_c/windows/Fonts"
    entries = subprocess.check_output(
        ["fc-list", "--format=%{file}\\t%{fullname[0]}\\t%{family[0]}\\n"], text=True, timeout=60)
    records = {}
    for entry in entries.splitlines():
        filename, full_name, family = entry.split("\t", 2)
        # Variable-font base patterns may have only a family name; named
        # instances and collection faces carry their own full names.
        full_name = full_name or family
        path = Path(filename)
        if path.parent != fonts or path.is_symlink() or not path.is_file() or not full_name:
            raise ValueError(f"Font registration requires an owned font face: {entry}")
        records[full_name + " (TrueType)"] = path.name
    if not records:
        raise ValueError("No owned font faces available for Windows registration")
    def quoted(value):
        return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'
    registry = work / "fonts.reg"
    registry.write_text(
        "Windows Registry Editor Version 5.00\n\n"
        "[HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts]\n"
        + "".join(f"{quoted(name)}={quoted(filename)}\n" for name, filename in sorted(records.items())),
        encoding="utf-16")
    wait_command([config["tools"]["wine"], "C:\\windows\\system32\\reg.exe",
                  "import", "W:\\fonts.reg"], 60)
    require_private_prefix(work / "prefix", work)


def drive_roots(config, work):
    roots = {"W": work}
    if config["action"] == "build":
        roots.update({"D": Path(config["dotnet_root"]), "M": Path(config["msvc_root"]),
                      "S": Path(config["windows_sdk_root"])})
        if "feed" in config:
            roots["F"] = Path(config["feed"])
    return roots


def wine_environment(config, work):
    proton = Path(config["proton_root"])
    environment = {
        "WINEPREFIX": str(work / "prefix"), "WINEARCH": "win64",
        "WINEDLLOVERRIDES": "mscoree,mshtml=",
        "LD_LIBRARY_PATH": f"{proton}/files/lib64:{proton}/files/lib",
        # NativeAOT consumers still use the Windows NLS APIs and bounded GC;
        # these runtime settings do not expose or require a .NET installation.
        "DOTNET_SYSTEM_GLOBALIZATION_USENLS": "true", "DOTNET_GCHeapHardLimit": "0x200000000",
        "TEMP": "W:\\tmp", "TMP": "W:\\tmp", "XDG_CACHE_HOME": str(work / "home/.cache"),
    }
    # Qualification is a stock non-.NET consumer: no compiler/SDK search paths
    # or managed loader. Only the producer receives those provider mappings.
    if config["action"] == "build":
        version = config["windows_sdk_version"]
        environment.update({
            # CoreCLR maps IL-only PE modules through Wine's builtin loader.
            "WINEDLLOVERRIDES": "mscoree=b;mshtml=",
            "WINEPATH": f"D:\\;M:\\bin\\Hostx64\\x64;S:\\bin\\{version}\\x64",
            "LIB": f"M:\\lib\\x64;S:\\Lib\\{version}\\um\\x64;S:\\Lib\\{version}\\ucrt\\x64",
            "INCLUDE": f"M:\\include;S:\\Include\\{version}\\ucrt;S:\\Include\\{version}\\shared;S:\\Include\\{version}\\um;S:\\Include\\{version}\\winrt",
            "DOTNET_ROOT": "D:\\", "DOTNET_CLI_HOME": "W:\\home",
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "MSBUILDDISABLENODEREUSE": "1", "UseSharedCompilation": "false",
        })
    return environment


def native_path(path, roots):
    path = Path(path)
    if not path.is_absolute():
        raise ValueError(f"Native path must be absolute: {path}")
    path = path.resolve()
    for drive, root in sorted(roots.items(), key=lambda item: len(item[1].parts), reverse=True):
        if contained(path, root):
            return drive + ":\\" + "\\".join(path.relative_to(root).parts)
    raise ValueError(f"Path is outside the private Windows drive mappings: {path}")


def dotnet_command(config, arguments):
    if not arguments or arguments[0] != "publish":
        raise ValueError("The scoped Windows SDK launcher only accepts publish")
    return [config["tools"]["wine"], "D:\\dotnet.exe", *arguments,
            "/p:IlcUseEnvironmentalTools=true", "/p:UseSharedCompilation=false"]


def read_display_number(read_fd, timeout=30):
    deadline = time.monotonic() + timeout
    response = b""
    while not response.endswith(b"\n"):
        remaining = deadline - time.monotonic()
        if remaining <= 0 or not select.select([read_fd], [], [], remaining)[0]:
            raise RuntimeError("Private Xvfb did not become ready")
        part = os.read(read_fd, 64)
        if not part or len(response) + len(part) > 64:
            raise RuntimeError("Incomplete private Xvfb display handshake")
        response += part
    if not re.fullmatch(rb"[0-9]+\n", response):
        raise RuntimeError("Invalid private Xvfb display")
    return response[:-1].decode("ascii")


def godot_arguments(arguments, roots):
    result = []
    for argument in arguments:
        # res://, switches and Windows-native paths remain untouched.
        result.append(native_path(argument, roots) if argument.startswith("/") else argument)
    return result


def fetch(url, destination):
    if not url.startswith("https://"):
        raise ValueError("Downloads require HTTPS")
    request = urllib.request.Request(url, headers={"User-Agent": "NecronomiDOM-windows-proton"})
    with urllib.request.urlopen(request, timeout=120) as response, destination.open("wb") as output:
        if not response.url.startswith("https://"):
            raise ValueError("Download redirected away from HTTPS")
        shutil.copyfileobj(response, output)


def extract_checked_zip(archive, expected, destination):
    checked_hash(archive, expected)
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            path = Path(entry.filename.replace("\\", "/"))
            if path.is_absolute() or ".." in path.parts or ":" in entry.filename or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Unsafe path in Windows Godot archive")
        package.extractall(destination)


def fetch_godot(version, work):
    if not re.fullmatch(r"\d+\.\d+(?:\.\d+)?", version):
        raise ValueError("Expected a numeric stable Godot version")
    metadata = work / "godot-release.json"
    fetch(f"https://api.github.com/repos/godotengine/godot/releases/tags/{version}-stable", metadata)
    name = f"Godot_v{version}-stable_win64.exe.zip"
    url = f"https://github.com/godotengine/godot/releases/download/{version}-stable/{name}"
    assets = [asset for asset in json.loads(metadata.read_text())["assets"] if asset["name"] == name]
    if len(assets) != 1 or assets[0]["browser_download_url"] != url:
        raise ValueError("Official Windows stock Godot asset missing or ambiguous")
    checksum = assets[0].get("digest", "") or ""
    if not checksum.startswith("sha256:"):
        raise ValueError("Official Godot release metadata has no SHA-256 digest")
    archive = work / name
    fetch(url, archive)
    extract_checked_zip(archive, checksum[7:], work / "godot")
    # Execute the real engine, not its console JobObject/tree-wait wrapper.
    # The owned PID namespace already controls every descendant's lifetime.
    executable = work / "godot" / f"Godot_v{version}-stable_win64.exe"
    require_pe(executable, dll=False)
    return executable


def require_pe(path, dll=True):
    with path.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError(f"Not a Windows PE file: {path}")
        stream.seek(0x3c)
        offset = struct.unpack("<I", stream.read(4))[0]
        stream.seek(offset)
        header = stream.read(26)
    if len(header) != 26 or header[:4] != b"PE\0\0" or struct.unpack_from("<H", header, 4)[0] != 0x8664 or struct.unpack_from("<H", header, 24)[0] != 0x20b:
        raise ValueError(f"Expected Windows x86_64 PE32+: {path}")
    if bool(struct.unpack_from("<H", header, 22)[0] & 0x2000) != dll:
        raise ValueError(f"Unexpected PE executable/library kind: {path}")


def stage_source(destination):
    # Copy current source edits, never a stale bin/obj/package cache. No Git
    # operation or mutation of the author's checkout is needed.
    def ignore(directory, names):
        return set(names) & {".git", ".godot", ".work", ".act", ".github", "dist", "bin", "obj", "__pycache__"}
    shutil.copytree(ROOT, destination, ignore=ignore, symlinks=False)


def wrapper(path, mode, config):
    path.write_text("#!/bin/sh\nexec " + " ".join(shlex.quote(str(part)) for part in
                    (sys.executable, Path(__file__).resolve(), mode, config)) + ' "$@"\n')
    path.chmod(0o700)


def sandbox_command(config, work):
    return ["bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-ipc",
            "--unshare-uts", "--ro-bind", "/", "/", "--proc", "/proc", "--dev", "/dev",
            "--bind", str(work), str(work), "--bind", str(work / "tmp"), "/tmp",
            "--bind", str(work / "tmp"), "/var/tmp", "--clearenv",
            "--setenv", "PATH", "/usr/bin:/bin", "--setenv", "HOME", str(work / "home"),
            "--setenv", "TMPDIR", str(work / "tmp"), "--setenv", "LANG", "C.UTF-8",
            "--chdir", str(work), sys.executable, str(Path(__file__).resolve()),
            "--session", str(work / "job.json")]


def interrupted(signum, frame):
    raise InterruptedError(f"Windows job interrupted by signal {signum}")


def wait_command(command, timeout=3600, env=None):
    print("+ " + shlex.join(map(str, command)), flush=True)
    process = subprocess.Popen(command, env=env)
    try:
        status = process.wait(timeout=timeout)
        if status:
            raise RuntimeError(f"Command exited with status {status}: {command[0]}")
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


def require_sandbox(config):
    for key in ROOT_KEYS:
        if not os.statvfs(config[key]).f_flag & os.ST_RDONLY:
            raise ValueError(f"Windows execution requires a read-only sandbox provider: {key}")


def session(config_path):
    config = json.loads(config_path.read_text())
    work = config_path.parent
    # No internal mode is an unsandboxed Wine fallback.
    require_sandbox(config)
    # Windows NativeAOT reserves a 256-GiB sparse address range. This is not
    # physical memory; the inherited admitted cgroup remains the RSS ceiling.
    resource.setrlimit(resource.RLIMIT_AS, (1024 * 1024**3, 1024 * 1024**3))
    resource.setrlimit(resource.RLIMIT_CPU, (3600, 3600))
    resource.setrlimit(resource.RLIMIT_NPROC, (1024, 1024))
    resource.setrlimit(resource.RLIMIT_NOFILE, (8192, 8192))
    resource.setrlimit(resource.RLIMIT_FSIZE, (8 * 1024**3, 8 * 1024**3))
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    roots = drive_roots(config, work)
    for drive, root in roots.items():
        (work / "prefix/dosdevices" / (drive.lower() + ":")).symlink_to(root, target_is_directory=True)
    os.environ.update(wine_environment(config, work))
    # Must precede every Wine process, including wineboot and wineserver:
    # the first GDI initialization populates the prefix's font registry/cache.
    provision_fonts(config, work)
    cancelled = threading.Event()
    def watch_deadline():
        deadline = time.monotonic() + 3600
        while not cancelled.wait(1):
            if (work / "cancel").exists() or time.monotonic() >= deadline:
                os.kill(os.getpid(), signal.SIGTERM)
                return
    threading.Thread(target=watch_deadline, daemon=True).start()
    xvfb = None
    try:
        read_fd, write_fd = os.pipe()
        try:
            xvfb = subprocess.Popen(["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "1280x720x24", "-nolisten", "tcp", "-ac"], pass_fds=(write_fd,))
            os.close(write_fd)
            write_fd = None
            # Xvfb writes the number and trailing newline separately. Keep the
            # reader alive through both writes or its readiness write can fail.
            os.environ["DISPLAY"] = ":" + read_display_number(read_fd)
        finally:
            os.close(read_fd)
            if write_fd is not None:
                os.close(write_fd)
        # Initialize before --version: do not filter normal Wine startup output
        # out of the strict official Godot version oracle.
        wait_command([config["tools"]["wineserver"], "-p"], 30)
        # Bootstrap must not offer Mono/Gecko installation. CoreCLR itself still
        # needs the builtin mscoree PE loader after initialization completes.
        bootstrap_env = os.environ.copy()
        bootstrap_env["WINEDLLOVERRIDES"] = "mscoree,mshtml="
        require_private_prefix(work / "prefix", work)
        wait_command([config["tools"]["wine"], "C:\\windows\\system32\\wineboot.exe", "-u"],
                     180, env=bootstrap_env)
        require_private_prefix(work / "prefix", work)
        register_fonts(config, work)
        wrappers = work / "launchers"
        wrappers.mkdir()
        wrapper(wrappers / "python", "--python", config_path)
        os.environ["PATH"] = str(wrappers) + ":" + os.environ["PATH"]
        if config["action"] == "build":
            wrapper(wrappers / "dotnet", "--dotnet", config_path)
            wrapper(wrappers / "native-path", "--map", config_path)
            restore_signing_roots(config, work)
            os.environ["NECRONOMIDOM_NATIVE_PATH_MAPPER"] = str(wrappers / "native-path")
            os.environ["NECRONOMIDOM_WORK"] = str(work / "build")
            os.environ["NECRONOMIDOM_SDK_FEED"] = config["feed"]
            wait_command(["bash", str(work / "source/native/build.sh"), "win-x64"])
        else:
            config["godot"] = str(fetch_godot(config["godot_version"], work))
            config_path.write_text(json.dumps(config))
            wrapper(wrappers / "godot", "--godot", config_path)
            spec = importlib.util.spec_from_file_location("stock_package", ROOT / "scripts/qualify-stock-package.py")
            stock = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(stock)
            stock.qualify(wrappers / "godot", work / "package.zip", config["godot_version"],
                          required_features=("windows", "x86_64"), version_stderr=work / "version.stderr.log")
    finally:
        cancelled.set()
        # SIGTERM must not interrupt graceful prefix cleanup. The outer deadline
        # still bounds this finally, and PID namespace teardown kills stragglers.
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        try:
            server = config["tools"]["wineserver"]
            status = subprocess.run([server, "-k"], timeout=15).returncode
            wait_command([server, "-w"], 15)
            if status not in (0, 1):
                raise RuntimeError(f"Owned wineserver cleanup failed: {status}")
            require_private_prefix(work / "prefix", work)
        finally:
            if xvfb is not None:
                xvfb.terminate()
                try:
                    xvfb.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    xvfb.kill()
                    xvfb.wait()


def internal(mode, path, arguments):
    config = json.loads(path.read_text())
    require_sandbox(config)
    if os.environ.get("WINEPREFIX") != str(path.parent / "prefix"):
        raise ValueError("Internal launcher requires its owned sandbox Wine prefix")
    roots = drive_roots(config, path.parent)
    if mode == "--map":
        if len(arguments) != 1:
            raise ValueError("Path mapper requires exactly one path")
        print(native_path(arguments[0], roots))
    elif mode == "--dotnet":
        os.execv(config["tools"]["wine"], dotnet_command(config, arguments))
    elif mode == "--godot":
        command = [config["tools"]["wine"], native_path(config["godot"], roots), *godot_arguments(arguments, roots)]
        os.execv(command[0], command)
    elif mode == "--python":
        os.execv(sys.executable, [sys.executable, *arguments])
    else:
        raise ValueError("Unknown internal launcher")


def main():
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    if len(sys.argv) >= 3 and sys.argv[1] == "--session":
        session(Path(sys.argv[2]))
        return 0
    if len(sys.argv) >= 3 and sys.argv[1] in {"--map", "--dotnet", "--godot", "--python"}:
        internal(sys.argv[1], Path(sys.argv[2]), sys.argv[3:])
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    build = commands.add_parser("build")
    build.add_argument("--toolchain", required=True, type=Path)
    build.add_argument("--sdk-feed", required=True, type=Path)
    build.add_argument("--output", required=True, type=Path)
    qualify = commands.add_parser("qualify")
    qualify.add_argument("--toolchain", required=True, type=Path)
    qualify.add_argument("--package", required=True, type=Path)
    qualify.add_argument("--godot-version", default="4.7.2")
    args = parser.parse_args()
    if sys.platform != "linux" or os.uname().machine != "x86_64":
        raise ValueError("Windows-Proton delivery requires a Linux x86_64 host")
    for tool in ("bwrap", "Xvfb", "bash", "fc-list"):
        if shutil.which(tool) is None:
            raise ValueError(f"Required sandbox tool is unavailable: {tool}")
    config = load_provider(args.toolchain)
    config["action"] = args.action
    storage = Path(config["work_root"])
    validate_storage(storage)
    if args.action == "build":
        config["feed"] = str(absolute_directory(args.sdk_feed))
        output = args.output.absolute()
        if output.is_symlink() or any(contained(output.resolve(), Path(config[key])) for key in ROOT_KEYS):
            raise ValueError("Output must not modify a provider or follow a symlink")
        # A failed attempt must never leave a prior DLL available for publication.
        output.unlink(missing_ok=True)
    else:
        if not args.package.is_file():
            raise ValueError("Qualification requires an existing final package")
        config["godot_version"] = args.godot_version
    with tempfile.TemporaryDirectory(prefix="necronomidom-windows-", dir=storage) as temporary:
        work = Path(temporary)
        for name in ("tmp", "home"):
            (work / name).mkdir()
        materialize_prefix(Path(config["proton_root"]), work / "prefix")
        if args.action == "build":
            stage_source(work / "source")
        else:
            shutil.copyfile(args.package, work / "package.zip")
        (work / "job.json").write_text(json.dumps(config))
        process = subprocess.Popen(sandbox_command(config, work), start_new_session=True)
        try:
            status = process.wait(timeout=3900)
            if status:
                raise RuntimeError(f"Isolated Windows {args.action} failed: exit {status}")
        finally:
            if process.poll() is None:
                signal.signal(signal.SIGTERM, signal.SIG_IGN)
                signal.signal(signal.SIGINT, signal.SIG_IGN)
                # Let the namespace's Python supervisor stop its own wineserver
                # before tearing down the namespace; bwrap is not a signal proxy.
                (work / "cancel").touch()
                try:
                    process.wait(timeout=60)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
            version_log = work / "version.stderr.log"
            if version_log.is_file():
                logs = storage / "qualification-logs"
                logs.mkdir(exist_ok=True)
                retained = logs / (work.name + ".version.stderr.log")
                version_log.replace(retained)
                print(f"Retained complete Windows version stderr: {retained} sha256={digest(retained)}")
        if args.action == "build":
            dll = work / "source/addons/necronomidom/bin/win-x64/necronomidom.dll"
            require_pe(dll)
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(dll, output)
            print(f"Windows NativeAOT build PASS: {output} sha256={digest(output)}")
        else:
            print("Windows-Proton compatibility qualification PASS (not native-Windows qualification)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, InterruptedError,
            subprocess.TimeoutExpired, zipfile.BadZipFile, struct.error) as error:
        print(f"windows-proton: {error}", file=sys.stderr)
        sys.exit(1)
