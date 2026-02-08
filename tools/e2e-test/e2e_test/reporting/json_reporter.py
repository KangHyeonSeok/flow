"""JSON report generator for E2E test results.

Generates structured JSON reports from test execution results.
"""

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

from ..validators.assertion_engine import AssertionReport


class JsonReporter:
    """Generates JSON reports from E2E test results."""

    def generate(
        self,
        scenario_name: str,
        platform: str,
        assertion_report: AssertionReport,
        duration_ms: int = 0,
        logs: Optional[list[dict]] = None,
        screenshots_saved: Optional[list[str]] = None,
        error: Optional[str] = None,
    ) -> dict[str, Any]:
        """Generate a JSON report from test results.

        Args:
            scenario_name: Name of the test scenario.
            platform: Target platform (unity/flutter).
            assertion_report: Results of assertion evaluation.
            duration_ms: Test duration in milliseconds.
            logs: Test execution logs.
            screenshots_saved: List of saved screenshot paths.
            error: Overall error message if test failed.

        Returns:
            Report dictionary ready for JSON serialization.
        """
        all_passed = assertion_report.all_passed if assertion_report.results else (error is None)

        report = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "scenario": scenario_name,
            "platform": platform,
            "status": "passed" if all_passed else "failed",
            "summary": {
                "total": assertion_report.total_count,
                "passed": assertion_report.passed_count,
                "failed": assertion_report.failed_count,
                "duration_ms": duration_ms,
            },
            "assertions": [
                {
                    "name": r.assertion.name,
                    "type": r.assertion.type,
                    "status": "pass" if r.passed else "fail",
                    "confidence": r.vlm_result.confidence if r.vlm_result else None,
                    "reason": r.details,
                    "warning": r.vlm_result.warning if r.vlm_result else None,
                }
                for r in assertion_report.results
            ],
            "logs": logs or [],
            "screenshots": screenshots_saved or [],
            "error": error,
        }

        return report

    def save(self, report: dict[str, Any], path: Path) -> Path:
        """Save report to a JSON file.

        Args:
            report: Report dictionary.
            path: Output file path.

        Returns:
            Path to the saved file.
        """
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)

        with open(path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)

        return path

    def to_json_string(self, report: dict[str, Any], pretty: bool = True) -> str:
        """Convert report to JSON string.

        Args:
            report: Report dictionary.
            pretty: If True, format with indentation.

        Returns:
            JSON string.
        """
        if pretty:
            return json.dumps(report, indent=2, ensure_ascii=False)
        return json.dumps(report, ensure_ascii=False)

    def generate_flow_output(
        self,
        report: dict[str, Any],
        report_path: Optional[str] = None,
    ) -> dict[str, Any]:
        """Generate flow CLI compatible JSON output.

        Follows the flow JSON output standard:
        {
            "success": bool,
            "command": "test",
            "data": { ... },
            "message": str
        }

        Args:
            report: Test report dictionary.
            report_path: Path where report was saved.

        Returns:
            Flow-compatible JSON output.
        """
        summary = report["summary"]
        all_passed = report["status"] == "passed"

        data: dict[str, Any] = {
            "scenario": report["scenario"],
            "total_tests": summary["total"],
            "passed": summary["passed"],
            "failed": summary["failed"],
            "duration_ms": summary["duration_ms"],
        }

        if report_path:
            data["report_path"] = report_path

        if not all_passed and report.get("error"):
            message = f"Test failed: {report['error']}"
        elif not all_passed:
            message = f"{summary['failed']} of {summary['total']} assertions failed"
        else:
            message = "All tests passed"

        return {
            "success": all_passed,
            "command": "test",
            "data": data,
            "message": message,
        }
