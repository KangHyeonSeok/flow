"""CLI entry point for E2E test tool.

Invoked by flow-cli's PythonBridge:
    python -m e2e_test.cli <scenario_file> [options]
"""

import json
import sys
import time
from pathlib import Path
from typing import Optional

# Don't require click for the basic CLI - keep it dependency-light
# click is available for future advanced CLI needs


def main():
    """Main CLI entry point."""
    args = parse_args(sys.argv[1:])

    if args.get("help"):
        print_help()
        return

    scenario_path = args.get("scenario")
    if not scenario_path:
        output_error("Scenario file path is required.")
        sys.exit(1)

    scenario_file = Path(scenario_path)
    if not scenario_file.exists():
        output_error(f"Scenario file not found: {scenario_path}")
        sys.exit(1)

    # Parse and validate scenario
    try:
        from .scenario.parser import parse_scenario
        from .scenario.validator import validate_scenario

        scenario = parse_scenario(scenario_file)
        validation = validate_scenario(scenario)

        if not validation.valid:
            errors_str = "; ".join(f"{e.path}: {e.message}" for e in validation.errors)
            output_error(f"Invalid scenario: {errors_str}")
            sys.exit(1)

    except Exception as e:
        output_error(f"Failed to parse scenario: {e}")
        sys.exit(1)

    # Execute test
    start_time = time.time()

    try:
        from .discovery.udp_listener import UDPListener
        from .runner.executor import ExecutionConfig, TestExecutor
        from .validators.gemini_vlm import GeminiValidator, load_api_key

        # Set up config
        config = ExecutionConfig(
            discovery_timeout=args.get("timeout", 300) / 10,  # 10% of total for discovery
            test_timeout=args.get("timeout", 300),
            save_report=args.get("save_report", False),
        )

        if args.get("report_dir"):
            config.report_dir = Path(args["report_dir"])

        # Try to set up VLM validator
        gemini_validator = None
        try:
            api_key = load_api_key()
            gemini_validator = GeminiValidator(api_key=api_key)
        except (FileNotFoundError, ValueError):
            # VLM validation won't be available
            pass

        # Handle platform filter
        if args.get("platform"):
            scenario.meta.platform = args["platform"]

        # Execute
        executor = TestExecutor(
            scenario=scenario,
            config=config,
            gemini_validator=gemini_validator,
        )

        result = executor.execute()

        # Output as flow-compatible JSON
        flow_output = result.to_flow_json()
        print(json.dumps(flow_output, ensure_ascii=False))

        if not flow_output.get("success", False):
            sys.exit(1)

    except KeyboardInterrupt:
        duration_ms = int((time.time() - start_time) * 1000)
        output_error("Test interrupted by user", duration_ms=duration_ms)
        sys.exit(130)

    except Exception as e:
        duration_ms = int((time.time() - start_time) * 1000)
        output_error(f"Test execution failed: {e}", duration_ms=duration_ms)
        sys.exit(1)


def parse_args(argv: list[str]) -> dict:
    """Simple argument parser (no external dependencies)."""
    args: dict = {}
    i = 0

    while i < len(argv):
        arg = argv[i]

        if arg in ("-h", "--help"):
            args["help"] = True
        elif arg == "--timeout" and i + 1 < len(argv):
            i += 1
            args["timeout"] = int(argv[i])
        elif arg == "--retry" and i + 1 < len(argv):
            i += 1
            args["retry"] = int(argv[i])
        elif arg == "--platform" and i + 1 < len(argv):
            i += 1
            args["platform"] = argv[i]
        elif arg == "--save-report":
            args["save_report"] = True
        elif arg == "--report-dir" and i + 1 < len(argv):
            i += 1
            args["report_dir"] = argv[i]
        elif arg == "--pretty":
            args["pretty"] = True
        elif not arg.startswith("-"):
            if "scenario" not in args:
                args["scenario"] = arg

        i += 1

    return args


def output_error(message: str, **extra):
    """Output error in flow JSON format."""
    output = {
        "success": False,
        "command": "test",
        "data": extra or None,
        "message": message,
    }
    print(json.dumps(output, ensure_ascii=False))


def print_help():
    """Print CLI usage help."""
    print("""E2E Test Tool - Flow Project

Usage:
    python -m e2e_test.cli <scenario.yaml> [options]

Options:
    --timeout <sec>     Test timeout (default: 300)
    --retry <count>     Retry count (default: 3)
    --platform <name>   Target platform (flutter|unity)
    --save-report       Save report to file
    --report-dir <dir>  Directory for saved reports
    --pretty            Pretty print output
    -h, --help          Show this help
""")


if __name__ == "__main__":
    main()
