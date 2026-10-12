"""Run only against the named disposable Docker fixture. No application configuration is read."""
import json
import argparse
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--evidence', type=Path, default=ROOT.parent / 'classification-postgres-audit-final')
parser.add_argument('--maintenance-dll', type=Path, default=ROOT / 'JobPortal.Maintenance/bin/Release/net9.0/JobPortal.Maintenance.dll')
arguments = parser.parse_args()
EVIDENCE = arguments.evidence.resolve()
CONTAINER = 'careerharbor-classification-verify-20261012'
DATABASE = 'careerharbor_classification_cli_test'
APPLICATION = 'classification_interrupt_fixture'
CONNECTION = f'Host=127.0.0.1;Port=54761;Database={DATABASE};Username=postgres;Application Name={APPLICATION}'
DLL = arguments.maintenance_dll.resolve()

def sql(statement):
    result = subprocess.run(['docker', 'exec', '-i', CONTAINER, 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', DATABASE, '-At'],
                            input=statement, capture_output=True, text=True, check=True)
    return result.stdout.strip()

def command(path, apply=False, batches=10, user='postgres', schema=None):
    args = ['dotnet', str(DLL), 'job-classification-backfill', '--checkpoint', str(path), '--batch-size', '1', '--max-batches', str(batches)]
    if apply:
        args.append('--apply')
    env = dict(os.environ, ConnectionStrings__DefaultConnection=CONNECTION.replace('Username=postgres', 'Username=' + user))
    if schema:
        env['ConnectionStrings__DefaultConnection'] += ';Search Path=' + schema
    return args, env

def run(path, apply=False, batches=10, expected=0, user='postgres', schema=None):
    args, env = command(path, apply, batches, user, schema)
    result = subprocess.run(args, cwd=ROOT, env=env, capture_output=True, text=True)
    (EVIDENCE / (path.stem + '-last.log')).write_text(result.stdout + result.stderr)
    assert result.returncode in (0, 1) if expected is None else result.returncode == expected, (result.returncode, result.stdout, result.stderr)
    return json.loads(result.stdout) if expected in (0, None) else None

def pairs(path):
    entries = [json.loads(line) for line in Path(str(path) + '.audit.jsonl').read_text().splitlines()]
    intents = {e['AttemptId']: e for e in entries if e.get('Phase') == 'Intent'}
    outcomes = [e for e in entries if e.get('Phase') == 'Outcome']
    for outcome in outcomes:
        intent = intents[outcome['AttemptId']]
        assert intent['Id'] == outcome['Id']
        assert intent['Before'] == outcome['Before'] and intent['After'] == outcome['After']
        assert 'Evidence' in intent and 'ReviewFields' in intent
    return entries, intents, outcomes

assert sql('select current_database();') == DATABASE
assert sql('select count(*) from "Jobs";') == '8'
EVIDENCE.mkdir(exist_ok=True)
apply_path = EVIDENCE / 'interrupted-apply.json'
assert not apply_path.exists(), 'Use a fresh fixture/checkpoint, do not overwrite evidence.'
cold_starts = []
for attempt in range(5):
    cold = run(EVIDENCE / f'cold-start-{attempt}.json', batches=1)
    assert cold['AnalyzedJobs'] == 1 and cold['Failures'] == 0 and cold['RetryJobs'] == 0
    cold_starts.append(cold)
bounded = run(EVIDENCE / 'dry-run.json', batches=2)
assert bounded['AnalyzedJobs'] == 2 and bounded['RemainingJobs'] == 5 and bounded['AppliedCorrections'] == 0
dry_resume = run(EVIDENCE / 'dry-run.json')
assert dry_resume['RemainingJobs'] == 0 and dry_resume['AppliedCorrections'] == 0
assert sql('SELECT count(*) FROM "Jobs" j JOIN classification_backfill_baseline b ON b."Id"=j."Id" WHERE to_jsonb(j)<>b.original;') == '0'

# Delay the second row BEFORE its atomic update so the first committed checkpoint is observable.
sql("""CREATE FUNCTION classification_fixture_pause() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF NEW."ReferenceNumber"='CLASS-2' THEN PERFORM pg_sleep(45); END IF; RETURN NEW; END $$;
CREATE TRIGGER classification_fixture_pause BEFORE UPDATE ON "Jobs" FOR EACH ROW EXECUTE FUNCTION classification_fixture_pause();""")
args, env = command(apply_path, apply=True)
flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
with (EVIDENCE / 'interrupted-process.log').open('w') as output:
    process = subprocess.Popen(args, cwd=ROOT, env=env, stdout=output, stderr=output, creationflags=flags)
    try:
        deadline = time.monotonic() + 30
        paused = False
        while time.monotonic() < deadline:
            if apply_path.exists():
                progress = json.loads(apply_path.read_text())
                if progress['Cursor'] == '00000000-0000-0000-0000-000000000001' and sql(
                    f"SELECT count(*) FROM pg_stat_activity WHERE application_name='{APPLICATION}' AND wait_event='PgSleep';") == '1':
                    paused = True
                    break
            assert process.poll() is None, 'Backfill exited before interruption point.'
            time.sleep(0.1)
        assert paused, 'Controlled interruption point was not reached.'
        process.kill()
        process.wait(timeout=10)
        sql(f"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='{APPLICATION}' AND pid<>pg_backend_pid();")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=10)
        sql('DROP TRIGGER classification_fixture_pause ON "Jobs"; DROP FUNCTION classification_fixture_pause();')

