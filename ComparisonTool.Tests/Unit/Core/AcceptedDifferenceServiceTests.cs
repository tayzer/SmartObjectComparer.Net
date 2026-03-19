using ComparisonTool.Core.AcceptedDifferences;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class AcceptedDifferenceServiceTests
{
    private string tempDirectory = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "ComparisonToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void FingerprintBuilder_ShouldNormalizeArrayIndicesAndDynamicIdentifiers()
    {
        var builder = CreateFingerprintBuilder();

        var first = builder.Create(CreateDifference("Orders[0].OrderId", 12345, 67890));
        var second = builder.Create(CreateDifference("Orders[9].OrderId", 54321, 98765));

        first.NormalizedPropertyPath.Should().Be("Orders[*].OrderId");
        first.Fingerprint.Should().Be(second.Fingerprint);
        first.ExpectedValuePattern.Should().Be("<identifier>");
        first.ActualValuePattern.Should().Be("<identifier>");
    }

    [TestMethod]
    public async Task SaveAsync_ShouldMatchFutureDifferencesAcrossRuns()
    {
        var service = CreateService();
        var original = CreateDifference("Responses[0].LastUpdated", "2026-03-18T10:00:00Z", "2026-03-18T10:05:00Z");
        var future = CreateDifference("Responses[4].LastUpdated", "2026-04-01T08:30:00Z", "2026-04-01T08:35:00Z");

        var saved = await service.SaveAsync(original, AcceptedDifferenceStatus.AcceptedDifference, "Expected async replication lag.");
        var reloadedService = CreateService();
        var matches = await reloadedService.GetMatchesAsync(new[] { future });
        var futureFingerprint = service.CreateFingerprint(future).Fingerprint;

        matches.Should().ContainKey(futureFingerprint);
        matches[futureFingerprint].Status.Should().Be(AcceptedDifferenceStatus.AcceptedDifference);
        matches[futureFingerprint].Fingerprint.Should().Be(saved.Fingerprint);
    }

    [TestMethod]
    public async Task SaveAsync_WithKnownBugWithoutTicket_ShouldThrow()
    {
        var service = CreateService();
        var difference = CreateDifference("Order.Status", "Pending", "Failed");

        var action = async () => await service.SaveAsync(difference, AcceptedDifferenceStatus.KnownBug);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task RemoveAsync_ShouldDeletePersistedProfile()
    {
        var service = CreateService();
        var difference = CreateDifference("Order.CustomerId", 10001, 10002);
        await service.SaveAsync(difference, AcceptedDifferenceStatus.KnownBug, ticketId: "BUG-101");

        var removed = await service.RemoveAsync(difference);
        var matches = await service.GetMatchesAsync(new[] { difference });

        removed.Should().BeTrue();
        matches.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetMatchesAsync_WithMalformedStore_ShouldReturnEmpty()
    {
        var storePath = Path.Combine(tempDirectory, "accepted-differences.json");
        await File.WriteAllTextAsync(storePath, "{ not-valid-json }");

        var service = CreateService(storePath);
        var matches = await service.GetMatchesAsync(new[] { CreateDifference("Order.Id", 1, 2) });

        matches.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetMatchesAsync_WithNullProfilesPayload_ShouldReturnEmpty()
    {
        var storePath = Path.Combine(tempDirectory, "accepted-differences.json");
        await File.WriteAllTextAsync(storePath, "{\"version\":1,\"profiles\":null}");

        var service = CreateService(storePath);
        var matches = await service.GetMatchesAsync(new[] { CreateDifference("Order.Id", 1, 2) });

        matches.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SaveAsync_FromSeparateServiceInstances_ShouldMergeProfiles()
    {
        var storePath = Path.Combine(tempDirectory, "accepted-differences.json");
        var serviceA = CreateService(storePath);
        var serviceB = CreateService(storePath);
        var firstDifference = CreateDifference("Orders[0].OrderId", 10001, 10002);
        var secondDifference = CreateDifference("Orders[0].Status", "Pending", "Failed");

        await Task.WhenAll(
            serviceA.SaveAsync(firstDifference, AcceptedDifferenceStatus.AcceptedDifference),
            serviceB.SaveAsync(secondDifference, AcceptedDifferenceStatus.KnownBug, ticketId: "BUG-2026"));

        var reloadedService = CreateService(storePath);
        var matches = await reloadedService.GetMatchesAsync(new[] { firstDifference, secondDifference });

        matches.Should().HaveCount(2);
        matches.Values.Should().Contain(profile => profile.Status == AcceptedDifferenceStatus.AcceptedDifference);
        matches.Values.Should().Contain(profile => profile.TicketId == "BUG-2026");
    }

    [TestMethod]
    public async Task ImportAsync_WithReplaceExisting_ShouldReplaceStoreContents()
    {
        var service = CreateService();
        var existingDifference = CreateDifference("Orders[0].Status", "Pending", "Failed");
        await service.SaveAsync(existingDifference, AcceptedDifferenceStatus.KnownBug, ticketId: "BUG-301");

        var importedProfiles = new[]
        {
            new AcceptedDifferenceProfile
            {
                Fingerprint = "imported-fingerprint",
                NormalizedPropertyPath = "Orders[*].OrderId",
                ExpectedValuePattern = "<identifier>",
                ActualValuePattern = "<identifier>",
                SamplePropertyPath = "Orders[0].OrderId",
                SampleExpectedValue = "10001",
                SampleActualValue = "10002",
                Status = AcceptedDifferenceStatus.AcceptedDifference,
            },
        };

        var importedCount = await service.ImportAsync(importedProfiles, replaceExisting: true);
        var profiles = await service.GetAllAsync();

        importedCount.Should().Be(1);
        profiles.Should().HaveCount(1);
        profiles[0].Fingerprint.Should().Be("imported-fingerprint");
    }

    [TestMethod]
    public async Task ClearAsync_ShouldRemoveAllProfiles()
    {
        var service = CreateService();
        await service.SaveAsync(CreateDifference("Orders[0].Status", "Pending", "Failed"), AcceptedDifferenceStatus.KnownBug, ticketId: "BUG-401");
        await service.SaveAsync(CreateDifference("Orders[0].OrderId", 1, 2), AcceptedDifferenceStatus.AcceptedDifference);

        await service.ClearAsync();
        var profiles = await service.GetAllAsync();

        profiles.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ImportAsync_WithKnownBugWithoutTicket_ShouldThrow()
    {
        var service = CreateService();
        var importedProfiles = new[]
        {
            new AcceptedDifferenceProfile
            {
                Fingerprint = "missing-ticket",
                NormalizedPropertyPath = "Orders[*].Status",
                Status = AcceptedDifferenceStatus.KnownBug,
            },
        };

        var action = async () => await service.ImportAsync(importedProfiles);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    private AcceptedDifferenceFingerprintBuilder CreateFingerprintBuilder() =>
        new(NullLogger<AcceptedDifferenceFingerprintBuilder>.Instance);

    private AcceptedDifferenceService CreateService(string? storePath = null)
    {
        var options = Options.Create(new AcceptedDifferencesOptions
        {
            StorePath = storePath ?? Path.Combine(tempDirectory, "accepted-differences.json"),
        });

        return new AcceptedDifferenceService(
            NullLogger<AcceptedDifferenceService>.Instance,
            CreateFingerprintBuilder(),
            options);
    }

    private static Difference CreateDifference(string propertyName, object? object1Value, object? object2Value) => new()
    {
        PropertyName = propertyName,
        Object1Value = object1Value?.ToString(),
        Object2Value = object2Value?.ToString(),
    };
}