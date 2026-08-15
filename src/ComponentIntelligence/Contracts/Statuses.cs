namespace ComponentIntelligence.Contracts;

public enum BomImportStatus { Imported, ImportedWithWarnings, Invalid }
public enum ResolutionStatus { WaitingForInput, Resolving, Resolved, Ambiguous, NotFound, Conflict, Failed }
public enum MatchLevel { None, Exact, Strong, Ambiguous }
public enum EnrichmentStatus { NotStarted, Enriching, Enriched, Partial, Failed }
public enum VerificationStatus { Verified, SingleSource, Conflict, NotAvailable, NotFound, Inferred, UserConfirmed }
public enum ReadinessStatus { Ready, Partial, NotReady }
public enum ComponentSourceType { ManufacturerDatasheet, ManufacturerProductPage, ManufacturerManual, ManufacturerDownloadCenter, AuthorizedDistributor, TrustedThirdParty, GenericWeb, AiInference, User }
public enum ExtractionMethod { StructuredJson, JsonLd, Html, Regex, PdfText, TableParser, BrowserAutomation, OcrText, AiText, AiVision, UserInput }
