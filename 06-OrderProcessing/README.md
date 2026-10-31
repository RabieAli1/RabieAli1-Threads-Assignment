# Mini Project — E-Commerce Order Processing System (File Storage Edition)

## Student Information
- **Student Name:** Rabie Ali
- **Group / ID:** Graded Assignment
- **Branch:** assignment/1-8

---

## 1. Project Overview & Architecture
This project is an asynchronous, thread-safe E-Commerce Order Processing System built with .NET 10.
Every collection is stored in a JSON file on disk, simulating database and external network latency using strictly measured non-blocking delays (`Task.Delay`).

### Folder Structure
```text
OrderProcessing/
├── Shared/
│   ├── Models/               # Customer, Product, Order, Coupon, Payment, Email, Report
│   ├── Storage/              # DataFiles, JsonFileStore, DataSeeder
│   ├── Logging/              # Log (synchronized, color-coded console logger)
│   ├── Exceptions/           # Custom domain exceptions
│   ├── CancellationScope.cs  # Unified Ctrl+C and --cancel-after manager
│   └── SimulatedDelays.cs    # Exact latency constants and random distributions
├── Phase1/
│   ├── Repositories/         # CustomerRepository, ProductRepository, CouponRepository, etc.
│   ├── Sync/                 # Synchronous comparison repositories & validator
│   ├── OrderValidator.cs     # 6-step sequential order validation
│   └── Phase1Runner.cs       # Orchestrates sync vs async sequential validation comparison
├── Phase2/                   # Concurrent processing, pricing, race conditions & reports
├── Phase3/                   # Full checkout workflow with payments, retries, and compensations
├── Phase4/                   # Scale-up, WhenEach, WhenAll, carriers, parallel fraud scoring
└── Program.cs                # Entry point with CLI args and interactive menu
```

### Storage Design
All files are stored under `Path.Combine(AppContext.BaseDirectory, "Data")`:
- `customers.json`: Customer profiles and loyalty tiers.
- `products.json`: Catalog prices and stock quantities.
- `coupons.json`: Active and expired discount coupons.
- `incoming_orders.json`: 20 seeded sample orders with `unitPrice = 0`.
- `orders.json`: Processed orders.
- `payments.json`: Payment transaction audit trail.
- `emails.json`: Email outbox.
- `email_retry.json`: Failed emails queued for retry.
- `invoices/`: Generated invoice text files.
- `reports/`: Generated summary reports.

---

## 2. Concurrency & Storage Safety (`JsonFileStore`)
- **Single Point of Storage Access:** `JsonFileStore` is the only class that performs I/O on JSON files.
- **Atomic File Writes (R6):** File writes serialize first to a unique temporary file (`*.tmp`) in the same directory, flush to disk, and then atomically replace the target file via `File.Move(..., overwrite: true)`. This guarantees no file is ever left partially written if an unexpected crash occurs.
- **Thread Synchronization (R5):** A `ConcurrentDictionary<string, SemaphoreSlim>` maintains a dedicated semaphore per file name. Any read or write operation waits for its file's lock, eliminating file access race conditions and lost updates.

---

## 3. Performance Measurements Table

