package io.github.lemonior.wavedeck;

import android.app.PendingIntent;
import android.content.*;
import android.os.*;
import android.view.KeyEvent;
import androidx.media3.common.*;
import androidx.media3.datasource.cache.CacheWriter;
import androidx.media3.exoplayer.*;
import androidx.media3.exoplayer.audio.*;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.exoplayer.offline.Download;
import androidx.media3.exoplayer.offline.DownloadManager;
import androidx.media3.session.*;
import com.google.common.util.concurrent.*;
import java.util.*;
import java.util.concurrent.*;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public final class PlaybackService extends MediaSessionService {
    static final String SETTINGS = "wavedeck.settings";
    static final String AUTO_DOWNLOAD = "wavedeck.auto-download";
    static volatile DspEngine visual;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService storageWorker = Executors.newSingleThreadExecutor();
    private final ExecutorService preloadWorker = Executors.newSingleThreadExecutor();
    private final Map<String, CacheWriter> warmers = new HashMap<>();
    private final Set<String> failed = new HashSet<>();
    private ExoPlayer player;
    private MediaSession session;
    private StateStore store;
    private DeckAudioProcessor processor;
    private int recovery, headsetTaps;
    private String currentId = "", preloadSignature = "";
    private long lastSave;
    private boolean restoring = true;
    private String pendingDownloadKey, pendingDownloadItem;
    private boolean pendingDownloadPaused;
    private DownloadManager observedDownloads;
    private final DownloadManager.Listener downloadListener = new DownloadManager.Listener() {
        @Override public void onDownloadChanged(DownloadManager manager, Download download, Exception error) {
            if (!download.request.id.equals(pendingDownloadKey)) return;
            if (download.state == Download.STATE_FAILED || download.state == Download.STATE_REMOVING) { pendingDownloadKey = pendingDownloadItem = null; return; }
            if (download.state != Download.STATE_COMPLETED || player == null) return;
            String item = pendingDownloadItem; boolean paused = pendingDownloadPaused; pendingDownloadKey = pendingDownloadItem = null;
            for (int i = 0; i < player.getMediaItemCount(); i++) if (player.getMediaItemAt(i).mediaId.equals(item)) {
                player.seekToDefaultPosition(i); player.prepare(); player.setPlayWhenReady(!paused); break;
            }
        }
    };
    private final Runnable headset = () -> {
        int taps = headsetTaps; headsetTaps = 0;
        if (taps == 1) { if (player.getPlayWhenReady() && player.getPlaybackState() != Player.STATE_IDLE && player.getPlaybackState() != Player.STATE_ENDED) player.pause(); else playFromHeadset(); }
        else { pendingDownloadKey = pendingDownloadItem = null; if (taps == 2) player.seekToNextMediaItem(); else player.seekToPreviousMediaItem(); }
    };
    private void playFromHeadset() {
        if (player.getMediaItemCount() > 0) { int state = player.getPlaybackState(); if (state == Player.STATE_IDLE || state == Player.STATE_ENDED) player.pause(); if (state == Player.STATE_ENDED) player.seekToDefaultPosition(); if (state == Player.STATE_IDLE) player.prepare(); player.play(); return; }
        storageWorker.execute(() -> {
            StateStore.Snapshot saved = store.load();
            main.post(() -> {
                if (player == null) return;
                if (player.getMediaItemCount() == 0 && !saved.items().isEmpty()) { player.setMediaItems(saved.items(), saved.index(), saved.position()); player.setShuffleModeEnabled(saved.shuffle()); player.setRepeatMode(saved.repeat()); }
                if (player.getMediaItemCount() > 0) { player.prepare(); player.play(); }
            });
        });
    }
    private final Runnable tick = new Runnable() {
        @Override public void run() {
            if (player == null) return;
            if (SystemClock.elapsedRealtime() - lastSave >= 10_000) save();
            if (player.isPlaying()) preloadNext();
            main.postDelayed(this, 1000);
        }
    };
    @Override public void onCreate() {
        super.onCreate(); store = new StateStore(this); processor = new DeckAudioProcessor(); visual = processor.engine;
        DefaultRenderersFactory renderers = new DefaultRenderersFactory(this) {
            @Override protected AudioSink buildAudioSink(Context context, boolean enableFloatOutput, boolean enableAudioOutputPlaybackParameters) {
                return new DefaultAudioSink.Builder(context).setAudioProcessors(new androidx.media3.common.audio.AudioProcessor[]{processor}).setEnableFloatOutput(false).build();
            }
        };
        player = new ExoPlayer.Builder(this, renderers).setMediaSourceFactory(new DefaultMediaSourceFactory(MediaRuntime.playback(this)))
            .setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
            .setHandleAudioBecomingNoisy(true).setWakeMode(C.WAKE_MODE_NETWORK).setSeekBackIncrementMs(10_000).setSeekForwardIncrementMs(10_000).build();
        player.setTrackSelectionParameters(player.getTrackSelectionParameters().buildUpon().setTrackTypeDisabled(C.TRACK_TYPE_VIDEO, true).build());
        player.setPreloadConfiguration(new ExoPlayer.PreloadConfiguration(5_000_000));
        Intent open = new Intent(this, MainActivity.class).setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent activity = PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        session = new MediaSession.Builder(this, player).setSessionActivity(activity).setCallback(new SessionCallback()).build();
        applySettings();
        player.addListener(new Player.Listener() {
            @Override public void onMediaItemTransition(MediaItem item, int reason) {
                String next = item == null ? "" : item.mediaId;
                if (!next.equals(currentId)) { recovery = 0; currentId = next; }
                preloadSignature = ""; save();
            }
            @Override public void onEvents(Player observed, Player.Events events) {
                if (events.contains(Player.EVENT_TIMELINE_CHANGED)) {
                    Set<String> present = new HashSet<>(); for (int i = 0; i < player.getMediaItemCount(); i++) present.add(player.getMediaItemAt(i).mediaId); failed.retainAll(present);
                }
                if (events.contains(Player.EVENT_TIMELINE_CHANGED) || events.contains(Player.EVENT_SHUFFLE_MODE_ENABLED_CHANGED) || events.contains(Player.EVENT_REPEAT_MODE_CHANGED)) { preloadSignature = ""; save(); }
                if (events.contains(Player.EVENT_IS_PLAYING_CHANGED)) save();
                if (player.getPlaybackState() == Player.STATE_READY && player.getCurrentMediaItem() != null) failed.remove(player.getCurrentMediaItem().mediaId);
            }
            @Override public void onPlayerError(PlaybackException error) {
                MediaItem item = player.getCurrentMediaItem(); if (item == null) return;
                Track track = Track.fromItem(item);
                if (SourceLinks.isYouTube(track.source) && recovery++ < 1) { Sources.invalidate(track.source); player.prepare(); return; }
                failed.add(item.mediaId); Bundle extras = new Bundle(); extras.putString("error", track.title); session.setSessionExtras(extras);
                if (failed.size() < player.getMediaItemCount() && player.hasNextMediaItem()) { player.seekToNextMediaItem(); player.prepare(); }
                else { player.pause(); player.stop(); }
            }
        });
        storageWorker.execute(() -> {
            StateStore.Snapshot saved = store.load(); main.post(() -> {
                restoring = false;
                if (player != null && player.getMediaItemCount() == 0 && !saved.items().isEmpty()) {
                    player.setMediaItems(saved.items(), saved.index(), saved.position()); player.setShuffleModeEnabled(saved.shuffle()); player.setRepeatMode(saved.repeat());
                    // Restoring the app prepares only on an explicit Play/media-button command.
                }
            });
        });
        main.post(tick);
    }
    private void applySettings() {
        SharedPreferences prefs = getSharedPreferences("settings", MODE_PRIVATE);
        processor.settings = DeckAudioProcessor.read(prefs);
        player.setVolume((float)Math.pow(Math.max(0, Math.min(1, prefs.getFloat("volume", .65f))), 1.660964047443681));
        boolean bypass = prefs.getBoolean("bypass", false); float tempo = bypass ? 1 : prefs.getFloat("tempo", 1);
        float pitch = bypass ? 1 : (float)Math.pow(2, prefs.getFloat("pitch", 0) / 12);
        if (!bypass && prefs.getBoolean("tape", false)) pitch *= tempo;
        player.setPlaybackParameters(new PlaybackParameters(Math.max(.5f, Math.min(2, tempo)), Math.max(.25f, Math.min(4, pitch))));
    }
    private void save() {
        if (player == null || store == null || restoring) return;
        lastSave = SystemClock.elapsedRealtime(); var state = StateStore.capture(player);
        storageWorker.execute(() -> store.save(state));
    }
    private void preloadNext() {
        Timeline timeline = player.getCurrentTimeline(); if (timeline.isEmpty()) return;
        List<Track> tracks = new ArrayList<>(); Set<Integer> indices = new HashSet<>(); int index = player.getCurrentMediaItemIndex();
        for (int i = 0; i < 3; i++) {
            index = timeline.getNextWindowIndex(index, player.getRepeatMode(), player.getShuffleModeEnabled());
            if (index == C.INDEX_UNSET || index == player.getCurrentMediaItemIndex() || !indices.add(index)) break;
            Track track = Track.fromItem(player.getMediaItemAt(index)); if (SourceLinks.isNetwork(track.source)) tracks.add(track);
        }
        String signature = tracks.stream().map(track -> track.id).reduce("", (a,b) -> a + ":" + b);
        if (signature.equals(preloadSignature)) return; preloadSignature = signature;
        for (var writer : warmers.values()) writer.cancel(); warmers.clear();
        for (Track track : tracks) {
            CacheWriter writer = new CacheWriter(MediaRuntime.streaming(this).createDataSource(), MediaRuntime.spec(track, 512 * 1024), null, null);
            warmers.put(track.id, writer); preloadWorker.execute(() -> { try { writer.cache(); } catch (Exception ignored) { /* Background preparation never notifies or changes a track's failure state. */ } });
        }
    }
    @Override public MediaSession onGetSession(MediaSession.ControllerInfo controllerInfo) { return session; }
    @Override public void onDestroy() {
        main.removeCallbacksAndMessages(null);
        if (observedDownloads != null) observedDownloads.removeListener(downloadListener);
        if (player != null) { var state = StateStore.capture(player); storageWorker.execute(() -> store.save(state)); player.release(); player = null; }
        for (var writer : warmers.values()) writer.cancel(); warmers.clear(); preloadWorker.shutdownNow();
        if (session != null) { session.release(); session = null; }
        storageWorker.shutdown(); visual = null; super.onDestroy();
    }
    private final class SessionCallback implements MediaSession.Callback {
        @Override public ListenableFuture<MediaSession.ConnectionResult> onConnectAsync(MediaSession active, MediaSession.ControllerInfo controller) {
            var result = new MediaSession.ConnectionResult.AcceptedResultBuilder(active, controller);
            boolean own = getPackageName().equals(controller.getPackageName());
            if (own) result.setAvailableSessionCommands(MediaSession.ConnectionResult.DEFAULT_SESSION_COMMANDS.buildUpon().add(new SessionCommand(SETTINGS, Bundle.EMPTY)).add(new SessionCommand(AUTO_DOWNLOAD, Bundle.EMPTY)).build());
            else result.setAvailablePlayerCommands(MediaSession.ConnectionResult.DEFAULT_PLAYER_COMMANDS.buildUpon().remove(Player.COMMAND_CHANGE_MEDIA_ITEMS).build());
            return Futures.immediateFuture(result.build());
        }
        @Override public ListenableFuture<SessionResult> onCustomCommand(MediaSession active, MediaSession.ControllerInfo controller, SessionCommand command, Bundle args) {
            if (getPackageName().equals(controller.getPackageName()) && SETTINGS.equals(command.customAction)) { applySettings(); return Futures.immediateFuture(new SessionResult(SessionResult.RESULT_SUCCESS)); }
            if (getPackageName().equals(controller.getPackageName()) && AUTO_DOWNLOAD.equals(command.customAction)) {
                pendingDownloadKey = args.getString("key"); pendingDownloadItem = args.getString("item");
                pendingDownloadPaused = player.getPlaybackState() == Player.STATE_READY && !player.getPlayWhenReady();
                if (observedDownloads == null) { observedDownloads = MediaRuntime.downloads(PlaybackService.this); observedDownloads.addListener(downloadListener); }
                try { Download saved = observedDownloads.getDownloadIndex().getDownload(pendingDownloadKey); if (saved != null && saved.state == Download.STATE_COMPLETED) downloadListener.onDownloadChanged(observedDownloads, saved, null); } catch (java.io.IOException ignored) { }
                return Futures.immediateFuture(new SessionResult(SessionResult.RESULT_SUCCESS));
            }
            return Futures.immediateFuture(new SessionResult(SessionError.ERROR_NOT_SUPPORTED));
        }
        @Override public int onPlayerCommandRequest(MediaSession active, MediaSession.ControllerInfo controller, int command) {
            if (command == Player.COMMAND_STOP || command == Player.COMMAND_SEEK_TO_MEDIA_ITEM || command == Player.COMMAND_SEEK_TO_NEXT || command == Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM || command == Player.COMMAND_SEEK_TO_PREVIOUS || command == Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM) pendingDownloadKey = pendingDownloadItem = null;
            return SessionResult.RESULT_SUCCESS;
        }
        @Override public ListenableFuture<MediaSession.MediaItemsWithStartPosition> onPlaybackResumption(MediaSession active, MediaSession.ControllerInfo controller, boolean isForPlayback) {
            SettableFuture<MediaSession.MediaItemsWithStartPosition> result = SettableFuture.create();
            storageWorker.execute(() -> {
                StateStore.Snapshot saved = store.load();
                if (saved.items().isEmpty()) result.setException(new IllegalStateException("The playlist is empty."));
                else result.set(new MediaSession.MediaItemsWithStartPosition(saved.items(), saved.index(), saved.position()));
            }); return result;
        }
        @Override public boolean onMediaButtonEvent(MediaSession active, MediaSession.ControllerInfo controller, Intent intent) {
            KeyEvent event = Build.VERSION.SDK_INT >= 33 ? intent.getParcelableExtra(Intent.EXTRA_KEY_EVENT, KeyEvent.class) : intent.getParcelableExtra(Intent.EXTRA_KEY_EVENT);
            if (event == null || event.getKeyCode() != KeyEvent.KEYCODE_HEADSETHOOK) return false;
            if (event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 0) { headsetTaps++; main.removeCallbacks(headset); main.postDelayed(headset, 300); }
            return true;
        }
    }
}
