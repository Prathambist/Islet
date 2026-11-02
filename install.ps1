param (
    [string]$Repo = "Prathambist/MusicIsland",
    [switch]$NoStartup,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$InstallDir = "$env:LOCALAPPDATA\MusicIsland"
$TempZip = "$env:TEMP\MusicIsland-win-x64.zip"
$DownloadUrl = "https://github.com/$Repo/releases/latest/download/MusicIsland-win-x64.zip"
$StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\MusicIsland.lnk"
$StartupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\MusicIsland.lnk"
$ProcessName = "MusicIsland"

# ================= Look =================
# Every symbol is built from its code point, so this file stays plain ASCII and reads
# the same whether it's run from disk or piped in with irm | iex.

function Chars([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }

$Levels = Chars (0x2581..0x2588)                    # eighth blocks, lowest to full
$Spin = Chars 0x25D0, 0x25D3, 0x25D1, 0x25D2         # a record going round
$Note = [string][char]0x266A
$Notes = [string][char]0x266B
$Line = [string][char]0x2500
$Heavy = [string][char]0x2501
$Knob = [string][char]0x25CF
$Corner = @([string][char]0x256D, [string][char]0x256E, [string][char]0x2570, [string][char]0x256F)   # rounded corners
$Side = [string][char]0x2502

# The equalizer keeps each bar's height between frames, so it eases instead of jumping
$EqRows = 3
$script:EqLevels = New-Object double[] 30
$script:EqLast = 0.0

$Tips = @(
    "Hover the island to open the player.",
    "Scroll over the island to change the volume.",
    "Right-click the tray icon to start a timer.",
    "Lyrics show up under the song when you open the island.",
    "When a timer ends, the Glyph lights flicker on the island.",
    "Pick Low, Medium or High under Appearance to trade looks for battery.",
    "The island hides itself in full-screen games, unless you tell it not to.",
    "Click a song title to find it in Spotify."
)

# Animation needs a real console window; anywhere else gets plain lines
$Fancy = $false
try {
    $Fancy = $Host.Name -eq 'ConsoleHost' -and -not [Console]::IsOutputRedirected -and [Console]::WindowWidth -ge 60
}
catch {
}

$Width = 72
if ($Fancy) { $Width = [Math]::Min([Console]::WindowWidth - 1, 72) }

function Put([int]$Row, [object[]]$Parts) {
    [Console]::SetCursorPosition(0, $Row)
    $used = 0

    for ($i = 0; $i -lt $Parts.Count; $i += 2) {
        $text = [string]$Parts[$i]
        if ($used + $text.Length -gt $Width) { $text = $text.Substring(0, [Math]::Max(0, $Width - $used)) }
        Write-Host $text -NoNewline -ForegroundColor $Parts[$i + 1]
        $used += $text.Length
    }

    # Wipe whatever the last frame left on this line
    Write-Host (' ' * [Math]::Max(0, $Width - $used)) -NoNewline
}

function Reserve([int]$Lines) {
    for ($i = 0; $i -lt $Lines; $i++) { Write-Host "" }
    return [Console]::CursorTop - $Lines
}

# ================= The island =================

function Island([int]$Row, [int]$Inner, [object[]]$Content, [string]$Edge = 'DarkGray') {
    $pad = 2
    Put $Row @(((' ' * $pad) + $Corner[0] + ($Line * $Inner) + $Corner[1]), $Edge)

    $parts = @(((' ' * $pad) + $Side), $Edge)
    $length = 0
    for ($i = 0; $i -lt $Content.Count; $i += 2) { $length += ([string]$Content[$i]).Length }
    $parts += $Content
    $parts += @(((' ' * [Math]::Max(0, $Inner - $length)) + $Side), $Edge)
    Put ($Row + 1) $parts

    Put ($Row + 2) @(((' ' * $pad) + $Corner[2] + ($Line * $Inner) + $Corner[3]), $Edge)
}

function Show-Intro {
    if (-not $Fancy) {
        Write-Host ""
        Write-Host "  $Note  MusicIsland installer" -ForegroundColor Magenta
        Write-Host ""
        return
    }

    Write-Host ""
    $row = Reserve 4
    $title = "  M U S I C  I S L A N D  "

    # The island opens up from a little pill, like it does on screen
    foreach ($inner in 4, 8, 14, 22, 32, 40, 44, 42) {
        $show = ""
        if ($inner -ge 40) { $show = $title.Substring(0, [Math]::Min($title.Length, $inner - 8)) }
        Island $row $inner @("  $Note", 'Magenta', $show, 'White') 'DarkGray'
        Start-Sleep -Milliseconds 45
    }

    # A few beats of the bars inside it
    for ($frame = 0; $frame -lt 14; $frame++) {
        $bars = ""
        for ($b = 0; $b -lt 4; $b++) { $bars += $Levels[[int](3.5 + 3.4 * [Math]::Sin($frame * 0.7 + $b * 1.9))] }
        Island $row 42 @("  $Note", 'Magenta', $title, 'White', $bars, 'White') 'DarkGray'
        Start-Sleep -Milliseconds 60
    }

    Put ($row + 3) @("   a Dynamic Island for your Windows desktop", 'DarkGray')
    Write-Host ""
    Write-Host ""
}

# ================= Steps =================

# Runs the work in the background while the equalizer plays, then reports how it went
function Invoke-Step {
    param(
        [string]$Label,
        [scriptblock]$Work,
        [object[]]$Arguments = @(),
        [scriptblock]$Measure = $null,
        [double]$Total = 0,
        [int]$MinimumMs = 0
    )

    $shell = [PowerShell]::Create()
    # The work's own param() has to come first in its block, so it runs nested and gets the arguments passed on
    [void]$shell.AddScript("`$ErrorActionPreference = 'Stop'; `$ProgressPreference = 'SilentlyContinue'; & {" + $Work.ToString() + "} @args")
    foreach ($argument in $Arguments) { [void]$shell.AddArgument($argument) }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $async = $shell.BeginInvoke()

    if ($Fancy) {
        $row = Reserve ($EqRows + 2)
        $frame = 0
        $tip = Get-Random -Maximum $Tips.Count

        while (-not $async.IsCompleted -or $clock.ElapsedMilliseconds -lt $MinimumMs) {
            $t = $clock.Elapsed.TotalSeconds
            Draw-Equalizer $row $t

            $done = 0
            if ($Measure) { try { $done = [double](& $Measure) } catch { } }
            $status = Progress-Text $done $Total $t
            Put ($row + $EqRows) @("   $($Spin[$frame % 4])  ", 'Magenta', $Label.PadRight(30), 'White', $status[0], 'Magenta', $status[1], 'DarkGray', $status[2], 'Gray')

            if ($frame % 80 -eq 0 -and $frame -gt 0) { $tip = ($tip + 1) % $Tips.Count }
            Put ($row + $EqRows + 1) @("      tip: ", 'DarkGray', $Tips[$tip], 'DarkGray')

            $frame++
            Start-Sleep -Milliseconds 45
        }
    }
    else {
        Write-Host "  $Label..." -ForegroundColor Gray
        while (-not $async.IsCompleted) { Start-Sleep -Milliseconds 100 }
    }

    try {
        $result = $shell.EndInvoke($async)
        if ($shell.Streams.Error.Count -gt 0) { throw $shell.Streams.Error[0] }
    }
    finally {
        $shell.Dispose()
        if ($Fancy) {
            # Fold the equalizer away, leaving just the finished line
            for ($i = 0; $i -lt $EqRows + 2; $i++) { Put ($row + $i) @("", 'Gray') }
            [Console]::SetCursorPosition(0, $row)
        }
    }

    return $result
}

function Done([string]$Text, [string]$Detail = "") {
    Write-Host "  $Note " -NoNewline -ForegroundColor Green
    Write-Host $Text -NoNewline -ForegroundColor White
    Write-Host "  $Detail" -ForegroundColor DarkGray
}

function Draw-Equalizer([int]$Row, [double]$T) {
    $count = [Math]::Min($script:EqLevels.Count, [int](($Width - 4) / 2))
    $dt = [Math]::Max(0.0, [Math]::Min(0.1, $T - $script:EqLast))
    $script:EqLast = $T

    # Two beats a second, felt most in the bass on the left
    $beat = [Math]::Pow([Math]::Max(0.0, [Math]::Sin($T * 2 * [Math]::PI * 2)), 3)
    $lines = New-Object string[] $EqRows

    for ($b = 0; $b -lt $count; $b++) {
        $x = $b / ($count - 1)

        # A spectrum-like shape: a slow swell rolling across, a quick shimmer per bar, and the beat
        $swell = 0.62 * (1 - 0.45 * $x) * (0.5 + 0.5 * [Math]::Sin($T * 1.9 - $x * 4.2))
        $shimmer = 0.11 * [Math]::Sin($T * (3.4 + $b * 0.19) + $b * 1.7) + 0.05 * [Math]::Sin($T * (6.1 + $b * 0.13) + $b * 0.6)
        $target = [Math]::Max(0.04, [Math]::Min(1.0, 0.1 + $swell + $shimmer + 0.24 * $beat * (1 - 0.7 * $x)))

        # Quick to rise, slow to fall, like a real meter, but never a hard jump
        $rate = 4.5
        if ($target -gt $script:EqLevels[$b]) { $rate = 11.0 }
        $script:EqLevels[$b] += ($target - $script:EqLevels[$b]) * (1 - [Math]::Exp(-$rate * $dt))

        $eighths = [int][Math]::Round($script:EqLevels[$b] * 8 * $EqRows)

        for ($r = 0; $r -lt $EqRows; $r++) {
            $fill = [Math]::Max(0, [Math]::Min(8, $eighths - ($EqRows - 1 - $r) * 8))
            $char = ' '
            if ($fill -gt 0) { $char = $Levels[$fill - 1] }
            $lines[$r] += "$char "
        }
    }

    for ($r = 0; $r -lt $EqRows; $r++) { Put ($Row + $r) @("   ", 'Gray', $lines[$r], 'White') }
}

# The bar, the numbers after it, and a percentage; a bouncing segment when the size is unknown
function Progress-Text([double]$Done, [double]$Total, [double]$T) {
    $cells = 18

    if ($Total -gt 0) {
        $fraction = [Math]::Min(1.0, $Done / $Total)
        $lit = [int][Math]::Round($fraction * $cells)
        $bar = ($Heavy * $lit) + ($Line * ($cells - $lit))
        return @($bar.Substring(0, $lit), ($bar.Substring($lit) + "  "), ("{0,3}%  {1:0.0}/{2:0.0} MB" -f [int]($fraction * 100), ($Done / 1MB), ($Total / 1MB)))
    }

    $at = [int](($T * 9) % ($cells * 2))
    if ($at -ge $cells) { $at = $cells * 2 - $at - 1 }
    $at = [Math]::Max(0, [Math]::Min($cells - 3, $at))
    $mb = ""
    if ($Done -gt 0) { $mb = "{0:0.0} MB" -f ($Done / 1MB) }
    return @((($Line * $at) + ($Heavy * 3)), (($Line * ($cells - $at - 3)) + "  "), $mb)
}

# ================= Finale =================

function Show-Finale([string]$Version, [bool]$Updated) {
    $verb = "Installed"
    if ($Updated) { $verb = "Updated" }

    if (-not $Fancy) {
        Write-Host ""
        Write-Host "  $verb MusicIsland $Version" -ForegroundColor Green
        return
    }

    Write-Host ""
    $row = Reserve 5
    $inner = 46
    $name = "MusicIsland $Version"
    $edge = 'DarkGray'

    function Card([double]$Fraction, [string]$Edge) {
        $cells = 26
        $at = [int][Math]::Round($Fraction * ($cells - 1))
        $elapsed = [int]($Fraction * 214)
        $time = " {0}:{1:00}  -{2}:{3:00}" -f [int][Math]::Floor($elapsed / 60), ($elapsed % 60), [int][Math]::Floor((214 - $elapsed) / 60), ((214 - $elapsed) % 60)

        Put $row @(("  " + $Corner[0] + ($Line * $inner) + $Corner[1]), $Edge)
        Put ($row + 1) @(("  " + $Side), $Edge, "  $Notes  ", 'Magenta', "Now playing".PadRight($inner - 5), 'DarkGray', $Side, $Edge)
        Put ($row + 2) @(("  " + $Side), $Edge, "     ", 'Gray', $name.PadRight($inner - 5), 'White', $Side, $Edge)
        Put ($row + 3) @(("  " + $Side), $Edge, "     ", 'Gray', ($Heavy * $at), 'White', $Knob, 'White', ($Line * ($cells - $at - 1)), 'DarkGray', $time.PadRight($inner - 5 - $cells), 'DarkGray', $Side, $Edge)
        Put ($row + 4) @(("  " + $Corner[2] + ($Line * $inner) + $Corner[3]), $Edge)
    }

    for ($f = 0; $f -le 30; $f++) {
        Card ($f / 30) $edge
        Start-Sleep -Milliseconds 30
    }

    # The timer's Glyph flicker, borrowed for a little applause
    foreach ($flash in 'White', 'DarkGray', 'White', 'DarkGray', 'White', 'DarkGray') {
        Card 1 $flash
        Start-Sleep -Milliseconds 70
    }

    [Console]::SetCursorPosition(0, $row + 5)
    Write-Host ""
    Write-Host "  $verb. " -NoNewline -ForegroundColor Green
    Write-Host "Look up: the island is at the top of your screen." -ForegroundColor White
}

# ================= Asking first =================

# Says what's about to happen and waits for a yes. Enter counts as yes; n or Esc backs out.
function Confirm-Install([bool]$Updating, [bool]$Running, [double]$Size) {
    if ($Yes) { return $true }

    $interactive = $false
    try { $interactive = [Environment]::UserInteractive -and $Host.Name -eq 'ConsoleHost' -and -not [Console]::IsInputRedirected } catch { }

    # Nobody can answer from a script or a redirected console, so carry on as before
    if (-not $interactive) {
        Write-Host "  Not in an interactive console, so installing without asking." -ForegroundColor DarkGray
        return $true
    }

    $amount = "the latest release"
    if ($Size -gt 0) { $amount = "the latest release ({0:0} MB)" -f ($Size / 1MB) }

    Write-Host "  This will:" -ForegroundColor Gray
    if ($Running) { Write-Host "    - close the island that's running now" -ForegroundColor DarkGray }
    Write-Host "    - download $amount from GitHub" -ForegroundColor DarkGray
    if ($Updating) { Write-Host "    - replace the copy in $InstallDir" -ForegroundColor DarkGray }
    else { Write-Host "    - install it in $InstallDir" -ForegroundColor DarkGray }
    if ($NoStartup) { Write-Host "    - add it to the Start menu" -ForegroundColor DarkGray }
    else { Write-Host "    - add it to the Start menu and start it when you sign in" -ForegroundColor DarkGray }
    Write-Host ""

    $question = "Install MusicIsland?"
    if ($Updating) { $question = "Update MusicIsland?" }
    Write-Host "  $question " -NoNewline -ForegroundColor White
    Write-Host "[Y/n] " -NoNewline -ForegroundColor DarkGray

    try {
        if ($Fancy) { [Console]::CursorVisible = $true }

        # Keys pressed before the question, like a pasted line break, must not answer it
        while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) }

        while ($true) {
            $key = [Console]::ReadKey($true)

            if ($key.Key -eq 'Enter' -or $key.KeyChar -eq 'y' -or $key.KeyChar -eq 'Y') {
                Write-Host "yes" -ForegroundColor Green
                Write-Host ""
                return $true
            }

            if ($key.Key -eq 'Escape' -or $key.KeyChar -eq 'n' -or $key.KeyChar -eq 'N') {
                Write-Host "no" -ForegroundColor Yellow
                return $false
            }
        }
    }
    finally {
        if ($Fancy) { [Console]::CursorVisible = $false }
    }
}

