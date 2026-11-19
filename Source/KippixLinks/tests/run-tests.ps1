# Offline tests for KippixLinks.dll (Windows PowerShell 5.1):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
# Only made-up addresses (example.com / example.org / test.invalid). No browser, no Playnite, temp folder removed after.
param([string]$TempRoot = [IO.Path]::GetTempPath())
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dll = Join-Path $root "build\KippixLinks.dll"
if (-not (Test-Path $dll)) { Write-Host "FAIL 0: build\KippixLinks.dll not found (run build.ps1 first)"; exit 1 }
$sdk = Join-Path $env:LOCALAPPDATA "Playnite\Playnite.SDK.dll"
if (Test-Path $sdk) { Add-Type -Path $sdk }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Security, System.Web.Extensions
Add-Type -Path $dll

$script:n = 0
$script:failed = 0
function Check([bool]$ok, [string]$what) {
    $script:n++
    if ($ok) { Write-Host ("PASS " + $script:n) }
    else { $script:failed++; Write-Host ("FAIL " + $script:n + ": " + $what) }
}

# ---------------- LinkUrl.TryParse ----------------
$accept = @(
    @("https://example.com/show/1", "https://example.com/show/1"),
    @("  http://example.org/a?b=c  ", "http://example.org/a?b=c"),
    @("example.com/watch/42", "https://example.com/watch/42"),
    @("www.example.org", "https://www.example.org/"),
    @("example.com:8080/x", "https://example.com:8080/x"),
    @("HTTPS://EXAMPLE.COM/Path", "https://example.com/Path"),
    @("https://sub.test.invalid/p#frag", "https://sub.test.invalid/p#frag")
)
foreach ($c in $accept) {
    $u = $null
    $ok = [KippixLinks.LinkUrl]::TryParse($c[0], [ref]$u)
    Check ($ok -and $u -eq $c[1]) ("TryParse accepts '" + $c[0] + "' as '" + $c[1] + "' (got '" + $u + "')")
}
$reject = @(
    $null, "", "   ", "example", "ftp://example.com/x", "javascript:alert(1)", "javascript:alert(document.domain)",
    "file:///C:/x.txt", "file://example.com/x", "mailto:a@example.com", "https://user:pw@example.com/",
    "https://user@example.org/", "https://example.com/a b", ('https://example.com/' + [char]0x60 + 'x'), "https://example.com/""x",
    "https://example.com/<x>", ("https://example.com/" + ("a" * 2040)), "https://example.com/x`r`nLocation: y",
    "data:text/html,hi", "about:blank", "C:\folder\file.txt", "https://", "http:///nohost"
)
foreach ($r in $reject) {
    $u = $null
    $ok = [KippixLinks.LinkUrl]::TryParse($r, [ref]$u)
    $label = if ($r -eq $null) { "(null)" } elseif ($r.Length -gt 60) { $r.Substring(0, 60) + "..." } else { $r }
    Check (-not $ok -and $u -eq $null) ("TryParse rejects '" + $label + "'")
}
$u = $null
Check ([KippixLinks.LinkUrl]::TryParse(("https://example.com/" + ("a" * 2000)), [ref]$u)) "TryParse accepts a long but allowed address"

