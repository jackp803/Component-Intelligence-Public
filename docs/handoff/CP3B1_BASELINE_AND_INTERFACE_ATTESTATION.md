# CP3-B1 Component Producer Baseline and Interface Attestation

Task: `E5-20260903-021`

Exact authorized Component Intelligence baseline:

```text
head = 98f5bebb378f6ba7b4dc9801b05399ee43a5b830
tree = 807ea7ed5eade71d9c2598d1cb11e42f5eff2c59
branch = agent/e5-cp3b-drawing-planning-workspace-20260903
```

Accepted CP3-A source surface:

```text
SymbolResolver.ResolveAsync(string componentId, SymbolRole role, bool allowGeneratedGeneric = true, CancellationToken cancellationToken = default)
SymbolResolution: ComponentId, Role, SourceType, Revision, AssetPath, Sha256, PortBindings, GeneratedGeneric
```

Direct CP3-A roles: `Schematic`, `ConnectorDetail`, `PanelFootprint`, `TopologyVisual` only.

Current accepted `ElectricalProjectMigrator.CurrentSchemaVersion` is exactly `0.4`. CP3-B4 therefore uses next project schema `0.5`; this note does not itself migrate the project.

Repository inspection found no accepted explicit CP3-B authority for `controllerId`, `physicalModuleId`, `functionKind`, `machineZoneId`, `seriesChainId`, or `heavyDutyConnectorId` on ordinary component instances. CP3-B1 therefore keeps those planning-context fields null unless a confirmed source supplies them. It must not derive them from `TypeKey`, display name, model, filename, geometry, placement, or ordering.

Fresh .NET baseline commands are `NOT_RUN` in the current connector-only worker environment. Prior PM-accepted CP3-A executable evidence remains historical context, not a fresh E5 run.

Protected state: no production workbook/SQLite/formal AutoCAD asset execution or mutation.
