"""Offline deterministic contract fixture; no crawler, backend client or ledger."""
from datetime import UTC, datetime
from pathlib import Path
import sys
sys.path.insert(0, sys.argv[1])
from worker.talentd_parse import wire
from worker.talentd_import import request_for
r = dict(identity='talentd:id:fixture-ABC123', public_id='fixture-ABC123', company='Talentd Contract Fixture',
 company_website='https://unfamiliar-employer.example', logo='https://assets.example/logo.png', title='Senior Engineer',
 description='Build distributed services. Responsibilities and qualifications are supplied separately.',
 location='London, UK', addresses=[dict(addressCountry='GB', addressLocality='London', addressRegion='England')],
 work_arrangement='hybrid', experience='5–8 years', minimum_experience=5, maximum_experience=8, fresher=False,
 employment_type=['FULL_TIME'], skills=[' C# ', 'c#', 'SQL'], degree='B.Tech', education='Engineering degree',
 salary_text='₹20K–30K a month', salary=dict(currency='INR', value=dict(minValue=20000, maxValue=30000, unitText='MONTH')),
 batch_years=[2025,2026], qualifications='Engineering degree required', responsibilities='<b>Build reliable services</b>',
 benefits='Health coverage', category='Engineering', categories=['Engineering'], tags=['Backend'],
 description_sections={'Eligibility':['Engineering graduates']}, structured={'eligibility':'Engineering graduates'},
 facts={'Degree':'B.Tech','Salary':'₹20K–30K a month'}, posted_at='2026-10-01T00:00:00Z', updated_at='2026-10-02T00:00:00Z',
 deadline='2036-10-31T00:00:00Z', discovered_url='https://www.talentd.in/jobs/example',
 source_url='https://www.talentd.in/jobs/example', apply_url='https://apply.workable.com/example/j/ABC123/', direct_apply=True)
request=request_for([wire(r, datetime(2026,10,16,tzinfo=UTC))])
out=Path('JobPortal.Application.Tests/Fixtures/Talentd/approved-worker-request.json')
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(request.model_dump_json(by_alias=True,indent=2),encoding='utf-8')
print('Offline approved-worker payload written:',out,'ImportId:',request.import_id)
