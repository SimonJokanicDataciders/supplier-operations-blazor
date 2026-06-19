using System.ComponentModel.DataAnnotations;

namespace inventory_admin.Models;

public class SupplierOperationsImportRequest
{
    public string? PayloadJson { get; set; }

    [StringLength(160)]
    public string? Name { get; set; }

    [StringLength(2, MinimumLength = 2)]
    public string? CountryCode { get; set; }

    [StringLength(254)]
    public string? ContactEmail { get; set; }

    [StringLength(500)]
    public string? WebsiteUrl { get; set; }

    [StringLength(120)]
    public string? Industry { get; set; }

    [StringLength(2000)]
    public string? OperationalNotes { get; set; }

    [StringLength(80)]
    public string? SourceSystem { get; set; }

    [StringLength(120)]
    public string? ExternalSupplierKey { get; set; }

    [StringLength(500)]
    public string? ResearchUrl { get; set; }
}
