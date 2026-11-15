using System;
using System.Collections.Generic;

namespace DomainModelDemo
{
    // 1. Базовий клас сутності з унікальним ідентифікатором
    public abstract class LibraryItem
    {
        private readonly Guid _id;
        private string _title;
        private int _year;

        public Guid Id => _id;

        public string Title
        {
            get { return _title; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Назва не може бути порожньою.");
                _title = value;
            }
        }

        public int Year
        {
            get { return _year; }
            set
            {
                if (value < 1450 || value > DateTime.Now.Year)
                    throw new ArgumentException("Некоректний рік видання.");
                _year = value;
            }
        }

        protected LibraryItem(string title, int year)
        {
            this._id = Guid.NewGuid(); // Генерація унікального ідентифікатора
            this.Title = title;        // Ініціалізація через властивість із перевіркою
            this.Year = year;
        }

        // Віртуальний метод для демонстрації поліморфізму
        public virtual void DisplayInfo()
        {
            Console.WriteLine($"[ID: {Id.ToString().Substring(0, 8)}] \"{Title}\" ({Year} р.)");
        }
    }

    // 2. Перший похідний клас (Книга)
    public class Book : LibraryItem
    {
        private string _author;
        private int _pages;

        public string Author
        {
            get { return _author; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Автор має бути вказаний.");
                _author = value;
            }
        }

        public int Pages
        {
            get { return _pages; }
            set
            {
                if (value <= 0)
                    throw new ArgumentException("Кількість сторінок має бути більшою за 0.");
                _pages = value;
            }
        }

        public Book(string title, int year, string author, int pages)
            : base(title, year) // Виклик конструктора базового класу
        {
            this.Author = author;
            this.Pages = pages;
        }

        // Перевизначення віртуального методу
        public override void DisplayInfo()
        {
            Console.WriteLine($"[Книга | ID: {Id.ToString().Substring(0, 8)}] \"{Title}\" — {Author}, {Year} р., {Pages} стор.");
        }
    }

    // 3. Другий похідний клас (Журнал)
    public class Magazine : LibraryItem
    {
        private int _issueNumber;

        public int IssueNumber
        {
            get { return _issueNumber; }
            set
            {
                if (value <= 0)
                    throw new ArgumentException("Номер випуску має бути додатним.");
                _issueNumber = value;
            }
        }

        public Magazine(string title, int year, int issueNumber)
            : base(title, year)
        {
            this.IssueNumber = issueNumber;
        }

        // Перевизначення віртуального методу
        public override void DisplayInfo()
        {
            Console.WriteLine($"[Журнал | ID: {Id.ToString().Substring(0, 8)}] \"{Title}\" №{IssueNumber} ({Year} р.)");
        }
    }

    // 4. Додатковий клас предметної області (Читач)
    public class Reader
    {
        private readonly Guid _readerId;
        private string _fullName;

        public Guid ReaderId => _readerId;

        public string FullName
        {
            get { return _fullName; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Ім'я читача не може бути порожнім.");
                _fullName = value;
            }
        }

        public Reader(string fullName)
        {
            this._readerId = Guid.NewGuid();
            this.FullName = fullName;
        }

        public void ShowCard()
        {
            Console.WriteLine($"Читацький квиток [{ReaderId.ToString().Substring(0, 8)}]: {FullName}");
        }
    }

    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Reader reader = new Reader("Олександр Коваленко");
            reader.ShowCard();
            Console.WriteLine(new string('-', 60));

            // Колекція базового типу для демонстрації поліморфізму
            List<LibraryItem> catalog = new List<LibraryItem>
            {
                new Book("CLR via C#", 2012, "Джеффрі Ріхтер", 896),
                new Magazine("Forbes Україна", 2024, 5),
                new Book("Чистий код", 2008, "Роберт Мартін", 464)
            };

            // Динамічний поліморфізм: викликається реалізація конкретного класу в пам'яті
            foreach (LibraryItem item in catalog)
            {
                item.DisplayInfo();
            }
        }
    }
}