## Student Information

- **Student Name:** Rabie Ali
- **Group / ID:** 3

## Phase 1 Questions & Answers

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
