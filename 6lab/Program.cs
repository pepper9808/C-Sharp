using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DomainModelComprehensiveLinqReportDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class Book
    {
        public int Id { get; }
        public string Title { get; }
        public string Author { get; }
        public string Genre { get; }
        public int Year { get; }
        public int Pages { get; }
        public bool IsBorrowed { get; private set; }
        public int? BorrowedByReaderId { get; private set; }

        public Book(int id, string title, string author, string genre, int year, int pages)
        {
            Id = id;
            Title = title;
            Author = author;
            Genre = genre;
            Year = year;
            Pages = pages;
        }

        public void Borrow(int readerId)
        {
            IsBorrowed = true;
            BorrowedByReaderId = readerId;
        }
    }

    public class Reader
    {
        public int Id { get; }
        public string FullName { get; }
        public string Faculty { get; }

        public Reader(int id, string fullName, string faculty)
        {
            Id = id;
            FullName = fullName;
            Faculty = faculty;
        }
    }

    // =========================================================
    // 2. МОДЕЛІ АНАЛІТИЧНИХ ПОКАЗНИКІВ ЗВІТУ
    // =========================================================
    public class GenreMetrics
    {
        public string Genre { get; set; }
        public int BooksCount { get; set; }
        public int BorrowedCount { get; set; }
        public int TotalPages { get; set; }
        public double AveragePages { get; set; }
    }

    public class AuthorMetrics
    {
        public string Author { get; set; }
        public int BooksCount { get; set; }
        public int MaxBookPages { get; set; }
    }

    public class ReaderActivityMetrics
    {
        public string ReaderName { get; set; }
        public string Faculty { get; set; }
        public int ActiveLoansCount { get; set; }
        public List<string> BorrowedTitles { get; set; }
    }

    public class ComprehensiveLibraryReport
    {
        public DateTime GeneratedAt { get; set; }

        // Показник 1: Загальні агреговані метрики фонду
        public int TotalBooks { get; set; }
        public int AvailableBooks { get; set; }
        public int BorrowedBooks { get; set; }
        public double UtilizationPercent { get; set; }
        public int TotalPagesInFund { get; set; }
        public double AveragePagesPerBook { get; set; }
        public int MinPages { get; set; }
        public int MaxPages { get; set; }

        // Показник 2: Групування за жанрами (GroupBy + Count + Sum + Average)
        public List<GenreMetrics> GenreBreakdown { get; set; }

        // Показник 3: Групування за авторами (GroupBy + Count + Max)
        public List<AuthorMetrics> AuthorBreakdown { get; set; }

        // Показник 4: Активність читачів (Join + GroupBy + Count)
        public List<ReaderActivityMetrics> ReaderActivity { get; set; }
    }

    // =========================================================
    // 3. ГЕНЕРАТОР КОМПЛЕКСНОГО АНАЛІТИЧНОГО ЗВІТУ (LINQ)
    // =========================================================
    public class LibraryAnalyticsReporter
    {
        public ComprehensiveLibraryReport BuildReport(IEnumerable<Book> books, IEnumerable<Reader> readers)
        {
            // Матеріалізуємо вхідні колекції у списки (ToList), щоб уникнути повторних обчислень
            var booksList = books.ToList();
            var readersList = readers.ToList();

            int totalBooks = booksList.Count;
            int borrowedBooks = booksList.Count(b => b.IsBorrowed);
            int availableBooks = totalBooks - borrowedBooks;

            // 1. Групування за жанрами з підрахунком кількості та агрегатних метрик
            var genreStats = booksList
                .GroupBy(b => b.Genre)
                .Select(group => new GenreMetrics
                {
                    Genre = group.Key,
                    BooksCount = group.Count(),
                    BorrowedCount = group.Count(b => b.IsBorrowed),
                    TotalPages = group.Sum(b => b.Pages),
                    AveragePages = group.Average(b => b.Pages)
                })
                .OrderByDescending(g => g.BooksCount)
                .ThenBy(g => g.Genre)
                .ToList();

            // 2. Групування за авторами
            var authorStats = booksList
                .GroupBy(b => b.Author)
                .Select(group => new AuthorMetrics
                {
                    Author = group.Key,
                    BooksCount = group.Count(),
                    MaxBookPages = group.Max(b => b.Pages)
                })
                .OrderByDescending(a => a.BooksCount)
                .ThenBy(a => a.Author)
                .ToList();

            // 3. З'єднання (Join) виданих книг із читачами та групування (GroupBy) за читачем
            var readerStats = booksList
                .Where(b => b.IsBorrowed && b.BorrowedByReaderId.HasValue)
                .Join(
                    readersList,
                    book => book.BorrowedByReaderId.Value,
                    reader => reader.Id,
                    (book, reader) => new { Reader = reader, BookTitle = book.Title }
                )
                .GroupBy(pair => new { pair.Reader.Id, pair.Reader.FullName, pair.Reader.Faculty })
                .Select(group => new ReaderActivityMetrics
                {
                    ReaderName = group.Key.FullName,
                    Faculty = group.Key.Faculty,
                    ActiveLoansCount = group.Count(),
                    BorrowedTitles = group.Select(x => x.BookTitle).ToList()
                })
                .OrderByDescending(r => r.ActiveLoansCount)
                .ToList();

            return new ComprehensiveLibraryReport
            {
                GeneratedAt = DateTime.Now,
                TotalBooks = totalBooks,
                AvailableBooks = availableBooks,
                BorrowedBooks = borrowedBooks,
                UtilizationPercent = totalBooks > 0 ? (double)borrowedBooks / totalBooks * 100.0 : 0.0,
                TotalPagesInFund = totalBooks > 0 ? booksList.Sum(b => b.Pages) : 0,
                AveragePagesPerBook = totalBooks > 0 ? booksList.Average(b => b.Pages) : 0.0,
                MinPages = totalBooks > 0 ? booksList.Min(b => b.Pages) : 0,
                MaxPages = totalBooks > 0 ? booksList.Max(b => b.Pages) : 0,
                GenreBreakdown = genreStats,
                AuthorBreakdown = authorStats,
                ReaderActivity = readerStats
            };
        }

        public string FormatReportAsText(ComprehensiveLibraryReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendLine($"   КОМПЛЕКСНИЙ АНАЛІТИЧНИЙ ЗВІТ БІБЛІОТЕКИ ({report.GeneratedAt:dd.MM.yyyy HH:mm})");
            sb.AppendLine("======================================================================");

            sb.AppendLine("\n1. ЗАГАЛЬНІ ПОКАЗНИКИ ФОНДУ (Count, Sum, Average, Min, Max):");
            sb.AppendLine($"   • Всього книг у каталозі   : {report.TotalBooks}");
            sb.AppendLine($"   • У наявності / Видано    : {report.AvailableBooks} / {report.BorrowedBooks} (Завантаженість: {report.UtilizationPercent:F1}%)");
            sb.AppendLine($"   • Загальний обсяг фонду   : {report.TotalPagesInFund} стор.");
            sb.AppendLine($"   • Обсяг книги (Сер/Мін/Макс): {report.AveragePagesPerBook:F1} / {report.MinPages} / {report.MaxPages} стор.");

            sb.AppendLine("\n2. СТАТИСТИКА ЗА ЖАНРАМИ (GroupBy + Count + Sum + Average):");
            foreach (var g in report.GenreBreakdown)
            {
                sb.AppendLine($"   • {g.Genre,-22} | Книг: {g.BooksCount} (Видано: {g.BorrowedCount}) | Сторінок: {g.TotalPages} (Сер: {g.AveragePages:F0})");
            }

            sb.AppendLine("\n3. СТАТИСТИКА ЗА АВТОРАМИ (GroupBy + Count + Max):");
            foreach (var a in report.AuthorBreakdown)
            {
                sb.AppendLine($"   • {a.Author,-22} | Книг у фонді: {a.BooksCount} | Найбільша книга: {a.MaxBookPages} стор.");
            }

            sb.AppendLine("\n4. АКТИВНІСТЬ ЧИТАЧІВ НА ПОТОЧНИЙ МОМЕНТ (Join + GroupBy):");
            if (!report.ReaderActivity.Any())
            {
                sb.AppendLine("   • Активних видач немає.");
            }
            else
            {
                foreach (var r in report.ReaderActivity)
                {
                    sb.AppendLine($"   • {r.ReaderName} ({r.Faculty}) — {r.ActiveLoansCount} кн.: {string.Join(", ", r.BorrowedTitles.Select(t => $"\"{t}\""))}");
                }
            }

            sb.AppendLine("======================================================================");
            return sb.ToString();
        }

        public void SaveReportToFile(ComprehensiveLibraryReport report, string filePath)
        {
            string formattedText = FormatReportAsText(report);
            File.WriteAllText(filePath, formattedText, Encoding.UTF8);
        }
    }

    // =========================================================
    // 4. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var readers = new List<Reader>
            {
                new Reader(1, "Олександр Коваленко", "ФІТ"),
                new Reader(2, "Марія Бойко", "ФКН"),
                new Reader(3, "Дмитро Ткаченко", "ФІТ")
            };

            var books = new List<Book>
            {
                new Book(1, "CLR via C#", "Джеффрі Ріхтер", "Програмування", 2012, 896),
                new Book(2, "Чистий код", "Роберт Мартін", "Програмування", 2008, 464),
                new Book(3, "Чиста архітектура", "Роберт Мартін", "Архітектура ПЗ", 2017, 432),
                new Book(4, "Патерни проєктування", "Ерік Фрімен", "Архітектура ПЗ", 2020, 640),
                new Book(5, "C# 12 у стислому викладі", "Джозеф Албахарі", "Програмування", 2023, 1040),
                new Book(6, "Проєктування високонавантажених систем", "Мартін Клеппман", "Бази даних та мережі", 2018, 610)
            };

            // Моделюємо поточні видачі книг читачам
            books[0].Borrow(1); // CLR via C# -> Олександр Коваленко
            books[2].Borrow(1); // Чиста архітектура -> Олександр Коваленко
            books[3].Borrow(2); // Патерни проєктування -> Марія Бойко

            var reporter = new LibraryAnalyticsReporter();
            var report = reporter.BuildReport(books, readers);

            // 1. Вивід сформованого звіту в консоль
            string reportText = reporter.FormatReportAsText(report);
            Console.WriteLine(reportText);

            // 2. Збереження підсумкового звіту у файл
            string fileName = "comprehensive_library_report.txt";
            reporter.SaveReportToFile(report, fileName);
            Console.WriteLine($"Звіт успішно збережено у файл: {Path.GetFullPath(fileName)}");
        }
    }
}