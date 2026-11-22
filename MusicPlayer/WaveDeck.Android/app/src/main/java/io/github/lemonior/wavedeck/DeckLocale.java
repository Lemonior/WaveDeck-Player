package io.github.lemonior.wavedeck;

import android.content.Context;
import android.content.res.Configuration;
import android.os.LocaleList;
import java.util.Locale;

final class DeckLocale {
    static Context wrap(Context context) {
        String code = context.getSharedPreferences("settings", Context.MODE_PRIVATE).getString("language", "en");
        Configuration configuration = new Configuration(context.getResources().getConfiguration());
        configuration.setLocales(new LocaleList(Locale.forLanguageTag("pt-BR".equals(code) ? "pt-BR" : "en")));
        return context.createConfigurationContext(configuration);
    }
}
