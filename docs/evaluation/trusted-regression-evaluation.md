# Trusted Regression Evaluation

## Current boundary

`.github/workflows/ci.yml` is ordinary repository CI. It runs the existing
Python tests and .NET build/test suite; it does not compare base and candidate
agent revisions or invoke the persisted acceptance service. Its workflow
permissions are limited to `contents: read`, its checkout does not retain the
token in the working tree, and it does not declare secrets. These are repository
workflow properties, not proof that a status check is required or that a
candidate cannot change the workflow.

The workflow and this document are stored in the repository being tested.
Consequently, a pull request can propose changes to both. Pinning the actions
used by ordinary CI makes those action implementations immutable for this
workflow revision; it does not make the workflow, its commands, evaluator, or
policy independent of candidate changes.

No trusted regression runner is implemented in this repository. No GitHub
branch-protection or ruleset settings were verified. Do not treat a green
ordinary CI run, a local reusable workflow, or a candidate-authored workflow
named "trusted" as proof of acceptance or as a release barrier.

## Trust prerequisite and ownership

Before regression evaluation can be enforced, an owner must establish a
separately controlled workflow and runner, for example in a dedicated
evaluation repository. Its maintainers must control the workflow, harness,
acceptance verifier, policy loading, and status reporting. The caller must
reference the trusted workflow at a full immutable commit SHA; a movable branch
or tag is not a pin. Updates to that SHA must be reviewed as changes to the
trusted computing base.

This repository alone does not establish that boundary. In particular, a
reusable workflow in this repository is still candidate-controlled when the
candidate can change its source. A protected-ref reusable workflow can be
trusted only when the caller and provider refs, permissions, and repository
protection rules actually enforce that trust model. Those conditions require
administrator verification and are not asserted here. No external trusted
repository or runner is configured by this change.

The trusted evaluator should run with read-only access to the application
source and no production credentials, live LLM credentials, or operational
integration credentials. Candidate code must not receive write-capable
repository credentials, acceptance-policy configuration, or control over
trusted status reporting. The disposable evaluation database and test
configuration must be isolated from developer and production data.

Untrusted pull requests must not receive secrets. Ordinary CI currently
declares no secrets and specifies only `contents: read`; administrator
settings still determine repository-level defaults, fork behavior, and whether
checks can be bypassed. Do not use a privileged `pull_request_target` workflow
to check out and execute candidate code. A trusted runner should obtain the
candidate source as data and execute it in an isolated, unprivileged job, not
grant the candidate the runner's credentials.

## Future runner contract

This contract describes required inputs and outputs for a later trusted runner;
it is not implemented or validated by the ordinary CI workflow.

Required inputs:

| Input | Required validation |
|---|---|
| Baseline revision | Exact full 40-character hexadecimal Git commit SHA; resolve and verify the fetched commit. |
| Candidate revision | Exact full 40-character hexadecimal Git commit SHA; resolve and verify the fetched commit. |
| Evaluation dataset | Reviewed dataset identity, version, and content digest. |
| Knowledge corpus | Reviewed corpus identity, version, and content digest. |
| Acceptance policy | Trusted server-controlled policy identity and version; policy values must not come from candidate code or the request payload. |

Reject missing, malformed, unresolved, or mismatched inputs before running
either revision. Evaluate the exact baseline and candidate SHAs independently,
with separate worktrees/checkouts, build outputs, Python environments, and
service ports. Use the same verified dataset bytes, populated knowledge
corpus, dependencies/runtime contract, and evaluation settings for both.
Record source revision provenance separately from the existing model, prompt,
configuration, and dataset compatibility metadata. Do not weaken the existing
acceptance compatibility checks to fit revision differences.

The trusted harness must persist both runs and the acceptance decision through
the existing .NET evaluation architecture, then read the persisted decision
back. Verify that the persisted decision identifies the exact baseline and
candidate run IDs and has status `Passed`. Missing, incomplete, failed,
incompatible, unpersisted, or unreadable results, any `Failed` or `Blocked`
status, and unexpected errors must result in a nonzero check. An HTTP success
status alone is not evidence of acceptance.

The future auditable, non-secret result artifact should include:

- Exact baseline and candidate SHAs.
- Dataset identity, version, and digest.
- Knowledge-corpus identity, version, and digest.
- Baseline and candidate persisted run IDs.
- Acceptance policy identity/version and persisted decision ID/status.
- Relevant .NET, Python, dependency, and runner versions.
- The final check outcome and any failure category.

Do not include credentials, tokens, or unnecessary ticket/incident data in
artifacts. The trusted workflow—not candidate-produced output—must decide the
check conclusion. Missing or malformed inputs and any result other than a
verified persisted `Passed` decision must fail closed.

## GitHub enforcement prerequisites

Repository code cannot configure GitHub branch protection or rulesets. A
repository administrator must:

1. Protect the target branch and restrict who can change trusted workflow,
   harness, policy, and dataset ownership/configuration.
2. Configure the trusted runner's status check as required for the intended
   merge or release path, and prevent applicable bypasses.
3. Ensure the status check is emitted by the trusted workflow and cannot be
   satisfied by an identically named candidate-controlled job.
4. Review fork and same-repository pull-request token permissions, workflow
   approval settings, and any trusted-repository access controls.
5. Protect policy and fixture updates with the agreed independent review
   process before enforcement is enabled.

No branch-protection, ruleset, required-check, CODEOWNERS, or external runner
configuration is verified by repository code. Until these administrator-owned
controls and the trusted runner exist, this remains a documented future
contract; acceptance enforcement must remain disabled.

## Remaining application-repository work

The application repository now includes a versioned Phase 15 evaluation
knowledge corpus with deterministic identifiers, validation, canonical
content hashing, and corpus provenance in the retrieval runner. These fixture
and unit-test results do not establish end-to-end retrieval or full-agent
behavior. A read-only check on 2026-10-29 found that the existing PostgreSQL
container had no knowledge documents or chunks, so no corpus was seeded there.
The opt-in PostgreSQL integration test requires a separately configured host,
port, and `aios_phase15_test` database, and was not run in that environment.
Its evaluation-run store is in-memory; repeated calls compare retrieved
document/chunk IDs, similarity values, and returned corpus provenance for the
same runner build. This does not verify PostgreSQL run persistence or
reproducibility across distinct source revisions.

The deterministic full-agent HTTP path is now covered by a focused test that
starts the Python FastAPI service, exercises `/api/v1/agent/runs` through
`PythonAgentGateway` and `EvaluationRunner`, and records two cases through an
`IEvaluationStore` test implementation. It checks resolved and proposal
responses, run metadata, and metrics. The .NET knowledge-search dependency is
stubbed locally; no external tools or live LLM credentials are used. This does
not verify the real .NET knowledge API, PostgreSQL evaluation-run persistence,
source-revision provenance, or baseline/candidate comparison. HTTP errors,
invalid gateway responses, and service failures are also covered as failed
evaluation outcomes rather than successes.

The next behavior-verification step is to run the full-agent test against
`PostgresEvaluationStore` using a separately configured isolated disposable
`aios_phase15_test` database and transaction rollback. No isolated PostgreSQL
evaluation connection was configured for this batch, so neither PostgreSQL
evaluation-run persistence nor PostgreSQL acceptance persistence is verified.
The transaction-scoped retrieval test remains a separate opt-in check. Do not
add acceptance thresholds or claim CI/release acceptance as part of this work.
Trusted-runner integration, independent policy ownership, and required-check
configuration remain unimplemented and require approval of the independent
trust boundary.
