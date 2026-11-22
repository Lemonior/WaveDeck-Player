package io.github.lemonior.wavedeck;

import org.json.*;
import org.schabi.newpipe.extractor.ServiceList;
import org.schabi.newpipe.extractor.search.SearchInfo;
import org.schabi.newpipe.extractor.stream.StreamInfoItem;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.regex.Pattern;

// Public metadata -> YouTube search. Never reads Spotify audio previews, accounts or cookies.
final class SpotifySources {
    record Metadata(String source,String title,String artist,long duration,String artwork){}
    private record Saved(long when,List<Metadata> tracks){}
    private static final Map<String,Saved> PAGES=new ConcurrentHashMap<>();
    private static final Map<String,String> MATCHES=new ConcurrentHashMap<>();
    private static final Map<String,FutureTask<String>> SEARCHES=new ConcurrentHashMap<>();
    private static final Pattern DATA=Pattern.compile("<script\\b[^>]*\\bid\\s*=\\s*[\"']__NEXT_DATA__[\"'][^>]*>(.*?)</script\\s*>",Pattern.DOTALL|Pattern.CASE_INSENSITIVE);
    static String knownMatch(String source){return MATCHES.getOrDefault(source,"");}
    static List<Track> read(String input)throws Exception{
        String url=expand(SpotifyLinks.normalize(input));Saved saved=PAGES.get(url);
        if(saved==null||System.currentTimeMillis()-saved.when>300_000){saved=new Saved(System.currentTimeMillis(),parse(fetch(URI.create("https://open.spotify.com/embed"+URI.create(url).getPath()))));if(PAGES.size()>=64)PAGES.clear();PAGES.put(url,saved);}
        List<Track> tracks=new ArrayList<>();for(Metadata item:saved.tracks){Track track=new Track();track.source=item.source;track.title=item.title;track.artist=item.artist;track.durationMs=item.duration;track.artwork=item.artwork;tracks.add(track);}return tracks;
    }
    static List<Metadata> parse(String html)throws Exception{
        var script=DATA.matcher(html);if(!script.find())throw new IOException("Spotify's public page could not be read.");
        JSONObject entity=new JSONObject(script.group(1));for(String key:new String[]{"props","pageProps","state","data","entity"}){entity=entity.optJSONObject(key);if(entity==null)throw new IOException("This Spotify link has no public songs.");}
        List<Metadata> tracks=new ArrayList<>();String type=entity.optString("type");
        if(type.equals("track"))add(entity,tracks,entity);
        else if(type.equals("playlist")||type.equals("album")){JSONArray list=entity.optJSONArray("trackList");if(list!=null)for(int i=0;i<list.length();i++){JSONObject item=list.optJSONObject(i);if(item!=null)add(item,tracks,entity);}}
        if(tracks.isEmpty())throw new IOException("This Spotify link has no public songs.");return tracks;
    }
    private static void add(JSONObject item,List<Metadata> output,JSONObject entity){
        String uri=item.optString("uri");if(!uri.matches("spotify:track:[a-zA-Z0-9]{22}"))return;
        String title=item.optString("title",item.optString("name")),artist=item.optString("subtitle");JSONArray names=item.optJSONArray("artists");
        if(names!=null){List<String> artists=new ArrayList<>();for(int i=0;i<names.length();i++){JSONObject name=names.optJSONObject(i);if(name!=null&&!name.optString("name").isEmpty())artists.add(name.optString("name"));}artist=String.join(", ",artists);}
        if(title.isEmpty()||artist.isEmpty())return;String artwork="";
        JSONObject cover=entity.optJSONObject("coverArt"),visual=entity.optJSONObject("visualIdentity");JSONArray images=cover==null?null:cover.optJSONArray("sources");if(images==null&&visual!=null)images=visual.optJSONArray("image");if(images!=null&&images.optJSONObject(0)!=null)artwork=images.optJSONObject(0).optString("url");
        output.add(new Metadata(SpotifyLinks.normalize(uri),title,artist.replace('\u00a0',' '),Math.max(0,item.optLong("duration")),artwork));
    }
    static String match(Track track)throws IOException{
        String found=knownMatch(track.source);if(!found.isEmpty())return found;
        FutureTask<String> task=new FutureTask<>(()->{
            Track wanted=track;
            if(wanted.artist.isEmpty()||wanted.title.isEmpty()||wanted.title.startsWith("http")){if(!URI.create(track.source).getPath().startsWith("/track/"))throw new IOException("Import the Spotify playlist first.");wanted=read(track.source).get(0);}
            String query=wanted.artist+" - "+wanted.title+" audio";
            var handler=ServiceList.YouTube.getSearchQHFactory().fromQuery(query,List.of("videos"),"");
            SearchInfo results=SearchInfo.getInfo(ServiceList.YouTube,handler);List<SpotifyMatch.Candidate> candidates=new ArrayList<>();
            for(var item:results.getRelatedItems())if(item instanceof StreamInfoItem stream&&SourceLinks.isYouTube(stream.getUrl())){String id=android.net.Uri.parse(stream.getUrl()).getQueryParameter("v");if(id!=null&&id.matches("[a-zA-Z0-9_-]{11}"))candidates.add(new SpotifyMatch.Candidate("https://www.youtube.com/watch?v="+id,stream.getName(),stream.getUploaderName(),Math.max(0,stream.getDuration())));if(candidates.size()>=12)break;}
            String best=SpotifyMatch.choose(wanted.title,wanted.artist,wanted.durationMs/1000d,candidates);
            if(best==null)throw new IOException("No close YouTube match was found for this Spotify song.");
            if(MATCHES.size()>=300)MATCHES.clear();MATCHES.put(track.source,best);return best;
        });
        FutureTask<String> active=SEARCHES.putIfAbsent(track.source,task);boolean owner=active==null;if(owner){active=task;task.run();}
        try{return active.get(70,TimeUnit.SECONDS);}catch(InterruptedException error){Thread.currentThread().interrupt();throw new IOException("Spotify matching was interrupted.",error);}catch(ExecutionException|TimeoutException error){throw new IOException("No close YouTube match was found for this Spotify song.",error);}finally{if(owner)SEARCHES.remove(track.source,active);}
    }
    private static String expand(String url)throws IOException{
        for(int hop=0;SpotifyLinks.isShort(URI.create(url))&&hop<5;hop++){
            HttpURLConnection request=open(URI.create(url));try{int code=request.getResponseCode();String location=request.getHeaderField("Location");if(code<300||code>399||location==null)throw new IOException("Paste the full open.spotify.com link.");String next=SpotifyLinks.normalize(URI.create(url).resolve(location).toString());if(next==null)throw new IOException("Unexpected Spotify redirect.");url=next;}finally{request.disconnect();}
        }
        if(SpotifyLinks.isShort(URI.create(url)))throw new IOException("Too many Spotify redirects.");return url;
    }
    private static HttpURLConnection open(URI uri)throws IOException{HttpURLConnection request=(HttpURLConnection)uri.toURL().openConnection();request.setInstanceFollowRedirects(false);request.setConnectTimeout(15_000);request.setReadTimeout(20_000);request.setRequestProperty("User-Agent",ExtractorDownloader.USER_AGENT);return request;}
    private static String fetch(URI uri)throws IOException{
        HttpURLConnection request=open(uri);try{if(request.getResponseCode()!=200)throw new IOException("Spotify's public page could not be read.");try(InputStream input=request.getInputStream();ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] buffer=new byte[16384];int count;while((count=input.read(buffer))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(bytes.size()+count>8*1024*1024)throw new IOException("Spotify's public page is too large.");bytes.write(buffer,0,count);}return bytes.toString(StandardCharsets.UTF_8.name());}}finally{request.disconnect();}
    }
}
