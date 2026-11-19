using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static class UiSmokeTest
{
    public static Task RunAsync(MainWindow window, Storage storage) => window.ValidateUiAsync(storage);
}
