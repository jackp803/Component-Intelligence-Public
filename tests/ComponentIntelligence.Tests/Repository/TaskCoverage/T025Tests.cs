using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;
using Xunit;
namespace ComponentIntelligence.Tests.Repository.TaskCoverage;
public sealed class T025Tests
{
 [Fact] public async Task SaveComponentAsync_PersistsComponent()
 { var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));var db=Path.Combine(dir,"ci.db");var time=new DateTimeOffset(2026,8,13,0,0,0,TimeSpan.Zero);try{var factory=new SqliteConnectionFactory();var repo=new ComponentRepository(db,factory);await repo.SaveComponentAsync(new ComponentRecord{Id="c1",Manufacturer="IFM",OfficialModel="O5D100",Mpn="O5D100",CreatedAt=time,UpdatedAt=time});using var c=factory.Open(db);using var cmd=c.CreateCommand();cmd.CommandText="SELECT manufacturer,official_model,mpn FROM components WHERE id='c1'";using var r=cmd.ExecuteReader();Assert.True(r.Read());Assert.Equal("IFM",r.GetString(0));Assert.Equal("O5D100",r.GetString(1));Assert.Equal("O5D100",r.GetString(2));}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}}
 [Fact] public async Task SaveComponentAsync_RejectsDuplicateIdentity(){var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));var db=Path.Combine(dir,"ci.db");try{var repo=new ComponentRepository(db);var t=DateTimeOffset.UtcNow;ComponentRecord Make(string id)=>new(){Id=id,Manufacturer="IFM",OfficialModel="O5D100",CreatedAt=t,UpdatedAt=t};await repo.SaveComponentAsync(Make("1"));await Assert.ThrowsAsync<SqliteException>(()=>repo.SaveComponentAsync(Make("2")));}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}}
}
