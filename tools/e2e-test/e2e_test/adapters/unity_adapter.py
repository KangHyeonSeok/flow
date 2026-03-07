"""Unity platform adapter for E2E test tool."""

from .base import PlatformAdapter, PlatformConfig


class UnityAdapter(PlatformAdapter):
    """E2E test adapter for Unity platform.

    Configures the E2E test environment for Unity applications.
    The Unity app must be built with FLOW_E2E_TESTS defined and the
    FlowE2E Unity package integrated.
    """

    @property
    def platform_name(self) -> str:
        return "unity"

    def get_config(self) -> PlatformConfig:
        return PlatformConfig(
            platform="unity",
            discovery_timeout=30.0,
            test_timeout=300.0,
        )

    def validate_environment(self) -> tuple[bool, list[str]]:
        """Check Unity E2E environment prerequisites."""
        # Unity E2E apps self-register via UDP beacon; no extra CLI tools needed.
        return True, []
