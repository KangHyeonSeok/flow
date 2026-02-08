"""Scenario validator for E2E test tool.

Validates parsed Scenario objects against business rules.
"""

from .schema import (
    Assertion,
    Scenario,
    Step,
    ValidationError,
    ValidationResult,
    VALID_ASSERTION_TYPES,
    VALID_PLATFORMS,
    VALID_STEP_TYPES,
)


def validate_scenario(scenario: Scenario) -> ValidationResult:
    """Validate a parsed Scenario object.

    Checks:
    - Meta fields (platform, resolution format)
    - Step types and required fields per type
    - Assertion types and required fields

    Args:
        scenario: Parsed Scenario to validate.

    Returns:
        ValidationResult with errors and warnings.
    """
    errors: list[ValidationError] = []
    warnings: list[ValidationError] = []

    # Validate meta
    _validate_meta(scenario, errors, warnings)

    # Validate steps
    _validate_steps(scenario, errors, warnings)

    # Validate assertions
    _validate_assertions(scenario, errors, warnings)

    # Warn if no assertions
    if not scenario.assertions:
        warnings.append(ValidationError(
            path="assert",
            message="No assertions defined. Test will pass without validation.",
            severity="warning",
        ))

    # Warn if no steps
    if not scenario.steps:
        warnings.append(ValidationError(
            path="steps",
            message="No steps defined.",
            severity="warning",
        ))

    return ValidationResult(
        valid=len(errors) == 0,
        errors=errors,
        warnings=warnings,
    )


def _validate_meta(
    scenario: Scenario,
    errors: list[ValidationError],
    warnings: list[ValidationError],
) -> None:
    """Validate scenario metadata."""
    meta = scenario.meta

    if not meta.app:
        errors.append(ValidationError(
            path="meta.app",
            message="'app' is required and must not be empty.",
        ))

    if meta.platform not in VALID_PLATFORMS:
        errors.append(ValidationError(
            path="meta.platform",
            message=f"Invalid platform '{meta.platform}'. Must be one of: {', '.join(sorted(VALID_PLATFORMS))}",
        ))

    # Validate resolution format (WxH)
    if meta.resolution:
        parts = meta.resolution.lower().split("x")
        if len(parts) != 2:
            errors.append(ValidationError(
                path="meta.resolution",
                message=f"Invalid resolution format '{meta.resolution}'. Expected 'WIDTHxHEIGHT' (e.g., '1920x1080').",
            ))
        else:
            try:
                w, h = int(parts[0]), int(parts[1])
                if w <= 0 or h <= 0:
                    raise ValueError()
            except ValueError:
                errors.append(ValidationError(
                    path="meta.resolution",
                    message=f"Invalid resolution '{meta.resolution}'. Width and height must be positive integers.",
                ))

    if meta.timeout <= 0:
        errors.append(ValidationError(
            path="meta.timeout",
            message=f"Timeout must be positive, got {meta.timeout}.",
        ))


def _validate_steps(
    scenario: Scenario,
    errors: list[ValidationError],
    warnings: list[ValidationError],
) -> None:
    """Validate scenario steps."""
    for i, step in enumerate(scenario.steps):
        path = f"steps[{i}]"

        # Check step type
        if step.type not in VALID_STEP_TYPES:
            errors.append(ValidationError(
                path=f"{path}.type",
                message=f"Invalid step type '{step.type}'. Must be one of: {', '.join(sorted(VALID_STEP_TYPES))}",
            ))
            continue

        # Type-specific validation
        if step.type == "input":
            if not step.target:
                errors.append(ValidationError(
                    path=f"{path}.target",
                    message="'input' step requires 'target'.",
                ))
            if step.text is None:
                errors.append(ValidationError(
                    path=f"{path}.text",
                    message="'input' step requires 'text'.",
                ))

        elif step.type == "click":
            if not step.target:
                errors.append(ValidationError(
                    path=f"{path}.target",
                    message="'click' step requires 'target'.",
                ))

        elif step.type == "select":
            if not step.target:
                errors.append(ValidationError(
                    path=f"{path}.target",
                    message="'select' step requires 'target'.",
                ))
            if step.value is None:
                errors.append(ValidationError(
                    path=f"{path}.value",
                    message="'select' step requires 'value'.",
                ))

        elif step.type == "wait":
            if step.ms is None:
                errors.append(ValidationError(
                    path=f"{path}.ms",
                    message="'wait' step requires 'ms'.",
                ))
            elif step.ms <= 0:
                errors.append(ValidationError(
                    path=f"{path}.ms",
                    message=f"'wait' ms must be positive, got {step.ms}.",
                ))

        elif step.type == "screenshot":
            if not step.target and not step.description:
                warnings.append(ValidationError(
                    path=path,
                    message="'screenshot' step has no target or description.",
                    severity="warning",
                ))


def _validate_assertions(
    scenario: Scenario,
    errors: list[ValidationError],
    warnings: list[ValidationError],
) -> None:
    """Validate scenario assertions."""
    for i, assertion in enumerate(scenario.assertions):
        path = f"assert[{i}]"

        if assertion.type not in VALID_ASSERTION_TYPES:
            errors.append(ValidationError(
                path=f"{path}.type",
                message=f"Invalid assertion type '{assertion.type}'. Must be one of: {', '.join(sorted(VALID_ASSERTION_TYPES))}",
            ))

        if not assertion.name:
            errors.append(ValidationError(
                path=f"{path}.name",
                message="Assertion 'name' is required and must not be empty.",
            ))

        if assertion.type == "screenshot" and not assertion.description:
            warnings.append(ValidationError(
                path=f"{path}.description",
                message="Screenshot assertion without 'description' limits VLM validation accuracy.",
                severity="warning",
            ))
