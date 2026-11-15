using System;
using System.Collections.Generic;

namespace DomainModelExceptionsDemo
{
    // =========================================================
    // 1. ВЛАСНІ ДОМЕННІ ВИНЯТКИ (Лекція 3)
    // =========================================================

    // Базовий виняток предметної області
    public abstract class LibraryDomainException : Exception
    {
        protected LibraryDomainException() { }
        protected LibraryDomainException(string message) : base(message) { }
        protected LibraryDomainException(string message, Exception inner) : base(message, inner) { }
    }

    // Виняток №1: Помилка валідації даних сутності
    public class DomainValidationException : LibraryDomainException
    {
        public string FieldName { get; }

        public DomainValidationException() { }
        public DomainValidationException(string message) : base(message) { }
        public DomainValidationException(string message, Exception inner) : base(message, inner) { }

        public DomainValidationException(string fieldName, string message)
            : base(message)
        {
            FieldName = fieldName;
        }
    }

    // Виняток №2: Порушення бізнес-правила (видання недоступне або перевищено ліміт)
    public class BookUnavailableException : LibraryDomainException
    {
        public int ItemId { get; }

        public BookUnavailableException() { }
        public BookUnavailableException(string message) : base(message) { }
        public BookUnavailableException(string message, Exception inner) : base(message, inner) { }

        public BookUnavailableException(int itemId, string message)
            : base(message)
        {
            ItemId = itemId;
        }
    }

    // =========================================================
    // 2. FAULT-TOLERANT ПАТЕРН: Result<T>
    // =========================================================
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public string ErrorMessage { get; }

