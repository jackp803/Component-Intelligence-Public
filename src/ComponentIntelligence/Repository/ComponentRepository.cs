using Microsoft.Data.Sqlite;
namespace ComponentIntelligence.Repository;
public sealed record ComponentRecord
{
 public required string Id { get; init; } public required string Manufacturer { get; init; } public required string OfficialModel { get; init; }
 public string? Mpn { get; init; } public string? ProductName { get; init; } public string? Category { get; init; } public string? Subcategory { get; init; } public string? IdentityStatus { get; init; } public string? EnrichmentStatus { get; init; } public string? VerificationStatus { get; init; }
 public DateTimeOffset CreatedAt { get; init; } public DateTimeOffset UpdatedAt { get; init; } public DateTimeOffset? LastVerifiedAt { get; init; }
}
public sealed class ComponentRepository
{
 readonly string _databasePath; readonly SqliteConnectionFactory _factory;
 public ComponentRepository(string databasePath, SqliteConnectionFactory? factory=null){ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);_databasePath=databasePath;_factory=factory??new SqliteConnectionFactory();}
 public async Task SaveComponentAsync(ComponentRecord component,CancellationToken cancellationToken=default)
 {
  ArgumentNullException.ThrowIfNull(component); using var connection=_factory.Open(_databasePath); await using(var schema=connection.CreateCommand()){schema.CommandText=SqliteSchema.ComponentsTable;await schema.ExecuteNonQueryAsync(cancellationToken);}
  await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO components (id,manufacturer,official_model,mpn,product_name,category,subcategory,identity_status,enrichment_status,verification_status,created_at,updated_at,last_verified_at) VALUES ($id,$manufacturer,$model,$mpn,$name,$category,$subcategory,$identity,$enrichment,$verification,$created,$updated,$verified);";
  command.Parameters.AddWithValue("$id",component.Id); command.Parameters.AddWithValue("$manufacturer",component.Manufacturer); command.Parameters.AddWithValue("$model",component.OfficialModel); command.Parameters.AddWithValue("$mpn",Db(component.Mpn)); command.Parameters.AddWithValue("$name",Db(component.ProductName)); command.Parameters.AddWithValue("$category",Db(component.Category)); command.Parameters.AddWithValue("$subcategory",Db(component.Subcategory)); command.Parameters.AddWithValue("$identity",Db(component.IdentityStatus)); command.Parameters.AddWithValue("$enrichment",Db(component.EnrichmentStatus)); command.Parameters.AddWithValue("$verification",Db(component.VerificationStatus)); command.Parameters.AddWithValue("$created",component.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated",component.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$verified",component.LastVerifiedAt is {} v?v.ToString("O"):DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken);
 }
 static object Db(string? value)=>value is null?DBNull.Value:value;
}
