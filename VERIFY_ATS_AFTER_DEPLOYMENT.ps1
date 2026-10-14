[CmdletBinding()]
param(
    [switch]$RunAirbusAndWalmart,
    [switch]$RunGreenhouse,
    [ValidateRange(1, 120)][int]$PollTimeoutMinutes = 45
)

$ErrorActionPreference = 'Stop'
$atsBaseUrl = 'https://job-portal-lgjc.onrender.com'
$atsSecureToken = Read-Host 'Paste an existing Administrator access token (input is hidden)' -AsSecureString
$atsHeaders = @{ Authorization = 'Bearer ' + [Net.NetworkCredential]::new('', $atsSecureToken).Password }
$atsSources = [ordered]@{
    Airbus = '18124fc8-73e2-4fc9-b35c-dc40d3bb6c00'
    Walmart = 'a2a780d1-0e99-43a5-9387-9a306dc0882d'
    JohnsonAndJohnson = '0f5ff6dd-3a73-4078-9505-a2102ceac51d'
    Stripe = '93354f36-b067-4427-a371-67772bbc5667'
    Airbnb = '1d86c370-4119-4629-a797-a81e0c8a6170'
}

try {
    foreach ($atsEntry in $atsSources.GetEnumerator()) {
        $atsSource = (Invoke-RestMethod -Method Get -Uri "$atsBaseUrl/api/admin/job-sources/$($atsEntry.Value)" -Headers $atsHeaders -TimeoutSec 30).data
        $atsSource | Select-Object id, atsType, careerPageUrl, atsIdentifier, isActive, autoPublishEnabled, scanIntervalMinutes | Format-List
    }

    $atsRunNames = @()
    if ($RunAirbusAndWalmart) { $atsRunNames += @('Airbus', 'Walmart') }
    if ($RunGreenhouse) { $atsRunNames += @('Stripe', 'Airbnb') }
    if ($atsRunNames.Count -gt 0) {
        foreach ($atsName in $atsRunNames) {
            $atsSourceId = $atsSources[$atsName]
            # Exactly one POST per source; do not retry an uncertain acceptance.
            try {
                $atsAccepted = (Invoke-RestMethod -Method Post -Uri "$atsBaseUrl/api/admin/job-sources/$atsSourceId/run" -Headers $atsHeaders -TimeoutSec 30).data
            }
            catch {
                $atsApiError = $null
                try { $atsApiError = ($_.ErrorDetails.Message | ConvertFrom-Json).error } catch { }
                [pscustomobject]@{ Company = $atsName; Status = 'AcceptanceFailedOrUnconfirmed'; ReasonCode = $atsApiError.code; Message = $atsApiError.message }
                continue
            }
            $atsRunId = [guid]$atsAccepted.runId
            $atsStatusUrl = "$atsBaseUrl/api/admin/job-sources/$atsSourceId/runs/$atsRunId"
            $atsDeadline = [DateTime]::UtcNow.AddMinutes($PollTimeoutMinutes)
            try {
                do {
                    $atsRun = (Invoke-RestMethod -Method Get -Uri $atsStatusUrl -Headers $atsHeaders -TimeoutSec 30).data
                    $atsPending = $atsRun.status -in @('Queued', 'Running', 'Paused') -or
                        ($atsRun.status -eq 'Interrupted' -and $atsRun.retryPending)
                    if ($atsPending -and [DateTime]::UtcNow -lt $atsDeadline) { Start-Sleep -Seconds 3 }
                } while ($atsPending -and [DateTime]::UtcNow -lt $atsDeadline)
            }
            catch {
                [pscustomobject]@{ Company = $atsName; RunId = $atsRunId; Status = 'MonitoringFailed'; Outcome = 'UNCONFIRMED'; StatusUrl = $atsStatusUrl }
                continue
            }
            [pscustomobject]@{
                Company = $atsName; RunId = $atsRun.runId; Status = $atsRun.status
                Outcome = if ($atsRun.status -eq 'Succeeded') { 'SUCCESS' } elseif ($atsPending) { 'MONITORING_TIMEOUT_OUTCOME_UNCONFIRMED' } else { 'FAILED_OR_PARTIAL' }
                ReasonCode = $atsRun.diagnostics.failureReasonCode; Stage = $atsRun.diagnostics.failureStage
                StopReason = $atsRun.diagnostics.stopReason; ReconciliationSafe = $atsRun.diagnostics.reconciliationSafe
                Fetched = $atsRun.totalReceived; Parsed = $atsRun.diagnostics.parsed; Created = $atsRun.created
                Existing = $atsRun.existing; Published = $atsRun.published; Rejected = $atsRun.rejected
                NotPublished = $atsRun.notPublished; SkippedInvalid = $atsRun.skippedInvalid
                Skipped = $atsRun.skipped; Failed = $atsRun.failed; StatusUrl = $atsStatusUrl
            } | Format-List
        }
    }
}
finally {
    $atsHeaders.Clear()
    $atsSecureToken.Dispose()
}
