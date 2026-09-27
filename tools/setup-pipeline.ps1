param(
    [Parameter(Mandatory=$true)][string]$Profile,
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^arn:aws:(codeconnections|codestar-connections):ap-southeast-1:492213594399:connection/.+$')]
    [string]$ConnectionArn,
    [string]$DnsSecretArn = 'arn:aws:secretsmanager:ap-southeast-5:492213594399:secret:aliyundns-VvNFXf'
)
$ErrorActionPreference = 'Stop'
$env:AWS_PAGER = ''
$project = Split-Path $PSScriptRoot -Parent
function Run-Aws {
    param([string[]]$Arguments)
    & aws @Arguments --profile $Profile --no-cli-pager
    if ($LASTEXITCODE -ne 0) { throw ('AWS command failed: ' + ($Arguments[0..1] -join ' ')) }
}
$identity = & aws sts get-caller-identity --profile $Profile --output json --no-cli-pager
if ($LASTEXITCODE -ne 0) { throw 'AWS profile authentication failed.' }
if (($identity | ConvertFrom-Json).Account -ne '492213594399') { throw 'Wrong AWS account.' }
$connection = & aws codeconnections get-connection --connection-arn $ConnectionArn --region ap-southeast-1 --profile $Profile --output json --no-cli-pager
if ($LASTEXITCODE -ne 0) { throw 'Cannot read GitHub connection.' }
if (($connection | ConvertFrom-Json).Connection.ConnectionStatus -ne 'AVAILABLE') { throw 'Finish GitHub authorization in AWS console first.' }
Push-Location $project
try {
    Run-Aws @('cloudformation','deploy','--template-file','ci/packages.template.json','--stack-name','vasa-app-packages','--region','ap-southeast-5','--no-fail-on-empty-changeset')
    Run-Aws @('cloudformation','deploy','--template-file','ci/pipeline.template.json','--stack-name','vasa-app-pipeline','--region','ap-southeast-1','--capabilities','CAPABILITY_IAM','--parameter-overrides',"ConnectionArn=$ConnectionArn","DnsSecretArn=$DnsSecretArn",'--no-fail-on-empty-changeset')
    Run-Aws @('cloudformation','describe-stacks','--stack-name','vasa-app-pipeline','--region','ap-southeast-1','--query','Stacks[0].Outputs','--output','table')
} finally { Pop-Location }
Write-Host 'Pipeline created. Do not approve deployment until Lambda quota and the failed application stack have been resolved.'
