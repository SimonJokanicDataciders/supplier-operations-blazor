namespace inventory_admin.Models;

public class SupplierImportPreview
{
    public string Name { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Industry { get; set; }

    public string? SourceSystem { get; set; }

    public string? ExternalSupplierKey { get; set; }

    public bool MatchedExisting { get; set; }

    public int? ExistingSupplierId { get; set; }

    public string MatchMessage { get; set; } = string.Empty;
}
