using System;
using System.Collections;
using System.Collections.Generic;

namespace DomainModelInterfacesDemo
{
    // =========================================================
    // 1. ІНТЕРФЕЙСИ-КОНТРАКТИ ПРЕДМЕТНОЇ ОБЛАСТІ (Лекція 5)
    // =========================================================

    // Контракт базової сутності
    public interface IEntity
    {
        int Id { get; }
        void DisplayInfo();
    }

    // Контракт репозиторію (зменшує зв'язаність сервісу та сховища)
    public interface IRepository<T> : IEnumerable<T> where T : IEntity
    {
        int Count { get; }
        void Add(T item);
        T GetById(int id);
        bool Remove(int id);
    }

    // Контракт сервісу сповіщень (дозволяє легко підміняти канали зв'язку)
    public interface INotificationSender
    {
        void Send(string recipient, string message);
    }

    // =========================================================
    // 2. РЕАЛІЗАЦІЇ КОНТРАКТУ СПОВІЩЕНЬ
    // =========================================================
    public class SmsNotificationSender : INotificationSender
    {
        public void Send(string recipient, string message)
        {
            Console.WriteLine($"  [SMS -> {recipient}]: {message}");
        }
    }

    public class EmailNotificationSender : INotificationSender
    {
        public void Send(string recipient, string message)
        {
            Console.WriteLine($"  [Email -> {recipient}]: {message}");
        }
    }

    // =========================================================
    // 3. ДОПОМІЖНІ МОДУЛІ ТА МОДЕЛЬ СУТНОСТЕЙ
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

    public static class IdGenerator
    {
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();

        public static int NextId<T>() where T : IEntity
        {
            string key = typeof(T).Name;
            if (!_counters.ContainsKey(key))
                _counters[key] = 0;
            return ++_counters[key];
        }
    }

    public abstract class LibraryItem : IEntity, IComparable<LibraryItem>
    {
        public int Id { get; }
        public string Title { get; set; }
        public int Year { get; set; }
        public bool IsBorrowed { get; private set; }
        public int? BorrowedByReaderId { get; private set; }

        protected LibraryItem(int id, string title, int year)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Назва не може бути порожньою.");

            Id = id;
            Title = title;
            Year = year;
        }

        public void Borrow(int readerId)
        {
            if (IsBorrowed)
                throw new InvalidOperationException($"Видання \"{Title}\" вже видано.");

            IsBorrowed = true;
            BorrowedByReaderId = readerId;
        }

        public void Return()
        {
            IsBorrowed = false;
            BorrowedByReaderId = null;
        }

        // Реалізація IComparable<LibraryItem> для сортування за роком видання
        public int CompareTo(LibraryItem other)
        {
            if (other == null) return 1;
            return this.Year.CompareTo(other.Year);
        }

