# Central Archive Manifest

Logical archive name: **Component Intelligence**  
Storage provider: **Google Drive**  
Role: central reusable engineering-data/document archive.

## Expected central surfaces

- Native editable central spreadsheet/sheet: `Component_Intelligence_Database`
- Desktop-consumed mirror: `Component_Intelligence_Database.xlsx`
- Required engineering sheets:
  - `Components`
  - `Ports`
  - `Pins`

## Document root contract

```text
Component Intelligence/
├─ Component_Intelligence_Database
├─ Component_Intelligence_Database.xlsx
└─ Documents/
   └─ <Manufacturer>/<Model>/...
```

Workbook paths are relative and use the logical root, for example `Documents/<Manufacturer>/<Model>/datasheet.pdf`.

## Connection verification

Before an automated write path is enabled, a runtime must verify:

1. it is connected to the intended **Component Intelligence** archive;
2. expected Components / Ports / Pins sheets exist;
3. the document root follows `Documents/<Manufacturer>/<Model>`;
4. an ArchiveLookup can read a known component and preserve stable IDs;
5. current storage authority determines which surface is editable and how `Component_Intelligence_Database.xlsx` is synchronized.

## Responsibility boundary

GitHub stores governance, Skill, schemas, Validator, evals, and this non-secret manifest. GitHub is **not a second component database**.

Google Drive stores the actual central Components / Ports / Pins rows and archived engineering documents.

## Secrets and confidential data

This public manifest contains no authentication secret. Do not add OAuth tokens, passwords, private keys, bearer credentials, NDA files, confidential company drawings, or private evidence here.

Provider-specific IDs may be recorded only when disclosure is acceptable and the value is not an authentication secret.
