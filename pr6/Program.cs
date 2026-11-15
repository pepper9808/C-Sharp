using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainModelLinqAnalyticsDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public abstract class LibraryItem
    {
        public int Id { get; }
        public string Title { get; set; }
        public string Category { get; set; }
        public int Year { get; set; }
        public bool IsBorrowed { get; private set; }
        public int? BorrowedByReaderId { get; private set; }

        protected LibraryItem(int id, string title, string category, int year)
        {
            Id = id;
            Title = title;
            Category = category;
            Year = year;
        }

        public void Borrow(int readerId)
        {
            IsBorrowed = true;
            BorrowedByReaderId = readerId;
        }
    }

    public class Book : LibraryItem
    {
        public string Author { get; set; }
        public int Pages { get; set; }

        public Book(int id, string title, string category, int year, string author, int pages)
            : base(id, title, category, year)
        {
            Author = author;
            Pages = pages;
        }
    }

    public class Magazine : LibraryItem
    {
        public int IssueNumber { get; set; }

        public Magazine(int id, string title, string category, int year, int issueNumber)
            : base(id, title, category, year)
        {
            IssueNumber = issueNumber;
        }
    }

    public class Reader
    {
        public int Id { get; }
        public string FullName { get; set; }
        public string Department { get; set; }

        public Reader(int id, string fullName, string department)
        {
            Id = id;
            FullName = fullName;
            Department = department;
        }
    }

    // DTO для результатів групування
    public class CategoryStatisticsDto
    {
        public string Category { get; set; }
        public int TotalItems { get; set; }
        public int BorrowedCount { get; set; }
        public double AverageYear { get; set; }
    }

    // =========================================================
    // 2. ПІДСИСТЕМА АНАЛІТИЧНИХ ЗАПИТІВ (LINQ)
    // =========================================================
    public class LibraryAnalyticsService
    {
        private readonly List<LibraryItem> _catalog;
        private readonly List<Reader> _readers;

        public LibraryAnalyticsService(List<LibraryItem> catalog, List<Reader> readers)
        {
            _catalog = catalog;
            _readers = readers;
        }

        // ЗАПИТ 1: Фільтрація (Where, OfType) + багаторівневе сортування (OrderByDescending, ThenBy)
        public IEnumerable<Book> GetAvailableModernBooks(int minYear)
        {
            return _catalog
                .OfType<Book>()
                .Where(b => !b.IsBorrowed && b.Year >= minYear)
                .OrderByDescending(b => b.Year)
                .ThenBy(b => b.Title);
        }

        // ЗАПИТ 2: Проєкція (Select) — формування коротких карток для каталогу
        public IEnumerable<string> GetCatalogCardsProjection()
        {
            return _catalog
                .OrderBy(item => item.Id)
                .Select(item =>
                {
                    string typeLabel = item is Book ? "Книга" : "Журнал";
                    string status = item.IsBorrowed ? "На руках" : "Вільна";
                    return $"#{item.Id:D2} [{typeLabel} | {item.Category}] \"{item.Title}\" ({item.Year}) — {status}";
                });
        }

        // ЗАПИТ 3: Агрегування (Count, Sum, Average, Min, Max)
        public void PrintBooksVolumeStatistics()
        {
            var books = _catalog.OfType<Book>().ToList();
            if (!books.Any())
            {
                Console.WriteLine("У фонді немає книг для аналізу.");
                return;
            }

            int totalBooks = books.Count();
            int totalPages = books.Sum(b => b.Pages);
            double avgPages = books.Average(b => b.Pages);
            int minPages = books.Min(b => b.Pages);
            int maxPages = books.Max(b => b.Pages);

            Console.WriteLine($"Всього книг: {totalBooks} | Загальний обсяг: {totalPages} стор.");
            Console.WriteLine($"Середня кількість сторінок: {avgPages:F1} | Мін: {minPages} стор. | Макс: {maxPages} стор.");
        }

        // ЗАПИТ 4: Групування (GroupBy) та формування підсумкової статистики за категоріями
        public List<CategoryStatisticsDto> GetStatisticsByCategory()
        {
            return _catalog
                .GroupBy(item => item.Category)
                .Select(group => new CategoryStatisticsDto
                {
                    Category = group.Key,
                    TotalItems = group.Count(),
                    BorrowedCount = group.Count(x => x.IsBorrowed),
                    AverageYear = group.Average(x => x.Year)
                })
                .OrderByDescending(stat => stat.TotalItems)
                .ToList();
        }

        // ЗАПИТ 5: З'єднання колекцій (Join) — звіт про видані книги та їхніх читачів
        public IEnumerable<string> GetActiveBorrowsReport()
        {
            return _catalog
                .Where(item => item.IsBorrowed && item.BorrowedByReaderId.HasValue)
                .Join(
                    _readers,
                    item => item.BorrowedByReaderId.Value,
                    reader => reader.Id,
                    (item, reader) => $"Читач: {reader.FullName} ({reader.Department}) -> \"{item.Title}\" [{item.Category}]"
                );
        }

        // ЗАПИТ 6: Пагінація (Skip, Take) та квантифікатори (All, Any)
        public IEnumerable<LibraryItem> GetCatalogPage(int pageNumber, int pageSize)
        {
            return _catalog
                .OrderBy(item => item.Title)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize);
        }
    }

    // =========================================================
    // 3. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Підготовка тестових даних
            var readers = new List<Reader>
            {
                new Reader(1, "Олександр Коваленко", "ІПЗ-21"),
                new Reader(2, "Марія Бойко", "КН-22"),
                new Reader(3, "Дмитро Ткаченко", "КІ-21")
            };

            var catalog = new List<LibraryItem>
            {
                new Book(1, "CLR via C#", "Програмування", 2012, "Джеффрі Ріхтер", 896),
                new Book(2, "Чистий код", "Програмування", 2008, "Роберт Мартін", 464),
                new Book(3, "Патерни проєктування", "Архітектура ПЗ", 2020, "Ерік Фрімен", 640),
                new Book(4, "C# 12 у стислому викладі", "Програмування", 2023, "Джозеф Албахарі", 1040),
                new Magazine(5, "Forbes Україна", "Економіка", 2024, 5),
                new Magazine(6, "National Geographic", "Наука", 2023, 11),
                new Book(7, "Чиста архітектура", "Архітектура ПЗ", 2017, "Роберт Мартін", 432)
            };

            // Моделюємо видачу кількох екземплярів
            catalog[0].Borrow(1); // CLR via C# -> Олександр Коваленко
            catalog[4].Borrow(2); // Forbes Україна -> Марія Бойко

            var analytics = new LibraryAnalyticsService(catalog, readers);

            Console.WriteLine("=== 1. ПРОЄКЦІЯ КАТАЛОГУ (Select) ===");
            foreach (var card in analytics.GetCatalogCardsProjection())
            {
                Console.WriteLine("  " + card);
            }

            Console.WriteLine("\n=== 2. АГРЕГУВАННЯ ОБСЯГУ КНИГ (Count, Sum, Average, Min, Max) ===");
            analytics.PrintBooksVolumeStatistics();

            Console.WriteLine("\n=== 3. ГРУПУВАННЯ ЗА КАТЕГОРІЯМИ (GroupBy) ===");
            var categoryStats = analytics.GetStatisticsByCategory();
            foreach (var stat in categoryStats)
            {
                Console.WriteLine($"  Категорія: {stat.Category,-18} | Всього: {stat.TotalItems} | Видано: {stat.BorrowedCount} | Сер. рік: {stat.AverageYear:F0}");
            }

            Console.WriteLine("\n=== 4. ЗВІТ ПРО АКТИВНІ ВИДАЧІ (Join) ===");
            foreach (var row in analytics.GetActiveBorrowsReport())
            {
                Console.WriteLine("  " + row);
            }

            Console.WriteLine("\n=== 5. ПАГІНАЦІЯ (Сторінка 2 по 3 елементи: Skip + Take) ===");
            foreach (var item in analytics.GetCatalogPage(pageNumber: 2, pageSize: 3))
            {
                Console.WriteLine($"  • {item.Title} ({item.Year})");
            }

            Console.WriteLine("\n=== 6. ВІДКЛАДЕНЕ ВИКОНАННЯ (Deferred) VS МАТЕРІАЛІЗАЦІЯ (ToList) ===");
            // Запит 1 повертає IEnumerable<Book> — це ВІДКЛАДЕНИЙ запит (він ще не виконався!)
            IEnumerable<Book> deferredModernBooks = analytics.GetAvailableModernBooks(2015);

            // Матеріалізований знімок даних на поточний момент через ToList()
            List<Book> materializedSnapshot = analytics.GetAvailableModernBooks(2015).ToList();

            // Додаємо нову книгу в оригінальну колекцію ВЖЕ ПІСЛЯ створення обох запитів
            catalog.Add(new Book(8, "ASP.NET Core у дії", "Програмування", 2024, "Ендрю Лок", 950));

            Console.WriteLine($"Кількість у матеріалізованому списку (ToList до додавання): {materializedSnapshot.Count}");
            Console.WriteLine($"Кількість у відкладеному запиті (виконується зараз при виклику Count): {deferredModernBooks.Count()}");
            Console.WriteLine("Елементи відкладеного запиту (містять нову книгу 'ASP.NET Core у дії'):");
            foreach (var book in deferredModernBooks)
            {
                Console.WriteLine($"  -> {book.Title} ({book.Year} р.)");
            }
        }
    }
}