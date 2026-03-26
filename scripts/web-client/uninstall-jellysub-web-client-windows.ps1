$ErrorActionPreference = 'Stop'

$candidates = @(
  'C:\Program Files\Jellyfin\Server\jellyfin-web',
  'C:\Program Files\Jellyfin\jellyfin-web',
  "$env:LOCALAPPDATA\Programs\Jellyfin\resources\jellyfin-web",
  "$env:LOCALAPPDATA\Programs\Jellyfin Desktop\resources\jellyfin-web"
)

function Revert-Root([string]$root) {
  $index = Join-Path $root 'index.html'
  $config = Join-Path $root 'config.json'
  $plugin = Join-Path $root 'jellysub-context-plugin.js'

  if (!(Test-Path $index) -or !(Test-Path $config)) {
    return $false
  }

  if (Test-Path $plugin) {
    Remove-Item $plugin -Force
  }

  $indexText = Get-Content $index -Raw
  $indexText = $indexText.Replace("    <script src=`"jellysub-context-plugin.js`"></script>`r`n", '')
  $indexText = $indexText.Replace("    <script src=`"jellysub-context-plugin.js`"></script>`n", '')
  Set-Content -Path $index -Value $indexText -Encoding UTF8

  $json = Get-Content $config -Raw | ConvertFrom-Json
  if ($null -ne $json.plugins) {
    $json.plugins = @($json.plugins | Where-Object { $_ -ne 'jellysubContext' })
    $json | ConvertTo-Json -Depth 16 | Set-Content -Path $config -Encoding UTF8
  }

  Write-Host "Reverted: $root"
  return $true
}

$reverted = $false
foreach ($root in $candidates) {
  if (Revert-Root $root) {
    $reverted = $true
  }
}

if (-not $reverted) {
  Write-Host 'No default Jellyfin web root found. Edit $candidates in this script for a custom install.'
  exit 1
}

Write-Host 'Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.'
