"""Simple VLM smoke test for a single image."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

E2E_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(E2E_ROOT))

from e2e_test.validators.gemini_vlm import GeminiValidator, load_api_key


def build_default_image_path() -> Path:
    repo_root = Path(__file__).resolve().parents[3]
    return repo_root / "examples" / "testimage" / "testimage.png"


def main() -> int:
    parser = argparse.ArgumentParser(description="Gemini VLM single-image smoke test")
    parser.add_argument(
        "--image",
        type=Path,
        default=build_default_image_path(),
        help="Path to the image to validate",
    )
    parser.add_argument(
        "--expected",
        type=str,
        default=(
            "A dark-themed app interface with multiple world previews and a bottom "
            "navigation bar."
        ),
        help="Expected description for the image",
    )
    parser.add_argument(
        "--model",
        type=str,
        default=None,
        help="Override Gemini model name",
    )
    parser.add_argument(
        "--confidence",
        type=float,
        default=GeminiValidator.DEFAULT_CONFIDENCE_THRESHOLD,
        help="Confidence threshold for warnings",
    )
    args = parser.parse_args()

    image_path = args.image
    if not image_path.exists():
        print(json.dumps({"success": False, "message": f"Image not found: {image_path}"}))
        return 2

    api_key = load_api_key()
    validator = GeminiValidator(
        api_key=api_key,
        model_name=args.model,
        confidence_threshold=args.confidence,
    )

    result = validator.validate_screenshot_file(image_path, args.expected)
    payload = {
        "success": result.passed,
        "confidence": result.confidence,
        "reason": result.reason,
        "warning": result.warning,
    }
    print(json.dumps(payload, ensure_ascii=False))

    return 0 if result.passed else 1


if __name__ == "__main__":
    sys.exit(main())
