"""HTTP client for communicating with E2E test target apps.

Implements the HTTP transport protocol:
- POST /e2e/run   - Submit test scenario
- GET /e2e/status  - Poll test status
- GET /e2e/result  - Retrieve test results
"""

import time
from dataclasses import dataclass, field
from typing import Any, Optional

import requests

from .retry_policy import RetryPolicy, default_retry_policy


@dataclass
class TestSession:
    """Active test session info."""
    session_id: str
    status: str = "submitted"


@dataclass
class TestStatus:
    """Test execution status."""
    status: str
    progress: float = 0.0
    current_step: int = 0
    total_steps: int = 0

    @property
    def is_running(self) -> bool:
        return self.status == "running"

    @property
    def is_completed(self) -> bool:
        return self.status == "completed"

    @property
    def is_failed(self) -> bool:
        return self.status == "failed"


@dataclass
class ScreenshotData:
    """Screenshot captured during test."""
    name: str
    data: str  # base64 encoded


@dataclass
class LogEntry:
    """Log entry from test execution."""
    timestamp: str
    level: str
    message: str


@dataclass
class TestResult:
    """Complete test result."""
    status: str
    screenshots: list[ScreenshotData] = field(default_factory=list)
    logs: list[LogEntry] = field(default_factory=list)
    error: Optional[str] = None

    @property
    def has_screenshots(self) -> bool:
        return len(self.screenshots) > 0


class E2EHttpClient:
    """HTTP client for E2E test target app communication.

    Communicates with the target app's embedded HTTP server
    that is activated when E2E_TESTS build flag is enabled.
    """

    def __init__(
        self,
        base_url: str,
        retry_policy: Optional[RetryPolicy] = None,
        request_timeout: float = 30.0,
    ):
        """Initialize HTTP client.

        Args:
            base_url: Base URL of the target app (e.g., http://192.168.1.100:51321).
            retry_policy: Retry policy for failed requests.
            request_timeout: Default request timeout in seconds.
        """
        self.base_url = base_url.rstrip("/")
        self.retry_policy = retry_policy or default_retry_policy()
        self.request_timeout = request_timeout
        self._session = requests.Session()
        self._session.headers.update({
            "Content-Type": "application/json",
            "Accept": "application/json",
        })

    def post_run(self, scenario: dict[str, Any]) -> TestSession:
        """Submit a test scenario for execution.

        POST /e2e/run

        Args:
            scenario: Scenario data dictionary.

        Returns:
            TestSession with session_id.

        Raises:
            requests.HTTPError: On HTTP errors.
            ConnectionError: If target app is unreachable.
        """
        response = self._request_with_retry(
            "POST",
            f"{self.base_url}/e2e/run",
            json={"scenario": scenario},
            timeout=10,
        )
        data = response.json()
        return TestSession(
            session_id=data["session_id"],
            status=data.get("status", "running"),
        )

    def get_status(self, session_id: str) -> TestStatus:
        """Get current test execution status.

        GET /e2e/status/:session_id

        Args:
            session_id: Test session identifier.

        Returns:
            TestStatus with current progress.
        """
        response = self._request_with_retry(
            "GET",
            f"{self.base_url}/e2e/status/{session_id}",
            timeout=5,
        )
        data = response.json()
        return TestStatus(
            status=data["status"],
            progress=data.get("progress", 0.0),
            current_step=data.get("current_step", 0),
            total_steps=data.get("total_steps", 0),
        )

    def get_result(self, session_id: str) -> TestResult:
        """Get test execution results.

        GET /e2e/result/:session_id

        Args:
            session_id: Test session identifier.

        Returns:
            TestResult with screenshots and logs.
        """
        response = self._request_with_retry(
            "GET",
            f"{self.base_url}/e2e/result/{session_id}",
            timeout=self.request_timeout,
        )
        data = response.json()

        screenshots = [
            ScreenshotData(name=s["name"], data=s["data"])
            for s in data.get("screenshots", [])
        ]
        logs = [
            LogEntry(
                timestamp=log.get("timestamp", ""),
                level=log.get("level", "info"),
                message=log.get("message", ""),
            )
            for log in data.get("logs", [])
        ]

        return TestResult(
            status=data["status"],
            screenshots=screenshots,
            logs=logs,
            error=data.get("error"),
        )

    def poll_until_complete(
        self,
        session_id: str,
        timeout: float = 300.0,
        poll_interval: float = 2.0,
        on_progress: Optional[callable] = None,
    ) -> TestResult:
        """Poll status until test completes, then return result.

        Args:
            session_id: Test session identifier.
            timeout: Maximum wait time in seconds.
            poll_interval: Interval between polls in seconds.
            on_progress: Optional callback(TestStatus) on each poll.

        Returns:
            TestResult when test completes.

        Raises:
            TimeoutError: If test doesn't complete within timeout.
            RuntimeError: If test fails.
        """
        start_time = time.time()

        while time.time() - start_time < timeout:
            status = self.get_status(session_id)

            if on_progress:
                on_progress(status)

            if status.is_completed:
                return self.get_result(session_id)

            if status.is_failed:
                result = self.get_result(session_id)
                raise RuntimeError(
                    f"Test failed: {result.error or 'Unknown error'}"
                )

            time.sleep(poll_interval)

        raise TimeoutError(
            f"Test did not complete within {timeout}s"
        )

    def health_check(self) -> bool:
        """Check if the target app's E2E server is reachable.

        Returns:
            True if server responds.
        """
        try:
            response = self._session.get(
                f"{self.base_url}/e2e/status/health",
                timeout=5,
            )
            return response.status_code < 500
        except (requests.ConnectionError, requests.Timeout):
            return False

    def _request_with_retry(
        self,
        method: str,
        url: str,
        **kwargs,
    ) -> requests.Response:
        """Execute HTTP request with retry logic.

        Args:
            method: HTTP method (GET, POST, etc.).
            url: Request URL.
            **kwargs: Additional arguments for requests.

        Returns:
            Response object.

        Raises:
            requests.HTTPError: After all retries exhausted.
        """
        last_error: Optional[Exception] = None

        for attempt in range(self.retry_policy.max_retries + 1):
            try:
                response = self._session.request(method, url, **kwargs)

                if response.status_code < 500:
                    response.raise_for_status()
                    return response

                # 5xx - retry
                if attempt < self.retry_policy.max_retries:
                    delay = self.retry_policy.get_delay(attempt)
                    time.sleep(delay)
                    continue

                response.raise_for_status()

            except requests.ConnectionError as e:
                last_error = e
                if attempt < self.retry_policy.max_retries:
                    delay = self.retry_policy.get_delay(attempt)
                    time.sleep(delay)
                    continue
                raise

            except requests.Timeout as e:
                last_error = e
                if attempt < self.retry_policy.max_retries:
                    delay = self.retry_policy.get_delay(attempt)
                    time.sleep(delay)
                    continue
                raise

        # Should not reach here, but just in case
        if last_error:
            raise last_error
        raise RuntimeError("Request failed with no error captured")

    def close(self) -> None:
        """Close the HTTP session."""
        self._session.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
