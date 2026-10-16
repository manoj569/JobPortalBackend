# Backend regression results

Full test receipt: 2,908 passed; 84 failed; 61 skipped; 3,053 total. No aborted test counters. The local test host stalled at shutdown and was stopped after approximately six minutes; the Blame collector reported all tests finished and no sequence file was needed. This is a failing suite, not a clean regression result. Normal test compilation requires resolving unrelated analyzer errors; this execution used the previously built test assembly with analyzers disabled during its build.

Failure counts by class (unchanged source/test modules; no isolated HEAD baseline rerun):

| Class | Failed |
|---|---:|
| CareerAnalyticsTests | 5 |
| CareerFinanceTests | 3 |
| CareerReservationExpiryTests | 3 |
| CareerSchedulingMigrationTests | 1 |
| CareerSessionReviewTests | 11 |
| CareerSessionTests | 29 |
| CareerTrustTests | 29 |
| ExternalJobEnrichmentTests | 1 |
| NotificationEligibilityTests | 1 |
| NotificationOutboxTests | 1 |

Provider/regression name-pattern checks (overlapping groups, not additive):

| Test names containing | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Talentd | 26 | 0 | 2 |
| FinalWebsitePolicy | 20 | 0 | 0 |
| FresherAcquisitionEligibility | 35 | 0 | 0 |
| AcquisitionPublicationEligibility | 22 | 0 | 0 |
| JobAutoPublish | 7 | 0 | 0 |
| Greenhouse | 97 | 0 | 0 |
| Lever | 44 | 0 | 0 |
| Ashby | 41 | 0 | 0 |
| Workday | 241 | 0 | 17 |
| SuccessFactors | 102 | 0 | 0 |
| SerpApi | 1 | 0 | 0 |
| Referral | 139 | 0 | 6 |
| CandidateJob | 0 | 0 | 1 |
| AdminJob | 8 | 0 | 0 |

Failed tests:

