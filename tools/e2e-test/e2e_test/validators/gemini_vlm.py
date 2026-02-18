"""Gemini VLM proxy validator for E2E tests.

E2E module delegates VLM execution to `.flow/bin/gemini_vlm.py`.
"""

from __future__ import annotations

import base64
import json
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Sequence


@dataclass
class VLMValidationResult:
    """Result of VLM screenshot validation."""

    passed: bool
    confidence: float
    reason: str
    warning: Optional[str] = None

    def __str__(self) -> str:
        status = "PASS" if self.passed else "FAIL"
        msg = f"{status} (confidence: {self.confidence:.2f}) - {self.reason}"
        if self.warning:
            msg += f" [WARNING: {self.warning}]"
        return msg


def load_api_key(env_path: Optional[Path] = None) -> str:
    """Load Gemini API key from environment or config file."""

    api_key = os.environ.get("GEMINI_API_KEY")
    if api_key:
        return api_key.strip()

    if env_path is None:
        env_path = Path.home() / ".flow" / "env"

    if not env_path.exists():
        raise FileNotFoundError(
            f"Gemini API key not found.\n"
            f"Set GEMINI_API_KEY environment variable, or\n"
            f"Create {env_path} with: GEMINI_API_KEY=your_key_here"
        )

    with open(env_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("#") or not line:
                continue
            if line.startswith("GEMINI_API_KEY="):
                key = line.split("=", 1)[1].strip()
                if key and key != "your_api_key_here":
                    return key

    raise ValueError(
        f"GEMINI_API_KEY not found or is placeholder in {env_path}.\n"
        f"Please set a valid API key."
    )


class GeminiValidator:
    """Delegates screenshot validation to `.flow/bin/gemini_vlm.py`."""

    DEFAULT_MODEL = "gemini-2.0-flash"
    DEFAULT_CONFIDENCE_THRESHOLD = 0.8
    DEFAULT_TEMPERATURE = 0.0
    DEFAULT_TOP_P = 0.0

    def __init__(
        self,
        api_key: Optional[str] = None,
        model_name: Optional[str] = None,
        confidence_threshold: float = DEFAULT_CONFIDENCE_THRESHOLD,
        temperature: float = DEFAULT_TEMPERATURE,
        top_p: float = DEFAULT_TOP_P,
    ):
        self.api_key = api_key
        self.model_name = model_name or self.DEFAULT_MODEL
        self.confidence_threshold = confidence_threshold
        self.temperature = temperature
        self.top_p = top_p

    def validate_screenshot(
        self,
        screenshot_data: str,
        expected: str,
        prompt_template: Optional[str] = None,
    ) -> VLMValidationResult:
        return self.validate_screenshots([screenshot_data], expected, prompt_template)

    def validate_screenshots(
        self,
        screenshot_data_list: Sequence[str],
        expected: str,
        prompt_template: Optional[str] = None,
    ) -> VLMValidationResult:
        if not screenshot_data_list:
            return VLMValidationResult(False, 0.0, "At least one screenshot is required.")
        if len(screenshot_data_list) > 3:
            return VLMValidationResult(
                False,
                0.0,
                f"Maximum 3 screenshots supported (got {len(screenshot_data_list)}).",
            )

        prompt = expected
        if prompt_template:
            try:
                prompt = prompt_template.format(expected=expected, image_count=len(screenshot_data_list))
            except KeyError:
                prompt = prompt_template.format(expected=expected)

        with tempfile.TemporaryDirectory(prefix="flow-vlm-") as tmp_dir:
            image_paths: list[Path] = []
            for index, screenshot_data in enumerate(screenshot_data_list):
                try:
                    image_bytes = base64.b64decode(screenshot_data)
                except Exception as e:
                    return VLMValidationResult(False, 0.0, f"Invalid base64 screenshot data: {e}")

                image_path = Path(tmp_dir) / f"image_{index + 1}.png"
                image_path.write_bytes(image_bytes)
                image_paths.append(image_path)

            return self._invoke_flow_vlm(image_paths, prompt)

    def validate_screenshot_file(self, screenshot_path: Path, expected: str) -> VLMValidationResult:
        return self.validate_screenshot_files([screenshot_path], expected)

    def validate_screenshot_files(
        self,
        screenshot_paths: Sequence[Path],
        expected: str,
    ) -> VLMValidationResult:
        if not screenshot_paths:
            return VLMValidationResult(False, 0.0, "At least one screenshot path is required.")
        if len(screenshot_paths) > 3:
            return VLMValidationResult(
                False,
                0.0,
                f"Maximum 3 screenshots supported (got {len(screenshot_paths)}).",
            )

        normalized: list[Path] = []
        for screenshot_path in screenshot_paths:
            path = Path(screenshot_path)
            if not path.exists():
                return VLMValidationResult(False, 0.0, f"Screenshot file not found: {path}")
            normalized.append(path)

        return self._invoke_flow_vlm(normalized, expected)

    def _invoke_flow_vlm(self, image_paths: Sequence[Path], expected: str) -> VLMValidationResult:
        script_path = _resolve_flow_vlm_script()
        if script_path is None:
            return VLMValidationResult(
                False,
                0.0,
                "Cannot find .flow/bin/gemini_vlm.py. Ensure Flow is installed and .flow/bin exists.",
            )

        python_cmd = _resolve_python_command(script_path)
        if not python_cmd:
            return VLMValidationResult(False, 0.0, "Python executable not found.")

        args: list[str] = [str(script_path)]
        for image_path in image_paths:
            args.extend(["--image", str(image_path)])
        args.extend(["--expected", expected])
        args.extend(["--confidence", str(self.confidence_threshold)])
        args.extend(["--temperature", str(self.temperature)])
        args.extend(["--top-p", str(self.top_p)])
        if self.model_name:
            args.extend(["--model", self.model_name])

        env = os.environ.copy()
        if self.api_key:
            env["GEMINI_API_KEY"] = self.api_key

        try:
            process = subprocess.run(
                python_cmd + args,
                capture_output=True,
                text=True,
                timeout=120,
                env=env,
            )
        except subprocess.TimeoutExpired:
            return VLMValidationResult(False, 0.0, "VLM command timed out.")
        except OSError as e:
            return VLMValidationResult(False, 0.0, f"Failed to execute VLM command: {e}")

        response_text = (process.stdout or process.stderr or "").strip()
        parsed = _parse_json_payload(response_text)
        if parsed is None:
            return VLMValidationResult(False, 0.0, f"Cannot parse VLM response: {response_text[:200]}")

        return VLMValidationResult(
            passed=bool(parsed.get("success", False)),
            confidence=float(parsed.get("confidence", 0.0)),
            reason=str(parsed.get("reason") or parsed.get("message") or "No reason provided"),
            warning=parsed.get("warning"),
        )


def _parse_json_payload(raw: str) -> Optional[dict]:
    raw = raw.strip()
    if not raw:
        return None

    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        start = raw.find("{")
        end = raw.rfind("}")
        if start == -1 or end == -1 or end <= start:
            return None
        try:
            return json.loads(raw[start : end + 1])
        except json.JSONDecodeError:
            return None


def _resolve_flow_vlm_script() -> Optional[Path]:
    current = Path(__file__).resolve()
    candidates = [
        current.parents[4] / ".flow" / "bin" / "gemini_vlm.py",  # repo/tools/e2e-test
        current.parents[3] / "bin" / "gemini_vlm.py",              # ~/.flow/e2e-test
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def _resolve_python_command(script_path: Path) -> list[str]:
    candidates = [
        script_path.parents[2] / ".venv" / "Scripts" / "python.exe",  # repo .venv
    ]
    for candidate in candidates:
        if candidate.exists():
            return [str(candidate)]

    if os.name == "nt":
        py_launcher = shutil.which("py")
        if py_launcher:
            return [py_launcher, "-3"]

    python_bin = shutil.which("python")
    if python_bin:
        return [python_bin]

    return []
