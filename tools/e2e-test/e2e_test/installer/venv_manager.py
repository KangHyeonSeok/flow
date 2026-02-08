"""Virtual environment manager for E2E test tool.

Manages creation, validation, and dependency installation for
the ~/.flow/.venv virtual environment.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Optional

from .python_checker import check_python_version


class VenvManager:
    """Manages the Python virtual environment for E2E testing."""

    DEFAULT_VENV_DIR = ".venv"
    FLOW_HOME_DIR = ".flow"

    def __init__(self, venv_path: Optional[Path] = None):
        """Initialize VenvManager.

        Args:
            venv_path: Custom venv path. Defaults to ~/.flow/.venv
        """
        if venv_path:
            self.venv_path = Path(venv_path)
        else:
            self.venv_path = Path.home() / self.FLOW_HOME_DIR / self.DEFAULT_VENV_DIR

    @property
    def python_exe(self) -> Path:
        """Get the venv Python executable path."""
        if os.name == "nt":  # Windows
            return self.venv_path / "Scripts" / "python.exe"
        else:  # Unix/macOS
            return self.venv_path / "bin" / "python"

    @property
    def pip_exe(self) -> Path:
        """Get the venv pip executable path."""
        if os.name == "nt":  # Windows
            return self.venv_path / "Scripts" / "pip.exe"
        else:  # Unix/macOS
            return self.venv_path / "bin" / "pip"

    def exists(self) -> bool:
        """Check if venv directory exists."""
        return self.venv_path.exists()

    def is_valid(self) -> bool:
        """Check if venv is valid and has correct Python version."""
        if not self.python_exe.exists():
            return False

        try:
            result = subprocess.run(
                [str(self.python_exe), "--version"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            output = result.stdout.strip() or result.stderr.strip()
            return "3.12." in output
        except (subprocess.TimeoutExpired, OSError):
            return False

    def create(self, force: bool = False) -> bool:
        """Create a new virtual environment.

        Args:
            force: If True, remove existing venv and recreate.

        Returns:
            True if venv was created successfully.

        Raises:
            RuntimeError: If Python 3.12 is not found or venv creation fails.
        """
        if self.exists():
            if force:
                print(f"Removing existing venv at {self.venv_path}...")
                shutil.rmtree(self.venv_path)
            elif self.is_valid():
                print(f"Valid venv already exists at {self.venv_path}")
                return True
            else:
                print(f"Invalid venv at {self.venv_path}, recreating...")
                shutil.rmtree(self.venv_path)

        python_exe, version = check_python_version()
        print(f"Using {version}")
        print(f"Creating venv at {self.venv_path}...")

        # Ensure parent directory exists
        self.venv_path.parent.mkdir(parents=True, exist_ok=True)

        try:
            subprocess.run(
                [python_exe, "-m", "venv", str(self.venv_path)],
                check=True,
                capture_output=True,
                text=True,
            )
        except subprocess.CalledProcessError as e:
            raise RuntimeError(
                f"Failed to create venv: {e.stderr or e.stdout}"
            ) from e

        # Validate the newly created venv
        if not self.is_valid():
            raise RuntimeError(
                f"Venv was created but validation failed at {self.venv_path}"
            )

        print(f"✓ Venv created at {self.venv_path}")
        return True

    def ensure(self) -> bool:
        """Ensure venv exists and is valid. Creates if needed.

        Returns:
            True if venv is ready.
        """
        if self.exists() and self.is_valid():
            return True
        return self.create()

    def install_dependencies(self, requirements_file: Path) -> bool:
        """Install dependencies from requirements.txt into the venv.

        Args:
            requirements_file: Path to requirements.txt file.

        Returns:
            True if installation was successful.

        Raises:
            FileNotFoundError: If requirements file doesn't exist.
            RuntimeError: If venv doesn't exist or installation fails.
        """
        requirements_file = Path(requirements_file)

        if not requirements_file.exists():
            raise FileNotFoundError(
                f"Requirements file not found: {requirements_file}"
            )

        if not self.is_valid():
            raise RuntimeError(
                f"Venv is not valid at {self.venv_path}. "
                "Run ensure() first."
            )

        print(f"Installing dependencies from {requirements_file}...")

        try:
            # Upgrade pip first
            subprocess.run(
                [str(self.python_exe), "-m", "pip", "install", "--upgrade", "pip"],
                check=True,
                capture_output=True,
                text=True,
            )

            # Install requirements
            subprocess.run(
                [
                    str(self.pip_exe),
                    "install",
                    "-r",
                    str(requirements_file),
                ],
                check=True,
                capture_output=True,
                text=True,
            )
        except subprocess.CalledProcessError as e:
            raise RuntimeError(
                f"Failed to install dependencies: {e.stderr or e.stdout}"
            ) from e

        print("✓ Dependencies installed")
        return True

    def run_in_venv(self, args: list[str], **kwargs) -> subprocess.CompletedProcess:
        """Run a command using the venv's Python.

        Args:
            args: Command arguments (after python).
            **kwargs: Additional arguments passed to subprocess.run.

        Returns:
            CompletedProcess result.
        """
        if not self.is_valid():
            raise RuntimeError(
                f"Venv is not valid at {self.venv_path}. "
                "Run ensure() first."
            )

        cmd = [str(self.python_exe)] + args
        return subprocess.run(cmd, **kwargs)

    def get_installed_packages(self) -> dict[str, str]:
        """Get dictionary of installed packages and their versions.

        Returns:
            Dict mapping package name to version string.
        """
        if not self.is_valid():
            return {}

        try:
            result = subprocess.run(
                [str(self.pip_exe), "list", "--format=json"],
                capture_output=True,
                text=True,
                timeout=30,
            )
            if result.returncode == 0:
                import json
                packages = json.loads(result.stdout)
                return {pkg["name"]: pkg["version"] for pkg in packages}
        except (subprocess.TimeoutExpired, OSError, Exception):
            pass

        return {}

    def get_info(self) -> dict:
        """Get comprehensive venv information.

        Returns:
            Dictionary with venv status details.
        """
        return {
            "path": str(self.venv_path),
            "exists": self.exists(),
            "valid": self.is_valid(),
            "python_exe": str(self.python_exe),
            "pip_exe": str(self.pip_exe),
        }


def setup_environment(
    requirements_file: Optional[Path] = None,
    venv_path: Optional[Path] = None,
) -> VenvManager:
    """One-shot environment setup: ensure venv + install deps.

    Args:
        requirements_file: Path to requirements.txt. If None, auto-detects.
        venv_path: Custom venv path. Defaults to ~/.flow/.venv.

    Returns:
        Configured VenvManager instance.
    """
    manager = VenvManager(venv_path=venv_path)
    manager.ensure()

    if requirements_file is None:
        # Auto-detect requirements.txt from project root
        project_root = Path(__file__).parent.parent.parent
        requirements_file = project_root / "requirements.txt"

    if requirements_file.exists():
        manager.install_dependencies(requirements_file)

    return manager


if __name__ == "__main__":
    manager = VenvManager()
    info = manager.get_info()
    print(f"Venv path: {info['path']}")
    print(f"Exists: {info['exists']}")
    print(f"Valid: {info['valid']}")

    if "--setup" in sys.argv:
        setup_environment()