# the 2048 limit applies after normalising, so a parsed address always parses again to itself
$kata = -join (1..250 | ForEach-Object { [char]0x30A2 })
$u = $null
Check (-not [KippixLinks.LinkUrl]::TryParse(("https://example.com/" + $kata), [ref]$u)) "TryParse rejects an address that grows past 2048 when percent-encoded"
$u = $null
Check (-not [KippixLinks.LinkUrl]::TryParse(("example.com/" + ("a" * 2032)), [ref]$u)) "TryParse rejects a bare address that grows past 2048 with https://"
$u = $null
$okShort = [KippixLinks.LinkUrl]::TryParse(("https://example.com/" + (-join (1..20 | ForEach-Object { [char]0x30A2 }))), [ref]$u)
$u2 = $null
Check ($okShort -and [KippixLinks.LinkUrl]::TryParse($u, [ref]$u2) -and $u2 -ceq $u -and $u.IndexOf('%') -gt 0) "a non-ASCII path is percent-encoded and parses again to itself"
foreach ($c in $accept) {
    $u = $null; $u2 = $null
    [KippixLinks.LinkUrl]::TryParse($c[0], [ref]$u) | Out-Null
    Check ([KippixLinks.LinkUrl]::TryParse($u, [ref]$u2) -and $u2 -ceq $u) ("TryParse is idempotent for '" + $c[0] + "'")
}
# international hosts are kept as punycode (an ASCII Location header); invisible format characters are refused
$u = $null
$idn = "https://b" + [char]0x00FC + "cher.example/x"
Check ([KippixLinks.LinkUrl]::TryParse($idn, [ref]$u) -and $u -ceq "https://xn--bcher-kva.example/x") ("an international host becomes punycode (got '" + $u + "')")
foreach ($fmtChar in @([char]0x200B, [char]0x202E, [char]0x2060, [char]0xFEFF)) {
    $u = $null
    Check (-not [KippixLinks.LinkUrl]::TryParse(("https://exa" + $fmtChar + "mple.com/x"), [ref]$u)) ("TryParse rejects a host with format character U+" + ([int]$fmtChar).ToString("X4"))
    $u = $null
    Check (-not [KippixLinks.LinkUrl]::TryParse(("https://example.com/a" + $fmtChar + "b"), [ref]$u)) ("TryParse rejects a path with format character U+" + ([int]$fmtChar).ToString("X4"))
}

# ---------------- LinkUrl.Site ----------------
$sites = @(
    @("https://www.netflix.com/title/1", "Netflix"), @("https://www.primevideo.com/detail/x", "Prime Video"),
    @("https://www.amazon.co.uk/gp/video/detail/x", "Prime Video"), @("https://www.amazon.com/Some-Show/dp/x/Amazon-Video", "Prime Video"),
    @("https://www.amazon.com/dp/x", "amazon.com"), @("https://www.disneyplus.com/series/x", "Disney+"),
    @("https://www.bbc.co.uk/iplayer/episodes/x", "BBC iPlayer"), @("https://www.bbc.co.uk/news", "bbc.co.uk"),
    @("https://www.bbc.com/iplayer/x", "BBC iPlayer"), @("https://www.itv.com/watch/x", "ITVX"), @("https://www.channel4.com/programmes/x", "Channel 4"),
    @("https://www.channel5.com/show/x", "5"), @("https://www.my5.tv/x", "5"), @("https://www.youtube.com/watch?v=x", "YouTube"),
    @("https://youtu.be/x", "YouTube"), @("https://m.youtube.com/x", "YouTube"), @("https://www.crunchyroll.com/series/x", "Crunchyroll"),
    @("https://tv.apple.com/show/x", "Apple TV"), @("https://www.paramountplus.com/shows/x", "Paramount+"), @("https://play.max.com/show/x", "Max"),
    @("https://www.hbomax.com/x", "Max"), @("https://www.nowtv.com/watch/x", "NOW"), @("https://www.discoveryplus.com/show/x", "discovery+"),
    @("https://www.twitch.tv/x", "Twitch"), @("https://app.plex.tv/x", "Plex"), @("https://www.example.com/x", "example.com"),
    @("https://shows.example.org/x", "shows.example.org"), @("https://notnetflix.com/x", "notnetflix.com")
)
foreach ($s in $sites) {
    $got = [KippixLinks.LinkUrl]::Site($s[0])
    Check ($got -ceq $s[1]) ("Site('" + $s[0] + "') = '" + $s[1] + "' (got '" + $got + "')")
}

