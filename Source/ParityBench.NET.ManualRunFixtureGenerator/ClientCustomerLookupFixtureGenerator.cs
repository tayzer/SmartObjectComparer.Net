using System.Globalization;

using ParityBench.NET.ClientCustomerLookupExample;

namespace ParityBench.NET.ManualRunFixtureGenerator;

public static class ClientCustomerLookupFixtureGenerator
{
    public sealed record GeneratedFixture(string FileName, string Content, ClientCustomerLookupVariation Category);

    public static IReadOnlyList<GeneratedFixture> Generate(int count, int startId)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");
        }

        int width = Math.Max(4, count.ToString(CultureInfo.InvariantCulture).Length);
        List<GeneratedFixture> fixtures = new List<GeneratedFixture>(count);

        for (int i = 0; i < count; i++)
        {
            int customerId = startId + i;
            string customerIdText = customerId.ToString(CultureInfo.InvariantCulture);
            string correlationId = $"trace-{customerIdText}";
            ClientCustomerLookupVariation category = ClientCustomerLookupVariationCatalog.Resolve(customerIdText);
            string label = ClientCustomerLookupVariationCatalog.ToLabel(category);
            string sequence = (i + 1).ToString(new string('0', width), CultureInfo.InvariantCulture);
            string fileName = $"{sequence}-{label}.xml";
            string content = BuildRequestXml(customerIdText, correlationId);

            fixtures.Add(new GeneratedFixture(fileName, content, category));
        }

        return fixtures;
    }

    public static void WriteToDirectory(string outputDirectory, IReadOnlyList<GeneratedFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(fixtures);

        if (Directory.Exists(outputDirectory))
        {
            foreach (string existingFile in Directory.EnumerateFiles(outputDirectory, "*.xml"))
            {
                File.Delete(existingFile);
            }
        }
        else
        {
            Directory.CreateDirectory(outputDirectory);
        }

        foreach (GeneratedFixture fixture in fixtures)
        {
            File.WriteAllText(Path.Combine(outputDirectory, fixture.FileName), fixture.Content);
        }
    }

    public static IReadOnlyDictionary<ClientCustomerLookupVariation, int> Summarize(IReadOnlyList<GeneratedFixture> fixtures) =>
        fixtures
            .GroupBy(fixture => fixture.Category)
            .ToDictionary(group => group.Key, group => group.Count());

    private static string BuildRequestXml(string customerId, string correlationId) =>
        "<Envelope>\n" +
        "  <Body>\n" +
        "    <LookupRequest>\n" +
        "      <UserName>demo-user</UserName>\n" +
        "      <Password>demo-password</Password>\n" +
        $"      <CustomerId>{customerId}</CustomerId>\n" +
        $"      <CorrelationId>{correlationId}</CorrelationId>\n" +
        "    </LookupRequest>\n" +
        "  </Body>\n" +
        "</Envelope>\n";
}
