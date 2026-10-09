# Candidate company boards — 2026-10-09

Follow-up aggregate recheck and audit: [multi-company-integration-audit.md](multi-company-integration-audit.md).
The observations below are the original discovery sample; the follow-up separately records complete Lever pagination and refreshed counts without asserting active India vacancies or publication rights.

This is a discovery inventory, **not a seed file, licence, partnership claim, or list of enabled sources**.
Every entry has `IsActive=false` / publication rights `NOT ESTABLISHED`. No discovered source was created in a database.
Identifiers below were read from official careers-page links/embedded board parameters, not generated from company names.
Case is intentional, particularly for Ashby. Fivetran's official page publishes `job_board=fivetran`; G2 publishes `embed/job_board?for=g2crowd`.

Aggregate-only public API observations did not store/import descriptions or application data. Numbers are published global postings as observed, not guaranteed current open seats, India eligibility, or cross-board unique vacancies. Lever's Palantir observation is deliberately capped at its first 100 results. An HTTP error is not proof that a company has no vacancies.

| Company | Official careers evidence | Provider / exact identifier | Observed published postings | Rights/brand review reference (permission still required) |
| --- | --- | --- | ---: | --- |
| Razorpay | https://razorpay.com/careers/ | Greenhouse / `razorpaysoftwareprivatelimited` | 27 | https://razorpay.com/terms/rsl |
| Fivetran | https://www.fivetran.com/careers | Greenhouse / `fivetran` | 197 | https://www.fivetran.com/legal |
| Vercel | https://vercel.com/careers | Greenhouse / `vercel` | 87 | Legal review outstanding; official careers evidence does not grant redistribution |
| Sentry | https://sentry.io/careers/ | Ashby / `sentry` | 43 | https://sentry.io/terms/ (legal page inaccessible during review) |
| Cohere | https://cohere.com/careers | Ashby / `cohere` | 120 | https://cohere.com/terms-of-use — restrictions on copying/publication; obtain explicit permission |
| Mistral | https://mistral.ai/careers | Ashby / `mistral` | HTTP error; not measured | https://legal.mistral.ai/terms |
| Fireworks AI | https://fireworks.ai/careers | Ashby / `fireworks` | 83 | No licence established from careers page; legal review outstanding |
| AssemblyAI | https://www.assemblyai.com/careers | Greenhouse / `assemblyai` | 9 | No licence established from careers page; legal review outstanding |
| Deepgram | https://deepgram.com/careers | Ashby / `Deepgram` | 92 | https://deepgram.com/terms |
| Baseten | https://www.baseten.co/careers/ | Ashby / `baseten` | 111 | https://www.baseten.co/terms-and-conditions/ |
| Abridge | https://www.abridge.com/careers | Ashby / `Abridge` | 47 | No licence established from careers page; legal review outstanding |
| Anyscale | https://www.anyscale.com/careers | Ashby / `anyscale` | 20 | https://www.anyscale.com/terms |
| Figma | https://www.figma.com/careers/ | Greenhouse / `figma` | 151 | https://www.figma.com/legal/ ; https://www.figma.com/using-the-figma-brand/ |
| Notion | https://www.notion.com/careers | Ashby / `notion` | 133 | No licence established from careers page; legal review outstanding |
| Linear | https://linear.app/careers | Ashby / `Linear` | 31 | https://linear.app/terms ; https://linear.app/brand |
| Mercury | https://mercury.com/jobs | Greenhouse / `mercury` | 64 | https://mercury.com/legal |
| Render | https://render.com/careers | Ashby / `render` | 38 | https://render.com/terms |
| PlanetScale | https://planetscale.com/careers | Greenhouse / `planetscale` | 13 | https://planetscale.com/brand (asset availability is not redistribution permission) |
| Supabase | https://supabase.com/careers | Ashby / `supabase` | 52 | https://supabase.com/legal ; https://supabase.com/brand-assets |
| Sourcegraph | https://sourcegraph.com/careers | Greenhouse / `sourcegraph91` | 11 | https://sourcegraph.com/terms |
| Palantir | https://www.palantir.com/careers/ | Lever / `palantir` | >=100; first page only | No licence established from careers page; legal review outstanding |
| Nium | https://www.nium.com/careers | Lever / `nium` | 17; first page | https://www.nium.com/legal/terms-of-use |
| Tide | https://www.tide.co/careers/ | Greenhouse / `tide` | 93 | https://www.tide.co/terms/ |
| CloudZero | https://www.cloudzero.com/careers/ | Ashby / `CloudZero` | 17 | https://www.cloudzero.com/terms-of-use/ — express authorization required for republication/logo use |
| G2 | https://company.g2.com/careers | Greenhouse / `g2crowd` | HTTP error; not measured | https://legal.g2.com/ ; https://company.g2.com/brand-resources |
| LangChain | https://www.langchain.com/careers | Ashby / `langchain` | 100 | https://www.langchain.com/terms-of-service ; https://www.langchain.com/brand-assets |
| Unstructured | https://unstructured.io/careers | Ashby / `unstructured` | 5 | https://unstructured.io/terms-and-conditions |
| Runpod | https://www.runpod.io/careers | Ashby / `runpod` | 29 | https://www.runpod.io/brandkit — separate posting/asset rights review required |
| Exa | https://exa.ai/careers | Ashby / `exa` | 58 | https://exa.ai/brand ; official careers footer links terms PDF |
| Cartesia | https://cartesia.ai/careers | Ashby / `cartesia` | 30 | https://cartesia.ai/legal/terms |
| Inngest | https://www.inngest.com/careers | Ashby / `inngest` | 1 | https://www.inngest.com/terms |
| Trigger.dev | https://www.trigger.dev/careers | Ashby / `triggerdev` | 6 | https://www.trigger.dev/legal ; https://www.trigger.dev/brand |

