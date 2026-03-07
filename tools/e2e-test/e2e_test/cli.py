"""CLI entry point for E2E test tool.

Invoked by flow-cli's PythonBridge:
    python -m e2e_test.cli <scenario_file_or_dir> [options]
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
        output_error("Scenario file path or directory is required.")
        sys.exit(1)

    target = Path(scenario_path)
    if not target.exists():
        output_error(f"Scenario path not found: {scenario_path}")
        sys.exit(1)

    if target.is_dir():
        _run_directory(target, args)
    else:
        _run_single_file(target, args)


def _run_single_file(scenario_file: Path, args: dict) -> None:
    """Run a single YAML scenario file."""
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

        # Load platform adapter
        from .adapters import get_adapter
        adapter = get_adapter(scenario.meta.platform)
        if adapter:
            adapter.on_test_start(scenario.meta.app)

        # Execute
        executor = TestExecutor(
            scenario=scenario,
            config=config,
            gemini_validator=gemini_validator,
        )

        result = executor.execute()

        if adapter:
            adapter.on_test_end(scenario.meta.app, result.all_passed)

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


def _run_directory(scenario_dir: Path, args: dict) -> None:
    """Discover and run all YAML scenarios in a directory."""
    start_time = time.time()

    try:
        from .scenario.discovery import discover_scenarios

        discovered = discover_scenarios(scenario_dir, recursive=True)
    except Exception as e:
        output_error(f"Scenario discovery failed: {e}")
        sys.exit(1)

    if not discovered:
        output_error(f"No YAML scenario files found in: {scenario_dir}")
        sys.exit(1)

    # Separate valid scenarios from parse errors
    valid = [d for d in discovered if d.is_valid]
    invalid = [d for d in discovered if not d.is_valid]

    print(f"Discovered {len(discovered)} scenario(s): {len(valid)} valid, {len(invalid)} invalid")

    if not valid:
        errors = [f"{d.path.name}: {d.error}" for d in invalid]
        output_error(f"No valid scenarios found. Errors: {'; '.join(errors)}")
        sys.exit(1)

    # Platform filter
    platform_filter = args.get("platform")
    if platform_filter:
        valid = [d for d in valid if d.scenario and d.scenario.meta.platform == platform_filter]
        print(f"After platform filter '{platform_filter}': {len(valid)} scenario(s)")

    if not valid:
        output_error(f"No scenarios match platform filter '{platform_filter}'")
        sys.exit(1)

    # Execute each scenario and aggregate results
    total_passed = 0
    total_failed = 0
    total_skipped = 0
    scenario_results = []
    all_success = True

    try:
        from .runner.executor import ExecutionConfig, TestExecutor
        from .validators.gemini_vlm import GeminiValidator, load_api_key
        from .adapters import get_adapter

        gemini_validator = None
        try:
            api_key = load_api_key()
            gemini_validator = GeminiValidator(api_key=api_key)
        except (FileNotFoundError, ValueError):
            pass

        for ds in valid:
            scenario = ds.scenario
            print(f"\nRunning: {ds.path.name} ({scenario.meta.app})")

            config = ExecutionConfig(
                discovery_timeout=args.get("timeout", 300) / 10,
                test_timeout=args.get("timeout", 300),
                save_report=args.get("save_report", False),
            )
            if args.get("report_dir"):
                config.report_dir = Path(args["report_dir"])

            adapter = get_adapter(scenario.meta.platform)
            if adapter:
                adapter.on_test_start(scenario.meta.app)

            executor = TestExecutor(
                scenario=scenario,
                config=config,
                gemini_validator=gemini_validator,
            )

            result = executor.execute()
            flow_output = result.to_flow_json()

            if adapter:
                adapter.on_test_end(scenario.meta.app, result.all_passed)

            data = flow_output.get("data", {})
            total_passed += data.get("passed", 0)
            total_failed += data.get("failed", 0)
            total_skipped += data.get("skipped", 0)

            scenario_results.append({
                "scenario": scenario.meta.app,
                "file": str(ds.path),
                "success": flow_output.get("success", False),
                "message": flow_output.get("message", ""),
                "passed": data.get("passed", 0),
                "failed": data.get("failed", 0),
                "skipped": data.get("skipped", 0),
                "duration_ms": data.get("duration_ms", 0),
            })

            if not flow_output.get("success", False):
                all_success = False

    except KeyboardInterrupt:
        output_error("Test run interrupted by user")
        sys.exit(130)
    except Exception as e:
        output_error(f"Test run failed: {e}")
        sys.exit(1)

    duration_ms = int((time.time() - start_time) * 1000)
    total = total_passed + total_failed + total_skipped

    if all_success:
        message = f"All {len(valid)} scenario(s) passed"
    else:
        failed_scenarios = sum(1 for r in scenario_results if not r["success"])
        message = f"{failed_scenarios}/{len(valid)} scenario(s) failed"

    output = {
        "success": all_success,
        "command": "test",
        "data": {
            "directory": str(scenario_dir),
            "scenarios_total": len(valid),
            "scenarios_passed": sum(1 for r in scenario_results if r["success"]),
            "scenarios_failed": sum(1 for r in scenario_results if not r["success"]),
            "total_tests": total,
            "passed": total_passed,
            "failed": total_failed,
            "skipped": total_skipped,
            "duration_ms": duration_ms,
            "results": scenario_results,
        },
        "message": message,
    }
    print(json.dumps(output, ensure_ascii=False))

    if not all_success:
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
    python -m e2e_test.cli <scenario.yaml|directory> [options]

Arguments:
    scenario.yaml   Path to a YAML scenario file, or
    directory       Path to a directory containing YAML scenario files

Options:
    --timeout <sec>     Test timeout per scenario in seconds (default: 300)
    --retry <count>     Retry count (default: 3)
    --platform <name>   Target platform filter (flutter|unity)
    --save-report       Save report to file
    --report-dir <dir>  Directory for saved reports
    --pretty            Pretty print output
    -h, --help          Show this help
""")


if __name__ == "__main__":
    main()