# ---------------- LinkUrl.CleanTitle ----------------
$zw = [char]0x200B
$em = [char]0x2014
$en = [char]0x2013
$titles = @(
    @("Show One - Mozilla Firefox", "Show One"),
    @(("Show One " + $em + " Mozilla Firefox"), "Show One"),
    @("Show One - Mozilla Firefox Private Browsing", "Show One"),
    @("Show Two - Google Chrome", "Show Two"),
    @(("Show Three - Personal - Microsoft" + $zw + " Edge"), "Show Three"),
    @("Show Three - Work - Microsoft Edge", "Show Three"),
    @(("Show Four " + $en + " Brave"), "Show Four"),
    @("Show Five - Profile 2 - Vivaldi", "Show Five"),
    @("Show Six - Opera", "Show Six"),
    @("Show Seven - Chromium", "Show Seven"),
    @("Watch - Part 2 - Google Chrome", "Watch - Part 2"),
    @("New Tab - Google Chrome", $null), @("Mozilla Firefox", $null), @("Untitled", $null), @("Loading...", $null),
    @(("Loading" + [char]0x2026), $null), @("127.0.0.1:1234/abc - Google Chrome", $null), @("https://example.com/x - Brave", $null),
    @("about:blank - Mozilla Firefox", $null), @("x", $null), @("", $null), @("   ", $null),
    # a window that only says its browser's name (nothing loaded yet) is not a page title
    @("Google Chrome", $null), @("Microsoft Edge", $null), @(("Microsoft" + $zw + " Edge"), $null), @("Brave", $null), @("mozilla firefox", $null),
    @("Mozilla Firefox Private Browsing", $null), @("Zen Browser", $null), @("Opera House Tour - Opera", "Opera House Tour"),
    @((("T" * 100) + " - Google Chrome"), ("T" * 80))
)
foreach ($t in $titles) {
    $got = [KippixLinks.LinkUrl]::CleanTitle($t[0])
    Check ($got -ceq $t[1]) ("CleanTitle('" + $t[0] + "') = '" + $t[1] + "' (got '" + $got + "')")
}
Check ([KippixLinks.LinkUrl]::CleanTitle($null) -eq $null) "CleanTitle(null) is null"

