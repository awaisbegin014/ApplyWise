namespace ApplyWise.Web.Services.Security;

public static class MailtoLinkBuilder
{
    public static string Build(string address, string? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var link = $"mailto:{Uri.EscapeDataString(address.Trim())}";
        var safeSubject = subject is null
            ? null
            : string.Concat(subject.Select(character =>
                char.IsControl(character) ? ' ' : character)).Trim();
        return string.IsNullOrEmpty(safeSubject)
            ? link
            : $"{link}?subject={Uri.EscapeDataString(safeSubject)}";
    }
}
