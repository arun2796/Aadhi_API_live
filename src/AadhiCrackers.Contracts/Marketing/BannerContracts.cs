namespace AadhiCrackers.Contracts.Marketing;

public class HomepageBannerDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? MobileImageUrl { get; set; }
    public string TargetUrl { get; set; } = "/products";
    public string CtaText { get; set; } = "Shop Now";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateHomepageBannerRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? MobileImageUrl { get; set; }
    public string TargetUrl { get; set; } = "/products";
    public string CtaText { get; set; } = "Shop Now";
    public int DisplayOrder { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
}

public class UpdateHomepageBannerRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? MobileImageUrl { get; set; }
    public string TargetUrl { get; set; } = "/products";
    public string CtaText { get; set; } = "Shop Now";
    public int DisplayOrder { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
}
