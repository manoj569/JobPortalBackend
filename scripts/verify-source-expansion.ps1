param([string]$OutputPath = 'docs/company-source-expansion-additional-probes.json')
$ErrorActionPreference = 'Stop'
# Public read-only probes. No ingestion, credentials, retry, or database access.
$boards = @(
    @('Mastercard','mastercard.wd1.myworkdayjobs.com','mastercard','CorporateCareers'),
    @('Salesforce','salesforce.wd12.myworkdayjobs.com','salesforce','External_Career_Site'),
    @('Intel','intel.wd1.myworkdayjobs.com','intel','External'),
    @('HP','hp.wd5.myworkdayjobs.com','hp','ExternalCareerSite'),
    @('Walmart Global Tech','walmart.wd5.myworkdayjobs.com','walmart','WalmartExternal')
)
$results = foreach ($board in $boards) {
    $root = 'https://' + $board[1] + '/wday/cxs/' + $board[2] + '/' + $board[3]
    try {
        $listing = Invoke-RestMethod -Uri ($root + '/jobs') -Method Post -ContentType 'application/json' -Body '{"appliedFacets":{},"limit":5,"offset":0,"searchText":"India"}' -TimeoutSec 20
        $sample = @($listing.jobPostings)
        $item = $sample | Where-Object { $_.locationsText -match 'India|Bangalore|Bengaluru|Pune|Hyderabad|Chennai' } | Select-Object -First 1
        if (-not $item) { $item = $sample | Select-Object -First 1 }
        $detailUrl = $null
        $detail = $null
        if ($item) {
            if (-not $item.externalPath.StartsWith('/job/')) { throw 'Unexpected detail path; stopped.' }
            $detailUrl = $root + $item.externalPath
            Start-Sleep -Milliseconds 300
            $detail = Invoke-RestMethod -Uri $detailUrl -TimeoutSec 20
        }
        [pscustomobject]@{ company=$board[0]; verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); listing=($root+'/jobs'); searchText='India'; total=$listing.total; sample=$sample; detailUrl=$detailUrl; detail=$detail.jobPostingInfo; status='ok'; fullPaginationVerified=$false }
    } catch {
        [pscustomobject]@{ company=$board[0]; verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); listing=($root+'/jobs'); status=$_.Exception.Message; fullPaginationVerified=$false }
    }
    Start-Sleep -Milliseconds 300
}
$results | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $OutputPath
$results | Select-Object company,total,status | Format-Table
