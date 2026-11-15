using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelConcurrencyDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class Book
    {
        public int Id { get; }
        public string Title { get; }
        public bool IsBorrowed { get; set; }
        public int? BorrowedByReaderId { get; set; }

        public Book(int id, string title)
        {
            Id = id;
            Title = title;
            IsBorrowed = false;
        }
    }

    // =========================================================
    // 2. СЕРВІС БІБЛІОТЕКИ: ПОРІВНЯННЯ UNSAFE ТА THREAD-SAFE РЕЖИМІВ
    // =========================================================
    public class ConcurrentLibraryService
    {
        // Спільна колекція книг (потокобезпечний словник)
        private readonly ConcurrentDictionary<int, Book> _catalog = new ConcurrentDictionary<int, Book>();

        // Об'єкт-затичка для критичної секції (lock)
        private readonly object _borrowLock = new object();

        // Потокобезпечний журнал операцій
        public ConcurrentQueue<string> AuditLog { get; } = new ConcurrentQueue<string>();

        // Лічильники для демонстрації втрати інкрементів при Race Condition
        private int _unsafeTotalBorrows = 0;
        private int _safeTotalBorrows = 0;

        public int UnsafeTotalBorrows => _unsafeTotalBorrows;
        public int SafeTotalBorrows => _safeTotalBorrows;

        public void AddBook(Book book)
        {
            _catalog[book.Id] = book;
        }

        public Book GetBook(int id) => _catalog.TryGetValue(id, out var book) ? book : null;

        public void ResetState()
        {
            foreach (var book in _catalog.Values)
            {
                book.IsBorrowed = false;
                book.BorrowedByReaderId = null;
            }
            _unsafeTotalBorrows = 0;
            _safeTotalBorrows = 0;
            while (AuditLog.TryDequeue(out _)) { }
        }

        // ---------------------------------------------------------
        // ВАРІАНТ 1: БЕЗ СИНХРОНІЗАЦІЇ (Вразливий до Race Condition)
        // ---------------------------------------------------------
        public bool TryBorrowUnsafe(int bookId, int readerId)
        {
            if (!_catalog.TryGetValue(bookId, out var book))
                return false;

            // Стан гонитви (Check-Then-Act): кілька потоків одночасно бачать IsBorrowed == false
            if (!book.IsBorrowed)
            {
                // Імітація затримки (звернення до БД/мережі), що провокує перемикання контексту
                Thread.SpinWait(5000);

                book.IsBorrowed = true;
                book.BorrowedByReaderId = readerId;

                // Неатомарна операція (Read -> Add -> Write): частина оновлень губиться
                int temp = _unsafeTotalBorrows;
                Thread.SpinWait(1000);
                _unsafeTotalBorrows = temp + 1;

                AuditLog.Enqueue($"[UNSAFE] Читач #{readerId} отримав книгу \"{book.Title}\"");
                return true;
            }

            return false;
        }

        // ---------------------------------------------------------
        // ВАРІАНТ 2: ІЗ СИНХРОНІЗАЦІЄЮ (lock + Interlocked)
        // ---------------------------------------------------------
        public bool TryBorrowThreadSafe(int bookId, int readerId)
        {
            if (!_catalog.TryGetValue(bookId, out var book))
                return false;

            // Критична секція: одночасно всередині перебуває лише один потік
            lock (_borrowLock)
            {
                if (!book.IsBorrowed)
                {
                    Thread.SpinWait(5000);

                    book.IsBorrowed = true;
                    book.BorrowedByReaderId = readerId;

                    // Атомарне збільшення лічильника на рівні інструкцій CPU
                    Interlocked.Increment(ref _safeTotalBorrows);

                    AuditLog.Enqueue($"[SAFE] Читач #{readerId} успішно отримав книгу \"{book.Title}\"");
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------
        // ВАРІАНТ 3: ПАРАЛЕЛЬНА ІНВЕНТАРИЗАЦІЯ З CANCELLATION TOKEN
        // ---------------------------------------------------------
        public Task<int> RunParallelInventoryAuditAsync(IEnumerable<Book> booksToCheck, CancellationToken token)
        {
            return Task.Run(() =>
            {
                int checkedCount = 0;

                // Використання TPL (Parallel.ForEach) із передачею CancellationToken
                ParallelOptions options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                Parallel.ForEach(booksToCheck, options, book =>
                {
                    // Перевірка запиту на скасування перед кожною ітерацією
                    token.ThrowIfCancellationRequested();

                    // Імітація тривалої перевірки стану екземпляра
                    Thread.Sleep(50);

                    Interlocked.Increment(ref checkedCount);
                });

                return checkedCount;
            }, token);
        }
    }

    // =========================================================
    // 3. ДЕМОНСТРАЦІЙНИЙ СЦЕНАРІЙ
    // =========================================================
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var libraryService = new ConcurrentLibraryService();
            libraryService.AddBook(new Book(1, "CLR via C# (Останній примірник)"));

            int concurrentRequests = 20;

            // ---------------------------------------------------------
            // ТЕСТ 1: КОНКУРЕНТНИЙ ДОСТУП БЕЗ СИНХРОНІЗАЦІЇ (RACE CONDITION)
            // ---------------------------------------------------------
            Console.WriteLine("=== 1. КОНКУРЕНТНА ВИДАЧА БЕЗ СИНХРОНІЗАЦІЇ (20 одночасних запитів) ===");
            Task<bool>[] unsafeTasks = new Task<bool>[concurrentRequests];

            for (int i = 0; i < concurrentRequests; i++)
            {
                int readerId = i + 1;
                unsafeTasks[i] = Task.Run(() => libraryService.TryBorrowUnsafe(1, readerId));
            }

            bool[] unsafeResults = await Task.WhenAll(unsafeTasks);
            int unsafeSuccesses = unsafeResults.Count(r => r);

            Console.WriteLine($"Успішних видач одного примірника: {unsafeSuccesses} (Очікувалось: 1!)");
            Console.WriteLine($"Значення лічильника _unsafeTotalBorrows: {libraryService.UnsafeTotalBorrows} (Втрачено оновлень через неатомарність!)");
            Console.WriteLine("Записи в журналі:");
            foreach (var log in libraryService.AuditLog)
            {
                Console.WriteLine("  " + log);
            }

            // ---------------------------------------------------------
            // ТЕСТ 2: КОНКУРЕНТНИЙ ДОСТУП ІЗ СИНХРОНІЗАЦІЄЮ (lock + Interlocked)
            // ---------------------------------------------------------
            libraryService.ResetState();
            Console.WriteLine("\n=== 2. КОНКУРЕНТНА ВИДАЧА З СИНХРОНІЗАЦІЄЮ (lock + Interlocked) ===");
            Task<bool>[] safeTasks = new Task<bool>[concurrentRequests];

            for (int i = 0; i < concurrentRequests; i++)
            {
                int readerId = i + 1;
                safeTasks[i] = Task.Run(() => libraryService.TryBorrowThreadSafe(1, readerId));
            }

            bool[] safeResults = await Task.WhenAll(safeTasks);
            int safeSuccesses = safeResults.Count(r => r);

            Console.WriteLine($"Успішних видач одного примірника: {safeSuccesses} (Коректно: 1)");
            Console.WriteLine($"Значення лічильника _safeTotalBorrows: {libraryService.SafeTotalBorrows}");
            Console.WriteLine($"Фактичний власник книги в системі: Читач #{libraryService.GetBook(1).BorrowedByReaderId}");
            foreach (var log in libraryService.AuditLog)
            {
                Console.WriteLine("  " + log);
            }

            // ---------------------------------------------------------
            // ТЕСТ 3: TPL + КОНТРОЛЬОВАНЕ СКАСУВАННЯ (CancellationToken)
            // ---------------------------------------------------------
            Console.WriteLine("\n=== 3. ПАРАЛЕЛЬНА ІНВЕНТАРИЗАЦІЯ (TPL) ТА СКАСУВАННЯ (CancellationToken) ===");
            var largeBatch = Enumerable.Range(1, 200)
                .Select(id => new Book(id, $"Том архіву #{id}"))
                .ToList();

            using (var cts = new CancellationTokenSource())
            {
                // Автоматично скасовуємо задачу через 220 мс
                cts.CancelAfter(220);

                try
                {
                    Console.WriteLine("Запуск паралельної перевірки 200 книг (таймаут скасування: 220 мс)...");
                    int processed = await libraryService.RunParallelInventoryAuditAsync(largeBatch, cts.Token);
                    Console.WriteLine($"Інвентаризацію завершено повністю. Перевірено: {processed}");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[TPL Cancellation]: Інвентаризацію коректно перервано за сигналом CancellationToken!");
                }
            }
        }
    }
}