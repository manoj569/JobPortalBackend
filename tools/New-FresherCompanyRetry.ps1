#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$inputFile = (Resolve-Path -LiteralPath $PayloadPath).Path
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if ($inputFile -eq $outputFile) { throw 'Output must differ from the retained payload.' }
if (Test-Path -LiteralPath $outputFile) { throw 'Output already exists; inspect it instead of overwriting.' }
$manifestFile = $outputFile + '.manifest.json'
if (Test-Path -LiteralPath $manifestFile) { throw 'Manifest already exists.' }
$bytes = [IO.File]::ReadAllBytes($inputFile)
$originalSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
$request = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable -DateKind String
if ($request.sourceType -ne 'serpapi-google-jobs' -or -not $request.importId -or $request.jobs.Count -lt 1) {
    throw 'Expected a retained SerpApi bulk-import request.'
}
$parentId = $request.importId
if ($parentId -cnotmatch '^ch-v1-[0-9a-f]{64}$') { throw 'Use the original retained ch-v1 payload, not a recovery payload.' }
$identityText = "company-resolution-v1`n$parentId`n$originalSha"
$retryId = 'ch-cr1-' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identityText))).ToLowerInvariant()
$jobsBefore = ConvertTo-Json -InputObject $request.jobs -Depth 100 -Compress
$request.importId = $retryId
$json = ConvertTo-Json -InputObject $request -Depth 100 -Compress
$roundTrip = $json | ConvertFrom-Json -AsHashtable -DateKind String
if ((ConvertTo-Json -InputObject $roundTrip.jobs -Depth 100 -Compress) -cne $jobsBefore) { throw 'Candidate preservation check failed.' }
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($outputFile, $json, $utf8)
$manifest = [ordered]@{
    purpose = 'company-resolution-v1'; parentImportId = $parentId; originalPayloadSha256 = $originalSha
    retryImportId = $retryId; jobCount = $request.jobs.Count
    retryPayloadSha256 = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
    externalIdsPreserved = $true
}
[IO.File]::WriteAllText($manifestFile, (ConvertTo-Json -InputObject $manifest), $utf8)
if ((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $originalSha) { throw 'Original payload changed during generation.' }
$manifest
# This script does not open SQLite, send HTTP, update receipts or run discovery.
