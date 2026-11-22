package io.github.lemonior.wavedeck;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Locale;

final class SourceLinks {
    static boolean isYouTube(String value) {
        try { String host = URI.create(value).getHost(); if (host == null) return false; host = host.toLowerCase(Locale.ROOT);
            return host.equals("youtu.be") || host.equals("youtube.com") || host.endsWith(".youtube.com") || host.equals("youtube-nocookie.com") || host.endsWith(".youtube-nocookie.com"); }
        catch (IllegalArgumentException ignored) { return false; }
    }
    static boolean isNetwork(String value) { try { String scheme = URI.create(value).getScheme(); return "https".equalsIgnoreCase(scheme) || "http".equalsIgnoreCase(scheme); } catch (IllegalArgumentException ignored) { return false; } }
    static String validate(String value) {
        String result = value.trim(); URI uri;
        try { uri = URI.create(result); } catch (IllegalArgumentException error) { throw new IllegalArgumentException("Invalid media URL."); }
        if (!isNetwork(result) || uri.getHost() == null || uri.getUserInfo() != null || result.length() > 8192) throw new IllegalArgumentException("Use an HTTP or HTTPS media URL.");
        return result;
    }
    static String cacheKey(String value) {
        try { byte[] digest = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8)); StringBuilder result = new StringBuilder(); for (byte b : digest) result.append(String.format(Locale.ROOT, "%02x", b & 255)); return result.toString(); }
        catch (java.security.NoSuchAlgorithmException error) { throw new IllegalStateException(error); }
    }
}
