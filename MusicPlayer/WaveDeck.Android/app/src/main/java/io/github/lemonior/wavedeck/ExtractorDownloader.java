package io.github.lemonior.wavedeck;

import org.schabi.newpipe.extractor.downloader.Downloader;
import org.schabi.newpipe.extractor.downloader.Request;
import org.schabi.newpipe.extractor.downloader.Response;
import org.schabi.newpipe.extractor.exceptions.ReCaptchaException;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.zip.GZIPInputStream;

final class ExtractorDownloader extends Downloader {
    static final String USER_AGENT = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";
    @Override public Response execute(Request request) throws IOException, ReCaptchaException {
        HttpURLConnection connection = (HttpURLConnection) URI.create(request.url()).toURL().openConnection();
        try {
            connection.setConnectTimeout(15_000); connection.setReadTimeout(20_000); connection.setInstanceFollowRedirects(true);
            connection.setRequestMethod(request.httpMethod()); connection.setRequestProperty("User-Agent", USER_AGENT);
            for (var header : request.headers().entrySet()) {
                boolean first = true;
                for (String value : header.getValue()) { if (first) connection.setRequestProperty(header.getKey(), value); else connection.addRequestProperty(header.getKey(), value); first = false; }
            }
            byte[] data = request.dataToSend();
            if (data != null) { connection.setDoOutput(true); try (var output = connection.getOutputStream()) { output.write(data); } }
            int code = connection.getResponseCode();
            if (code == 429) throw new ReCaptchaException("YouTube requested verification. Try again later or use local music.", request.url());
            InputStream input = code >= 400 ? connection.getErrorStream() : connection.getInputStream();
            String body = "";
            if (input != null) {
                if ("gzip".equalsIgnoreCase(connection.getContentEncoding())) input = new GZIPInputStream(input);
                try (InputStream stream = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[16384]; int read;
                    while ((read = stream.read(buffer)) != -1) { if (output.size() + read > 32 * 1024 * 1024) throw new IOException("The site response is too large."); output.write(buffer, 0, read); }
                    body = output.toString(StandardCharsets.UTF_8.name());
                }
            }
            return new Response(code, connection.getResponseMessage(), connection.getHeaderFields(), body, connection.getURL().toString());
        } finally { connection.disconnect(); }
    }
}
