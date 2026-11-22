package io.github.lemonior.wavedeck;

import android.content.Context;
import android.net.Uri;
import androidx.media3.database.StandaloneDatabaseProvider;
import androidx.media3.datasource.*;
import androidx.media3.datasource.cache.*;
import androidx.media3.exoplayer.offline.DownloadManager;
import java.io.File;
import java.util.concurrent.Executors;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
final class MediaRuntime {
    private static SimpleCache streamCache, offlineCache;
    private static DownloadManager downloads;
    private static StandaloneDatabaseProvider database;
    static synchronized SimpleCache streamCache(Context context) {
        if (database == null) database = new StandaloneDatabaseProvider(context.getApplicationContext());
        if (streamCache == null) streamCache = new SimpleCache(new File(context.getCacheDir(), "streams"), new LeastRecentlyUsedCacheEvictor(256L * 1024 * 1024), database);
        return streamCache;
    }
    static synchronized SimpleCache offlineCache(Context context) {
        streamCache(context);
        if (offlineCache == null) offlineCache = new SimpleCache(new File(context.getFilesDir(), "offline-audio"), new NoOpCacheEvictor(), database);
        return offlineCache;
    }
    static DataSource.Factory network(Context context) {
        DefaultHttpDataSource.Factory http = new DefaultHttpDataSource.Factory().setUserAgent(ExtractorDownloader.USER_AGENT).setConnectTimeoutMs(15_000).setReadTimeoutMs(20_000);
        DataSource.Factory source = new DefaultDataSource.Factory(context.getApplicationContext(), http);
        return new ResolvingDataSource.Factory(source, spec -> spec.withUri(Sources.resolveUri(spec.uri)));
    }
    static CacheDataSource.Factory streaming(Context context) {
        return new CacheDataSource.Factory().setCache(streamCache(context)).setUpstreamDataSourceFactory(network(context)).setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR);
    }
    static DataSource.Factory playback(Context context) {
        var cached = new CacheDataSource.Factory().setCache(offlineCache(context)).setCacheWriteDataSinkFactory(null).setUpstreamDataSourceFactory(streaming(context)).setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR);
        return new DefaultDataSource.Factory(context.getApplicationContext(), cached);
    }
    static synchronized DownloadManager downloads(Context context) {
        if (downloads == null) {
            var cache = offlineCache(context);
            downloads = new DownloadManager(context.getApplicationContext(), database, cache, network(context), Executors.newFixedThreadPool(2));
            downloads.setMaxParallelDownloads(2); downloads.resumeDownloads();
        }
        return downloads;
    }
    static DataSpec spec(Track track, long length) {
        Uri uri = track.mediaItem().localConfiguration.uri;
        return new DataSpec.Builder().setUri(uri).setKey(SourceLinks.cacheKey(track.source)).setLength(length).build();
    }
}
