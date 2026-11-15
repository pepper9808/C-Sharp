using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DomainModelCentralizedLoggingDemo
{
    // =========================================================
    // 1. ВЛАСНІ ВИНЯТКИ ПРЕДМЕТНОЇ ОБЛАСТІ (Лекція 3)
    // =========================================================
    public abstract class LibraryDomainException : Exception
    {
        protected LibraryDomainException(string message) : base(message) { }
        protected LibraryDomainException(string message, Exception inner) : base(message, inner) { }
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

    public class EntityNotFoundException : LibraryDomainException
    {
        public EntityNotFoundException(string message) : base(message) { }
    }

    // =========================================================
    // 2. РІВНІ ЛОГУВАННЯ ТА ФАЙЛОВИЙ ЛОГЕР
    // =========================================================
    public enum LogLevel
    {
        INFO,
        WARNING,
        ERROR,
        CRITICAL
    }

    public class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _fileLock = new object();

        public string LogFilePath => _logFilePath;

        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("Шлях до лог-файлу не може бути порожнім.", nameof(logFilePath));

            _logFilePath = logFilePath;
        }

        public void LogInfo(string context, string message)
        {
            WriteEntry(LogLevel.INFO, context, message, null);
        }

        public void LogException(LogLevel level, string context, Exception ex)
        {
            WriteEntry(level, context, ex.Message, ex);
        }

        private void WriteEntry(LogLevel level, string context, string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [Операція: {context}]");

            if (ex != null)
            {
                sb.AppendLine($"  Тип помилки : {ex.GetType().FullName}");
                sb.AppendLine($"  Повідомлення: {message}");

                if (ex.InnerException != null)
                {
                    sb.AppendLine($"  Першопричина: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }

                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    sb.AppendLine("  StackTrace  :");
                    sb.AppendLine(ex.StackTrace);
                }
            }
            else
            {
                sb.AppendLine($"  Повідомлення: {message}");
            }

            sb.AppendLine(new string('-', 70));

            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
            }
        }
    }

    // =========================================================
    // 3. РЕЗУЛЬТАТ ТА ЦЕНТРАЛІЗОВАНИЙ ВИКОНАВЕЦЬ (SafeExecutor)
    // =========================================================
    public class ExecutionResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }

        protected ExecutionResult(bool isSuccess, string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }

        public static ExecutionResult Ok() => new ExecutionResult(true, null);
        public static ExecutionResult Fail(string errorMessage) => new ExecutionResult(false, errorMessage);
    }

    public class ExecutionResult<T> : ExecutionResult
    {
        public T Data { get; }

        private ExecutionResult(bool isSuccess, T data, string errorMessage)
            : base(isSuccess, errorMessage)
        {
            Data = data;
        }

        public static ExecutionResult<T> Ok(T data) => new ExecutionResult<T>(true, data, null);
        public new static ExecutionResult<T> Fail(string errorMessage) => new ExecutionResult<T>(false, default, errorMessage);
    }

    // Єдина точка безпечного виконання критичних операцій
    public class SafeExecutor
    {
        private readonly FileLogger _logger;

        public SafeExecutor(FileLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Безпечне виконання операції без повернення значення (Action)
        public ExecutionResult Execute(Action operation, string operationName)
        {
            try
            {
                operation();
                _logger.LogInfo(operationName, "Операцію виконано успішно.");
                return ExecutionResult.Ok();
            }
            catch (LibraryDomainException ex)
            {
                // Очікувані помилки бізнес-логіки або валідації -> WARNING
                _logger.LogException(LogLevel.WARNING, operationName, ex);
                return ExecutionResult.Fail($"Помилка бізнес-правил: {ex.Message}");
            }
            catch (IOException ex)
            {
                // Помилки вводу/виводу -> ERROR
                _logger.LogException(LogLevel.ERROR, operationName, ex);
                return ExecutionResult.Fail($"Помилка роботи з даними/файлами: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Будь-які інші непередбачувані технічні помилки -> CRITICAL
                _logger.LogException(LogLevel.CRITICAL, operationName, ex);
                return ExecutionResult.Fail("Виникла непередбачувана системна помилка. Деталі записано в журнал.");
            }
        }

        // Безпечне виконання операції з поверненням результату (Func<T>)
        public ExecutionResult<T> Execute<T>(Func<T> operation, string operationName)
        {
            try
            {
                T result = operation();
                _logger.LogInfo(operationName, "Операцію виконано успішно.");
                return ExecutionResult<T>.Ok(result);
            }
            catch (LibraryDomainException ex)
            {
                _logger.LogException(LogLevel.WARNING, operationName, ex);
                return ExecutionResult<T>.Fail($"Помилка бізнес-правил: {ex.Message}");
            }
            catch (IOException ex)
            {
                _logger.LogException(LogLevel.ERROR, operationName, ex);
                return ExecutionResult<T>.Fail($"Помилка роботи з даними/файлами: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogException(LogLevel.CRITICAL, operationName, ex);
                return ExecutionResult<T>.Fail("Виникла непередбачувана системна помилка. Деталі записано в журнал.");
            }
        }
    }

    // =========================================================
    // 4. МОДЕЛЬ ТА СЕРВІС ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class Book
    {
        public int Id { get; }
        public string Title { get; }
        public string Author { get; }
        public bool IsBorrowed { get; private set; }
        public string BorrowedBy { get; private set; }

        public Book(int id, string title, string author)
        {
            if (id <= 0)
                throw new DomainValidationException(nameof(id), "Ідентифікатор книги має бути додатним.");
            if (string.IsNullOrWhiteSpace(title))
                throw new DomainValidationException(nameof(title), "Назва книги не може бути порожньою.");
            if (string.IsNullOrWhiteSpace(author))
                throw new DomainValidationException(nameof(author), "Автор книги має бути вказаний.");

            Id = id;
            Title = title;
            Author = author;
        }

        public void Borrow(string readerName)
        {
            if (string.IsNullOrWhiteSpace(readerName))
                throw new DomainValidationException(nameof(readerName), "Ім'я читача не може бути порожнім.");

            if (IsBorrowed)
                throw new BookUnavailableException(Id, $"Книга \"{Title}\" (ID={Id}) вже видана читачу '{BorrowedBy}'.");

            IsBorrowed = true;
            BorrowedBy = readerName;
        }
    }

    public class LibraryService
    {
        private readonly List<Book> _books = new List<Book>();
        private int _nextId = 1;

        public Book AddBook(string title, string author)
        {
            var book = new Book(_nextId++, title, author);
            _books.Add(book);
            return book;
        }

        public void BorrowBook(int bookId, string readerName)
        {
            var book = _books.FirstOrDefault(b => b.Id == bookId)
                ?? throw new EntityNotFoundException($"Книгу з ID={bookId} не знайдено в каталозі.");

            book.Borrow(readerName);
        }

        public int GetBooksCount() => _books.Count;
    }

    // =========================================================
    // 5. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            string logFile = "library_operations.log";
            if (File.Exists(logFile))
                File.Delete(logFile); // Очищаємо старий лог для наочності запуску

            var logger = new FileLogger(logFile);
            var safeExecutor = new SafeExecutor(logger);
            var libraryService = new LibraryService();

            Console.WriteLine("=== ЗАПУСК ОПЕРАЦІЙ ЧЕРЕЗ ЦЕНТРАЛІЗОВАНИЙ SAFE EXECUTOR ===\n");

            // Операція 1: Успішне створення книги
            var addResult1 = safeExecutor.Execute(
                () => libraryService.AddBook("CLR via C#", "Джеффрі Ріхтер"),
                "Реєстрація книги 'CLR via C#'"
            );
            Console.WriteLine($"1. Створення книги: {(addResult1.IsSuccess ? $"УСПІХ (ID={addResult1.Data.Id})" : addResult1.ErrorMessage)}");

            // Операція 2 (Помилка валідації): Порожня назва книги
            var addResult2 = safeExecutor.Execute(
                () => libraryService.AddBook("   ", "Роберт Мартін"),
                "Реєстрація книги з порожньою назвою"
            );
            Console.WriteLine($"2. Створення некоректної книги: {(addResult2.IsSuccess ? "УСПІХ" : addResult2.ErrorMessage)}");

            // Операція 3: Успішна видача книги читачу
            var borrowResult1 = safeExecutor.Execute(
                () => libraryService.BorrowBook(1, "Олександр Коваленко"),
                "Видача книги #1 читачу Олександр Коваленко"
            );
            Console.WriteLine($"3. Перша видача книги #1: {(borrowResult1.IsSuccess ? "УСПІХ" : borrowResult1.ErrorMessage)}");

            // Операція 4 (Порушення бізнес-правила): Повторна видача вже зайнятої книги
            var borrowResult2 = safeExecutor.Execute(
                () => libraryService.BorrowBook(1, "Марія Бойко"),
                "Повторна видача зайнятої книги #1"
            );
            Console.WriteLine($"4. Повторна видача книги #1: {(borrowResult2.IsSuccess ? "УСПІХ" : borrowResult2.ErrorMessage)}");

            // Операція 5 (Помилка пошуку сутності): Видача неіснуючої книги
            var borrowResult3 = safeExecutor.Execute(
                () => libraryService.BorrowBook(999, "Марія Бойко"),
                "Видача неіснуючої книги #999"
            );
            Console.WriteLine($"5. Видача книги #999: {(borrowResult3.IsSuccess ? "УСПІХ" : borrowResult3.ErrorMessage)}");

            Console.WriteLine($"\nПрограма продовжила роботу без аварійного завершення! Книг у каталозі: {libraryService.GetBooksCount()}");

            // Виведення сформованого лог-файлу для перевірки
            Console.WriteLine($"\n=== ВМІСТ СФОРМОВАНОГО ЛОГ-ФАЙЛУ ({Path.GetFullPath(logFile)}) ===\n");
            Console.WriteLine(File.ReadAllText(logFile, Encoding.UTF8));
        }
    }
}