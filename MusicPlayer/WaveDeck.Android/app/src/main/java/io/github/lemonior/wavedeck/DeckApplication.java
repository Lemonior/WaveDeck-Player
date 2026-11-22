package io.github.lemonior.wavedeck;

import android.app.Application;
import org.schabi.newpipe.extractor.NewPipe;
import org.schabi.newpipe.extractor.localization.Localization;

public final class DeckApplication extends Application {
    @Override public void onCreate() {
        super.onCreate();
        NewPipe.init(new ExtractorDownloader(), new Localization("en", "US"));
    }
}
