namespace GasNet.UseCases;

public static class Program
{
    public static int Main(string[] args) =>
        UseCaseRunner.Run(args,
        [
            new UseCase
            {
                Name = "InputPatterns",
                Description = "tap / hold / combo-window input shapes via ability tasks",
                Run = InputPatternsScenario.Run,
            },
            new UseCase
            {
                Name = "ChannelAndInterrupt",
                Description = "channeled payoff, TagAdded-triggered interruptor, task cancellation",
                Run = ChannelAndInterruptScenario.Run,
            },
            new UseCase
            {
                Name = "TargetingViaHostEvent",
                Description = "WaitTargetData stand-in: host raises a targeting event with the payload",
                Run = TargetingViaHostEventScenario.Run,
            },
            new UseCase
            {
                Name = "DataDrivenCombat",
                Description = "cost/cooldown/damage/burn from the JSON catalog, on-cooldown rejection as a return value",
                Run = DataDrivenCombatScenario.Run,
            },
            new UseCase
            {
                Name = "NetworkBridgeSimulation",
                Description = "docs/networking.md as code: GasNet events → DTO lines, input intents in",
                Run = NetworkBridgeSimulationScenario.Run,
            },
        ]);
}
