package io.github.lemonior.wavedeck;

import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.widget.Toast;
import androidx.core.content.FileProvider;
import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

final class AndroidUpdates {
    private static final ExecutorService WORKER=Executors.newSingleThreadExecutor();
    private static final String API="https://api.github.com/repos/Lemonior/WaveDeck-Player/releases/latest";
    private static final String RELEASES="https://github.com/Lemonior/WaveDeck-Player/releases";
    private static volatile boolean checking;
    static void check(Activity owner,boolean manual){
        if(checking&&!manual)return;checking=true;if(manual)Toast.makeText(owner,R.string.checking_updates,Toast.LENGTH_SHORT).show();
        WORKER.execute(()->{try{JSONObject release=new JSONObject(new String(fetch(API,2*1024*1024),StandardCharsets.UTF_8));String version=release.getString("tag_name").replaceFirst("^v","");
            if(release.optBoolean("draft")||release.optBoolean("prerelease")||compare(version,BuildConfig.VERSION_NAME)<=0){owner.runOnUiThread(()->{if(manual&&!owner.isDestroyed())Toast.makeText(owner,R.string.up_to_date,Toast.LENGTH_LONG).show();});return;}
            JSONObject apk=null;JSONArray assets=release.getJSONArray("assets");String expected="WaveDeck-"+version+"-android.apk";
            for(int i=0;i<assets.length();i++){JSONObject asset=assets.getJSONObject(i);if(expected.equals(asset.optString("name"))&&"uploaded".equals(asset.optString("state")))apk=asset;}
            JSONObject artifact=apk;owner.runOnUiThread(()->{if(owner.isDestroyed())return;if(!manual){if(artifact!=null)Toast.makeText(owner,owner.getString(R.string.update_available,version),Toast.LENGTH_LONG).show();return;}AlertDialog.Builder dialog=new AlertDialog.Builder(owner).setTitle(owner.getString(R.string.update_available,version)).setMessage(release.optString("body","")+"\n\n"+owner.getString(R.string.update_restart)).setNegativeButton(R.string.cancel,null).setNeutralButton(R.string.open_releases,(view,which)->owner.startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(RELEASES))));if(artifact!=null)dialog.setPositiveButton(R.string.download_install,(view,which)->download(owner,artifact));dialog.show();});
        }catch(Exception error){if(manual)owner.runOnUiThread(()->{if(!owner.isDestroyed())new AlertDialog.Builder(owner).setMessage(R.string.update_error).setPositiveButton(R.string.open_releases,(dialog,which)->owner.startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(RELEASES)))).setNegativeButton(R.string.close,null).show();});}finally{checking=false;}});
    }
    private static void download(Activity owner,JSONObject asset){
        if(Build.VERSION.SDK_INT>=26&&!owner.getPackageManager().canRequestPackageInstalls()){Toast.makeText(owner,R.string.install_permission,Toast.LENGTH_LONG).show();owner.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+owner.getPackageName())));return;}
        Toast.makeText(owner,R.string.update_verifying,Toast.LENGTH_LONG).show();WORKER.execute(()->{File target=null;try{long expected=asset.getLong("size");String digest=asset.optString("digest");if(expected<1||expected>300L*1024*1024||!digest.matches("sha256:[a-fA-F0-9]{64}"))throw new IOException("No verified APK digest.");URI uri=URI.create(asset.getString("url"));if(!uri.getScheme().equals("https")||!uri.getHost().equals("api.github.com")||!uri.getPath().matches("/repos/Lemonior/WaveDeck-Player/releases/assets/[0-9]+"))throw new IOException("Unexpected APK URL.");
            File folder=new File(owner.getCacheDir(),"updates");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException();target=new File(folder,"WaveDeck-"+UUID.randomUUID()+".apk");MessageDigest sha=MessageDigest.getInstance("SHA-256");HttpURLConnection request=connect(uri.toString(),true);
            try(InputStream input=request.getInputStream();OutputStream output=new FileOutputStream(target)){byte[] buffer=new byte[131072];long total=0;int count;while((count=input.read(buffer))!=-1){total+=count;if(total>expected)throw new IOException("APK size mismatch.");sha.update(buffer,0,count);output.write(buffer,0,count);}if(total!=expected)throw new IOException("Incomplete APK.");}finally{request.disconnect();}
            if(!hex(sha.digest()).equalsIgnoreCase(digest.substring(7)))throw new IOException("APK digest mismatch.");
            verifySigner(owner,target);Uri content=FileProvider.getUriForFile(owner,owner.getPackageName()+".files",target);owner.runOnUiThread(()->{if(!owner.isDestroyed())owner.startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(content,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));});
        }catch(Exception error){if(target!=null)target.delete();owner.runOnUiThread(()->{if(!owner.isDestroyed())Toast.makeText(owner,R.string.update_error,Toast.LENGTH_LONG).show();});}});
    }
    @SuppressWarnings("deprecation") private static void verifySigner(Context context,File apk)throws Exception{
        PackageManager manager=context.getPackageManager();int flags=Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;
        PackageInfo installed=manager.getPackageInfo(context.getPackageName(),flags),next=manager.getPackageArchiveInfo(apk.getAbsolutePath(),flags);
        if(next==null||!context.getPackageName().equals(next.packageName)||(Build.VERSION.SDK_INT>=28?next.getLongVersionCode()<=installed.getLongVersionCode():next.versionCode<=installed.versionCode))throw new SecurityException("The APK is not a newer WaveDeck build.");
        Signature[] previous=Build.VERSION.SDK_INT>=28?installed.signingInfo.getApkContentsSigners():installed.signatures;Signature[] signatures=Build.VERSION.SDK_INT>=28?next.signingInfo.getApkContentsSigners():next.signatures;
        if(previous==null||signatures==null||previous.length!=signatures.length)throw new SecurityException("APK signer mismatch.");
        Set<String> trusted=new HashSet<>();for(Signature signature:previous)trusted.add(hex(MessageDigest.getInstance("SHA-256").digest(signature.toByteArray())));for(Signature signature:signatures)if(!trusted.contains(hex(MessageDigest.getInstance("SHA-256").digest(signature.toByteArray()))))throw new SecurityException("APK signer mismatch.");
    }
    private static HttpURLConnection connect(String url,boolean binary)throws IOException{HttpURLConnection request=(HttpURLConnection)URI.create(url).toURL().openConnection();request.setConnectTimeout(15_000);request.setReadTimeout(binary?30_000:15_000);request.setRequestProperty("User-Agent","WaveDeck/"+BuildConfig.VERSION_NAME);request.setRequestProperty("Accept",binary?"application/octet-stream":"application/vnd.github+json");request.setRequestProperty("X-GitHub-Api-Version","2022-11-28");if(request.getResponseCode()!=200){request.disconnect();throw new IOException("Update server unavailable.");}return request;}
    private static byte[] fetch(String url,int max)throws IOException{HttpURLConnection request=connect(url,false);try(InputStream input=request.getInputStream();ByteArrayOutputStream output=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];int count;while((count=input.read(buffer))!=-1){if(output.size()+count>max)throw new IOException("Oversized response.");output.write(buffer,0,count);}return output.toByteArray();}finally{request.disconnect();}}
    static int compare(String left,String right){if(!left.matches("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)"))throw new IllegalArgumentException("Invalid stable version.");String[] a=left.split("\\."),b=right.split("\\.");for(int i=0;i<3;i++){int result=Integer.compare(Integer.parseInt(a[i]),Integer.parseInt(b[i]));if(result!=0)return result;}return 0;}
    private static String hex(byte[] bytes){StringBuilder value=new StringBuilder();for(byte b:bytes)value.append(String.format(Locale.ROOT,"%02x",b&255));return value.toString();}
}
