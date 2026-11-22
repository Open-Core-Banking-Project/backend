# Performance evidence gate

`P1-E04-D16` approves the initial Products and Accounts acceptance profile. The source of truth is
`tests/performance/profiles/p1-e04-initial-launch.v1.json`; target values are not duplicated in application code or
ordinary deterministic tests.

Run the profile contract and its negative cases on any workstation:

```powershell
powershell -File scripts/performance-profile-gate.ps1 -SelfTest
```

This proves only that the versioned profile and gate are coherent. It does not generate load and makes no latency,
capacity, RTO or RPO claim.

The dedicated acceptance environment must produce a JSON result containing:

- the exact `profileId`;
- a stable environment identity and `workstation: false`;
- the exact approved active-account volume, using synthetic data only;
- one observation for every approved journey, concurrency, cold/warm mode and repetition;
- sample count, p50, p95, p99 and technical-error rate for every observation.

Evaluate that captured result separately:

```powershell
powershell -File scripts/performance-profile-gate.ps1 `
  -ResultsPath artifacts/p1-e04-performance-result.json
```

The command fails on an incomplete matrix, duplicate or unexpected observation, dataset/environment mismatch,
invalid percentile ordering, latency breach or error-rate breach. It never starts an API, generates synthetic records,
or modifies a database.

Recovery remains a separate claim. A four-hour RTO and zero loss of committed journals, idempotency outcomes and audit
records may be reported only after every prerequisite in the profile exists and a fresh-cluster drill measures detection
through verification. The local dump/restore drill is useful evidence, but it is not that proof.
