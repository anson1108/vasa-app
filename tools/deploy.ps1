param(
    [Parameter(Mandatory=$true)][string]$Profile,
    [ValidatePattern('^[a-z][a-z0-9-]{2,40}$')][string]$StackName = 'personal-ec2-switch',
    [switch]$UseContainer
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    foreach ($tool in @('aws','sam','dotnet')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Install $tool first (see README)." }
    }
    & aws sts get-caller-identity --profile $Profile
    if ($LASTEXITCODE -ne 0) { throw 'AWS profile is not ready.' }
    & sam validate --lint --region ap-southeast-5 --profile $Profile
    if ($LASTEXITCODE -ne 0) { throw 'Template validation failed.' }
    if ($UseContainer) { & sam build --use-container } else { & sam build }
    if ($LASTEXITCODE -ne 0) { throw 'SAM build failed.' }
    & sam deploy --guided --stack-name $StackName --region ap-southeast-5 --profile $Profile --capabilities CAPABILITY_IAM
    if ($LASTEXITCODE -ne 0) { throw 'Deployment failed.' }
    & aws cloudformation describe-stacks --stack-name $StackName --region ap-southeast-5 --profile $Profile --query 'Stacks[0].Outputs' --output table
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read stack outputs.' }
} finally { Pop-Location }
