package io.github.lemonior.wavedeck;

import android.content.Context;
import android.util.AtomicFile;
import androidx.media3.common.MediaItem;
import androidx.media3.common.Player;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
final class StateStore {
    record Snapshot(List<MediaItem> items, int index, long position, boolean shuffle, int repeat) { }
    private final AtomicFile file;
    StateStore(Context context) { file = new AtomicFile(new File(context.getFilesDir(), "playback.json")); }
    synchronized Snapshot load() {
        try {
            if (file.getBaseFile().length() > 32 * 1024 * 1024) return empty();
            JSONObject json = new JSONObject(new String(file.readFully(), StandardCharsets.UTF_8));
            JSONArray entries = json.optJSONArray("queue"); List<MediaItem> items = new ArrayList<>();
            if (entries != null) for (int i = 0; i < Math.min(50_000, entries.length()); i++) {
                Track track = Track.fromJson(entries.getJSONObject(i));
                if (SourceLinks.isNetwork(track.source) || track.source.startsWith("content://") || track.source.startsWith("asset:///")) items.add(track.mediaItem());
            }
            int index = items.isEmpty() ? 0 : Math.max(0, Math.min(items.size() - 1, json.optInt("index")));
            return new Snapshot(items, index, Math.max(0, json.optLong("position")), json.optBoolean("shuffle"), Math.max(0, Math.min(2, json.optInt("repeat"))));
        } catch (IOException | JSONException | RuntimeException error) { return empty(); }
    }
    private static Snapshot empty() { return new Snapshot(List.of(), 0, 0, false, Player.REPEAT_MODE_OFF); }
    static JSONObject capture(Player player) {
        JSONArray tracks = new JSONArray(); for (int i = 0; i < player.getMediaItemCount(); i++) tracks.put(Track.fromItem(player.getMediaItemAt(i)).json());
        JSONObject state = new JSONObject();
        try { state.put("queue", tracks).put("index", Math.max(0, player.getCurrentMediaItemIndex())).put("position", Math.max(0, player.getCurrentPosition()))
            .put("shuffle", player.getShuffleModeEnabled()).put("repeat", player.getRepeatMode()); }
        catch (JSONException error) { throw new IllegalStateException(error); } return state;
    }
    synchronized void save(JSONObject state) {
        FileOutputStream stream = null;
        try { stream = file.startWrite(); stream.write(state.toString().getBytes(StandardCharsets.UTF_8)); file.finishWrite(stream); }
        catch (IOException error) { if (stream != null) file.failWrite(stream); }
    }
}
