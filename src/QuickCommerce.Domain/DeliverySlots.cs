namespace QuickCommerce.Domain;

/// <summary>
/// When a store takes scheduled deliveries. One row with no store is the default for every store; a row with a store overrides it for that
/// store. Times are India time, as minutes after midnight (9 am is 540).
/// </summary>
public sealed class DeliverySlotSetting
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid? StoreId { get; set; }

	/// <summary>Off means customers get no choice of time: every order is "as soon as possible".</summary>
	public bool Enabled { get; set; }
	public int OpenMinutes { get; set; } = 9 * 60;
	public int CloseMinutes { get; set; } = 21 * 60;
	public int WindowMinutes { get; set; } = 60;

	/// <summary>How many orders one window takes.</summary>
	public int Capacity { get; set; } = 10;

	/// <summary>A window can be chosen only if it starts at least this many minutes from now.</summary>
	public int CutoffMinutes { get; set; } = 60;

	/// <summary>How many days can be chosen, counting today.</summary>
	public int DaysAhead { get; set; } = 2;

	/// <summary>Whether "as soon as possible" is still offered next to the windows.</summary>
	public bool AsapAllowed { get; set; } = true;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public string? UpdatedBy { get; set; }
}

/// <summary>How many orders hold one window of one store. Changed only with one conditional update, so the limit holds when customers race.</summary>
public sealed class SlotBooking
{
	public Guid StoreId { get; set; }

	/// <summary>The start of the window, UTC.</summary>
	public DateTime SlotStart { get; set; }
	public int Booked { get; set; }
}
