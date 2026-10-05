using System.Text;
using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;
public sealed partial class BoardStore
{
    private static async Task UpgradeSearchAsync(BoardDbContext db)
    {
        var hasText=await db.Database.SqlQueryRaw<int>("SELECT count(*) AS Value FROM pragma_table_info('GuestRegistrations') WHERE name='SearchText'").SingleAsync();
        if(hasText==0){
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE GuestRegistrations ADD COLUMN SearchText TEXT NOT NULL DEFAULT ''");
            await db.Database.ExecuteSqlRawAsync("UPDATE GuestRegistrations SET SearchText=lower(Name||char(31)||Phone||char(31)||DocumentNumber||char(31)||Platform||char(31)||Notes)");
        }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER IF NOT EXISTS StayText_insert AFTER INSERT ON GuestRegistrations BEGIN
              UPDATE GuestRegistrations SET SearchText=lower(new.Name||char(31)||new.Phone||char(31)||new.DocumentNumber||char(31)||new.Platform||char(31)||new.Notes) WHERE Id=new.Id;
            END;
            CREATE TRIGGER IF NOT EXISTS StayText_update AFTER UPDATE OF Name,Phone,DocumentNumber,Notes,Platform ON GuestRegistrations BEGIN
              UPDATE GuestRegistrations SET SearchText=lower(new.Name||char(31)||new.Phone||char(31)||new.DocumentNumber||char(31)||new.Platform||char(31)||new.Notes) WHERE Id=new.Id;
            END;
            CREATE INDEX IF NOT EXISTS IX_Stays_ShortText ON GuestRegistrations(Id DESC,SearchText,RoomId) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Stays_Suggest ON GuestRegistrations(Id DESC,Name,DocumentNumber,DocumentType,Phone,CheckedInAtUtc) WHERE DeletedAtUtc IS NULL;
            DROP INDEX IF EXISTS IX_Stays_TextScan;
            CREATE INDEX IF NOT EXISTS IX_Stays_Page ON GuestRegistrations(Id DESC,CheckedOutAtUtc) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Stays_VisibleId ON GuestRegistrations(Id DESC) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Stays_StatusId ON GuestRegistrations(CheckedOutAtUtc,Id DESC) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Stays_Identity ON GuestRegistrations(
                upper(DocumentNumber),CASE WHEN DocumentNumber='' THEN '' ELSE DocumentType END,
                CASE WHEN DocumentNumber='' THEN Name ELSE '' END,CASE WHEN DocumentNumber='' THEN Phone ELSE '' END,Id DESC) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Stays_RoomHistory ON GuestRegistrations(RoomId,Id DESC);
            CREATE INDEX IF NOT EXISTS IX_Stays_BillCover ON GuestRegistrations(CheckedInAtUtc,SalePriceCents) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IF NOT EXISTS IX_Rooms_Number ON Rooms(RoomNumber);
            """);
        // Build only on first migration, in the same transaction as its maintenance triggers.
        var exists=await db.Database.SqlQueryRaw<int>("SELECT count(*) AS Value FROM sqlite_master WHERE type='table' AND name='StaySearch'").SingleAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS StaySearch USING fts5(Name,Phone,DocumentNumber,Notes,Platform,content='GuestRegistrations',content_rowid='Id',tokenize='trigram');
            CREATE TRIGGER IF NOT EXISTS StaySearch_insert AFTER INSERT ON GuestRegistrations BEGIN
              INSERT INTO StaySearch(rowid,Name,Phone,DocumentNumber,Notes,Platform) VALUES(new.Id,new.Name,new.Phone,new.DocumentNumber,new.Notes,new.Platform);
            END;
            CREATE TRIGGER IF NOT EXISTS StaySearch_delete AFTER DELETE ON GuestRegistrations BEGIN
              INSERT INTO StaySearch(StaySearch,rowid,Name,Phone,DocumentNumber,Notes,Platform) VALUES('delete',old.Id,old.Name,old.Phone,old.DocumentNumber,old.Notes,old.Platform);
            END;
            CREATE TRIGGER IF NOT EXISTS StaySearch_update AFTER UPDATE OF Name,Phone,DocumentNumber,Notes,Platform ON GuestRegistrations BEGIN
              INSERT INTO StaySearch(StaySearch,rowid,Name,Phone,DocumentNumber,Notes,Platform) VALUES('delete',old.Id,old.Name,old.Phone,old.DocumentNumber,old.Notes,old.Platform);
              INSERT INTO StaySearch(rowid,Name,Phone,DocumentNumber,Notes,Platform) VALUES(new.Id,new.Name,new.Phone,new.DocumentNumber,new.Notes,new.Platform);
            END;
            """);
        if(exists==0)await db.Database.ExecuteSqlRawAsync("INSERT INTO StaySearch(StaySearch) VALUES('rebuild')");
    }
    private static IQueryable<GuestRegistration> SearchCandidates(BoardDbContext db,string term,bool includeRoom=true)
    {
        var query=db.GuestRegistrations.AsNoTracking().Where(g=>g.DeletedAtUtc==null);
        // Trigram handles literal substring search from 3 Unicode characters. Short Chinese names still use the exact existing substring semantics.
        if(term.Length==0)return query;
        if(term.EnumerateRunes().Count()<3)return db.GuestRegistrations.FromSqlInterpolated($"SELECT * FROM GuestRegistrations INDEXED BY IX_Stays_ShortText WHERE DeletedAtUtc IS NULL AND (instr(SearchText,{term})>0 OR RoomId IN (SELECT Id FROM Rooms WHERE instr(CAST(RoomNumber AS TEXT),{term})>0))").AsNoTracking();
        var phrase="\""+term.Replace("\"","\"\"")+"\"";
        var ids=db.Database.SqlQuery<long>($"SELECT rowid AS Value FROM StaySearch WHERE StaySearch MATCH {phrase}");
        if(includeRoom){
            var combined=db.Database.SqlQuery<long>($"SELECT rowid AS Value FROM StaySearch WHERE StaySearch MATCH {phrase} UNION SELECT g.Id AS Value FROM GuestRegistrations g WHERE g.RoomId IN (SELECT r.Id FROM Rooms r WHERE instr(CAST(r.RoomNumber AS TEXT),{term})>0)");
            return query.Where(g=>combined.Contains(g.Id));
        }
        return query.Where(g=>ids.Contains(g.Id));
    }
}
