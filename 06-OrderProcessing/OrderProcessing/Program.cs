using OrderProcessing.Phase1;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing;

public class Program
{
    public static async Task Main(string[] args)
    {
        var (phase, cancelAfterMs) = ParseArguments(args);

        if (string.IsNullOrEmpty(phase))
        {
            phase = PromptMenu();
            if (phase == "exit") return;
        }

        using var scope = new CancellationScope(cancelAfterMs);
        if (cancelAfterMs.HasValue)
        {
            Log.Info($"Automatic cancellation scheduled after {cancelAfterMs.Value} ms.");
        }

        try
        {
            await RunPhaseAsync(phase, scope.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Info("Run cancelled by token.");
        }
        catch (Exception ex)
        {
            Log.Error($"Fatal error: {ex.Message}");
        }
    }

    private static async Task RunPhaseAsync(string phase, CancellationToken ct)
    {
        switch (phase.ToLowerInvariant())
        {
            case "phase1":
                await Phase1Runner.RunAsync(ct);
                break;
            case "phase2":
                Log.Info("Executing Phase 2...");
                // Phase 2 runner will be hooked up in Task 8.4
                break;
            case "phase3":
                Log.Info("Executing Phase 3...");
                break;
            case "phase4":
                Log.Info("Executing Phase 4...");
                break;
            case "all":
                Log.Info("Executing All Phases sequentially...");
                await Phase1Runner.RunAsync(ct);
                break;
            case "seed":
                await DataSeeder.ResetAsync(ct: ct);
                break;
            default:
                Log.Error($"Unknown phase: {phase}");
                break;
        }
    }

    private static string PromptMenu()
    {
        Console.WriteLine("\n================ Order Processing System ================");
        Console.WriteLine("1. Phase 1 — Async Data Access Layer");
        Console.WriteLine("2. Phase 2 — Concurrent Processing");
        Console.WriteLine("3. Phase 3 — Full Checkout Workflow");
        Console.WriteLine("4. Phase 4 — Scale-Up");
        Console.WriteLine("5. Run All Phases");
        Console.WriteLine("6. Reset Seed Data");
        Console.WriteLine("0. Exit");
        Console.WriteLine("=========================================================");
        Console.Write("Select an option: ");

        string? input = Console.ReadLine()?.Trim();
        return input switch
        {
            "1" => "phase1",
            "2" => "phase2",
            "3" => "phase3",
            "4" => "phase4",
            "5" => "all",
            "6" => "seed",
            _ => "exit"
        };
    }

    private static (string? Phase, int? CancelAfterMs) ParseArguments(string[] args)
    {
        string? phase = null;
        int? cancelAfterMs = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            if (arg.Equals("--cancel-after", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int ms))
                {
                    cancelAfterMs = ms;
                    i++;
                }
            }
            else if (arg.StartsWith("--cancel-after=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg["--cancel-after=".Length..], out int ms))
                {
                    cancelAfterMs = ms;
                }
            }
            else if (!arg.StartsWith("-"))
            {
                phase = arg;
            }
        }

        return (phase, cancelAfterMs);
    }
}
