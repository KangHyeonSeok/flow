using Flow.Commands;

if (args.Length == 0)
    return PrintUsage();

var command = args[0].ToLowerInvariant();
var rest = args[1..];

return command switch
{
    "spec" => await SpecCommand.RunAsync(rest),
    "runner" => await RunnerCommand.RunAsync(rest),
    _ => PrintUsage()
};

static int PrintUsage()
{
    Console.WriteLine("""
    Usage: flow <command> [options]

    Commands:
      spec create   --project <id> --title <title> [--id <id>] [--type feature|task]
      spec list     --project <id> [--status <status>]
      spec get      --project <id> <spec-id>
      runner start  --project <id> [--once]
      runner stop
    """);
    return 1;
}
