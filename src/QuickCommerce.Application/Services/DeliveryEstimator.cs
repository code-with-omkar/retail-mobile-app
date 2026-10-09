namespace QuickCommerce.Application.Services;

/// <summary>Delivery estimate settings (configuration section "Delivery"). Never hardcode these in code or in the app.</summary>
public sealed class DeliverySettings
{
    public const string SectionName = "Delivery";

    /// <summary>Minutes to pick and pack before the rider leaves.</summary>
    public int BasePrepMinutes { get; set; } = 8;

    /// <summary>Riding minutes per kilometre.</summary>
    public double MinutesPerKm { get; set; } = 2;

    /// <summary>Throws at startup when the configuration is unusable.</summary>
    public void Validate()
    {
        if (BasePrepMinutes is <= 0 or > 240)
        {
            throw new InvalidOperationException("Delivery:BasePrepMinutes must be between 1 and 240.");
        }

        if (double.IsNaN(MinutesPerKm) || MinutesPerKm <= 0 || MinutesPerKm > 60)
        {
            throw new InvalidOperationException("Delivery:MinutesPerKm must be greater than 0 and at most 60.");
        }
    }
}

public interface IDeliveryEstimator
{
    int EstimateMinutes(double distanceKm);
}

public sealed class DeliveryEstimator(DeliverySettings settings) : IDeliveryEstimator
{
    /// <summary>BasePrepMinutes + ceil(distance x MinutesPerKm).</summary>
    public int EstimateMinutes(double distanceKm) => settings.BasePrepMinutes + (int)Math.Ceiling(Math.Max(0, distanceKm) * settings.MinutesPerKm);
}
