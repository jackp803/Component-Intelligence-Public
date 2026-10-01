# Archive Change Policy

## 1. Policy changes

A semantic change to what counts as valid archive engineering data must:

1. be proposed in Git;
2. update the authoritative archive specification, or introduce a new version;
3. update `docs/archive/AUTHORITY.md` when the selected authority/version changes;
4. update deterministic validator rules when the changed rule is mechanically enforceable;
5. update affected evals;
6. include migration notes when existing central data may require review or repair.

A Skill edit is not a valid way to introduce new engineering policy.

## 2. Skill changes

`skills/component-intelligence-archive/SKILL.md` owns procedure only: triggering, authority resolution, research sequence, changeset/validation/write/readback flow, failure states, and reporting.

If a proposed Skill change changes the meaning of `NC`, `Direction`, readiness, identity, connector semantics, or any other engineering fact, change the archive specification first.

## 3. Validator changes

Every validator rule must have:

- a stable rule ID;
- a severity;
- a documented authority mapping;
- tests for rejection/review/pass behavior.

Validator code may enforce authority; it may not create undocumented engineering policy.

## 4. Authority promotion

`main` is the production discovery branch. Feature branches are proposals. A future AI must not need a historical feature-branch name to find the current production policy.

## 5. Backward compatibility

Stable ComponentID / PortID / PinID values are engineering identity. Policy or UI wording changes must not casually rewrite them. Any identity migration must be explicit, reviewable, and preserve traceability.

## 6. Public repository restrictions

Do not commit credentials, OAuth tokens, private company files, NDA/confidential documents, or secret archive access material. Public policy, schemas, validator code, sanitized fixtures, and public-source references are acceptable.
