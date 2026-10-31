using System.Collections.Concurrent;

namespace ViewsAndEmails;

public sealed class AnalyticsAndEmailJobs
{
    public readonly ConcurrentDictionary<string, int> Views = new();
    public readonly ConcurrentQueue<string> EmailJobs = new();
    public readonly List<string> SentEmails = new();

    readonly object _sentSync = new();
    public volatile bool ProducersDone;

    // public void RecordView(string product)
    // {
    //     if (!Views.ContainsKey(product))
    //         Views.TryAdd(product, 0);

    //     Views[product] = Views[product] + 1;
    // }
    public void RecordView(string product)
    {
        Views.AddOrUpdate(
            product,
            addValue: 1,
            updateValueFactory: (_, oldVal) => oldVal + 1
        );
    }



    public void PlaceOrder(int orderId)
    {
        EmailJobs.Enqueue($"Email: order {orderId}");
    }

    public void EmailWorker()
    {
        while (true)
        {
            if (EmailJobs.TryDequeue(out string? job))
            {
                lock (_sentSync)
                {
                    SentEmails.Add(job);
                }
                continue;
            }

            if (ProducersDone && EmailJobs.IsEmpty)
            {
                return;
            }

            Thread.Sleep(1);
        }
    }
}
