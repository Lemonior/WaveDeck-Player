using System.Text.RegularExpressions;

namespace MusicPlayer.Services;

public enum AudioErrorKind { AccessDenied, Unavailable, Decoder }
public sealed class AudioSourceException(AudioErrorKind kind, string message) : Exception(message)
{
    public AudioErrorKind Kind { get; } = kind;
}
public static partial class AudioError
{
    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)] private static partial Regex Urls();
    public static bool IsDenied(Exception ex) => ex is AudioSourceException { Kind: AudioErrorKind.AccessDenied } || ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
    public static string SafeMessage(string raw)
    {
        if (raw.Contains("403", StringComparison.OrdinalIgnoreCase) || raw.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)) return L.T("The audio server rejected this link (403). Retry playback or update the YouTube tool in Settings.");
        if (raw.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || raw.Contains("private", StringComparison.OrdinalIgnoreCase) || raw.Contains("members-only", StringComparison.OrdinalIgnoreCase)) return L.T("This video needs an eligible YouTube account. Connect your account from Settings and try again.");
        var text = Urls().Replace(raw, "[audio source]").Replace('\r', ' ').Replace('\n', ' ').Trim();
        text = L.T(text);
        return text.Length <= 280 ? text : text[..277] + "…";
    }
}
