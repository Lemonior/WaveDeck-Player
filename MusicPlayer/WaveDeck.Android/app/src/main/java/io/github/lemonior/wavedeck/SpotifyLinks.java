package io.github.lemonior.wavedeck;

import java.net.URI;
import java.util.Locale;
import java.util.regex.Pattern;

final class SpotifyLinks {
    private static final Pattern PATH=Pattern.compile("^/(?:embed/|intl-[a-zA-Z-]+/)?(track|playlist|album)/([a-zA-Z0-9]{22})/?$");
    static boolean isSpotify(String value){
        if(value==null)return false;if(value.trim().toLowerCase(Locale.ROOT).startsWith("spotify:"))return true;
        try{String host=URI.create(value).getHost();return host!=null&&(host.equalsIgnoreCase("open.spotify.com")||host.equalsIgnoreCase("spotify.link")||host.equalsIgnoreCase("spoti.fi"));}catch(IllegalArgumentException error){return false;}
    }
    static boolean isShort(URI uri){return "spotify.link".equalsIgnoreCase(uri.getHost())||"spoti.fi".equalsIgnoreCase(uri.getHost());}
    static String normalize(String input){
        input=input.trim();
        if(input.toLowerCase(Locale.ROOT).startsWith("spotify:")){
            var match=Pattern.compile("^spotify:(track|playlist|album):([a-zA-Z0-9]{22})$",Pattern.CASE_INSENSITIVE).matcher(input);
            if(!match.matches())throw new IllegalArgumentException("Use a Spotify song, album or playlist link.");
            return "https://open.spotify.com/"+match.group(1).toLowerCase(Locale.ROOT)+"/"+match.group(2);
        }
        if(!isSpotify(input))return null;
        URI uri=URI.create(input);String scheme=uri.getScheme();
        if((!"https".equalsIgnoreCase(scheme)&&!"http".equalsIgnoreCase(scheme))||uri.getUserInfo()!=null||uri.getPort()!=-1)throw new IllegalArgumentException("Use a complete public Spotify link.");
        if(isShort(uri))return "https://"+uri.getHost().toLowerCase(Locale.ROOT)+uri.getRawPath()+(uri.getRawQuery()==null?"":"?"+uri.getRawQuery());
        var path=PATH.matcher(uri.getPath());if(!path.matches())throw new IllegalArgumentException("Use a Spotify song, album or playlist link.");
        return "https://open.spotify.com/"+path.group(1)+"/"+path.group(2);
    }
}
