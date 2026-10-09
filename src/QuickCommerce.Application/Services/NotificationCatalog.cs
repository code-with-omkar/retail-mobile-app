using System.Text.Json;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The words of a notification, before it is given to a customer.</summary>
public sealed record NoteText(string Type, string Category, string Title, string Message, string? DataJson);

/// <summary>
/// The one place that knows every kind of notification: its code, its category and its words in English and Marathi. Events (an order
/// accepted, a payment received) ask for a type here; the customer's list is written in their language from the type and the small data
/// stored with it (the order number), so the wording is changed in one place and old notifications follow. A type this list does not know
/// (older rows) is shown exactly as stored.
/// </summary>
public static class NotificationCatalog
{
    private sealed record Template(string Category, string TitleEn, string BodyEn, string TitleMr, string BodyMr);

    private static readonly Dictionary<string, Template> Templates = new()
    {
        [NotificationTypes.OrderPlaced] = new(NotificationCategories.Order, "Order placed", "Your order {orderNumber} has been placed.", "ऑर्डर नोंदवली", "तुमची ऑर्डर {orderNumber} नोंदवली गेली आहे."),
        [NotificationTypes.OrderAccepted] = new(NotificationCategories.Order, "Order accepted", "The store accepted your order {orderNumber}.", "ऑर्डर स्वीकारली", "दुकानाने तुमची ऑर्डर {orderNumber} स्वीकारली."),
        [NotificationTypes.OrderPacking] = new(NotificationCategories.Order, "Order is being packed", "Your order {orderNumber} is being packed.", "ऑर्डर पॅक होत आहे", "तुमची ऑर्डर {orderNumber} पॅक केली जात आहे."),
        [NotificationTypes.OutForDelivery] = new(NotificationCategories.Order, "Out for delivery", "Your order {orderNumber} is on its way.", "डिलिव्हरीसाठी निघाली", "तुमची ऑर्डर {orderNumber} तुमच्याकडे येत आहे."),
        [NotificationTypes.OrderDelivered] = new(NotificationCategories.Order, "Order delivered", "Your order {orderNumber} was delivered. Enjoy!", "ऑर्डर पोहोचली", "तुमची ऑर्डर {orderNumber} पोहोचली. आनंद घ्या!"),
        [NotificationTypes.OrderRejected] = new(NotificationCategories.Order, "Order declined", "The store could not accept your order {orderNumber}. If you paid, you will be refunded.", "ऑर्डर नाकारली", "दुकान तुमची ऑर्डर {orderNumber} स्वीकारू शकले नाही. पैसे दिले असल्यास ते परत केले जातील."),
        [NotificationTypes.OrderCancelled] = new(NotificationCategories.Order, "Order cancelled", "Your order {orderNumber} was cancelled.", "ऑर्डर रद्द झाली", "तुमची ऑर्डर {orderNumber} रद्द झाली."),
        [NotificationTypes.PaymentReceived] = new(NotificationCategories.Payment, "Payment received", "We received your payment. Your order {orderNumber} has been placed.", "पेमेंट मिळाले", "तुमचे पेमेंट मिळाले. तुमची ऑर्डर {orderNumber} नोंदवली गेली आहे."),
        [NotificationTypes.PaymentNotCompleted] = new(NotificationCategories.Payment, "Payment not completed", "Your order {orderNumber} was cancelled because the payment was not completed in time.", "पेमेंट पूर्ण झाले नाही", "वेळेत पेमेंट न झाल्यामुळे तुमची ऑर्डर {orderNumber} रद्द झाली."),
        [NotificationTypes.PaymentProblem] = new(NotificationCategories.Payment, "Payment problem", "The amount paid did not match your order {orderNumber}, so it will be refunded.", "पेमेंटमध्ये अडचण", "दिलेली रक्कम तुमच्या ऑर्डर {orderNumber} शी जुळली नाही, त्यामुळे ती परत केली जाईल."),
        [NotificationTypes.PaymentWillBeRefunded] = new(NotificationCategories.Payment, "Payment will be refunded", "Your payment arrived after your order {orderNumber} was cancelled, so it will be refunded in full.", "पेमेंट परत केले जाईल", "तुमची ऑर्डर {orderNumber} रद्द झाल्यानंतर पेमेंट आले, त्यामुळे संपूर्ण रक्कम परत केली जाईल."),
        [NotificationTypes.RefundProcessed] = new(NotificationCategories.Payment, "Refund processed", "Your refund for order {orderNumber} has been processed. It reaches your account in a few days.", "परतावा पूर्ण झाला", "ऑर्डर {orderNumber} चा परतावा पूर्ण झाला. तो काही दिवसांत तुमच्या खात्यात येईल."),
    };

