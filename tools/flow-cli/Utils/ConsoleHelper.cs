namespace FlowCLI.Utils;

public static class ConsoleHelper
{
    public static string Confirm(string prompt, int? timeoutSeconds = null, string? defaultValue = null)
    {
        Console.Error.Write($"{prompt} (y/n)");
        if (defaultValue != null) Console.Error.Write($" [{defaultValue}]");
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