# ---------------- LinkStore ----------------
$tmp = Join-Path $TempRoot ("kxlinks-test-" + [Guid]::NewGuid().ToString("N"))
try {
    $store = New-Object KippixLinks.LinkStore $tmp
    Check ($store.Items.Count -eq 0) "a new store is empty"
    $ex = $false
    $a = $store.AddOrTouch("https://example.com/secret-show-alpha", [ref]$ex)
    Check (-not $ex) "first add is new"
    $b = $store.AddOrTouch("example.org/hidden/beta-path", [ref]$ex)
    Check ($b.Url -eq "https://example.org/hidden/beta-path") "bare address stored as https"
    Check ($store.Items[0] -eq $b -and $store.Items[1] -eq $a) "newest first"
    $a2 = $store.AddOrTouch("https://example.com/secret-show-alpha", [ref]$ex)
    Check ($ex -and $a2 -eq $a -and $store.Items.Count -eq 2) "dedupe on the same address"
    Check ($store.Items[0] -eq $a) "re-added item sorts first"
    Check ($a.Id -match '^[0-9a-f]{8}$' -and $a.Id -ne $b.Id) "ids are 8 hex and differ"
    Check ($a.Display -eq "example.com" -and $a.Detail -eq "not opened yet") "display / detail before opening"
    $store.MarkOpened($b)
    Check ($store.Items[0] -eq $b -and $b.Count -eq 1 -and $b.OpenedUtc -ne [DateTime]::MinValue) "MarkOpened counts and sorts first"
    Check ($b.Detail -eq "opened today") "detail after opening (no name or title: just the time)"
    $store.SetTitle($b, "  Hidden Beta Title Zeta  ")
    Check ($b.Title -eq "Hidden Beta Title Zeta" -and $b.Display -eq "Hidden Beta Title Zeta") "SetTitle"
    Check ($b.Detail -eq ([string]"example.org " + [char]0x00B7 + " opened today")) "detail with a title shows the site"
    $store.SetName($a, ("  Secret Name Gamma " + ("x" * 80)))
    Check ($a.Name.Length -le 60 -and $a.Name.StartsWith("Secret Name Gamma")) "SetName trims and clips to 60"
    $store.SetName($a, "Secret Name Gamma")
    $store.SetName($b, "   ")
    Check ($b.Name -eq $null) "an empty name is null"
    try { $store.AddOrTouch("javascript:alert(1)", [ref]$ex) | Out-Null; Check $false "AddOrTouch refuses a non-web address" }
    catch { Check ($_.Exception.InnerException.Message -eq "Not a web address.") "AddOrTouch refuses a non-web address (fixed text)" }

    # reload from disk
    $re = New-Object KippixLinks.LinkStore $tmp
    $same = $re.Items.Count -eq 2
    for ($i = 0; $i -lt 2 -and $same; $i++) {
        $x = $store.Items[$i]; $y = $re.Items[$i]
        $same = $x.Url -eq $y.Url -and $x.Title -eq $y.Title -and $x.Name -eq $y.Name -and $x.Count -eq $y.Count -and $x.Id -eq $y.Id -and $x.AddedUtc -eq $y.AddedUtc -and $x.OpenedUtc -eq $y.OpenedUtc
    }
    Check $same "reload gives the same items, order and ids"

    # settings.json
    $ps = [KippixLinks.PanelSettings]::Load($tmp)
    Check ($ps.Pinned -and -not $ps.Stay -and $ps.Before -eq "options" -and $ps.Titles) "settings defaults"
    $ps.Stay = $true; $ps.Before = "clock"; $ps.Save()
    $ps2 = [KippixLinks.PanelSettings]::Load($tmp)
    Check ($ps2.Stay -and $ps2.Before -eq "clock") "settings round trip"

    # nothing secret in any file of the data folder (UTF-8, UTF-16LE, and base64 of UTF-8 at every alignment)
    $secrets = @("example.com", "example.org", "secret-show-alpha", "hidden/beta-path", "Hidden Beta Title Zeta", "Secret Name Gamma", "beta-path")
    $latin = [Text.Encoding]::GetEncoding(28591)
    function Needles([string]$sec) {
        $list = @()
        foreach ($enc in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) { $list += $latin.GetString($enc.GetBytes($sec)) }
        $raw = [Text.Encoding]::UTF8.GetBytes($sec)
        for ($o = 0; $o -lt 3; $o++) {
            $b64 = [Convert]::ToBase64String([byte[]]((@(0x41) * $o) + $raw))
            $skip = if ($o -gt 0) { 4 } else { 0 }
            if ($b64.Length - $skip - 4 -ge 8) { $list += $b64.Substring($skip, $b64.Length - $skip - 4) }
        }
        return $list
    }
    function FolderLeaks([string]$folder) {
        foreach ($f in (Get-ChildItem -LiteralPath $folder -Recurse -File -Force)) {
            $hay = $latin.GetString([IO.File]::ReadAllBytes($f.FullName))
            foreach ($sec in $secrets) {
                foreach ($needle in (Needles $sec)) {
                    if ($hay.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $f.Name }
                }
            }
        }
        return $null
    }
    # positive control: the same search does find a secret that is there
    $ctl = Join-Path $tmp "control"
    New-Item -ItemType Directory $ctl | Out-Null
    [IO.File]::WriteAllText((Join-Path $ctl "plain.txt"), "x Secret Name Gamma y")
    [IO.File]::WriteAllText((Join-Path $ctl "b64.txt"), [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("zz" + "secret-show-alpha")))
    Check ((FolderLeaks $ctl) -ne $null) "control: the leak search finds a plain secret"
    Remove-Item (Join-Path $ctl "plain.txt")
    Check ((FolderLeaks $ctl) -ne $null) "control: the leak search finds a base64 secret"
    Remove-Item $ctl -Recurse -Force
    $leakFile = FolderLeaks $tmp
    Check ($leakFile -eq $null) ("no file in the data folder holds a test address, title or name (" + $leakFile + ")")
    Check (-not (Test-Path (Join-Path $tmp "links.bin.tmp"))) "no temp file left after saving"
    # and it really is DPAPI with the add-on's entropy: it opens with it (and holds the secrets), not without it
    $sealed = [IO.File]::ReadAllBytes((Join-Path $tmp "links.bin"))
    $hdr = [byte[]](0x01,0x00,0x00,0x00,0xD0,0x8C,0x9D,0xDF,0x01,0x15,0xD1,0x11,0x8C,0x7A,0x00,0xC0,0x4F,0xC2,0x97,0xEB)
    $hdrOk = $sealed.Length -gt $hdr.Length
    for ($i = 0; $i -lt $hdr.Length -and $hdrOk; $i++) { if ($sealed[$i] -ne $hdr[$i]) { $hdrOk = $false } }
    Check $hdrOk "links.bin starts with the DPAPI blob header"
    $opened = $null
    try { $opened = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($sealed, [Text.Encoding]::UTF8.GetBytes("KippixLinks.v1"), [Security.Cryptography.DataProtectionScope]::CurrentUser)) } catch { $opened = $null }
    Check ($opened -ne $null -and $opened.Contains("secret-show-alpha") -and $opened.Contains("Secret Name Gamma") -and $opened.Contains("Hidden Beta Title Zeta")) "links.bin decrypts with the add-on's entropy and holds the list"
    $opened = $null
    $noEntropy = $true
    try { [Security.Cryptography.ProtectedData]::Unprotect($sealed, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser) | Out-Null; $noEntropy = $false } catch { }
    Check $noEntropy "links.bin doesn't decrypt without the entropy"
    Check (-not $store.SaveFailed -and -not $store.Unavailable) "SaveFailed / Unavailable are false after good saves"

    # remove
    $store.Remove($a)
    Check ($store.Items.Count -eq 1 -and $store.Items[0] -eq $b) "Remove"

    # cap 40
    for ($i = 1; $i -le 45; $i++) { $store.AddOrTouch(("https://test.invalid/item/" + $i), [ref]$ex) | Out-Null }
    Check ($store.Items.Count -eq 40) "capped at 40"
    Check ($store.Items[0].Url -eq "https://test.invalid/item/45" -and $store.Items[39].Url -eq "https://test.invalid/item/6") "the oldest are dropped"

    # clear rotates ids
    $c1 = $store.AddOrTouch("https://example.com/rotate", [ref]$ex)
    $id1 = $c1.Id
    $store.Clear()
    Check ($store.Items.Count -eq 0) "Clear empties the list"
    $c2 = $store.AddOrTouch("https://example.com/rotate", [ref]$ex)
    Check ($c2.Id -ne $id1) "Clear makes a new key (ids change)"
    $re2 = New-Object KippixLinks.LinkStore $tmp
    Check ($re2.Items.Count -eq 1 -and $re2.Items[0].Id -eq $c2.Id) "after Clear the file holds only the new list"

    # corrupted file
    [IO.File]::WriteAllBytes((Join-Path $tmp "links.bin"), [byte[]](1..64))
    $bad = New-Object KippixLinks.LinkStore $tmp
    Check ($bad.Items.Count -eq 0) "a corrupted links.bin gives an empty store"
    Check ((Test-Path (Join-Path $tmp "links.bin.unreadable")) -and -not (Test-Path (Join-Path $tmp "links.bin"))) "it is set aside as links.bin.unreadable"
    [IO.File]::WriteAllBytes((Join-Path $tmp "links.bin"), [byte[]](5..80))
    $bad2 = New-Object KippixLinks.LinkStore $tmp
    Check ($bad2.Items.Count -eq 0 -and (Test-Path (Join-Path $tmp "links.bin.unreadable")) -and -not (Test-Path (Join-Path $tmp "links.bin"))) "a second unreadable file replaces the older one"
    $bad2.AddOrTouch("https://example.org/after", [ref]$ex) | Out-Null
    $re3 = New-Object KippixLinks.LinkStore $tmp
    Check ($re3.Items.Count -eq 1) "the store works again after that"

    # "Forget all" leaves no older copy behind: a set-aside unreadable file (here a real old list) and a temp file go too
    $old = New-Object KippixLinks.LinkStore $tmp
    $old.AddOrTouch("https://example.com/secret-show-alpha", [ref]$ex) | Out-Null
    Copy-Item (Join-Path $tmp "links.bin") (Join-Path $tmp "links.bin.unreadable") -Force
    Copy-Item (Join-Path $tmp "links.bin") (Join-Path $tmp "links.bin.tmp") -Force
    $old.Clear()
    Check (-not (Test-Path (Join-Path $tmp "links.bin.unreadable")) -and -not (Test-Path (Join-Path $tmp "links.bin.tmp"))) "Clear deletes links.bin.unreadable and links.bin.tmp"
    $leakFile = FolderLeaks $tmp
    Check ($leakFile -eq $null) ("after the unreadable-file steps and Clear, no file holds a test secret (" + $leakFile + ")")
    $sealed = [IO.File]::ReadAllBytes((Join-Path $tmp "links.bin"))
    $opened = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($sealed, [Text.Encoding]::UTF8.GetBytes("KippixLinks.v1"), [Security.Cryptography.DataProtectionScope]::CurrentUser))
    Check (-not $opened.Contains("example")) "after Clear the decrypted file holds no address"
    $opened = $null

    # a links.bin that is locked (antivirus, backup, sync) is not damage: left as it is, not set aside, never saved over
    $keep = New-Object KippixLinks.LinkStore $tmp
    $keep.AddOrTouch("https://example.org/kept-while-locked", [ref]$ex) | Out-Null
    $before = [IO.File]::ReadAllBytes((Join-Path $tmp "links.bin"))
    $lock = [IO.File]::Open((Join-Path $tmp "links.bin"), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $locked = New-Object KippixLinks.LinkStore $tmp
        Check ($locked.Unavailable -and $locked.Items.Count -eq 0) "a locked links.bin opens the store as unavailable"
        Check (-not (Test-Path (Join-Path $tmp "links.bin.unreadable"))) "a locked links.bin is not set aside"
        $locked.AddOrTouch("https://test.invalid/while-locked", [ref]$ex) | Out-Null
        Check ($locked.SaveFailed) "an unavailable store reports that it didn't save"
    }
    finally { $lock.Close() }
    $after = [IO.File]::ReadAllBytes((Join-Path $tmp "links.bin"))
    Check ([Convert]::ToBase64String($before) -eq [Convert]::ToBase64String($after)) "the locked links.bin is unchanged afterwards"
    $re4 = New-Object KippixLinks.LinkStore $tmp
    Check (-not $re4.Unavailable -and $re4.Items.Count -eq 1 -and $re4.Items[0].Url -eq "https://example.org/kept-while-locked") "once free again, the list is all there"

    # a save that can't reach the disk is reported (the panel says so)
    $roDir = Join-Path $tmp "ro"
    $ro = New-Object KippixLinks.LinkStore $roDir
    New-Item -ItemType Directory (Join-Path $roDir "links.bin.tmp") | Out-Null   # (a folder where the temp file must go: the write fails)
    $ro.AddOrTouch("https://test.invalid/not-saved", [ref]$ex) | Out-Null
    Check ($ro.SaveFailed) "SaveFailed after a failed save"
    Remove-Item (Join-Path $roDir "links.bin.tmp") -Recurse -Force
    $ro.AddOrTouch("https://test.invalid/saved", [ref]$ex) | Out-Null
    Check (-not $ro.SaveFailed) "SaveFailed clears after a good save"
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
}
Check (-not (Test-Path $tmp)) "temp folder removed"

