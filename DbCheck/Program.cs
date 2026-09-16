using System;
using System.Linq;
using Microsoft.Data.Sqlite;

class Program {
    static void Main() {
        var cs = "Data Source=config/quimerareader.db";
        using var con = new SqliteConnection(cs);
        con.Open();

        Console.WriteLine("Merging duplicate authors...");
        using (var cmd = new SqliteCommand("SELECT Name, MIN(Id) as MinId FROM Authors GROUP BY Name HAVING COUNT(*) > 1", con))
        {
            using var reader = cmd.ExecuteReader();
            while(reader.Read())
            {
                var name = reader.GetString(0);
                var minId = reader.GetInt32(1);
                
                using var updateCmd = new SqliteCommand("UPDATE BookAuthor SET AuthorId = @minId WHERE AuthorId IN (SELECT Id FROM Authors WHERE Name = @name AND Id != @minId)", con);
                updateCmd.Parameters.AddWithValue("@minId", minId);
                updateCmd.Parameters.AddWithValue("@name", name);
                updateCmd.ExecuteNonQuery();

                using var deleteCmd = new SqliteCommand("DELETE FROM Authors WHERE Name = @name AND Id != @minId", con);
                deleteCmd.Parameters.AddWithValue("@minId", minId);
                deleteCmd.Parameters.AddWithValue("@name", name);
                deleteCmd.ExecuteNonQuery();
                
                Console.WriteLine("Merged '" + name + "' -> kept ID " + minId);
            }
        }

        Console.WriteLine("Merging duplicate categories...");
        using (var cmd = new SqliteCommand("SELECT Name, MIN(Id) as MinId FROM Categories GROUP BY Name HAVING COUNT(*) > 1", con))
        {
            using var reader = cmd.ExecuteReader();
            while(reader.Read())
            {
                var name = reader.GetString(0);
                var minId = reader.GetInt32(1);
                
                using var updateCmd = new SqliteCommand("UPDATE BookCategory SET CategoryId = @minId WHERE CategoryId IN (SELECT Id FROM Categories WHERE Name = @name AND Id != @minId)", con);
                updateCmd.Parameters.AddWithValue("@minId", minId);
                updateCmd.Parameters.AddWithValue("@name", name);
                updateCmd.ExecuteNonQuery();

                using var deleteCmd = new SqliteCommand("DELETE FROM Categories WHERE Name = @name AND Id != @minId", con);
                deleteCmd.Parameters.AddWithValue("@minId", minId);
                deleteCmd.Parameters.AddWithValue("@name", name);
                deleteCmd.ExecuteNonQuery();
                
                Console.WriteLine("Merged category '" + name + "' -> kept ID " + minId);
            }
        }
        Console.WriteLine("Done.");
    }
}
