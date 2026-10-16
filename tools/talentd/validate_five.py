"""PREPARED ONLY - NOT EXECUTED. Operator-controlled five-job validation."""
import argparse
import json
import os
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker-root', type=Path, required=True)
    parser.add_argument('--ledger', type=Path, required=True)
    parser.add_argument('--execute', action='store_true', required=True)
    args = parser.parse_args()
    if not args.execute:
        parser.error('--execute is required')
    if not os.environ.get('CH_BACKEND_URL', '').startswith('https://') or len(os.environ.get('CH_BACKEND_TOKEN', '').encode()) < 32:
        parser.error('Protected CH_BACKEND_URL (HTTPS) and CH_BACKEND_TOKEN are required')
    ledger = args.ledger.resolve()
    if ledger.exists():
        parser.error('Chosen ledger already exists; never reset or replay it using this script')
    if not ledger.parent.is_dir():
        parser.error('Ledger parent must already exist')
    sys.path.insert(0, str(args.worker_root.resolve()))
    import httpx
    from worker.config import Settings
    from worker.state import State
    from worker.talentd import TalentdProvider
    from worker.talentd_import import batches, dispatch
    from worker.clients import Backend
    from worker.website_http import SafeWeb
    # Reserve with exclusive creation, including protection against racing operators.
    with ledger.open('xb'):
        pass
    settings = Settings(_env_file=None, mode='IMPORT', backend_url=os.environ['CH_BACKEND_URL'],
                        backend_token=os.environ['CH_BACKEND_TOKEN'], retries=0)
    state = State(ledger)
    try:
        with state.locked_run():
            web = SafeWeb(delay=2, max_bytes=2_000_000, max_requests=100)
            report = TalentdProvider(state, web).run(max_jobs=5)
            ledger.with_suffix('.discovery.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
            records = report['records']
            if report['errors'] or len(records) != 5:
                raise RuntimeError('Exactly five successful observations required; no submission performed')
            requests = list(batches(records, batch_size=5))
            if len(requests) != 1 or len(requests[0].jobs) != 5:
                raise RuntimeError('Exactly one five-job payload required; no submission performed')
            request = requests[0]
            # Persist exact bytes before network submission; the approved adapter reuses these.
            state.enqueue(request)
            payload = state.db.execute('SELECT payload FROM outbox WHERE id=?', (request.import_id,)).fetchone()[0]
            with ledger.with_suffix('.payload.json').open('x', encoding='utf-8') as snapshot:
                snapshot.write(payload)
            with httpx.Client(timeout=settings.timeout) as client:
                dispatch(state, Backend(settings, client), [], web.validate, approved=True, batch_size=5)
            print('Five-job request processed. Verify durable status and DB before any rollout.')
    finally:
        state.close()


if __name__ == '__main__':
    main()

