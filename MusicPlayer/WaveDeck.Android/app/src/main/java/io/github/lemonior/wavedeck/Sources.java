package io.github.lemonior.wavedeck;

import android.net.Uri;
import org.schabi.newpipe.extractor.ServiceList;
import org.schabi.newpipe.extractor.Page;
import org.schabi.newpipe.extractor.stream.*;
import org.schabi.newpipe.extractor.playlist.PlaylistInfo;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;

final class Sources {
    record Resolved(String url, StreamInfo info, long created) { }
    record ImportPage(List<Track> tracks, Page next, String url) { }
    private static final ConcurrentHashMap<String, Resolved> CACHE = new ConcurrentHashMap<>();
    private static final ConcurrentHashMap<String, FutureTask<Resolved>> IN_FLIGHT = new ConcurrentHashMap<>();
    static Resolved resolve(String source) throws IOException {
        Resolved cached = CACHE.get(source);
        if (cached != null && System.currentTimeMillis() - cached.created < 20 * 60_000) return cached;
        FutureTask<Resolved> task = new FutureTask<>(() -> {
            StreamInfo info = StreamInfo.getInfo(ServiceList.YouTube, source);
            AudioStream stream = info.getAudioStreams().stream().filter(item -> item.isUrl() && item.getDeliveryMethod() == DeliveryMethod.PROGRESSIVE_HTTP)
                .max(Comparator.comparingInt(item -> Math.min(192, item.getAverageBitrate()))).orElseThrow(() -> new IOException("No playable audio stream is available."));
            Resolved result = new Resolved(stream.getContent(), info, System.currentTimeMillis());
            if (CACHE.size() > 200) CACHE.clear(); CACHE.put(source, result); return result;
        });
        FutureTask<Resolved> active = IN_FLIGHT.putIfAbsent(source, task);
        if (active == null) { active = task; task.run(); }
        try { return active.get(60, TimeUnit.SECONDS); }
        catch (InterruptedException error) { Thread.currentThread().interrupt(); throw new IOException("Audio loading was interrupted.", error); }
        catch (ExecutionException | TimeoutException error) { throw new IOException("YouTube could not load this audio. The video may be unavailable or require verification.", error); }
        finally { IN_FLIGHT.remove(source, active); }
    }
    static void invalidate(String source) { CACHE.remove(SpotifyLinks.isSpotify(source)?SpotifySources.knownMatch(source):source); }
    static Uri resolveUri(Uri input) throws IOException {
        if (!"wavedeck".equals(input.getScheme())) return input;
        String source = input.getQueryParameter("url");
        if(source!=null&&SpotifyLinks.isSpotify(source)){
            Track track=new Track();track.source=SourceLinks.validate(source);track.title=Objects.toString(input.getQueryParameter("title"),"");track.artist=Objects.toString(input.getQueryParameter("artist"),"");
            try{track.durationMs=Long.parseLong(Objects.toString(input.getQueryParameter("duration"),"0"));}catch(NumberFormatException ignored){}
            return Uri.parse(resolve(SpotifySources.match(track)).url);
        }
        if (source == null || !SourceLinks.isYouTube(source)) throw new IOException("Invalid streaming source.");
        return Uri.parse(resolve(source).url);
    }
    static ImportPage importUrl(String input, boolean chapters, Page next) throws Exception {
        String url = SourceLinks.validate(input);
        if(SpotifyLinks.isSpotify(url))return new ImportPage(SpotifySources.read(url),null,url);
        if (!SourceLinks.isYouTube(url)) {
            Track track = new Track(); track.source = url;
            String name = Uri.parse(url).getLastPathSegment(); track.title = name == null || name.isEmpty() ? url : name;
            return new ImportPage(List.of(track), null, url);
        }
        boolean playlist = Uri.parse(url).getQueryParameter("list") != null;
        if (playlist) {
            List<StreamInfoItem> entries; Page after;
            if (next == null) { PlaylistInfo info = PlaylistInfo.getInfo(ServiceList.YouTube, url); entries = info.getRelatedItems(); after = info.getNextPage(); }
            else { var page = PlaylistInfo.getMoreItems(ServiceList.YouTube, url, next); entries = page.getItems(); after = page.getNextPage(); }
            List<Track> tracks = new ArrayList<>();
            for (var item : entries) {
                if (item.getUrl() == null || item.getUrl().isEmpty() || item.getName().equals("[Private video]") || item.getName().equals("[Deleted video]")) continue;
                Track track = new Track(); track.source = item.getUrl(); track.title = item.getName(); track.artist = item.getUploaderName(); track.durationMs = Math.max(0, item.getDuration()) * 1000;
                if (!item.getThumbnails().isEmpty()) track.artwork = item.getThumbnails().get(0).getUrl(); tracks.add(track);
            }
            return new ImportPage(tracks, after, url);
        }
        StreamInfo info = resolve(url).info; List<Track> tracks = new ArrayList<>();
        List<StreamSegment> segments = chapters ? info.getStreamSegments() : List.of();
        if (segments.size() > 1) {
            for (int i = 0; i < segments.size(); i++) {
                Track track = fromInfo(url, info); track.album = info.getName(); track.title = segments.get(i).getTitle(); track.startMs = segments.get(i).getStartTimeSeconds() * 1000L;
                track.endMs = i + 1 < segments.size() ? segments.get(i + 1).getStartTimeSeconds() * 1000L : info.getDuration() * 1000;
                track.durationMs = Math.max(0, track.endMs - track.startMs); if (track.durationMs > 0) tracks.add(track);
            }
        }
        if (tracks.isEmpty()) tracks.add(fromInfo(url, info)); return new ImportPage(tracks, null, url);
    }
    private static Track fromInfo(String url, StreamInfo info) {
        Track track = new Track(); track.source = url; track.title = info.getName(); track.artist = info.getUploaderName(); track.durationMs = Math.max(0, info.getDuration()) * 1000;
        if (!info.getThumbnails().isEmpty()) track.artwork = info.getThumbnails().get(0).getUrl(); return track;
    }
}