# ---------------- Handoff ----------------
function RawRequest([string]$hostPort, [string]$request) {
    $parts = $hostPort.Split(':')
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $c.Connect($parts[0], [int]$parts[1])
        $c.ReceiveTimeout = 5000
        $st = $c.GetStream()
        $bytes = [Text.Encoding]::ASCII.GetBytes($request)
        $st.Write($bytes, 0, $bytes.Length)
        $ms = New-Object IO.MemoryStream
        $buf = New-Object byte[] 4096
        while (($r = $st.Read($buf, 0, $buf.Length)) -gt 0) { $ms.Write($buf, 0, $r) }
        return [Text.Encoding]::UTF8.GetString($ms.ToArray())
    }
    finally { $c.Close() }
}
function CanConnect([string]$hostPort) {
    $parts = $hostPort.Split(':')
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect($parts[0], [int]$parts[1]); return $true } catch { return $false } finally { $c.Close() }
}

$h = New-Object KippixLinks.Handoff
$target = "https://example.com/watch/handoff-test?x=1"
$hop = $h.Begin($target)
Check ($hop -match '^http://127\.0\.0\.1:\d+/[0-9a-f]{32}$') "Begin returns a 127.0.0.1 address with a token"
Check ($h.Alive) "Alive after Begin"
$uri = [Uri]$hop
$hp = "127.0.0.1:" + $uri.Port
$path = $uri.AbsolutePath
$wrong = RawRequest $hp ("GET /nottoken HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n")
Check ($wrong.StartsWith("HTTP/1.1 404") -and $wrong.IndexOf("example.com") -lt 0) "a wrong path gets 404 (and no address)"
$post = RawRequest $hp ("POST " + $path + " HTTP/1.1`r`nHost: 127.0.0.1`r`nContent-Length: 0`r`n`r`n")
Check ($post.StartsWith("HTTP/1.1 404")) "POST to the token gets 404"
$near = RawRequest $hp ("GET " + $path + "/ HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n")
Check ($near.StartsWith("HTTP/1.1 404")) "the token path must match exactly"
Check ($h.Alive) "still alive after bad requests"
$good = RawRequest $hp ("GET " + $path + " HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n")
Check ($good.StartsWith("HTTP/1.1 302 Found`r`n")) "the token path gets 302"
$loc = ($good -split "`r`n" | Where-Object { $_ -like "Location: *" } | Select-Object -First 1)
Check ($loc -ceq ("Location: " + $target)) "302 has the exact Location"
Check ($good -match "Cache-Control: no-store" -and $good -match "Referrer-Policy: no-referrer" -and $good -match "Content-Length: 0" -and $good -match "Connection: close") "302 has the no-store / no-referrer / close headers"
$head = RawRequest $hp ("HEAD " + $path + " HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n")
Check ($head.StartsWith("HTTP/1.1 302")) "a second request within the grace period is answered too"
Start-Sleep -Milliseconds ([KippixLinks.Handoff]::GraceMs + 800)
Check (-not $h.Alive) "not alive after the grace period"
Check (-not (CanConnect $hp)) "the port refuses connections after the grace period"

