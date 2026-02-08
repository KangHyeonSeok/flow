"""UDP broadcast listener for E2E test app discovery.

Listens for UDP broadcast messages from target apps running in E2E mode.
Target apps broadcast their connection info (app name, HTTP port, platform)
on UDP port 51320.
"""

import json
import socket
import time
from dataclasses import dataclass, field
from typing import Optional


# Default UDP discovery port (fixed)
DEFAULT_DISCOVERY_PORT = 51320

# Default listen timeout in seconds
DEFAULT_TIMEOUT = 30


@dataclass
class AppInfo:
    """Information about a discovered E2E test target app."""
    app: str
    host: str
    port: int
    platform: Optional[str] = None
    version: Optional[str] = None
    discovered_at: float = field(default_factory=time.time)

    @property
    def base_url(self) -> str:
        """HTTP base URL for the discovered app."""
        return f"http://{self.host}:{self.port}"

    def __str__(self) -> str:
        platform_str = f" ({self.platform})" if self.platform else ""
        return f"{self.app}{platform_str} at {self.base_url}"


class UDPListener:
    """Listens for UDP broadcast messages from E2E target apps.

    Target apps broadcast JSON messages on a fixed UDP port:
    {
        "app": "flow-editor",
        "platform": "unity",
        "port": 51321,
        "version": "1.0.0"
    }
    """

    def __init__(self, port: int = DEFAULT_DISCOVERY_PORT):
        """Initialize UDP listener.

        Args:
            port: UDP port to listen on. Default: 51320.
        """
        self.port = port
        self._sock: Optional[socket.socket] = None

    def _create_socket(self) -> socket.socket:
        """Create and configure the UDP socket."""
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(("", self.port))
        sock.settimeout(1.0)  # 1-second polling interval
        return sock

    def listen(
        self,
        timeout: float = DEFAULT_TIMEOUT,
        app_filter: Optional[str] = None,
        platform_filter: Optional[str] = None,
    ) -> AppInfo:
        """Listen for a target app's UDP broadcast.

        Args:
            timeout: Maximum time to wait in seconds. Default: 30.
            app_filter: Only accept apps matching this name.
            platform_filter: Only accept apps matching this platform.

        Returns:
            AppInfo for the discovered app.

        Raises:
            TimeoutError: If no matching app is found within timeout.
            OSError: If socket binding fails.
        """
        self._sock = self._create_socket()
        start_time = time.time()

        try:
            print(f"Listening for E2E target app on UDP port {self.port}...")

            while time.time() - start_time < timeout:
                try:
                    data, addr = self._sock.recvfrom(4096)
                    app_info = self._parse_broadcast(data, addr[0])

                    if app_info is None:
                        continue

                    # Apply filters
                    if app_filter and app_info.app != app_filter:
                        continue
                    if platform_filter and app_info.platform != platform_filter:
                        continue

                    print(f"Discovered: {app_info}")
                    return app_info

                except socket.timeout:
                    continue

            elapsed = time.time() - start_time
            raise TimeoutError(
                f"No target app found after {elapsed:.1f}s on UDP port {self.port}. "
                "Make sure the app is running with E2E_TESTS=true."
            )
        finally:
            self.close()

    def listen_all(
        self,
        timeout: float = DEFAULT_TIMEOUT,
        app_filter: Optional[str] = None,
    ) -> list[AppInfo]:
        """Listen for all target apps broadcasting within the timeout period.

        Args:
            timeout: Time to listen in seconds.
            app_filter: Only accept apps matching this name.

        Returns:
            List of discovered AppInfo objects (deduplicated by host:port).
        """
        self._sock = self._create_socket()
        start_time = time.time()
        discovered: dict[str, AppInfo] = {}

        try:
            print(f"Scanning for E2E target apps on UDP port {self.port} ({timeout}s)...")

            while time.time() - start_time < timeout:
                try:
                    data, addr = self._sock.recvfrom(4096)
                    app_info = self._parse_broadcast(data, addr[0])

                    if app_info is None:
                        continue

                    if app_filter and app_info.app != app_filter:
                        continue

                    key = f"{app_info.host}:{app_info.port}"
                    if key not in discovered:
                        discovered[key] = app_info
                        print(f"  Found: {app_info}")

                except socket.timeout:
                    continue

        finally:
            self.close()

        return list(discovered.values())

    def _parse_broadcast(self, data: bytes, host: str) -> Optional[AppInfo]:
        """Parse a UDP broadcast message into AppInfo.

        Expected JSON format:
        {
            "app": "flow-editor",
            "platform": "unity",
            "port": 51321,
            "version": "1.0.0"
        }
        """
        try:
            message = json.loads(data.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            return None

        if not isinstance(message, dict):
            return None

        # Required fields
        app = message.get("app")
        port = message.get("port")

        if not app or not isinstance(port, int):
            return None

        return AppInfo(
            app=str(app),
            host=host,
            port=port,
            platform=message.get("platform"),
            version=message.get("version"),
        )

    def close(self) -> None:
        """Close the UDP socket."""
        if self._sock:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
