using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DomainModelEventSubsystemDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class Book
    {
        public int Id { get; }
        public string Title { get; }
        public string Author { get; }
        public bool IsBorrowed { get; internal set; }
        public string BorrowedBy { get; internal set; }

        public Book(int id, string title, string author)
        {
            Id = id;
            Title = title;
            Author = author;
        }
    }

    public class Reader
    {
        public int Id { get; }
        public string FullName { get; }

        public Reader(int id, string fullName)
        {
            Id = id;
            FullName = fullName;
        }
    }

    // =========================================================
    // 2. КЛАСИ АРГУМЕНТІВ ПОДІЙ (Спадкоємці EventArgs)
    // =========================================================
    public class BookRegisteredEventArgs : EventArgs
    {
        public Book Book { get; }
        public DateTime OccurredAt { get; }

        public BookRegisteredEventArgs(Book book)
        {
            Book = book;
            OccurredAt = DateTime.Now;
        }
    }

    public class BookLoanStatusEventArgs : EventArgs
    {
        public Book Book { get; }
        public Reader Reader { get; }
        public string Operation { get; } // "Видано" або "Повернуто"
        public DateTime OccurredAt { get; }

        public BookLoanStatusEventArgs(Book book, Reader reader, string operation)
        {
            Book = book;
            Reader = reader;
            Operation = operation;
            OccurredAt = DateTime.Now;
        }
    }

    public class RuleViolationEventArgs : EventArgs
    {
        public string RuleName { get; }
        public string Details { get; }
        public DateTime OccurredAt { get; }

        public RuleViolationEventArgs(string ruleName, string details)
        {
            RuleName = ruleName;
            Details = details;
            OccurredAt = DateTime.Now;
        }
    }

    // =========================================================
    // 3. ВИДАВЕЦЬ ПОДІЙ (PUBLISHER): СЕРВІС БІБЛІОТЕКИ
    // =========================================================
    public class LibraryService
    {
        private const int MaxActiveBooksPerReader = 2;

        private readonly List<Book> _books = new List<Book>();
        private readonly List<Reader> _readers = new List<Reader>();
        private int _nextBookId = 1;
        private int _nextReaderId = 1;

        // Оголошення подій за стандартним патерном .NET EventHandler<TEventArgs>
        public event EventHandler<BookRegisteredEventArgs> BookRegistered;
        public event EventHandler<BookLoanStatusEventArgs> BookLoanStatusChanged;
        public event EventHandler<RuleViolationEventArgs> BusinessRuleViolated;

        public Reader RegisterReader(string fullName)
        {
            var reader = new Reader(_nextReaderId++, fullName);
            _readers.Add(reader);
            return reader;
        }

        public Book RegisterBook(string title, string author)
        {
            var book = new Book(_nextBookId++, title, author);
            _books.Add(book);

            // Генерація події створення сутності
            OnBookRegistered(new BookRegisteredEventArgs(book));
            return book;
        }

        public bool BorrowBook(int bookId, int readerId)
        {
            var book = _books.FirstOrDefault(b => b.Id == bookId);
            var reader = _readers.FirstOrDefault(r => r.Id == readerId);

            if (book == null || reader == null)
            {
                OnBusinessRuleViolated(new RuleViolationEventArgs(
                    "EntityExistenceRule",
                    $"Спроба видачі неіснуючої сутності (BookId={bookId}, ReaderId={readerId})."
                ));
                return false;
            }

            // Перевірка правила №1: книга має бути вільною
            if (book.IsBorrowed)
            {
                OnBusinessRuleViolated(new RuleViolationEventArgs(
                    "BookAvailabilityRule",
                    $"Читач {reader.FullName} намагався взяти книгу \"{book.Title}\", яка вже видана ({book.BorrowedBy})."
                ));
                return false;
            }

            // Перевірка правила №2: ліміт книг на одного читача
            int currentCount = _books.Count(b => b.IsBorrowed && b.BorrowedBy == reader.FullName);
            if (currentCount >= MaxActiveBooksPerReader)
            {
                OnBusinessRuleViolated(new RuleViolationEventArgs(
                    "ReaderQuotaRule",
                    $"Читач {reader.FullName} перевищив ліміт ({MaxActiveBooksPerReader} книги) при спробі взяти \"{book.Title}\"."
                ));
                return false;
            }

            // Зміна стану та генерація події виконання операції
            book.IsBorrowed = true;
            book.BorrowedBy = reader.FullName;

            OnBookLoanStatusChanged(new BookLoanStatusEventArgs(book, reader, "Видано"));
            return true;
        }

        public bool ReturnBook(int bookId, int readerId)
        {
            var book = _books.FirstOrDefault(b => b.Id == bookId);
            var reader = _readers.FirstOrDefault(r => r.Id == readerId);

            if (book == null || reader == null || !book.IsBorrowed || book.BorrowedBy != reader.FullName)
            {
                OnBusinessRuleViolated(new RuleViolationEventArgs(
                    "InvalidReturnRule",
                    $"Некоректна спроба повернення книги #{bookId} читачем #{readerId}."
                ));
                return false;
            }

            book.IsBorrowed = false;
            book.BorrowedBy = null;

            // Генерація події зміни статусу
            OnBookLoanStatusChanged(new BookLoanStatusEventArgs(book, reader, "Повернуто"));
            return true;
        }

        // Захищені методи виклику подій із перевіркою на null (?.)
        protected virtual void OnBookRegistered(BookRegisteredEventArgs e) =>
            BookRegistered?.Invoke(this, e);

        protected virtual void OnBookLoanStatusChanged(BookLoanStatusEventArgs e) =>
            BookLoanStatusChanged?.Invoke(this, e);

        protected virtual void OnBusinessRuleViolated(RuleViolationEventArgs e) =>
            BusinessRuleViolated?.Invoke(this, e);
    }

    // =========================================================
    // 4. НЕЗАЛЕЖНІ ПІДПИСНИКИ (SUBSCRIBERS)
    // =========================================================

    // Підписник №1: Формування повідомлень користувачу
    public class UserNotificationSubscriber
    {
        public void NotifyAboutNewBook(object sender, BookRegisteredEventArgs e)
        {
            Console.WriteLine($"  [Повідомлення користувачу]: Новинка в каталозі! \"{e.Book.Title}\" ({e.Book.Author}) вже доступна для замовлення.");
        }

        public void NotifyAboutLoanChange(object sender, BookLoanStatusEventArgs e)
        {
            if (e.Operation == "Видано")
                Console.WriteLine($"  [Квитанція для {e.Reader.FullName}]: Ви взяли книгу \"{e.Book.Title}\". Термін повернення — 14 днів.");
            else
                Console.WriteLine($"  [Квитанція для {e.Reader.FullName}]: Дякуємо за повернення книги \"{e.Book.Title}\"!");
        }

        public void NotifyAboutRejection(object sender, RuleViolationEventArgs e)
        {
            Console.WriteLine($"  [Відмова в операції]: {e.Details}");
        }
    }

    // Підписник №2: Журналювання подій (Аудит-лог)
    public class SystemEventLoggerSubscriber
    {
        private readonly List<string> _logHistory = new List<string>();

        public void LogBookRegistered(object sender, BookRegisteredEventArgs e)
        {
            _logHistory.Add($"[{e.OccurredAt:HH:mm:ss}] INFO: Зареєстровано книгу ID={e.Book.Id} \"{e.Book.Title}\".");
        }

        public void LogLoanStatusChanged(object sender, BookLoanStatusEventArgs e)
        {
            _logHistory.Add($"[{e.OccurredAt:HH:mm:ss}] INFO: Статус книги ID={e.Book.Id} -> '{e.Operation}' (Читач: {e.Reader.FullName}).");
        }

        public void LogRuleViolation(object sender, RuleViolationEventArgs e)
        {
            _logHistory.Add($"[{e.OccurredAt:HH:mm:ss}] WARN [{e.RuleName}]: {e.Details}");
        }

        public void PrintAuditLog()
        {
            Console.WriteLine("\n=== ЖУРНАЛ ПОДІЙ СИСТЕМИ (SystemEventLoggerSubscriber) ===");
            foreach (var entry in _logHistory)
            {
                Console.WriteLine("  " + entry);
            }
        }
    }

    // Підписник №3: Статистичний підрахунок у реальному часі
    public class LibraryAnalyticsSubscriber
    {
        public int BooksAddedCount { get; private set; }
        public int IssuedOperationsCount { get; private set; }
        public int ReturnedOperationsCount { get; private set; }
        public int ViolationsCount { get; private set; }

        public void OnBookAdded(object sender, BookRegisteredEventArgs e) => BooksAddedCount++;

        public void OnLoanChanged(object sender, BookLoanStatusEventArgs e)
        {
            if (e.Operation == "Видано") IssuedOperationsCount++;
            else if (e.Operation == "Повернуто") ReturnedOperationsCount++;
        }

        public void OnViolation(object sender, RuleViolationEventArgs e) => ViolationsCount++;

        public void PrintStatistics()
        {
            Console.WriteLine("\n=== СТАТИСТИКА АКТИВНОСТІ (LibraryAnalyticsSubscriber) ===");
            Console.WriteLine($"  • Зареєстровано книг     : {BooksAddedCount}");
            Console.WriteLine($"  • Успішних видач         : {IssuedOperationsCount}");
            Console.WriteLine($"  • Успішних повернень     : {ReturnedOperationsCount}");
            Console.WriteLine($"  • Зафіксовано порушень   : {ViolationsCount}");
        }
    }

    // =========================================================
    // 5. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var library = new LibraryService();

            // Створюємо незалежних підписників
            var notifier = new UserNotificationSubscriber();
            var logger = new SystemEventLoggerSubscriber();
            var analytics = new LibraryAnalyticsSubscriber();

            // Підписуємо всіх спостерігачів на події видавця (+=)
            library.BookRegistered += notifier.NotifyAboutNewBook;
            library.BookRegistered += logger.LogBookRegistered;
            library.BookRegistered += analytics.OnBookAdded;

            library.BookLoanStatusChanged += notifier.NotifyAboutLoanChange;
            library.BookLoanStatusChanged += logger.LogLoanStatusChanged;
            library.BookLoanStatusChanged += analytics.OnLoanChanged;

            library.BusinessRuleViolated += notifier.NotifyAboutRejection;
            library.BusinessRuleViolated += logger.LogRuleViolation;
            library.BusinessRuleViolated += analytics.OnViolation;

            Console.WriteLine("=== ВИКОНАННЯ РЕАЛЬНИХ ОПЕРАЦІЙ У БІБЛІОТЕЦІ ===\n");

            var reader1 = library.RegisterReader("Олександр Коваленко");
            var reader2 = library.RegisterReader("Марія Бойко");

            // 1. Подія створення сутностей
            var b1 = library.RegisterBook("CLR via C#", "Джеффрі Ріхтер");
            var b2 = library.RegisterBook("Чистий код", "Роберт Мартін");
            var b3 = library.RegisterBook("Патерни проєктування", "Ерік Фрімен");

            Console.WriteLine("\n--- Операції видачі та повернення ---");
            // 2. Успішні видачі
            library.BorrowBook(b1.Id, reader1.Id);
            library.BorrowBook(b2.Id, reader1.Id);

            // 3. Порушення правил (зайнята книга та перевищення ліміту)
            library.BorrowBook(b1.Id, reader2.Id);
            library.BorrowBook(b3.Id, reader1.Id);

            // 4. Повернення книги
            library.ReturnBook(b1.Id, reader1.Id);

            // Підсумковий звіт підписників
            analytics.PrintStatistics();
            logger.PrintAuditLog();
        }
    }
}