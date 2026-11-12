"""Focused safety/command tests; no Wine, toolchain, or sandbox is executed."""
import importlib.util
import json
from pathlib import Path
import ssl
import struct
import tempfile
from types import SimpleNamespace
import xml.etree.ElementTree as ET
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


windows = load("windows_proton", "windows-proton.py")
stock = load("stock_windows_package", "qualify-stock-package.py")


class ProviderTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.config = {"schema": 1, "windows_sdk_version": "10.0.26100.0",
                       "dotnet_sdk_version": "10.0.404"}
        for key in (*windows.ROOT_KEYS, "work_root"):
            directory = self.root / key
            directory.mkdir()
            self.config[key] = str(directory)
        files = {
            "proton_root": ["files/bin/wine", "files/bin/wineserver", "version"],
            "dotnet_root": ["dotnet.exe", "sdk/10.0.404/dotnet.dll",
                            "sdk/10.0.404/trustedroots/codesignctl.pem",
                            "sdk/10.0.404/trustedroots/timestampctl.pem"],
            "msvc_root": ["bin/Hostx64/x64/CL.EXE", "bin/Hostx64/x64/link.exe", "bin/Hostx64/x64/lib.exe",
                          "lib/x64/OLDNAMES.LIB", "lib/x64/libcmt.lib", "lib/x64/libvcruntime.lib", "include/vcruntime.h"],
            "windows_sdk_root": ["Lib/10.0.26100.0/um/x64/kernel32.lib", "Lib/10.0.26100.0/ucrt/x64/ucrt.lib",
                                 "Include/10.0.26100.0/um/Windows.h", "Include/10.0.26100.0/ucrt/stdio.h",
                                 "Include/10.0.26100.0/shared/sdkddkver.h", "bin/10.0.26100.0/x64/rc.exe"],
        }
        self.config["fingerprints"] = []
        for key, names in files.items():
            for name in names:
                path = Path(self.config[key]) / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("fixture")
                if name == "version":
                    path.write_text("1234567890 proton-11.0-1-x86_64\n")
                self.config["fingerprints"].append({"path": str(path), "sha256": windows.digest(path)})
        (Path(self.config["proton_root"]) / "files/share/default_pfx").mkdir(parents=True)
        self.receipt = self.root / "proof.json"
        self.receipt.write_text(json.dumps({"kind": "windows-proton", "observed_probe": 42}))
        self.config["qualification"] = {"kind": "windows-proton", "receipt": str(self.receipt),
                                        "sha256": windows.digest(self.receipt)}
        self.path = self.root / "provider.json"

    def read_provider(self):
        self.path.write_text(json.dumps(self.config))
        return windows.load_provider(self.path)

    def test_accepts_case_insensitive_tools_and_provider_selected_sdk(self):
        actual = self.read_provider()
        self.assertTrue(actual["tools"]["cl.exe"].endswith("CL.EXE"))
        self.assertTrue(actual["tools"]["oldnames.lib"].endswith("OLDNAMES.LIB"))
        self.assertEqual(actual["dotnet_sdk_version"], "10.0.404")
        for name in windows.SIGNING_BUNDLES:
            self.assertTrue(actual["tools"][name].endswith(f"sdk/10.0.404/trustedroots/{name}"))

    def test_requires_explicit_dotnet_sdk_version(self):
        del self.config["dotnet_sdk_version"]
        with self.assertRaises(KeyError):
            self.read_provider()

    def test_rejects_invalid_dotnet_sdk_version(self):
        for version in ("9.0.404", "../10.0.404", "10.0.404/../other", None):
            with self.subTest(version=version):
                self.config["dotnet_sdk_version"] = version
                with self.assertRaisesRegex(ValueError, "Invalid .NET SDK version"):
                    self.read_provider()

    def test_never_substitutes_another_installed_dotnet_sdk(self):
        self.config["dotnet_sdk_version"] = "10.0.405"
        with self.assertRaisesRegex(ValueError, "Missing or ambiguous"):
            self.read_provider()

    def test_requires_selected_sdk_and_both_bundle_files(self):
        for relative in ("dotnet.dll", "trustedroots/codesignctl.pem", "trustedroots/timestampctl.pem"):
            path = Path(self.config["dotnet_root"]) / "sdk/10.0.404" / relative
            original = path.read_bytes()
            path.unlink()
            with self.subTest(relative=relative), self.assertRaisesRegex(ValueError, "Missing or ambiguous"):
                self.read_provider()
            path.write_bytes(original)

    def test_rejects_tampered_provider_bundles(self):
        for name in windows.SIGNING_BUNDLES:
            path = Path(self.config["dotnet_root"]) / "sdk/10.0.404/trustedroots" / name
            original = path.read_bytes()
            path.write_bytes(b"tampered")
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "integrity failure"):
                self.read_provider()
            path.write_bytes(original)

    def test_rejects_missing_required_fingerprint(self):
        for name in ("wine", "version", "dotnet.exe", "codesignctl.pem", "timestampctl.pem",
                     "CL.EXE", "link.exe", "lib.exe", "kernel32.lib", "ucrt.lib", "OLDNAMES.LIB",
                     "libcmt.lib", "libvcruntime.lib"):
            original = self.config["fingerprints"]
            self.config["fingerprints"] = [item for item in original if Path(item["path"]).name != name]
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Missing required fingerprints"):
                self.read_provider()
            self.config["fingerprints"] = original

    def test_rejects_tampered_tools_and_receipt(self):
        self.config["fingerprints"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "integrity failure"):
            self.read_provider()
        self.config["fingerprints"][0]["sha256"] = windows.digest(self.config["fingerprints"][0]["path"])
        self.receipt.write_text("{}")
        with self.assertRaisesRegex(ValueError, "integrity failure"):
            self.read_provider()

    def test_rejects_wrong_receipt_kind(self):
        self.config["qualification"]["kind"] = "native-windows"
        with self.assertRaisesRegex(ValueError, "windows-proton qualification"):
            self.read_provider()

    def test_rejects_wrong_receipt_content_kind(self):
        self.receipt.write_text('{"kind":"native-windows"}')
        self.config["qualification"]["sha256"] = windows.digest(self.receipt)
        with self.assertRaisesRegex(ValueError, "proof receipt"):
            self.read_provider()

    def test_rejects_provider_work_overlap_even_through_symlink(self):
        link = self.root / "alias"
        link.symlink_to(self.config["proton_root"], target_is_directory=True)
        self.config["work_root"] = str(link)
        with self.assertRaisesRegex(ValueError, "overlap"):
            self.read_provider()

    def test_internal_launchers_reject_unsandboxed_provider_roots(self):
        self.read_provider()
        with self.assertRaisesRegex(ValueError, "read-only sandbox provider"):
            windows.internal("--dotnet", self.path, ["publish"])

    def test_rejects_tool_escape_and_ambiguous_case(self):
        tool = Path(self.config["msvc_root"]) / "bin/Hostx64/x64/CL.EXE"
        alias = tool.with_name("cl.exe")
        alias.write_text("ambiguous")
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            self.read_provider()
        alias.unlink()
        tool.unlink()
        tool.symlink_to(self.receipt)
        with self.assertRaisesRegex(ValueError, "real provider file"):
            self.read_provider()

    def test_materializes_vendor_links_but_never_follows_default_z_drive(self):
        proton = Path(self.config["proton_root"])
        source = proton / "files/share/default_pfx"
        (source / "drive_c/windows/system32").mkdir(parents=True)
        target = proton / "files/builtin.dll"
        target.write_bytes(b"vendor")
        (source / "drive_c/windows/system32/builtin.dll").symlink_to(target)
        (source / "dosdevices").mkdir()
        (source / "dosdevices/z:").symlink_to("/", target_is_directory=True)
        destination = self.root / "prefix"
        windows.materialize_prefix(proton, destination)
        copied = destination / "drive_c/windows/system32/builtin.dll"
        self.assertFalse(copied.is_symlink())
        self.assertEqual(copied.read_bytes(), b"vendor")
        copied.write_bytes(b"private")
        self.assertEqual(target.read_bytes(), b"vendor")
        self.assertFalse((destination / "dosdevices/z:").exists())
        self.assertEqual((destination / "dosdevices/c:").resolve(), destination / "drive_c")

    def test_rejects_vendor_profile_link_into_host_userdata(self):
        source = Path(self.config["proton_root"] ) / "files/share/default_pfx"
        (source / "profile").symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "escapes provider"):
            windows.materialize_prefix(Path(self.config["proton_root"]), self.root / "prefix")

    def test_rejects_shared_links_recreated_after_wine_startup(self):
        work = self.root / "job"
        windows_drive = work / "prefix/drive_c"
        windows_drive.mkdir(parents=True)
        shared = self.root / "shared.dll"
        shared.write_bytes(b"shared vendor file")
        (windows_drive / "runtime.dll").symlink_to(shared)
        with self.assertRaisesRegex(ValueError, "escapes owned job"):
            windows.require_private_prefix(work / "prefix", work)

    def test_accepts_only_private_profile_links_after_wine_startup(self):
        work = self.root / "job"
        windows_drive = work / "prefix/drive_c"
        windows_drive.mkdir(parents=True)
        (work / "home").mkdir()
        (windows_drive / "profile").symlink_to(work / "home", target_is_directory=True)
        windows.require_private_prefix(work / "prefix", work)
        (work / "prefix/dosdevices").mkdir()
        (work / "prefix/dosdevices/z:").symlink_to("/")
        with self.assertRaisesRegex(ValueError, "host Z drive"):
            windows.require_private_prefix(work / "prefix", work)


class OwnedFontTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.proton = self.root / "provider"
        self.vendor = self.proton / "files/share/wine/fonts"
        self.vendor.mkdir(parents=True)
        self.work = self.root / "owned & fonts"
        (self.work / "prefix/drive_c/windows").mkdir(parents=True)
        (self.work / "home").mkdir()
        self.config = {"proton_root": str(self.proton)}
        self.addCleanup(patch.stopall)
        patch.object(windows.os, "statvfs", return_value=SimpleNamespace(
            f_flag=windows.os.ST_RDONLY)).start()
        patch.dict(windows.os.environ, {}, clear=True).start()

    def provision(self, sources):
        with patch.object(windows.subprocess, "check_output",
                          return_value="".join(str(path) + "\n" for path in sources)) as listing:
            windows.provision_fonts(self.config, self.work)
        listing.assert_called_once_with(["fc-list", "--format=%{file}\\n"], text=True, timeout=60)
        return self.work / "prefix/drive_c/windows/Fonts"

    def test_copies_full_enumerated_set_and_vendor_fonts_without_links(self):
        vendor = self.vendor / "tahoma.ttf"
        vendor.write_bytes(b"vendor font fixture")
        replacement = self.proton / "files/share/fonts/msgothic.ttc"
        replacement.parent.mkdir()
        replacement.write_bytes(b"Proton CJK replacement fixture")
        sources = []
        for directory, content in (("regular", b"regular fixture"), ("black", b"black fixture")):
            source = self.proton / directory / "same.ttc"
            source.parent.mkdir()
            source.write_bytes(content)
            sources.append(source)
        fonts = self.provision([*sources, sources[0]])
        self.assertEqual(len(list(fonts.iterdir())), 4)
        self.assertEqual((fonts / "tahoma.ttf").read_bytes(), vendor.read_bytes())
        self.assertEqual((fonts / "msgothic.ttc").read_bytes(), replacement.read_bytes())
        for source in sources:
            copied = fonts / (windows.digest(source) + ".ttc")
            self.assertFalse(copied.is_symlink())
            self.assertEqual(copied.read_bytes(), source.read_bytes())
            copied.write_bytes(b"owned modification")
            self.assertNotEqual(copied.read_bytes(), source.read_bytes())

    def test_fontconfig_retains_aliases_but_replaces_external_search_paths(self):
        (self.vendor / "tahoma.ttf").write_bytes(b"font fixture")
        fonts = self.provision([])
        config = Path(windows.os.environ["FONTCONFIG_FILE"])
        self.assertEqual(config, self.work / "fonts.conf")
        tree = ET.parse(config).getroot()
        self.assertEqual([item.tag for item in tree], ["include", "reset-dirs", "dir", "cachedir"])
        self.assertEqual(tree.findtext("include"), "/etc/fonts/fonts.conf")
        self.assertEqual(tree.findtext("dir"), str(fonts))
        self.assertEqual(tree.findtext("cachedir"), str(self.work / "home/.cache/fontconfig"))
        self.assertIsNone(tree.find("selectfont"))

    def test_rejects_user_font_and_provider_link_to_user_font(self):
        user_font = self.root / "user.ttf"
        user_font.write_bytes(b"user font")
        alias = self.proton / "linked.ttf"
        alias.symlink_to(user_font)
        for source in (user_font, alias):
            with self.subTest(source=source), self.assertRaisesRegex(ValueError, "read-only system/provider"):
                self.provision([source])
        self.assertEqual(user_font.read_bytes(), b"user font")

    def test_rejects_writable_font_sources(self):
        source = self.vendor / "tahoma.ttf"
        source.write_bytes(b"vendor font")
        with patch.object(windows.os, "statvfs", return_value=SimpleNamespace(f_flag=0)):
            with self.assertRaisesRegex(ValueError, "read-only system/provider"):
                self.provision([])

    def test_rejects_empty_font_set_instead_of_disabling_fallback(self):
        with self.assertRaisesRegex(ValueError, "No fonts available"):
            self.provision([])

    def test_rejects_existing_font_collision_without_overwriting(self):
        (self.vendor / "tahoma.ttf").write_bytes(b"vendor font")
        fonts = self.work / "prefix/drive_c/windows/Fonts"
        fonts.mkdir()
        target = fonts / "tahoma.ttf"
        target.write_bytes(b"different installed font")
        with self.assertRaisesRegex(ValueError, "filename collision"):
            self.provision([])
        self.assertEqual(target.read_bytes(), b"different installed font")

    def test_registers_real_collection_faces_and_variable_font_families(self):
        fonts = self.work / "prefix/drive_c/windows/Fonts"
        fonts.mkdir()
        font = fonts / "collection.ttc"
        font.write_bytes(b"collection fixture")
        entries = f'{font}\tNoto CJK 日本語\tNoto CJK\n{font}\tFace "Black"\tFace\n{font}\t\tVariable Family\n'
        self.config["tools"] = {"wine": "/provider/wine"}
        with patch.object(windows.subprocess, "check_output", return_value=entries) as listing:
            with patch.object(windows, "wait_command") as command:
                windows.register_fonts(self.config, self.work)
        listing.assert_called_once_with(
            ["fc-list", "--format=%{file}\\t%{fullname[0]}\\t%{family[0]}\\n"], text=True, timeout=60)
        registry = (self.work / "fonts.reg").read_text(encoding="utf-16")
        self.assertIn("[HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts]", registry)
        self.assertIn('"Noto CJK 日本語 (TrueType)"="collection.ttc"', registry)
        self.assertIn('"Face \\"Black\\" (TrueType)"="collection.ttc"', registry)
        self.assertIn('"Variable Family (TrueType)"="collection.ttc"', registry)
        self.assertNotIn(str(self.work), registry)
        command.assert_called_once_with(
            ["/provider/wine", "C:\\windows\\system32\\reg.exe", "import", "W:\\fonts.reg"], 60)

    def test_registration_rejects_external_missing_and_empty_faces(self):
        fonts = self.work / "prefix/drive_c/windows/Fonts"
        fonts.mkdir()
        external = self.proton / "external.ttf"
        external.write_bytes(b"external font")
        alias = fonts / "alias.ttf"
        alias.symlink_to(external)
        for entries in ("", f"{external}\tExternal\tExternal\n", f"{alias}\tAlias\tAlias\n",
                        f"{fonts / 'missing.ttf'}\tMissing\tMissing\n"):
            with self.subTest(entries=entries):
                with patch.object(windows.subprocess, "check_output", return_value=entries):
                    with patch.object(windows, "wait_command") as command:
                        with self.assertRaisesRegex(ValueError, "owned font face"):
                            windows.register_fonts(self.config, self.work)
                        command.assert_not_called()


class SigningRootTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)
        # These bytes test PEM transport only; the real C importer validates X.509.
        self.der = (b"\x30\x03\x02\x01\x01", b"\x30\x03\x02\x01\x02")
        self.pins = frozenset(windows.hashlib.sha256(der).hexdigest() for der in self.der)
        self.config = {"tools": {"wine": "/provider/files/bin/wine"}, "fingerprints": []}
        for name in windows.SIGNING_BUNDLES:
            path = self.work / name
            self.config["tools"][name] = str(path)
            self.set_bundle(name, self.der)
        self.destination = self.work / "signing-roots"
        self.production_pins = windows.SIGNING_ROOT_SHA256
        self.pin_patch = patch.object(windows, "SIGNING_ROOT_SHA256", self.pins)
        self.pin_patch.start()
        self.addCleanup(self.pin_patch.stop)

    def set_bundle(self, name, certificates):
        path = Path(self.config["tools"][name])
        path.write_text("# Authenticated SDK bundle fixture\n" +
                        "".join(ssl.DER_cert_to_PEM_cert(der) for der in certificates), encoding="ascii")
        self.authenticate(name)

    def authenticate(self, name):
        path = self.config["tools"][name]
        self.config["fingerprints"] = [item for item in self.config["fingerprints"] if item["path"] != path]
        self.config["fingerprints"].append({"path": path, "sha256": windows.digest(path)})

    def test_production_allowlist_is_exactly_the_two_authorized_roots(self):
        self.assertEqual(self.production_pins, frozenset({
            "3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c",
            "2399561127a57125de8cefea610ddf2fa078b5c8067f4e828290bfb860e84b3c",
        }))

    def test_extracts_only_exact_pinned_der_files_into_private_directory(self):
        for name in windows.SIGNING_BUNDLES:
            self.set_bundle(name, (*self.der, b"\x30\x03\x02\x01\x03"))
        windows.extract_signing_roots(self.config, self.destination)
        self.assertEqual(self.destination.stat().st_mode & 0o777, 0o700)
        self.assertEqual({path.name: path.read_bytes() for path in self.destination.iterdir()},
                         {windows.hashlib.sha256(der).hexdigest() + ".cer": der for der in self.der})

    def test_requires_each_pinned_root_in_each_bundle_without_partial_output(self):
        for name in windows.SIGNING_BUNDLES:
            for remaining in self.der:
                self.set_bundle(name, (remaining,))
                with self.subTest(name=name, remaining=remaining), self.assertRaisesRegex(
                        ValueError, "Missing approved signing roots"):
                    windows.extract_signing_roots(self.config, self.destination)
                self.assertFalse(self.destination.exists())
                self.set_bundle(name, self.der)

    def test_rejects_changed_certificate_even_when_bundle_hash_is_updated(self):
        self.set_bundle("codesignctl.pem", (self.der[0], self.der[1] + b"tampered"))
        with self.assertRaisesRegex(ValueError, "Missing approved signing roots"):
            windows.extract_signing_roots(self.config, self.destination)
        self.assertFalse(self.destination.exists())

    def test_rechecks_both_bundle_fingerprints_at_extraction(self):
        for name in windows.SIGNING_BUNDLES:
            Path(self.config["tools"][name]).write_text("tampered")
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "integrity failure"):
                windows.extract_signing_roots(self.config, self.destination)
            self.assertFalse(self.destination.exists())
            self.set_bundle(name, self.der)

    def test_extraction_rejects_uncovered_bundle(self):
        for name in windows.SIGNING_BUNDLES:
            original = self.config["fingerprints"]
            self.config["fingerprints"] = [item for item in original
                                           if item["path"] != self.config["tools"][name]]
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "integrity failure"):
                windows.extract_signing_roots(self.config, self.destination)
            self.assertFalse(self.destination.exists())
            self.config["fingerprints"] = original

    def test_rejects_malformed_pem_and_duplicate_pinned_roots(self):
        path = Path(self.config["tools"]["codesignctl.pem"])
        for suffix in ("-----BEGIN CERTIFICATE-----\n",
                       "-----END CERTIFICATE-----\n",
                       "-----BEGIN CERTIFICATE-----\n!!!!\n-----END CERTIFICATE-----\n",
                       ssl.DER_cert_to_PEM_cert(self.der[0])):
            self.set_bundle("codesignctl.pem", self.der)
            path.write_text(path.read_text() + suffix)
            self.authenticate("codesignctl.pem")
            with self.subTest(suffix=suffix), self.assertRaises(ValueError):
                windows.extract_signing_roots(self.config, self.destination)
            self.assertFalse(self.destination.exists())

    def test_refuses_existing_signing_root_directory(self):
        self.destination.mkdir()
        with self.assertRaises(FileExistsError):
            windows.extract_signing_roots(self.config, self.destination)

    def test_compiles_actual_cryptoapi_tool_then_imports_with_inherited_environment(self):
        events = []
        def checked(prefix, work):
            self.assertEqual((prefix, work), (self.work / "prefix", self.work))
            events.append("check")
        def command(arguments, timeout):
            self.assertEqual(timeout, 180)
            events.append(arguments)
        with patch.object(windows, "require_private_prefix", side_effect=checked), \
                patch.object(windows, "wait_command", side_effect=command):
            windows.restore_signing_roots(self.config, self.work)
        self.assertEqual(events, [
            "check",
            ["/provider/files/bin/wine", r"M:\bin\Hostx64\x64\cl.exe", "/W4", "/WX",
             r"/Fe:W:\signing-roots.exe", r"W:\source\scripts\windows-signing-roots.c",
             "/link", "crypt32.lib"],
            "check", "check",
            ["/provider/files/bin/wine", r"W:\signing-roots.exe", r"W:\signing-roots"],
            "check",
        ])

    def test_failed_compile_aborts_import_but_still_checks_prefix(self):
        with patch.object(windows, "require_private_prefix") as checked, \
                patch.object(windows, "wait_command", side_effect=RuntimeError("compile failed")) as command:
            with self.assertRaisesRegex(RuntimeError, "compile failed"):
                windows.restore_signing_roots(self.config, self.work)
        self.assertEqual(command.call_count, 1)
        self.assertEqual(checked.call_count, 2)

    def test_invalid_bundle_never_launches_wine(self):
        self.set_bundle("timestampctl.pem", (self.der[0],))
        with patch.object(windows, "wait_command") as command:
            with self.assertRaisesRegex(ValueError, "Missing approved signing roots"):
                windows.restore_signing_roots(self.config, self.work)
        command.assert_not_called()


