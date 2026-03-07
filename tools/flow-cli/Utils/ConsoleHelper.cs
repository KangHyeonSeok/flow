using System.Runtime.InteropServices;

namespace FlowCLI.Utils;

public static class ConsoleHelper
{
    /// <summary>
    /// 데몬 모드에서 Windows 콘솔 그룹 신호(CTRL_C_EVENT 등)를 무시한다.
    /// VS Code ConPTY 터미널에서 runner-status 호출 시 CTRL_C_EVENT가 콘솔 그룹에
    /// 브로드캐스트되어 데몬이 즉시 종료되는 문제를 방지한다.
    /// 데몬은 runner-stop (Process.Kill)으로만 종료된다.
    /// </summary>
    public static void DetachConsoleForDaemon()
    {
        if (OperatingSystem.IsWindows())
        {
            // NULL 핸들러 + TRUE = 이 프로세스에서 CTRL_C_EVENT 무시
            SetConsoleCtrlHandler(null, true);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(
        [MarshalAs(UnmanagedType.FunctionPtr)] ConsoleCtrlHandlerRoutine? HandlerRoutine,
        bool Add);

    private delegate bool ConsoleCtrlHandlerRoutine(uint dwCtrlType);


    public static string Confirm(string prompt, int? timeoutSeconds = null, string? defaultValue = null)
    {
        Console.Error.Write($"{prompt} (y/n)");
        defaultValue ??= "y";
        Console.Error.Write($" [{defaultValue}]");
        Console.Error.Write(": ");
        Console.Error.Flush();

        if (timeoutSeconds.HasValue)
        {
            var task = Task.Run(Console.ReadLine);
            if (task.Wait(TimeSpan.FromSeconds(timeoutSeconds.Value)))
            {
                var input = task.Result?.Trim().ToLower();
                if (string.IsNullOrEmpty(input)) input = defaultValue?.ToLower();
                return input is "y" or "yes" ? "yes" : "no";
            }
            // Timeout reached — use default
            return defaultValue?.ToLower() is "yes" or "y" ? "yes" : "no";
        }

        var line = Console.ReadLine()?.Trim().ToLower();
        if (string.IsNullOrEmpty(line)) line = defaultValue?.ToLower();
        return line is "y" or "yes" ? "yes" : "no";
    }

    public static string? Select(string prompt, string[] options)
    {
        if (options.Length == 0)
            throw new ArgumentException("No options provided for select input.");

        Console.Error.WriteLine(prompt);
        for (int i = 0; i < options.Length; i++)
            Console.Error.WriteLine($"  [{i + 1}] {options[i]}");
        Console.Error.Write("선택: ");
        Console.Error.Flush();

        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out int index) && index >= 1 && index <= options.Length)
            return options[index - 1];
        return input;
    }

    public static string? Text(string prompt)
    {
        Console.Error.Write($"{prompt}: ");
        Console.Error.Flush();
        return Console.ReadLine()?.Trim();
    }
}
