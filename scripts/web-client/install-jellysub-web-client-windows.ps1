$ErrorActionPreference = 'Stop'

$pluginUrl = 'https://raw.githubusercontent.com/McLytir/JellySub/main/web-client/jellysub-context-plugin.js'
$candidates = @(
  'C:\Program Files\Jellyfin\Server\jellyfin-web',
  'C:\Program Files\Jellyfin\jellyfin-web',
  "$env:LOCALAPPDATA\Programs\Jellyfin\resources\jellyfin-web",
  "$env:LOCALAPPDATA\Programs\Jellyfin Desktop\resources\jellyfin-web"
)

function Patch-Root([string]$root) {
  $index = Join-Path $root 'index.html'
  $config = Join-Path $root 'config.json'
  $plugin = Join-Path $root 'jellysub-context-plugin.js'

  if (!(Test-Path $index) -or !(Test-Path $config)) {
    return $false
  }

  Invoke-WebRequest -Uri $pluginUrl -OutFile $plugin

  $indexText = Get-Content $index -Raw
  if ($indexText -notmatch 'jellysub-context-plugin.js') {
    if ($indexText.Contains('</body>')) {
      $indexText = $indexText.Replace('</body>', "    <script src=`"jellysub-context-plugin.js`"></script>`r`n</body>")
    } else {
      $indexText = $indexText.Replace('</head>', "    <script src=`"jellysub-context-plugin.js`"></script>`r`n</head>")
    }
    Set-Content -Path $index -Value $indexText -Encoding UTF8
  }

  $json = Get-Content $config -Raw | ConvertFrom-Json
  if ($null -eq $json.plugins) {
    $json | Add-Member -NotePropertyName plugins -NotePropertyValue @()
  }
  if ($json.plugins -notcontains 'jellysubContext') {
    $json.plugins += 'jellysubContext'
    $json | ConvertTo-Json -Depth 16 | Set-Content -Path $config -Encoding UTF8
  }

  Write-Host "Patched: $root"
  return $true
}

$patched = $false
foreach ($root in $candidates) {
  if (Patch-Root $root) {
    $patched = $true
  }
}

if (-not $patched) {
  Write-Host 'No default Jellyfin web root found. Edit $candidates in this script for a custom install.'
  exit 1
}

Write-Host 'Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.'
