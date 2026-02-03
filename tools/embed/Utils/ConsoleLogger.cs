namespace EmbedCLI.Utils;

/// <summary>
/// 콘솔 로깅 유틸리티
/// </summary>
public static class ConsoleLogger
{
    private static bool _verbose = Environment.GetEnvironmentVariable("EMBED_VERBOSE") == "1";
    
    public static void SetVerbose(bool verbose) => _verbose = verbose;
    
    public static void Info(string message)
    {
        if (_verbose)
        {
            Console.Error.WriteLine($"[INFO] {message}");
        }
    }
    
    public static void Debug(string message)
    {
        if (_verbose)
        {
            Console.Error.WriteLine($"[DEBUG] {message}");
        }
    }
    
    public static void Warning(string message)
    {
        Console.Error.WriteLine($"[WARNING] {message}");
    }
    
    public static void Error(string message)
    {
        Console.Error.WriteLine($"[ERROR] {message}");
    }
}
