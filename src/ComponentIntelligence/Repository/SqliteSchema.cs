namespace ComponentIntelligence.Repository;

public static class SqliteSchema
{
    public const string ComponentsTable = """
        CREATE TABLE IF NOT EXISTS components (
            id TEXT NOT NULL PRIMARY KEY,
            manufacturer TEXT NOT NULL,
            official_model TEXT NOT NULL,
            mpn TEXT NULL,
            product_name TEXT NULL,
            category TEXT NULL,
            subcategory TEXT NULL,
            identity_status TEXT NULL,
            enrichment_status TEXT NULL,
            verification_status TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            last_verified_at TEXT NULL,
            UNIQUE(manufacturer, official_model)
        );
        """;
    public const string ComponentSourcesTable = """
        CREATE TABLE IF NOT EXISTS component_sources (
            id TEXT NOT NULL PRIMARY KEY,
            source_authority TEXT NOT NULL,
            url TEXT NOT NULL,
            hash TEXT NULL,
            timestamp TEXT NOT NULL,
            component_id TEXT NOT NULL,
            FOREIGN KEY (component_id) REFERENCES components(id),
            UNIQUE(source_authority, url)
        );
        """;

    public const string ComponentDocumentsTable = """
        CREATE TABLE IF NOT EXISTS component_documents (
            id TEXT NOT NULL PRIMARY KEY,
            document_name TEXT NOT NULL,
            hash TEXT NOT NULL,
            timestamp TEXT NOT NULL,
            source_url TEXT NULL,
            component_id TEXT NOT NULL,
            FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string ComponentRawSpecsTable = """
        CREATE TABLE IF NOT EXISTS component_raw_specs (
            id TEXT NOT NULL PRIMARY KEY,
            component_id TEXT NOT NULL,
            section TEXT NULL,
            name TEXT NOT NULL,
            raw_value TEXT NULL,
            normalized_value TEXT NULL,
            unit TEXT NULL,
            source_id TEXT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string ComponentNormalizedSpecsTable = """
        CREATE TABLE IF NOT EXISTS component_normalized_specs (
            id TEXT NOT NULL PRIMARY KEY,
            component_id TEXT NOT NULL,
            name TEXT NOT NULL,
            text_value TEXT NULL,
            numeric_value REAL NULL,
            unit TEXT NULL,
            status TEXT NOT NULL,
            source_id TEXT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string ComponentPortsTable = """
        CREATE TABLE IF NOT EXISTS component_ports (
            id TEXT NOT NULL PRIMARY KEY,
            component_id TEXT NOT NULL,
            name TEXT NOT NULL,
            port_type TEXT NOT NULL,
            label TEXT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string ComponentPinsTable = """
        CREATE TABLE IF NOT EXISTS component_pins (
            id TEXT NOT NULL PRIMARY KEY,
            port_id TEXT NOT NULL,
            component_id TEXT NOT NULL,
            pin_number TEXT NOT NULL,
            name TEXT NOT NULL,
            signal_type TEXT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (port_id) REFERENCES component_ports(id),
            FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string ResolutionRunsTable = """
        CREATE TABLE IF NOT EXISTS resolution_runs (
            id TEXT NOT NULL PRIMARY KEY, component_id TEXT NOT NULL, status TEXT NOT NULL, started_at TEXT NOT NULL, completed_at TEXT NULL, message TEXT NULL, FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string EnrichmentRunsTable = """
        CREATE TABLE IF NOT EXISTS enrichment_runs (
            id TEXT NOT NULL PRIMARY KEY, component_id TEXT NOT NULL, status TEXT NOT NULL, started_at TEXT NOT NULL, completed_at TEXT NULL, message TEXT NULL, FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;

    public const string VerificationResultsTable = """
        CREATE TABLE IF NOT EXISTS verification_results (
            id TEXT NOT NULL PRIMARY KEY, run_id TEXT NULL, component_id TEXT NOT NULL, status TEXT NOT NULL, checked_at TEXT NOT NULL, details TEXT NULL, FOREIGN KEY (run_id) REFERENCES enrichment_runs(id), FOREIGN KEY (component_id) REFERENCES components(id)
        );
        """;
}
