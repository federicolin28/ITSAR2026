# Client a riga di comando per Claude Unity Bridge.
# Uso: unity <comando> [valori posizionali] [chiave=valore ...]
# Codici di uscita: 0 = ok, 1 = errore (compilazione, test falliti, comando rifiutato), 2 = Unity non raggiungibile

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$command = if ($args.Count -gt 0) { [string]$args[0] } else { 'help' }
$rest = if ($args.Count -gt 1) { @($args[1..($args.Count - 1)] | ForEach-Object { [string]$_ }) } else { @() }

# ---------- individuazione del server ----------

function Find-ServerFile {
    foreach ($start in @((Get-Location).Path, $PSScriptRoot)) {
        $dir = $start
        while ($dir) {
            $file = Join-Path $dir 'Library\ClaudeBridge\server.json'
            if (Test-Path $file) { return $file }
            $parent = Split-Path $dir -Parent
            if ($parent -eq $dir) { break }
            $dir = $parent
        }
    }
    return $null
}

if ($env:UNITY_BRIDGE_URL) {
    $Base = $env:UNITY_BRIDGE_URL.TrimEnd('/')
} elseif ($env:UNITY_BRIDGE_PORT) {
    $Base = "http://127.0.0.1:$($env:UNITY_BRIDGE_PORT)"
} else {
    $port = 7777
    $serverFile = Find-ServerFile
    if ($serverFile) {
        try { $port = (Get-Content $serverFile -Raw | ConvertFrom-Json).port } catch { }
    }
    $Base = "http://127.0.0.1:$port"
}

# ---------- HTTP ----------

function Invoke-Bridge([string]$Endpoint, [hashtable]$Params = @{}, [int]$TimeoutSec = 60) {
    $pairs = @()
    foreach ($key in $Params.Keys) {
        foreach ($value in @($Params[$key])) {
            $pairs += [Uri]::EscapeDataString([string]$key) + '=' + [Uri]::EscapeDataString([string]$value)
        }
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($pairs -join '&'))
    try {
        $req = [System.Net.HttpWebRequest]::Create("$Base/$Endpoint")
        $req.Method = 'POST'
        $req.ContentType = 'application/x-www-form-urlencoded'
        $req.Headers.Add('X-Claude-Bridge', 'cli')
        $req.Timeout = $TimeoutSec * 1000
        $req.ReadWriteTimeout = $TimeoutSec * 1000
        $req.Proxy = $null
        $stream = $req.GetRequestStream()
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Close()
        try {
            $resp = $req.GetResponse()
        } catch {
            $ex = $_.Exception
            while ($ex -and -not ($ex -is [System.Net.WebException])) { $ex = $ex.InnerException }
            if ($ex -and $ex.Response) { $resp = $ex.Response } else { throw }
        }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $reader.Close()
        $resp.Close()
    } catch {
        return [pscustomobject]@{ Offline = $true; Ok = $false; Data = $null; Raw = $_.Exception.Message }
    }
    $data = $null
    try { $data = $text | ConvertFrom-Json } catch { }
    $ok = $data -ne $null -and $data.ok -ne $false
    return [pscustomobject]@{ Offline = $false; Ok = $ok; Data = $data; Raw = $text }
}

function Exit-Offline {
    Write-Output "Unity non risponde su $Base."
    Write-Output "Controlla che l'Editor sia aperto sul progetto con il pacchetto Claude Unity Bridge (menu Tools > Claude Bridge)."
    Write-Output "Se sta compilando o importando, riprova tra qualche secondo (oppure: unity wait)."
    exit 2
}

function Write-Result($r) {
    if ($r.Offline) { Exit-Offline }
    if (-not $r.Ok) {
        if ($r.Data -and $r.Data.error) { Write-Output "ERRORE: $($r.Data.error)" } else { Write-Output "ERRORE: $($r.Raw)" }
        exit 1
    }
    $d = $r.Data
    if ($d -eq $null) { Write-Output $r.Raw }
    elseif ($d.PSObject.Properties['text']) { Write-Output $d.text }
    elseif ($d.PSObject.Properties['result']) { Write-Output $d.result }
    else { Write-Output ($d | ConvertTo-Json -Depth 8) }
}

function Get-Status([int]$TimeoutSec = 5) {
    $r = Invoke-Bridge 'status' @{} $TimeoutSec
    if ($r.Ok) { return $r.Data }
    return $null
}

# Attende finché $Condition (che riceve lo stato) restituisce true
function Wait-Until([scriptblock]$Condition, [int]$TimeoutSec = 300) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $s = Get-Status
        if ($s -and (& $Condition $s $sw.Elapsed.TotalSeconds)) { return $s }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Write-CompileReport {
    $r = Invoke-Bridge 'compile'
    if ($r.Offline) { Exit-Offline }
    $d = $r.Data
    $errors = @($d.errors)
    $warnings = @($d.warnings)
    if ($errors.Count -gt 0) {
        Write-Output "COMPILAZIONE FALLITA: $($errors.Count) errori"
        foreach ($e in $errors) { Write-Output "  $($e.message)" }
        $script:CompileOk = $false
        return
    }
    $script:CompileOk = $true
    Write-Output "Compilazione OK ($($warnings.Count) warning$(if ($d.finishedAt) { ", ultima alle $($d.finishedAt)" }))"
    foreach ($w in $warnings) { Write-Output "  $($w.message)" }
}

