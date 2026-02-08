"""Gemini VLM (Vision Language Model) validator for E2E test screenshots.

Uses Google's Gemini Flash model to validate test screenshots
against expected descriptions using visual understanding.
"""

import base64
import io
import json
import os
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional


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
    """Load Gemini API key from environment or config file.

    Search order:
    1. GEMINI_API_KEY environment variable
    2. ~/.flow/env file

    Args:
        env_path: Custom path to env file. Default: ~/.flow/env

    Returns:
        API key string.

    Raises:
        FileNotFoundError: If no API key source is found.
        ValueError: If env file exists but doesn't contain the key.
    """
    # Check environment variable first
    api_key = os.environ.get("GEMINI_API_KEY")
    if api_key:
        return api_key.strip()

    # Check ~/.flow/env file
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


# Default prompt template for VLM validation
VALIDATION_PROMPT = """You are a UI test validator. Analyze this screenshot.

Expected: {expected}

Does the screenshot match the expected result?
Answer ONLY in JSON format (no markdown, no extra text):
{{
  "pass": true or false,
  "confidence": 0.0 to 1.0,
  "reason": "detailed explanation"
}}
"""


class GeminiValidator:
    """Validates E2E test screenshots using Gemini VLM.

    Uses Google's Gemini Flash model to analyze screenshots
    and determine if they match expected test outcomes.
    """

    DEFAULT_MODEL = "gemini-2.0-flash"
    DEFAULT_CONFIDENCE_THRESHOLD = 0.8
    MIN_REQUEST_INTERVAL = 0.1  # seconds between API calls

    def __init__(
        self,
        api_key: Optional[str] = None,
        model_name: Optional[str] = None,
        confidence_threshold: float = DEFAULT_CONFIDENCE_THRESHOLD,
    ):
        """Initialize Gemini validator.

        Args:
            api_key: Gemini API key. If None, loads from env/config.
            model_name: Gemini model name. Default: gemini-2.0-flash.
            confidence_threshold: Minimum confidence for pass. Default: 0.8.
        """
        self.api_key = api_key or load_api_key()
        self.model_name = model_name or self.DEFAULT_MODEL
        self.confidence_threshold = confidence_threshold
        self._last_request_time = 0.0
        self._model = None

    def _get_model(self):
        """Lazy-initialize the Gemini model."""
        if self._model is None:
            import google.generativeai as genai
            genai.configure(api_key=self.api_key)
            self._model = genai.GenerativeModel(self.model_name)
        return self._model

    def validate_screenshot(
        self,
        screenshot_data: str,
        expected: str,
        prompt_template: Optional[str] = None,
    ) -> VLMValidationResult:
        """Validate a screenshot against expected description.

        Args:
            screenshot_data: Base64 encoded screenshot image.
            expected: Description of what the screenshot should show.
            prompt_template: Custom prompt template. Use {expected} placeholder.

        Returns:
            VLMValidationResult with pass/fail, confidence, and reason.
        """
        try:
            from PIL import Image
        except ImportError:
            return VLMValidationResult(
                passed=False,
                confidence=0.0,
                reason="Pillow not installed. Run: pip install pillow",
            )

        try:
            # Decode screenshot
            image_bytes = base64.b64decode(screenshot_data)
            image = Image.open(io.BytesIO(image_bytes))

            # Check image size (max 4MB)
            if len(image_bytes) > 4 * 1024 * 1024:
                return VLMValidationResult(
                    passed=False,
                    confidence=0.0,
                    reason="Screenshot exceeds 4MB limit.",
                )

            # Rate limiting
            self._rate_limit()

            # Build prompt
            template = prompt_template or VALIDATION_PROMPT
            prompt = template.format(expected=expected)

            # Call Gemini API
            model = self._get_model()
            response = model.generate_content([prompt, image])

            # Parse response
            result = self._parse_response(response.text)

            # Check confidence threshold
            warning = None
            if result["confidence"] < self.confidence_threshold:
                warning = f"Low confidence: {result['confidence']:.2f} (threshold: {self.confidence_threshold})"

            return VLMValidationResult(
                passed=result["pass"],
                confidence=result["confidence"],
                reason=result["reason"],
                warning=warning,
            )

        except Exception as e:
            return VLMValidationResult(
                passed=False,
                confidence=0.0,
                reason=f"VLM validation error: {str(e)}",
            )

    def validate_screenshot_file(
        self,
        screenshot_path: Path,
        expected: str,
    ) -> VLMValidationResult:
        """Validate a screenshot file against expected description.

        Args:
            screenshot_path: Path to screenshot image file.
            expected: Description of what the screenshot should show.

        Returns:
            VLMValidationResult.
        """
        screenshot_path = Path(screenshot_path)
        if not screenshot_path.exists():
            return VLMValidationResult(
                passed=False,
                confidence=0.0,
                reason=f"Screenshot file not found: {screenshot_path}",
            )

        with open(screenshot_path, "rb") as f:
            data = base64.b64encode(f.read()).decode("utf-8")

        return self.validate_screenshot(data, expected)

    def _rate_limit(self) -> None:
        """Enforce minimum interval between API requests."""
        elapsed = time.time() - self._last_request_time
        if elapsed < self.MIN_REQUEST_INTERVAL:
            time.sleep(self.MIN_REQUEST_INTERVAL - elapsed)
        self._last_request_time = time.time()

    @staticmethod
    def _parse_response(text: str) -> dict:
        """Parse Gemini response, extracting JSON.

        Handles cases where the response may contain markdown code fences
        or extra text around the JSON.
        """
        # Strip markdown code fences if present
        text = text.strip()
        if text.startswith("```"):
            lines = text.split("\n")
            text = "\n".join(lines[1:])
            if text.endswith("```"):
                text = text[:-3]
            text = text.strip()

        # Try to find JSON object
        start = text.find("{")
        end = text.rfind("}") + 1

        if start != -1 and end > start:
            json_text = text[start:end]
            try:
                result = json.loads(json_text)
                # Normalize field names
                return {
                    "pass": bool(result.get("pass", False)),
                    "confidence": float(result.get("confidence", 0.0)),
                    "reason": str(result.get("reason", "No reason provided")),
                }
            except (json.JSONDecodeError, KeyError, TypeError):
                pass

        # Fallback: simple heuristic
        text_lower = text.lower()
        if "pass" in text_lower and "true" in text_lower:
            return {
                "pass": True,
                "confidence": 0.5,
                "reason": f"Heuristic parse from: {text[:200]}",
            }

        return {
            "pass": False,
            "confidence": 0.0,
            "reason": f"Cannot parse VLM response: {text[:200]}",
        }
