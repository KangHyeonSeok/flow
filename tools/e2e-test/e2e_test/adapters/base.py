"""Base platform adapter for E2E test tool."""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class PlatformConfig:
    """Platform-specific execution configuration."""
    platform: str
    discovery_timeout: float = 30.0
    test_timeout: float = 300.0
    extra_env: dict = field(default_factory=dict)


class PlatformAdapter(ABC):
    """Base class for platform-specific E2E test adapters.

    Platform adapters configure the E2E test environment for a specific
    platform (Flutter, Unity, etc.) before test execution begins.
    """

    @property
    @abstractmethod
    def platform_name(self) -> str:
        """Platform identifier string (flutter|unity)."""

    @abstractmethod
    def get_config(self) -> PlatformConfig:
        """Return platform-specific execution configuration."""

    def validate_environment(self) -> tuple[bool, list[str]]:
        """Verify the platform test environment is properly configured.

        Returns:
            Tuple of (is_valid, list_of_issues). is_valid is True if no
            blocking issues were found.
        """
        return True, []

    def on_test_start(self, scenario_name: str) -> None:
        """Called before a test scenario starts executing."""

    def on_test_end(self, scenario_name: str, passed: bool) -> None:
        """Called after a test scenario finishes executing."""


def get_adapter(platform: str) -> Optional[PlatformAdapter]:
    """Load a platform adapter by name.

    Args:
        platform: Platform identifier (flutter|unity).

    Returns:
        PlatformAdapter instance, or None if the platform is unknown.
    """
    from .flutter_adapter import FlutterAdapter
    from .unity_adapter import UnityAdapter

    registry: dict[str, type[PlatformAdapter]] = {
        "flutter": FlutterAdapter,
        "unity": UnityAdapter,
    }

    adapter_class = registry.get(platform.lower())
    if adapter_class is None:
        return None
    return adapter_class()
