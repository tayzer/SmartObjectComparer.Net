using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Domain.Tests;

[TestClass]
public sealed class AcceptedDifferenceFingerprintBuilderTests
{
    [TestMethod]
    public void Create_WhenCollectionIndexesDiffer_NormalizesPathForStableFingerprint()
    {
        ComparisonDifference first = new ComparisonDifference("Orders[0].CustomerId", "123456", "789012", "Changed.");
        ComparisonDifference second = new ComparisonDifference("Orders[4].CustomerId", "111111", "222222", "Changed.");

        AcceptedDifferenceFingerprint firstFingerprint = AcceptedDifferenceFingerprintBuilder.Create(first);
        AcceptedDifferenceFingerprint secondFingerprint = AcceptedDifferenceFingerprintBuilder.Create(second);

        Assert.AreEqual("Orders[*].CustomerId", firstFingerprint.NormalizedPropertyPath);
        Assert.AreEqual(firstFingerprint.Fingerprint, secondFingerprint.Fingerprint);
    }

    [TestMethod]
    public void Create_WhenValuesAreStable_IncludesValuePatternInFingerprint()
    {
        ComparisonDifference first = new ComparisonDifference("Customer.Name", "Alice", "Alicia");
        ComparisonDifference second = new ComparisonDifference("Customer.Name", "Bob", "Robert");

        AcceptedDifferenceFingerprint firstFingerprint = AcceptedDifferenceFingerprintBuilder.Create(first);
        AcceptedDifferenceFingerprint secondFingerprint = AcceptedDifferenceFingerprintBuilder.Create(second);

        Assert.AreNotEqual(firstFingerprint.Fingerprint, secondFingerprint.Fingerprint);
    }
}