| Phase & Operation | Mode | Time (s) | Notes / Details |
|---|---|---|---|
| **Phase 1: Validation** | Sync (Thread.Sleep) | 33.9 s | 20 orders sequentially validated (not saved) |
| **Phase 1: Validation** | Async (Task.Delay) | 52.5 s | 20 orders validated & saved to orders.json sequentially |
| **Phase 2: Pricing (Order #1)** | Sequential Awaits | 4.78 s | SubTotal -> Discount -> Shipping |
| **Phase 2: Pricing (Order #1)** | Concurrent Awaits | 3.52 s | SubTotal -> [Discount // Shipping] concurrently (1.36x speedup) |
| **Phase 2: Full Concurrent Batch** | Concurrent Tasks | TBD | Will be updated in Task 8.4 |
| **Phase 3: Checkout (10 orders)** | Sync (10 Threads) | TBD | Will be updated in Phase 3 |
| **Phase 3: Checkout (10 orders)** | Async (10 Tasks) | TBD | Will be updated in Phase 3 |
| **Phase 4: Customer Dashboard** | Single await (WhenAll) | TBD | Will be updated in Phase 4 |
| **Phase 4: Fastest Shipping Quote** | First to arrive (WhenAny) | TBD | Will be updated in Phase 4 |
| **Phase 4: Fraud Scoring** | Sequential vs Parallel | TBD | Will be updated in Phase 4 |
| **Phase 4: Email Re-send** | Limited concurrency (4 max) | TBD | Will be updated in Phase 4 |

---

## 4. Phase 1 Questions & Answers

### Q1: Why are the sync and async times almost the same when orders are awaited one after another?
**Answer:**
`async` and `await` do not accelerate the intrinsic duration of an I/O operation or delay. When orders are processed sequentially and awaited one by one inside a loop, each operation must fully complete its simulated latency before the next operation is scheduled. The total wall-clock duration is strictly additive:
$$\text{Total Time} \approx \sum \text{Latency}_i$$
Asynchronous code only delivers speedup when multiple asynchronous operations are initiated concurrently (without immediately blocking on each one), allowing their I/O wait times to overlap (as demonstrated in Phase 2).

### Q2: Is the thread ID before an await always the same as after it? Why? Use your own log lines as evidence.
**Answer:**
**No.** The thread ID before an `await` is often different from the thread ID after it.
When an asynchronous method awaits an uncompleted task, it yields control, releases the current thread back to the .NET `ThreadPool`, and attaches a continuation callback. When the awaited operation finishes, the thread pool picks any available worker thread to execute the continuation.

**Real Log Evidence from our run:**
```text
[13:22:11.286] [Thread 10] OrderRepository -> Opening connection...
[13:22:11.487] [Thread 10] OrderRepository -> Beginning transaction...
[13:22:11.587] [Thread 7]  OrderRepository -> Inserting/Updating Order #18 (Status: Rejected)...
```
Here, before `await Task.Delay(100)` the code executed on **Thread 10**. After the delay completed, the continuation resumed on **Thread 7**.

Another example on Order #20:
```text
[13:22:17.523] [Thread 7]  OrderRepository -> Inserting/Updating Order #20 (Status: Validated)...
[13:22:17.926] [Thread 10] OrderRepository -> Committing transaction...
```
Before `await Task.Delay(400)` the code was on **Thread 7**, and resumed after the await on **Thread 10**.

### Q3: What does the thread do while Task.Delay() is waiting? And while Thread.Sleep() is waiting?
**Answer:**
- **`Thread.Sleep()`:** The operating system thread enters a blocked sleep state (`ThreadState.WaitSleepJoin`). The thread is pinned, inactive, and cannot execute any other work. Its call stack and thread resources (~1 MB of stack memory) remain tied up in the OS scheduler while doing zero work.
- **`Task.Delay()`:** Leverages a lightweight system timer. When awaited, it registers a timer callback and immediately returns the calling thread to the `ThreadPool`. The thread is 100% free to handle other tasks or requests. No thread is blocked or wasted while waiting.

### Q4: Why should a repository method return Task<T> instead of T?
**Answer:**
Repository methods perform I/O operations against disks, databases, or remote services. Returning `Task<T>` explicitly models an asynchronous operation that completes in the future, allowing callers to non-blockingly `await` the result. Returning `T` directly would force synchronous execution, blocking a thread for the entire round-trip duration and severely reducing application throughput and scalability under load.

### Q5: What is the difference between File.ReadAllText and File.ReadAllTextAsync? Does the async version occupy a thread while the disk is reading?
**Answer:**
- **Difference:** `File.ReadAllText` issues a synchronous system call that halts the executing thread until the disk read finishes. In contrast, `File.ReadAllTextAsync` issues an asynchronous I/O request using OS completion mechanisms (such as Windows I/O Completion Ports - IOCP / overlapped I/O) and returns a `Task<string>`.
- **Does it occupy a thread?** **No.** While the physical storage controller reads data and transfers it via DMA (Direct Memory Access) into memory, zero CPU threads are allocated or blocked. When the transfer finishes, the hardware interrupt alerts the kernel, which queues an IOCP packet, prompting the .NET ThreadPool to awaken a thread only to handle the completion callback.

