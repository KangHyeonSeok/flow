using Cocona;
using FlowCLI;
using FlowCLI.Utils;

try
{
    CoconaLiteApp.Run<FlowApp>(args);
}
catch (Exception ex)
{
    // F-005-C2: 미처리 예외를 표준 JSON 오류 envelope로 출력한다.
    JsonOutput.Write(JsonOutput.Error("flow", ex.Message, new { type = ex.GetType().Name }));
    Environment.ExitCode = 1;
}
