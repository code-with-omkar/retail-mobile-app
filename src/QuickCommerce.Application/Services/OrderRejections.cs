namespace QuickCommerce.Application.Services;

/// <summary>Why a store declines an order. The reason is required; the note is for the team and is never shown to the customer.</summary>
public static class OrderRejections
{
    public const string Other = "Other";
    public const string NotRecorded = "NotRecorded";
    public const int NoteLength = 200;

    public static readonly string[] Reasons = ["OutOfStock", "StoreClosed", "TooBusy", "AddressProblem", Other];

    /// <summary>Why this rejection cannot be accepted; null when it can.</summary>
    public static string? Problem(string? reason, string? note)
    {
        if (reason is null || !Reasons.Contains(reason))
        {
            return "Choose why the order is being rejected.";
        }

        var text = note?.Trim() ?? string.Empty;
        if (reason == Other && text.Length == 0)
        {
            return "Write a remark for the reason Other.";
        }

        return text.Length > NoteLength || text.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n'))
            ? "The remark can be up to 200 characters and cannot contain < or >."
            : null;
    }

    /// <summary>What the customer is told, in English and Marathi; null when there is nothing to add.</summary>
    public static (string En, string Mr)? ForCustomer(string? reason) => reason switch
    {
        "OutOfStock" => ("Some items are out of stock.", "काही वस्तू संपल्या आहेत."),
        "StoreClosed" => ("The store is closed right now.", "दुकान सध्या बंद आहे."),
        "TooBusy" => ("The store is too busy right now.", "दुकान सध्या खूप व्यस्त आहे."),
        "AddressProblem" => ("The store could not deliver to your address.", "दुकान तुमच्या पत्त्यावर डिलिव्हरी करू शकले नाही."),
        _ => null
    };
}

/// <summary>A store's rejection of an order, as it is saved.</summary>
public sealed record OrderRejection(string Reason, string? Note, Guid RejectedBy);
