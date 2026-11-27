using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>A store cancelling an order it already accepted. The reason is required; the remark is for the team.</summary>
public static class OrderCancellations
{
    public static readonly string[] Reasons = ["CustomerRequest", "DeliveryFailed", "OutOfStock", "StoreProblem", OrderRejections.Other];

    /// <summary>Accepted and not yet delivered. A new order is rejected instead.</summary>
    public static bool CanCancel(OrderStatus status) => status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Confirmed or OrderStatus.OutForDelivery;

    public static string? Problem(string? reason, string? note) => reason is null || !Reasons.Contains(reason) ? "Choose why the order is being cancelled." : Remarks.Problem(reason, note);

    public static (string En, string Mr)? ForCustomer(string? reason) => reason switch
    {
        "CustomerRequest" => ("It was cancelled as you asked.", "तुमच्या सांगण्यावरून ती रद्द केली."),
        "DeliveryFailed" => ("We could not deliver it.", "आम्ही ती पोहोचवू शकलो नाही."),
        "OutOfStock" => ("Some items are out of stock.", "काही वस्तू संपल्या आहेत."),
        "StoreProblem" => ("The store could not complete it.", "दुकान ती पूर्ण करू शकले नाही."),
        _ => null
    };
}

/// <summary>Why a delivery partner could not hand an order over.</summary>
public static class DeliveryFailures
{
    public static readonly string[] Reasons = ["CustomerUnavailable", "WrongAddress", "CustomerRefused", "VehicleProblem", OrderRejections.Other];

    public static string? Problem(string? reason, string? note) => reason is null || !Reasons.Contains(reason) ? "Choose why it could not be delivered." : Remarks.Problem(reason, note);
}

internal static class Remarks
{
    public static string? Problem(string reason, string? note)
    {
        var text = note?.Trim() ?? string.Empty;
        if (reason == OrderRejections.Other && text.Length == 0)
        {
            return "Write a remark for the reason Other.";
        }

        return text.Length > OrderRejections.NoteLength || text.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n'))
            ? "The remark can be up to 200 characters and cannot contain < or >."
            : null;
    }
}

public sealed record OrderCancellation(string Reason, string? Note, Guid CancelledBy);
