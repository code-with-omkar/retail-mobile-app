namespace QuickCommerce.Domain;

/// <summary>A customer's report after delivery (missing, damaged or wrong items) and what the store decided. One per order.</summary>
public sealed class OrderIssue
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid OrganizationId { get; set; }
	public Guid StoreId { get; set; }
	public Guid OrderId { get; set; }
	public Guid CustomerUserId { get; set; }

	/// <summary>Missing, Damaged, WrongItem or Quality.</summary>
	public required string Kind { get; set; }
	public string? Note { get; set; }

	/// <summary>Open, Approved or Refused.</summary>
	public string Status { get; set; } = "Open";
	public decimal ClaimedAmount { get; set; }
	public decimal? ApprovedAmount { get; set; }

	/// <summary>What the store writes to the customer with its decision.</summary>
	public string? DecisionNote { get; set; }
	public Guid? DecidedBy { get; set; }
	public DateTime? DecidedAt { get; set; }

	/// <summary>Online (back to the payment) or Cash (the store pays by hand and marks it). Null until approved.</summary>
	public string? RefundMethod { get; set; }
	public DateTime? CashPaidAt { get; set; }
	public Guid? CashPaidBy { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public List<OrderIssueItem> Items { get; set; } = [];
}

public sealed class OrderIssueItem
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid IssueId { get; set; }
	public Guid ProductId { get; set; }
	public Guid? VariantId { get; set; }
	public required string Name { get; set; }
	public string? Label { get; set; }
	public int Quantity { get; set; }
	public decimal Amount { get; set; }
}
