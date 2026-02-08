"""Test executor - orchestrates E2E test execution.

Coordinates the full test flow:
1. Parse scenario
2. Discover target app (UDP)
3. Send scenario (HTTP)
4. Poll for completion
5. Collect results
6. Validate with VLM
7. Generate report
"""

import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from ..discovery.udp_listener import AppInfo, UDPListener
from ..reporting.json_reporter import JsonReporter
from ..scenario.schema import Scenario
from ..transport.http_client import E2EHttpClient, TestResult as HttpTestResult
from ..validators.assertion_engine import AssertionEngine, AssertionReport
from ..validators.gemini_vlm import GeminiValidator


@dataclass
class ExecutionConfig:
    """Configuration for test execution."""
    discovery_timeout: float = 30.0
    test_timeout: float = 300.0
    poll_interval: float = 2.0
    save_report: bool = False
    report_dir: Optional[Path] = None
    pretty_output: bool = True


@dataclass
class ExecutionResult:
    """Complete result of a test execution."""
    scenario_name: str
    platform: str
    all_passed: bool = False
    assertion_report: Optional[AssertionReport] = None
    duration_ms: int = 0
    logs: list[dict] = field(default_factory=list)
    screenshots_saved: list[str] = field(default_factory=list)
    error: Optional[str] = None
    report_path: Optional[str] = None

    def to_flow_json(self) -> dict:
        """Convert to flow CLI compatible JSON output."""
        reporter = JsonReporter()
        report = reporter.generate(
            scenario_name=self.scenario_name,
            platform=self.platform,
            assertion_report=self.assertion_report or AssertionReport(),
            duration_ms=self.duration_ms,
            logs=self.logs,
            screenshots_saved=self.screenshots_saved,
            error=self.error,
        )
        return reporter.generate_flow_output(report, self.report_path)


class TestExecutor:
    """Orchestrates E2E test execution.

    Coordinates discovery, HTTP communication, VLM validation,
    and report generation into a single test execution flow.
    """

    def __init__(
        self,
        scenario: Scenario,
        config: Optional[ExecutionConfig] = None,
        gemini_validator: Optional[GeminiValidator] = None,
        app_info: Optional[AppInfo] = None,
    ):
        """Initialize test executor.

        Args:
            scenario: Parsed test scenario.
            config: Execution configuration.
            gemini_validator: VLM validator (None = skip VLM validation).
            app_info: Pre-discovered app info (skip UDP discovery if provided).
        """
        self.scenario = scenario
        self.config = config or ExecutionConfig()
        self.gemini_validator = gemini_validator
        self.app_info = app_info
        self._reporter = JsonReporter()

    def execute(self) -> ExecutionResult:
        """Execute the full test flow.

        Returns:
            ExecutionResult with all test outcomes.
        """
        start_time = time.time()
        result = ExecutionResult(
            scenario_name=self.scenario.meta.app,
            platform=self.scenario.meta.platform,
        )

        try:
            # Step 1: Discover target app
            app_info = self._discover_app()
            print(f"Connected to: {app_info}")

            # Step 2: Set up HTTP client
            with E2EHttpClient(app_info.base_url) as client:
                # Step 3: Submit scenario
                print(f"Submitting scenario: {self.scenario.meta.app}")
                session = client.post_run(self.scenario.to_dict())
                print(f"Session started: {session.session_id}")

                # Step 4: Poll until complete and get results
                print("Waiting for test completion...")
                http_result = client.poll_until_complete(
                    session.session_id,
                    timeout=self.config.test_timeout,
                    poll_interval=self.config.poll_interval,
                    on_progress=self._on_progress,
                )

            # Step 5: Collect logs
            result.logs = [
                {
                    "timestamp": log.timestamp,
                    "level": log.level,
                    "message": log.message,
                }
                for log in http_result.logs
            ]

            # Step 6: Validate with VLM
            screenshots_dict = {
                ss.name: ss.data for ss in http_result.screenshots
            }

            assertion_engine = AssertionEngine(self.gemini_validator)
            assertion_report = assertion_engine.evaluate(
                self.scenario.assertions, screenshots_dict
            )

            result.assertion_report = assertion_report
            result.all_passed = assertion_report.all_passed

            # Step 7: Report
            self._print_assertion_summary(assertion_report)

        except TimeoutError as e:
            result.error = f"Timeout: {str(e)}"
            print(f"ERROR: {result.error}")

        except ConnectionError as e:
            result.error = f"Connection failed: {str(e)}"
            print(f"ERROR: {result.error}")

        except RuntimeError as e:
            result.error = str(e)
            print(f"ERROR: {result.error}")

        except Exception as e:
            result.error = f"Unexpected error: {type(e).__name__}: {str(e)}"
            print(f"ERROR: {result.error}")

        finally:
            result.duration_ms = int((time.time() - start_time) * 1000)

        # Save report if configured
        if self.config.save_report:
            result.report_path = self._save_report(result)

        return result

    def _discover_app(self) -> AppInfo:
        """Discover or use pre-configured target app."""
        if self.app_info:
            return self.app_info

        listener = UDPListener()
        return listener.listen(
            timeout=self.config.discovery_timeout,
            platform_filter=self.scenario.meta.platform,
        )

    def _on_progress(self, status) -> None:
        """Callback for test progress updates."""
        if status.total_steps > 0:
            pct = int(status.progress * 100)
            print(f"  Progress: {pct}% ({status.current_step}/{status.total_steps})")

    def _print_assertion_summary(self, report: AssertionReport) -> None:
        """Print assertion results summary."""
        print(f"\nResults: {report.passed_count}/{report.total_count} passed")
        for r in report.results:
            status = "PASS" if r.passed else "FAIL"
            print(f"  [{status}] {r.assertion.name}: {r.details}")

    def _save_report(self, result: ExecutionResult) -> Optional[str]:
        """Save test report to file."""
        try:
            report_dir = self.config.report_dir or Path(".")
            report_path = report_dir / f"e2e_report_{self.scenario.meta.app}.json"

            report = self._reporter.generate(
                scenario_name=result.scenario_name,
                platform=result.platform,
                assertion_report=result.assertion_report or AssertionReport(),
                duration_ms=result.duration_ms,
                logs=result.logs,
                screenshots_saved=result.screenshots_saved,
                error=result.error,
            )

            saved_path = self._reporter.save(report, report_path)
            print(f"Report saved: {saved_path}")
            return str(saved_path)

        except Exception as e:
            print(f"Warning: Failed to save report: {e}")
            return None
