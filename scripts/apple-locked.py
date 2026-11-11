#!/usr/bin/env python3
"""Run one foreground producer under the shared Apple workstation lock."""

import fcntl
import os
from pathlib import Path
import signal
import subprocess
import sys


def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: apple-locked.py COMMAND [ARG ...]")
    lock = Path.home() / ".local/state/eldritch-macos/producer.lock"
    lock.parent.mkdir(parents=True, exist_ok=True)
    with lock.open("a") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX)
        # The wrapper alone owns the descriptor: no detached lock-holder or leaked
        # lock in an SDK build server. Forward cancellation to the whole command group.
        pending = []
        child = None

        def forward(signum, _frame):
            pending.append(signum)
            if child is not None:
                try:
                    os.killpg(child.pid, signum)
                except ProcessLookupError:
                    pass

        for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(signum, forward)
        environment = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1")
        child = subprocess.Popen(sys.argv[1:], env=environment, start_new_session=True)
        for signum in pending:
            try:
                os.killpg(child.pid, signum)
            except ProcessLookupError:
                pass
        result = child.wait()
        return 128 + pending[-1] if pending else (128 - result if result < 0 else result)


if __name__ == "__main__":
    sys.exit(main())