class MappingAndCommandTests(unittest.TestCase):
    def test_qualification_maps_only_owned_work_and_has_no_producer_environment(self):
        work = Path("/storage/job")
        config = {"action": "qualify", "proton_root": "/provider/proton",
                  "dotnet_root": "/provider/dotnet", "msvc_root": "/provider/msvc",
                  "windows_sdk_root": "/provider/sdk", "feed": "/provider/feed"}
        self.assertEqual(windows.drive_roots(config, work), {"W": work})
        environment = windows.wine_environment(config, work)
        self.assertEqual(set(environment), {
            "WINEPREFIX", "WINEARCH", "WINEDLLOVERRIDES", "LD_LIBRARY_PATH",
            "TEMP", "TMP", "XDG_CACHE_HOME", "DOTNET_SYSTEM_GLOBALIZATION_USENLS",
            "DOTNET_GCHeapHardLimit"})
        self.assertEqual(environment["DOTNET_SYSTEM_GLOBALIZATION_USENLS"], "true")
        self.assertEqual(environment["DOTNET_GCHeapHardLimit"], "0x200000000")
        self.assertEqual(environment["WINEDLLOVERRIDES"], "mscoree,mshtml=")

    def test_build_keeps_sdk_compiler_feed_and_coreclr_environment(self):
        work = Path("/storage/job")
        config = {"action": "build", "proton_root": "/provider/proton",
                  "dotnet_root": "/provider/dotnet", "msvc_root": "/provider/msvc",
                  "windows_sdk_root": "/provider/sdk", "feed": "/provider/feed",
                  "windows_sdk_version": "10.0.26100.0"}
        self.assertEqual(windows.drive_roots(config, work), {
            "W": work, "D": Path("/provider/dotnet"), "M": Path("/provider/msvc"),
            "S": Path("/provider/sdk"), "F": Path("/provider/feed")})
        environment = windows.wine_environment(config, work)
        self.assertEqual(environment["DOTNET_ROOT"], "D:\\")
        self.assertEqual(environment["WINEDLLOVERRIDES"], "mscoree=b;mshtml=")
        self.assertEqual(environment["WINEPATH"], "D:\\;M:\\bin\\Hostx64\\x64;S:\\bin\\10.0.26100.0\\x64")
        self.assertIn("M:\\lib\\x64", environment["LIB"])
        self.assertIn("S:\\Include\\10.0.26100.0\\um", environment["INCLUDE"])

    def test_mapping_preserves_spaces_and_rejects_unmapped_or_symlink_escape(self):
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary) / "work"
            work.mkdir()
            roots = {"W": work}
            self.assertEqual(windows.native_path(work / "source/a b.csproj", roots), "W:\\source\\a b.csproj")
            self.assertEqual(windows.native_path(work, roots), "W:\\")
            with self.assertRaisesRegex(ValueError, "absolute"):
                windows.native_path("relative", roots)
            with self.assertRaisesRegex(ValueError, "outside"):
                windows.native_path(work.parent, roots)
            (work / "escape").symlink_to(work.parent, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "outside"):
                windows.native_path(work / "escape/file", roots)

    def test_godot_maps_only_host_paths_and_keeps_resource_switches(self):
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary)
            args = ["--headless", "--path", str(work / "project"), "res://demo.gd", "--", "--smoke"]
            self.assertEqual(windows.godot_arguments(args, {"W": work}),
                             ["--headless", "--path", "W:\\project", "res://demo.gd", "--", "--smoke"])

    def test_windows_publish_uses_real_sdk_supported_tools_and_isolation(self):
        config = {"tools": {"wine": "/provider/files/bin/wine"}}
        args = ["publish", "W:\\source\\native\\NecronomiDom.Native.csproj", "-r", "win-x64",
                "/p:RestoreConfigFile=W:\\build\\nuget.config"]
        self.assertEqual(windows.dotnet_command(config, args),
                         ["/provider/files/bin/wine", "D:\\dotnet.exe", *args,
                          "/p:IlcUseEnvironmentalTools=true", "/p:UseSharedCompilation=false"])
        with self.assertRaisesRegex(ValueError, "only accepts publish"):
            windows.dotnet_command(config, ["workload", "install"])

    def test_sandbox_has_one_private_writable_tree_and_no_environment_inheritance(self):
        work = Path("/storage/jobs/unique")
        command = windows.sandbox_command({}, work)
        self.assertIn("--clearenv", command)
        self.assertIn("--unshare-pid", command)
        self.assertIn("--unshare-ipc", command)
        self.assertIn("--die-with-parent", command)
        self.assertEqual(command[command.index("--ro-bind") + 1:command.index("--ro-bind") + 3], ["/", "/"])
        binds = [command[index + 1:index + 3] for index, arg in enumerate(command) if arg == "--bind"]
        self.assertEqual(binds, [[str(work), str(work)], [str(work / "tmp"), "/tmp"], [str(work / "tmp"), "/var/tmp"]])
        self.assertNotIn("--share-pid", command)

    def test_native_driver_keeps_host_config_path_separate_from_windows_content(self):
        driver = (ROOT / "native/build.sh").read_text()
        self.assertIn('"$NECRONOMIDOM_NATIVE_PATH_MAPPER" "$1"', driver)
        self.assertIn('RESTORE_CONFIG_LOCAL="$SDK_ROOT/nuget.config"', driver)
        self.assertIn('python - "$FEED_NATIVE" "$RESTORE_CONFIG_LOCAL"', driver)
        self.assertIn('/p:RestoreConfigFile="$RESTORE_CONFIG_NATIVE"', driver)
        self.assertIn('"$(cat "$FEED/.identity")" != "$SDK_ID"', driver)


