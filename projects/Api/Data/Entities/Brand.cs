using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Represents a brand that can be applied to manufactured products.
/// Brands can be product-specific, category-specific, or company-wide.
/// Higher brand awareness and quality drive more sales.
/// </summary>
public sealed class Brand
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The company that owns this brand.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Navigation property to the owning company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>Brand name displayed to consumers.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Brand scope: PRODUCT (single product), CATEGORY (industry), COMPANY (all products).
    /// </summary>
    [Required, MaxLength(20)]
    public string Scope { get; set; } = BrandScope.Product;

    /// <summary>Specific product type this brand applies to (if scope is PRODUCT).</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Industry category this brand applies to (if scope is CATEGORY).</summary>
    [MaxLength(50)]
    public string? IndustryCategory { get; set; }

    /// <summary>Brand awareness level (0.0-1.0). Increased by marketing.</summary>
    public decimal Awareness { get; set; }

    /// <summary>Brand quality level (0.0-1.0). Increased by R&amp;D product-quality research and media.</summary>
    public decimal Quality { get; set; }

    /// <summary>
    /// Marketing efficiency multiplier (≥ 1.0). Increased by R&amp;D brand-quality research.
    /// A value of 1.0 means standard marketing effectiveness.
    /// A value of 2.0 means each unit of marketing budget generates twice the awareness gain.
    /// This is NOT a direct brand gain — it only amplifies the effect of marketing spend.
    /// </summary>
    public decimal MarketingEfficiencyMultiplier { get; set; } = 1m;
}

/// <summary>Defines valid brand scope values.</summary>
public static class BrandScope
{
    public const string Product = "PRODUCT";
    public const string Category = "CATEGORY";
    public const string Company = "COMPANY";
}
