package io.github.lemonior.wavedeck;

import android.content.Context;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.runner.RunWith;
import static androidx.test.espresso.Espresso.*;
import static androidx.test.espresso.action.ViewActions.*;
import static androidx.test.espresso.assertion.ViewAssertions.*;
import static androidx.test.espresso.matcher.ViewMatchers.*;
import static androidx.test.espresso.matcher.RootMatchers.*;
import static org.hamcrest.Matchers.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class SettingsIntegrationTest {
    @org.junit.Rule public org.junit.rules.TestRule notifications=TestEnvironment.notifications();
    @Test public void languageCancelAndSaveWorkWhileKeepingPlaybackSettings()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();var prefs=context.getSharedPreferences("settings",Context.MODE_PRIVATE);
        prefs.edit().putString("language","en").putString("theme","Modern").putBoolean("startupUpdates",false).putFloat("g0",3).putFloat("tempo",1.25f).commit();
        try(ActivityScenario<MainActivity> activity=ActivityScenario.launch(MainActivity.class)){
            onView(withContentDescription("Settings")).perform(click());
            onView(withText("English")).inRoot(isDialog()).perform(click());
            onView(withText("Português (Brasil)")).inRoot(focusedWindow()).perform(click());
            onView(withText("Cancel")).inRoot(isDialog()).perform(click());
            assertEquals("en",prefs.getString("language",""));onView(withContentDescription("Settings")).check(matches(isDisplayed()));
            onView(withContentDescription("Settings")).perform(click());
            onView(withText("English")).inRoot(isDialog()).perform(click());
            onView(withText("Português (Brasil)")).inRoot(focusedWindow()).perform(click());
            onView(withText("Save")).inRoot(isDialog()).perform(click());
            onView(withContentDescription("Configurações")).check(matches(isDisplayed()));assertEquals("pt-BR",prefs.getString("language",""));assertEquals(3,prefs.getFloat("g0",0),0);assertEquals(1.25,prefs.getFloat("tempo",0),.001);
        }finally{prefs.edit().putString("language","en").putFloat("tempo",1).putFloat("g0",0).commit();}
    }
    private static org.hamcrest.Matcher<androidx.test.espresso.Root> focusedWindow(){
        return new org.hamcrest.TypeSafeMatcher<androidx.test.espresso.Root>(){
            @Override public void describeTo(org.hamcrest.Description description){description.appendText("the focused window");}
            @Override protected boolean matchesSafely(androidx.test.espresso.Root root){return root.getDecorView().hasWindowFocus();}
        };
    }
}
