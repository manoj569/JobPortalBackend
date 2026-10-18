# Local PostgreSQL validation environment

This is an explicitly NON-PRODUCTION, task-owned Docker fixture. It does not read application/Neon connection settings or CH_BACKEND_URL/CH_BACKEND_TOKEN.

- Container: careerharbor-talentd-validation-20261016-01a1441f
- ID: 279d7704dd57d6241d0f4d866d65deae5157a1f9cc03a05eb25fba91867d1594
- Ownership label: codex.task=01a1441f-3ca1-78d2-879a-a9f15b4930be
- Image: cached postgres:17-alpine, PostgreSQL 17.11
- Image ID: sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73
- Binding: 127.0.0.1:54769 → container 5432; no host bind mounts
- Databases: careerharbor_generic_india_test; careerharbor_talentd_migration_test; careerharbor_website_talentd_test
- Authentication: trust confined to this loopback disposable fixture; no production credentials.

The installed Docker Desktop was started in the background. The container and all three databases were created new for this continuation. No unknown database/container/volume was deleted or reset. The migration test rolls back/re-applies only the Talentd source migration in its own local migration database.

Reproduce local validation without resetting any database:

```powershell
# If the owned container is stopped, start this exact known fixture only.
docker start careerharbor-talentd-validation-20261016-01a1441f
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:GENERIC_INDIA_TEST_POSTGRES = 'Host=127.0.0.1;Port=54769;Database=careerharbor_generic_india_test;Username=postgres'
$env:TALENTD_MIGRATION_TEST_POSTGRES = 'Host=127.0.0.1;Port=54769;Database=careerharbor_talentd_migration_test;Username=postgres'
$env:CAREERHARBOR_WEBSITE_TEST_POSTGRES = 'Host=127.0.0.1;Port=54769;Database=careerharbor_website_talentd_test;Username=postgres'
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false --filter 'FullyQualifiedName~Talentd|FullyQualifiedName~FinalWebsitePolicy|FullyQualifiedName~AcquisitionPublicationEligibility|FullyQualifiedName~FresherAcquisitionEligibility'
```

Run from the primary backend checkout. The analyzer command-line setting matches the existing verification workflow because unrelated test-file analyzer failures block the normal test build. No project analyzer setting or assertion was weakened. DOTNET_PROCESSOR_COUNT scopes the local test process's reported CPU count to reduce resource contention; no production runtime setting is changed. Other PostgreSQL integrations retain their own explicit opt-in variables and existing skips. Retained/historical-payload variables are deliberately never configured.

`build_contract_fixture.py` is an offline fixture generator, not a crawler/import command. `validate_five.py` and OPERATOR_VALIDATION.md remain PREPARED ONLY — NOT EXECUTED; do not confuse them with these local integration tests.

Final lifecycle: the verified owned container was stopped after all tests completed; databases/volume were retained. No unknown container or database was touched.
