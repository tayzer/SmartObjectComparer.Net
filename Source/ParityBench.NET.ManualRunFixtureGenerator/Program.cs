using ParityBench.NET.ManualRunFixtureGenerator;

int count = 1000;
int startId = 10000;
string? outputOverride = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--count" when i + 1 < args.Length:
            count = int.Parse(args[++i]);
            break;
        case "--start-id" when i + 1 < args.Length:
            startId = int.Parse(args[++i]);
            break;
        case "--output" when i + 1 < args.Length:
            outputOverride = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unrecognized argument: {args[i]}");
            return 1;
    }
}

string outputDirectory = outputOverride is not null
    ? Path.GetFullPath(outputOverride)
    : Path.Combine(GetRepositoryRoot(), "Examples", "ParityBench.NET.ManualRuns", "client-soap-json-token", "volume");

IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> fixtures = ClientCustomerLookupFixtureGenerator.Generate(count, startId);
ClientCustomerLookupFixtureGenerator.WriteToDirectory(outputDirectory, fixtures);

Console.WriteLine($"Wrote {fixtures.Count} request fixtures to {outputDirectory}");
Console.WriteLine("Category distribution:");
foreach (KeyValuePair<ParityBench.NET.ClientCustomerLookupExample.ClientCustomerLookupVariation, int> entry in ClientCustomerLookupFixtureGenerator.Summarize(fixtures).OrderBy(pair => pair.Key))
{
    Console.WriteLine($"  {entry.Key}: {entry.Value}");
}

return 0;

static string GetRepositoryRoot()
{
    DirectoryInfo? currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
    while (currentDirectory is not null)
    {
        if (File.Exists(Path.Combine(currentDirectory.FullName, "ComparisonTool.sln")))
        {
            return currentDirectory.FullName;
        }

        currentDirectory = currentDirectory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate ComparisonTool.sln.");
}
