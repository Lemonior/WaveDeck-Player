package io.github.lemonior.wavedeck;

import android.net.Uri;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import org.json.JSONObject;
import java.util.UUID;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
final class Track {
    String id = UUID.randomUUID().toString();
    String title = "", artist = "", source = "", artwork = "", album = "";
    long durationMs, startMs, endMs = -1;
    boolean failed;

    static Track fromJson(JSONObject json) {
        Track track = new Track(); track.id = json.optString("id", track.id);
        track.title = json.optString("title"); track.artist = json.optString("artist");
        track.source = json.optString("source"); track.artwork = json.optString("artwork"); track.album = json.optString("album");
        track.durationMs = Math.max(0, json.optLong("durationMs")); track.startMs = Math.max(0, json.optLong("startMs"));
        track.endMs = json.optLong("endMs", -1); return track;
    }
    JSONObject json() {
        JSONObject json = new JSONObject();
        try { json.put("id", id).put("title", title).put("artist", artist).put("source", source).put("artwork", artwork).put("album", album)
            .put("durationMs", durationMs).put("startMs", startMs).put("endMs", endMs); }
        catch (org.json.JSONException error) { throw new IllegalStateException(error); } return json;
    }
    MediaItem mediaItem() {
        MediaMetadata.Builder metadata = new MediaMetadata.Builder().setTitle(title).setArtist(artist).setAlbumTitle(album).setIsPlayable(true);
        if (!artwork.isEmpty()) metadata.setArtworkUri(Uri.parse(artwork));
        android.os.Bundle extras = new android.os.Bundle(); extras.putString("track", json().toString()); metadata.setExtras(extras);
        Uri uri = SourceLinks.isYouTube(source)||SpotifyLinks.isSpotify(source) ? new Uri.Builder().scheme("wavedeck").authority("stream").appendQueryParameter("url", source).appendQueryParameter("title",title).appendQueryParameter("artist",artist).appendQueryParameter("duration",Long.toString(durationMs)).build() : Uri.parse(source);
        MediaItem.Builder item = new MediaItem.Builder().setMediaId(id).setUri(uri).setMediaMetadata(metadata.build());
        if (SourceLinks.isNetwork(source)) item.setCustomCacheKey(SourceLinks.cacheKey(source));
        if (startMs > 0 || endMs > startMs) {
            MediaItem.ClippingConfiguration.Builder clip = new MediaItem.ClippingConfiguration.Builder().setStartPositionMs(startMs);
            if (endMs > startMs) clip.setEndPositionMs(endMs); item.setClippingConfiguration(clip.build());
        }
        return item.build();
    }
    static Track fromItem(MediaItem item) {
        try {
            if (item.mediaMetadata.extras != null && item.mediaMetadata.extras.containsKey("track"))
                return fromJson(new JSONObject(item.mediaMetadata.extras.getString("track", "{}")));
        } catch (org.json.JSONException ignored) { }
        Track track = new Track(); track.id = item.mediaId; track.title = String.valueOf(item.mediaMetadata.title);
        if (item.localConfiguration != null) track.source = item.localConfiguration.uri.toString(); return track;
    }
}
