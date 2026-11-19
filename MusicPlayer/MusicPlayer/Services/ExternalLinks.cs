using System.Diagnostics;

namespace MusicPlayer.Services;

public static class ExternalLinks
{
    public const string GoogleSignIn = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";
    internal static ProcessStartInfo LinkInfo(string url) => new(UrlService.Validate(url).AbsoluteUri) { UseShellExecute = true, Verb = "open" };
    public static void Open(string url) => Process.Start(LinkInfo(url));
}
