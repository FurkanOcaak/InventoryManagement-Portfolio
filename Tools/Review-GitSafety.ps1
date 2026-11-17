param([string]$Repository = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$candidates = @(git -C $Repository ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate Git candidates.' }
$patterns = [ordered]@{
    PrivateKey = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY'
    AccessToken = '(gh[pousr]_[A-Za-z0-9]{25,}|sk-[A-Za-z0-9]{25,}|AKIA[A-Z0-9]{16})'
    EmailReview = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
    CredentialReview = '(?i)(password|pwd)\s*[=:]'
    MachinePathReview = '(?i)C:[\\/]+Users[\\/]'
}
$findings = @(
    foreach ($file in $candidates) {
        if ($file -match '(^|/)(bin|obj|node_modules|artifacts|TestResults)/|\.(bak|trn|mdf|ldf|pfx|pem|key)$|(^|/)\.env($|\.)' -and $file -notmatch '\.env\.example$') {
            [pscustomobject]@{ File = $file; Line = 0; Category = 'ExcludedArtifactTracked' }
        }
        $path = Join-Path $Repository $file
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $lines = @(Get-Content -LiteralPath $path -Encoding UTF8)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($category in $patterns.Keys) {
                if ($lines[$i] -match $patterns[$category]) {
                    # Deliberately output no matching text, passwords, emails or keys.
                    [pscustomobject]@{ File = $file; Line = $i + 1; Category = $category }
                }
            }
        }
    }
)
$findings | ConvertTo-Json -Depth 3
if (@($findings | Where-Object { $_.Category -in @('PrivateKey', 'AccessToken', 'ExcludedArtifactTracked') }).Count) { exit 1 }
Write-Output 'Pattern scan complete. Review findings locally; this is not a complete secret-detection guarantee.'
