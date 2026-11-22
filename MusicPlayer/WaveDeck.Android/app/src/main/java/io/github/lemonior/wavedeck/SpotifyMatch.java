package io.github.lemonior.wavedeck;

import java.text.Normalizer;
import java.util.*;
import java.util.regex.Pattern;

final class SpotifyMatch {
    record Candidate(String url,String title,String artist,double duration){}
    static String choose(String title,String artist,double duration,List<Candidate> candidates){
        double best=.85;String url=null;
        for(Candidate item:candidates){if(!item.url.matches("https://www\\.youtube\\.com/watch\\?v=[a-zA-Z0-9_-]{11}"))continue;double score=score(title,artist,duration,item);if(score>best||url==null&&score>=best){best=score;url=item.url;}}
        return url;
    }
    static double score(String title,String artist,double duration,Candidate candidate){
        Set<String> wanted=titleWords(title),found=titleWords(candidate.title);if(wanted.isEmpty()||found.isEmpty())return 0;
        for(String version:new String[]{"cover","karaoke","instrumental","live","remix","slowed","sped","nightcore"})if(found.contains(version)!=wanted.contains(version))return 0;
        long present=wanted.stream().filter(found::contains).count();double coverage=present/(double)wanted.size();String compact=compact(candidate.title+" "+candidate.artist);
        boolean artistMatch=Arrays.stream(artist.split(",| feat\\. | & ")).map(SpotifyMatch::compact).anyMatch(value->value.length()>=2&&compact.contains(value));
        if(coverage<.8||!artistMatch)return 0;
        double tolerance=Math.max(12,duration*.1),difference=Math.abs(duration-candidate.duration);
        if(duration>0&&(candidate.duration<=0||difference>tolerance))return 0;
        return coverage*.65+.25+(duration>0?.1*(1-difference/tolerance):.05);
    }
    private static String normalized(String value){return Normalizer.normalize(value.toLowerCase(Locale.ROOT),Normalizer.Form.NFD).replaceAll("\\p{M}","");}
    private static Set<String> titleWords(String value){return words(value.replaceAll("(?i)[\\(\\[]\\s*(?:feat\\.?|ft\\.?|with)\\s+[^\\)\\]]*[\\)\\]]","").replaceAll("(?i)\\b(?:\\d{4}\\s+remaster(?:ed)?|remaster(?:ed)?(?:\\s+\\d{4})?)\\b",""));}
    private static Set<String> words(String value){Set<String> result=new HashSet<>();var match=Pattern.compile("[\\p{L}\\p{N}]+").matcher(normalized(value));while(match.find())result.add(match.group());return result;}
    private static String compact(String value){return normalized(value).replaceAll("[^\\p{L}\\p{N}]","");}
}
