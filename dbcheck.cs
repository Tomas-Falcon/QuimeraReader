using System;
using Microsoft.Data.Sqlite;

class Program {
    static void Main() {
        var cs = "Data Source=QuimeraReader.API/quimerareader.db";
        using var con = new SqliteConnection(cs);
        con.Open();
        using var cmd = new SqliteCommand("SELECT Id, Title, EpubFilePath, CoverImagePath FROM Books", con);
        using var reader = cmd.ExecuteReader();
        while(reader.Read()) {
            Console.WriteLine("" + reader[0] + " | "" + reader[1] + " | "" + reader[2] + " | "" + reader[3]);
        }
    }
}
