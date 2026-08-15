# Task Index（任務索引）

| Phase | Task | Title | Dependency Artifact | Output Artifact |
|---|---|---|---|---|
| Phase 0 | `T001` | 建立 ComponentIntelligence 專案骨架 | `` | `T001-result` |
| Phase 0 | `T002` | 建立核心 Status Enum（狀態列舉） | `T001-result` | `T002-result` |
| Phase 0 | `T003` | ComponentIdentity Contract（元件身分契約） | `T002-result` | `T003-result` |
| Phase 0 | `T004` | Resolution Contract（解析契約） | `T003-result` | `T004-result` |
| Phase 0 | `T005` | Evidence Contract（證據契約） | `T004-result` | `T005-result` |
| Phase 0 | `T006` | RawSpecification Contract（原始規格契約） | `T005-result` | `T006-result` |
| Phase 0 | `T007` | Port / Pin Contract（連接埠／腳位契約） | `T006-result` | `T007-result` |
| Phase 0 | `T008` | ComponentIR Skeleton（元件 IR 骨架） | `T007-result` | `T008-result` |
| Phase 1 | `T009` | BomRow | `T008-result` | `T009-result` |
| Phase 1 | `T010` | Spare Quantity Calculator（備品計算） | `T009-result` | `T010-result` |
| Phase 1 | `T011` | BomRowValidator（BOM 資料列驗證） | `T010-result` | `T011-result` |
| Phase 1 | `T012` | BomHeaderMapper（標題映射） | `T011-result` | `T012-result` |
| Phase 1 | `T013` | Excel Row Reader（Excel 資料列讀取） | `T012-result` | `T013-result` |
| Phase 1 | `T014` | BomImporter | `T013-result` | `T014-result` |
| Phase 1 | `T015` | BOM Template Generator（BOM 模板產生器） | `T014-result` | `T015-result` |
| Phase 1 | `T016` | BOM Integration Tests | `T015-result` | `T016-result` |
| Phase 2 | `T017` | SQLite Connection Factory | `T016-result` | `T017-result` |
| Phase 2 | `T018` | DatabaseBootstrap | `T017-result` | `T018-result` |
| Phase 2 | `T019` | Components Schema | `T018-result` | `T019-result` |
| Phase 2 | `T020` | Source / Document Schema | `T019-result` | `T020-result` |
| Phase 2 | `T021` | Raw Specs Schema | `T020-result` | `T021-result` |
| Phase 2 | `T022` | Normalized Specs Schema | `T021-result` | `T022-result` |
| Phase 2 | `T023` | Port / Pin Schema | `T022-result` | `T023-result` |
| Phase 2 | `T024` | Run Log Schema | `T023-result` | `T024-result` |
| Phase 2 | `T025` | SaveComponentAsync | `T024-result` | `T025-result` |
| Phase 2 | `T026` | GetByIdAsync | `T025-result` | `T026-result` |
| Phase 2 | `T027` | FindByIdentityAsync | `T026-result` | `T027-result` |
| Phase 2 | `T028` | UpdateComponentAsync | `T027-result` | `T028-result` |
| Phase 2 | `T029` | Repository CRUD Tests | `T028-result` | `T029-result` |
| Phase 3 | `T030` | ManufacturerNormalizer | `T029-result` | `T030-result` |
| Phase 3 | `T031` | ModelNormalizer | `T030-result` | `T031-result` |
| Phase 3 | `T032` | ManufacturerAliasRepository | `T031-result` | `T032-result` |
| Phase 3 | `T033` | LocalComponentLookup | `T032-result` | `T033-result` |
| Phase 3 | `T034` | IdentityMatcher | `T033-result` | `T034-result` |
| Phase 3 | `T035` | ResolutionDecisionEngine | `T034-result` | `T035-result` |
| Phase 3 | `T036` | ComponentResolver Local Pipeline | `T035-result` | `T036-result` |
| Phase 3 | `T037` | Local Resolver Tests | `T036-result` | `T037-result` |
| Phase 4 | `T038` | IComponentSource | `T037-result` | `T038-result` |
| Phase 4 | `T039` | SourceResult / ProductPage / ComponentDocument | `T038-result` | `T039-result` |
| Phase 4 | `T040` | SourcePlanner | `T039-result` | `T040-result` |
| Phase 4 | `T041` | FakeComponentSource | `T040-result` | `T041-result` |
| Phase 4 | `T042` | Resolver External Hook | `T041-result` | `T042-result` |
| Phase 4 | `T043` | External Source Tests | `T042-result` | `T043-result` |
| Phase 5 | `T044` | HttpClient Infrastructure | `T043-result` | `T044-result` |
| Phase 5 | `T045` | HTTP Response Wrapper | `T044-result` | `T045-result` |
| Phase 5 | `T046` | Retry Policy | `T045-result` | `T046-result` |
| Phase 5 | `T047` | Rate Limit Contract | `T046-result` | `T047-result` |
| Phase 5 | `T048` | HTML Parser Wrapper | `T047-result` | `T048-result` |
| Phase 5 | `T049` | Fake HTTP Tests | `T048-result` | `T049-result` |
| Phase 6 | `T050` | IfmSource Skeleton | `T049-result` | `T050-result` |
| Phase 6 | `T051` | IFM Product Search | `T050-result` | `T051-result` |
| Phase 6 | `T052` | IFM Identity Parser | `T051-result` | `T052-result` |
| Phase 6 | `T053` | IFM Official Product URL | `T052-result` | `T053-result` |
| Phase 6 | `T054` | IFM Candidate Builder | `T053-result` | `T054-result` |
| Phase 6 | `T055` | IFM Resolver Integration | `T054-result` | `T055-result` |
| Phase 6 | `T056` | IFM Fixture Tests | `T055-result` | `T056-result` |
| Phase 6 | `T057` | Optional Live IFM Test | `T056-result` | `T057-result` |
| Phase 7 | `T058` | RawComponentProfile Contract | `T057-result` | `T058-result` |
| Phase 7 | `T059` | ComponentEnricher Skeleton | `T058-result` | `T059-result` |
| Phase 7 | `T060` | Enrichment Source Planning | `T059-result` | `T060-result` |
| Phase 7 | `T061` | Product Page Retrieval | `T060-result` | `T061-result` |
| Phase 7 | `T062` | DocumentDiscoverer | `T061-result` | `T062-result` |
| Phase 7 | `T063` | AssetDiscoverer | `T062-result` | `T063-result` |
| Phase 7 | `T064` | StructuredDataExtractor | `T063-result` | `T064-result` |
| Phase 7 | `T065` | MissingDataAnalyzer | `T064-result` | `T065-result` |
| Phase 7 | `T066` | EnrichmentRunLogger | `T065-result` | `T066-result` |
| Phase 8 | `T067` | DocumentDownloader | `T066-result` | `T067-result` |
| Phase 8 | `T068` | SHA256 HashService | `T067-result` | `T068-result` |
| Phase 8 | `T069` | CacheMetadata | `T068-result` | `T069-result` |
| Phase 8 | `T070` | PdfTextExtractor | `T069-result` | `T070-result` |
| Phase 8 | `T071` | Datasheet Fixture Test | `T070-result` | `T071-result` |
| Phase 8 | `T072` | Document Evidence | `T071-result` | `T072-result` |
| Phase 9 | `T073` | SpecificationDictionary | `T072-result` | `T073-result` |
| Phase 9 | `T074` | VoltageRawParser | `T073-result` | `T074-result` |
| Phase 9 | `T075` | CurrentRawParser | `T074-result` | `T075-result` |
| Phase 9 | `T076` | OutputTypeParser | `T075-result` | `T076-result` |
| Phase 9 | `T077` | ProtocolParser | `T076-result` | `T077-result` |
| Phase 9 | `T078` | ConnectorParser | `T077-result` | `T078-result` |
| Phase 9 | `T079` | PinTableParser | `T078-result` | `T079-result` |
| Phase 9 | `T080` | PortParser | `T079-result` | `T080-result` |
| Phase 9 | `T081` | Extraction Integration | `T080-result` | `T081-result` |
| Phase 10 | `T082` | UnitNormalizer | `T081-result` | `T082-result` |
| Phase 10 | `T083` | VoltageNormalizer | `T082-result` | `T083-result` |
| Phase 10 | `T084` | CurrentNormalizer | `T083-result` | `T084-result` |
| Phase 10 | `T085` | SignalNormalizer | `T084-result` | `T085-result` |
| Phase 10 | `T086` | ProtocolNormalizer | `T085-result` | `T086-result` |
| Phase 10 | `T087` | ConnectorNormalizer | `T086-result` | `T087-result` |
| Phase 10 | `T088` | PinNormalizer | `T087-result` | `T088-result` |
| Phase 10 | `T089` | PortNormalizer | `T088-result` | `T089-result` |
| Phase 10 | `T090` | CategoryNormalizer | `T089-result` | `T090-result` |
| Phase 10 | `T091` | ComponentNormalizer | `T090-result` | `T091-result` |
| Phase 11 | `T092` | SourceAuthority | `T091-result` | `T092-result` |
| Phase 11 | `T093` | FieldEvidence Model Integration | `T092-result` | `T093-result` |
| Phase 11 | `T094` | FieldComparator | `T093-result` | `T094-result` |
| Phase 11 | `T095` | ConflictDetector | `T094-result` | `T095-result` |
| Phase 11 | `T096` | Verification Status Decision | `T095-result` | `T096-result` |
| Phase 11 | `T097` | CompletenessCalculator | `T096-result` | `T097-result` |
| Phase 11 | `T098` | ConfidenceCalculator | `T097-result` | `T098-result` |
| Phase 11 | `T099` | WiringReadiness | `T098-result` | `T099-result` |
| Phase 11 | `T100` | TopologyReadiness | `T099-result` | `T100-result` |
| Phase 11 | `T101` | ValidationReadiness | `T100-result` | `T101-result` |
| Phase 11 | `T102` | DrawingReadiness | `T101-result` | `T102-result` |
| Phase 11 | `T103` | VerificationEngine Integration | `T102-result` | `T103-result` |
| Phase 12 | `T104` | ComponentIR Builder | `T103-result` | `T104-result` |
| Phase 12 | `T105` | Save Raw Specs | `T104-result` | `T105-result` |
| Phase 12 | `T106` | Save Normalized Specs | `T105-result` | `T106-result` |
| Phase 12 | `T107` | Save Evidence | `T106-result` | `T107-result` |
| Phase 12 | `T108` | Save Pins | `T107-result` | `T108-result` |
| Phase 12 | `T109` | Save Ports | `T108-result` | `T109-result` |
| Phase 12 | `T110` | Save Assets | `T109-result` | `T110-result` |
| Phase 12 | `T111` | Load ComponentIR | `T110-result` | `T111-result` |
| Phase 13 | `T112` | CacheDirectoryManager | `T111-result` | `T112-result` |
| Phase 13 | `T113` | CacheAccessTracker | `T112-result` | `T113-result` |
| Phase 13 | `T114` | CacheSizeCalculator | `T113-result` | `T114-result` |
| Phase 13 | `T115` | LruSelector | `T114-result` | `T115-result` |
| Phase 13 | `T116` | CacheEviction | `T115-result` | `T116-result` |
| Phase 13 | `T117` | Cache Tests | `T116-result` | `T117-result` |
| Phase 14 | `T118` | Offline E2E Fixture | `T117-result` | `T118-result` |
| Phase 14 | `T119` | IFM O5D100 E2E | `T118-result` | `T119-result` |
| Phase 14 | `T120` | Existing Component Reuse | `T119-result` | `T120-result` |
| Phase 14 | `T121` | Missing Model Case | `T120-result` | `T121-result` |
| Phase 14 | `T122` | Ambiguous Case | `T121-result` | `T122-result` |
| Phase 14 | `T123` | Conflict Case | `T122-result` | `T123-result` |
| Phase 14 | `T124` | Database Reload | `T123-result` | `T124-result` |
| Phase 14 | `T125` | Final Regression Suite | `T124-result` | `T125-result` |