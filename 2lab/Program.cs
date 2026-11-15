using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DomainModelReportingDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class Book
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public int Year { get; set; }
        public int Pages { get; set; }
        public bool IsBorrowed { get; set; }
        public string BorrowedBy { get; set; }

        public Book() { } // Потрібен для десеріалізації з JSON

        public Book(int id, string title, string author, int year, int pages)
        {
            Id = id;
            Title = title;
            Author = author;
            Year = year;
            Pages = pages;
            IsBorrowed = false;
            BorrowedBy = null;
        }

        public void Borrow(string readerName)
        {
            IsBorrowed = true;
            BorrowedBy = readerName;
        }
    }

    // =========================================================
    // 2. DTO-МОДЕЛЬ ЗВІТУ ПРО СТАН СИСТЕМИ
    // =========================================================
    public class BookReportItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public int Year { get; set; }
        public int Pages { get; set; }
        public string Status { get; set; }
        public string Holder { get; set; }
    }

    public class LibrarySystemReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalBooks { get; set; }
        public int AvailableBooks { get; set; }
        public int BorrowedBooks { get; set; }
        public int TotalPages { get; set; }
        public List<BookReportItem> Items { get; set; } = new List<BookReportItem>();
    }

    // =========================================================
    // 3. ОКРЕМИЙ МОДУЛЬ ФОРМУВАННЯ ЗВІТНОСТІ ТА ЕКСПОРТУ ДАНИХ
    // =========================================================
    public class LibraryReportExporter
    {
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Коректне відображення кирилиці
        };

        // Формування об'єкта звіту на основі поточного стану каталогу
        public LibrarySystemReport BuildReport(IEnumerable<Book> catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var booksList = catalog.ToList();

            return new LibrarySystemReport
            {
                GeneratedAt = DateTime.Now,
                TotalBooks = booksList.Count,
                AvailableBooks = booksList.Count(b => !b.IsBorrowed),
                BorrowedBooks = booksList.Count(b => b.IsBorrowed),
                TotalPages = booksList.Sum(b => b.Pages),
                Items = booksList.Select(b => new BookReportItem
                {
                    Id = b.Id,
                    Title = b.Title,
                    Author = b.Author,
                    Year = b.Year,
                    Pages = b.Pages,
                    Status = b.IsBorrowed ? "Видано" : "В наявності",
                    Holder = b.IsBorrowed ? b.BorrowedBy : "-"
                }).ToList()
            };
        }

        // ЕКСПОРТ У ФОРМАТ JSON
        public void ExportToJson(LibrarySystemReport report, string filePath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            ValidateFilePath(filePath);

            try
            {
                string jsonContent = JsonSerializer.Serialize(report, _jsonOptions);
                File.WriteAllText(filePath, jsonContent, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Відмовлено у доступі при записі JSON-файлу '{filePath}'.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new IOException($"Директорію для збереження файлу '{filePath}' не знайдено.", ex);
            }
        }

        // ПОВТОРНЕ ЗЧИТУВАННЯ ТА ПЕРЕВІРКА КОРЕКТНОСТІ JSON-ЗВІТУ
        public LibrarySystemReport ImportAndVerifyJsonReport(string filePath)
        {
            ValidateFilePath(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл звіту не знайдено за шляхом: '{filePath}'.", filePath);

            string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new InvalidDataException("Файл звіту порожній.");

            LibrarySystemReport report;
            try
            {
                report = JsonSerializer.Deserialize<LibrarySystemReport>(jsonContent, _jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Помилка структури JSON у файлі '{filePath}': {ex.Message}", ex);
            }

            if (report == null || report.Items == null)
                throw new InvalidDataException("Звіт не містить обов'язкових даних.");

            // Перевірка цілісності агрегованих показників
            if (report.TotalBooks != report.Items.Count)
            {
                throw new InvalidDataException(
                    $"Порушення цілісності звіту: заявлено TotalBooks={report.TotalBooks}, але фактично записів {report.Items.Count}.");
            }

            return report;
        }

        // ЕКСПОРТ У ФОРМАТ CSV
        public void ExportToCsv(LibrarySystemReport report, string filePath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            ValidateFilePath(filePath);

            // Використання using гарантує закриття StreamWriter навіть при помилці
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // Заголовок таблиці
                writer.WriteLine("Id;Title;Author;Year;Pages;Status;Holder");

                foreach (var item in report.Items)
                {
                    string safeTitle = EscapeCsvField(item.Title);
                    string safeAuthor = EscapeCsvField(item.Author);
                    string safeHolder = EscapeCsvField(item.Holder);

                    writer.WriteLine($"{item.Id};{safeTitle};{safeAuthor};{item.Year};{item.Pages};{item.Status};{safeHolder}");
                }

                // Підсумковий рядок статистики
                writer.WriteLine($"# SUMMARY: GeneratedAt={report.GeneratedAt:yyyy-MM-dd HH:mm:ss}; Total={report.TotalBooks}; Available={report.AvailableBooks}; Borrowed={report.BorrowedBooks}; Pages={report.TotalPages}");
            }
        }

        // ПЕРЕВІРКА КОРЕКТНОСТІ CSV-ФАЙЛУ
        public int VerifyCsvReport(string filePath)
        {
            ValidateFilePath(filePath);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV-файл не знайдено: '{filePath}'.", filePath);

            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length < 2)
                throw new InvalidDataException("CSV-файл не містить даних окрім заголовка.");

            if (lines[0] != "Id;Title;Author;Year;Pages;Status;Holder")
                throw new InvalidDataException("Некоректний заголовок колонок у CSV-файлі.");

            int validRecordsCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("# SUMMARY:"))
                    continue;

                string[] columns = lines[i].Split(';');
                if (columns.Length != 7 || !int.TryParse(columns[0], out _))
                    throw new InvalidDataException($"Пошкоджений рядок #{i + 1} у CSV-звіті: '{lines[i]}'");

                validRecordsCount++;
            }

            return validRecordsCount;
        }

        private static void ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Шлях до файлу не може бути порожнім.", nameof(filePath));
        }

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(";") || value.Contains("\""))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
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

            // Підготовка даних предметної області
            var catalog = new List<Book>
            {
                new Book(1, "CLR via C#", "Джеффрі Ріхтер", 2012, 896),
                new Book(2, "Чистий код", "Роберт Мартін", 2008, 464),
                new Book(3, "Патерни проєктування", "Ерік Фрімен", 2020, 640),
                new Book(4, "ASP.NET Core у дії", "Ендрю Лок", 2024, 950)
            };

            catalog[0].Borrow("Олександр Коваленко");
            catalog[2].Borrow("Марія Бойко");

            var exporter = new LibraryReportExporter();
            var report = exporter.BuildReport(catalog);

            string jsonPath = "library_report.json";
            string csvPath = "library_report.csv";

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 1: Успішний експорт та верифікація JSON-звіту
            // ---------------------------------------------------------
            Console.WriteLine("=== 1. ЕКСПОРТ ТА ПЕРЕВІРКА JSON-ЗВІТУ ===");
            exporter.ExportToJson(report, jsonPath);
            Console.WriteLine($"Звіт успішно збережено у файл: {Path.GetFullPath(jsonPath)}");

            var verifiedJson = exporter.ImportAndVerifyJsonReport(jsonPath);
            Console.WriteLine($"[Верифікація JSON успішна]: Дата={verifiedJson.GeneratedAt:dd.MM.yyyy HH:mm:ss}, " +
                              $"Всього={verifiedJson.TotalBooks}, Вільних={verifiedJson.AvailableBooks}, Видано={verifiedJson.BorrowedBooks}");

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 2: Успішний експорт та верифікація CSV-звіту
            // ---------------------------------------------------------
            Console.WriteLine("\n=== 2. ЕКСПОРТ ТА ПЕРЕВІРКА CSV-ЗВІТУ ===");
            exporter.ExportToCsv(report, csvPath);
            int csvRows = exporter.VerifyCsvReport(csvPath);
            Console.WriteLine($"[Верифікація CSV успішна]: Перевірено коректних записів сутностей: {csvRows}");
            Console.WriteLine("Вміст CSV-файлу:");
            Console.WriteLine(File.ReadAllText(csvPath, Encoding.UTF8));

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 3: Моделювання помилок файлового I/O
            // ---------------------------------------------------------
            Console.WriteLine("=== 3. ОБРОБКА ПОМИЛОК ФАЙЛОВОГО I/O ===");

            // Помилка 3.1: Спроба прочитати неіснуючий файл
            try
            {
                exporter.ImportAndVerifyJsonReport("non_existent_report.json");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"[Перехоплено FileNotFoundException]: {ex.Message}");
            }

            // Помилка 3.2: Спроба запису у неіснуючу директорію
            try
            {
                string invalidDirPath = Path.Combine("Z_NonExistentFolder_999", "report.json");
                exporter.ExportToJson(report, invalidDirPath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[Перехоплено IOException]: {ex.Message}");
            }

            // Помилка 3.3: Спроба верифікації пошкодженого (невалідного) JSON-файлу
            string corruptedFile = "corrupted_report.json";
            try
            {
                File.WriteAllText(corruptedFile, "{ \"TotalBooks\": 999, \"Items\": [ { broken json ");
                exporter.ImportAndVerifyJsonReport(corruptedFile);
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"[Перехоплено InvalidDataException]: {ex.Message}");
            }
            finally
            {
                // Гарантоване прибирання тимчасового пошкодженого файлу
                if (File.Exists(corruptedFile))
                    File.Delete(corruptedFile);
            }
        }
    }
}