"""Open the Git panel without starting TIA Portal.

Git commands run for real against the folder you pass. HMI and hardware
import and export stay unavailable until the panel is opened from TIA Portal.

Examples:
    python open_panel.py
    python open_panel.py "C:\\path\\to\\workspace"
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def main() -> int:
    root = Path(__file__).resolve().parent
    folder = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else root
    project = root / "src" / "TiaGitAddIn.Preview" / "TiaGitAddIn.Preview.csproj"
    command = [
        "dotnet",
        "run",
        "--project",
        str(project),
        "-p:EnableTiaAddInPackaging=false",
        "-p:AllowMissingStartdrive=true",
        "--",
        str(folder),
    ]
    completed = subprocess.run(command, cwd=root)
    return completed.returncode


if __name__ == "__main__":
    raise SystemExit(main())
