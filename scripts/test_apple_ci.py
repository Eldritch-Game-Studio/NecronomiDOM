"""Exercise architecture-bound qualification, runner trust and foreground locking."""

import importlib.util
import json
import os
from pathlib import Path
import re
import select
import signal
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("stock_package", ROOT / "scripts/qualify-stock-package.py")
stock = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(stock)


class ArchitectureTests(unittest.TestCase):
    def test_existing_non_apple_invocation_is_unchanged(self):
        self.assertEqual(stock.godot_command(Path("godot")), ["godot"])

    def test_architecture_binding_rejects_other_hosts(self):
        with patch.object(stock.platform, "system", return_value="Linux"):
            with self.assertRaises(ValueError):
                stock.godot_command(Path("godot"), "x86_64")

    def test_every_apple_process_is_bound_and_requires_runtime_proof(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary) / "addon.zip"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("addons/necronomidom/necronomidom.gdextension",
                                 '[libraries]\nmacos = "res://addons/necronomidom/bin/library.dylib"\n')
                archive.writestr("addons/necronomidom/bin/library.dylib", b"fixture")
            for architecture in ("arm64", "x86_64"):
                for prove_architecture in (True, False):
                    commands = []

                    def run(command):
                        commands.append(command)
                        if "--version" in command:
                            return "4.7.2.stable.official.fixture"
                        if "res://architecture.gd" in command:
                            project = Path(command[command.index("--path") + 1])
                            self.assertIn(f'OS.has_feature("{architecture}")',
                                          (project / "architecture.gd").read_text())
                            return f"STOCK_PACKAGE_ARCH {architecture}\n" if prove_architecture else ""
                        if "--smoke" in command:
                            return 'count="One shape stirs beneath the page."'
                        if "res://native/tests/lifecycle.gd" in command:
                            return "native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true\n"
                        return ""

                    with self.subTest(architecture=architecture, proof=prove_architecture), \
                            patch.object(stock.platform, "system", return_value="Darwin"), \
                            patch.object(stock, "run", side_effect=run):
                        if prove_architecture:
                            stock.qualify(Path("godot"), package, "4.7.2", architecture)
                            self.assertEqual(len(commands), 6)
                        else:
                            with self.assertRaisesRegex(RuntimeError, "process architecture"):
                                stock.qualify(Path("godot"), package, "4.7.2", architecture)
                        for command in commands:
                            self.assertEqual(command[:3], ["/usr/bin/arch", "-" + architecture, "godot"])


