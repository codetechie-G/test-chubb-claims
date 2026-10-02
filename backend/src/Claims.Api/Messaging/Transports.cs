using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;

namespace Claims.Api.Messaging;

public sealed class MessagingOptions
{
    /// <summary>"InMemory" (default, zero-dependency local run) or "Kafka".</summary>
    public string Transport { get; set; } = "InMemory";
    public string Topic { get; set; } = "claims.claim-events";
    public string BootstrapServers { get; set; } = "localhost:9092";
}

/// <summary>
/// Thin seam over the broker so the app runs locally without Kafka and the same consumers run
/// unchanged against a real cluster. Semantics we rely on: per-key ordering, at-least-once.
/// </summary>
public interface IMessageTransport
{
    Task PublishAsync(string key, string payload, CancellationToken ct);

    /// <summary>Runs until cancelled, invoking <paramref name="onMessage"/> for each message in the group.
    /// The offset is committed only after the callback returns.</summary>
    Task ConsumeAsync(string groupId, Func<string, CancellationToken, Task> onMessage, CancellationToken ct);
}

/// <summary>In-process transport: one unbounded channel per consumer group (fan-out like Kafka groups).</summary>
public sealed class InMemoryTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<string, Channel<string>> _groups = new();

    public InMemoryTransport(IEnumerable<ConsumerRegistration> registrations)
    {
        // Pre-create channels so nothing published before a consumer starts is lost.
        foreach (var r in registrations) _groups[r.GroupId] = Channel.CreateUnbounded<string>();
    }

    public async Task PublishAsync(string key, string payload, CancellationToken ct)
    {
        foreach (var channel in _groups.Values) await channel.Writer.WriteAsync(payload, ct);
    }

    public async Task ConsumeAsync(string groupId, Func<string, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        var channel = _groups.GetOrAdd(groupId, _ => Channel.CreateUnbounded<string>());
        await foreach (var payload in channel.Reader.ReadAllAsync(ct)) await onMessage(payload, ct);
    }
}

public sealed class KafkaTransport : IMessageTransport, IDisposable
{
    private readonly MessagingOptions _options;
    private readonly ILogger<KafkaTransport> _logger;
    private readonly IProducer<string, string> _producer;

    public KafkaTransport(MessagingOptions options, ILogger<KafkaTransport> logger)
    {
        _options = options;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
    }

    public async Task PublishAsync(string key, string payload, CancellationToken ct) =>
        await _producer.ProduceAsync(_options.Topic, new Message<string, string> { Key = key, Value = payload }, ct);

    public Task ConsumeAsync(string groupId, Func<string, CancellationToken, Task> onMessage, CancellationToken ct) =>
        // Confluent's Consume() is blocking; give each group its own long-running thread.
        Task.Factory.StartNew(async () =>
        {
            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(_options.Topic);
            _logger.LogInformation("Kafka consumer {Group} subscribed to {Topic}", groupId, _options.Topic);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var result = consumer.Consume(ct);
                        await onMessage(result.Message.Value, ct);
                        consumer.Commit(result);
                    }
                    catch (KafkaException ex) when (!ex.Error.IsFatal)
                    {
                        // Broker unavailable, topic not created yet, rebalance in progress... keep going.
                        _logger.LogWarning("Kafka consumer {Group}: {Reason}; retrying", groupId, ex.Error.Reason);
                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally { consumer.Close(); }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
