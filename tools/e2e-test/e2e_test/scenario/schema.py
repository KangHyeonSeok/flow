"""Scenario data models for E2E test scenarios.

Defines dataclasses for parsing and representing YAML test scenarios.
"""

from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Optional


class StepType(str, Enum):
    """Supported test step types."""
    INPUT = "input"
    CLICK = "click"
    SELECT = "select"
    WAIT = "wait"
    SCREENSHOT = "screenshot"


class AssertionType(str, Enum):
    """Supported assertion types."""
    SCREENSHOT = "screenshot"
    TEXT = "text"
    ELEMENT = "element"


VALID_STEP_TYPES = {e.value for e in StepType}
VALID_ASSERTION_TYPES = {e.value for e in AssertionType}
VALID_PLATFORMS = {"unity", "flutter"}


@dataclass
class ScenarioMeta:
    """Metadata for a test scenario."""
    app: str
    platform: str
    resolution: str = "1920x1080"
    timeout: int = 300
    description: str = ""

    def __post_init__(self):
        self.platform = self.platform.lower()


@dataclass
class Step:
    """A single test step."""
    type: str
    target: Optional[str] = None
    text: Optional[str] = None
    value: Optional[str] = None
    ms: Optional[int] = None
    description: Optional[str] = None

    def __post_init__(self):
        self.type = self.type.lower()


@dataclass
class Assertion:
    """A test assertion."""
    type: str
    name: str
    description: Optional[str] = None
    expected: Optional[str] = None

    def __post_init__(self):
        self.type = self.type.lower()


@dataclass
class Scenario:
    """A complete E2E test scenario."""
    meta: ScenarioMeta
    steps: list[Step] = field(default_factory=list)
    assertions: list[Assertion] = field(default_factory=list)

    @property
    def total_steps(self) -> int:
        """Total number of steps."""
        return len(self.steps)

    @property
    def total_assertions(self) -> int:
        """Total number of assertions."""
        return len(self.assertions)

    def to_dict(self) -> dict[str, Any]:
        """Convert scenario to dictionary for serialization."""
        return {
            "meta": {
                "app": self.meta.app,
                "platform": self.meta.platform,
                "resolution": self.meta.resolution,
                "timeout": self.meta.timeout,
                "description": self.meta.description,
            },
            "steps": [
                {k: v for k, v in step.__dict__.items() if v is not None}
                for step in self.steps
            ],
            "assert": [
                {k: v for k, v in a.__dict__.items() if v is not None}
                for a in self.assertions
            ],
        }


@dataclass
class ValidationError:
    """A single validation error."""
    path: str
    message: str
    severity: str = "error"  # "error" or "warning"


@dataclass
class ValidationResult:
    """Result of scenario validation."""
    valid: bool
    errors: list[ValidationError] = field(default_factory=list)
    warnings: list[ValidationError] = field(default_factory=list)

    @property
    def error_count(self) -> int:
        return len(self.errors)

    @property
    def warning_count(self) -> int:
        return len(self.warnings)

    def __str__(self) -> str:
        if self.valid:
            msg = "Valid"
            if self.warnings:
                msg += f" ({self.warning_count} warnings)"
            return msg
        return f"Invalid: {self.error_count} errors, {self.warning_count} warnings"
