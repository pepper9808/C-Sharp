using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace DomainModelCustomContainerDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public abstract class LibraryItem
    {
        public int Id { get; }
        public string Title { get; }
        public int Year { get; }
        public bool IsBorrowed { get; internal set; }
        public string BorrowedBy { get; internal set; }

        protected LibraryItem(int id, string title, int year)
        {
            if (id <= 0) throw new ArgumentException("ID має бути додатним.");
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Назва не може бути порожньою.");

            Id = id;
            Title = title;
            Year = year;
        }

        public abstract void DisplayInfo();
    }

    public class Book : LibraryItem
    {
        public string Author { get; }
        public int Pages { get; }

        public Book(int id, string title, string author, int year, int pages)
            : base(id, title, year)
        {
            Author = author;
            Pages = pages;
        }

        public override void DisplayInfo()
        {
            string status = IsBorrowed ? $"Видано ({BorrowedBy})" : "В наявності";
            Console.WriteLine($"  [Книга #{Id}] \"{Title}\" — {Author} ({Year} р., {Pages} стор.) | {status}");
        }
    }

    public class Magazine : LibraryItem
    {
        public int IssueNumber { get; }

        public Magazine(int id, string title, int year, int issueNumber)
            : base(id, title, year)
        {
            IssueNumber = issueNumber;
        }

        public override void DisplayInfo()
        {
            string status = IsBorrowed ? $"Видано ({BorrowedBy})" : "В наявності";
            Console.WriteLine($"  [Журнал #{Id}] \"{Title}\" №{IssueNumber} ({Year} р.) | {status}");
        }
    }

    // =========================================================
    // 2. ВЛАСНИЙ КОНТЕЙНЕР З ПІДТРИМКОЮ FOREACH (IEnumerable<T>)
    // =========================================================
    public class LibraryCatalogCollection<T> : IEnumerable<T> where T : LibraryItem
    {
        private T[] _items;
        private int _count;
        private int _version; // Лічильник змін для захисту від модифікації під час foreach

        public int Count => _count;
        public int Capacity => _items.Length;

        public LibraryCatalogCollection(int initialCapacity = 4)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Початкова місткість має бути більшою за 0.");

            _items = new T[initialCapacity];
            _count = 0;
            _version = 0;
        }

        // Індексатор для прямого доступу за позицією
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException("Індекс виходить за межі колекції.");
                return _items[index];
            }
        }

        // Додавання елемента з динамічним розширенням внутрішнього масиву
        public void Add(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[_count++] = item;
            _version++;
        }

        // Видалення елемента за ID
        public bool RemoveById(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Id == id)
                {
                    for (int j = i; j < _count - 1; j++)
                    {
                        _items[j] = _items[j + 1];
                    }
                    _items[--_count] = null;
                    _version++;
                    return true;
                }
            }
            return false;
        }

        // Базовий пошук першого елемента за умовою (Predicate<T>)
        public T FindFirst(Predicate<T> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _count; i++)
            {
                if (predicate(_items[i]))
                    return _items[i];
            }
            return null;
        }

        // Лінива фільтрація через ітератор yield return (не виділяє пам'ять під новий список)
        public IEnumerable<T> FilterLazy(Predicate<T> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            int startVersion = _version;
            for (int i = 0; i < _count; i++)
            {
                if (_version != startVersion)
                    throw new InvalidOperationException("Колекцію було змінено під час перебору!");

                if (predicate(_items[i]))
                {
                    yield return _items[i];
                }
            }
        }

        // Реалізація IEnumerable<T> за допомогою yield return для підтримки foreach
        public IEnumerator<T> GetEnumerator()
        {
            int startVersion = _version;
            for (int i = 0; i < _count; i++)
            {
                if (_version != startVersion)
                    throw new InvalidOperationException("Колекцію не можна змінювати під час ітерації у циклі foreach!");

                yield return _items[i];
            }
        }

        // Явна реалізація неузагальненого IEnumerable
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    // =========================================================
    // 3. СЕРВІС БІБЛІОТЕКИ (використовує власний контейнер)
    // =========================================================
    public class LibraryService
    {
        // Власна колекція як основне сховище фонду
        private readonly LibraryCatalogCollection<LibraryItem> _catalog = new LibraryCatalogCollection<LibraryItem>();
        private int _nextId = 1;

        public Book AddBook(string title, string author, int year, int pages)
        {
            var book = new Book(_nextId++, title, author, year, pages);
            _catalog.Add(book);
            return book;
        }

        public Magazine AddMagazine(string title, int year, int issueNumber)
        {
            var magazine = new Magazine(_nextId++, title, year, issueNumber);
            _catalog.Add(magazine);
            return magazine;
        }

        // Бізнес-сценарій видачі видання читачу з використанням пошуку в контейнері
        public bool BorrowItem(int itemId, string readerName)
        {
            var item = _catalog.FindFirst(x => x.Id == itemId);
            if (item == null || item.IsBorrowed)
                return false;

            item.IsBorrowed = true;
            item.BorrowedBy = readerName;
            return true;
        }

        // Повертає основну колекцію для перебору
        public IEnumerable<LibraryItem> GetAllItems() => _catalog;

        // Повертає відфільтрований потік вільних видань через лінивий ітератор контейнера
        public IEnumerable<LibraryItem> GetAvailableItems() =>
            _catalog.FilterLazy(item => !item.IsBorrowed);
    }

    // =========================================================
    // 4. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var libraryService = new LibraryService();

            // Наповнення власної колекції через сервіс
            libraryService.AddBook("CLR via C#", "Джеффрі Ріхтер", 2012, 896);
            libraryService.AddBook("Чистий код", "Роберт Мартін", 2008, 464);
            libraryService.AddMagazine("Forbes Україна", 2024, 5);
            libraryService.AddBook("Патерни проєктування", "Ерік Фрімен", 2020, 640);
            libraryService.AddMagazine("National Geographic", 2023, 11);

            // Виконання бізнес-сценарію (пошук у контейнері та зміна статусу)
            libraryService.BorrowItem(itemId: 1, readerName: "Олександр Коваленко");
            libraryService.BorrowItem(itemId: 3, readerName: "Марія Бойко");

            Console.WriteLine("=== 1. ПЕРЕБІР УСЬОГО ВЛАСНОГО КОНТЕЙНЕРА ЧЕРЕЗ FOREACH ===");
            foreach (LibraryItem item in libraryService.GetAllItems())
            {
                item.DisplayInfo();
            }

            Console.WriteLine("\n=== 2. ПЕРЕБІР ДОСТУПНИХ ВИДАНЬ (ЛІНИВА ФІЛЬТРАЦІЯ YIELD RETURN) ===");
            foreach (LibraryItem available in libraryService.GetAvailableItems())
            {
                available.DisplayInfo();
            }

            Console.WriteLine("\n=== 3. ПЕРЕВІРКА ЗАХИСТУ ІТЕРАТОРА ВІД ЗМІНИ КОЛЕКЦІЇ ПІД ЧАС FOREACH ===");
            var customContainer = new LibraryCatalogCollection<Book>();
            customContainer.Add(new Book(10, "Тестова книга 1", "Автор А", 2021, 200));
            customContainer.Add(new Book(11, "Тестова книга 2", "Автор Б", 2022, 300));

            try
            {
                foreach (var book in customContainer)
                {
                    Console.WriteLine($"Читаємо: {book.Title}");
                    // Спроба модифікувати колекцію під час активного перебору
                    customContainer.Add(new Book(12, "Зайва книга", "Автор В", 2023, 150));
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[Захист ітератора спрацював]: {ex.Message}");
            }
        }
    }
}