        public abstract void DisplayInfo();
    }

    public class Book : LibraryItem
    {
        public string Author { get; set; }
        public int Pages { get; set; }

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
        public int IssueNumber { get; set; }

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

    public class Reader : IEntity
    {
        public int Id { get; }
        public string FullName { get; set; }

        public Reader(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("Ім'я читача не може бути порожнім.");

            Id = IdGenerator.NextId<Reader>();
            FullName = fullName;
        }

        public void DisplayInfo()
        {
            Console.WriteLine($"[Читач #{Id}] {FullName}");
        }
    }

    // =========================================================
    // 4. ВЛАСНА ІТЕРОВАНА КОЛЕКЦІЯ (IEnumerable<T> + yield return)
    // =========================================================
    public class LibraryShelf<T> : IRepository<T> where T : IEntity
    {
        private T[] _items;
        private int _count;

        public string ShelfCode { get; }
        public int Count => _count;

        public LibraryShelf(string shelfCode, int initialCapacity = 4)
        {
            ShelfCode = shelfCode;
            _items = new T[initialCapacity];
            _count = 0;
        }

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }
            _items[_count++] = item;
        }

        public T GetById(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id == id)
                    return _items[i];
            }
            return default;
        }

        public bool Remove(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id == id)
                {
                    for (int j = i; j < _count - 1; j++)
                    {
                        _items[j] = _items[j + 1];
                    }
                    _items[--_count] = default;
                    return true;
                }
            }
            return false;
        }

        // Реалізація IEnumerable<T> за допомогою лінивого ітератора yield return
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _items[i];
            }
        }

        // Явна реалізація неузагальненого інтерфейсу IEnumerable
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    // =========================================================
    // 5. СЕРВІС БІБЛІОТЕКИ (залежить лише від інтерфейсів)
    // =========================================================
    public class LibraryService
    {
        private readonly IRepository<LibraryItem> _catalog;
        private readonly IRepository<Reader> _readers;
        private readonly INotificationSender _notifier;

        // Впровадження залежностей через інтерфейси (Dependency Injection)
        public LibraryService(
            IRepository<LibraryItem> catalog,
            IRepository<Reader> readers,
            INotificationSender notifier)
        {
            _catalog = catalog;
            _readers = readers;
            _notifier = notifier;
        }

        // Лінивий ітератор із yield return: повертає лише доступні книги по одній без витрат пам'яті
        public IEnumerable<LibraryItem> GetAvailableItems()
        {
            foreach (var item in _catalog)
            {
                if (!item.IsBorrowed)
                {
                    yield return item;
                }
            }
        }

        public Result<LibraryItem> TryBorrowItem(int itemId, int readerId)
        {
            var reader = _readers.GetById(readerId);
            if (reader == null)
                return Result<LibraryItem>.Failure($"Читача #{readerId} не знайдено.");

            var item = _catalog.GetById(itemId);
            if (item == null)
                return Result<LibraryItem>.Failure($"Видання #{itemId} не знайдено.");

            if (item.IsBorrowed)
                return Result<LibraryItem>.Failure($"Видання \"{item.Title}\" вже на руках.");

            item.Borrow(reader.Id);
            _notifier.Send(reader.FullName, $"Вам видано \"{item.Title}\".");
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

            // Створюємо власні ітеровані колекції (полиці бібліотеки)
            IRepository<LibraryItem> mainShelf = new LibraryShelf<LibraryItem>("A-01");
            IRepository<Reader> readersRegistry = new LibraryShelf<Reader>("R-01");

            mainShelf.Add(new Book("CLR via C#", 2012, "Джеффрі Ріхтер", 896));
            mainShelf.Add(new Magazine("Forbes Україна", 2024, 5));
            mainShelf.Add(new Book("Чистий код", 2008, "Роберт Мартін", 464));

            readersRegistry.Add(new Reader("Олександр Коваленко"));
            readersRegistry.Add(new Reader("Марія Бойко"));

            Console.WriteLine("=== 1. ПЕРЕБІР ВЛАСНОЇ КОЛЕКЦІЇ ЧЕРЕЗ FOREACH (IEnumerable<T> + yield) ===");
            foreach (LibraryItem item in mainShelf)
            {
                item.DisplayInfo();
            }

            Console.WriteLine("\n=== 2. РОБОТА СЕРВІСУ З SMS-ПОВІДОМЛЕННЯМИ (SmsNotificationSender) ===");
            INotificationSender smsSender = new SmsNotificationSender();
            var serviceWithSms = new LibraryService(mainShelf, readersRegistry, smsSender);
            serviceWithSms.TryBorrowItem(itemId: 1, readerId: 1);

            Console.WriteLine("\n=== 3. ПІДМІНА РЕАЛІЗАЦІЇ ІНТЕРФЕЙСУ НА EMAIL (EmailNotificationSender) ===");
            // Код LibraryService не змінюється — просто передаємо іншу реалізацію INotificationSender
            INotificationSender emailSender = new EmailNotificationSender();
            var serviceWithEmail = new LibraryService(mainShelf, readersRegistry, emailSender);
            serviceWithEmail.TryBorrowItem(itemId: 2, readerId: 2);

            Console.WriteLine("\n=== 4. ЛІНИВИЙ ІТЕРАТОР ДОСТУПНИХ ВИДАНЬ (yield return) ===");
            foreach (var availableItem in serviceWithEmail.GetAvailableItems())
            {
                availableItem.DisplayInfo();
            }
        }
    }
}