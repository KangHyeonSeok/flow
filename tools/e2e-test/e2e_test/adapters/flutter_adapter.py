"""Flutter platform adapter for E2E test tool."""

import subprocess

from .base import PlatformAdapter, PlatformConfig


class FlutterAdapter(PlatformAdapter):
    """E2E test adapter for Flutter platform.

    Configures the E2E test environment for Flutter applications.
    The Flutter app must be started with E2E_TESTS=true and the
    flow_e2e_flutter adapter package integrated.
    """

    @property
    def platform_name(self) -> str:
        return "flutter"

    def get_config(self) -> PlatformConfig:
        return PlatformConfig(
            platform="flutter",
            discovery_timeout=30.0,
            test_timeout=300.0,
        )

    def validate_environment(self) -> tuple[bool, list[str]]:
        """Check Flutter E2E environment prerequisites."""
        issues = []
        if not self._is_flutter_available():
            issues.append(
                "Flutter SDK not found in PATH. "
                "Install Flutter from https://flutter.dev/docs/get-started/install"
            )
        return len(issues) == 0, issues

    def _is_flutter_available(self) -> bool:
        """Check if the flutter CLI is available."""
        try:
            result = subprocess.run(
                ["flutter", "--version"],
                capture_output=True,
                timeout=5,
            )
            return result.returncode == 0
        except Exception:
            return False
