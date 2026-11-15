using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelFullAsyncIntegrationDemo
{
    // =========================================================
    // 1. ВЛАСНІ ДОМЕННІ ВИНЯТКИ ТА ВАЛІДАЦІЯ (Лекції 2, 3)
    // =========================================================
    public abstract class LibraryDomainException : Exception
    {
        protected LibraryDomainException(string message) : base(message) { }
    }

    public class DomainValidationException : LibraryDomainException
    {
        public string FieldName { get; }
        public DomainValidationException(string fieldName, string message) : base(message)
        {
            FieldName = fieldName;
        }
    }

    public class BookUnavailableException : LibraryDomainException
    {
        public int BookId { get; }
        public BookUnavailableException(int bookId, string message) : base(message)
        {
            BookId = bookId;
        }
    }

    public static class ValidationHelper
    {
        public static void ValidateNotEmpty(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' не може бути порожнім.");
        }

        public static void ValidatePositive(int value, string fieldName)
        {
            if (value <= 0)
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' має бути більшим за 0.");
        }
    }

    // =========================================================
    // 2. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ ТА РЕПОЗИТОРІЙ (Лекції 1, 2)
    // =========================================================
    public abstract class Entity
    {
        public int Id { get; }

        protected Entity(int id)
        {
            ValidationHelper.ValidatePositive(id, nameof(Id));
            Id = id;
        }
    }

    public class Book : Entity
    {
        private string _title;
        private string _author;

        public string Title
        {
            get => _title;
            set
            {
                ValidationHelper.ValidateNotEmpty(value, nameof(Title));
                _title = value;
            }
        }

        public string Author
        {
            get => _author;
            set
            {
                ValidationHelper.ValidateNotEmpty(value, nameof(Author));
                _author = value;
            }
        }

        public string Category { get; }
        public int Pages { get; }
        public bool IsBorrowed { get; private set; }
        public int? BorrowedByReaderId { get; private set; }

        public Book(int id, string title, string author, string category, int pages)
            : base(id)
        {
            Title = title;
            Author = author;
            ValidationHelper.ValidateNotEmpty(category, nameof(category));
            ValidationHelper.ValidatePositive(pages, nameof(pages));

            Category = category;
            Pages = pages;
        }

        public void Borrow(int readerId)
        {
            if (IsBorrowed)
                throw new BookUnavailableException(Id, $"Книга \"{Title}\" (ID={Id}) вже видана читачу #{BorrowedByReaderId}.");

            IsBorrowed = true;
            BorrowedByReaderId = readerId;
        }
    }

    public class Reader : Entity
    {
        private string _fullName;

        public string FullName
        {
            get => _fullName;
            set
            {
                ValidationHelper.ValidateNotEmpty(value, nameof(FullName));
                _fullName = value;
            }
        }

        public Reader(int id, string fullName) : base(id)
        {
            FullName = fullName;
        }
    }

    public class Repository<T> where T : Entity
    {
        private readonly List<T> _items = new List<T>();

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items.Add(item);
        }

        public T GetById(int id) => _items.FirstOrDefault(x => x.Id == id);

        public List<T> GetAll() => new List<T>(_items);
    }

    // =========================================================
    // 3. АСИНХРОННИЙ ФАЙЛОВИЙ ЛОГЕР (SemaphoreSlim + StreamWriter)
    // =========================================================
    public class AsyncFileLogger
    {
        private readonly string _logFilePath;
        // SemaphoreSlim використовується замість lock для синхронізації навколо await
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public string LogFilePath => _logFilePath;

        public AsyncFileLogger(string logFilePath)
        {
            ValidationHelper.ValidateNotEmpty(logFilePath, nameof(logFilePath));
            _logFilePath = logFilePath;
        }

        public async Task LogAsync(string level, string message, CancellationToken cancellationToken = default)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-8}] {message}";

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                using (var writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
                {
                    await writer.WriteLineAsync(entry);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    // =========================================================
    // 4. АСИНХРОННИЙ СЕРВІС ЗВІТНОСТІ НА БАЗІ LINQ (Лекції 6, 9)
    // =========================================================
    public class AsyncLibraryReportService
    {
        public async Task<string> GenerateAndSaveAnalyticsReportAsync(
            Repository<Book> catalog,
            string reportFilePath,
            CancellationToken cancellationToken = default)
        {
            var books = catalog.GetAll();

            // LINQ-аналітика: групування за категоріями та агрегатні метрики
            var categoryStats = books
                .GroupBy(b => b.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Count(),
                    Borrowed = g.Count(b => b.IsBorrowed),
                    AvgPages = g.Average(b => b.Pages)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            using (var writer = new StreamWriter(reportFilePath, append: false, Encoding.UTF8))
            {
                await writer.WriteLineAsync($"=== АНАЛІТИЧНИЙ ЗВІТ БІБЛІОТЕКИ ({DateTime.Now:dd.MM.yyyy HH:mm:ss}) ===");
                await writer.WriteLineAsync($"Всього книг: {books.Count} | Видано: {books.Count(b => b.IsBorrowed)} | Загальний обсяг: {books.Sum(b => b.Pages)} стор.");
                await writer.WriteLineAsync(new string('-', 65));

                foreach (var stat in categoryStats)
                {
                    // Кооперативна перевірка скасування перед кожним блоком запису
                    cancellationToken.ThrowIfCancellationRequested();

                    // Імітація тривалого I/O формування розділу звіту
                    await Task.Delay(120, cancellationToken);

                    await writer.WriteLineAsync(
                        $"Категорія: {stat.Category,-18} | Книг: {stat.Total} | Видано: {stat.Borrowed} | Сер. сторінок: {stat.AvgPages:F0}");
                }
            }

            return Path.GetFullPath(reportFilePath);
        }
    }

    // =========================================================
    // 5. ГОЛОВНИЙ СЕРВІС БІБЛІОТЕКИ (ПОДІЇ + АСИНХРОННІ ОПЕРАЦІЇ)
    // =========================================================
    public class BookIssuedEventArgs : EventArgs
    {
        public Book Book { get; }
        public Reader Reader { get; }
        public BookIssuedEventArgs(Book book, Reader reader)
        {
            Book = book;
            Reader = reader;
        }
    }

    public class LibraryService
    {
        private readonly Repository<Book> _books;
        private readonly Repository<Reader> _readers;
        private readonly AsyncFileLogger _logger;

        public event EventHandler<BookIssuedEventArgs> BookIssued;

        public LibraryService(Repository<Book> books, Repository<Reader> readers, AsyncFileLogger logger)
        {
            _books = books;
            _readers = readers;
            _logger = logger;
        }

        // Асинхронний бізнес-сценарій видачі книги з валідацією, логуванням та подією
        public async Task IssueBookAsync(int bookId, int readerId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Імітація асинхронної перевірки читацького квитка у зовнішньому реєстрі
                await Task.Delay(100, cancellationToken);

                var reader = _readers.GetById(readerId)
                    ?? throw new DomainValidationException(nameof(readerId), $"Читача #{readerId} не знайдено.");

                var book = _books.GetById(bookId)
                    ?? throw new DomainValidationException(nameof(bookId), $"Книгу #{bookId} не знайдено.");

                book.Borrow(reader.Id);

                // Генерація події для підписників
                BookIssued?.Invoke(this, new BookIssuedEventArgs(book, reader));

                // Асинхронне логування успішної операції
                await _logger.LogAsync("INFO", $"Книгу \"{book.Title}\" (ID={book.Id}) видано читачу {reader.FullName}.", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await _logger.LogAsync("WARNING", $"Операцію видачі книги #{bookId} скасовано за запитом користувача.");
                throw;
            }
            catch (LibraryDomainException ex)
            {
                await _logger.LogAsync("WARNING", $"Відмова у видачі (бізнес-правило): {ex.Message}");
                throw;
            }
        }
    }

    // =========================================================
    // 6. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ІНТЕГРАЦІЇ
    // =========================================================
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            string logPath = "integrated_library.log";
            string reportPath = "integrated_analytics_report.txt";
            if (File.Exists(logPath)) File.Delete(logPath);

            // Ініціалізація всіх підсистем проєкту
            var bookRepo = new Repository<Book>();
            var readerRepo = new Repository<Reader>();
            var logger = new AsyncFileLogger(logPath);
            var reportService = new AsyncLibraryReportService();
            var libraryService = new LibraryService(bookRepo, readerRepo, logger);

            int eventsHandledCount = 0;
            // Підписник на подію видачі книги
            libraryService.BookIssued += (sender, e) =>
            {
                eventsHandledCount++;
                Console.WriteLine($"  [Подія BookIssued #{eventsHandledCount}]: Сповіщення для {e.Reader.FullName} — отримано \"{e.Book.Title}\".");
            };

            // Наповнення репозиторіїв (із валідацією)
            bookRepo.Add(new Book(1, "CLR via C#", "Джеффрі Ріхтер", "Програмування", 896));
            bookRepo.Add(new Book(2, "Чистий код", "Роберт Мартін", "Програмування", 464));
            bookRepo.Add(new Book(3, "Патерни проєктування", "Ерік Фрімен", "Архітектура ПЗ", 640));
            bookRepo.Add(new Book(4, "Проєктування високонавантажених систем", "Мартін Клеппман", "Бази даних", 610));

            readerRepo.Add(new Reader(1, "Олександр Коваленко"));
            readerRepo.Add(new Reader(2, "Марія Бойко"));

            Console.WriteLine("=== 1. АСИНХРОННІ БІЗНЕС-ОПЕРАЦІЇ (Репозиторій + Події + Логування) ===");
            await libraryService.IssueBookAsync(bookId: 1, readerId: 1);
            await libraryService.IssueBookAsync(bookId: 3, readerId: 2);

            Console.WriteLine("\n=== 2. ОБРОБКА ВИНЯТКУ В ASYNC-МЕТОДІ (Повторна видача зайнятої книги) ===");
            try
            {
                await libraryService.IssueBookAsync(bookId: 1, readerId: 2);
            }
            catch (BookUnavailableException ex)
            {
                Console.WriteLine($"  [Перехоплено виняток]: {ex.Message}");
            }

            Console.WriteLine("\n=== 3. СКАСУВАННЯ АСИНХРОННОГО ЗВІТУ ЧЕРЕЗ CANCELLATION TOKEN ===");
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(150); // Скасовуємо формування звіту до завершення всіх 3 категорій
                try
                {
                    await reportService.GenerateAndSaveAnalyticsReportAsync(bookRepo, "canceled_report.txt", cts.Token);
                }
                catch (OperationCanceledException)
                {
                    await logger.LogAsync("WARNING", "Формування аналітичного звіту скасовано через CancellationToken.");
                    Console.WriteLine("  [Скасовано]: Експорт звіту коректно перервано через CancellationToken.");
                }
                finally
                {
                    if (File.Exists("canceled_report.txt"))
                        File.Delete("canceled_report.txt");
                }
            }

            Console.WriteLine("\n=== 4. УСПІШНИЙ АСИНХРОННИЙ ЕКСПОРТ ЗВІТУ (LINQ + File I/O) ===");
            string fullReportPath = await reportService.GenerateAndSaveAnalyticsReportAsync(bookRepo, reportPath);
            await logger.LogAsync("INFO", $"Аналітичний звіт успішно збережено у {fullReportPath}.");

            Console.WriteLine(await File.ReadAllTextAsync(fullReportPath, Encoding.UTF8));

            Console.WriteLine("=== 5. ПЕРЕВІРКА СФОРМОВАНОГО ЖУРНАЛУ ПОДІЙ (AsyncFileLogger) ===");
            Console.WriteLine(await File.ReadAllTextAsync(logPath, Encoding.UTF8));
        }
    }
}