assert sql('SELECT count(*) FROM "Jobs" WHERE "ClassificationJson" IS NOT NULL;') == '1'
assert json.loads(apply_path.read_text())['Analyzed'] == 1
entries_before, intents_before, outcomes_before = pairs(apply_path)
assert len(intents_before) == 2 and len(outcomes_before) == 1

for attempt in range(3):
    resume = run(apply_path, apply=True, expected=None)
    if resume['RetryJobs'] == 0:
        break
assert resume['RemainingJobs'] == 0 and resume['AppliedCorrections'] == 5 and resume['ProposedCorrections'] == 5
entries, intents, outcomes = pairs(apply_path)
assert len(outcomes) == 7 and len(intents) == 8  # one durable intent from the killed attempt
preserved = sql('''SELECT count(*) FROM "Jobs" j JOIN classification_backfill_baseline b ON b."Id"=j."Id"
WHERE (to_jsonb(j)-ARRAY['EmploymentType','WorkplaceType','MinimumExperienceYears','MaximumExperienceYears','MinimumSalary','MaximumSalary','CurrencyCode','SalaryPeriod','ClassificationJson'])
 <> (b.original-ARRAY['EmploymentType','WorkplaceType','MinimumExperienceYears','MaximumExperienceYears','MinimumSalary','MaximumSalary','CurrencyCode','SalaryPeriod','ClassificationJson']);''')
assert preserved == '0'
assert sql('SELECT count(*) FROM "Jobs" WHERE "ApplicationUrl"=\'https://example.test/duplicate\';') == '2'
assert sql('SELECT "MinimumExperienceYears" FROM "Jobs" WHERE "ReferenceNumber"=\'CLASS-3\';') == '2'
assert sql('SELECT count(*) FROM "Jobs" WHERE "ReferenceNumber"=\'CLASS-8\' AND "ClassificationJson" IS NULL;') == '1'
sql('CREATE TABLE classification_replay_baseline AS SELECT "Id",md5(row_to_json(j)::text) AS revision FROM "Jobs" j;')
replay = run(EVIDENCE / 'idempotent-apply.json', apply=True)
assert replay['AppliedCorrections'] == 0 and replay['ProposedCorrections'] == 0
assert sql('SELECT count(*) FROM "Jobs" j JOIN classification_replay_baseline b ON b."Id"=j."Id" WHERE md5(row_to_json(j)::text)<>b.revision;') == '0'
run(EVIDENCE / 'dry-run.json', apply=True, expected=1)  # dry-run/apply checkpoints cannot be interchanged
sql('CREATE SCHEMA classification_shadow; CREATE TABLE classification_shadow."Jobs" (LIKE public."Jobs" INCLUDING ALL);')
run(apply_path, apply=True, expected=1, schema='classification_shadow,public')
sql("DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='classification_readonly') THEN CREATE ROLE classification_readonly LOGIN; END IF; END $$; GRANT CONNECT ON DATABASE careerharbor_classification_cli_test TO classification_readonly; GRANT USAGE ON SCHEMA public TO classification_readonly; GRANT SELECT ON ALL TABLES IN SCHEMA public TO classification_readonly;")
readonly = run(EVIDENCE / 'readonly-dry-run.json', user='classification_readonly')
assert readonly['RemainingJobs'] == 0 and readonly['AppliedCorrections'] == 0
assert sql('SELECT count(*) FROM "Jobs" j JOIN classification_replay_baseline b ON b."Id"=j."Id" WHERE md5(row_to_json(j)::text)<>b.revision;') == '0'
summary = dict(interruption='PASS', first_committed_rows=1, unpaired_crash_intents=1, cold_starts=cold_starts,
               bounded_dry_run=bounded, dry_run_resume=dry_resume, resume=resume, replay=replay, read_only_role=readonly, nonclassification_changes=int(preserved),
               duplicate_records_retained=2, future_cutoff_excluded=True, mode_mismatch_rejected=True, schema_mismatch_rejected=True)
(EVIDENCE / 'cli-verification-summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary, indent=2))
