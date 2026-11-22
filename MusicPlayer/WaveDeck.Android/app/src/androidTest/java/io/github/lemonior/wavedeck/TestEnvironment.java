package io.github.lemonior.wavedeck;

import android.Manifest;
import android.os.Build;
import androidx.test.rule.GrantPermissionRule;
import org.junit.rules.TestRule;

final class TestEnvironment {
    static TestRule notifications() {
        return Build.VERSION.SDK_INT >= 33 ? GrantPermissionRule.grant(Manifest.permission.POST_NOTIFICATIONS) : (statement, description) -> statement;
    }
}
