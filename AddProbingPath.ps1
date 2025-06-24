param (
    [string]$ConfigPath
)

if (-Not (Test-Path $ConfigPath)) {
    Write-Host "Config file not found: $ConfigPath"
    exit 1
}

[xml]$xml = Get-Content $ConfigPath

$runtime = $xml.configuration.runtime
if (-not $runtime) {
    $runtime = $xml.CreateElement("runtime")
    $xml.configuration.AppendChild($runtime) | Out-Null
}

$assemblyBinding = $runtime.assemblyBinding
if (-not $assemblyBinding) {
    $assemblyBinding = $xml.CreateElement("assemblyBinding", "urn:schemas-microsoft-com:asm.v1")
    $runtime.AppendChild($assemblyBinding) | Out-Null
}

$probing = $assemblyBinding.probing
if (-not $probing) {
    $probing = $xml.CreateElement("probing", "urn:schemas-microsoft-com:asm.v1")
    $probing.SetAttribute("privatePath", "MPRISBee")
    $assemblyBinding.AppendChild($probing) | Out-Null
} elseif (-not $probing.GetAttribute("privatePath").Split(';') -contains "MPRISBee") {
    $currentPath = $probing.GetAttribute("privatePath")
    $probing.SetAttribute("privatePath", "$currentPath;MPRISBee")
}

$xml.Save($ConfigPath)
Write-Host "Probing path ensured in config."