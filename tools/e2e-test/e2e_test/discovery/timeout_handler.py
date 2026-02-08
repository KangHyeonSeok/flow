"""Timeout handling utilities for discovery and transport."""

import time
from dataclasses import dataclass
from typing import Callable, Optional, TypeVar

T = TypeVar("T")


@dataclass
class RetryConfig:
    """Configuration for retry logic."""
    max_retries: int = 3
    initial_delay: float = 1.0
    backoff_factor: float = 2.0
    max_delay: float = 30.0


class TimeoutHandler:
    """Handles timeout and retry logic for discovery operations."""

    def __init__(self, timeout: float, retry_config: Optional[RetryConfig] = None):
        """Initialize timeout handler.

        Args:
            timeout: Overall timeout in seconds.
            retry_config: Retry configuration. Default: 3 retries with exponential backoff.
        """
        self.timeout = timeout
        self.retry_config = retry_config or RetryConfig()
        self._start_time: Optional[float] = None

    @property
    def elapsed(self) -> float:
        """Seconds elapsed since start."""
        if self._start_time is None:
            return 0.0
        return time.time() - self._start_time

    @property
    def remaining(self) -> float:
        """Seconds remaining before timeout."""
        return max(0.0, self.timeout - self.elapsed)

    @property
    def is_expired(self) -> bool:
        """Whether the timeout has expired."""
        return self.elapsed >= self.timeout

    def start(self) -> None:
        """Start the timeout timer."""
        self._start_time = time.time()

    def reset(self) -> None:
        """Reset the timeout timer."""
        self._start_time = time.time()

    def execute_with_retry(
        self,
        operation: Callable[[], T],
        on_retry: Optional[Callable[[int, Exception], None]] = None,
    ) -> T:
        """Execute an operation with retry logic.

        Args:
            operation: Callable to execute.
            on_retry: Optional callback on each retry (attempt_number, exception).

        Returns:
            Result of the operation.

        Raises:
            TimeoutError: If all retries exhausted or timeout reached.
            Exception: Last exception if retries exhausted.
        """
        self.start()
        last_error: Optional[Exception] = None
        delay = self.retry_config.initial_delay

        for attempt in range(self.retry_config.max_retries + 1):
            if self.is_expired:
                break

            try:
                return operation()
            except Exception as e:
                last_error = e
                if attempt < self.retry_config.max_retries:
                    if on_retry:
                        on_retry(attempt + 1, e)

                    # Wait with backoff, but respect overall timeout
                    wait_time = min(delay, self.remaining)
                    if wait_time > 0:
                        time.sleep(wait_time)
                    delay = min(
                        delay * self.retry_config.backoff_factor,
                        self.retry_config.max_delay,
                    )

        if last_error:
            raise last_error
        raise TimeoutError(f"Operation timed out after {self.elapsed:.1f}s")
