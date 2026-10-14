namespace QuickCommerce.Api.Security;

/// <summary>How many browser origins were configured, so start-up can say when there are none.</summary>
public sealed record CorsOriginsInfo(int Count);
