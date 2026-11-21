<#
Operator-only, explicit execution. Requires a deployed readiness API and an Administrator token.
Preview is the default. Use -Apply only after reviewing before/preview output.
No credentials are stored here; set CAREERHARBOR_ADMIN_TOKEN in the invoking process.
Example: .\scripts\bootstrap-accelerator-baseline-catalog.ps1 -BaseUri https://your-approved-api -TaxonomyId <uuid>
Then repeat with -Apply after review. No migrations or direct user/payment updates are performed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][uri]$BaseUri,
    [Parameter(Mandatory=$true)][guid]$TaxonomyId,
    [guid]$MarketPathId=[guid]::Empty,
    [switch]$Apply
)
$ErrorActionPreference='Stop'
if ($BaseUri.Scheme -ne 'https' -and -not ($BaseUri.IsLoopback -and $BaseUri.Scheme -eq 'http')) { throw 'HTTPS is required outside localhost.' }
if ($BaseUri.UserInfo -or $BaseUri.Query -or $BaseUri.Fragment) { throw 'Use an API base URI without embedded credentials, query or fragment.' }
if (-not $env:CAREERHARBOR_ADMIN_TOKEN) { throw 'Set CAREERHARBOR_ADMIN_TOKEN to an authorized Administrator token.' }
$catalogHeaders=@{ Authorization='Bearer '+$env:CAREERHARBOR_ADMIN_TOKEN }
$catalogApi=$BaseUri.AbsoluteUri.TrimEnd('/')+'/api/admin/accelerator/v1/readiness'
$catalogBefore=Invoke-RestMethod -Uri ($catalogApi+'/roles?code=full-stack-developer&taxonomyId='+$TaxonomyId) -Headers $catalogHeaders -TimeoutSec 120
$catalogMarketBeforeData=$null
if ($MarketPathId -ne [guid]::Empty) { $catalogMarketBeforeData=(Invoke-RestMethod -Uri ($catalogApi+'/market-paths/'+$MarketPathId) -Headers $catalogHeaders -TimeoutSec 120).data }
$catalogPreview=Invoke-RestMethod -Method Post -Uri ($catalogApi+'/catalog/bootstrap') -Headers $catalogHeaders -ContentType 'application/json' -Body (@{taxonomyId=$TaxonomyId;apply=$false}|ConvertTo-Json) -TimeoutSec 120
$catalogAppliedData=$null
if ($Apply) {
    $catalogApplied=Invoke-RestMethod -Method Post -Uri ($catalogApi+'/catalog/bootstrap') -Headers $catalogHeaders -ContentType 'application/json' -Body (@{taxonomyId=$TaxonomyId;apply=$true}|ConvertTo-Json) -TimeoutSec 120
    $catalogAppliedData=$catalogApplied.data
}
$catalogAfter=Invoke-RestMethod -Uri ($catalogApi+'/roles?code=full-stack-developer&taxonomyId='+$TaxonomyId) -Headers $catalogHeaders -TimeoutSec 120
$catalogLaunch=Invoke-RestMethod -Uri ($catalogApi+'/launch-roles?taxonomyId='+$TaxonomyId) -Headers $catalogHeaders -TimeoutSec 120
$catalogMarketData=$null
if ($MarketPathId -ne [guid]::Empty) { $catalogMarketData=(Invoke-RestMethod -Uri ($catalogApi+'/market-paths/'+$MarketPathId) -Headers $catalogHeaders -TimeoutSec 120).data }
[pscustomobject]@{ BeforeFullStack=$catalogBefore.data; BeforeMarketPath=$catalogMarketBeforeData; Preview=$catalogPreview.data; Applied=$catalogAppliedData; AfterFullStack=$catalogAfter.data; LaunchRoles=$catalogLaunch.data; MarketPath=$catalogMarketData } | ConvertTo-Json -Depth 30
