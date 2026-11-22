package io.github.lemonior.wavedeck;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import androidx.core.content.ContextCompat;
import androidx.documentfile.provider.DocumentFile;
import androidx.media3.common.*;
import androidx.media3.exoplayer.offline.*;
import androidx.media3.session.*;
import androidx.media3.session.MediaController;
import com.google.common.util.concurrent.ListenableFuture;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public final class MainActivity extends Activity {
    private static final int PICK_MUSIC=10, PICK_FOLDER=11, PICK_LIST=12, SAVE_LIST=13, SAVE_PRESET=14, PICK_PRESET=15;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private ListenableFuture<MediaController> connection;
    private MediaController controller;
    private SharedPreferences prefs;
    private EditText url, search;
    private Spinner mode;
    private TextView title, artist, status, time, playback, count;
    private Button play, repeat, shuffle, loadNext;
    private SeekBar seek;
    private LinearLayout soundPanel;
    private ListView playlist;
    private TrackAdapter adapter;
    private boolean seeking, resumed, checkingStartup;
    private Future<?> importing;
    private volatile int importGeneration;
    private Sources.ImportPage importPage;
    private int background, surface, accent, text, muted;
    private String soundTab = "";
    private Intent pendingIntent;
    private final Player.Listener listener = new Player.Listener() {
        @Override public void onEvents(Player player, Player.Events events) { refresh(); }
    };
    private final Runnable pulse = new Runnable() { @Override public void run() { if (!resumed) return; refreshProgress(); main.postDelayed(this, 250); } };
    @Override protected void attachBaseContext(Context base) { super.attachBaseContext(DeckLocale.wrap(base)); }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved); setVolumeControlStream(android.media.AudioManager.STREAM_MUSIC); prefs = getSharedPreferences("settings", MODE_PRIVATE); colors(); build(); pendingIntent = getIntent();
        SessionToken token = new SessionToken(this, new ComponentName(this, PlaybackService.class));
        connection = new MediaController.Builder(this, token).setListener(new MediaController.Listener() {
            @Override public void onExtrasChanged(MediaController connected, Bundle extras) { String failed = extras.getString("error"); if (failed != null) status.setText(getString(R.string.failed_track, failed)); }
        }).buildAsync();
        connection.addListener(() -> {
            try { controller = connection.get(); controller.addListener(listener); refresh(); acceptIntent(pendingIntent); pendingIntent = null; }
            catch (Exception error) { status.setText(R.string.connection_error); }
        }, ContextCompat.getMainExecutor(this));
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 30);
    }
    @Override protected void onStart() { super.onStart(); resumed = true; main.post(pulse); }
    @Override protected void onStop() { resumed = false; main.removeCallbacks(pulse); super.onStop(); }
    @Override protected void onResume() {
        super.onResume();
        if (!checkingStartup && prefs.getBoolean("startupUpdates", true)) { checkingStartup = true; AndroidUpdates.check(this, false); }
    }
    @Override protected void onNewIntent(Intent intent) { super.onNewIntent(intent); setIntent(intent); if (controller == null) pendingIntent = intent; else acceptIntent(intent); }
    @Override protected void onDestroy() {
        main.removeCallbacksAndMessages(null); if (importing != null) importing.cancel(true); worker.shutdownNow();
        if (controller != null) controller.removeListener(listener); if (connection != null) MediaController.releaseFuture(connection); super.onDestroy();
    }
    private void colors() {
        String theme = prefs.getString("theme", "Modern");
        background=Color.rgb(15,25,32);surface=Color.rgb(25,39,48);accent=Color.rgb(224,187,111);text=Color.rgb(236,233,222);muted=Color.rgb(158,180,188);
        if ("OldSchool".equals(theme)) { background=Color.rgb(24,22,17);surface=Color.rgb(39,34,26);accent=Color.rgb(225,189,104);muted=Color.rgb(186,172,139); }
        if ("Retro".equals(theme)) { background=Color.rgb(21,15,34);surface=Color.rgb(39,26,56);accent=Color.rgb(209,146,255);muted=Color.rgb(178,160,205); }
    }
    private void build() {
        LinearLayout root = column(); root.setBackgroundColor(background); root.setPadding(dp(12),dp(12),dp(12),dp(12));
        root.setOnApplyWindowInsetsListener((view,insets)->{
            if(Build.VERSION.SDK_INT>=30){var bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());view.setPadding(dp(12)+bars.left,dp(8)+bars.top,dp(12)+bars.right,dp(8)+bars.bottom);}
            else view.setPadding(dp(12)+insets.getSystemWindowInsetLeft(),dp(8)+insets.getSystemWindowInsetTop(),dp(12)+insets.getSystemWindowInsetRight(),dp(8)+insets.getSystemWindowInsetBottom()); return insets;
        });
        androidx.core.widget.NestedScrollView scroll = new androidx.core.widget.NestedScrollView(this); scroll.setFillViewport(true); LinearLayout body = column(); body.setPadding(0,0,0,dp(12)); scroll.addView(body); root.addView(scroll); setContentView(root);
        LinearLayout header = row(); ImageView icon = new ImageView(this); icon.setImageResource(R.drawable.wavedeck); header.addView(icon,new LinearLayout.LayoutParams(dp(38),dp(38)));
        TextView name = label("WaveDeck",24);name.setTypeface(Typeface.DEFAULT,Typeface.BOLD);name.setTextColor(accent);header.addView(name,new LinearLayout.LayoutParams(0,dp(48),1));
        Button settings = button("⚙",R.string.settings,this::showSettings);header.addView(settings,new LinearLayout.LayoutParams(dp(54),dp(48)));body.addView(header); body.addView(label(BuildConfig.VERSION_NAME+" · Android",11));
        LinearLayout source = card(); url = new EditText(this); url.setSingleLine(true);url.setTextColor(text);url.setHintTextColor(muted);url.setHint(R.string.url_hint);url.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI);source.addView(url);
        mode = spinner(new String[]{getString(R.string.stream),getString(R.string.download),getString(R.string.download_play)},prefs.getInt("mode",0));
        Button go = button(getString(R.string.go),R.string.go,()->importUrl(false,false));Button cancel=button("×",R.string.cancel,()->{++importGeneration;if(importing!=null)importing.cancel(true);status.setText(R.string.import_cancelled);});
        LinearLayout actions=row();actions.addView(mode,new LinearLayout.LayoutParams(0,dp(48),1));actions.addView(go,new LinearLayout.LayoutParams(dp(65),dp(48)));actions.addView(cancel,new LinearLayout.LayoutParams(dp(48),dp(48)));source.addView(actions);LinearLayout local=row();local.addView(button(getString(R.string.add_music),R.string.add_music,this::pickMusic),new LinearLayout.LayoutParams(0,dp(46),1));local.addView(button(getString(R.string.add_folder),R.string.add_folder,this::pickFolder),new LinearLayout.LayoutParams(0,dp(46),1));source.addView(local);body.addView(source);
        LinearLayout now=card();playback=label(getString(R.string.stopped),10);playback.setTextColor(accent);now.addView(playback);title=label(getString(R.string.ready),21);title.setTypeface(Typeface.DEFAULT,Typeface.BOLD);title.setMaxLines(3);now.addView(title);artist=label("WaveDeck",13);artist.setTextColor(muted);now.addView(artist);
        time=label("00:00 / 00:00",16);time.setTypeface(Typeface.MONOSPACE);now.addView(time);seek=new SeekBar(this);seek.setMax(1000);seek.setProgressTintList(ColorStateList.valueOf(accent));seek.setThumbTintList(ColorStateList.valueOf(accent));seek.setContentDescription(getString(R.string.forward));now.addView(seek);
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar bar){seeking=true;}public void onProgressChanged(SeekBar bar,int value,boolean user){}public void onStopTrackingTouch(SeekBar bar){if(controller!=null&&controller.getDuration()>0)controller.seekTo(controller.getDuration()*bar.getProgress()/1000);seeking=false;}});
        LinearLayout transport=row();transport.addView(button("|◀",R.string.previous,()->{if(controller!=null)controller.seekToPrevious();}),new LinearLayout.LayoutParams(0,dp(56),1));
        play=button("▶",R.string.play,this::togglePlay);play.setTextSize(24);transport.addView(play,new LinearLayout.LayoutParams(0,dp(56),1.3f));transport.addView(button("■",R.string.stop,()->{if(controller!=null){controller.pause();controller.stop();}}),new LinearLayout.LayoutParams(0,dp(56),1));transport.addView(button("▶|",R.string.next,()->{if(controller!=null)controller.seekToNextMediaItem();}),new LinearLayout.LayoutParams(0,dp(56),1));now.addView(transport);
        LinearLayout secondary=row();secondary.addView(button("−10s",R.string.rewind,()->{if(controller!=null)controller.seekBack();}),new LinearLayout.LayoutParams(0,dp(46),1));shuffle=button("⇄",R.string.shuffle,()->{if(controller!=null)controller.setShuffleModeEnabled(!controller.getShuffleModeEnabled());});secondary.addView(shuffle,new LinearLayout.LayoutParams(dp(54),dp(46)));repeat=button(getString(R.string.repeat_off),R.string.repeat_off,()->{if(controller!=null)controller.setRepeatMode((controller.getRepeatMode()+1)%3);});secondary.addView(repeat,new LinearLayout.LayoutParams(0,dp(46),1.6f));secondary.addView(button("+10s",R.string.forward,()->{if(controller!=null)controller.seekForward();}),new LinearLayout.LayoutParams(0,dp(46),1));now.addView(secondary);
        slider(now,R.string.volume,"volume",0,1,.65f,"%",100);body.addView(now);
        LinearLayout sound=card();LinearLayout tabs=row();for(var entry:new Object[][]{{R.string.equalizer,"eq"},{R.string.knobs,"knobs"},{R.string.effects,"effects"}}){int id=(int)entry[0];String tab=(String)entry[1];tabs.addView(button(getString(id),id,()->showSound(tab)),new LinearLayout.LayoutParams(0,dp(46),1));}sound.addView(tabs);soundPanel=column();sound.addView(soundPanel);body.addView(sound);
        LinearLayout list=card();LinearLayout heading=row();TextView caption=label(getString(R.string.playlist),15);caption.setTextColor(accent);heading.addView(caption,new LinearLayout.LayoutParams(0,dp(40),1));count=label("",12);heading.addView(count);heading.addView(button("⋮",R.string.playlist,()->playlistMenu(heading)));list.addView(heading);
        search=new EditText(this);search.setSingleLine(true);search.setHint(R.string.search);search.setTextColor(text);search.setHintTextColor(muted);list.addView(search);search.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){filter();}public void afterTextChanged(android.text.Editable s){}});
        playlist=new ListView(this);playlist.setDividerHeight(1);playlist.setBackgroundColor(surface);adapter=new TrackAdapter();playlist.setAdapter(adapter);list.addView(playlist,new LinearLayout.LayoutParams(-1,dp(380)));
        playlist.setOnItemClickListener((parent,view,pos,id)->{if(controller!=null){controller.seekToDefaultPosition(adapter.indices.get(pos));controller.prepare();controller.play();}});
        playlist.setOnItemLongClickListener((parent,view,pos,id)->{trackMenu(view,adapter.indices.get(pos));return true;});
        playlist.setNestedScrollingEnabled(true);
        LinearLayout files=row();files.addView(button(getString(R.string.add_music),R.string.add_music,this::pickMusic),new LinearLayout.LayoutParams(0,dp(48),1));files.addView(button(getString(R.string.add_folder),R.string.add_folder,this::pickFolder),new LinearLayout.LayoutParams(0,dp(48),1));list.addView(files);
        loadNext=button(getString(R.string.load_next),R.string.load_next,()->importUrl(true,false));loadNext.setVisibility(View.GONE);list.addView(loadNext);body.addView(list);
        status=label(getString(R.string.ready),12);status.setTextColor(muted);status.setPadding(dp(8),dp(8),dp(8),dp(16));body.addView(status);
    }
    private void togglePlay(){
        if(controller==null)return;
        if(controller.getMediaItemCount()==0){toast(R.string.empty_playlist);return;}
        int state=controller.getPlaybackState();
        if(state==Player.STATE_IDLE||state==Player.STATE_ENDED){controller.pause();if(state==Player.STATE_ENDED)controller.seekToDefaultPosition();controller.prepare();controller.play();}
        else if(controller.getPlayWhenReady())controller.pause();else controller.play();
    }
    private void refresh(){if(controller==null||isDestroyed())return;MediaItem current=controller.getCurrentMediaItem();title.setText(current==null?getString(R.string.ready):current.mediaMetadata.title);artist.setText(current==null?"WaveDeck":current.mediaMetadata.artist);boolean active=controller.getPlayWhenReady()&&controller.getPlaybackState()!=Player.STATE_IDLE&&controller.getPlaybackState()!=Player.STATE_ENDED;play.setText(active?"Ⅱ":"▶");play.setContentDescription(getString(active?R.string.pause:R.string.play));playback.setText(controller.isPlaying()?R.string.playing:controller.getPlaybackState()==Player.STATE_BUFFERING?R.string.buffering:controller.getPlaybackState()==Player.STATE_READY?R.string.paused:R.string.stopped);shuffle.setTextColor(controller.getShuffleModeEnabled()?accent:muted);repeat.setText(controller.getRepeatMode()==Player.REPEAT_MODE_ONE?R.string.repeat_one:controller.getRepeatMode()==Player.REPEAT_MODE_ALL?R.string.repeat_all:R.string.repeat_off);count.setText(getString(R.string.track_count,controller.getMediaItemCount()));filter();refreshProgress();}
    private void refreshProgress(){if(controller==null||time==null)return;long duration=Math.max(0,controller.getDuration()),position=Math.max(0,controller.getCurrentPosition());time.setText(clock(position)+" / "+clock(duration));seek.setEnabled(duration>0);if(!seeking)seek.setProgress(duration>0?(int)Math.min(1000,position*1000/duration):0);}
    private static String clock(long milliseconds){long seconds=milliseconds/1000;return seconds>=3600?String.format(Locale.ROOT,"%d:%02d:%02d",seconds/3600,seconds/60%60,seconds%60):String.format(Locale.ROOT,"%02d:%02d",seconds/60,seconds%60);}
    private void filter(){if(adapter==null||controller==null)return;String query=search.getText().toString().trim().toLowerCase(Locale.ROOT);adapter.indices.clear();for(int i=0;i<controller.getMediaItemCount();i++){var metadata=controller.getMediaItemAt(i).mediaMetadata;String value=String.valueOf(metadata.title)+" "+metadata.artist;if(value.toLowerCase(Locale.ROOT).contains(query))adapter.indices.add(i);}adapter.notifyDataSetChanged();}
    private final class TrackAdapter extends BaseAdapter{
        final List<Integer> indices=new ArrayList<>();public int getCount(){return indices.size();}public Object getItem(int pos){return controller.getMediaItemAt(indices.get(pos));}public long getItemId(int pos){return indices.get(pos);}
        public View getView(int pos,View reuse,ViewGroup parent){LinearLayout item=reuse instanceof LinearLayout?(LinearLayout)reuse:column();item.removeAllViews();item.setPadding(dp(9),dp(9),dp(9),dp(9));int index=indices.get(pos);Track track=Track.fromItem(controller.getMediaItemAt(index));TextView name=label((index==controller.getCurrentMediaItemIndex()?"▶  ":"")+track.title,14);name.setTextColor(index==controller.getCurrentMediaItemIndex()?accent:text);name.setMaxLines(2);item.addView(name);TextView info=label(track.artist+(track.durationMs>0?" · "+clock(track.durationMs):""),11);info.setTextColor(muted);item.addView(info);return item;}
    }
    private void showSound(String tab){if(tab.equals(soundTab)){soundTab="";soundPanel.removeAllViews();return;}soundTab=tab;soundPanel.removeAllViews();
        LinearLayout controls=row();controls.addView(button(getString(R.string.bypass),R.string.bypass,()->{prefs.edit().putBoolean("bypass",!prefs.getBoolean("bypass",false)).apply();changedSettings();}),new LinearLayout.LayoutParams(0,dp(44),1));controls.addView(button(getString(R.string.reset),R.string.reset,()->{resetSound(tab);soundTab="";showSound(tab);}),new LinearLayout.LayoutParams(0,dp(44),1));soundPanel.addView(controls);
        if(tab.equals("eq")){
            Spinner preset=spinner(new String[]{getString(R.string.custom),getString(R.string.flat),getString(R.string.bass_boost),getString(R.string.vocal),getString(R.string.bright)},0);soundPanel.addView(preset);preset.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){boolean first=true;public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int pos,long id){if(first){first=false;return;}if(pos==0)return;float[][] gains={{0,0,0,0,0,0,0,0,0,0},{5,5,4,2,0,-1,-1,0,1,1},{-2,-2,-1,0,2,4,4,2,0,-1},{-2,-1,0,0,0,1,2,3,4,4}};SharedPreferences.Editor edit=prefs.edit();for(int i=0;i<10;i++)edit.putFloat("g"+i,gains[pos-1][i]);edit.apply();changedSettings();main.post(()->{soundTab="";showSound("eq");});}});
            for(int i=0;i<10;i++)slider(soundPanel,(int)DspEngine.FREQUENCIES[i]+" Hz","g"+i,-12,12,0,"dB",1);
            slider(soundPanel,R.string.preamp,"preamp",-12,12,0,"dB",1);slider(soundPanel,R.string.width,"width",0,2,1,"",100);check(soundPanel,R.string.safe_gain,"safe",true);
        }else if(tab.equals("knobs")){slider(soundPanel,R.string.bass,"bass",-12,12,0,"dB",1);slider(soundPanel,R.string.presence,"presence",-12,12,0,"dB",1);slider(soundPanel,R.string.air,"air",-12,12,0,"dB",1);slider(soundPanel,R.string.drive,"drive",0,1,0,"%",100);slider(soundPanel,R.string.compression,"compression",0,1,0,"%",100);slider(soundPanel,R.string.balance,"balance",-1,1,0,"",100);}
        else {slider(soundPanel,R.string.tempo,"tempo",.5f,2,1,"×",100);slider(soundPanel,R.string.pitch,"pitch",-12,12,0,"",2);check(soundPanel,R.string.tape,"tape",false);slider(soundPanel,R.string.echo,"echo",0,1,0,"%",100);slider(soundPanel,R.string.echo_time,"echoTime",80,1200,320,"ms",1);slider(soundPanel,R.string.feedback,"feedback",0,.8f,.35f,"%",100);slider(soundPanel,R.string.reverb,"reverb",0,1,0,"%",100);slider(soundPanel,R.string.room,"room",0,1,.5f,"%",100);slider(soundPanel,R.string.filter,"filter",20,20000,20000,"Hz",1);slider(soundPanel,R.string.resonance,"resonance",.5f,2.5f,.707f,"",1000);slider(soundPanel,R.string.crush,"crush",0,1,0,"%",100);slider(soundPanel,R.string.mix,"mix",0,1,1,"%",100);}
    }
    private void resetSound(String tab){SharedPreferences.Editor edit=prefs.edit();String[] keys=tab.equals("eq")?new String[]{"g0","g1","g2","g3","g4","g5","g6","g7","g8","g9","preamp","width","safe"}:tab.equals("knobs")?new String[]{"bass","presence","air","drive","compression","balance"}:new String[]{"tempo","pitch","tape","echo","echoTime","feedback","reverb","room","filter","resonance","crush","mix"};for(String key:keys)edit.remove(key);edit.apply();changedSettings();}
    private void changedSettings(){if(controller!=null)controller.sendCustomCommand(new SessionCommand(PlaybackService.SETTINGS,Bundle.EMPTY),Bundle.EMPTY);}
    private void check(LinearLayout parent,int label,String key,boolean fallback){CheckBox view=new CheckBox(this);view.setText(label);view.setTextColor(text);view.setChecked(prefs.getBoolean(key,fallback));view.setOnCheckedChangeListener((button,value)->{prefs.edit().putBoolean(key,value).apply();changedSettings();});parent.addView(view);}
    private void slider(LinearLayout parent,int label,String key,float min,float max,float fallback,String unit,int steps){slider(parent,getString(label),key,min,max,fallback,unit,steps);}
    private void slider(LinearLayout parent,String caption,String key,float min,float max,float fallback,String unit,int steps){LinearLayout line=column();TextView name=label("",12);SeekBar bar=new SeekBar(this);bar.setMax(Math.max(1,Math.round((max-min)*steps)));float value=prefs.getFloat(key,fallback);bar.setProgress(Math.round((Math.max(min,Math.min(max,value))-min)*steps));bar.setProgressTintList(ColorStateList.valueOf(accent));bar.setThumbTintList(ColorStateList.valueOf(accent));bar.setContentDescription(caption);java.util.function.Consumer<Float> show=v->name.setText(caption+"   "+String.format(Locale.getDefault(),unit.equals("%")?"%.0f%s":"%.2f %s",unit.equals("%")?v*100:v,unit));show.accept(value);bar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int progress,boolean user){if(!user)return;float next=min+progress/(float)steps;prefs.edit().putFloat(key,next).apply();show.accept(next);changedSettings();}});line.addView(name);line.addView(bar,new LinearLayout.LayoutParams(-1,dp(38)));parent.addView(line);}
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private LinearLayout column(){LinearLayout view=new LinearLayout(this);view.setOrientation(LinearLayout.VERTICAL);return view;}
    private LinearLayout row(){LinearLayout view=new LinearLayout(this);view.setOrientation(LinearLayout.HORIZONTAL);view.setGravity(Gravity.CENTER_VERTICAL);return view;}
    private LinearLayout card(){LinearLayout view=column();view.setPadding(dp(12),dp(12),dp(12),dp(12));GradientDrawable shape=new GradientDrawable();shape.setColor(surface);shape.setCornerRadius(dp(12));shape.setStroke(dp(1),Color.argb(90,Color.red(accent),Color.green(accent),Color.blue(accent)));view.setBackground(shape);LinearLayout.LayoutParams size=new LinearLayout.LayoutParams(-1,-2);size.setMargins(0,dp(9),0,0);view.setLayoutParams(size);return view;}
    private TextView label(String value,int size){TextView view=new TextView(this);view.setText(value);view.setTextColor(text);view.setTextSize(size);view.setGravity(Gravity.CENTER_VERTICAL);view.setPadding(dp(4),dp(4),dp(4),dp(4));return view;}
    private Button button(String caption,int description,Runnable action){Button view=new Button(this);view.setText(caption);view.setTextColor(accent);view.setTextSize(12);view.setAllCaps(false);view.setMinWidth(0);view.setMinimumWidth(0);view.setPadding(dp(6),0,dp(6),0);view.setBackgroundTintList(ColorStateList.valueOf(surface));view.setContentDescription(getString(description));view.setOnClickListener(v->action.run());return view;}
    private Spinner spinner(String[] values,int selection){Spinner view=new Spinner(this);ArrayAdapter<String> data=new ArrayAdapter<>(this,android.R.layout.simple_spinner_item,values);data.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);view.setAdapter(data);view.setSelection(Math.max(0,Math.min(values.length-1,selection)));return view;}
    private void toast(int message){Toast.makeText(this,message,Toast.LENGTH_LONG).show();}
    private void pickMusic(){Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true).putExtra(Intent.EXTRA_MIME_TYPES,new String[]{"audio/*","video/*"});startActivityForResult(intent,PICK_MUSIC);}
    private void pickFolder(){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION),PICK_FOLDER);}
    private void pickList(){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE),PICK_LIST);}
    private void createDocument(int request,String name){startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/json").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,name),request);}
    private boolean persist(Uri uri,int flags){try{getContentResolver().takePersistableUriPermission(uri,flags&Intent.FLAG_GRANT_READ_URI_PERMISSION);return true;}catch(SecurityException ignored){return false;}}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(result!=RESULT_OK||data==null)return;Uri uri=data.getData();
        if(request==SAVE_LIST&&uri!=null&&controller!=null){String content=StateStore.capture(controller).toString();worker.execute(()->writeDocument(uri,content,R.string.saving));return;}
        if(request==SAVE_PRESET&&uri!=null){JSONObject preset=new JSONObject();try{preset.put("format","WaveDeckAndroidSound");JSONObject values=new JSONObject();for(String key:soundKeys()){Object value=prefs.getAll().get(key);if(value!=null)values.put(key,value);}preset.put("settings",values);}catch(JSONException ignored){}String content=preset.toString();worker.execute(()->writeDocument(uri,content,R.string.preset_saved));return;}
        if(request==PICK_PRESET&&uri!=null){worker.execute(()->{try{JSONObject values=new JSONObject(FileLibrary.text(this,uri)).getJSONObject("settings");SharedPreferences.Editor edit=prefs.edit();for(String key:soundKeys()){if(!values.has(key)){edit.remove(key);continue;}Object value=values.get(key);if(value instanceof Boolean)edit.putBoolean(key,(Boolean)value);else if(value instanceof Number){float number=((Number)value).floatValue();if(Float.isFinite(number))edit.putFloat(key,number);}}edit.apply();runOnUiThread(()->{changedSettings();String previous=soundTab;soundTab="";if(!previous.isEmpty())showSound(previous);});}catch(Exception error){runOnUiThread(()->toast(R.string.load_error));}});return;}
        if(request==PICK_FOLDER&&uri!=null){persist(uri,data.getFlags());runImport(()->FileLibrary.scan(this,uri,prefs.getBoolean("subfolders",true),prefs.getInt("maximum",10000)),false,false);return;}
        if(request==PICK_LIST&&uri!=null){runImport(()->FileLibrary.playlist(this,uri,prefs.getInt("maximum",10000)),false,false);return;}
        if(request==PICK_MUSIC){List<Uri> selected=new ArrayList<>();if(data.getClipData()!=null)for(int i=0;i<data.getClipData().getItemCount();i++)selected.add(data.getClipData().getItemAt(i).getUri());else if(uri!=null)selected.add(uri);for(Uri item:selected)persist(item,data.getFlags());runImport(()->{List<Track> tracks=new ArrayList<>();for(Uri item:selected){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();tracks.add(FileLibrary.read(this,item));}return tracks;},false,false);}
    }
    private void writeDocument(Uri uri,String content,int message){try(OutputStream output=getContentResolver().openOutputStream(uri,"wt")){if(output==null)throw new IOException();output.write(content.getBytes(StandardCharsets.UTF_8));runOnUiThread(()->toast(message));}catch(IOException error){runOnUiThread(()->toast(R.string.load_error));}}
    private void acceptIntent(Intent intent){if(intent==null)return;if(Intent.ACTION_SEND.equals(intent.getAction())){String input=intent.getStringExtra(Intent.EXTRA_TEXT);if(input!=null){java.util.regex.Matcher match=java.util.regex.Pattern.compile("https?://[^\\s<>]+").matcher(input);if(match.find()){url.setText(match.group());importUrl(false,false);}}}else if(Intent.ACTION_VIEW.equals(intent.getAction())&&intent.getData()!=null){Uri uri=intent.getData();if(SourceLinks.isNetwork(uri.toString())){url.setText(uri.toString());importUrl(false,false);}else if("content".equals(uri.getScheme())){boolean remembered=persist(uri,intent.getFlags());runImport(()->List.of(remembered?FileLibrary.read(this,uri):FileLibrary.copyForPlayback(this,uri)),true,false);}}}
    private void runImport(Callable<List<Track>> action,boolean start,boolean download) {
        if(controller==null||importing!=null&&!importing.isDone())return;
        int generation=++importGeneration;status.setText(R.string.loading);
        importing=worker.submit(()->{
            try {
                List<Track> tracks=action.call();if(Thread.currentThread().isInterrupted())return;
                runOnUiThread(()->{
                    if(isDestroyed()||generation!=importGeneration||controller==null)return;
                    int first=append(tracks);
                    if(first>=0&&download)for(int i=first;i<controller.getMediaItemCount();i++)offline(Track.fromItem(controller.getMediaItemAt(i)));
                    if(first>=0&&start){controller.seekToDefaultPosition(first);controller.prepare();controller.play();}
                    if(tracks.isEmpty())status.setText(R.string.no_compatible_tracks);
                });
            }catch(Exception error){runOnUiThread(()->{if(!isDestroyed()&&generation==importGeneration)status.setText(error instanceof InterruptedException||error instanceof InterruptedIOException?R.string.import_cancelled:R.string.load_error);});}
        });
    }
    private int append(List<Track> tracks){if(controller==null)return -1;Set<String> existing=new HashSet<>();for(int i=0;i<controller.getMediaItemCount();i++){Track track=Track.fromItem(controller.getMediaItemAt(i));existing.add(track.source+":"+track.startMs+":"+track.endMs);}List<MediaItem> items=new ArrayList<>();int maximum=Math.max(1,Math.min(50000,prefs.getInt("maximum",10000)));for(Track track:tracks){if(controller.getMediaItemCount()+items.size()>=maximum)break;if(existing.add(track.source+":"+track.startMs+":"+track.endMs))items.add(track.mediaItem());}int first=controller.getMediaItemCount();if(!items.isEmpty())controller.addMediaItems(items);status.setText(getString(R.string.added,items.size()));return items.isEmpty()?-1:first;}
    private void importUrl(boolean next,boolean all) {
        if(controller==null||importing!=null&&!importing.isDone())return;
        String input=next&&importPage!=null?importPage.url():url.getText().toString();
        int selectedMode=mode.getSelectedItemPosition(),generation=++importGeneration;
        prefs.edit().putInt("mode",selectedMode).apply();status.setText(R.string.loading);
        importing=worker.submit(()->{
            try {
                Sources.ImportPage page=Sources.importUrl(input,prefs.getBoolean("chapters",true),next&&importPage!=null?importPage.next():null);
                boolean started=next;
                do {
                    if(Thread.currentThread().isInterrupted()||generation!=importGeneration)throw new InterruptedIOException();
                    Sources.ImportPage ready=page;boolean canStart=!started;
                    java.util.concurrent.atomic.AtomicBoolean full=new java.util.concurrent.atomic.AtomicBoolean(),addedAny=new java.util.concurrent.atomic.AtomicBoolean();
                    CountDownLatch added=new CountDownLatch(1);
                    runOnUiThread(()->{
                        try {
                            if(isDestroyed()||generation!=importGeneration||controller==null)return;
                            importPage=ready;loadNext.setVisibility(ready.next()!=null?View.VISIBLE:View.GONE);
                            int first=append(ready.tracks());addedAny.set(first>=0);
                            if(first>=0&&canStart&&selectedMode==2){
                                Track target=Track.fromItem(controller.getMediaItemAt(first));Bundle args=new Bundle();args.putString("key",SourceLinks.cacheKey(target.source));args.putString("item",target.id);
                                controller.sendCustomCommand(new SessionCommand(PlaybackService.AUTO_DOWNLOAD,Bundle.EMPTY),args);
                            }
                            if(first>=0&&selectedMode!=0)for(int i=first;i<controller.getMediaItemCount();i++)offline(Track.fromItem(controller.getMediaItemAt(i)));
                            if(first>=0&&canStart&&selectedMode==0){controller.seekToDefaultPosition(first);controller.prepare();controller.play();}
                            full.set(controller.getMediaItemCount()>=prefs.getInt("maximum",10000));
                        }finally{added.countDown();}
                    });
                    added.await();if(addedAny.get())started=true;
                    if(page.next()==null||full.get()||isDestroyed()||generation!=importGeneration||(!all&&(next||started||selectedMode==1)))break;
                    page=Sources.importUrl(page.url(),prefs.getBoolean("chapters",true),page.next());
                }while(!Thread.currentThread().isInterrupted());
            }catch(Exception error){runOnUiThread(()->{if(!isDestroyed()&&generation==importGeneration)status.setText(error instanceof InterruptedException||error instanceof InterruptedIOException?R.string.import_cancelled:R.string.load_error);});}
        });
    }
    private void offline(Track track){if(!SourceLinks.isNetwork(track.source))return;try{String id=SourceLinks.cacheKey(track.source);DownloadRequest request=new DownloadRequest.Builder(id,track.mediaItem().localConfiguration.uri).setCustomCacheKey(id).setData(track.json().toString().getBytes(StandardCharsets.UTF_8)).build();DownloadService.sendAddDownload(this,WaveDownloadService.class,request,true);status.setText(R.string.download_started);}catch(RuntimeException error){status.setText(R.string.download_error);}}
    private void trackMenu(View anchor,int index){if(controller==null||index>=controller.getMediaItemCount())return;Track track=Track.fromItem(controller.getMediaItemAt(index));PopupMenu menu=new PopupMenu(this,anchor);menu.getMenu().add(R.string.play).setOnMenuItemClickListener(item->{controller.seekToDefaultPosition(index);controller.prepare();controller.play();return true;});if(SourceLinks.isNetwork(track.source)){menu.getMenu().add(R.string.save_offline).setOnMenuItemClickListener(item->{offline(track);return true;});menu.getMenu().add(R.string.remove_offline).setOnMenuItemClickListener(item->{DownloadService.sendRemoveDownload(this,WaveDownloadService.class,SourceLinks.cacheKey(track.source),true);return true;});menu.getMenu().add(R.string.share).setOnMenuItemClickListener(item->{startActivity(Intent.createChooser(new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,track.source),getString(R.string.share)));return true;});}menu.getMenu().add(R.string.remove).setOnMenuItemClickListener(item->{controller.removeMediaItem(index);return true;});menu.show();}
    private void playlistMenu(View anchor){PopupMenu menu=new PopupMenu(this,anchor);menu.getMenu().add(R.string.load_list).setOnMenuItemClickListener(item->{pickList();return true;});menu.getMenu().add(R.string.save_list).setOnMenuItemClickListener(item->{createDocument(SAVE_LIST,"My playlist.wavedeck");return true;});menu.getMenu().add(R.string.load_all).setOnMenuItemClickListener(item->{if(importPage!=null&&importPage.next()!=null)importUrl(true,true);return true;});menu.getMenu().add(R.string.save_preset).setOnMenuItemClickListener(item->{createDocument(SAVE_PRESET,"WaveDeck-sound.json");return true;});menu.getMenu().add(R.string.load_preset).setOnMenuItemClickListener(item->{startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("application/json").addCategory(Intent.CATEGORY_OPENABLE),PICK_PRESET);return true;});menu.getMenu().add(R.string.offline).setOnMenuItemClickListener(item->{showOffline();return true;});menu.getMenu().add(R.string.clear).setOnMenuItemClickListener(item->{new AlertDialog.Builder(this).setMessage(R.string.clear_prompt).setNegativeButton(R.string.cancel,null).setPositiveButton(R.string.clear,(dialog,which)->{if(controller!=null){controller.stop();controller.clearMediaItems();}}).show();return true;});menu.show();}
    private void showOffline(){worker.execute(()->{try{List<Track> tracks=new ArrayList<>();List<String> labels=new ArrayList<>();try(DownloadCursor cursor=MediaRuntime.downloads(this).getDownloadIndex().getDownloads()){while(cursor.moveToNext()){Download download=cursor.getDownload();Track track=Track.fromJson(new JSONObject(new String(download.request.data,StandardCharsets.UTF_8)));tracks.add(track);labels.add(track.title+" · "+(download.state==Download.STATE_COMPLETED?getString(R.string.download_complete):Math.max(0,(int)download.getPercentDownloaded())+"%"));}}runOnUiThread(()->{if(isDestroyed())return;new AlertDialog.Builder(this).setTitle(R.string.offline).setItems(labels.toArray(new String[0]),(dialog,which)->{int index=append(List.of(tracks.get(which)));if(index<0)for(int i=0;i<controller.getMediaItemCount();i++)if(Track.fromItem(controller.getMediaItemAt(i)).source.equals(tracks.get(which).source)){index=i;break;}if(index>=0){controller.seekToDefaultPosition(index);controller.prepare();controller.play();}}).setPositiveButton(R.string.close,null).show();});}catch(Exception error){runOnUiThread(()->toast(R.string.load_error));}});}
    private void showSettings(){LinearLayout body=column();body.setPadding(dp(18),dp(8),dp(18),dp(8));String[] themes={"Modern","OldSchool","Retro"};Spinner theme=spinner(themes,Arrays.asList(themes).indexOf(prefs.getString("theme","Modern")));body.addView(label(getString(R.string.theme),13));body.addView(theme);Spinner language=spinner(new String[]{"English","Português (Brasil)"},"pt-BR".equals(prefs.getString("language","en"))?1:0);body.addView(label(getString(R.string.language),13));body.addView(language);
        CheckBox chapters=new CheckBox(this);chapters.setText(R.string.split_chapters);chapters.setChecked(prefs.getBoolean("chapters",true));body.addView(chapters);CheckBox subfolders=new CheckBox(this);subfolders.setText(R.string.scan_subfolders);subfolders.setChecked(prefs.getBoolean("subfolders",true));body.addView(subfolders);body.addView(label(getString(R.string.maximum),13));EditText maximum=new EditText(this);maximum.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);maximum.setText(String.valueOf(prefs.getInt("maximum",10000)));body.addView(maximum);
        body.addView(label(getString(R.string.app_updates),14));body.addView(label("WaveDeck "+BuildConfig.VERSION_NAME,12));CheckBox updates=new CheckBox(this);updates.setText(R.string.startup_updates);updates.setChecked(prefs.getBoolean("startupUpdates",true));body.addView(updates);body.addView(button(getString(R.string.check_updates),R.string.check_updates,()->AndroidUpdates.check(this,true)));body.addView(label(getString(R.string.battery_hint),12));body.addView(label(getString(R.string.offline_hint),12));body.addView(button(getString(R.string.about),R.string.about,()->new AlertDialog.Builder(this).setTitle(R.string.about).setMessage(getString(R.string.about_text,BuildConfig.VERSION_NAME)).setPositiveButton(R.string.close,null).show()));body.addView(button(getString(R.string.licenses),R.string.licenses,this::showLicenses));ScrollView scroll=new ScrollView(this);scroll.addView(body);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(R.string.settings).setView(scroll).setNegativeButton(R.string.cancel,null).setPositiveButton(R.string.save,null).create();dialog.setOnShowListener(ignored->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view->{int limit;try{limit=Integer.parseInt(maximum.getText().toString());}catch(NumberFormatException error){toast(R.string.invalid_limit);return;}if(limit<1||limit>50000){toast(R.string.invalid_limit);return;}prefs.edit().putString("theme",themes[theme.getSelectedItemPosition()]).putString("language",language.getSelectedItemPosition()==1?"pt-BR":"en").putBoolean("chapters",chapters.isChecked()).putBoolean("subfolders",subfolders.isChecked()).putBoolean("startupUpdates",updates.isChecked()).putInt("maximum",limit).apply();dialog.dismiss();recreate();}));dialog.show();
    }
    private void showLicenses(){try(InputStream input=getAssets().open("licenses/NOTICE.txt");ByteArrayOutputStream output=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];int read;while((read=input.read(buffer))!=-1)output.write(buffer,0,read);String notice=output.toString(StandardCharsets.UTF_8.name());new AlertDialog.Builder(this).setTitle(R.string.licenses).setMessage(notice).setPositiveButton(R.string.close,null).show();}catch(IOException error){toast(R.string.load_error);}}
    private static Set<String> soundKeys(){Set<String> keys=new HashSet<>(Arrays.asList("eq","safe","preamp","width","bass","presence","air","drive","compression","balance","tempo","pitch","tape","echo","echoTime","feedback","reverb","room","filter","resonance","crush","mix"));for(int i=0;i<10;i++)keys.add("g"+i);return keys;}
}