# ================= Install =================

function Install-MusicIsland {
    Show-Intro
    $updated = Test-Path $InstallDir

    $size = 0
    try { $size = [double](Invoke-WebRequest -Uri $DownloadUrl -Method Head -UseBasicParsing -MaximumRedirection 10).Headers['Content-Length'] } catch { }

    $isRunning = [bool](Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)

    if (-not (Confirm-Install $updated $isRunning $size)) {
        Write-Host ""
        Write-Host "  No worries, nothing was changed." -ForegroundColor Gray
        Write-Host ""
        return
    }

    # 1. Stop a running copy so its files can be replaced
    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($running) {
        [void](Invoke-Step "Asking the island to take a bow" { param($name) Stop-Process -Name $name -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800 } @($ProcessName))
        Done "Stopped the running island"
    }

    # 2. Download the latest release, with the size known up front when GitHub says it
    $download = {
        param($url, $path)
        if (Test-Path $path) { Remove-Item $path -Force }
        Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing -MaximumRedirection 10
    }
    $measure = { if (Test-Path $TempZip) { (Get-Item $TempZip).Length } else { 0 } }

    try {
        [void](Invoke-Step "Fetching the latest drop" $download @($DownloadUrl, $TempZip) $measure $size)
    }
    catch {
        if ($Fancy) { Write-Host "  Direct download failed, asking the GitHub API instead..." -ForegroundColor Yellow }
        else { Write-Host "  Direct download failed, querying GitHub API..." -ForegroundColor Yellow }

        try {
            $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UseBasicParsing
            $asset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
            if (-not $asset) { throw "No zip asset found in release." }
            [void](Invoke-Step "Fetching the latest drop" $download @($asset.browser_download_url, $TempZip) $measure ([double]$asset.size))
        }
        catch {
            Write-Host ""
            Write-Host "  Couldn't download MusicIsland: $_" -ForegroundColor Red
            Write-Host "  Check your connection and run the installer again." -ForegroundColor DarkGray
            return
        }
    }

    Done "Downloaded" ("{0:0.0} MB" -f ((Get-Item $TempZip).Length / 1MB))

    # 3. Unpack into LocalAppData
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($TempZip)
    try { $unpacked = [double]($archive.Entries | Measure-Object -Property Length -Sum).Sum } finally { $archive.Dispose() }

    $unpack = {
        param($zip, $folder)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if (Test-Path $folder) { Remove-Item -Path $folder -Recurse -Force }
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $folder)
    }
    $measureFolder = { if (Test-Path $InstallDir) { (Get-ChildItem $InstallDir -Recurse -File | Measure-Object -Property Length -Sum).Sum } else { 0 } }

    try {
        [void](Invoke-Step "Unboxing the vinyl" $unpack @($TempZip, $InstallDir) $measureFolder $unpacked)
    }
    catch {
        Write-Host ""
        Write-Host "  Couldn't unpack MusicIsland into $InstallDir`: $_" -ForegroundColor Red
        Write-Host "  If MusicIsland is still open somewhere, close it and run the installer again." -ForegroundColor DarkGray
        return
    }
    finally {
        Remove-Item -Path $TempZip -Force -ErrorAction SilentlyContinue
    }

    $ExePath = Join-Path $InstallDir "MusicIsland.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Host "  MusicIsland.exe was not found in the extracted files." -ForegroundColor Red
        return
    }

    $Version = ""
    try { $Version = "v" + ((Get-Item $ExePath).VersionInfo.ProductVersion -split '\+')[0] } catch { }
    Done "Unpacked" $InstallDir

    # 4. Shortcuts, with the app's own icon
    $shortcuts = {
        param($exe, $folder, $startMenu, $startup)
        $shell = New-Object -ComObject WScript.Shell

        $link = $shell.CreateShortcut($startMenu)
        $link.TargetPath = $exe
        $link.WorkingDirectory = $folder
        $link.Description = "iPhone-style Dynamic Island Spotify widget for Windows"
        $link.Save()

        if ($startup) {
            $link = $shell.CreateShortcut($startup)
            $link.TargetPath = $exe
            $link.WorkingDirectory = $folder
            $link.Description = "MusicIsland Startup"
            $link.Save()
        }
    }

    $startupLink = $StartupPath
    if ($NoStartup) { $startupLink = $null }
    [void](Invoke-Step "Pinning it to the Start menu" $shortcuts @($ExePath, $InstallDir, $StartMenuPath, $startupLink) -MinimumMs 700)
    Done "Added to the Start menu"
    if (-not $NoStartup) { Done "Starts when you sign in" "turn it off in Settings > General" }

    # 5. Launch
    [void](Invoke-Step "Warming up the amp" { param($exe) Start-Process -FilePath $exe } @($ExePath) -MinimumMs 900)

    Show-Finale $Version $updated
    Write-Host ""
    Write-Host "  Hover it to open the player. Right-click the tray icon for timers and Settings." -ForegroundColor DarkGray
    Write-Host "  Installed in $InstallDir" -ForegroundColor DarkGray
    Write-Host ""
}

try {
    if ($Fancy) { [Console]::CursorVisible = $false }
    Install-MusicIsland
}
finally {
    if ($Fancy) { [Console]::CursorVisible = $true }
}
