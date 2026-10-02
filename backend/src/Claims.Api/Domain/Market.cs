namespace Claims.Api.Domain;

// Claims are booked in the market currency; never summed across currencies without conversion.
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
