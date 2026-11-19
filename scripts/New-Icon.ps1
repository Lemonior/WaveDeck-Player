$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDir = Join-Path $PSScriptRoot '..\MusicPlayer\MusicPlayer\Assets'
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs><linearGradient id="case" x2="0" y2="1"><stop stop-color="#35414a"/><stop offset="1" stop-color="#131c22"/></linearGradient></defs>
  <rect x="6" y="6" width="244" height="244" rx="51" fill="url(#case)" stroke="#67717a" stroke-width="4"/>
  <rect x="26" y="26" width="204" height="204" rx="34" fill="#0e191c" stroke="#32454b" stroke-width="2"/>
  <path d="M44 133 H66 L82 69 L106 193 L131 87 L149 153 H174" fill="none" stroke="#f4c36d" stroke-width="13" stroke-linejoin="round" stroke-linecap="round"/>
  <path d="M178 78 L216 101 L178 124 Z" fill="#7ddbd0"/>
  <path d="M176 164 V196 M191 151 V196 M206 175 V196" fill="none" stroke="#7ddbd0" stroke-width="9" stroke-linecap="round"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $assetDir 'WaveDeck.svg'), $svg)
function Make-Png([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    $gradient = New-Object Windows.Media.LinearGradientBrush ([Windows.Media.ColorConverter]::ConvertFromString('#35414a')), ([Windows.Media.ColorConverter]::ConvertFromString('#131c22')), 90
    $dc.DrawRoundedRectangle($gradient, (New-Object Windows.Media.Pen ([Windows.Media.BrushConverter]::new().ConvertFromString('#67717a')), 4), (New-Object Windows.Rect 6,6,244,244), 51,51)
    $dc.DrawRoundedRectangle(([Windows.Media.BrushConverter]::new().ConvertFromString('#0e191c')), (New-Object Windows.Media.Pen ([Windows.Media.BrushConverter]::new().ConvertFromString('#32454b')), 2), (New-Object Windows.Rect 26,26,204,204), 34,34)
    $gold = New-Object Windows.Media.Pen ([Windows.Media.BrushConverter]::new().ConvertFromString('#f4c36d')), 13
    $gold.StartLineCap = 'Round'; $gold.EndLineCap = 'Round'; $gold.LineJoin = 'Round'
    $dc.DrawGeometry($null, $gold, [Windows.Media.Geometry]::Parse('M44 133 H66 L82 69 L106 193 L131 87 L149 153 H174'))
    $cyan = [Windows.Media.BrushConverter]::new().ConvertFromString('#7ddbd0')
    $dc.DrawGeometry($cyan, $null, [Windows.Media.Geometry]::Parse('M178 78 L216 101 L178 124 Z'))
    $pen = New-Object Windows.Media.Pen $cyan,9
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $pen, [Windows.Media.Geometry]::Parse('M176 164 V196 M191 151 V196 M206 175 V196'))
    $dc.Pop(); $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return ,$stream.ToArray()
}
$sizes = @(16,32,48,256)
$frames = @($sizes | ForEach-Object { ,(Make-Png $_) })
[IO.File]::WriteAllBytes((Join-Path $assetDir 'WaveDeck.png'), $frames[3])
$stream = [IO.File]::Create((Join-Path $assetDir 'WaveDeck.ico'))
$writer = New-Object IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]4)
$offset = 6 + 16 * 4
for ($i = 0; $i -lt 4; $i++) {
    $dim = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()
Write-Output "Created SVG, PNG and ICO in $assetDir"
