using SecureHub.Core.Security.Guardrails;

namespace SecureHub.Core.Tests;

public class DetectorCatalogTests
{
    public static TheoryData<string> DetectorIds()
    {
        var data = new TheoryData<string>();
        foreach (var detector in DetectorCatalog.All) data.Add(detector.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(DetectorIds))]
    public void Catalog_sample_triggers_its_detector(string id)
    {
        var descriptor = DetectorCatalog.Find(id);
        Assert.NotNull(descriptor);

        var detections = descriptor.Category == "secret"
            ? SecretDetectors.Scan(descriptor.Sample, "sample")
            : PiiDetectors.Scan(descriptor.Sample, "sample");

        var detection = Assert.Single(detections, item => item.DetectorId == id);
        Assert.True(detection.MatchCount > 0);
        Assert.Equal(descriptor.Severity, detection.Severity);
    }

    [Fact]
    public void Catalog_ids_are_unique()
    {
        Assert.Equal(DetectorCatalog.All.Count, DetectorCatalog.All.Select(d => d.Id).Distinct().Count());
    }
}