## Readiness and remaining research

- 32 real candidate identifiers; 30 feeds returned measurable results; two HTTP failures were not bypassed.
- Observed aggregate lower bound: **1,785 global published postings**, including 100 from Palantir's capped page. Do not sum this into a unique India-active counter.
- Source-by-source publication and logo licences are **not established**. Linked legal/brand pages identify review work, not approvals. Full legal review and employer/content-owner authorization remain outstanding; no permissive licence has been asserted.
- Prioritize Indian postings in Razorpay, Tide, Nium and Fivetran during authorized evaluation. Only Razorpay's observed board explicitly demonstrated Indian city listings during page inspection. No board-wide remote/India eligibility was assumed for the other companies.
- India eligibility must be determined per posting by the runtime policy; generic `Remote` does not qualify. Most globally located boards may contribute few or zero Indian postings.
- No approved automatic logo asset was discovered/installed. Brand kits are candidate evidence for review only. Missing logos are allowed.
- No new real jobs imported/published: **0**. Gap to 10,000 newly verified imported active vacancies: **10,000**. Existing CareerHarbor production counts were not accessed, so the total-platform gap is unknown.
- **Capgemini remains disabled**: https://www.capgemini.com/terms-of-use/ section 6 requires prior written authorization for uses beyond personal/non-commercial copying. No authorization obtained; no adapter/source activation here.
- Existing SuccessFactors/Deloitte compatibility remains in code. This release deliberately has no grandfathered rights exemptions: configure reviewed source approvals before rollout, including any existing Deloitte source. It does not change production configuration or source records itself.

## Provider documentation (technical access is not a republication licence)

- Greenhouse: https://docs.greenhouse.io/job-board.html — public list plus `meta.total`; content/detail fields and official IDs.
- Lever: https://github.com/lever/postings-api — public postings, `skip`/`limit`, global/EU endpoints, stable posting ID and apply URL.
- Ashby: https://developers.ashbyhq.com/docs/public-job-posting-api — published/listed postings, structured locations, official job/apply URLs; intended for an organization's careers page.
- Ashby partner-feed program: https://developers.ashbyhq.com/docs/dedicated-partner-job-feeds — explore formal partner authorization rather than treating public access as a commercial grant.