    public static bool Knows(string type) => Templates.ContainsKey(type);

    /// <summary>The notification type for an order becoming this status.</summary>
    public static string TypeFor(OrderStatus status) => status switch
    {
        OrderStatus.Accepted => NotificationTypes.OrderAccepted,
        OrderStatus.Preparing => NotificationTypes.OrderPacking,
        OrderStatus.Ready or OrderStatus.OutForDelivery => NotificationTypes.OutForDelivery,
        OrderStatus.Completed or OrderStatus.Delivered => NotificationTypes.OrderDelivered,
        OrderStatus.Rejected => NotificationTypes.OrderRejected,
        OrderStatus.Cancelled => NotificationTypes.OrderCancelled,
        _ => NotificationTypes.OrderPlaced
    };

    private static string Data(string orderNumber) => JsonSerializer.Serialize(new Dictionary<string, string> { ["orderNumber"] = orderNumber });

    /// <summary>The words (English, for older apps) and data of a notification of this type about this order.</summary>
    public static NoteText Note(string type, string orderNumber)
    {
        var template = Templates[type];
        return new NoteText(type, template.Category, Fill(template.TitleEn, orderNumber), Fill(template.BodyEn, orderNumber), Data(orderNumber));
    }

    public static Notification Create(Guid customerId, Guid orderId, string type, string orderNumber) => From(customerId, orderId, Note(type, orderNumber));

    public static Notification ForStatus(Guid customerId, Order order, OrderStatus status) => Create(customerId, order.Id, TypeFor(status), order.OrderNumber);

    public static Notification From(Guid customerId, Guid? orderId, NoteText note) => new()
    {
        CustomerId = customerId,
        OrderId = orderId,
        Type = note.Type,
        Category = note.Category,
        Title = note.Title,
        Message = note.Message,
        DataJson = note.DataJson
    };

    /// <summary>
    /// The title and message in the customer's language ("mr" for Marathi, anything else English). Known types are written from their
    /// template; an offer uses its campaign's own words; anything else is shown as it was stored.
    /// </summary>
    public static (string Title, string Message) Render(Notification notification, string? language)
    {
        var marathi = string.Equals(language, "mr", StringComparison.OrdinalIgnoreCase);
        if (notification.Campaign is { } campaign)
        {
            return marathi && !string.IsNullOrWhiteSpace(campaign.TitleMr) && !string.IsNullOrWhiteSpace(campaign.BodyMr)
                ? (campaign.TitleMr, campaign.BodyMr)
                : (campaign.TitleEn, campaign.BodyEn);
        }

        if (!Templates.TryGetValue(notification.Type, out var template))
        {
            return (notification.Title, notification.Message);
        }

        var orderNumber = ReadOrderNumber(notification.DataJson);
        if (orderNumber is null)
        {
            return (notification.Title, notification.Message);
        }

        return marathi
            ? (template.TitleMr, Fill(template.BodyMr, orderNumber))
            : (Fill(template.TitleEn, orderNumber), Fill(template.BodyEn, orderNumber));
    }

    private static string Fill(string text, string orderNumber) => text.Replace("{orderNumber}", orderNumber, StringComparison.Ordinal);

    private static string? ReadOrderNumber(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(dataJson);
            return json.RootElement.TryGetProperty("orderNumber", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