        private Result(bool isSuccess, T value, string errorMessage)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
        }

        public static Result<T> Success(T value) => new Result<T>(true, value, null);
        public static Result<T> Failure(string errorMessage) => new Result<T>(false, default, errorMessage);
    }

    // =========================================================
    // 3. СТАТИЧНІ МОДУЛІ ГЕНЕРАЦІЇ ID ТА ВАЛІДАЦІЇ
    // =========================================================
    public static class IdGenerator
    {
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        private static readonly object _syncLock = new object();

        public static int NextId<T>() where T : Entity
        {
            string key = typeof(T).Name;
            lock (_syncLock)
            {
                if (!_counters.ContainsKey(key))
                    _counters[key] = 0;
                return ++_counters[key];
            }
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
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' має бути більшим за 0 (отримано: {value}).");
        }

        public static void ValidateRange(int value, int min, int max, string fieldName)
        {
            if (value < min || value > max)
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' має бути в межах [{min}..{max}] (отримано: {value}).");
        }
    }

    // =========================================================
    // 4. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ
    // =========================================================
    public abstract class Entity
    {
        public readonly int Id;

        protected Entity(int id)
        {
            ValidationHelper.ValidatePositive(id, nameof(Id));
            Id = id;
        }

        public abstract void DisplayInfo();
    }

    public abstract class LibraryItem : Entity
    {
        private string _title;
        private int _year;

        public bool IsBorrowed { get; private set; }
        public int? BorrowedByReaderId { get; private set; }

        public string Title
        {
            get => _title;
            set
            {
                ValidationHelper.ValidateNotEmpty(value, nameof(Title));
                _title = value;
            }
        }

        public int Year
        {
            get => _year;
            set
            {
                ValidationHelper.ValidateRange(value, 1450, DateTime.Now.Year, nameof(Year));
                _year = value;
            }
        }

        protected LibraryItem(int id, string title, int year) : base(id)
        {
            Title = title;
            Year = year;
        }

        // Жорсткий контракт: якщо видання вже зайняте — кидаємо бізнес-виняток
        public void Borrow(int readerId)
        {
            if (IsBorrowed)
            {
                throw new BookUnavailableException(
                    Id,
                    $"Видання \"{Title}\" (ID={Id}) вже видано читачу #{BorrowedByReaderId}."
                );
            }

            IsBorrowed = true;
            BorrowedByReaderId = readerId;
        }

        public void Return()
        {
            IsBorrowed = false;
            BorrowedByReaderId = null;
        }
    }

    public class Book : LibraryItem
    {
        private string _author;
        private int _pages;

        public string Author
        {
            get => _author;
            set
            {
                ValidationHelper.ValidateNotEmpty(value, nameof(Author));
                _author = value;
            }
        }

        public int Pages
        {
            get => _pages;
            set
            {
                ValidationHelper.ValidatePositive(value, nameof(Pages));
                _pages = value;
            }
        }

        public Book(string title, int year, string author, int pages)
            : base(IdGenerator.NextId<Book>(), title, year)
        {
            Author = author;
            Pages = pages;
        }

        public override void DisplayInfo()
        {
            string status = IsBorrowed ? $"Видано читачу #{BorrowedByReaderId}" : "В наявності";
            Console.WriteLine($"[Книга #{Id}] \"{Title}\" — {Author} ({Year} р., {Pages} стор.) | Статус: {status}");
        }
    }

    public class Magazine : LibraryItem
    {
        private int _issueNumber;

        public int IssueNumber
        {
            get => _issueNumber;
            set
            {
                ValidationHelper.ValidatePositive(value, nameof(IssueNumber));
                _issueNumber = value;
            }
        }

        public Magazine(string title, int year, int issueNumber)
            : base(IdGenerator.NextId<Magazine>(), title, year)
        {
            IssueNumber = issueNumber;
        }

        public override void DisplayInfo()
        {
            string status = IsBorrowed ? $"Видано читачу #{BorrowedByReaderId}" : "В наявності";
            Console.WriteLine($"[Журнал #{Id}] \"{Title}\" №{IssueNumber} ({Year} р.) | Статус: {status}");
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

        public Reader(string fullName)
            : base(IdGenerator.NextId<Reader>())
        {
            FullName = fullName;
        }

        public override void DisplayInfo()
        {
            Console.WriteLine($"[Читач #{Id}] {FullName}");
        }
    }

    // =========================================================
    // 5. РЕПОЗИТОРІЙ ТА СЕРВІС БІБЛІОТЕКИ
    // =========================================================
    public class Repository<T> where T : Entity
    {
        private readonly List<T> _items = new List<T>();

        public void Add(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            _items.Add(item);
        }

        public T GetById(int id)
        {
            foreach (var item in _items)
            {
                if (item.Id == id)
                    return item;
            }
            return null;
        }

        public List<T> GetAll() => new List<T>(_items);
    }

    public class LibraryService
    {
        private readonly Repository<LibraryItem> _catalog;
        private readonly Repository<Reader> _readers;

        public LibraryService(Repository<LibraryItem> catalog, Repository<Reader> readers)
        {
            _catalog = catalog;
            _readers = readers;
        }

        // Підхід 1: Виняток як помилка виконання (Fail Fast при порушенні контракту)
        public void BorrowItemStrict(int itemId, int readerId)
        {
            var reader = _readers.GetById(readerId)
                ?? throw new DomainValidationException(nameof(readerId), $"Читача з ID={readerId} не існує.");

            var item = _catalog.GetById(itemId)
                ?? throw new DomainValidationException(nameof(itemId), $"Видання з ID={itemId} не знайдено в каталозі.");

            item.Borrow(reader.Id); // Може викинути BookUnavailableException
        }

        // Підхід 2: Fault-tolerant метод через Result<T> для очікуваних помилкових ситуацій
        public Result<LibraryItem> TryBorrowItem(int itemId, int readerId)
        {
            var reader = _readers.GetById(readerId);
            if (reader == null)
                return Result<LibraryItem>.Failure($"Операцію відхилено: читача #{readerId} не знайдено.");

            var item = _catalog.GetById(itemId);
            if (item == null)
                return Result<LibraryItem>.Failure($"Операцію відхилено: видання #{itemId} відсутнє в каталозі.");

            if (item.IsBorrowed)
                return Result<LibraryItem>.Failure($"Видання \"{item.Title}\" зараз недоступне (знаходиться у читача #{item.BorrowedByReaderId}).");

            item.Borrow(reader.Id);
            return Result<LibraryItem>.Success(item);
        }
    }

    // =========================================================
    // 6. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var catalog = new Repository<LibraryItem>();
            var readers = new Repository<Reader>();
            var service = new LibraryService(catalog, readers);

            catalog.Add(new Book("CLR via C#", 2012, "Джеффрі Ріхтер", 896));
            catalog.Add(new Book("Чистий код", 2008, "Роберт Мартін", 464));
            readers.Add(new Reader("Олександр Коваленко"));
            readers.Add(new Reader("Марія Бойко"));

            Console.WriteLine("=== СЦЕНАРІЙ 1: Помилка валідації при створенні (try / catch / finally) ===");
            bool sessionActive = true;
            try
            {
                Console.WriteLine("Спроба створити книгу з від'ємною кількістю сторінок...");
                var invalidBook = new Book("Тестова книга", 2024, "Автор", -50);
                catalog.Add(invalidBook);
            }
            catch (DomainValidationException ex)
            {
                Console.WriteLine($"[Помилка валідації | Поле: {ex.FieldName}]: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Непередбачена помилка]: {ex.Message}");
            }
            finally
            {
                // Блок finally виконається завжди для очищення стану
                sessionActive = false;
                Console.WriteLine($"[Finally]: Транзакцію завершено (sessionActive = {sessionActive}).\n");
            }

            Console.WriteLine("=== СЦЕНАРІЙ 2: Виняток як помилка бізнес-правила (BorrowItemStrict) ===");
            try
            {
                service.BorrowItemStrict(itemId: 1, readerId: 1);
                Console.WriteLine("Успіх: Книгу #1 видано читачу #1.");

                // Повторна спроба видати вже зайняту книгу викличе BookUnavailableException
                service.BorrowItemStrict(itemId: 1, readerId: 2);
            }
            catch (BookUnavailableException ex)
            {
                Console.WriteLine($"[Бізнес-обмеження | Видання #{ex.ItemId}]: {ex.Message}");
            }
            catch (DomainValidationException ex)
            {
                Console.WriteLine($"[Помилка даних]: {ex.Message}");
            }

            Console.WriteLine("\n=== СЦЕНАРІЙ 3: Fault-tolerant підхід через Result<T> (TryBorrowItem) ===");
            // 1. Очікувана помилка: книга вже на руках (програма не генерує виняток)
            var result1 = service.TryBorrowItem(itemId: 1, readerId: 2);
            if (!result1.IsSuccess)
            {
                Console.WriteLine($"[Result Failure]: {result1.ErrorMessage}");
            }

            // 2. Очікувана помилка: неіснуючий ID книги
            var result2 = service.TryBorrowItem(itemId: 999, readerId: 2);
            if (!result2.IsSuccess)
            {
                Console.WriteLine($"[Result Failure]: {result2.ErrorMessage}");
            }

            // 3. Коректне виконання
            var result3 = service.TryBorrowItem(itemId: 2, readerId: 2);
            if (result3.IsSuccess)
            {
                Console.WriteLine($"[Result Success]: Книгу \"{result3.Value.Title}\" успішно видано читачу #2.");
            }

            Console.WriteLine("\n=== Підсумковий стан каталогу ===");
            foreach (var item in catalog.GetAll())
            {
                item.DisplayInfo();
            }
        }
    }
}