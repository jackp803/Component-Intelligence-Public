# AI Portability — START HERE

This is the bootstrap document for an AI/runtime that has no prior Component Intelligence chat context.

## Bootstrap sequence

1. Read `docs/archive/AI_PORTABILITY.md`.
2. Read `docs/archive/AUTHORITY.md`.
3. Load `skills/component-intelligence-archive/SKILL.md`.
4. Read the current authority documents selected by `AUTHORITY.md`.
5. Discover available capabilities using `skills/component-intelligence-archive/references/TOOL_CONTRACT.md`.
6. Use `ArchiveLookup` to read the current central state before proposing a mutation.
7. Perform evidence research/reasoning and build a structured changeset.
8. Run deterministic validation.
9. Write only if an authorized `ArchiveWrite` capability exists and validation permits it.
10. Read back the result, perform required sync, and report the real terminal state.

## Model independence

The reasoning engine is replaceable. ChatGPT, Codex, another hosted model, or a local model may perform the reasoning role. The model must not redefine authority because its training data, memory, or prior conversation differs.

A local model may require separate search, browser, PDF/table extraction, image/vision, Google Drive, and validation tools. Missing tool capability must be reported rather than simulated.

## No chat-history dependency

Historical chat history may explain why a rule was created, but it is never required to recover the current procedure or engineering policy. If chat history conflicts with `main` authority, GitHub authority wins.

## Safe degradation

A runtime with research + changeset + validation capability but no write tool may still return `VALIDATED_NOT_WRITTEN`. A runtime that cannot establish authority or exact identity must stop rather than invent the missing fact.

## Security

Never copy credentials, OAuth tokens, NDA/confidential documents, or private company material into this public repository. Use only the authorized storage surface for restricted evidence.