- JobPortal.Application.Tests.CareerAnalyticsTests.FinalNoShowsAreCountedWithoutPrivateSessionContent(consultantNoShow: False)
- JobPortal.Application.Tests.CareerAnalyticsTests.FinalNoShowsAreCountedWithoutPrivateSessionContent(consultantNoShow: True)
- JobPortal.Application.Tests.CareerAnalyticsTests.PendingAndHeldAreDisjointAndNullAvailabilityAloneIsNotADisputeHold
- JobPortal.Application.Tests.CareerAnalyticsTests.RatingsFollowModerationAndWithdrawalEligibility
- JobPortal.Application.Tests.CareerAnalyticsTests.UpcomingSessionIsNotAnUnprovisionedBookingAndIsIndependentOfCohort
- JobPortal.Application.Tests.CareerFinanceTests.LegacyCaptureConfirmsBookingOnceAndPreservesFinancialSnapshots
- JobPortal.Application.Tests.CareerFinanceTests.LegacyWebhookAndCallbackOrderingConvergesWithoutRepeatingCapture(webhookFirst: False)
- JobPortal.Application.Tests.CareerFinanceTests.LegacyWebhookAndCallbackOrderingConvergesWithoutRepeatingCapture(webhookFirst: True)
- JobPortal.Application.Tests.CareerReservationExpiryTests.RecordedCaptureCannotExpireEvenIfBookingIsPending(pending: False)
- JobPortal.Application.Tests.CareerReservationExpiryTests.RecordedCaptureCannotExpireEvenIfBookingIsPending(pending: True)
- JobPortal.Application.Tests.CareerReservationExpiryTests.StaleExpiryCannotOverwriteCommittedCapture
- JobPortal.Application.Tests.CareerSchedulingMigrationTests.PostgreSqlModelKeepsRevisionAndExpectedRelationshipsWithoutShadowForeignKeys
- JobPortal.Application.Tests.CareerSessionReviewTests.CompetingReminderBatchesCannotCreateDuplicateInboxRecords
- JobPortal.Application.Tests.CareerSessionReviewTests.ProviderMeetingIdentifierCannotLeakAJoinUrlThroughMetadata
- JobPortal.Application.Tests.CareerSessionReviewTests.ProviderRefreshWithoutHostLinkClearsOldHostCredential
- JobPortal.Application.Tests.CareerSessionReviewTests.ProviderResultAfterConcurrentCancellationCannotResurrectSession
- JobPortal.Application.Tests.CareerSessionReviewTests.SoftDeletedDependenciesDoNotStrandPendingReminders(deleted: "candidate")
- JobPortal.Application.Tests.CareerSessionReviewTests.SoftDeletedDependenciesDoNotStrandPendingReminders(deleted: "consultant")
- JobPortal.Application.Tests.CareerSessionReviewTests.SoftDeletedDependenciesDoNotStrandPendingReminders(deleted: "session")
- JobPortal.Application.Tests.CareerSessionReviewTests.StaleCompletionCannotOverrideRefundOrConfirmedNoShow(refund: False)
- JobPortal.Application.Tests.CareerSessionReviewTests.StaleCompletionCannotOverrideRefundOrConfirmedNoShow(refund: True)
- JobPortal.Application.Tests.CareerSessionReviewTests.SuspendedOrDeletedConsultantDoesNotBlockNoShowReportAndAdminReview(deleted: False)
- JobPortal.Application.Tests.CareerSessionReviewTests.SuspendedOrDeletedConsultantDoesNotBlockNoShowReportAndAdminReview(deleted: True)
- JobPortal.Application.Tests.CareerSessionTests.CancellationOrRefundDisablesSessionAndReminders(refund: False)
- JobPortal.Application.Tests.CareerSessionTests.CancellationOrRefundDisablesSessionAndReminders(refund: True)
- JobPortal.Application.Tests.CareerSessionTests.CandidateNoShowRequiresGraceAndIsTerminalWithoutRefundOrRelease
- JobPortal.Application.Tests.CareerSessionTests.CandidateReportHasNoFinancialEffectUntilAdminConfirms
- JobPortal.Application.Tests.CareerSessionTests.CompetingSessionRevisionsCannotBothCommit
- JobPortal.Application.Tests.CareerSessionTests.ExistingReminderOffsetsBridgeOnceForBothParticipants(offset: 10)
- JobPortal.Application.Tests.CareerSessionTests.ExistingReminderOffsetsBridgeOnceForBothParticipants(offset: 1440)
- JobPortal.Application.Tests.CareerSessionTests.ExistingReminderOffsetsBridgeOnceForBothParticipants(offset: 60)
- JobPortal.Application.Tests.CareerSessionTests.InProgressJoinUsesEndGraceButReadyLateStartIsRejected
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: -10, allowed: True)
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: -11, allowed: False)
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: 0, allowed: True)
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: 30, allowed: True)
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: 31, allowed: False)
- JobPortal.Application.Tests.CareerSessionTests.JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(minutes: 60, allowed: False)
- JobPortal.Application.Tests.CareerSessionTests.LegacyConsultantCompletionUpdatesBookingAndSchedulesPendingEarningWithoutPayout
- JobPortal.Application.Tests.CareerSessionTests.LinksAreEncryptedAndBoundToSessionAndRole
- JobPortal.Application.Tests.CareerSessionTests.ManualLinksRejectUnsafeHostsWithoutEchoingInput(url: "http://meet.google.com/test")
- JobPortal.Application.Tests.CareerSessionTests.ManualLinksRejectUnsafeHostsWithoutEchoingInput(url: "https://127.0.0.1/test")
- JobPortal.Application.Tests.CareerSessionTests.ManualLinksRejectUnsafeHostsWithoutEchoingInput(url: "https://meet.google.com.evil.test/token")
- JobPortal.Application.Tests.CareerSessionTests.ManualLinksRejectUnsafeHostsWithoutEchoingInput(url: "https://meet.google.com:444/test")
- JobPortal.Application.Tests.CareerSessionTests.ManualLinksRejectUnsafeHostsWithoutEchoingInput(url: "https://user:secret@meet.google.com/test")
- JobPortal.Application.Tests.CareerSessionTests.OwnershipAndAdminAuthorizationAreEnforcedInService
- JobPortal.Application.Tests.CareerSessionTests.PaidBookingCannotBypassSessionLifecycleThroughLegacyStatusEndpoint
- JobPortal.Application.Tests.CareerSessionTests.PendingRemindersDoNotSendForRefundReviewOrSuspendedConsultant
- JobPortal.Application.Tests.CareerSessionTests.ProvisioningIsIdempotentAndRemindersAreUniqueFutureOnly
- JobPortal.Application.Tests.CareerSessionTests.RefundAfterCompletionClearsEarningReleaseTimestamp
- JobPortal.Application.Tests.CareerSessionTests.RemindersDeliverOnceToExistingInboxWithoutMeetingSecrets
- JobPortal.Application.Tests.CareerSessionTests.UncertainProviderOutcomeIsSanitizedAndReconciledWithoutDuplicateCreate
- JobPortal.Application.Tests.CareerTrustTests.AdminTransitionsRejectStaleOrTerminalChangesAndKeepNotesPrivate
- JobPortal.Application.Tests.CareerTrustTests.CompetingAdminResolutionsAndStalePublicationCannotBothCommit
- JobPortal.Application.Tests.CareerTrustTests.CompletedPaidReviewIsPendingAndIdenticalReplayIsSafe
- JobPortal.Application.Tests.CareerTrustTests.CompletionDuringActiveDisputeCannotRestoreAvailability
- JobPortal.Application.Tests.CareerTrustTests.DecimalRatingsMatchDiscoveryDetailAndPagedPublicReviews
- JobPortal.Application.Tests.CareerTrustTests.DisputeHoldsEvenPastAvailabilityWithoutMovingMoneyAndResolvesWithNewDelay
- JobPortal.Application.Tests.CareerTrustTests.DisputeWindowHasExplicitBoundary(ticks: -1, permitted: True)
- JobPortal.Application.Tests.CareerTrustTests.DisputeWindowHasExplicitBoundary(ticks: 0, permitted: True)
- JobPortal.Application.Tests.CareerTrustTests.DisputeWindowHasExplicitBoundary(ticks: 1, permitted: False)
- JobPortal.Application.Tests.CareerTrustTests.EvidencePrivacyOwnershipAndReplayAreEnforced
- JobPortal.Application.Tests.CareerTrustTests.FullRefundHidesPublishedReviewAndCannotBeRepublished
- JobPortal.Application.Tests.CareerTrustTests.HtmlAndControlCharactersAreRejected(text: "<script>alert(1)</script>")
- JobPortal.Application.Tests.CareerTrustTests.HtmlAndControlCharactersAreRejected(text: "text\0control")
- JobPortal.Application.Tests.CareerTrustTests.InactiveCandidateOrRestrictedConsultantDoesNotRegainReleaseEligibility
- JobPortal.Application.Tests.CareerTrustTests.ModerationEditWithdrawalAndAggregateStayConsistent
- JobPortal.Application.Tests.CareerTrustTests.ModerationLockExcludesRatingsAndStopsCandidateEdits(status: Hidden)
- JobPortal.Application.Tests.CareerTrustTests.ModerationLockExcludesRatingsAndStopsCandidateEdits(status: Rejected)
- JobPortal.Application.Tests.CareerTrustTests.NoShowConfirmationAndNoRefundResolutionDoNotReleaseEarnings
- JobPortal.Application.Tests.CareerTrustTests.OwnershipAndTextLimitsApplyToDisputesAndEvidence
- JobPortal.Application.Tests.CareerTrustTests.RatingBoundsAreEnforced(rating: 0)
- JobPortal.Application.Tests.CareerTrustTests.RatingBoundsAreEnforced(rating: 6)
- JobPortal.Application.Tests.CareerTrustTests.RefundApprovalIsRecommendationAndUsesExistingRefundWorkflow
- JobPortal.Application.Tests.CareerTrustTests.RefundedDisputeResolutionNeverResurrectsEarning
- JobPortal.Application.Tests.CareerTrustTests.ReviewEditWindowAndStaleRevisionAreEnforced
- JobPortal.Application.Tests.CareerTrustTests.StaleCompletionAfterOpeningDisputeCannotReleaseEarning
- JobPortal.Application.Tests.CareerTrustTests.StaleResolutionAfterNoShowCannotRecreateEligibility
- JobPortal.Application.Tests.CareerTrustTests.StaleResolutionAfterRefundCannotRestoreRelease
- JobPortal.Application.Tests.CareerTrustTests.TrustRecordsCannotLoseIdentityOrEvidenceHistory
- JobPortal.Application.Tests.CareerTrustTests.UnpaidUnstartedAndWrongOwnerCannotReview
- JobPortal.Application.Tests.ExternalJobEnrichmentTests.BroadDepartmentsDoNotImplyTechnologyRoles(title: "Civil Engineer", department: "Infrastructure")
- JobPortal.Application.Tests.NotificationEligibilityTests.CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment
- JobPortal.Application.Tests.NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation
