# make_assistant_bundles.ps1
# Empaqueta todo el código relevante del proyecto TRS para ChatGPT
# Incluye backend, vistas y frontend local (wwwroot).

$ErrorActionPreference = "Stop"

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outDir = "assistant_bundles"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Módulos backend principales
$modules = @(
  "Controllers",
  "Services",
  "Models",
  "ViewModels",
  "Jobs",
  "Data",
  "Areas",
  "Resources",
  "Help",
  "Dataload"
)

function Add-FilesToBundle {
  param(
    [string]$Folder,
    [string]$OutFile,
    [string[]]$Extensions = @("*.cs")
  )
  if (-not (Test-Path $Folder)) { return }

  $files = Get-ChildItem -Path $Folder -Recurse -Include $Extensions | Sort-Object FullName
  if ($files.Count -eq 0) { return }

  "=== BUNDLE: $Folder ($stamp) ===" | Out-File $OutFile -Encoding UTF8
  foreach ($f in $files) {
    "----- FILE: $($f.FullName) -----" | Out-File $OutFile -Append -Encoding UTF8
    Get-Content $f.FullName -Raw | Out-File $OutFile -Append -Encoding UTF8
    "`n" | Out-File $OutFile -Append -Encoding UTF8
  }
  Write-Host "OK -> $OutFile"
}

# Backend bundles
foreach ($m in $modules) {
  $out = Join-Path $outDir ("bundle_" + $m.ToLower() + "_$stamp.txt")
  Add-FilesToBundle -Folder $m -OutFile $out
}

# Program.cs
if (Test-Path "Program.cs") {
  $out = Join-Path $outDir ("bundle_program_$stamp.txt")
  "----- FILE: Program.cs -----" | Out-File $out -Encoding UTF8
  Get-Content "Program.cs" -Raw | Out-File $out -Append -Encoding UTF8
  Write-Host "OK -> $out"
}

# CSPROJ
$projOut = Join-Path $outDir ("bundle_csproj_$stamp.txt")
$projs = Get-ChildItem -Recurse -Include *.csproj
if ($projs) {
  "=== BUNDLE: CSPROJ ($stamp) ===" | Out-File $projOut -Encoding UTF8
  foreach ($p in $projs) {
    "----- FILE: $($p.FullName) -----" | Out-File $projOut -Append -Encoding UTF8
    Get-Content $p.FullName -Raw | Out-File $projOut -Append -Encoding UTF8
    "`n" | Out-File $projOut -Append -Encoding UTF8
  }
  Write-Host "OK -> $projOut"
}

# appsettings.json (saneado)
function Sanitize-AppSettings {
  param([string]$JsonPath, [string]$OutFile)
  if (-not (Test-Path $JsonPath)) { return }
  try {
    $json = Get-Content $JsonPath -Raw | ConvertFrom-Json
  } catch {
    Write-Warning "No pude parsear $JsonPath; lo copiaré tal cual."
    "----- FILE (RAW): $JsonPath -----" | Out-File $OutFile -Append -Encoding UTF8
    Get-Content $JsonPath -Raw | Out-File $OutFile -Append -Encoding UTF8
    return
  }
  if ($json.ConnectionStrings) { $json.PSObject.Properties.Remove('ConnectionStrings') }
  if ($json.SmtpSettings) {
    if ($json.SmtpSettings.Password) { $json.SmtpSettings.Password = '***REDACTED***' }
    if ($json.SmtpSettings.Username) { $json.SmtpSettings.Username = '***REDACTED***' }
  }
  "----- FILE (SANITIZED): $JsonPath -----" | Out-File $OutFile -Append -Encoding UTF8
  ($json | ConvertTo-Json -Depth 10) | Out-File $OutFile -Append -Encoding UTF8
  "`n" | Out-File $OutFile -Append -Encoding UTF8
}

$appOut = Join-Path $outDir ("bundle_appsettings_$stamp.txt")
Sanitize-AppSettings -JsonPath "appsettings.json" -OutFile $appOut
Sanitize-AppSettings -JsonPath "appsettings.Development.json" -OutFile $appOut

# --- NUEVO ---
# VIEWS (todas las .cshtml)
$outViews = Join-Path $outDir ("bundle_views_$stamp.txt")
Add-FilesToBundle -Folder "Views" -OutFile $outViews -Extensions @("*.cshtml")

# WWWROOT (solo js y css, para frontend)
$outWww = Join-Path $outDir ("bundle_wwwroot_$stamp.txt")
Add-FilesToBundle -Folder "wwwroot" -OutFile $outWww -Extensions @("*.js","*.css")

Write-Host ""
Write-Host "Bundles generados en: $outDir"
Write-Host "Sube aquí los *.txt que veas dentro de esa carpeta."

