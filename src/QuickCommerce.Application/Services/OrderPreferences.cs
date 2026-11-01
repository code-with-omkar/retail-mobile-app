namespace QuickCommerce.Application.Services;

/// <summary>The note a customer leaves for the store and what to do about an item that is out of stock. One rule for checkout and its tests.</summary>
public static class OrderPreferences
{
    public const string Remove = "Remove";
    public const string CallMe = "CallMe";
    public const string Replace = "Replace";
    public const int NoteMax = 200;

    /// <summary>Returns the cleaned note (null when empty), the choice (CallMe when none was given) and a message when something is wrong.</summary>
    public static (string? Note, string Choice, string? Problem) Check(string? note, string? choice)
    {
        var chosen = string.IsNullOrWhiteSpace(choice) ? CallMe : choice.Trim();
        if (chosen is not (Remove or CallMe or Replace))
        {
            return (null, CallMe, "Choose Remove, CallMe or Replace for an item that is out of stock.");
        }

        var clean = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (clean is not null && (clean.Length > NoteMax || clean.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n'))))
        {
            return (null, chosen, clean.Length > NoteMax ? $"A note can be up to {NoteMax} characters." : "The note cannot contain < or > or control characters.");
        }

        return (clean, chosen, null);
    }
}