$first = $h.Begin("https://example.org/first")
$second = $h.Begin("https://example.org/second")
$fu = [Uri]$first; $su = [Uri]$second
Check (-not (CanConnect ("127.0.0.1:" + $fu.Port)) -or $fu.Port -eq $su.Port) "a second Begin closes the first listener"
$r2 = RawRequest ("127.0.0.1:" + $su.Port) ("GET " + $fu.AbsolutePath + " HTTP/1.1`r`n`r`n")
Check ($r2.StartsWith("HTTP/1.1 404")) "the first token isn't answered by the second listener"
$r3 = RawRequest ("127.0.0.1:" + $su.Port) ("GET " + $su.AbsolutePath + " HTTP/1.1`r`n`r`n")
Check ($r3 -match "Location: https://example.org/second`r`n") "the second handoff serves its own address"
# a handoff with "ready": the good request waits for it, a wrong path doesn't
$held = $h.Begin("https://example.org/held", [KippixLinks.Handoff]::After(1200))
$hu = [Uri]$held
$hw = [Diagnostics.Stopwatch]::StartNew()
$w404 = RawRequest ("127.0.0.1:" + $hu.Port) ("GET /nottoken HTTP/1.1`r`n`r`n")
Check ($w404.StartsWith("HTTP/1.1 404") -and $hw.ElapsedMilliseconds -lt 900) "a wrong path isn't held back"
$r4 = RawRequest ("127.0.0.1:" + $hu.Port) ("GET " + $hu.AbsolutePath + " HTTP/1.1`r`n`r`n")
Check ($r4 -match "Location: https://example.org/held`r`n" -and $hw.ElapsedMilliseconds -ge 1100 -and $hw.ElapsedMilliseconds -lt 4000) "the good request is answered once ready (held about 1.2 s)"
$second = $h.Begin("https://example.org/second")
$su = [Uri]$second
# a client trickling its request a byte at a time is cut off after ~3 s in all (not 3 s per byte)
$trickle = New-Object System.Net.Sockets.TcpClient
$clock = [Diagnostics.Stopwatch]::StartNew()
$cutOff = $false
try {
    $trickle.Connect("127.0.0.1", $su.Port)
    $ts = $trickle.GetStream()
    for ($i = 0; $i -lt 12; $i++) {
        $ts.WriteByte(71)
        Start-Sleep -Milliseconds 700
        if ($trickle.Client.Poll(0, [Net.Sockets.SelectMode]::SelectRead)) { $cutOff = $true; break }
    }
}
catch { $cutOff = $true }
finally { $trickle.Close() }
Check ($cutOff -and $clock.ElapsedMilliseconds -lt 6000) ("a trickling client is cut off within the overall deadline (" + $clock.ElapsedMilliseconds + " ms)")
foreach ($badUrl in @("https://example.com/x`r`nSet-Cookie: a=b", "ftp://example.com/x", "javascript:alert(1)", ("https://example.com/" + [char]0x00E9), "https://example.com/a b")) {
    try { $h.Begin($badUrl) | Out-Null; Check $false "Begin refuses a bad address" }
    catch { Check ($_.Exception.InnerException -is [ArgumentException] -and $_.Exception.InnerException.Message -eq "Not a web address.") "Begin refuses a bad address with fixed text" }
}
$h.Stop()
Check (-not $h.Alive -and -not (CanConnect ("127.0.0.1:" + $su.Port))) "Stop closes it"

# ---------------- SafeLog format ----------------
$fmt = [KippixLinks.SafeLog].GetMethod("Format", [Reflection.BindingFlags]"NonPublic,Static")
$e = New-Object System.IO.IOException "secret https://example.com/leak"
$line = $fmt.Invoke($null, [object[]]@("open failed", "0a1b2c3d", [Exception]$e))
Check ($line -like "Links: open failed id=0a1b2c3d: IOException 0x*" -and $line.IndexOf("example") -lt 0) "SafeLog writes the type and HResult, never the message"
$line2 = $fmt.Invoke($null, [object[]]@("x", "https://example.com/", $null))
Check ($line2 -eq "Links: x id=?") "SafeLog won't write something that isn't an id"

Write-Host ""
if ($script:failed -gt 0) { Write-Host ("" + $script:failed + " of " + $script:n + " FAILED"); exit 1 }
Write-Host ("All " + $script:n + " passed")
exit 0
