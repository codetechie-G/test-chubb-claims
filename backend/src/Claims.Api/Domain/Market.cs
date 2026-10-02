namespace Claims.Api.Domain;

/// <summary>
/// The six APAC markets. Each claim is booked in its market's local currency;
/// we never sum across currencies without an explicit conversion.
/// </summary>
public sealed record Market(string Code, string Name, string Currency)
{
    public static readonly IReadOnlyList<Market> All =
    [
        new("AU", "Australia", "AUD"),
        new("NZ", "New Zealand", "NZD"),
        new("SG", "Singapore", "SGD"),
        new("HK", "Hong Kong", "HKD"),
        new("MY", "Malaysia", "MYR"),
        new("TH", "Thailand", "THB"),
    ];

    public static Market? Find(string? code) =>
        All.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
}
