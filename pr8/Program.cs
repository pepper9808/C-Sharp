using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelAsyncDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ ТА DTO
    // =========================================================
    public class Book
    {
        public int Id { get; }
        public string Isbn { get; }
        public string Title { get; }
        public string Author { get; }
        public int Year { get; }
        public bool IsBorrowed { get; set; }

        public Book(int id, string isbn, string title, string author, int year)
        {
            Id = id;
            Isbn = isbn;
            Title = title;
            Author = author;
            Year = year;
        }
    }

    public class ExternalBookMetadata
    {
        public string Isbn { get; set; }
        public string Publisher { get; set; }
        public double Rating { get; set; }
    }

    // Власний виняток для помилок інтеграції з зовнішнім каталогом
    public class ExternalCatalogException : Exception
    {
        public string Isbn { get; }
        public ExternalCatalogException(string isbn, string message) : base(message)
        {
            Isbn = isbn;
        }
    }

    // =========================================================
    // 2. АСИНХРОННИЙ СЕРВІС БІБЛІОТЕКИ (Лекція 9: TAP, async/await)
    // =========================================================
    public class AsyncLibraryService
    {
        private readonly List<Book> _catalog;

        public AsyncLibraryService(List<Book> catalog)
        {
            _catalog = catalog;
        }

        // АСИНХРОННИЙ МЕТОД №1: Асинхронний запис аналітичного звіту у файл
        // Суфікс Async, повертає Task<string> (шлях до збереженого файлу)
        public async Task<string> ExportCatalogReportAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Шлях до файлу звіту не може бути порожнім.", nameof(filePath));

            // Використання async using / using для гарантованого закриття файлового потоку
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                await writer.WriteLineAsync($"=== ЗВІТ БІБЛІОТЕЧНОГО ФОНДУ ({DateTime.Now:dd.MM.yyyy HH:mm:ss}) ===");
                await writer.WriteLineAsync($"Всього примірників: {_catalog.Count} | Видано на руки: {_catalog.Count(b => b.IsBorrowed)}");
                await writer.WriteLineAsync(new string('-', 65));

                foreach (var book in _catalog)
                {
                    // Кооперативна перевірка скасування на кожному кроці
                    cancellationToken.ThrowIfCancellationRequested();

                    // Імітація асинхронної I/O затримки без блокування потоку
                    await Task.Delay(120, cancellationToken);

                    string status = book.IsBorrowed ? "Видано" : "В наявності";
                    string line = $"[#{book.Id}] ISBN: {book.Isbn} | \"{book.Title}\" — {book.Author} ({book.Year}) [{status}]";

                    // Неблокуючий запис рядка у файл
                    await writer.WriteLineAsync(line);
                }

                await writer.FlushAsync();
            }

            return Path.GetFullPath(filePath);
        }

        // АСИНХРОННИЙ МЕТОД №2: Імітація зовнішнього мережевого запиту за ISBN
        public async Task<ExternalBookMetadata> FetchExternalBookMetadataAsync(
            string isbn,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(isbn))
                throw new ArgumentException("ISBN не може бути порожнім.", nameof(isbn));

            // Імітація неблокуючого мережевого очікування (відповідь від зовнішнього API)
            await Task.Delay(350, cancellationToken);

            // Моделювання помилки зовнішнього сервісу для конкретного тестового ISBN
            if (isbn == "000-0-00-000000-0")
            {
                throw new ExternalCatalogException(isbn, $"Зовнішній сервіс повернув 404 Not Found для ISBN '{isbn}'.");
            }

            return new ExternalBookMetadata
            {
                Isbn = isbn,
                Publisher = "O'Reilly / Microsoft Press",
                Rating = 4.9
            };
        }

        // АСИНХРОННИЙ МЕТОД №3: Одночасне виконання кількох асинхронних запитів через Task.WhenAll
        public async Task<List<ExternalBookMetadata>> FetchAllMetadataAsync(
            IEnumerable<string> isbns,
            CancellationToken cancellationToken = default)
        {
            // Запускаємо всі I/O запити одночасно (не послідовно!)
            IEnumerable<Task<ExternalBookMetadata>> tasks = isbns
                .Select(isbn => FetchExternalBookMetadataAsync(isbn, cancellationToken));

            // Очікуємо завершення всіх задач без блокування потоку
            ExternalBookMetadata[] results = await Task.WhenAll(tasks);
            return results.ToList();
        }
    }

    // =========================================================
    // 3. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var catalog = new List<Book>
            {
                new Book(1, "978-0-7356-6745-7", "CLR via C#", "Джеффрі Ріхтер", 2012),
                new Book(2, "978-0-13-235088-4", "Чистий код", "Роберт Мартін", 2008) { IsBorrowed = true },
                new Book(3, "978-1-098-12195-2", "C# 12 у стислому викладі", "Джозеф Албахарі", 2023),
                new Book(4, "978-1-61729-836-3", "ASP.NET Core у дії", "Ендрю Лок", 2024)
            };

            var asyncService = new AsyncLibraryService(catalog);
            string reportFileName = "library_async_report.txt";

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 1: Успішний асинхронний експорт звіту у файл
            // ---------------------------------------------------------
            Console.WriteLine("=== 1. АСИНХРОННИЙ ЕКСПОРТ ЗВІТУ У ФАЙЛ (ExportCatalogReportAsync) ===");
            try
            {
                Console.WriteLine($"[Потік #{Thread.CurrentThread.ManagedThreadId}] Запуск експорту звіту...");
                string savedPath = await asyncService.ExportCatalogReportAsync(reportFileName);
                Console.WriteLine($"[Потік #{Thread.CurrentThread.ManagedThreadId}] Звіт успішно збережено: {savedPath}");

                // Асинхронне читання створеного файлу для перевірки вмісту
                string content = await File.ReadAllTextAsync(savedPath, Encoding.UTF8);
                Console.WriteLine("\nВміст згенерованого файлу:\n" + content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка під час експорту: {ex.Message}");
            }

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 2: Скасування асинхронної операції через CancellationToken
            // ---------------------------------------------------------
            Console.WriteLine("=== 2. СКАСУВАННЯ АСИНХРОННОЇ ОПЕРАЦІЇ (CancellationToken) ===");
            using (var cts = new CancellationTokenSource())
            {
                // Встановлюємо автоматичне скасування через 200 мс (звіт потребує ~480 мс)
                cts.CancelAfter(200);

                try
                {
                    Console.WriteLine("Запуск експорту з жорстким таймаутом 200 мс...");
                    await asyncService.ExportCatalogReportAsync("canceled_report.txt", cts.Token);
                    Console.WriteLine("Експорт завершено.");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[Скасовано]: Операцію експорту коректно перервано через CancellationToken!");
                }
                finally
                {
                    // Очищення недописаного файлу після скасування
                    if (File.Exists("canceled_report.txt"))
                    {
                        File.Delete("canceled_report.txt");
                        Console.WriteLine("[Finally]: Тимчасовий недописаний файл видалено.");
                    }
                }
            }

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 3: Неблокуючі зовнішні запити (Task.WhenAll + try-catch)
            // ---------------------------------------------------------
            Console.WriteLine("\n=== 3. АСИНХРОННІ ЗОВНІШНІ ЗАПИТИ (Task.WhenAll та обробка винятків) ===");

            // 3.1. Коректний пакетний запит
            var validIsbns = catalog.Select(b => b.Isbn);
            var metadataList = await asyncService.FetchAllMetadataAsync(validIsbns);
            Console.WriteLine($"Отримано метадані для {metadataList.Count} книг одночасно:");
            foreach (var meta in metadataList)
            {
                Console.WriteLine($"  • ISBN {meta.Isbn} -> Видавництво: {meta.Publisher}, Рейтинг: {meta.Rating}");
            }

            // 3.2. Обробка винятку в async-методі при некоректному ISBN
            try
            {
                Console.WriteLine("\nЗапит метаданих для неіснуючого ISBN '000-0-00-000000-0'...");
                await asyncService.FetchExternalBookMetadataAsync("000-0-00-000000-0");
            }
            catch (ExternalCatalogException ex)
            {
                Console.WriteLine($"[Перехоплено ExternalCatalogException]: {ex.Message}");
            }
        }
    }
}