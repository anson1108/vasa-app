param(
    [Parameter(Mandatory=$true)][string]$Profile,
    [Parameter(Mandatory=$true)][string]$Table,
    [Parameter(Mandatory=$true)][ValidateSet('register','revoke')][string]$Action,
    [Parameter(Mandatory=$true)][string]$Device
)
$ErrorActionPreference = 'Stop'
# Export temporary credentials directly into this process, never to disk or terminal output.
$credentialNames = @('AWS_ACCESS_KEY_ID','AWS_SECRET_ACCESS_KEY','AWS_SESSION_TOKEN')
$previousCredentials = @{}
foreach ($name in $credentialNames) { $previousCredentials[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $credentialJson = & aws configure export-credentials --profile $Profile --format process
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve AWS profile. Run aws sso login first if applicable.' }
    $resolvedCredentials = $credentialJson | ConvertFrom-Json
    $env:AWS_ACCESS_KEY_ID = $resolvedCredentials.AccessKeyId
    $env:AWS_SECRET_ACCESS_KEY = $resolvedCredentials.SecretAccessKey
    $env:AWS_SESSION_TOKEN = $resolvedCredentials.SessionToken
    $adminProject = Join-Path (Split-Path $PSScriptRoot -Parent) 'backend/Ec2Switch.Admin'
    & dotnet run --project $adminProject -c Release -- $Action $Table $Device
    if ($LASTEXITCODE -ne 0) { throw 'Device administration failed.' }
} finally {
    foreach ($name in $credentialNames) { [Environment]::SetEnvironmentVariable($name, $previousCredentials[$name], 'Process') }
    $credentialJson = $null
    $resolvedCredentials = $null
}
