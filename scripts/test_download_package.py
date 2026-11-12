import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("download_package", Path(__file__).with_name("download-package.py"))
download = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(download)


class Response(io.BytesIO):
    status = 200
    url = "https://storage.example/artifact"

    def __init__(self, data, size=None):
        super().__init__(data)
        self.headers = {"Content-Length": str(len(data) if size is None else size)}


def zipped(name, data):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as archive:
        archive.writestr(name, data)
    return stream.getvalue()


class PackageDownloadTests(unittest.TestCase):
    def test_truncated_transfer_is_retried_from_start_and_exact_zip_installed(self):
        inner = zipped("addon.txt", b"native package")
        outer = zipped("package.zip", inner)
        digest = hashlib.sha256(outer).hexdigest()
        responses = [Response(outer[:20], len(outer)), Response(outer)]
        with tempfile.TemporaryDirectory() as temporary, \
                patch.object(download, "artifact_url", return_value=Response.url), \
                patch.object(download.urllib.request, "urlopen", side_effect=responses) as request, \
                patch.object(download.time, "sleep"):
            destination = Path(temporary)
            download.download("owner/repo", 123, digest, "package.zip", destination, "secret")
            self.assertEqual((destination / "package.zip").read_bytes(), inner)
            self.assertEqual(request.call_count, 2)
            self.assertEqual(list(destination.iterdir()), [destination / "package.zip"])
            for call in request.call_args_list:
                self.assertIsInstance(call.args[0], str)  # No authorized API Request object reaches storage.

    def test_wrong_digest_never_replaces_previous_package(self):
        with tempfile.TemporaryDirectory() as temporary, \
                patch.object(download, "artifact_url", return_value=Response.url), \
                patch.object(download.urllib.request, "urlopen", side_effect=lambda *a, **k: Response(b"wrong")), \
                patch.object(download.time, "sleep"):
            destination = Path(temporary)
            (destination / "package.zip").write_bytes(b"previous")
            with self.assertRaisesRegex(RuntimeError, "three complete-download attempts"):
                download.download("owner/repo", 123, "sha256:" + "0" * 64, "package.zip", destination, "secret")
            self.assertEqual((destination / "package.zip").read_bytes(), b"previous")

    def test_matching_digest_does_not_accept_invalid_inner_zip(self):
        outer = zipped("package.zip", b"PK\x03\x04incomplete")
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            archive = destination / "artifact.zip"
            archive.write_bytes(outer)
            with self.assertRaises(zipfile.BadZipFile):
                download.install(archive, "package.zip", destination)
            self.assertFalse((destination / "package.zip").exists())
            self.assertEqual(list(destination.iterdir()), [archive])

    def test_wrong_artifact_member_is_not_extracted(self):
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary)
            archive = destination / "artifact.zip"
            archive.write_bytes(zipped("../package.zip", b"unexpected"))
            with self.assertRaisesRegex(ValueError, "exactly the expected"):
                download.install(archive, "package.zip", destination)


if __name__ == "__main__":
    unittest.main()
