using System;
using System.Collections.Generic;

namespace DomainModelEventsAndDelegatesDemo
{
    // =========================================================
    // 1. ВЛАСНІ ВИНЯТКИ ТА FAULT-TOLERANT RESULT (з ЛР 3)
    // =========================================================
    public class DomainValidationException : Exception
    {
        public string FieldName { get; }
        public DomainValidationException(string fieldName, string message) : base(message)
        {
            FieldName = fieldName;
        }
    }

    public class BookUnavailableException : Exception
    {
        public int ItemId { get; }
        public BookUnavailableException(int itemId, string message) : base(message)
        {
            ItemId = itemId;
        }
    }

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
    // 2. СТАТИЧНІ МОДУЛІ ГЕНЕРАЦІЇ ID ТА ВАЛІДАЦІЇ (з ЛР 2)
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
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' має бути більшим за 0.");
        }

        public static void ValidateRange(int value, int min, int max, string fieldName)
        {
            if (value < min || value > max)
                throw new DomainValidationException(fieldName, $"Поле '{fieldName}' має бути в межах [{min}..{max}].");
        }
    }

    // =========================================================
    // 3. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ
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

        public void Borrow(int readerId)
        {
            if (IsBorrowed)
                throw new BookUnavailableException(Id, $"Видання \"{Title}\" вже видано читачу #{BorrowedByReaderId}.");

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
            string status = IsBorrowed ? $"Видано (#{BorrowedByReaderId})" : "В наявності";
            Console.WriteLine($"[Книга #{Id}] \"{Title}\" — {Author} ({Year} р., {Pages} стор.) | {status}");
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
            string status = IsBorrowed ? $"Видано (#{BorrowedByReaderId})" : "В наявності";
            Console.WriteLine($"[Журнал #{Id}] \"{Title}\" №{IssueNumber} ({Year} р.) | {status}");
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
    // 4. РЕПОЗИТОРІЙ ІЗ ДЕЛЕГУВАННЯМ ПОВЕДІНКИ (Predicate, Func, Action)
    // =========================================================
    public class Repository<T> where T : Entity
    {
        private readonly List<T> _items = new List<T>();

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items.Add(item);
        }

        public T GetById(int id)
        {
            foreach (var item in _items)
                if (item.Id == id) return item;
            return null;
        }

        // Сценарій 1 (Predicate<T>): Пошук/фільтрація за довільним критерієм
        public List<T> Find(Predicate<T> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            List<T> result = new List<T>();
            foreach (var item in _items)
            {
                if (condition(item))
                {
                    result.Add(item);
                }
            }
            return result;
        }

        // Сценарій 2 (Func<T, TResult>): Трансформація об'єктів у інший формат
        public List<TResult> Project<TResult>(Func<T, TResult> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            List<TResult> result = new List<TResult>();
            foreach (var item in _items)
            {
                result.Add(selector(item));
            }
            return result;
        }

        // Сценарій 3 (Action<T>): Виконання довільної дії над кожним елементом
        public void ForEach(Action<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            foreach (var item in _items)
            {
                action(item);
            }
        }
    }

    // =========================================================
    // 5. ПОДІЄВА МОДЕЛЬ: АРГУМЕНТИ ПОДІЙ ТА ВИДАВЕЦЬ (PUBLISHER)
    // =========================================================
    public class ItemAddedEventArgs : EventArgs
    {
        public LibraryItem Item { get; }
        public DateTime Timestamp { get; }

        public ItemAddedEventArgs(LibraryItem item)
        {
            Item = item;
            Timestamp = DateTime.Now;
        }
    }

    public class ItemStatusChangedEventArgs : EventArgs
    {
        public LibraryItem Item { get; }
        public Reader Reader { get; }
        public string ActionType { get; } // "Видано" або "Повернуто"
        public DateTime Timestamp { get; }

        public ItemStatusChangedEventArgs(LibraryItem item, Reader reader, string actionType)
        {
            Item = item;
            Reader = reader;
            ActionType = actionType;
            Timestamp = DateTime.Now;
        }
    }

    // Publisher (Видавець): генерує події, але не знає, хто на них підписаний
    public class LibraryService
    {
        private readonly Repository<LibraryItem> _catalog;
        private readonly Repository<Reader> _readers;

        // Стандартний патерн подій EventHandler<TEventArgs>
        public event EventHandler<ItemAddedEventArgs> ItemAdded;
        public event EventHandler<ItemStatusChangedEventArgs> ItemStatusChanged;

        public LibraryService(Repository<LibraryItem> catalog, Repository<Reader> readers)
        {
            _catalog = catalog;
            _readers = readers;
        }

        public void RegisterNewItem(LibraryItem item)
        {
            _catalog.Add(item);
            // Безпечний виклик події з перевіркою на null
            ItemAdded?.Invoke(this, new ItemAddedEventArgs(item));
        }

        public Result<LibraryItem> TryBorrowItem(int itemId, int readerId)
        {
            var reader = _readers.GetById(readerId);
            if (reader == null)
                return Result<LibraryItem>.Failure($"Читача #{readerId} не знайдено.");

            var item = _catalog.GetById(itemId);
            if (item == null)
                return Result<LibraryItem>.Failure($"Видання #{itemId} відсутнє в каталозі.");

            if (item.IsBorrowed)
                return Result<LibraryItem>.Failure($"Видання \"{item.Title}\" вже зайняте.");

            item.Borrow(reader.Id);

            // Генерація події зміни статусу
            ItemStatusChanged?.Invoke(this, new ItemStatusChangedEventArgs(item, reader, "Видано"));
            return Result<LibraryItem>.Success(item);
        }

        public Result<LibraryItem> TryReturnItem(int itemId)
        {
            var item = _catalog.GetById(itemId);
            if (item == null)
                return Result<LibraryItem>.Failure($"Видання #{itemId} не знайдено.");

            if (!item.IsBorrowed)
                return Result<LibraryItem>.Failure($"Видання \"{item.Title}\" і так знаходиться в бібліотеці.");

            var reader = _readers.GetById(item.BorrowedByReaderId.Value);
            item.Return();

            // Генерація події зміни статусу
            ItemStatusChanged?.Invoke(this, new ItemStatusChangedEventArgs(item, reader, "Повернуто"));
            return Result<LibraryItem>.Success(item);
        }
    }

    // =========================================================
    // 6. ПІДПИСНИКИ ПОДІЙ (SUBSCRIBERS)
    // =========================================================

    // Підписник №1: Аудит-логер
    public class AuditLogger
    {
        public void OnItemAdded(object sender, ItemAddedEventArgs e)
        {
            Console.WriteLine($"  [AUDIT {e.Timestamp:HH:mm:ss}] Додано нове видання до фонду: \"{e.Item.Title}\" (ID={e.Item.Id}).");
        }

        public void OnItemStatusChanged(object sender, ItemStatusChangedEventArgs e)
        {
            Console.WriteLine($"  [AUDIT {e.Timestamp:HH:mm:ss}] Операція '{e.ActionType}': \"{e.Item.Title}\" <-> Читач: {e.Reader?.FullName}.");
        }
    }

    // Підписник №2: Сервіс сповіщень
    public class NotificationService
    {
        public void SendReceiptNotification(object sender, ItemStatusChangedEventArgs e)
        {
            if (e.ActionType == "Видано" && e.Reader != null)
            {
                Console.WriteLine($"  [SMS -> {e.Reader.FullName}]: Ви отримали книгу \"{e.Item.Title}\". Просимо повернути вчасно!");
            }
        }
    }

    // Підписник №3: Модуль статистики в реальному часі
    public class LibraryStatistics
    {
        public int TotalAdded { get; private set; }
        public int ActiveBorrows { get; private set; }

        public void TrackNewItem(object sender, ItemAddedEventArgs e) => TotalAdded++;

        public void TrackBorrowStats(object sender, ItemStatusChangedEventArgs e)
        {
            if (e.ActionType == "Видано") ActiveBorrows++;
            else if (e.ActionType == "Повернуто") ActiveBorrows--;
        }

        public void PrintSummary()
        {
            Console.WriteLine($"[СТАТИСТИКА]: Додано нових видань: {TotalAdded} | Зараз на руках у читачів: {ActiveBorrows}");
        }
    }

    // =========================================================
    // 7. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var catalog = new Repository<LibraryItem>();
            var readers = new Repository<Reader>();
            var libraryService = new LibraryService(catalog, readers);

            // Ініціалізація підписників
            var logger = new AuditLogger();
            var notifier = new NotificationService();
            var stats = new LibraryStatistics();

            // Підписка на події (Multicast через +=)
            libraryService.ItemAdded += logger.OnItemAdded;
            libraryService.ItemAdded += stats.TrackNewItem;

            libraryService.ItemStatusChanged += logger.OnItemStatusChanged;
            libraryService.ItemStatusChanged += notifier.SendReceiptNotification;
            libraryService.ItemStatusChanged += stats.TrackBorrowStats;

            Console.WriteLine("=== 1. РОБОТА ПОДІЙ ПРИ ДОДАВАННІ ТА ВИДАЧІ КНИГ ===");
            readers.Add(new Reader("Олександр Коваленко"));
            readers.Add(new Reader("Марія Бойко"));

            libraryService.RegisterNewItem(new Book("CLR via C#", 2012, "Джеффрі Ріхтер", 896));
            libraryService.RegisterNewItem(new Book("Чистий код", 2008, "Роберт Мартін", 464));
            libraryService.RegisterNewItem(new Magazine("Forbes Україна", 2024, 5));

            Console.WriteLine("\n--- Видача та повернення видань ---");
            libraryService.TryBorrowItem(itemId: 1, readerId: 1);
            libraryService.TryBorrowItem(itemId: 2, readerId: 2);
            libraryService.TryReturnItem(itemId: 1);

            Console.WriteLine();
            stats.PrintSummary();

            // Відписка від одного з обробників (демонстрація оператора -=)
            libraryService.ItemStatusChanged -= notifier.SendReceiptNotification;

            Console.WriteLine("\n=== 2. ДЕЛЕГУВАННЯ ПОВЕДІНКИ (Predicate, Func, Action) ===");

            // Використання Predicate<LibraryItem>: пошук доступних книг, виданих після 2010 року
            Predicate<LibraryItem> modernAvailableFilter = item => !item.IsBorrowed && item.Year >= 2010;
            var filteredItems = catalog.Find(modernAvailableFilter);

            Console.WriteLine("Доступні видання від 2010 року:");
            // Використання Action<LibraryItem>
            filteredItems.ForEach(item => item.DisplayInfo());

            // Використання Func<LibraryItem, string>: проекція об'єктів у короткий прайс/довідник
            Func<LibraryItem, string> shortFormatSelector = item =>
                $"• {item.Title} ({item.Year}) — {(item.IsBorrowed ? "Зайнято" : "Вільно")}";

            List<string> catalogSummary = catalog.Project(shortFormatSelector);
            Console.WriteLine("\nКороткий звіт (через Func<T, TResult>):");
            catalogSummary.ForEach(line => Console.WriteLine(line));
        }
    }
}