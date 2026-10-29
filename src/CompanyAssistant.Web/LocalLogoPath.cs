internal static class LocalLogoPath
{
    private const string DefaultPath = "/logo.svg";

    // Accept only canonical root-relative asset paths. Reject encoding entirely so
    // browsers, proxies and static-file middleware cannot interpret them differently.
    public static string OrDefault(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024 || value[0] != '/')
            return DefaultPath;

        var segments = value[1..].Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment[0] == '.' || segment.EndsWith('.'))
                return DefaultPath;

            foreach (var character in segment)
            {
                if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                    return DefaultPath;
            }
        }

        return Path.GetExtension(value).ToLowerInvariant() is
            ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif" or ".ico"
            ? value
            : DefaultPath;
    }
}
