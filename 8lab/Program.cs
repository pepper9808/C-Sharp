using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DomainModelBatchTaskDemo
{
    // =========================================================
    // 1. МОДЕЛЬ ПРЕДМЕТНОЇ ОБЛАСТІ (БІБЛІОТЕКА)
    // =========================================================
    public class BookItem
    {
        public int Id { get; }
        public string Title { get; }
        public int Pages { get; }
        public bool IsIndexed { get; set; }

        public BookItem(int id, string title, int pages)
        {
            Id = id;
            Title = title;
            Pages = pages;
        }
    }

    public class BatchProcessingSummary
    {
        public int TotalSubmitted { get; set; }
        public int SuccessfullyProcessed { get; set; }
        public bool WasCanceled { get; set; }
        public List<string> ProcessedLogs { get; set; } = new List<string>();
    }

    // =========================================================
    // 2. ОБРОБНИК ПАКЕТНИХ ЗАДАЧ НА ОСНОВІ TPL (Task.Run)
    // =========================================================
    public class BatchCatalogProcessor
    {
        public async Task<BatchProcessingSummary> ProcessBatchAsync(
            IEnumerable<BookItem> books,
            CancellationToken cancellationToken)
        {
            var bookList = books.ToList();
            int processedCount = 0;
            var resultsLog = new ConcurrentBag<string>();
            bool isCanceled = false;

            // Формуємо список однотипних задач через Task.Run для паралельної обробки
            List<Task> tasks = bookList.Select(book => Task.Run(async () =>
            {
                // Перевірка сигналу скасування перед початком обробки елемента
                cancellationToken.ThrowIfCancellationRequested();

                // Імітація обчислень / перевірки метаданих книги
                await Task.Delay(150, cancellationToken);

                // Повторна перевірка перед фіксацією результату
                cancellationToken.ThrowIfCancellationRequested();

                book.IsIndexed = true;
                int currentNumber = Interlocked.Increment(ref processedCount);

                string message = $"[Потік #{Thread.CurrentThread.ManagedThreadId:D2}] " +
                                 $"Оброблено ({currentNumber}/{bookList.Count}): \"{book.Title}\" ({book.Pages} стор.)";
                resultsLog.Add(message);

                // Вивід проміжного результату в реальному часі
                Console.WriteLine("  " + message);

            }, cancellationToken)).ToList();

            try
            {
                // Неблокуюче очікування завершення всіх паралельних задач
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Перехоплюємо скасування, щоб програма завершила роботу коректно і повернула проміжний звіт
                isCanceled = true;
            }

            return new BatchProcessingSummary
            {
                TotalSubmitted = bookList.Count,
                SuccessfullyProcessed = processedCount,
                WasCanceled = isCanceled,
                ProcessedLogs = resultsLog.ToList()
            };
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

            var processor = new BatchCatalogProcessor();

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 1: Повна паралельна обробка невеликого пакета
            // ---------------------------------------------------------
            Console.WriteLine("=== 1. ПАКЕТНА ПАРАЛЕЛЬНА ОБРОБКА (Успішне завершення) ===");
            var smallBatch = Enumerable.Range(1, 5)
                .Select(i => new BookItem(i, $"Том технічної документації #{i}", 200 + i * 50))
                .ToList();

            using (var cts1 = new CancellationTokenSource())
            {
                var summary1 = await processor.ProcessBatchAsync(smallBatch, cts1.Token);
                Console.WriteLine($"Підсумок: оброблено {summary1.SuccessfullyProcessed} із {summary1.TotalSubmitted}, скасовано: {summary1.WasCanceled}.\n");
            }

            // ---------------------------------------------------------
            // СЦЕНАРІЙ 2: Паралельна обробка з контрольованим скасуванням
            // ---------------------------------------------------------
            Console.WriteLine("=== 2. ПАКЕТНА ОБРОБКА З КОНТРОЛЬОВАНИМ СКАСУВАННЯМ (CancellationToken) ===");
            var largeBatch = Enumerable.Range(1, 20)
                .Select(i => new BookItem(100 + i, $"Архівне видання #{i}", 300 + i * 10))
                .ToList();

            using (var cts2 = new CancellationTokenSource())
            {
                // Надсилаємо сигнал скасування через 260 мс (частина задач встигне виконатися, решта — скасується)
                cts2.CancelAfter(260);
                Console.WriteLine("Запущено обробку 20 книг (сигнал скасування надійде через 260 мс)...\n");

                var summary2 = await processor.ProcessBatchAsync(largeBatch, cts2.Token);

                Console.WriteLine("\n--- Звіт після зупинки пакетної обробки ---");
                Console.WriteLine($"Статус скасування : {(summary2.WasCanceled ? "Перервано за сигналом CancellationToken" : "Виконано повністю")}");
                Console.WriteLine($"Встигло обробитися: {summary2.SuccessfullyProcessed} із {summary2.TotalSubmitted} книг.");
                Console.WriteLine("Програма коректно завершила роботу без аварійного падіння.");
            }
        }
    }
}