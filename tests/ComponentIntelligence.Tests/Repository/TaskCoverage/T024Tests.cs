using ComponentIntelligence.Repository;
using Xunit;
namespace ComponentIntelligence.Tests.Repository.TaskCoverage;
public sealed class T024Tests
{
 [Fact] public void RunLogTables_CreateAndStoreRows()
 {
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); var db=Path.Combine(dir,"ci.db");
  try { var factory=new SqliteConnectionFactory(); using var c=factory.Open(db); using(var cmd=c.CreateCommand()){cmd.CommandText=SqliteSchema.ComponentsTable+SqliteSchema.ResolutionRunsTable+SqliteSchema.EnrichmentRunsTable+SqliteSchema.VerificationResultsTable;cmd.ExecuteNonQuery();}
   AssertColumns(c,"resolution_runs","id","component_id","status","started_at","completed_at","message"); AssertColumns(c,"enrichment_runs","id","component_id","status","started_at"); AssertColumns(c,"verification_results","id","run_id","component_id","status","checked_at","details");
   Exec(c,"INSERT INTO components (id,manufacturer,official_model,created_at,updated_at) VALUES ('c','IFM','O5D100','2026','2026');"); Exec(c,"INSERT INTO resolution_runs (id,component_id,status,started_at) VALUES ('r','c','DONE','2026');"); Exec(c,"INSERT INTO enrichment_runs (id,component_id,status,started_at) VALUES ('e','c','DONE','2026');"); Exec(c,"INSERT INTO verification_results (id,run_id,component_id,status,checked_at) VALUES ('v','e','c','VERIFIED','2026');");
  } finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
 }
 static void Exec(Microsoft.Data.Sqlite.SqliteConnection c,string sql){using var x=c.CreateCommand();x.CommandText=sql;Assert.Equal(1,x.ExecuteNonQuery());}
 static void AssertColumns(Microsoft.Data.Sqlite.SqliteConnection c,string table,params string[] expected){var cols=new HashSet<string>(StringComparer.OrdinalIgnoreCase);using var x=c.CreateCommand();x.CommandText=$"PRAGMA table_info({table});";using var r=x.ExecuteReader();while(r.Read())cols.Add(r.GetString(r.GetOrdinal("name")));Assert.All(expected,n=>Assert.Contains(n,cols));}
}
