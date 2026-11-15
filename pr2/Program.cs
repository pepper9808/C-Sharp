using System;
using System.Collections.Generic;

namespace DomainModelGenericsDemo
{
    // 1. Статичний модуль для централізованої генерації послідовних ідентифікаторів
    public static class IdGenerator
    {
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        private static readonly object _syncLock = new object();

        public static int NextId<T>() where T : Entity
        {
            string typeKey = typeof(T).Name;
            lock (_syncLock)
            {
                if (!_counters.ContainsKey(typeKey))
                {
                    _counters[typeKey] = 0;
                }
                _counters[typeKey]++;
                return _counters[typeKey];
            }
        }
    }

    // 2. Статичний модуль для централізованої валідації даних
    public static class ValidationHelper
    {
        public static void ValidateNotEmpty(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Поле '{fieldName}' не може бути порожнім.");
        }

        public static void ValidatePositive(int value, string fieldName)
        {
            if (value <= 0)
                throw new ArgumentException($"Поле '{fieldName}' має бути більшим за 0.");
        }

        public static void ValidateRange(int value, int min, int max, string fieldName)
        {
            if (value < min || value > max)
                throw new ArgumentException($"Поле '{fieldName}' має бути в діапазоні від {min} до {max}.");
        }
    }

    // 3. Спільний базовий клас для всіх сутностей проєкту
    public abstract class Entity
    {
        public readonly int Id;

        protected Entity(int id)
        {
            ValidationHelper.ValidatePositive(id, nameof(Id));
            this.Id = id;
        }

        public abstract void DisplayInfo();
    }

    // 4. Базовий клас бібліотечних видань
    public abstract class LibraryItem : Entity
    {
        private string _title;
        private int _year;

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
            this.Title = title;
            this.Year = year;
        }
    }

    // Похідний клас: Книга
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
            this.Author = author;
            this.Pages = pages;
        }

        public override void DisplayInfo()
        {
            Console.WriteLine($"[Книга #{Id}] \"{Title}\" — {Author}, {Year} р., {Pages} стор.");
        }
    }

    // Похідний клас: Журнал
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
            this.IssueNumber = issueNumber;
        }

        public override void DisplayInfo()
        {
            Console.WriteLine($"[Журнал #{Id}] \"{Title}\" №{IssueNumber} ({Year} р.)");
        }
    }

    // Сутність: Читач
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
            this.FullName = fullName;
        }

        public override void DisplayInfo()
        {
            Console.WriteLine($"[Читач #{Id}] {FullName}");
        }
    }

    // 5. Узагальнений репозиторій з обмеженням типу (where T : Entity)
    public class Repository<T> where T : Entity
    {
        private readonly List<T> _items = new List<T>();

        public int Count => _items.Count;

        public void Add(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (GetById(item.Id) != null)
                throw new InvalidOperationException($"Сутність із ID={item.Id} вже існує в репозиторії.");

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

        public List<T> GetAll()
        {
            // Повертаємо копію списку для збереження інкапсуляції внутрішньої колекції
            return new List<T>(_items);
        }

        public bool Remove(int id)
        {
            T existing = GetById(id);
            if (existing == null)
                return false;

            return _items.Remove(existing);
        }
    }

    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Репозиторій для видань (поліморфне зберігання Book та Magazine)
            Repository<LibraryItem> catalogRepo = new Repository<LibraryItem>();
            catalogRepo.Add(new Book("CLR via C#", 2012, "Джеффрі Ріхтер", 896));
            catalogRepo.Add(new Magazine("Forbes Україна", 2024, 5));
            catalogRepo.Add(new Book("Чистий код", 2008, "Роберт Мартін", 464));

            // Окремий репозиторій для читачів (повторне використання того ж коду)
            Repository<Reader> readerRepo = new Repository<Reader>();
            readerRepo.Add(new Reader("Олександр Коваленко"));
            readerRepo.Add(new Reader("Марія Шевченко"));

            Console.WriteLine("=== Каталог видань ===");
            foreach (var item in catalogRepo.GetAll())
            {
                item.DisplayInfo();
            }

            Console.WriteLine("\n=== Зареєстровані читачі ===");
            foreach (var reader in readerRepo.GetAll())
            {
                reader.DisplayInfo();
            }

            Console.WriteLine("\n=== Пошук та видалення ===");
            var foundReader = readerRepo.GetById(2);
            if (foundReader != null)
            {
                foundReader.FullName = "Марія Бойко"; // Валідація спрацьовує і при модифікації
                foundReader.DisplayInfo();
            }

            catalogRepo.Remove(1);
            Console.WriteLine($"Залишилось видань у каталозі: {catalogRepo.Count}");
        }
    }
}