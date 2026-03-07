"""YAML scenario discovery for E2E test tool.

Scans directories to auto-discover YAML test scenario files.
"""

from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Union

from .parser import parse_scenario
from .schema import Scenario, ValidationResult
from .validator import validate_scenario


@dataclass
class DiscoveredScenario:
    """A discovered and optionally parsed scenario file."""
    path: Path
    scenario: Optional[Scenario] = None
    validation: Optional[ValidationResult] = None
    error: Optional[str] = None

    @property
    def is_valid(self) -> bool:
        """True if the scenario was parsed and validated successfully."""
        return (
            self.scenario is not None
            and self.validation is not None
            and self.validation.valid
        )


class ScenarioDiscovery:
    """Discovers YAML scenario files in a directory tree."""

    def __init__(self, recursive: bool = True):
        """Initialize scenario discovery.

        Args:
            recursive: If True, scan subdirectories recursively.
        """
        self.recursive = recursive

    def discover(self, directory: Union[str, Path]) -> list[DiscoveredScenario]:
        """Discover all YAML scenario files in a directory.

        Args:
            directory: Root directory to scan for scenario files.

        Returns:
            List of DiscoveredScenario objects (including files with parse errors).

        Raises:
            NotADirectoryError: If the path is not a directory.
        """
        directory = Path(directory)
        if not directory.is_dir():
            raise NotADirectoryError(f"Not a directory: {directory}")

        yaml_files = self._find_yaml_files(directory)
        return [self._parse_file(p) for p in sorted(yaml_files)]

    def discover_valid(self, directory: Union[str, Path]) -> list[DiscoveredScenario]:
        """Discover and return only valid (parseable + valid) scenarios.

        Args:
            directory: Root directory to scan.

        Returns:
            List of valid DiscoveredScenario objects.
        """
        return [s for s in self.discover(directory) if s.is_valid]

    def _find_yaml_files(self, directory: Path) -> list[Path]:
        """Find all .yaml/.yml files in a directory."""
        pattern = "**/*.yaml" if self.recursive else "*.yaml"
        files = list(directory.glob(pattern))
        pattern_yml = "**/*.yml" if self.recursive else "*.yml"
        files.extend(directory.glob(pattern_yml))
        return files

    def _parse_file(self, file_path: Path) -> DiscoveredScenario:
        """Parse and validate a single YAML file."""
        try:
            scenario = parse_scenario(file_path)
            validation = validate_scenario(scenario)
            return DiscoveredScenario(
                path=file_path,
                scenario=scenario,
                validation=validation,
            )
        except Exception as e:
            return DiscoveredScenario(path=file_path, error=str(e))


def discover_scenarios(
    directory: Union[str, Path],
    recursive: bool = True,
) -> list[DiscoveredScenario]:
    """Discover YAML scenario files in a directory.

    Args:
        directory: Directory to scan.
        recursive: If True, scan subdirectories recursively.

    Returns:
        List of DiscoveredScenario objects.
    """
    return ScenarioDiscovery(recursive=recursive).discover(directory)
