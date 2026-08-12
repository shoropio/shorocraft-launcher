# Firma Authenticode (signtool) de los binarios de release.
#
# Uso:
#   .\sign.ps1                                    # firma exe de la app + setup.exe (con timestamp)
#   .\sign.ps1 -Path "src\...\publish\ShoroCraftLauncher.exe"
#   .\sign.ps1 -NoTimestamp                       # si el servidor de timestamp no responde
#
# El certificado se lee del almacen CurrentUser\My (ver: instalador/signing/code-signing.pfx).
# Con un certificado OV/EV real de una CA basta con cambiarlo por "signtool /sha1 <thumb>".

param(
    [string[]]$Path,
    [switch]$NoTimestamp
)

$ErrorActionPreference = "Stop"

$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
if (-not (Test-Path $signtool)) {
    $signtool = (Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $signtool) { throw "signtool.exe no encontrado. Instala el Windows SDK." }

$certSubject = "ShoroCraft Launcher"

if (-not $Path) {
    $publish = "src\ShoroCraftLauncher.App\bin\Release\net8.0-windows\win-x64\publish"
    $setup = "dist\ShoroCraftLauncher_Setup.exe"
    $Path = @((Join-Path $publish "ShoroCraftLauncher.exe"), $setup)
}

foreach ($file in $Path) {
    if (-not (Test-Path $file)) { Write-Host "SKIP (no existe): $file"; continue }

    Write-Host "Firmando: $file"
    $args = @("sign", "/n", $certSubject, "/s", "my", "/fd", "sha256", "/v", $file)
    if (-not $NoTimestamp) {
        $args = @("sign", "/n", $certSubject, "/s", "my", "/fd", "sha256",
                  "/tr", "http://timestamp.digicert.com", "/td", "sha256", "/v", $file)
    }

    & $signtool @args 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        if (-not $NoTimestamp) {
            Write-Host "Fallo con timestamp; reintento sin timestamp."
            & $signtool sign /n $certSubject /s my /fd sha256 /v $file 2>&1 | Out-Host
        }
        if ($LASTEXITCODE -ne 0) { throw "No se pudo firmar: $file" }
    }

    # Verificar la firma. Para un cert self-signed la cadena termina en un root
    # no confiado (si no se instala en Trusted Root), por lo que signtool verify
    # devuelve exit 1 aun con firma correcta; se valida por Get-AuthenticodeSignature.
    $sig = Get-AuthenticodeSignature $file
    if ($sig.Status -eq "NotSigned") { throw "Archivo sin firmar: $file" }
    if (-not $sig.SignerCertificate) { throw "Sin certificado de firma: $file" }
    $state = if ($sig.TimeStamperCertificate) { "+ timestamp" } else { "SIN timestamp" }
    Write-Host "OK  firma=$($sig.Status) signer=$($sig.SignerCertificate.Subject) $state"
    if ($sig.Status -ne "Valid") {
        Write-Host "    Nota: $($sig.StatusMessage) (normal para self-signed si el root no esta en Trusted Root)"
    }
}

Write-Host "Firmado completado."
