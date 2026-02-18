#!/usr/bin/env python3
"""Gemini VLM (Vision Language Model) validator for screenshots.

Primary VLM implementation for Flow. Supports up to 3 images.
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import os
import sys
import time
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


VALIDATION_PROMPT = """You are a UI test validator. Analyze this screenshot.

You are given {image_count} image(s) (up to 3).

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
    """Validates screenshots using Gemini VLM."""

    DEFAULT_MODEL = "gemini-2.0-flash"
    DEFAULT_CONFIDENCE_THRESHOLD = 0.8
    DEFAULT_TEMPERATURE = 0.0
    DEFAULT_TOP_P = 0.0
    MIN_REQUEST_INTERVAL = 0.1

    def __init__(
        self,
        api_key: Optional[str] = None,
        model_name: Optional[str] = None,
        confidence_threshold: float = DEFAULT_CONFIDENCE_THRESHOLD,
        temperature: float = DEFAULT_TEMPERATURE,
        top_p: float = DEFAULT_TOP_P,
    ):
        self.api_key = api_key or load_api_key()
        self.model_name = model_name or self.DEFAULT_MODEL
        self.confidence_threshold = confidence_threshold
        self.temperature = temperature
        self.top_p = top_p
        self._last_request_time = 0.0
        self._client = None

    def _get_client(self):
        if self._client is None:
            from google import genai

            self._client = genai.Client(api_key=self.api_key)
        return self._client

    @staticmethod
    def _get_image_mime_type(image) -> str:
        format_name = (getattr(image, "format", "") or "").lower()
        if format_name in ("jpeg", "jpg"):
            return "image/jpeg"
        if format_name == "png":
            return "image/png"
        if format_name == "webp":
            return "image/webp"
        return "image/png"

    def validate_screenshot(
        self,
        screenshot_data: str,
        expected: str,
        prompt_template: Optional[str] = None,
    ) -> VLMValidationResult:
        return self.validate_screenshots(
            screenshot_data_list=[screenshot_data],
            expected=expected,
            prompt_template=prompt_template,
        )

    def validate_screenshots(
        self,
        screenshot_data_list: Sequence[str],
        expected: str,
        prompt_template: Optional[str] = None,
    ) -> VLMValidationResult:
        try:
            from PIL import Image
        except ImportError:
            return VLMValidationResult(
                passed=False,
                confidence=0.0,
                reason="Pillow not installed. Run: pip install pillow",
            )

        try:
            if not screenshot_data_list:
                return VLMValidationResult(False, 0.0, "At least one screenshot is required.")

            if len(screenshot_data_list) > 3:
                return VLMValidationResult(
                    False,
                    0.0,
                    f"Maximum 3 screenshots supported (got {len(screenshot_data_list)}).",
                )

            decoded_images: list[tuple[bytes, str]] = []
            for screenshot_data in screenshot_data_list:
                image_bytes = base64.b64decode(screenshot_data)
                if len(image_bytes) > 4 * 1024 * 1024:
                    return VLMValidationResult(False, 0.0, "One or more screenshots exceed 4MB limit.")

                image = Image.open(io.BytesIO(image_bytes))
                mime_type = self._get_image_mime_type(image)
                decoded_images.append((image_bytes, mime_type))

            self._rate_limit()

            template = prompt_template or VALIDATION_PROMPT
            try:
                prompt = template.format(expected=expected, image_count=len(decoded_images))
            except KeyError:
                prompt = template.format(expected=expected)

            from google.genai import types

            client = self._get_client()
            contents = [types.Part.from_text(text=prompt)]
            for image_bytes, mime_type in decoded_images:
                contents.append(types.Part.from_bytes(data=image_bytes, mime_type=mime_type))

            response = client.models.generate_content(
                model=self.model_name,
                contents=contents,
                config=types.GenerateContentConfig(
                    temperature=self.temperature,
                    top_p=self.top_p,
                ),
            )

            response_text = response.text if hasattr(response, "text") else str(response)
            result = self._parse_response(response_text)

            warning = None
            if result["confidence"] < self.confidence_threshold:
                warning = (
                    f"Low confidence: {result['confidence']:.2f} "
                    f"(threshold: {self.confidence_threshold})"
                )

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

    def validate_screenshot_file(self, screenshot_path: Path, expected: str) -> VLMValidationResult:
        screenshot_path = Path(screenshot_path)
        if not screenshot_path.exists():
            return VLMValidationResult(False, 0.0, f"Screenshot file not found: {screenshot_path}")

        with open(screenshot_path, "rb") as f:
            data = base64.b64encode(f.read()).decode("utf-8")
        return self.validate_screenshot(data, expected)

    def validate_screenshot_files(self, screenshot_paths: Sequence[Path], expected: str) -> VLMValidationResult:
        if not screenshot_paths:
            return VLMValidationResult(False, 0.0, "At least one screenshot path is required.")
        if len(screenshot_paths) > 3:
            return VLMValidationResult(False, 0.0, f"Maximum 3 screenshots supported (got {len(screenshot_paths)}).")

        encoded_images: list[str] = []
        for screenshot_path in screenshot_paths:
            screenshot_path = Path(screenshot_path)
            if not screenshot_path.exists():
                return VLMValidationResult(False, 0.0, f"Screenshot file not found: {screenshot_path}")
            with open(screenshot_path, "rb") as f:
                encoded_images.append(base64.b64encode(f.read()).decode("utf-8"))

        return self.validate_screenshots(encoded_images, expected)

    def _rate_limit(self) -> None:
        elapsed = time.time() - self._last_request_time
        if elapsed < self.MIN_REQUEST_INTERVAL:
            time.sleep(self.MIN_REQUEST_INTERVAL - elapsed)
        self._last_request_time = time.time()

    @staticmethod
    def _parse_response(text: str) -> dict:
        text = text.strip()
        if text.startswith("```"):
            lines = text.split("\n")
            text = "\n".join(lines[1:])
            if text.endswith("```"):
                text = text[:-3]
            text = text.strip()

        start = text.find("{")
        end = text.rfind("}") + 1

        if start != -1 and end > start:
            json_text = text[start:end]
            try:
                result = json.loads(json_text)
                return {
                    "pass": bool(result.get("pass", False)),
                    "confidence": float(result.get("confidence", 0.0)),
                    "reason": str(result.get("reason", "No reason provided")),
                }
            except (json.JSONDecodeError, KeyError, TypeError):
                pass

        text_lower = text.lower()
        if "pass" in text_lower and "true" in text_lower:
            return {"pass": True, "confidence": 0.5, "reason": f"Heuristic parse from: {text[:200]}"}

        return {"pass": False, "confidence": 0.0, "reason": f"Cannot parse VLM response: {text[:200]}"}


