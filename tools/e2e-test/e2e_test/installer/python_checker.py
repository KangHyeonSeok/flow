"""Python version checker for E2E test tool.

Finds and validates Python 3.12.x installation.
"""

import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Optional, Tuple

# Minimum required Python version
REQUIRED_MAJOR = 3
REQUIRED_MINOR = 12

DOWNLOAD_URL = "https://www.python.org/downloads/"
WINDOWS_INSTALL_CMD = (
    "python-3.12.x-amd64.exe /quiet InstallAllUsers=1 PrependPath=1"
)


def _parse_version(version_string: str) -> Optional[Tuple[int, int, int]]:
    """Parse 'Python X.Y.Z' into (major, minor, patch) tuple."""
    match = re.search(r"(\d+)\.(\d+)\.(\d+)", version_string)
    if match:
        return int(match.group(1)), int(match.group(2)), int(match.group(3))
    return None


def _try_python_command(cmd: str) -> Optional[Tuple[str, str, Tuple[int, int, int]]]:
    """Try running a python command and return (cmd, version_str, version_tuple) or None."""
    try:
        result = subprocess.run(
            [cmd, "--version"],
            capture_output=True,
            text=True,
            timeout=10,
        )
        output = result.stdout.strip() or result.stderr.strip()
        version_tuple = _parse_version(output)
        if version_tuple:
            return cmd, output, version_tuple
    except (FileNotFoundError, subprocess.TimeoutExpired, OSError):
        pass
    return None


def find_python() -> Tuple[Optional[str], Optional[str]]:
    """Find a Python 3.12.x executable on the system.

    Searches in order:
    1. FLOW_PYTHON_PATH environment variable
    2. Common Python command names (python, python3, python3.12)

    Returns:
        Tuple of (python_executable, version_string) or (None, None) if not found.
    """
    candidates = []

    # Check environment variable first
    env_python = os.environ.get("FLOW_PYTHON_PATH")
    if env_python:
        candidates.append(env_python)

    # Common command names
    if os.name == "nt":  # Windows
        candidates.extend(["python", "python3", "py -3.12", "py -3"])
    else:  # Unix/macOS
        candidates.extend(["python3.12", "python3", "python"])

    for cmd in candidates:
        result = _try_python_command(cmd)
        if result is None:
            continue

        cmd, version_str, version_tuple = result
        major, minor, _ = version_tuple

        if major == REQUIRED_MAJOR and minor == REQUIRED_MINOR:
            return cmd, version_str

    return None, None


def check_python_version() -> Tuple[str, str]:
    """Check if Python 3.12.x is installed and return executable info.

    Returns:
        Tuple of (python_executable, version_string).

    Raises:
        RuntimeError: If Python 3.12.x is not found.
    """
    python_exe, version = find_python()

    if not python_exe:
        raise RuntimeError(
            f"Python {REQUIRED_MAJOR}.{REQUIRED_MINOR} not found.\n"
            f"Please install from {DOWNLOAD_URL}\n"
            f"Or set FLOW_PYTHON_PATH environment variable to your Python 3.12 executable."
        )

    return python_exe, version


def get_python_info() -> dict:
    """Get comprehensive Python environment information.

    Returns:
        Dictionary with python executable path, version, and status.
    """
    try:
        python_exe, version = check_python_version()
        return {
            "found": True,
            "executable": python_exe,
            "version": version,
            "error": None,
        }
    except RuntimeError as e:
        return {
            "found": False,
            "executable": None,
            "version": None,
            "error": str(e),
        }


if __name__ == "__main__":
    info = get_python_info()
    if info["found"]:
        print(f"✓ Found: {info['executable']} ({info['version']})")
    else:
        print(f"✗ {info['error']}")
        sys.exit(1)
