namespace OrderProcessing.Shared.Logging;

public static class Log
{
    private static readonly object ConsoleLock = new();

    public static void Info(string message)
    {
        WriteLine(message, null);
    }

    public static void Success(string message)
    {
        WriteLine(message, ConsoleColor.Green);
    }

    public static void Error(string message)
    {
        WriteLine(message, ConsoleColor.Red);
    }

    private static void WriteLine(string message, ConsoleColor? color)
    {
        lock (ConsoleLock)
        {
            var originalColor = Console.ForegroundColor;
            try
            {
                if (color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                }

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                int threadId = Environment.CurrentManagedThreadId;
                Console.WriteLine($"[{timestamp}] [Thread {threadId}] {message}");
            }
            finally
            {
                if (color.HasValue)
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }
    }
}
