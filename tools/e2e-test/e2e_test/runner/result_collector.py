"""Result collector for E2E test execution.

Collects and aggregates test results from multiple sources.
"""

import base64
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional


@dataclass
class CollectedScreenshot:
    """A collected screenshot with metadata."""
    name: str
    data: str  # base64 encoded
    saved_path: Optional[str] = None


@dataclass
class CollectedResult:
    """Aggregated collection of test results."""
    screenshots: list[CollectedScreenshot] = field(default_factory=list)
    logs: list[dict] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)

    def add_screenshot(self, name: str, data: str) -> None:
        """Add a screenshot to the collection."""
        self.screenshots.append(CollectedScreenshot(name=name, data=data))

    def add_log(self, level: str, message: str, timestamp: str = "") -> None:
        """Add a log entry."""
        self.logs.append({
            "level": level,
            "message": message,
            "timestamp": timestamp,
        })

    def add_error(self, error: str) -> None:
        """Add an error message."""
        self.errors.append(error)

    @property
    def has_errors(self) -> bool:
        return len(self.errors) > 0

    def get_screenshots_dict(self) -> dict[str, str]:
        """Get screenshots as name -> base64 dict."""
        return {ss.name: ss.data for ss in self.screenshots}


class ResultCollector:
    """Collects test results and manages screenshot saving."""

    def __init__(self, output_dir: Optional[Path] = None):
        """Initialize result collector.

        Args:
            output_dir: Directory to save screenshots. None = don't save.
        """
        self.output_dir = Path(output_dir) if output_dir else None
        self.result = CollectedResult()

    def collect_screenshots(
        self,
        screenshots: list[dict],
    ) -> None:
        """Collect screenshots from HTTP response.

        Args:
            screenshots: List of {name, data} dicts from HTTP result.
        """
        for ss in screenshots:
            name = ss.get("name", "unnamed")
            data = ss.get("data", "")
            self.result.add_screenshot(name, data)

            # Save to disk if output_dir is configured
            if self.output_dir:
                self._save_screenshot(name, data)

    def collect_logs(self, logs: list[dict]) -> None:
        """Collect log entries from HTTP response."""
        for log in logs:
            self.result.add_log(
                level=log.get("level", "info"),
                message=log.get("message", ""),
                timestamp=log.get("timestamp", ""),
            )

    def _save_screenshot(self, name: str, data: str) -> Optional[str]:
        """Save a base64 screenshot to disk."""
        try:
            self.output_dir.mkdir(parents=True, exist_ok=True)
            file_path = self.output_dir / f"{name}.png"
            image_bytes = base64.b64decode(data)

            with open(file_path, "wb") as f:
                f.write(image_bytes)

            # Update the screenshot's saved_path
            for ss in self.result.screenshots:
                if ss.name == name and ss.saved_path is None:
                    ss.saved_path = str(file_path)
                    break

            return str(file_path)

        except Exception as e:
            self.result.add_error(f"Failed to save screenshot '{name}': {e}")
            return None

    def get_saved_paths(self) -> list[str]:
        """Get list of saved screenshot file paths."""
        return [
            ss.saved_path
            for ss in self.result.screenshots
            if ss.saved_path is not None
        ]
