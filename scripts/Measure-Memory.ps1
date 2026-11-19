param([string]$Executable, [string]$Output, [int]$SampleCount=7, [int]$WarmSeconds=3)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
$tonePath = Join-Path $taskRoot 'memory-tone.wav'
& (Join-Path $PSScriptRoot '..\tools\ffmpeg.exe') -hide_banner -loglevel error -f lavfi -i 'sine=frequency=440:duration=120' -ac 2 -ar 48000 -y $tonePath
if ($LASTEXITCODE -ne 0) { throw 'Memory fixture generation failed.' }
@{Volume=0;Theme='Modern';RememberQueue=$true;Queue=@(@{Title='Memory benchmark';LocalPath=$tonePath;Duration=120})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'settings.json') -Encoding UTF8
if (!('WaveDeckMemoryInput' -as [type])) { Add-Type 'using System; using System.Runtime.InteropServices; public static class WaveDeckMemoryInput { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,int m,IntPtr w,IntPtr l); }' }
$playerProcess = Start-Process -FilePath $Executable -ArgumentList @('--data-dir',$taskRoot) -WindowStyle Hidden -PassThru
$samples = [Collections.Generic.List[object]]::new()
try {
    Start-Sleep -Seconds 5
    foreach ($phase in @('Idle','Playing','Minimized')) {
        $playerProcess.Refresh()
        if ($phase -eq 'Playing') { [WaveDeckMemoryInput]::PostMessage($playerProcess.MainWindowHandle,0x319,$playerProcess.MainWindowHandle,[IntPtr](14 -shl 16)) | Out-Null }
        if ($phase -eq 'Minimized') { [WaveDeckMemoryInput]::PostMessage($playerProcess.MainWindowHandle,0x112,[IntPtr]0xF020,[IntPtr]0) | Out-Null }
        Start-Sleep -Seconds $WarmSeconds
        for ($sampleIndex=0; $sampleIndex -lt $SampleCount; $sampleIndex++) {
            $playerProcess.Refresh()
            $samples.Add([pscustomobject]@{Phase=$phase;WorkingSetMB=[Math]::Round($playerProcess.WorkingSet64/1MB,2);PrivateMB=[Math]::Round($playerProcess.PrivateMemorySize64/1MB,2);CpuSeconds=$playerProcess.TotalProcessorTime.TotalSeconds})
            Start-Sleep -Seconds 1
        }
    }
    $samples | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'memory.json') -Encoding UTF8
    $samples | Group-Object Phase | ForEach-Object { [pscustomobject]@{Phase=$_.Name;MeanWorkingSetMB=[Math]::Round(($_.Group | Measure-Object WorkingSetMB -Average).Average,2);MeanPrivateMB=[Math]::Round(($_.Group | Measure-Object PrivateMB -Average).Average,2);CpuSeconds=$_.Group[-1].CpuSeconds-$_.Group[0].CpuSeconds} }
} finally { $playerProcess.Refresh(); if (!$playerProcess.HasExited) { $playerProcess.CloseMainWindow() | Out-Null; if (!$playerProcess.WaitForExit(5000)) { $playerProcess.Kill() } }; $playerProcess.Dispose() }
