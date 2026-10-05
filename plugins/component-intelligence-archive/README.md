# Component Intelligence Archive Plugin Source

This directory stores the durable Plugin manifest source for the private **Component Intelligence Archive** Plugin.

The Plugin package is intentionally assembled from canonical repository assets instead of duplicating policy:

- `plugin.json` — Plugin identity/presentation.
- `../../skills/component-intelligence-archive/SKILL.md` — canonical portable procedure.
- `../../skills/component-intelligence-archive/references/TOOL_CONTRACT.md` — provider-neutral capability contract.

The Plugin does **not** copy `COMPONENT_ARCHIVE_SPEC_V2.md`. Runtime archive decisions resolve current policy from:

```text
jackp803/Component-Intelligence-Public
branch: main
docs/archive/AUTHORITY.md
```

## Package layout

A release archive is assembled as:

```text
component-intelligence-archive/
├─ plugin.json
└─ skills/
   └─ component-intelligence-archive/
      ├─ SKILL.md
      └─ references/
         └─ TOOL_CONTRACT.md
```

This keeps:

```text
Policy       → GitHub Authority
Procedure    → canonical SKILL.md
Enforcement  → archive/validator/
Archive Data → Google Drive
```

Plugin releases may change version/presentation without creating a second engineering policy source.