class DisplayHandshakeTests(unittest.TestCase):
    def test_waits_for_separate_readiness_newline(self):
        with patch.object(windows.select, "select", return_value=([7], [], [])), \
                patch.object(windows.os, "read", side_effect=[b"12", b"\n"]) as read:
            self.assertEqual(windows.read_display_number(7), "12")
            self.assertEqual(read.call_count, 2)

    def test_rejects_incomplete_or_invalid_readiness(self):
        for chunks in ([b"12", b""], [b"12\nextra", b""], [b"x\n"]):
            with self.subTest(chunks=chunks), \
                    patch.object(windows.select, "select", return_value=([7], [], [])), \
                    patch.object(windows.os, "read", side_effect=chunks):
                with self.assertRaises(RuntimeError):
                    windows.read_display_number(7)


class ArtifactIntegrityTests(unittest.TestCase):
    def test_integrity_failure_prevents_extraction(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "godot.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("godot.exe", b"fixture")
            with self.assertRaisesRegex(ValueError, "integrity failure"):
                windows.extract_checked_zip(archive, "0" * 64, root / "unpacked")
            self.assertFalse((root / "unpacked").exists())

    def test_archive_rejects_windows_and_posix_traversal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("../escape", "..\\escape", "C:/escape", "/escape"):
                archive = root / "godot.zip"
                with zipfile.ZipFile(archive, "w") as output:
                    output.writestr(name, b"fixture")
                with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Unsafe path"):
                    windows.extract_checked_zip(archive, windows.digest(archive), root / "unpacked")

    def test_official_metadata_requires_digest_and_exact_asset_url(self):
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary)
            name = "Godot_v4.7.2-stable_win64.exe.zip"
            official = "https://github.com/godotengine/godot/releases/download/4.7.2-stable/" + name
            for asset in ({"name": name, "browser_download_url": official},
                          {"name": name, "browser_download_url": "https://untrusted.invalid/" + name, "digest": "sha256:" + "0" * 64}):
                def fetch(url, path):
                    path.write_text(json.dumps({"assets": [asset]}))
                with self.subTest(asset=asset), patch.object(windows, "fetch", side_effect=fetch) as download:
                    with self.assertRaises(ValueError):
                        windows.fetch_godot("4.7.2", work)
                    self.assertEqual(download.call_count, 1)

    def test_official_engine_is_executed_without_console_tree_wait_wrapper(self):
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary)
            name = "Godot_v4.7.2-stable_win64.exe.zip"
            checksum = "a" * 64
            asset = {"name": name, "digest": "sha256:" + checksum,
                     "browser_download_url": "https://github.com/godotengine/godot/releases/download/4.7.2-stable/" + name}
            def fetch(url, path):
                path.write_text(json.dumps({"assets": [asset]}))
            with patch.object(windows, "fetch", side_effect=fetch), \
                    patch.object(windows, "extract_checked_zip") as extract, \
                    patch.object(windows, "require_pe") as require_pe:
                executable = windows.fetch_godot("4.7.2", work)
                self.assertEqual(executable, work / "godot/Godot_v4.7.2-stable_win64.exe")
                extract.assert_called_once_with(work / name, checksum, work / "godot")
                require_pe.assert_called_once_with(executable, dll=False)

    def test_pe_requires_x64_dll_not_linux_or_x86_image(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "native.dll"
            image = bytearray(0x80 + 26)
            image[:2] = b"MZ"
            struct.pack_into("<I", image, 0x3c, 0x80)
            image[0x80:0x84] = b"PE\0\0"
            struct.pack_into("<H", image, 0x84, 0x8664)
            struct.pack_into("<H", image, 0x80 + 22, 0x2000)
            struct.pack_into("<H", image, 0x80 + 24, 0x20b)
            path.write_bytes(image)
            windows.require_pe(path)
            struct.pack_into("<H", image, 0x84, 0x14c)
            path.write_bytes(image)
            with self.assertRaisesRegex(ValueError, "x86_64"):
                windows.require_pe(path)
            path.write_bytes(b"\x7fELF")
            with self.assertRaisesRegex(ValueError, "Windows PE"):
                windows.require_pe(path)


class VersionStreamTests(unittest.TestCase):
    def test_version_stderr_is_retained_and_printed_without_filtering(self):
        diagnostics = b"Wine FIXME diagnostic\r\nanother diagnostic\n"
        for status in (0, 3):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "version.stderr"
                def execute(command, **kwargs):
                    kwargs["stderr"].write(diagnostics)
                    return stock.subprocess.CompletedProcess(command, status, "4.7.2.stable.official.fixture\n")
                with patch.object(stock.subprocess, "run", side_effect=execute), \
                        patch.object(stock.sys, "stderr") as stderr:
                    if status:
                        with self.assertRaisesRegex(RuntimeError, "status 3"):
                            stock.run(["godot", "--version"], stderr_path=path)
                    else:
                        self.assertEqual(stock.run(["godot", "--version"], stderr_path=path),
                                         "4.7.2.stable.official.fixture\n")
                    self.assertEqual(path.read_bytes(), diagnostics)
                    stderr.buffer.write.assert_called_once_with(diagnostics)

    def test_version_timeout_preserves_all_stderr(self):
        diagnostics = b"Wine diagnostic before timeout\n"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "version.stderr"
            def execute(command, **kwargs):
                kwargs["stderr"].write(diagnostics)
                raise stock.subprocess.TimeoutExpired(command, 1, output=b"partial stdout\n")
            with patch.object(stock.subprocess, "run", side_effect=execute), \
                    patch.object(stock.sys, "stderr") as stderr:
                with self.assertRaises(stock.subprocess.TimeoutExpired):
                    stock.run(["godot", "--version"], stderr_path=path)
                self.assertEqual(path.read_bytes(), diagnostics)
                stderr.buffer.write.assert_called_once_with(diagnostics)


class QualificationAdapterTests(unittest.TestCase):
    def test_windows_identity_is_required_without_weakening_existing_oracles(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary) / "addon.zip"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("addons/necronomidom/necronomidom.gdextension",
                                 '[libraries]\nwindows.debug.x86_64 = "res://addons/necronomidom/bin/native.dll"\n')
                archive.writestr("addons/necronomidom/bin/native.dll", b"fixture")
            version_stderr = Path(temporary) / "version.stderr.log"
            for missing in (None, "features", "demo", "lifecycle", "runtime_bridge", "editor_bridge"):
                commands = []
                def run(command, **kwargs):
                    commands.append(command)
                    if "--version" in command:
                        self.assertEqual(kwargs, {"stderr_path": version_stderr})
                        return "4.7.2.stable.official.fixture"
                    self.assertFalse(kwargs)
                    if "res://platform.gd" in command:
                        project = Path(command[command.index("--path") + 1])
                        script = (project / "platform.gd").read_text()
                        self.assertIn('OS.has_feature("windows")', script)
                        self.assertIn('OS.has_feature("x86_64")', script)
                        return "" if missing == "features" else "STOCK_PACKAGE_FEATURES windows x86_64\n"
                    if "--smoke" in command:
                        return "" if missing == "demo" else 'count="One shape stirs beneath the page."'
                    if "res://native/tests/lifecycle.gd" in command:
                        return "" if missing == "lifecycle" else "native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true\n"
                    if "res://native/tests/bridge_runtime.gd" in command or command[-1] == "--editor":
                        editor = command[-1] == "--editor"
                        project = Path(command[command.index("--path") + 1])
                        self.assertTrue((project / "native/tests/bridge.gd").is_file())
                        if editor:
                            self.assertNotIn("--script", command)
                            self.assertTrue((project / "addons/bridge_qualification/plugin.cfg").is_file())
                        absent = missing == ("editor_bridge" if editor else "runtime_bridge")
                        return "" if absent else "native-bridge: incremental DOM, state, geometry, keyboard, wheel, pointer capture and resource lifetime passed\n"
                    return ""
                with self.subTest(missing=missing), patch.object(stock, "run", side_effect=run):
                    if missing:
                        with self.assertRaises(RuntimeError):
                            stock.qualify(Path("godot-adapter"), package, "4.7.2", required_features=("windows", "x86_64"), version_stderr=version_stderr)
                    else:
                        stock.qualify(Path("godot-adapter"), package, "4.7.2", required_features=("windows", "x86_64"), version_stderr=version_stderr)
                        self.assertEqual(len(commands), 8)
                        bridge = [command for command in commands if "res://native/tests/bridge_runtime.gd" in command or command[-1] == "--editor"]
                        self.assertEqual(len(bridge), 2)
                        self.assertFalse("--editor" in bridge[0])
                        self.assertIn("--editor", bridge[1])
                        self.assertNotIn("--script", bridge[1])
                    self.assertTrue(all(command[0] == "godot-adapter" for command in commands))


if __name__ == "__main__":
    unittest.main()
