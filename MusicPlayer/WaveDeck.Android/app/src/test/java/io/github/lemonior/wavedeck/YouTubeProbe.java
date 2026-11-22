package io.github.lemonior.wavedeck;

import org.schabi.newpipe.extractor.NewPipe;
import org.schabi.newpipe.extractor.stream.*;
import org.schabi.newpipe.extractor.localization.Localization;
import java.net.*;

public final class YouTubeProbe {
    public static void main(String[] args)throws Exception{
        NewPipe.init(new ExtractorDownloader(),new Localization("en","US"));
        StreamInfo info=StreamInfo.getInfo("https://www.youtube.com/watch?v=jNQXAC9IVRw");
        AudioStream audio=info.getAudioStreams().stream().filter(item->item.isUrl()&&item.getDeliveryMethod()==DeliveryMethod.PROGRESSIVE_HTTP).findFirst().orElseThrow();
        HttpURLConnection request=(HttpURLConnection)URI.create(audio.getContent()).toURL().openConnection();request.setConnectTimeout(15_000);request.setReadTimeout(20_000);request.setRequestProperty("User-Agent",ExtractorDownloader.USER_AGENT);request.setRequestProperty("Range","bytes=0-32767");
        try{int code=request.getResponseCode();if(code!=200&&code!=206)throw new Exception("YouTube audio HTTP "+code);try(var input=request.getInputStream()){byte[] buffer=new byte[4096];int count=input.read(buffer);if(count<1024)throw new Exception("YouTube audio returned too little data.");System.out.println("Live YouTube extraction and audio bytes verified: "+info.getName()+", "+info.getDuration()+" seconds, HTTP "+code+", "+count+" initial bytes.");}}
        finally{request.disconnect();}
    }
}
