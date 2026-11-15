using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainModelServiceLayerDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (ТРИ СУТНОСТІ)
    // =========================================================
    public abstract class Entity
    {
        public int Id { get; }

        protected Entity(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Ідентифікатор має бути додатним.");
            Id = id;
        }

        public abstract void DisplayInfo();
    }

    public abstract class LibraryItem : Entity
    {
        private string _title;
        private int _year;

        public bool IsBorrowed { get; internal set; }

        public string Title
        {
            get => _title;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Назва видання не може бути порожньою.");
                _title = value;
            }
        }

        public int Year
        {
            get => _year;
            set
            {
                if (value < 1450 || value > DateTime.Now.Year)
                    throw new ArgumentException("Некоректний рік видання.");
                _year = value;
            }
        }

        protected LibraryItem(int id, string title, int year) : base(id)
        {
            Title = title;
            Year = year;
        }
    }

    // Сутність №1: Книга
    public class Book : LibraryItem
    {
        private string _author;

        public string Author
        {
            get => _author;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Автор має бути вказаний.");
                _author = value;
            }
        }

        public Book(int id, string title, string author, int year)
            : base(id, title, year)
        {
            Author = author;
        }

        public override void DisplayInfo()
        {
            string status = IsBorrowed ? "Видано" : "В наявності";
            Console.WriteLine($"  [Книга #{Id}] \"{Title}\" — {Author} ({Year} р.) | {status}");
        }
    }

    // Сутність №2: Читач
    public class Reader : Entity
    {
        private string _fullName;
        private string _email;

        public string FullName
        {
            get => _fullName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("ПІБ читача не може бути порожнім.");
                _fullName = value;
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || !value.Contains("@"))
                    throw new ArgumentException("Некоректна електронна адреса.");
                _email = value;
            }
        }

        public Reader(int id, string fullName, string email) : base(id)
        {
            FullName = fullName;
            Email = email;
        }

        public override void DisplayInfo()
        {
            Console.WriteLine($"  [Читач #{Id}] {FullName} ({Email})");
        }
    }

    // Сутність №3: Запис про видачу (зв'язує Читача та Книгу)
    public class LoanRecord : Entity
    {
        public int BookId { get; }
        public int ReaderId { get; }
        public DateTime IssuedAt { get; }
        public DateTime DueDate { get; }
        public DateTime? ReturnedAt { get; private set; }
        public bool IsActive => !ReturnedAt.HasValue;

        public LoanRecord(int id, int bookId, int readerId, int loanDays = 14) : base(id)
        {
            BookId = bookId;
            ReaderId = readerId;
            IssuedAt = DateTime.Now;
            DueDate = IssuedAt.AddDays(loanDays);
        }

        public void CompleteReturn()
        {
            if (!IsActive)
                throw new InvalidOperationException("Ця видача вже закрита.");
            ReturnedAt = DateTime.Now;
        }

        public override void DisplayInfo()
        {
            string state = IsActive
                ? $"Активна (повернути до {DueDate:dd.MM.yyyy})"
                : $"Закрита (повернено {ReturnedAt:dd.MM.yyyy HH:mm})";
            Console.WriteLine($"  [Формуляр #{Id}] Книга #{BookId} -> Читач #{ReaderId} | {state}");
        }
    }

    // =========================================================
    // 2. СЕРВІСНИЙ КЛАС ПРЕДМЕТНОЇ ОБЛАСТІ
    // =========================================================
    public class LibraryService
    {
        private const int MaxBooksPerReader = 2; // Бізнес-обмеження: максимум 2 книги на руки

        private readonly List<Book> _books = new List<Book>();
        private readonly List<Reader> _readers = new List<Reader>();
        private readonly List<LoanRecord> _loans = new List<LoanRecord>();

        private int _nextBookId = 1;
        private int _nextReaderId = 1;
        private int _nextLoanId = 1;

        // --- CREATE (Створення сутностей) ---
        public Book RegisterBook(string title, string author, int year)
        {
            var book = new Book(_nextBookId++, title, author, year);
            _books.Add(book);
            return book;
        }

        public Reader RegisterReader(string fullName, string email)
        {
            // Бізнес-перевірка унікальності email
            if (_readers.Any(r => r.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Читач з email '{email}' вже зареєстрований.");

            var reader = new Reader(_nextReaderId++, fullName, email);
            _readers.Add(reader);
            return reader;
        }

        // --- READ (Пошук за ID та атрибутами) ---
        public Book GetBookById(int id) => _books.FirstOrDefault(b => b.Id == id);

        public Reader GetReaderById(int id) => _readers.FirstOrDefault(r => r.Id == id);

        public List<Book> SearchBooks(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<Book>(_books);

            return _books
                .Where(b => b.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            b.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<Book> GetAllBooks() => new List<Book>(_books);
        public List<Reader> GetAllReaders() => new List<Reader>(_readers);
        public List<LoanRecord> GetAllLoans() => new List<LoanRecord>(_loans);

        // --- UPDATE (Оновлення сутностей) ---
        public void UpdateBookInfo(int bookId, string newTitle, string newAuthor, int newYear)
        {
            var book = GetBookById(bookId)
                ?? throw new KeyNotFoundException($"Книгу з ID={bookId} не знайдено.");

            book.Title = newTitle;
            book.Author = newAuthor;
            book.Year = newYear;
        }

        // --- DELETE (Видалення з перевіркою бізнес-обмежень) ---
        public void DeleteBook(int bookId)
        {
            var book = GetBookById(bookId)
                ?? throw new KeyNotFoundException($"Книгу з ID={bookId} не знайдено.");

            // Бізнес-обмеження: не можна списати книгу, яка зараз на руках у читача
            if (book.IsBorrowed)
                throw new InvalidOperationException($"Неможливо видалити книгу \"{book.Title}\", оскільки вона зараз видана читачу.");

            _books.Remove(book);
        }

        // --- КОМПЛЕКСНИЙ СЦЕНАРІЙ ВЗАЄМОДІЇ ТРЬОХ СУТНОСТЕЙ ---
        // Пов'язує Reader + Book -> створює LoanRecord
        public LoanRecord IssueBookToReader(int bookId, int readerId, int loanDays = 14)
        {
            var book = GetBookById(bookId)
                ?? throw new KeyNotFoundException($"Книгу з ID={bookId} не знайдено.");

            var reader = GetReaderById(readerId)
                ?? throw new KeyNotFoundException($"Читача з ID={readerId} не знайдено.");

            // Бізнес-обмеження 1: книга має бути вільною
            if (book.IsBorrowed)
                throw new InvalidOperationException($"Книга \"{book.Title}\" вже видана іншому читачу.");

            // Бізнес-обмеження 2: перевірка ліміту книг на одного читача
            int activeReaderLoans = _loans.Count(l => l.ReaderId == readerId && l.IsActive);
            if (activeReaderLoans >= MaxBooksPerReader)
                throw new InvalidOperationException($"Читач {reader.FullName} досяг ліміту ({MaxBooksPerReader} активні книги).");

            // Зміна стану сутностей та створення зв'язку
            book.IsBorrowed = true;
            var loan = new LoanRecord(_nextLoanId++, book.Id, reader.Id, loanDays);
            _loans.Add(loan);

            return loan;
        }

        public void ReturnBookFromReader(int loanId)
        {
            var loan = _loans.FirstOrDefault(l => l.Id == loanId)
                ?? throw new KeyNotFoundException($"Формуляр видачі #{loanId} не знайдено.");

            if (!loan.IsActive)
                throw new InvalidOperationException($"Формуляр #{loanId} вже закрито раніше.");

            var book = GetBookById(loan.BookId);
            if (book != null)
            {
                book.IsBorrowed = false;
            }

            loan.CompleteReturn();
        }
    }

    // =========================================================
    // 3. ТОЧКА ВХОДУ (Тільки виклики сервісу та вивід)
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var libraryService = new LibraryService();

            // 1. Створення сутностей через сервіс (Create)
            Console.WriteLine("=== 1. РЕЄСТРАЦІЯ КНИГ ТА ЧИТАЧІВ ===");
            var b1 = libraryService.RegisterBook("CLR via C#", "Джеффрі Ріхтер", 2012);
            var b2 = libraryService.RegisterBook("Чистий код", "Роберт Мартін", 2008);
            var b3 = libraryService.RegisterBook("Патерни проєктування", "Ерік Фрімен", 2020);

            var r1 = libraryService.RegisterReader("Олександр Коваленко", "o.kovalenko@edu.ua");
            var r2 = libraryService.RegisterReader("Марія Бойко", "m.boiko@edu.ua");

            libraryService.GetAllBooks().ForEach(b => b.DisplayInfo());
            libraryService.GetAllReaders().ForEach(r => r.DisplayInfo());

            // 2. Пошук та оновлення (Read & Update)
            Console.WriteLine("\n=== 2. ПОШУК ЗА АТРИБУТОМ ТА ОНОВЛЕННЯ ===");
            libraryService.UpdateBookInfo(b2.Id, "Чистий код: створення і рефакторинг", "Роберт Мартін", 2008);
            var found = libraryService.SearchBooks("Роберт");
            Console.WriteLine("Результати пошуку за запитом 'Роберт':");
            found.ForEach(b => b.DisplayInfo());

            // 3. Сценарій взаємодії 3 сутностей (Reader + Book + LoanRecord)
            Console.WriteLine("\n=== 3. ОФОРМЛЕННЯ ВИДАЧІ КНИГ (ВЗАЄМОДІЯ 3 СУТНОСТЕЙ) ===");
            var loan1 = libraryService.IssueBookToReader(b1.Id, r1.Id);
            var loan2 = libraryService.IssueBookToReader(b2.Id, r1.Id);
            Console.WriteLine("Оформлено дві видачі для читача #1:");
            loan1.DisplayInfo();
            loan2.DisplayInfo();

            // 4. Перевірка бізнес-обмежень у сервісі
            Console.WriteLine("\n=== 4. ПЕРЕВІРКА БІЗНЕС-ОБМЕЖЕНЬ ===");
            try
            {
                // Спроба видати 3-тю книгу читачу при ліміті 2
                libraryService.IssueBookToReader(b3.Id, r1.Id);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[Бізнес-правило спрацювало]: {ex.Message}");
            }

            try
            {
                // Спроба видалити книгу, яка зараз на руках
                libraryService.DeleteBook(b1.Id);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[Бізнес-правило спрацювало]: {ex.Message}");
            }

            // 5. Повернення книги та видалення (Delete)
            Console.WriteLine("\n=== 5. ПОВЕРНЕННЯ КНИГИ ТА ВИДАЛЕННЯ З КАТАЛОГУ ===");
            libraryService.ReturnBookFromReader(loan1.Id);
            libraryService.DeleteBook(b1.Id);
            Console.WriteLine("Книгу #1 повернуто за формуляром та успішно списано з фонду.");

            Console.WriteLine("\nАктуальний стан формулярів:");
            libraryService.GetAllLoans().ForEach(l => l.DisplayInfo());
        }
    }
}