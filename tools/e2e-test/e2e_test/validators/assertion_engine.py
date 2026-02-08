"""Assertion engine for evaluating test assertions.

Combines VLM validation results with assertion definitions.
"""

from dataclasses import dataclass, field
from typing import Optional

from ..scenario.schema import Assertion
from .gemini_vlm import GeminiValidator, VLMValidationResult


@dataclass
class AssertionResult:
    """Result of a single assertion evaluation."""
    assertion: Assertion
    passed: bool
    details: str
    vlm_result: Optional[VLMValidationResult] = None


@dataclass
class AssertionReport:
    """Report of all assertion evaluations."""
    results: list[AssertionResult] = field(default_factory=list)

    @property
    def all_passed(self) -> bool:
        return all(r.passed for r in self.results)

    @property
    def passed_count(self) -> int:
        return sum(1 for r in self.results if r.passed)

    @property
    def failed_count(self) -> int:
        return sum(1 for r in self.results if not r.passed)

    @property
    def total_count(self) -> int:
        return len(self.results)


class AssertionEngine:
    """Evaluates test assertions using available validators."""

    def __init__(self, gemini_validator: Optional[GeminiValidator] = None):
        """Initialize assertion engine.

        Args:
            gemini_validator: Gemini VLM validator for screenshot assertions.
                             If None, screenshot assertions will fail with message.
        """
        self.gemini_validator = gemini_validator

    def evaluate(
        self,
        assertions: list[Assertion],
        screenshots: dict[str, str],  # name -> base64 data
    ) -> AssertionReport:
        """Evaluate all assertions against test results.

        Args:
            assertions: List of assertions from the scenario.
            screenshots: Dict mapping screenshot names to base64 data.

        Returns:
            AssertionReport with individual results.
        """
        report = AssertionReport()

        for assertion in assertions:
            if assertion.type == "screenshot":
                result = self._evaluate_screenshot(assertion, screenshots)
            else:
                result = AssertionResult(
                    assertion=assertion,
                    passed=False,
                    details=f"Unsupported assertion type: {assertion.type}",
                )

            report.results.append(result)

        return report

    def _evaluate_screenshot(
        self,
        assertion: Assertion,
        screenshots: dict[str, str],
    ) -> AssertionResult:
        """Evaluate a screenshot assertion."""
        # Find matching screenshot
        screenshot_data = screenshots.get(assertion.name)

        if screenshot_data is None:
            # Try finding by partial match
            for name, data in screenshots.items():
                if assertion.name in name:
                    screenshot_data = data
                    break

        if screenshot_data is None:
            return AssertionResult(
                assertion=assertion,
                passed=False,
                details=f"Screenshot '{assertion.name}' not found in results.",
            )

        if self.gemini_validator is None:
            return AssertionResult(
                assertion=assertion,
                passed=False,
                details="No VLM validator configured. Cannot validate screenshots.",
            )

        expected = assertion.description or assertion.name
        vlm_result = self.gemini_validator.validate_screenshot(
            screenshot_data, expected
        )

        return AssertionResult(
            assertion=assertion,
            passed=vlm_result.passed,
            details=vlm_result.reason,
            vlm_result=vlm_result,
        )
