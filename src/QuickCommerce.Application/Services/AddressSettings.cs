namespace QuickCommerce.Application.Services;

public sealed class AddressSettings
{
    public const string SectionName = "Addresses";

    /// <summary>How many addresses one customer may save.</summary>
    public int MaxPerCustomer { get; set; } = 10;

    /// <summary>Throws at startup when the configuration is unusable.</summary>
    public void Validate()
    {
        if (MaxPerCustomer is < 1 or > 50)
        {
            throw new InvalidOperationException("Addresses:MaxPerCustomer must be between 1 and 50.");
        }
    }
}
