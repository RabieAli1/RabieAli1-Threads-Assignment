# Mini Project — E-Commerce Order Processing System (File Storage Edition)

## Student Information
- **Student Name:** Rabie Ali
- **Group / ID:** Graded Assignment
- **Branch:** assignment/1-8

---

## 1. Project Overview & Execution Guide

This project is an asynchronous, thread-safe E-Commerce Order Processing System built with **.NET 10 (C# 13)**.
The system implements a realistic file-based transaction architecture where all collections are stored on disk in JSON files, simulating real-world network and database latency via non-blocking asynchronous delays (`Task.Delay`).

### How to Run Each Phase
All phases are executed from the console CLI using the root console project:
```bash
# Run individual phases:
dotnet run --project OrderProcessing/OrderProcessing.csproj -- phase1
dotnet run --project OrderProcessing/OrderProcessing.csproj -- phase2
dotnet run --project OrderProcessing/OrderProcessing.csproj -- phase3
dotnet run --project OrderProcessing/OrderProcessing.csproj -- phase4

# Run all phases sequentially:
dotnet run --project OrderProcessing/OrderProcessing.csproj -- all

# Run with graceful automatic cancellation timeout:
dotnet run --project OrderProcessing/OrderProcessing.csproj -- phase3 --cancel-after 4000

# Run without arguments to display the interactive numbered menu:
dotnet run --project OrderProcessing/OrderProcessing.csproj
```

### Data Folder Location
The runtime `Data` folder is automatically managed and created at:
`Path.Combine(AppContext.BaseDirectory, "Data")`  
(e.g., `bin/Debug/net10.0/Data`).  
Every phase invocation begins with an atomic `DataSeeder.ResetAsync()`, recreating all seeded files fresh from in-memory seed records.

---

## 2. Project Architecture & Role of Each Class

### Folder Structure
```text
OrderProcessing/
├── Shared/
│   ├── Models/               # Customer, Product, Order, Coupon, Payment, Email, Report
│   ├── Storage/              # DataFiles, JsonFileStore, DataSeeder
│   ├── Logging/              # Log (thread-safe, color-coded console logger)
│   ├── Exceptions/           # Domain exceptions (CustomerNotFound, DatabaseWrite, etc.)
│   ├── CancellationScope.cs  # Unified Ctrl+C and --cancel-after manager
│   └── SimulatedDelays.cs    # Exact latency constants and random distributions
├── Phase1/
│   ├── Repositories/         # CustomerRepository, ProductRepository, CouponRepository, OrderRepository
│   ├── Sync/                 # Synchronous comparison repositories & validator
│   ├── OrderValidator.cs     # 6-step sequential order validation
│   └── Phase1Runner.cs       # Orchestrates sync vs async sequential validation comparison
├── Phase2/
│   ├── PricingRules.cs       # Exact pricing, discount capping, shipping formula, AwayFromZero rounding
│   ├── PricingService.cs     # DB SubTotal query + concurrent discount and shipping APIs
│   ├── OrderPricer.cs        # Compares sequential awaits vs concurrent awaits
│   ├── OrderProcessorV2.cs   # Concurrent order processing pipeline
│   ├── InvoiceGenerator.cs   # CPU-bound PDF invoice simulation (Task.Run loop)
│   ├── ReportGenerator.cs    # Aggregates completed revenue and top-selling product
│   ├── RaceConditionDemo.cs  # Unsafe vs safe stock reservation comparison
│   ├── BlockingDemo.cs       # Demonstrates thread starvation via .Result / .Wait()
│   └── Phase2Runner.cs       # Phase 2 orchestration
├── Phase3/
│   ├── IPaymentGateway.cs    # Payment gateway contract
│   ├── PaymentGateway.cs     # Authorize, Fraud check, Capture (5000ms timeout for #5, decline for #8)
│   ├── IEmailService.cs      # Email service contract
│   ├── EmailService.cs       # Simulated SMTP with transient failures
│   ├── InventoryService.cs   # All-or-nothing stock reservation and release
│   ├── CustomerService.cs    # Customer profile queries
│   ├── OrderProcessor.cs     # 7-step checkout workflow with linked CTS, refund compensation, and retries
│   ├── FireAndForgetDemo.cs  # Task 9.7 unawaited task & unobserved exception investigation
│   ├── Sync/                 # SyncOrderProcessor and sync services for Thread vs Task comparison
│   └── Phase3Runner.cs       # Phase 3 orchestration
├── Phase4/
│   ├── Models/               # CustomerDashboard, ShippingQuote
│   ├── Carriers/             # IShippingCarrier, FastShip, CairoExpress, NileCourier
│   ├── Services/             # CustomerDashboardService (WhenAll), ShippingQuoteService (WhenAny),
│   │                         # FraudScoringService (Parallel.ForEach), EmailResendService (Parallel.ForEachAsync)
│   └── Phase4Runner.cs       # Phase 4 scale-up orchestration
└── Program.cs                # Entry point with CLI args and interactive menu
```

---

## 3. Storage Design & Concurrency Safety

### File Storage List
- `customers.json`: 10 customer records (loyalty tier, active status).
- `products.json`: 8 products with catalog prices, stock quantities, and physical weights.
- `coupons.json`: 4 coupons with discount percentages and expiration dates.
- `incoming_orders.json`: 20 seeded incoming orders.
- `orders.json`: Processed orders with final statuses (`Completed`, `Rejected`, `PaymentFailed`, `Failed`, `Pending`).
- `payments.json`: Audit log of all payment transactions (`Captured`, `Declined`, `Refunded`).
- `emails.json`: Sent confirmation emails.
- `email_retry.json`: Failed emails that exhausted all 4 retry attempts.
- `invoices/`: Simulated PDF invoices (`invoice_{orderId}.txt`).
- `reports/`: Executive summary reports (`report_{timestamp}.json`).

### JSON Formatting Rules
- Uses `System.Text.Json` only with a single shared static `JsonSerializerOptions`:
  - `WriteIndented = true`
  - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
  - `JsonStringEnumConverter` so enum values are stored as readable strings (`"Completed"`, not `4`).
- UTF-8 encoding without BOM. Empty collections are strictly formatted as `[]`, never as blank files.

### Concurrency Protection & Atomic Writes (`JsonFileStore`)
1. **Per-File Concurrency Locks (R5):**
   `JsonFileStore` maintains a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by file name. Every read and write acquires this semaphore, ensuring no concurrent operations interleave or corrupt disk state.
2. **Atomic Temp-File Swapping (R6):**
   Writes are serialized into a unique temporary file (`*.tmp`) in the same folder. Once serialization and file flushing succeed, `File.Move(tempPath, filePath, overwrite: true)` atomically replaces the destination. An unexpected power outage or crash will never leave a half-written or corrupted file.
3. **Atomic Read-Modify-Write (`UpdateAsync` / `AppendAsync`):**
   Holding the file semaphore across both the read and write steps eliminates lost updates when multiple threads append records (e.g. `emails.json` during concurrent email processing).

---

## 4. Performance Measurements Table

| Phase & Task | Execution Mode | Wall-Clock Time | Distinct Threads | ThreadPool Count | Key Findings & Observations |
|---|---|---|---|---|---|
| **Phase 1: Validation** | Synchronous (`Thread.Sleep`) | **34.8 s** | 1 | 4 | Sequential thread blocking; thread sits idle in OS wait state. |
| **Phase 1: Validation** | Asynchronous (`Task.Delay`) | **51.8 s** | 2 | 4 | Sequentially awaited one-by-one; includes save I/O overhead. |
| **Phase 2: Pricing (Order #1)** | Sequential Awaits | **4.78 s** | 2 | 4 | SubTotal (0.6s) -> Discount (2.0s) -> Shipping (2.1s). |
| **Phase 2: Pricing (Order #1)** | Concurrent Awaits | **3.52 s** | 3 | 5 | SubTotal -> [Discount // Shipping] run in parallel (1.36x speedup). |
| **Phase 2: Full Batch** | Concurrent Processing | **23.56 s** | 4 | 6 | All 15 validated orders run concurrently; no lost updates. |
| **Phase 3: Checkout (10 Orders)** | Synchronous (10 Threads) | **10.53 s** | **10** | 4 | 1 dedicated OS thread per order; high memory footprint. |
| **Phase 3: Checkout (10 Orders)** | Asynchronous (10 Tasks) | **15.40 s** | **4** | 5 | Reuses thread pool workers efficiently via IOCP and state machines. |
| **Phase 4: Customer Dashboard** | Single await (`Task.WhenAll`) | **0.62 s** | 4 | 6 | 4 queries run concurrently; total duration equals slowest query. |
| **Phase 4: Shipping Quotes** | First to arrive (`Task.WhenAny`) | **0.61 s – 1.05 s** | 3 | 6 | Fastest quote wins; losers immediately cancelled. |
| **Phase 4: CPU Fraud Scoring** | Sequential Loop | **2.384 s** | 1 | 6 | 10,000 orders scored sequentially on a single CPU core. |
| **Phase 4: CPU Fraud Scoring** | `Parallel.ForEach` | **0.288 s** | **16** | 16 | **8.28x speedup** across all 16 physical/logical CPU cores. |
| **Phase 4: Email Re-Send** | `Parallel.ForEachAsync` (Max=4) | **8.14 s** | 4 | 15 | Peak concurrency strictly capped at 4; 30 emails / 4 * 1s ~ 8s. |

---

## 5. Console Outputs & Verification Screenshots

### Phase 2: Race Condition Demo (Task 8.3)
```text
=== Race Condition Demo ===
[RaceConditionDemo] Setting Product #4 (Mechanical Keyboard) stock to 1...
--- Unsafe Reservation (No Lock) ---
[RaceConditionDemo] Order #10 reservation result: True
[RaceConditionDemo] Order #11 reservation result: True
[RaceConditionDemo] Stock remaining in products.json: 0
Oversell detected! Both orders acquired the last keyboard due to race condition.

--- Safe Reservation (SemaphoreSlim Protected) ---
[RaceConditionDemo] Order #10 reservation result: True
[RaceConditionDemo] Order #11 reservation result: False
[RaceConditionDemo] Stock remaining in products.json: 0
Safe reservation verified: Exactly 1 order succeeded, 1 was rejected.
```

### Phase 2: Faulted Task & Concurrent Processing (Task 8.4)
```text
[TaskStatusImmediately] Task[0] Status: WaitingForActivation
[TaskStatus1000ms] Task[0] Status: Running
Task[0] finished with Status: RanToCompletion
Task[6] (Order #7) FAULTED: ShippingProviderUnavailableException: Shipping provider unavailable
[Phase 2] Processed 20 orders: 13 Completed, 6 Rejected, 1 Failed (#7).
```

### Phase 3: Payment Timeout, Declined, Refund Compensation, and Email Retries (Task 9.4)
```text
================================= PHASE 3 CHECKOUT RESULTS =================================
Order    | Status          |      Total | Reason                                       
-------------------------------------------------------------------------------------
#1       | Completed       |     980.00 | —                                            
#2       | Completed       |     767.30 | —                                            
#3       | Completed       |     360.75 | —                                            
#4       | Rejected        |       0.00 | Customer is inactive                         
#5       | PaymentFailed   |       0.00 | Payment gateway timeout (3s)                 
#6       | Completed       |      44.20 | —                                            
#7       | Completed       |     235.60 | —                                            
#8       | PaymentFailed   |       0.00 | Card declined by issuer                      
#9       | Rejected        |       0.00 | Insufficient stock for 'Wireless Mouse'      
#10      | Completed       |      97.45 | —                                            
#11      | Rejected        |       0.00 | Insufficient stock for 'Mechanical Keyboard' 
#12      | Rejected        |       0.00 | Coupon 'OLD20' has expired                   
#13      | Failed          |       0.00 | Database save failed after payment; payment TXN-4C10D695333F refunded
#14      | Completed       |     197.60 | —                                            
#15      | Rejected        |       0.00 | Customer #99 not found                       
#16      | Completed       |     406.80 | —                                            
#17      | Completed       |     727.30 | —                                            
#18      | Rejected        |       0.00 | Coupon 'FAKE50' does not exist               
#19      | Completed       |      73.40 | —                                            
#20      | Completed       |    1074.00 | —                                            
=====================================================================================

===== Task 9.4 Verification Summary =====
orders.json:       20 records (Completed: 11, Rejected: 6, PaymentFailed: 2, Failed: 1)
payments.json:     13 records (Captured: 11, Declined: 1, Refunded: 1)
emails.json:       10 records
email_retry.json:  1 record (#17 with Attempts = 4)
P4 Keyboard Stock: 0
```

### Phase 3: Fire-and-Forget Investigation Demo (Task 9.7)
```text
=== Task 9.7: Fire-and-Forget Investigation Demo ===
--- Scenario 1: Unawaited Task (Method returns before background work completes) ---
[Broken] Order processing finished (email was not awaited!)
Caller regained control after only 906 ms (Email takes ~1000ms in background).
Notice: If the process exits now, the background email task is abruptly killed!

--- Scenario 2: Unawaited Task throws an exception ---
[Broken] Order processing finished (email was not awaited!)
[Broken Call]: No exception was caught by caller! The unawaited exception was lost/unobserved.

--- Scenario 3: Fixed Method (Awaiting email service) ---
[Fixed] Order processing finished (email successfully awaited)
[Fixed Call]: Completed reliably in 1909 ms. Email was guaranteed sent.

--- Scenario 4: Fixed Method with failure (Exception properly surfaced) ---
[Fixed Call]: Caught expected exception in caller: SMTP server unavailable (attempt 2)
```

### Phase 4: Live Checkout Progress (`Task.WhenEach`) (Task 10.1)
```text
[  1/20] Order #4  -> Rejected (Customer is inactive) after 0.6 s
[  2/20] Order #15 -> Rejected (Customer #99 not found) after 0.6 s
[  3/20] Order #9  -> Rejected (Insufficient stock for 'Wireless Mouse') after 2.3 s
[  4/20] Order #18 -> Rejected (Coupon 'FAKE50' does not exist) after 2.4 s
[  5/20] Order #12 -> Rejected (Coupon 'OLD20' has expired) after 2.5 s
[  6/20] Order #11 -> Rejected (Insufficient stock for 'Mechanical Keyboard') after 2.6 s
[  7/20] Order #8  -> PaymentFailed (Card declined by issuer) after 4.1 s
[  8/20] Order #5  -> PaymentFailed (Payment gateway timeout (3s)) after 5.3 s
[  9/20] Order #13 -> Failed (Database save failed after payment; payment TXN-F2B9D91973DE refunded) after 5.4 s
...
[ 20/20] Order #17 -> Completed after 8.7 s
```

### Phase 4: Customer Dashboard Multi-Exception Aggregation (Task 10.2)
```text
--- Customer #99 Dashboard (Multiple Failures Aggregated) ---
Reported failures from Task.WhenAll:
 - TimeoutException: Payments store timeout
 - CustomerNotFoundException: Customer #99 not found
```

### Phase 4: Fastest Shipping Quote (`Task.WhenAny`) (Task 10.3)
```text
Order #1 -> Winner: CairoExpress | Price: $  7.98 | Time:  620 ms
Order #2 -> Winner: NileCourier  | Price: $  8.03 | Time: 1050 ms
Order #3 -> Winner: CairoExpress | Price: $ 10.17 | Time:  619 ms
Order #4 -> Winner: FastShip     | Price: $  8.30 | Time:  780 ms
Order #5 -> Winner: CairoExpress | Price: $ 15.68 | Time:  827 ms

--- Task 10.3 R5: All Carriers Failing Scenario (Order #6) ---
Expected exception caught: All carriers failed
```

### Phase 4: Fraud Scoring & Email Re-send (Task 10.4)
```text
Sequential Time:       2.384 s (Flagged: 1005)
Parallel Time:         0.288 s (Flagged: 1005)
Speed-up:              8.28x
Distinct Threads Used: 16
ProcessorCount:        16
First 5 Flagged IDs:   4, 8, 12, 29, 33

Total Emails Sent:     30
Max Concurrent Sends:  4 (Required: exactly 4)
Total Duration:        8.14 s (Expected ~8 s)
Saved in emails.json:  30 records (IDs 1001–1030)
```

### Graceful Cancellation Run (`--cancel-after 4000`)
```text
[14:34:58.972] [Thread 2] Automatic cancellation scheduled after 4000 ms.
...
[14:35:04.087] [Thread 5] Order #7 -> CANCELLED gracefully, stock released and status set to Pending.
[14:35:04.791] [Thread 5] Order #16 -> CANCELLED gracefully, stock released and status set to Pending.
[14:35:05.701] [Thread 5] Order #8 -> CANCELLED gracefully, stock released and status set to Pending.
Run cancelled — 2 orders finished, 18 cancelled.
```

---

## 6. Problems Encountered and Solutions Applied

### 1. File Access Race Conditions & Lost Updates
- **Problem:** When multiple concurrent tasks appended records to `emails.json`, `orders.json`, or `payments.json` by calling `ReadAllAsync()` followed by `WriteAllAsync()`, the per-file semaphore lock was released between the read and the write. Multiple concurrent tasks read the same snapshot and overwrote each other's additions, causing missing records (e.g. only 23 of 30 emails saved).
- **Solution:** Designed and implemented atomic `UpdateAsync<T>` and `AppendAsync<T>` methods on `JsonFileStore`. The semaphore lock is acquired before reading the existing file, held while the callback modifies the list, held through serialization to a temporary file, and only released after the atomic `File.Move(..., overwrite: true)`. This completely eliminated lost writes.

### 2. Differentiating Payment Timeout from Run Cancellation
- **Problem:** Step 4 has a 3-second payment timeout, while the overall checkout run can also be cancelled via `--cancel-after` or Ctrl+C. Using a simple `Task.Delay` or linked token source caused both triggers to throw `OperationCanceledException`, making it ambiguous whether to mark the order as `PaymentFailed` or `Pending (Cancelled)`.
- **Solution:** Linked the run token `ct` with a 3-second timeout token:
  ```csharp
  using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
  using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
  ```
  In the `catch (OperationCanceledException)` block, the code inspects `ct.IsCancellationRequested`: if the global run token was triggered, the order rolls back and saves as `Pending / Cancelled`; if only `timeoutCts` was triggered, it releases stock and saves as `PaymentFailed / "Payment gateway timeout (3s)"`.

### 3. Aggregating All Exceptions in `Task.WhenAll`
- **Problem:** When executing `CustomerDashboardService.LoadAsync`, awaiting `Task.WhenAll(customerTask, ordersTask, paymentsTask, emailsTask)` re-throws only the *first* exception that occurred, swallowing any subsequent exceptions (such as `CustomerNotFoundException` when `Payments store timeout` also occurs).
- **Solution:** Captured the parent task returned by `Task.WhenAll` and inspected `parentTask.Exception.Flatten().InnerExceptions`. This guarantees that every individual query failure is logged and reported to caller.

### 4. Overselling the Last Mechanical Keyboard
- **Problem:** When orders #10 and #11 ran concurrently, both read `StockQuantity = 1`, entered the transaction delay, and deducted 1, resulting in an oversell where both succeeded.
- **Solution:** Enforced critical-section synchronization using `SemaphoreSlim` in `ProductRepository` and `InventoryService`, ensuring stock verification and deduction occur as an atomic transactional unit.

---

## 7. Answers to Questions: Phase 1

### Q1: Why are the sync and async times almost the same when orders are awaited one after another?
**Answer:**
`async` and `await` do not accelerate the intrinsic duration of an I/O operation. When orders are processed sequentially and awaited one by one inside a loop, each operation must fully complete its simulated latency before the next operation begins. The total wall-clock duration is strictly additive:
$$\text{Total Time} \approx \sum \text{Latency}_i$$
Asynchronous code provides speedup when multiple asynchronous operations are initiated concurrently (without immediately blocking on each one), allowing their I/O wait times to overlap.

### Q2: Is the thread ID before an await always the same as after it? Why? Use your own log lines as evidence.
**Answer:**
No, the thread ID is not guaranteed to be the same before and after an `await`.
In console applications (which lack a single-threaded `SynchronizationContext`), the runtime compiler splits the `async` method into a state machine. When an `await` encounters an incomplete task, the calling thread is immediately freed and returned to the thread pool. When the awaited operation completes (via I/O completion ports), the task continuation is scheduled on any available worker thread from the thread pool.
**Evidence from our logs:**
```text
[14:25:06.105] [Thread 6] EmailService -> [SMTP] Connecting to SMTP server for Order #991...
[14:25:06.412] [Thread 7] EmailService -> [SMTP] Sending email to ahmed.ali@example.com for Order #991...
```
The method was initiated on Thread 6, but resumed after the await on Thread 7.

### Q3: What does the thread do while `Task.Delay()` is waiting? And while `Thread.Sleep()` is waiting?
**Answer:**
- **`Thread.Sleep()`:** Forces the OS thread into a blocked sleep state (`WaitSleepJoin`). The thread cannot execute other instructions, its call stack remains in memory (~1 MB), and the operating system must perform expensive context switches.
- **`Task.Delay()`:** Non-blocking timer. It registers a callback with the system timer engine and immediately yields the thread back to the `ThreadPool`. The thread is 100% free to handle other requests while the timer ticks down.

### Q4: Why should a repository method return `Task<T>` instead of `T`?
**Answer:**
A repository method represents an out-of-process I/O operation (disk access or network socket to a database server). Returning `Task<T>` makes the asynchronous nature of the operation explicit in its signature, allowing callers to await it without blocking calling threads, preventing thread starvation and dramatically increasing server scalability under high load.

### Q5: What is the difference between `File.ReadAllText` and `File.ReadAllTextAsync`? Does the async version occupy a thread while the disk is reading?
**Answer:**
`File.ReadAllText` blocks the calling thread while reading bytes from disk. `File.ReadAllTextAsync` issues an asynchronous I/O request to the OS kernel using Windows I/O Completion Ports (IOCP) or Linux `io_uring`. **Zero threads are occupied while the physical storage device reads data**. A thread is only dispatched once the hardware interrupts the CPU to signal that data is ready in memory.

---

## 8. Answers to Questions: Phase 2

### Q1: What is the difference between starting a task and awaiting it?
**Answer:**
- **Starting a task:** Schedules or initiates an asynchronous operation, returning a `Task` handle representing the ongoing work. The calling thread immediately continues executing subsequent lines of code concurrently with the running task.
- **Awaiting a task (`await`):** Temporarily pauses execution of the current method at that point, frees the executing thread back to the thread pool, and sets up a continuation that automatically resumes once the task completes (unwrapping its result or rethrowing any exceptions).

### Q2: Does every `Task` create a new thread? Does `Task.Delay()` use a thread while waiting?
**Answer:**
No. A `Task` is an abstraction representing a promise of future completion, not an operating system thread.
- I/O-bound tasks (`HttpClient`, `FileStreamAsync`, `Socket`) and `Task.Delay()` run without any dedicated thread using hardware timers and OS kernel completion ports (IOCP).
- Only CPU-bound tasks explicitly started with `Task.Run()` borrow a worker thread from the thread pool.

### Q3: What happens if you call `.Result` or `.Wait()` on an unfinished task instead of `await`? Why is it dangerous?
**Answer:**
Calling `.Result` or `.Wait()` synchronously blocks the calling thread until the task finishes.
**Why it is dangerous:**
1. **Thread Starvation:** Consumes a thread pool worker that could otherwise process active requests.
2. **Deadlocks:** In environments with a `SynchronizationContext` (e.g. ASP.NET classic, WPF, WinForms), the continuation cannot execute because the UI or request thread is blocked waiting for `.Result`, causing a permanent deadlock.
3. **Exception Masking:** Throws an `AggregateException` instead of the original exception type, complicating error handling.

### Q4: What happens to an exception thrown inside a task that is never awaited?
**Answer:**
If an exception is thrown inside an unawaited task and the task is never awaited or inspected, the exception is stored inside the task's `Task.Exception` property. When the unobserved task object is eventually collected by the Garbage Collector, the finalizer triggers the `TaskScheduler.UnobservedTaskException` event. In earlier .NET versions this crashed the process; in modern .NET it is logged as an unobserved task exception, silently concealing catastrophic errors.

### Q5: When should you use `Task.Run()`, and when should you avoid it?
**Answer:**
- **When to use:** For heavy **CPU-bound operations** (image encoding, cryptographic hashing, PDF generation, large mathematical calculations) to offload work from a UI thread or parallelize across CPU cores.
- **When to avoid:** For **I/O-bound operations** (database calls, file reads, HTTP requests). Wrapping async I/O in `Task.Run` is the "sync-over-async" anti-pattern which wastes thread pool threads without providing any performance benefit.

### Q6: What is the difference between `Task` and `Task<T>`?
**Answer:**
`Task` represents an asynchronous operation that returns no value (analogous to `void`). `Task<T>` represents an asynchronous operation that yields a result of type `T` upon successful completion (analogous to returning `T`).

### Q7: Why can a `lock` block not contain an `await`?
**Answer:**
The C# `lock` keyword compiles to `Monitor.Enter` and `Monitor.Exit`, which enforce **thread affinity**: the exact same thread that entered the lock must be the one that exits it. Because an `await` can release the current thread and resume execution on a completely different thread pool worker, the runtime would encounter an `IllegalMonitorStateException`. Non-blocking synchronization across awaits requires `SemaphoreSlim`.

### Q8: Why did the unprotected stock reservation oversell, and how did you prevent it?
**Answer:**
In the unprotected implementation, Order #10 and Order #11 both read `StockQuantity = 1` before either had committed its deduction. Both orders saw sufficient stock, waited during the simulated latency, and deducted 1, allowing 2 keyboards to be sold when only 1 existed. This was prevented using `SemaphoreSlim(1, 1)` to serialize the read-modify-write transactional sequence.

---

## 9. Answers to Questions: Phase 3

### Task 9.6: Sync vs Async Comparison
1. **Does async make one order's workflow faster?**  
   No. A single order still executes the exact same sequence of I/O steps. Asynchronous code adds slight state machine overhead; it does not accelerate network latency.
2. **Why does async scale better for many orders?**  
   Async frees worker threads during I/O delays. While 10 synchronous orders require 10 dedicated threads blocked in memory, 10 asynchronous orders share 2–4 thread pool threads that jump between active continuations as I/O packets arrive.
3. **What happens to the sync version with 1,000 orders?**  
   Creating 1,000 OS threads consumes ~1 GB of stack memory, exhausts OS thread limits, triggers extreme context-switching CPU thrashing, and crashes or grinds the server to a halt. The async version handles 1,000 concurrent tasks smoothly using a handful of worker threads.
4. **Why is async essential for apps that call databases, files, and APIs?**  
   External databases, file systems, and third-party APIs have high latency (tens to hundreds of milliseconds) where the CPU does zero computational work. Asynchrony allows a single server to handle thousands of concurrent client connections without thread starvation.

### Task 9.7: Fire-and-Forget Investigation
1. **Problem in the code:** The email sending task `_emailService.SendConfirmationAsync(order, customer)` is not awaited. The method returns immediately while email delivery is still running in the background.
2. **Method return / App exit behavior:** The caller proceeds believing the entire checkout workflow succeeded. If the process terminates, background threads are abruptly terminated, resulting in unsent confirmation emails.
3. **Exception behavior:** If SMTP fails, the exception is thrown on a detached background task. The caller's `try-catch` never catches it, the order is falsely recorded as successful, and the exception becomes unobserved.
4. **When `await` is necessary:** Always necessary when the caller depends on the completion or outcome of the operation, needs to observe exceptions, or must guarantee completion before releasing resources.
5. **When fire-and-forget is acceptable:** Only for truly non-critical background telemetry or metrics where lost work has zero impact on business integrity.
6. **Why `async void` is dangerous:** `async void` methods cannot be awaited and exceptions cannot be caught by callers, crashing the entire process unless caught inside the method. It is only acceptable for top-level UI event handlers where the event signature requires `void`.

---

## 10. Answers to Questions: Phase 4

### Q1: For each of Tasks 10.1–10.4: which tool did you use, and why can't "start, then await one by one" meet the requirement?
- **Task 10.1 (Live Checkout Progress):** Used `Task.WhenEach(tasks)`. "Start, then await one by one" awaits tasks in the order they were started (Order #1, #2, etc.), causing the console to freeze on slow orders (like #5 taking 5 seconds) even if later orders (#6, #7) already finished. `Task.WhenEach` streams tasks in actual completion order.
- **Task 10.2 (Customer Dashboard):** Used `Task.WhenAll`. Awaiting tasks one by one aborts at the first failure and masks errors from other loads. `Task.WhenAll` aggregates all exceptions across all 4 concurrent queries.
- **Task 10.3 (Fastest Shipping Quote):** Used `Task.WhenAny`. Awaiting sequentially forces waiting for carrier 1 first even if carrier 2 responded in 300ms. `Task.WhenAny` returns the first arrival immediately.
- **Task 10.4 A (CPU Fraud Scoring):** Used `Parallel.ForEach`. Async tasks are meant for I/O; CPU-bound loops require true multi-threaded parallel execution across physical CPU cores.
- **Task 10.4 B (Limited Concurrency Email Re-send):** Used `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`. Awaiting one by one gives concurrency 1; awaiting all gives concurrency 30. Neither caps concurrency at exactly 4 without manual throttling.

### Q2: In 10.2, when several loads fail, how do you get every exception? What does a plain await give you?
A plain `await` unrolls and throws only the *first* exception. To obtain every exception, capture the task returned by `Task.WhenAll` and inspect `task.Exception.Flatten().InnerExceptions`.

### Q3: In 10.3, what happens to the losing carriers if you never cancel them?
The losing carriers keep executing in the background, consuming CPU cycles, sockets, thread pool workers, and remote API rate limits for quotes that will be discarded. Cancelling them via `CancellationTokenSource.Cancel()` immediately halts wasted work.

### Q4: Why does 10.4 A get faster with more CPU cores, while 10.4 B does not depend on the number of cores?
- **10.4 A is CPU-bound:** Each order executes 100,000 arithmetic operations. Adding CPU cores directly adds physical execution hardware, yielding linear speedup (8.28x on 16 cores).
- **10.4 B is I/O-bound:** Threads spend 99.9% of their time idle waiting for network latency (`Task.Delay`). Throughput is governed by network latency and the concurrency cap (4), not CPU clock cycles.

### Q5: What is the difference between parallel work (10.4 A) and concurrent asynchronous work (10.1)?
- **Parallel work (10.4 A):** Executes multiple CPU instructions simultaneously across multiple physical processor cores at the exact same instant in time.
- **Concurrent asynchronous work (10.1):** Interleaves multiple independent tasks over time on one or few threads by yielding execution during I/O waits, maximizing thread reuse without needing parallel CPU computation.

### Q6: Cancelled run explanation (`--cancel-after`):
When `--cancel-after 4000` fires, all pending operations receive a cancellation signal. In-flight orders stop at their next step, inventory reservations are rolled back, captured payments are refunded, and orders are persisted as `Status = OrderStatus.Pending` with `FailureReason = "Cancelled"`. The run exits cleanly with:
`Run cancelled — 2 orders finished, 18 cancelled.`