class TrustTests(unittest.TestCase):
    def test_actual_trusted_admission_checks_event_identity_and_fork_approval(self):
        workflow = (ROOT / ".github/workflows/ci-local.yml").read_text()
        admission = workflow.split("  admit:\n", 1)[1].split("\n  changes:", 1)[0]
        self.assertNotIn("actions/checkout", admission)
        self.assertIn("'local-runner-external-pr'", admission)
        self.assertIn("EXTERNAL_APPROVAL_ENABLED: ${{ vars.LOCAL_EXTERNAL_PR_APPROVAL_ENABLED }}", admission)
        script = textwrap.dedent(re.search(
            r'python3" - <<\'PY\'\n(.*?)^          PY$', admission, re.MULTILINE | re.DOTALL).group(1))
        repository = "Eldritch-Game-Studio/NecronomiDOM"
        sha = "a" * 40
        cases = [
            ({}, True),
            ({"event": "push"}, True),
            ({"event": "workflow_dispatch"}, True),
            ({"event": "pull_request_target"}, False),
            ({"head": "fork/NecronomiDOM"}, False),
            ({"head": "fork/NecronomiDOM", "approved": "true"}, True),
            ({"head": "fork/NecronomiDOM", "approved": "false"}, False),
            ({"head": None, "approved": "true"}, False),
            ({"base": "other/repo", "approved": "true"}, False),
            ({"source": "b" * 40}, False),
            ({"source": "main"}, False),
            ({"repository": "other/repo"}, False),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            event_path = Path(temporary) / "event.json"
            for case, allowed in cases:
                with self.subTest(case=case):
                    head = case.get("head", repository)
                    event_path.write_text(json.dumps({"pull_request": {
                        "head": {"repo": {"full_name": head} if head else None},
                        "base": {"repo": {"full_name": case.get("base", repository)}},
                    }}))
                    environment = dict(os.environ, GITHUB_REPOSITORY=case.get("repository", repository),
                                       GITHUB_SHA=sha, SOURCE_SHA=case.get("source", sha),
                                       GITHUB_EVENT_NAME=case.get("event", "pull_request"),
                                       GITHUB_EVENT_PATH=str(event_path),
                                       EXTERNAL_APPROVAL_ENABLED=case.get("approved", ""))
                    result = subprocess.run([sys.executable, "-c", script], env=environment,
                                            capture_output=True, text=True)
                    self.assertEqual(result.returncode == 0, allowed, result.stdout + result.stderr)

    def test_wrapper_uses_trusted_main_and_all_execution_is_local(self):
        caller = (ROOT / ".github/workflows/ci.yml").read_text()
        self.assertIn("uses: Eldritch-Game-Studio/NecronomiDOM/.github/workflows/ci-local.yml@main", caller)
        self.assertIn("source-sha: ${{ github.sha }}", caller)
        workflow = (ROOT / ".github/workflows/ci-local.yml").read_text()
        self.assertIn("  changes:\n    needs: admit\n", workflow)
        for runner in re.findall(r"^    runs-on: (.+)$", workflow, re.MULTILINE):
            self.assertTrue(runner.startswith("[self-hosted,") or runner == "${{ matrix.runner }}")
        for matrix in re.findall(r"'(\[\{\"runner\":[^']+)'", workflow):
            for target in json.loads(matrix):
                self.assertIn("self-hosted", target["runner"])
        checkouts = workflow.count("uses: actions/checkout@v4")
        self.assertEqual(checkouts, workflow.count("ref: ${{ inputs.source-sha }}"))
        self.assertEqual(checkouts, workflow.count("persist-credentials: false"))

    def test_windows_is_opt_in_for_both_matrices_and_publication(self):
        workflow = (ROOT / ".github/workflows/ci-local.yml").read_text()
        matrices = re.findall(r"include: \$\{\{ fromJSON\((.+)\) \}\}", workflow)
        self.assertEqual(len(matrices), 2)
        for matrix in matrices:
            self.assertTrue(matrix.startswith("vars.NECRONOMIDOM_WINDOWS_ENABLED == 'true' && "))
            enabled, disabled = re.findall(r"'(\[\{\"runner\":[^']+)'", matrix)
            self.assertTrue(any("Windows" in item["runner"] for item in json.loads(enabled)))
            self.assertEqual([item["runner"] for item in json.loads(disabled)],
                             [["self-hosted", "Linux", "ARM64", "eldritch-linux-arm64"]])
        release = workflow.split("  release:\n", 1)[1]
        self.assertIn("if: ${{ vars.NECRONOMIDOM_WINDOWS_ENABLED == 'true' && ", release)


@unittest.skipUnless(os.name == "posix", "fcntl/process groups require POSIX")
class LockTests(unittest.TestCase):
    def launch(self, home, code):
        return subprocess.Popen([sys.executable, str(ROOT / "scripts/apple-locked.py"),
                                 sys.executable, "-c", code],
                                env=dict(os.environ, HOME=home), stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, text=True)

    def test_waits_for_shared_lock_and_preserves_exit_status(self):
        import fcntl
        with tempfile.TemporaryDirectory() as home:
            lock = Path(home) / ".local/state/eldritch-macos/producer.lock"
            lock.parent.mkdir(parents=True)
            with lock.open("a") as handle:
                fcntl.flock(handle, fcntl.LOCK_EX)
                child = self.launch(home, 'print("started", flush=True); raise SystemExit(7)')
                try:
                    self.assertEqual(select.select([child.stdout], [], [], 0.2)[0], [])
                    fcntl.flock(handle, fcntl.LOCK_UN)
                    stdout, stderr = child.communicate(timeout=10)
                    self.assertEqual((child.returncode, stdout, stderr), (7, "started\n", ""))
                finally:
                    if child.poll() is None:
                        child.kill()
                    child.communicate()

    def test_cancellation_reaches_child_before_releasing_lock(self):
        import fcntl
        with tempfile.TemporaryDirectory() as home:
            stopped = Path(home) / "stopped"
            code = ("import pathlib, signal, time\n"
                    f"def stop(*args):\n pathlib.Path({str(stopped)!r}).touch()\n raise SystemExit(0)\n"
                    "signal.signal(signal.SIGTERM, stop)\nprint('ready', flush=True)\ntime.sleep(60)\n")
            child = self.launch(home, code)
            try:
                self.assertTrue(select.select([child.stdout], [], [], 10)[0])
                self.assertEqual(child.stdout.readline(), "ready\n")
                child.send_signal(signal.SIGTERM)
                child.communicate(timeout=10)
                self.assertEqual(child.returncode, 128 + signal.SIGTERM)
                self.assertTrue(stopped.exists())
                with (Path(home) / ".local/state/eldritch-macos/producer.lock").open("a") as handle:
                    fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            finally:
                if child.poll() is None:
                    child.kill()
                child.communicate()


if __name__ == "__main__":
    unittest.main()
