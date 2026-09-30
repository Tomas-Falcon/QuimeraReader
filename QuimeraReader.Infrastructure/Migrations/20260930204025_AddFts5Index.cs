using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    public partial class AddFts5Index : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the FTS5 virtual table
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE BooksFTS USING fts5(
                    Id UNINDEXED, 
                    Title, 
                    content='Books', 
                    content_rowid='Id'
                );
            ");

            // Populate it
            migrationBuilder.Sql(@"
                INSERT INTO BooksFTS(BooksFTS, rowid, Title) 
                SELECT 'rebuild', Id, Title FROM Books;
            ");

            // Create Triggers to keep it updated
            migrationBuilder.Sql(@"
                CREATE TRIGGER Books_ai AFTER INSERT ON Books BEGIN
                  INSERT INTO BooksFTS(rowid, Title) VALUES (new.Id, new.Title);
                END;
                CREATE TRIGGER Books_ad AFTER DELETE ON Books BEGIN
                  INSERT INTO BooksFTS(BooksFTS, rowid, Title) VALUES('delete', old.Id, old.Title);
                END;
                CREATE TRIGGER Books_au AFTER UPDATE ON Books BEGIN
                  INSERT INTO BooksFTS(BooksFTS, rowid, Title) VALUES('delete', old.Id, old.Title);
                  INSERT INTO BooksFTS(rowid, Title) VALUES (new.Id, new.Title);
                END;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Books_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Books_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Books_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS BooksFTS;");
        }
    }
}
