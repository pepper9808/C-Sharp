using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelRaceConditionDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕЧНИЙ ФОНД)
    // =========================================================
    public class LibraryBookStock
    {
        public string Title { get; }
        public int InitialCopies { get; }

        // Спільні змінювані поля для демонстрації
        public int AvailableCopies { get; set; }
        public int IssuedCopies { get; set; }
        public List<string> IssuedToReaders { get; } = new List<string>();

        public LibraryBookStock(string title, int initialCopies)
        {
            Title = title;
            InitialCopies = initialCopies;
            Reset();
        }

        public void Reset()
        {
            AvailableCopies = InitialCopies;
            IssuedCopies = 0;
            IssuedToReaders.Clear();
        }
    }

    // =========================================================
    // 2. СЕРВІС ВИДАЧІ: БЕЗ СИНХРОНІЗАЦІЇ ТА З СИНХРОНІЗАЦІЄЮ
    // =========================================================
    public class ConcurrentBookLoanService
    {
        private readonly object _syncLock = new object();
        private int _totalOperationsCounter = 0;

        public int TotalOperationsCounter => _totalOperationsCounter;

        public void ResetCounter() => _totalOperationsCounter = 0;

        // ---------------------------------------------------------
        // ЕТАП 1: НЕБЕЗПЕЧНА РЕАЛІЗАЦІЯ (БЕЗ СИНХРОНІЗАЦІЇ)
        // ---------------------------------------------------------
        public bool TryIssueBookUnsafe(LibraryBookStock stock, string readerName)
        {
            // Помилка Check-Then-Act: кілька потоків одночасно бачать AvailableCopies > 0
            if (stock.AvailableCopies > 0)
            {
                // Імітація затримки обробки, що провокує перемикання контексту ОС
                Thread.SpinWait(4000);

                // Неатомарне зменшення залишку (Read -> Sub -> Write)
                int currentAvailable = stock.AvailableCopies;
                Thread.SpinWait(1500);
                stock.AvailableCopies = currentAvailable - 1;

                // Неатомарне збільшення кількості виданих примірників
                int currentIssued = stock.IssuedCopies;
                Thread.SpinWait(1500);
                stock.IssuedCopies = currentIssued + 1;

                // Непотокобезпечний запис у звичайний List<string>
                try
                {
                    stock.IssuedToReaders.Add(readerName);
                }
                catch
                {
                    // Можливий ArgumentException при одночасному розширенні внутрішнього масиву List<T>
                }

                // Неатомарний інкремент загального лічильника
                int ops = _totalOperationsCounter;
                Thread.SpinWait(1000);
                _totalOperationsCounter = ops + 1;

                return true;
            }

            return false;
        }

        // ---------------------------------------------------------
        // ЕТАП 2: ПОТОКОБЕЗПЕЧНА РЕАЛІЗАЦІЯ (lock + Interlocked)
        // ---------------------------------------------------------
        public bool TryIssueBookThreadSafe(LibraryBookStock stock, string readerName)
        {
            // Критична секція: одночасно доступ до стану книги має лише один потік
            lock (_syncLock)
            {
                if (stock.AvailableCopies > 0)
                {
                    Thread.SpinWait(4000);

                    stock.AvailableCopies--;
                    stock.IssuedCopies++;
                    stock.IssuedToReaders.Add(readerName);

                    // Атомарний інкремент глобального лічильника
                    Interlocked.Increment(ref _totalOperationsCounter);
                    return true;
                }
            }

            return false;
        }
    }

    // =========================================================
    // 3. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ ТА ФІКСАЦІЯ РЕЗУЛЬТАТІВ
    // =========================================================
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            const int initialCopies = 10;
            const int concurrentReaders = 30;

            var stock = new LibraryBookStock("CLR via C#", initialCopies);
            var loanService = new ConcurrentBookLoanService();

            Console.WriteLine($"Початкові умови: книга \"{stock.Title}\", примірників у фонді = {initialCopies}, " +
                              $"одночасних запитів читачів = {concurrentReaders}.\n");

            // ---------------------------------------------------------
            // ЗАПУСК 1: БЕЗ СИНХРОНІЗАЦІЇ
            // ---------------------------------------------------------
            Task[] unsafeTasks = new Task[concurrentReaders];
            for (int i = 0; i < concurrentReaders; i++)
            {
                string reader = $"Читач #{i + 1}";
                unsafeTasks[i] = Task.Run(() => loanService.TryIssueBookUnsafe(stock, reader));
            }
            await Task.WhenAll(unsafeTasks);

            int unsafeAvailable = stock.AvailableCopies;
            int unsafeIssued = stock.IssuedCopies;
            int unsafeListCount = stock.IssuedToReaders.Count;
            int unsafeOpsCounter = loanService.TotalOperationsCounter;

            // ---------------------------------------------------------
            // ЗАПУСК 2: З СИНХРОНІЗАЦІЄЮ (lock + Interlocked)
            // ---------------------------------------------------------
            stock.Reset();
            loanService.ResetCounter();

            Task[] safeTasks = new Task[concurrentReaders];
            for (int i = 0; i < concurrentReaders; i++)
            {
                string reader = $"Читач #{i + 1}";
                safeTasks[i] = Task.Run(() => loanService.TryIssueBookThreadSafe(stock, reader));
            }
            await Task.WhenAll(safeTasks);

            int safeAvailable = stock.AvailableCopies;
            int safeIssued = stock.IssuedCopies;
            int safeListCount = stock.IssuedToReaders.Count;
            int safeOpsCounter = loanService.TotalOperationsCounter;

            // ---------------------------------------------------------
            // ВИВІД ПОРІВНЯЛЬНОЇ ТАБЛИЦІ РЕЗУЛЬТАТІВ ДЛЯ ЗВІТУ
            // ---------------------------------------------------------
            Console.WriteLine("====================================================================================");
            Console.WriteLine("             ПОРІВНЯННЯ РЕЗУЛЬТАТІВ КОНКУРЕНТНОГО ВИКОНАННЯ (ДЛЯ ЗВІТУ)");
            Console.WriteLine("====================================================================================");
            Console.WriteLine($"{"Показник",-40} | {"Очікувано",-10} | {"Без sync",-10} | {"З lock",-10}");
            Console.WriteLine(new string('-', 84));
            Console.WriteLine($"{"Залишок примірників (AvailableCopies)",-40} | {0,-10} | {unsafeAvailable,-10} | {safeAvailable,-10}");
            Console.WriteLine($"{"Лічильник виданих (IssuedCopies)",-40} | {initialCopies,-10} | {unsafeIssued,-10} | {safeIssued,-10}");
            Console.WriteLine($"{"Записів у списку читачів (List.Count)",-40} | {initialCopies,-10} | {unsafeListCount,-10} | {safeListCount,-10}");
            Console.WriteLine($"{"Загальний лічильник операцій",-40} | {initialCopies,-10} | {unsafeOpsCounter,-10} | {safeOpsCounter,-10}");
            Console.WriteLine($"{"Інваріант (Available + Issued == 10)",-40} | {"Так",-10} | {(unsafeAvailable + unsafeIssued == initialCopies ? "Так" : "НІ (ЗБІЙ)"),-10} | {(safeAvailable + safeIssued == initialCopies ? "Так" : "НІ"),-10}");
            Console.WriteLine("====================================================================================");
        }
    }
}