function Write-Logs($entries) {
    foreach ($e in @($entries)) {
        if ($e -eq $null) { continue }
        Write-Output "#$($e.id) $($e.time) [$($e.type)] $($e.message)"
        if ($e.stack) {
            foreach ($line in ($e.stack -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 6)) { Write-Output "      $line" }
        }
    }
}

# ---------- argomenti ----------

$aliases = @{
    'open' = 'scene/open'; 'save' = 'scene/save'; 'find' = 'find-assets'; 'errors' = 'compile'
    'tree' = 'hierarchy'; 'clear' = 'logs/clear'
}
if ($aliases.ContainsKey($command)) { $command = $aliases[$command] }

$positional = @{
    'menu' = @('path'); 'inspect' = @('path', 'component'); 'set' = @('path', 'component', 'property', 'value')
    'create' = @('name'); 'instantiate' = @('asset'); 'add-component' = @('path', 'type'); 'remove-component' = @('path', 'type')
    'delete' = @('path'); 'select' = @('path'); 'exec' = @('type', 'method'); 'scene/open' = @('path'); 'scene/save' = @('path')
    'find-assets' = @('filter'); 'hierarchy' = @('path'); 'screenshot' = @('view'); 'logs' = @('type')
    'tests' = @('mode'); 'run' = @('seconds')
}

$params = @{}
$names = @(if ($positional.ContainsKey($command)) { $positional[$command] })
$next = 0
foreach ($token in $rest) {
    if ($token -match '^([A-Za-z][\w-]*)=(.*)$') {
        $key = $Matches[1]; $value = $Matches[2]
    } elseif ($next -lt $names.Count) {
        $key = $names[$next]; $value = $token; $next++
    } else {
        # I valori in più diventano argomenti del metodo per 'exec'
        $key = 'arg'; $value = $token
    }
    if ($params.ContainsKey($key)) { $params[$key] = @($params[$key]) + $value } else { $params[$key] = $value }
}

# ---------- comandi ----------

