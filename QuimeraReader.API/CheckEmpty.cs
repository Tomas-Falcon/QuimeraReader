using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

class Program {
    static void Main() {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=C:/Users/tomas/Desktop/Proyectos/QuimeraReader/QuimeraReader.API/Quimera.db").Options;
        using var db = new AppDbContext(options);
        var booksEmpty = db.Books.Where(b => b.EpubFilePath == null || b.EpubFilePath == "").ToList();
        Console.WriteLine("EMPTY COUNT: " + booksEmpty.Count);
    }
}
