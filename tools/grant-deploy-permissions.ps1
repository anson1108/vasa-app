param(
    [Parameter(Mandatory=$true)][string]$AdminProfile,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$accountId = '492213594399'
$userName = 'iamanson'
$policyNames = @('Ec2SwitchDeployCloudFormation','Ec2SwitchDeployResources','Ec2SwitchDeployRoles')
$env:AWS_PAGER = ''
function Invoke-AwsJson {
    param([string[]]$Arguments)
    $result = & aws @Arguments --profile $AdminProfile --output json --no-cli-pager
    if ($LASTEXITCODE -ne 0) { throw ('AWS command failed: ' + ($Arguments[0..1] -join ' ')) }
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}
foreach ($name in $policyNames) {
    $path = Join-Path $PSScriptRoot ($name + '.json')
    $policy = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $compact = $policy | ConvertTo-Json -Depth 30 -Compress
    if ($compact.Length -gt 6144) { throw "Policy too large: $name" }
    Write-Host "Policy: $path"
}
if (-not $Apply) {
    Write-Host "Preview only. Add -Apply to create and attach these three policies to $userName in account $accountId."
    return
}
$identity = Invoke-AwsJson @('sts','get-caller-identity')
if ($identity.Account -ne $accountId) { throw 'Wrong AWS account. No changes made.' }
Write-Host "Administrator identity: $($identity.Arn)"
$null = Invoke-AwsJson @('iam','get-user','--user-name',$userName)
Push-Location $PSScriptRoot
try {
    $existing = Invoke-AwsJson @('iam','list-policies','--scope','Local')
    foreach ($name in $policyNames) {
        $arn = "arn:aws:iam::${accountId}:policy/ec2-switch-deploy/$name"
        $found = @($existing.Policies | Where-Object { $_.Arn -eq $arn })
        if ($found.Count -gt 0) {
            # Never overwrite an existing policy: its contents may have been changed by an administrator.
            $version = Invoke-AwsJson @('iam','get-policy-version','--policy-arn',$arn,'--version-id',$found[0].DefaultVersionId)
            $expected = (Get-Content -LiteralPath "$name.json" -Raw | ConvertFrom-Json) | ConvertTo-Json -Depth 30 -Compress
            $actual = $version.PolicyVersion.Document | ConvertTo-Json -Depth 30 -Compress
            if ($actual -cne $expected) { throw "Existing policy differs: $arn. Ask the administrator to review it; this script will not overwrite it." }
        } else {
            $null = Invoke-AwsJson @('iam','create-policy','--policy-name',$name,'--path','/ec2-switch-deploy/','--description','Personal EC2 switch deployment permissions','--policy-document',"file://$name.json")
        }
        $null = Invoke-AwsJson @('iam','attach-user-policy','--user-name',$userName,'--policy-arn',$arn)
        Write-Host "Attached $name to $userName"
    }
} finally { Pop-Location }
Write-Host 'Complete. Retry tools/deploy.ps1 -Profile personal. IAM changes may take a short time to propagate.'
