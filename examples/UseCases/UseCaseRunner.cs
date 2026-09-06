namespace GasNet.UseCases;

/// <summary>One runnable use case: a self-contained scenario with its own world.</summary>
public sealed class UseCase
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Action Run { get; init; }
}

public static class UseCaseRunner
{
    /// <summary>Runs every use case (or those whose name contains args[0]); returns the failure count.</summary>
    public static int Run(string[] args, IReadOnlyList<UseCase> useCases)
    {
        string? filter = args.FirstOrDefault();
        int index = 0, failures = 0;
        foreach (var useCase in useCases)
        {
            if (filter != null && !useCase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            index++;
            Console.WriteLine();
            Console.WriteLine($"=== {index}. {useCase.Name} — {useCase.Description} ===");
            try
            {
                useCase.Run();
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"  [FAIL] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"=== all {index} use case(s) finished ==="
            : $"=== {failures} of {index} use case(s) FAILED ===");
        return failures;
    }
}
