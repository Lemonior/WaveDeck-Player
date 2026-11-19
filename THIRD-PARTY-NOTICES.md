# WaveDeck dependencies

WaveDeck source is licensed under the MIT license in `LICENSE`. Separately bundled dependencies retain their own licenses.

| Component | Version in this build | License and source |
| --- | --- | --- |
| NAudio | 2.2.1 | MIT: https://github.com/naudio/NAudio/tree/v2.2.1 |
| SoundTouch.Net | 2.3.2 | LGPL-2.1-or-later: https://github.com/owoudenberg/soundtouch.net/tree/98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc; matching source archive and license in tools/ |
| .NET Windows Desktop Runtime | 10.0.12 | Microsoft runtime notices: https://github.com/dotnet/runtime/blob/v10.0.12/LICENSE.TXT and https://github.com/dotnet/wpf |
| yt-dlp Windows binary | 2026.08.19 | Bundled binary GPLv3+; project/source Unlicense: https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19 |
| FFmpeg essentials Windows build | 9.0.2 | GPLv3 build; license in tools/FFmpeg-LICENSE and source/build information: https://www.gyan.dev/ffmpeg/builds/ and https://ffmpeg.org |
| Deno | 2.9.7 | MIT: https://github.com/denoland/deno/tree/v2.9.7 |
| Avalonia Linux desktop | 12.1.3 | MIT: https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/licence.md |
| .NET Linux Runtime | 10.0.12 | MIT and bundled notices: https://github.com/dotnet/runtime/blob/v10.0.12/LICENSE.TXT |
| SkiaSharp / HarfBuzzSharp | Versions in WaveDeck.deps.json | MIT and native third-party notices: https://github.com/mono/SkiaSharp |
| yt-dlp Linux binary | 2026.08.19 | GPLv3+ binary dependencies; project/source Unlicense: https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19 |
| SDL2 and Linux FFmpeg | Installed by distribution | SDL2 zlib license: https://github.com/libsdl-org/SDL; FFmpeg build licenses depend on distribution: https://ffmpeg.org/legal.html |

Official yt-dlp Windows bundles include their JavaScript challenge component. Tools are launched as separate child processes. Download provenance/checksums are captured in `Context/validation.md`; `scripts/Get-AudioTools.ps1` verifies upstream archive/binary checksums before extracting tools. yt-dlp can be updated separately from Settings.

SoundTouch.Net is used without modifications as a separate `SoundTouch.Net.dll`. Its exact upstream source commit matches the NuGet 2.3.2 metadata; `tools/SoundTouch.Net-2.3.2-source.zip` and `tools/SoundTouch.Net-LICENSE.txt` accompany the Windows build. The application source permits rebuilding and replacing this library; dependency provenance is recorded in `Context/soundtouch-source-provenance.json`.
