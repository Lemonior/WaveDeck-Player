package io.github.lemonior.wavedeck;

import android.app.Notification;
import androidx.media3.exoplayer.offline.*;
import androidx.media3.exoplayer.scheduler.Scheduler;
import androidx.media3.exoplayer.offline.DownloadNotificationHelper;
import java.util.List;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public final class WaveDownloadService extends DownloadService {
    private DownloadNotificationHelper notification;
    public WaveDownloadService() { super(2002, DEFAULT_FOREGROUND_NOTIFICATION_UPDATE_INTERVAL, "wavedeck-downloads", R.string.downloads, 0); }
    @Override public void onCreate() { super.onCreate(); notification = new DownloadNotificationHelper(this, "wavedeck-downloads"); }
    @Override protected DownloadManager getDownloadManager() { return MediaRuntime.downloads(this); }
    @Override protected Scheduler getScheduler() { return null; }
    @Override protected Notification getForegroundNotification(List<Download> downloads, int unmetRequirements) {
        if (notification == null) notification = new DownloadNotificationHelper(this, "wavedeck-downloads");
        return notification.buildProgressNotification(this, android.R.drawable.stat_sys_download, null, getString(R.string.downloading_offline), downloads, unmetRequirements);
    }
}
