namespace Entities.Entities;

// An offline tool ledger, never an Identity user or an HTTP resource.
public sealed class DemoSeedRun
{
    public string DatasetId { get; set; } = string.Empty;
    public string PlanHash { get; set; } = string.Empty;
    public string PlanJson { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
}
