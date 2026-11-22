package io.github.lemonior.wavedeck;

import android.content.*;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.provider.OpenableColumns;
import androidx.documentfile.provider.DocumentFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
final class FileLibrary {
    static final Set<String> EXTENSIONS = Set.of("mp3","m4a","aac","flac","wav","ogg","opus","aiff","aif","wma","mp4","webm","mkv","3gp","alac");
    static Track read(Context context, Uri uri) throws IOException {
        Track track = new Track(); track.source = uri.toString(); track.title = uri.getLastPathSegment();
        try (var cursor = context.getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)) {
            if(cursor!=null&&cursor.moveToFirst())track.title=cursor.getString(0);
        } catch (RuntimeException ignored) { }
        MediaMetadataRetriever metadata = new MediaMetadataRetriever();
        try {
            metadata.setDataSource(context,uri);String title=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE);String artist=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST);
            if(title!=null&&!title.isBlank())track.title=title;if(artist!=null)track.artist=artist;
            String duration=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION);if(duration!=null)track.durationMs=Long.parseLong(duration);
        } catch (Exception ignored) { /* Some document providers cannot seek for metadata. ExoPlayer still gets the URI. */ }
        finally { metadata.release(); }
        if(track.title==null||track.title.isBlank())track.title="Audio";return track;
    }
    static Track copyForPlayback(Context context,Uri uri)throws IOException {
        Track track=read(context,uri);
        if((context.getPackageName()+".files").equals(uri.getAuthority()))return track;
        File folder=new File(context.getFilesDir(),"imported");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("Cannot preserve shared audio.");
        String extension=track.title.contains(".")?track.title.substring(track.title.lastIndexOf('.')+1):"audio";if(!extension.matches("[A-Za-z0-9]{1,8}"))extension="audio";
        File copy=new File(folder,UUID.randomUUID()+"."+extension);
        try(InputStream input=context.getContentResolver().openInputStream(uri);OutputStream output=new FileOutputStream(copy)){
            if(input==null)throw new IOException("The shared audio could not be opened.");byte[] buffer=new byte[131072];int count;
            while((count=input.read(buffer))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(folder.getUsableSpace()<count+10L*1024*1024)throw new IOException("There is not enough space to preserve shared audio.");output.write(buffer,0,count);}
        }catch(IOException error){copy.delete();throw error;}
        track.source=androidx.core.content.FileProvider.getUriForFile(context,context.getPackageName()+".files",copy).toString();return track;
    }
    static List<Track> scan(Context context,Uri tree,boolean recurse,int limit) throws IOException {
        DocumentFile root=DocumentFile.fromTreeUri(context,tree);if(root==null||!root.canRead())throw new IOException("Folder access is unavailable.");
        List<Track> result=new ArrayList<>();ArrayDeque<DocumentFile> pending=new ArrayDeque<>();pending.add(root);Set<String> visited=new HashSet<>();
        while(!pending.isEmpty()&&result.size()<limit){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();DocumentFile folder=pending.remove();if(!visited.add(folder.getUri().toString()))continue;
            DocumentFile[] files=folder.listFiles();Arrays.sort(files,Comparator.comparing(item->String.valueOf(item.getName()),String.CASE_INSENSITIVE_ORDER));
            for(DocumentFile file:files){if(result.size()>=limit)break;if(file.isDirectory()){if(recurse)pending.add(file);continue;}String name=String.valueOf(file.getName());int dot=name.lastIndexOf('.');String type=file.getType();if(type!=null&&(type.startsWith("audio/")||type.startsWith("video/"))||dot>=0&&EXTENSIONS.contains(name.substring(dot+1).toLowerCase(Locale.ROOT)))result.add(read(context,file.getUri()));}
        }return result;
    }
    static String text(Context context,Uri uri) throws IOException {
        try(InputStream input=context.getContentResolver().openInputStream(uri);ByteArrayOutputStream output=new ByteArrayOutputStream()){
            if(input==null)throw new IOException("The document could not be opened.");byte[] buffer=new byte[16384];int read;while((read=input.read(buffer))!=-1){if(output.size()+read>32*1024*1024)throw new IOException("The playlist is too large.");output.write(buffer,0,read);}return output.toString(StandardCharsets.UTF_8.name());
        }
    }
    static List<Track> playlist(Context context,Uri uri,int limit) throws IOException,JSONException {
        String text=text(context,uri).strip();List<Track> result=new ArrayList<>();
        if(text.startsWith("[")||text.startsWith("{")){
            Object parsed=new JSONTokener(text).nextValue();JSONArray entries=parsed instanceof JSONArray?(JSONArray)parsed:((JSONObject)parsed).getJSONArray("queue");
            for(int i=0;i<entries.length()&&result.size()<limit;i++){
                JSONObject entry=entries.getJSONObject(i);Track track;
                if(entry.has("source"))track=Track.fromJson(entry);
                else{track=new Track();track.title=entry.optString("Title",entry.optString("title"));track.artist=entry.optString("Artist",entry.optString("artist"));track.source=entry.optString("Url",entry.optString("url"));if(track.source.isEmpty())track.source=entry.optString("LocalPath",entry.optString("localPath"));track.durationMs=(long)(entry.optDouble("Duration",entry.optDouble("duration"))*1000);track.startMs=(long)(entry.optDouble("StartSeconds",0)*1000);if(entry.has("EndSeconds")&&!entry.isNull("EndSeconds"))track.endMs=(long)(entry.optDouble("EndSeconds")*1000);}
                if(SourceLinks.isNetwork(track.source)||track.source.startsWith("content://"))result.add(track);
            }
        }else{
            String title="";for(String line:text.split("\\R")){line=line.trim();if(line.startsWith("#EXTINF:")){int comma=line.indexOf(',');title=comma>=0?line.substring(comma+1):"";continue;}if(line.startsWith("#")||line.isEmpty())continue;if(SourceLinks.isNetwork(line)){Track track=new Track();track.source=SourceLinks.validate(line);track.title=title.isEmpty()?line:title;result.add(track);}title="";if(result.size()>=limit)break;}
        }return result;
    }
}