def main(argv: Optional[list[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Gemini VLM image validator (up to 3 images)")
    parser.add_argument("--image", type=Path, action="append", required=True, help="Image path to validate. Provide 1-3 times.")
    parser.add_argument("--expected", type=str, required=True, help="Expected description or question for the images.")
    parser.add_argument("--model", type=str, default=None, help="Override Gemini model name")
    parser.add_argument("--confidence", type=float, default=GeminiValidator.DEFAULT_CONFIDENCE_THRESHOLD, help="Confidence threshold for warning message")
    parser.add_argument("--temperature", type=float, default=GeminiValidator.DEFAULT_TEMPERATURE, help="Generation temperature (default: 0.0)")
    parser.add_argument("--top-p", dest="top_p", type=float, default=GeminiValidator.DEFAULT_TOP_P, help="Generation top_p (default: 0.0)")
    args = parser.parse_args(argv)

    if len(args.image) > 3:
        print(json.dumps({"success": False, "message": f"Maximum 3 images supported (got {len(args.image)})."}, ensure_ascii=False))
        return 2

    try:
        api_key = load_api_key()
        validator = GeminiValidator(
            api_key=api_key,
            model_name=args.model,
            confidence_threshold=args.confidence,
            temperature=args.temperature,
            top_p=args.top_p,
        )
        result = validator.validate_screenshot_files(args.image, args.expected)
    except Exception as e:
        print(json.dumps({"success": False, "message": str(e)}, ensure_ascii=False))
        return 2

    payload = {
        "success": result.passed,
        "confidence": result.confidence,
        "reason": result.reason,
        "warning": result.warning,
        "model": validator.model_name,
        "temperature": validator.temperature,
        "top_p": validator.top_p,
        "images": [str(path) for path in args.image],
    }
    print(json.dumps(payload, ensure_ascii=False))
    return 0 if result.passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
