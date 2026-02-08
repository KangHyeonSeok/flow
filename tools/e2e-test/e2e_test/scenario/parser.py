"""YAML scenario parser for E2E test tool.

Parses YAML test scenario files into Scenario dataclass objects.
"""

from pathlib import Path
from typing import Union

import yaml

from .schema import Assertion, Scenario, ScenarioMeta, Step


def parse_scenario(file_path: Union[str, Path]) -> Scenario:
    """Parse a YAML scenario file into a Scenario object.

    Args:
        file_path: Path to the YAML scenario file.

    Returns:
        Parsed Scenario object.

    Raises:
        FileNotFoundError: If the scenario file doesn't exist.
        ValueError: If the YAML is malformed or missing required fields.
    """
    file_path = Path(file_path)

    if not file_path.exists():
        raise FileNotFoundError(f"Scenario file not found: {file_path}")

    if not file_path.suffix in (".yaml", ".yml"):
        raise ValueError(f"Expected .yaml or .yml file, got: {file_path.suffix}")

    with open(file_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    if data is None:
        raise ValueError(f"Empty scenario file: {file_path}")

    return parse_scenario_data(data, source=str(file_path))


def parse_scenario_data(data: dict, source: str = "<inline>") -> Scenario:
    """Parse a scenario from a dictionary (already loaded YAML).

    Args:
        data: Dictionary with scenario data.
        source: Source identifier for error messages.

    Returns:
        Parsed Scenario object.

    Raises:
        ValueError: If required fields are missing or malformed.
    """
    if not isinstance(data, dict):
        raise ValueError(f"Scenario must be a YAML mapping, got {type(data).__name__}")

    # Parse meta
    if "meta" not in data:
        raise ValueError(f"Missing required field 'meta' in {source}")

    meta_data = data["meta"]
    if not isinstance(meta_data, dict):
        raise ValueError(f"'meta' must be a mapping in {source}")

    _require_fields(meta_data, ["app", "platform"], "meta", source)
    meta = ScenarioMeta(**{
        k: v for k, v in meta_data.items()
        if k in ScenarioMeta.__dataclass_fields__
    })

    # Parse steps
    steps_data = data.get("steps", [])
    if not isinstance(steps_data, list):
        raise ValueError(f"'steps' must be a list in {source}")

    steps = []
    for i, step_data in enumerate(steps_data):
        if not isinstance(step_data, dict):
            raise ValueError(f"Step {i} must be a mapping in {source}")
        _require_fields(step_data, ["type"], f"steps[{i}]", source)
        step = Step(**{
            k: v for k, v in step_data.items()
            if k in Step.__dataclass_fields__
        })
        steps.append(step)

    # Parse assertions
    assert_data = data.get("assert", [])
    if not isinstance(assert_data, list):
        raise ValueError(f"'assert' must be a list in {source}")

    assertions = []
    for i, a_data in enumerate(assert_data):
        if not isinstance(a_data, dict):
            raise ValueError(f"Assertion {i} must be a mapping in {source}")
        _require_fields(a_data, ["type", "name"], f"assert[{i}]", source)
        assertion = Assertion(**{
            k: v for k, v in a_data.items()
            if k in Assertion.__dataclass_fields__
        })
        assertions.append(assertion)

    return Scenario(meta=meta, steps=steps, assertions=assertions)


def _require_fields(
    data: dict, fields: list[str], context: str, source: str
) -> None:
    """Check that required fields exist in a dictionary."""
    for field_name in fields:
        if field_name not in data:
            raise ValueError(
                f"Missing required field '{field_name}' in {context} ({source})"
            )
