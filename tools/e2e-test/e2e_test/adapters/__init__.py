"""Platform adapters for E2E test tool.

Provides adapter classes that configure platform-specific E2E test environments
for Flutter and Unity projects.
"""

from .base import PlatformAdapter, PlatformConfig, get_adapter

__all__ = [
    "PlatformAdapter",
    "PlatformConfig",
    "get_adapter",
]
