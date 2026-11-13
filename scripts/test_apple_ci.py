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
                        if "res://native/tests/bridge_runtime.gd" in command or command[-1] == "--editor":
                            project = Path(command[command.index("--path") + 1])
                            self.assertTrue((project / "native/tests/bridge.html").is_file())
                            self.assertTrue((project / "native/tests/bridge.gd").is_file())
                            if command[-1] == "--editor":
                                self.assertNotIn("--script", command)
                                self.assertTrue((project / "addons/bridge_qualification/plugin.cfg").is_file())
                            return "native-bridge: incremental DOM, state, geometry, keyboard, wheel, pointer capture and resource lifetime passed\n"
                        return ""

                    with self.subTest(architecture=architecture, proof=prove_architecture), \
                            patch.object(stock.platform, "system", return_value="Darwin"), \
                            patch.object(stock, "run", side_effect=run):
                        if prove_architecture:
                            stock.qualify(Path("godot"), package, "4.7.2", architecture)
                            self.assertEqual(len(commands), 8)
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
        matrices = re.findall(r"^          - runner: \[(.+)\]$", workflow, re.MULTILINE)
        self.assertEqual(len(matrices), 2)
        for runner in matrices:
            self.assertIn("self-hosted", runner.split(", "))
        checkouts = workflow.count("uses: actions/checkout@v4")
        self.assertEqual(checkouts, workflow.count("ref: ${{ inputs.source-sha }}"))
        self.assertEqual(checkouts, workflow.count("persist-credentials: false"))

    def job(self, name):
        workflow = (ROOT / ".github/workflows/ci-local.yml").read_text()
        return re.split(r"\n  [a-z][a-z0-9-]*:\n", workflow.split(f"  {name}:\n", 1)[1], 1)[0]

    def test_windows_proton_jobs_are_mandatory_linux_hosted_full_gates(self):
        workflow = (ROOT / ".github/workflows/ci-local.yml").read_text()
        self.assertNotIn("NECRONOMIDOM_WINDOWS_ENABLED", workflow)
        self.assertNotIn("eldritch-windows", workflow)
        for name, command in (("native-windows-proton", "build"), ("qualify-windows-proton", "qualify")):
            job = self.job(name)
            self.assertEqual(re.findall(r"^    if: (.+)$", job, re.MULTILINE),
                             ["needs.changes.outputs.full == 'true'"])
            self.assertIn("runs-on: [self-hosted, Linux, X64, eldritch-linux]", job)
            self.assertIn("NECRONOMIDOM_WINDOWS_TOOLCHAIN: ${{ vars.NECRONOMIDOM_WINDOWS_TOOLCHAIN }}", job)
            self.assertIn(f"python3 scripts/windows-proton.py {command}", job)
            self.assertIn('--toolchain "$NECRONOMIDOM_WINDOWS_TOOLCHAIN"', job)
            self.assertNotIn("continue-on-error", job)
        build = self.job("native-windows-proton")
        self.assertIn("needs: [changes, native-linux-x64]", build)
        self.assertIn("name: sdk-managed-feed", build)
        self.assertIn('path: ${{ runner.temp }}/sf', build)
        self.assertIn('--sdk-feed "$RUNNER_TEMP/sf"', build)
        self.assertIn("--output addons/necronomidom/bin/win-x64/necronomidom.dll", build)
        self.assertIn("name: native-win-x64", build)
        self.assertIn("if-no-files-found: error", build)
        qualifier = self.job("qualify-windows-proton")
        self.assertIn("needs: [changes, package]", qualifier)
        self.assertIn('--package "dist/$package"', qualifier)
        self.assertIn('--godot-version "$GODOT_VERSION"', qualifier)

    def test_full_package_requires_windows_and_only_manual_partial_can_skip_targets(self):
        job = self.job("package")
        needs = re.search(r"^    needs: \[(.+)\]$", job, re.MULTILINE).group(1).split(", ")
        targets = ("native-platforms", "native-apple", "native-windows-proton")
        condition = re.search(r"^    if: (.+)$", job, re.MULTILINE).group(1)
        full, partial = condition.split(" || ")
        self.assertIn("needs.changes.outputs.full == 'true'", full)
        for target in targets:
            self.assertIn(target, needs)
            self.assertIn(f"needs.{target}.result == 'success'", full)
            self.assertIn(f"needs.{target}.result == 'skipped'", partial)
        for gate in ("changes", "docs", "test", "native-linux-x64", "smoke"):
            self.assertIn(f"needs.{gate}.result == 'success'", full)
        self.assertIn("github.event_name == 'workflow_dispatch'", partial)
        self.assertIn("inputs.partial == true", partial)
        self.assertIn("needs.changes.outputs.mode == 'partial'", partial)
        self.assertIn("PARTIAL: ${{ github.event_name == 'workflow_dispatch' && inputs.partial == true }}", job)
        # The bootstrap retains the trusted-main assembler API, never optional routing.
        self.assertEqual(re.findall(r"^\s+WINDOWS_ENABLED: (.+)$", job, re.MULTILINE), ["'true'"])
        self.assertIn("run: python3 scripts/assemble-package.py", job)

    def test_runtime_projection_precedes_shared_artifact_upload(self):
        package = self.job("package")
        self.assertIn("runtime-name: ${{ steps.runtime.outputs.name }}", package)
        projection = package.split("        id: runtime\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn("FULL_PACKAGE: ${{ steps.zip.outputs.name }}", projection)
        self.assertIn('runtime_name="${FULL_PACKAGE%.zip}-runtime.zip"', projection)
        self.assertIn("python3 scripts/project-runtime-package.py", projection)
        self.assertIn('--package "dist/$FULL_PACKAGE" --output "dist/$runtime_name"', projection)
        self.assertIn('echo "name=$runtime_name" >> "$GITHUB_OUTPUT"', projection)
        self.assertLess(package.index("scripts/assemble-package.py"), package.index("id: runtime"))
        self.assertLess(package.index("id: runtime"), package.index("id: upload"))
        upload = package.split("        id: upload\n", 1)[1]
        self.assertIn("name: NecronomiDOM", upload)
        self.assertIn("path: |\n            dist/${{ steps.zip.outputs.name }}\n"
                      "            dist/${{ steps.runtime.outputs.name }}", upload)
        self.assertIn("if-no-files-found: error", upload)

    def test_every_final_zip_qualifier_runs_both_and_fails_on_either(self):
        full = "NecronomiDOM-v1.2.3.zip"
        runtime = "NecronomiDOM-v1.2.3-runtime.zip"
        for name, qualifier in (
                ("qualify-linux-x64", "scripts/qualify-stock-package.py"),
                ("qualify-platforms", "scripts/qualify-stock-package.py"),
                ("qualify-apple", "scripts/apple-locked.py python3 scripts/qualify-stock-package.py"),
                ("qualify-windows-proton", "scripts/windows-proton.py qualify")):
            job = self.job(name)
            self.assertNotIn("continue-on-error", job)
            loop = re.search(r'^          for package in .*?^          done$',
                             job, re.MULTILINE | re.DOTALL).group(0)
            self.assertIn('for package in "${{ needs.package.outputs.name }}" '
                          '"${{ needs.package.outputs.runtime-name }}"; do', loop)
            self.assertIn(qualifier, loop)
            self.assertIn('--package "dist/$package"', loop)
            self.assertIn('--godot-version "$GODOT_VERSION"', loop)
            script = textwrap.dedent(loop).replace("${{ needs.package.outputs.name }}", full)
            script = script.replace("${{ needs.package.outputs.runtime-name }}", runtime)
            script = script.replace("${{ matrix.architecture }}", "x86_64")
            # Stub only the qualifier process; exercise the workflow's real shell loop
            # and fail-fast behavior for either exact package filename.
            stub = """\
python3() {
    printf '%s\\n' "$*" >> "$CALLS"
    if [ -n "$FAIL_PACKAGE" ] && [[ " $* " == *" --package dist/$FAIL_PACKAGE "* ]]; then
        return 23
    fi
}
python() { python3 "$@"; }
"""
            for failed in ("", full, runtime):
                with self.subTest(job=name, failed=failed), tempfile.TemporaryDirectory() as temporary:
                    calls = Path(temporary) / "calls"
                    result = subprocess.run(
                        ["bash", "-e", "-c", stub + script],
                        env=dict(os.environ, CALLS=str(calls), FAIL_PACKAGE=failed,
                                 GODOT_PACKAGE="stock-godot", GODOT_VERSION="4.7.2",
                                 NECRONOMIDOM_WINDOWS_TOOLCHAIN="provider.json"),
                        capture_output=True, text=True)
                    self.assertEqual(result.returncode, 23 if failed else 0, result.stderr)
                    invocations = calls.read_text().splitlines()
                    expected = [full] if failed == full else [full, runtime]
                    self.assertEqual(len(invocations), len(expected))
                    for invocation, filename in zip(invocations, expected):
                        self.assertIn(f"--package dist/{filename}", invocation)
                        self.assertIn(qualifier, invocation)

    def test_linux_arm_and_native_apple_gates_remain_required(self):
        for name in ("native-platforms", "qualify-platforms"):
            job = self.job(name)
            self.assertIn("if: needs.changes.outputs.full == 'true'", job)
            self.assertIn("runner: [self-hosted, Linux, ARM64, eldritch-linux-arm64]", job)
            self.assertIn("runs-on: ${{ matrix.runner }}", job)
        self.assertIn("rid: linux-arm64", self.job("native-platforms"))
        self.assertIn("archive: linux.arm64", self.job("qualify-platforms"))
        native = self.job("native-apple")
        qualifier = self.job("qualify-apple")
        for job in (native, qualifier):
            self.assertIn("runs-on: [self-hosted, macOS, ARM64, eldritch-apple]", job)
            self.assertIn("if: needs.changes.outputs.full == 'true'", job)
        self.assertIn("rid: [osx-arm64, osx-x64]", native)
        self.assertIn('/usr/bin/lipo -verify_arch "$arch"', native)
        self.assertIn("architecture: [arm64, x86_64]", qualifier)
        self.assertIn('--architecture "${{ matrix.architecture }}"', qualifier)
        self.assertIn("scripts/apple-locked.py python3 scripts/qualify-stock-package.py", qualifier)

    def test_release_requires_full_tag_and_all_final_package_qualifiers(self):
        release = self.job("release")
        needs = re.search(r"^    needs: \[(.+)\]$", release, re.MULTILINE).group(1).split(", ")
        self.assertIn("github.event_name == 'push'", release)
        self.assertIn("startsWith(github.ref, 'refs/tags/v')", release)
        self.assertIn("needs.changes.outputs.full == 'true'", release)
        for name in ("package", "qualify-linux-x64", "qualify-platforms", "qualify-apple", "qualify-windows-proton"):
            self.assertIn(name, needs)
            self.assertIn(f"needs.{name}.result == 'success'", release)
        self.assertNotIn(" || ", release)
        self.assertNotIn("continue-on-error", release)
        self.assertIn("files: |\n            dist/${{ needs.package.outputs.name }}\n"
                      "            dist/${{ needs.package.outputs.runtime-name }}", release)
        self.assertIn("fail_on_unmatched_files: true", release)
        self.assertNotIn("project-runtime-package.py", release)
        self.assertNotIn("assemble-package.py", release)

    def test_every_qualifier_and_release_use_exact_artifact_id_and_digest(self):
        package = self.job("package")
        self.assertIn("artifact-id: ${{ steps.upload.outputs.artifact-id }}", package)
        self.assertIn("artifact-digest: ${{ steps.upload.outputs.artifact-digest }}", package)
        for name in ("qualify-linux-x64", "qualify-platforms", "qualify-apple", "qualify-windows-proton", "release"):
            with self.subTest(job=name):
                job = self.job(name)
                self.assertIn('scripts/download-package.py --artifact-id "${{ needs.package.outputs.artifact-id }}"', job)
                self.assertIn('--digest "${{ needs.package.outputs.artifact-digest }}"', job)
                self.assertIn('--name "${{ needs.package.outputs.name }}"', job)
                self.assertIn('--name "${{ needs.package.outputs.runtime-name }}"', job)
                self.assertIn("GH_TOKEN: ${{ github.token }}", job)
                self.assertNotIn("uses: actions/download-artifact", job)


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
