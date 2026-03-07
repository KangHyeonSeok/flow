using Cocona;
using FlowCLI;
using FlowCLI.Services;
using FlowCLI.Utils;

try
{
    // F-006-C1: Detect legacy direct-arg calls (known commands, not 'invoke').
    // Convert to FlowRequest and route through the new JSON dispatcher.
    // Fall through to Cocona for 'invoke', '--help', '-h', and unknown commands.
    if (args.Length > 0
        && LegacyArgsAdapter.IsLegacyCommand(args[0])
        && !Array.Exists(args, a => a is "--help" or "-h"))
    {
        var request = LegacyArgsAdapter.ToFlowRequest(args);
        var pretty  = LegacyArgsAdapter.ExtractPretty(args);

        // F-006-C3: Optional deprecation warning (enabled via FLOW_DEPRECATION_WARNINGS env var)
        DeprecationPolicy.WarnIfEnabled(request.Command);

        // F-006-C1/C2: Route through new dispatcher — same path as 'flow invoke'
        var app = new FlowApp();
        app.DispatchLegacy(request, pretty);
    }
    else
    {
        CoconaLiteApp.Run<FlowApp>(args);
    }
}
catch (Exception ex)
{
    // F-005-C2: 미처리 예외를 표준 JSON 오류 envelope로 출력한다.
    JsonOutput.Write(JsonOutput.Error("flow", ex.Message, new { type = ex.GetType().Name }));
    Environment.ExitCode = 1;
}

