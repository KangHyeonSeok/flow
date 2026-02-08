"""Retry policy for HTTP transport.

Provides configurable retry logic with exponential backoff.
"""

from dataclasses import dataclass


@dataclass
class RetryPolicy:
    """Configurable retry policy with exponential backoff."""
    max_retries: int = 3
    initial_delay: float = 1.0
    backoff_factor: float = 2.0
    max_delay: float = 30.0

    def get_delay(self, attempt: int) -> float:
        """Calculate delay for a given retry attempt (0-indexed).

        Args:
            attempt: Current attempt number (0 = first retry).

        Returns:
            Delay in seconds before next retry.
        """
        delay = self.initial_delay * (self.backoff_factor ** attempt)
        return min(delay, self.max_delay)


def default_retry_policy() -> RetryPolicy:
    """Create default retry policy.

    3 retries, 1s initial delay, 2x backoff, 30s max.
    """
    return RetryPolicy()


def aggressive_retry_policy() -> RetryPolicy:
    """Create aggressive retry policy for flaky connections.

    5 retries, 0.5s initial delay, 1.5x backoff, 10s max.
    """
    return RetryPolicy(
        max_retries=5,
        initial_delay=0.5,
        backoff_factor=1.5,
        max_delay=10.0,
    )


def no_retry_policy() -> RetryPolicy:
    """Create a no-retry policy (fail immediately)."""
    return RetryPolicy(max_retries=0)