switch ($command) {
    'help' {
        Write-Output @'
Claude Unity Bridge - client CLI

  unity status                      stato dell'Editor
  unity refresh [force=1]           importa i file modificati, attende la compilazione e mostra gli errori
  unity compile                     errori/warning dell'ultima compilazione
  unity wait                        attende che Unity abbia finito di compilare/importare
  unity logs [error|warning|problems] [since=<id>] [limit=<n>]
  unity clear                       svuota la console
  unity play | stop | pause         play mode (play e stop attendono il cambio di stato)
  unity run [secondi]               play per N secondi (default 5), poi stop e riepilogo di errori e warning
  unity scenes                      elenco delle scene
  unity open <Assets/...unity> [save=1|discard=1]
  unity save [Assets/Scenes/Nome.unity]   salva le scene aperte (con percorso: salva con nome)
  unity tree [<percorso>] [depth=<n>] [components=0]
  unity inspect <percorso> [<Componente>]
  unity set <percorso> <Componente> <proprieta> <valore>
  unity create <nome> [primitive=Cube] [parent=<percorso>] [position=x,y,z] [rotation=x,y,z] [scale=x,y,z]
  unity instantiate <Assets/...prefab> [parent=] [name=] [position=]
  unity add-component <percorso> <Tipo>
  unity remove-component <percorso> <Tipo>
  unity delete <percorso>
  unity select <percorso o asset>
  unity undo
  unity find "<filtro>" [folder=Assets/...]   es. unity find "t:Prefab Enemy"
  unity menu "<voce/di/menu>"       es. unity menu "GameObject/3D Object/Cube"
  unity exec <Classe> <Metodo> [arg ...]      invoca un metodo statico (anche di script Editor scritti da Claude)
  unity screenshot [camera|scene] [width=1280 height=720 camera=<percorso>]
  unity tests [EditMode|PlayMode] [filter=<regex>] [save=1]
  unity raw <endpoint> [chiave=valore ...]    chiama direttamente un endpoint del server

I percorsi dei GameObject sono come nella Hierarchy: Environment/Ground
'@
        Write-Result (Invoke-Bridge 'help')
    }

    'refresh' {
        $before = Get-Status
        if (-not $before) { Exit-Offline }
        $r = Invoke-Bridge 'refresh' $params 300
        if ($r.Offline) { Exit-Offline }
        if (-not $r.Ok) { Write-Result $r }
        Write-Output 'Refresh eseguito, attendo la compilazione...'
        $s = Wait-Until {
            param($s, $elapsed)
            if ($s.isCompiling -or $s.isUpdating) { return $false }
            if ($s.compileId -ne $before.compileId) {
                # Con errori non c'è ricaricamento del dominio; senza errori aspetta che sia completato
                return ($s.compileErrors -gt 0) -or ($s.reloadId -ne $before.reloadId)
            }
            return $elapsed -gt 3   # nessuna compilazione partita: niente script modificati
        }
        if (-not $s) { Write-Output 'Timeout: Unity sta ancora lavorando.'; exit 1 }
        Write-CompileReport
        if (-not $script:CompileOk) { exit 1 }
    }

    'compile' {
        Write-CompileReport
        if (-not $script:CompileOk) { exit 1 }
    }

    'wait' {
        $s = Wait-Until { param($s) -not $s.isCompiling -and -not $s.isUpdating } 300
        if (-not $s) { Exit-Offline }
        Write-Output 'Unity è pronto.'
    }

    'logs' {
        if (-not $params.ContainsKey('limit')) { $params['limit'] = 50 }
        $r = Invoke-Bridge 'logs' $params
        if (-not $r.Ok) { Write-Result $r }
        Write-Logs $r.Data.entries
        Write-Output "(ultimo id: $($r.Data.lastId) - usa since=$($r.Data.lastId) per vedere solo i nuovi)"
    }

    'play' {
        Write-Result (Invoke-Bridge 'play')
        $s = Wait-Until { param($s) $s.isPlaying -and -not $s.isCompiling } 120
        if (-not $s) { Write-Output 'Il play mode non è partito (errori di compilazione?)'; exit 1 }
        Write-Output 'Play mode attivo.'
    }

    'stop' {
        Write-Result (Invoke-Bridge 'stop')
        $s = Wait-Until { param($s) -not $s.isPlaying } 120
        if (-not $s) { exit 1 }
        Write-Output 'Play mode terminato.'
    }

    'run' {
        $seconds = if ($params['seconds']) { [double]$params['seconds'] } else { 5 }
        $before = Get-Status
        if (-not $before) { Exit-Offline }
        if ($before.compileErrors -gt 0) { Write-CompileReport; Write-Output 'Correggi prima gli errori di compilazione.'; exit 1 }
        $r = Invoke-Bridge 'play'
        if (-not $r.Ok) { Write-Result $r }
        $s = Wait-Until { param($s) $s.isPlaying -and -not $s.isCompiling } 120
        if (-not $s) { Write-Output 'Il play mode non è partito.'; exit 1 }
        Write-Output "Play mode attivo, lascio girare il gioco per $seconds secondi..."
        Start-Sleep -Milliseconds ([int]($seconds * 1000))
        if ($params['screenshot']) {
            $shot = Invoke-Bridge 'screenshot'
            if ($shot.Ok) { Write-Output "Screenshot: $($shot.Data.path)" }
        }
        $logs = Invoke-Bridge 'logs' @{ since = $before.lastLogId; type = 'problems'; limit = 100 }
        Invoke-Bridge 'stop' | Out-Null
        Wait-Until { param($s) -not $s.isPlaying } 120 | Out-Null
        $entries = @($logs.Data.entries | Where-Object { $_ -ne $null })
        if ($entries.Count -eq 0) {
            Write-Output 'Nessun errore o warning durante il play.'
        } else {
            Write-Output "$($entries.Count) messaggi durante il play:"
            Write-Logs $entries
            if (@($entries | Where-Object { $_.type -ne 'Warning' }).Count -gt 0) { exit 1 }
        }
    }

    'tests' {
        $r = Invoke-Bridge 'tests/run' $params
        if (-not $r.Ok) { Write-Result $r }
        $runId = $r.Data.runId
        Write-Output "Test $($r.Data.mode) avviati, attendo i risultati..."
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $result = $null
        while ($sw.Elapsed.TotalSeconds -lt 900) {
            Start-Sleep -Milliseconds 700
            $t = Invoke-Bridge 'tests/result' @{} 10
            if ($t.Ok -and $t.Data.runId -eq $runId -and $t.Data.status -eq 'finished') { $result = $t.Data; break }
        }
        if (-not $result) { Write-Output 'Timeout in attesa dei test.'; exit 1 }
        Write-Output "Risultato: $($result.result) - passati $($result.passed), falliti $($result.failed), saltati $($result.skipped) ($([math]::Round($result.duration, 2)) s)"
        foreach ($f in @($result.failures)) {
            if ($f -eq $null) { continue }
            Write-Output "FALLITO $($f.test)"
            Write-Output "  $($f.message)"
            foreach ($line in ($f.stack -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 4)) { Write-Output "    $line" }
        }
        if ($result.failed -gt 0) { exit 1 }
    }

    'raw' {
        $endpoint = if ($rest.Count -gt 0) { $rest[0] } else { 'help' }
        $p = @{}
        foreach ($token in ($rest | Select-Object -Skip 1)) {
            if ($token -match '^([A-Za-z][\w-]*)=(.*)$') {
                if ($p.ContainsKey($Matches[1])) { $p[$Matches[1]] = @($p[$Matches[1]]) + $Matches[2] } else { $p[$Matches[1]] = $Matches[2] }
            }
        }
        $r = Invoke-Bridge $endpoint $p 300
        if ($r.Offline) { Exit-Offline }
        Write-Output $r.Raw
        if (-not $r.Ok) { exit 1 }
    }

    default {
        Write-Result (Invoke-Bridge $command $params 300)
    }
}
exit 0
