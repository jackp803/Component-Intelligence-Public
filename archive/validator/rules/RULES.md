# Archive Validator Rule Map

Validator rules enforce existing authority; they do not create new engineering policy.

| Rule ID | Severity | Meaning | Primary authority |
|---|---|---|---|
| `ARCHIVE-IDENTITY-001` | ERROR | Missing formal Manufacturer / Model / IDs | Archive Spec v2 §4 |
| `ARCHIVE-IDENTITY-002` | ERROR | Component identity conflicts with changeset target | Archive Spec v2 §4 |
| `ARCHIVE-IDENTITY-003` | ERROR | Changeset does not contain exactly one Component row for its target identity | Archive Spec v2 §4, §15 |
| `ARCHIVE-PORT-001` | ERROR | PortID or PortName is blank | Archive Spec v2 §6 |

| `ARCHIVE-OP-001` | ERROR | CREATE contradicts existing archive identity | Archive Spec v2 §4, §15 |
| `ARCHIVE-ID-001` | ERROR | Duplicate ComponentID | Archive Spec v2 §4 |
| `ARCHIVE-ID-002` | ERROR | Duplicate PortID | Archive Spec v2 §4 |
| `ARCHIVE-ID-003` | ERROR | Duplicate PinID | Archive Spec v2 §4, §13 |
| `ARCHIVE-OWNER-001` | ERROR | Port owner mismatch | Archive Spec v2 §6, §13 |
| `ARCHIVE-OWNER-002` | ERROR | Pin owner mismatch | Archive Spec v2 §7, §13 |
| `ARCHIVE-STABLE-ID-001..003` | ERROR | Stable engineering ID changed without migration | Archive Spec v2 §4 |
| `ARCHIVE-PIN-001` | ERROR | Known PinCount differs from physical Pin rows | Archive Spec v2 §8, §13 |
| `ARCHIVE-PIN-002` | ERROR | Duplicate contact identifier inside one Port | Archive Spec v2 §4, §8 |
| `ARCHIVE-PIN-003` | ERROR | PinID or PinNumber/contact identifier is blank | Archive Spec v2 §4, §7, §8 |
| `ARCHIVE-PIN-004` | ERROR | Declared ActualPinCount differs from archived Pin rows | Archive Spec v2 §8 |

| `ARCHIVE-EVIDENCE-001` | REVIEW | NC/Reserved lacks explicit evidence | Archive Spec v2 §7, §12, §14 |
| `ARCHIVE-PATH-001` | ERROR | Archive path is absolute/escaping/not under Documents | Archive Spec v2 §2 |
| `ARCHIVE-READY-001` | ERROR | Ready contradicts Pin completeness | Archive Spec v2 §13 |
| `ARCHIVE-READY-002` | REVIEW | Ready lacks endpoint-mode decision | Archive Spec v2 §9, §13 |
| `ARCHIVE-CONFLICT-001` | REVIEW | Source conflict is unresolved | Archive Spec v2 §3 |
| `ARCHIVE-UNKNOWN-001` | WARNING | Unknown engineering facts are explicitly recorded | Archive Spec v2 §5, §13 |
| `ARCHIVE-INPUT-001` | ERROR | Invalid/unmapped JSON or validator input/tool failure | Architecture v1 failure semantics |

## Severity

- `ERROR` → `REJECT`
- `REVIEW` without ERROR → `REVIEW_REQUIRED`
- `WARNING` / `INFO` alone → `PASS`

A future rule must cite authority and have tests before it can be added.
