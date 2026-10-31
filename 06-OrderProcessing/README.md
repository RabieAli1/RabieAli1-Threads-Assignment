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
| **Phase 2: Full Concurrent Batch** | Concurrent Tasks | 23.56 s | 15 validated orders processed concurrently (13 Completed, 1 Rejected keyboard race, 1 Failed shipping) |
| **Phase 3: Checkout (10 orders)** | Sync (10 Threads) | TBD | Will be updated in Phase 3 |

---

## 4. Task 8.5 Analysis: Task.Run for CPU-Bound vs. I/O-Bound Work

### Why `Task.Run()` fits Invoice Generation:
- **CPU-Bound Nature:** Rendering the invoice involves 20,000,000 intensive mathematical iterations. The CPU core is actively computing during this entire duration.
- **Offloading to Worker Threads:** Calling `Task.Run()` queues this heavy computation onto the .NET `ThreadPool`. On multi-core systems, this distributes the computational workload across all available CPU cores without stalling the initiating thread or blocking asynchronous workflow progression.

### Why `Task.Run()` is WRONG for Database and I/O Calls:
- **No CPU Work During I/O:** When executing database queries or reading files, the system is fundamentally waiting for storage hardware controllers or remote network packets.
- **Zero-Thread Async I/O (IOCP):** Genuine asynchronous APIs (`File.ReadAllTextAsync`, socket reads, etc.) rely on OS kernel completion ports (Windows IOCP). While the hardware transfers bytes into memory, **zero CPU threads are occupied or blocked**.
- **The "Sync-Over-Async" Anti-Pattern:** Wrapping a blocking I/O call in `Task.Run(() => db.Query())` consumes and ties up an entire `ThreadPool` worker thread whose only purpose is to sit idle in a blocked state. Under heavy traffic (such as Black Friday order volume), this induces **Thread Pool Starvation**, causing massive latency spikes and destroying server throughput.

---

## 5. Phase 1 Questions & Answers
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



---

## 6. Phase 2 Questions & Answers

### Q1: What is the difference between starting a task and awaiting it?
- **Starting a Task:** Initiates or schedules the asynchronous operation. The task begins executing in the background concurrently while the calling thread immediately continues executing subsequent code without waiting.
- **Awaiting a Task (`await task`):** Asynchronously pauses execution of the enclosing method until the awaited task completes. Crucially, it does not block the thread; it yields the thread back to the runtime/ThreadPool. When the task finishes, execution resumes, unpacking the result `T` directly or unwrapping any thrown exception.

### Q2: Does every Task create a new thread? Does Task.Delay() use a thread while waiting?
- **Does every Task create a new thread?** **No.** A `Task` is an abstraction representing an ongoing asynchronous operation or future result. CPU-bound tasks reuse worker threads from the .NET `ThreadPool`. Purely asynchronous I/O operations (such as file I/O or network requests) do not require any thread while waiting for hardware.
- **Does `Task.Delay()` use a thread while waiting?** **No.** `Task.Delay()` registers a callback with an OS system timer (e.g. Windows timer queue) and immediately yields the calling thread back to the `ThreadPool`. No thread is blocked or consumed during the delay period. When the timer expires, the thread pool assigns an available thread to resume execution.

### Q3: What happens if you call .Result or .Wait() on an unfinished task instead of await? Why is it dangerous? Write a small separate demo method — the only place .Result / .Wait() is allowed.
- **What happens:** Calling `.Result` or `.Wait()` forces the current thread to block synchronously until the task completes.
- **Why it is dangerous:**
  1. **Deadlocks:** In synchronization contexts that enforce a single thread (such as classic ASP.NET, WPF, or WinForms), the continuation of the async method must run on the original thread. Since that thread is blocked waiting for `.Result`, the continuation can never execute, resulting in a permanent deadlock.
  2. **Thread Pool Starvation:** Synchronously blocking worker threads wastes system resources, degrading scalability and causing queuing delays under high concurrent load.
  3. **Error Handling Complexity:** Exceptions are wrapped in an `AggregateException` rather than thrown directly as with `await`.
- **Demo Method:** Implemented in [`BlockingDemo.RunDemo()`](file:///c:/Users/RABIE%20ALI/Desktop/pro/RabieAli1-Threads-Assignment/06-OrderProcessing/OrderProcessing/Phase2/BlockingDemo.cs).

### Q4: What happens to an exception thrown inside a task that is never awaited?
- The exception is captured inside the `Task` object. If the task is never awaited and its `.Exception` property is never inspected, the exception remains unobserved. When the unobserved `Task` is eventually garbage collected, the finalizer triggers the `TaskScheduler.UnobservedTaskException` event. The failure goes completely unnoticed by application business logic, leading to silent failures and state inconsistencies.

### Q5: When should you use Task.Run(), and when should you avoid it?
- **When to Use:** Use `Task.Run()` for **CPU-bound operations** (such as complex mathematical algorithms, PDF rendering, image processing, or data compression) to offload heavy computation from the calling thread to a background `ThreadPool` worker thread.
- **When to Avoid:** Avoid `Task.Run()` for **I/O-bound operations** (such as database queries, network calls, or file reads). Wrapping blocking I/O calls in `Task.Run()` is the "Sync-Over-Async" anti-pattern, which needlessly ties up thread pool worker threads in an idle blocked state, leading to ThreadPool starvation.

### Q6: What is the difference between Task and Task<T>?
- `Task`: Represents an asynchronous operation that does not return a value (the asynchronous counterpart to `void`).
- `Task<T>`: A generic subclass of `Task` representing an asynchronous operation that produces a value of type `T` upon completion. Awaiting `Task<T>` extracts the return value `T`.

### Q7: Why can a lock block not contain an await?
- C# prohibits `await` inside a `lock` block (compiler error CS1996).
- **Reason:** C#'s `lock` is based on `Monitor.Enter` and `Monitor.Exit`, which strictly require **thread affinity** — the exact same thread that acquired the lock must be the one that releases it. When an `await` is encountered, the calling thread is released back to the `ThreadPool`. When the awaited operation completes, continuation may resume on a different thread. If this occurred inside a lock, the second thread would attempt to release a lock it did not acquire, throwing `SynchronizationLockException`.
- **Solution:** Use asynchronous synchronization primitives like `SemaphoreSlim.WaitAsync()` and `SemaphoreSlim.Release()`, which do not enforce thread affinity.

### Q8: Why did the unprotected stock reservation oversell, and how did you prevent it?
- **Why it oversold:** In `ReserveStockUnsafeAsync`, orders #10 and #11 ran concurrently. Both tasks read `StockQuantity = 1` from `products.json` before either task had written back its updated stock. During the 700 ms latency window (open connection 200ms + begin transaction 100ms + update 400ms), both tasks evaluated that stock was sufficient ($\ge 1$). Task 10 deducted 1 and saved stock = 0, and Task 11 (which had read 1 previously) also deducted 1 and saved stock = 0. Both orders returned `true`, resulting in 2 units sold when only 1 existed in inventory (a classic race condition and lost update).
- **How we prevented it:** We wrapped the entire read-modify-write operation with a `SemaphoreSlim(1, 1)` in `ReserveStockAsync`. This serialized access so that only one order could enter the reservation block at any time. Order #10 reserved the unit, reduced stock to 0, and committed. When Order #11 acquired the lock, it re-read the updated stock (0), detected that $0 < 1$, and correctly failed with `false`.
