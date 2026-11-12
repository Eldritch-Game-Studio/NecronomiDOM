"""Focused subprocess-output contract tests; no engine or toolchain is launched."""
import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "stock_package_diagnostics", Path(__file__).with_name("qualify-stock-package.py"))
stock = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stock)

MARKER = "native-bridge: incremental DOM, state, geometry, keyboard, wheel, pointer capture and resource lifetime passed"


class DiagnosticsTests(unittest.TestCase):
    def command(self, stdout, stderr="", status=0):
        return [sys.executable, "-c",
                f"import sys; sys.stdout.write({stdout!r}); sys.stdout.flush(); "
                f"sys.stderr.write({stderr!r}); sys.exit({status})"]

    def test_pass_marker_and_zero_exit_do_not_hide_errors_or_leaks(self):
        diagnostics = (
            "ERROR: Can't open file from path '/missing/font.ttc'.",
            "SCRIPT ERROR: Invalid call. Nonexistent function 'missing'.",
            'WARNING: 6 RIDs of type "Canvas" were leaked.',
            "ERROR: 6 RID allocations of type 'Viewport' were leaked at exit.",
            "WARNING: 209 ObjectDB instances were leaked at exit (run with `--verbose` for details).",
            "WARNING: ObjectDB instances leaked at exit (run with --verbose for details).",
            "1 RID allocations of type 'Font' were leaked at exit.",
            "\x1b[31mERROR:\x1b[0m colored engine error",
        )
        for diagnostic in diagnostics:
            for stream in ("stdout", "stderr"):
                with self.subTest(diagnostic=diagnostic, stream=stream):
                    stdout = MARKER + "\n"
                    stderr = ""
                    if stream == "stdout":
                        stdout += diagnostic + "\n"
                    else:
                        stderr = diagnostic + "\n"
                    captured = io.StringIO()
                    with contextlib.redirect_stdout(captured):
                        with self.assertRaisesRegex(RuntimeError, "error or leaked resources"):
                            stock.run(self.command(stdout, stderr))
                    self.assertIn(MARKER, captured.getvalue())
                    self.assertIn(diagnostic, captured.getvalue())

    def test_ordinary_adapter_diagnostics_are_preserved_not_classified_as_godot_errors(self):
        output = (MARKER + "\n0123:fixme:win:unimplemented function\n"
                  "Errors from xkbcomp are not fatal to the X server\n"
                  "WARNING: A non-leak warning\nfixture text quotes ERROR: without a diagnostic\n")
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            self.assertEqual(stock.run(self.command(output)), output)
        self.assertIn(output, captured.getvalue())

    def test_version_stderr_is_retained_but_not_part_of_version_identity(self):
        version = "4.7.2.stable.official.ed1daf0bf\n"
        diagnostic = b"0123:fixme:win:adapter diagnostic\n"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "version.stderr.log"
            stderr = io.TextIOWrapper(io.BytesIO(), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(stderr):
                self.assertEqual(stock.run(self.command(version, diagnostic.decode()), stderr_path=path), version)
            self.assertEqual(path.read_bytes(), diagnostic)
            self.assertEqual(stderr.buffer.getvalue(), diagnostic)

    def test_separate_version_stderr_still_rejects_godot_errors(self):
        diagnostic = b"ERROR: version invocation failed\n"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "version.stderr.log"
            stderr = io.TextIOWrapper(io.BytesIO(), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(stderr):
                with self.assertRaisesRegex(RuntimeError, "error or leaked resources"):
                    stock.run(self.command("4.7.2.stable.official.ed1daf0bf\n", diagnostic.decode()),
                              stderr_path=path)
            self.assertEqual(path.read_bytes(), diagnostic)
            self.assertEqual(stderr.buffer.getvalue(), diagnostic)

    def test_nonzero_exit_remains_failure_even_with_marker(self):
        with contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "status 3"):
                stock.run(self.command(MARKER + "\n", status=3))


if __name__ == "__main__":
    unittest.main()
