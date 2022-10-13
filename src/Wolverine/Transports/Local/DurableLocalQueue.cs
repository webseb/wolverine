using System;
using System.Threading.Tasks;
using Baseline;
using Microsoft.Extensions.Logging;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Wolverine.Runtime.WorkerQueues;
using Wolverine.Transports.Sending;

namespace Wolverine.Transports.Local;

internal class DurableLocalQueue : ISendingAgent, IDisposable, IListenerCircuit, ILocalQueue
{
    private readonly IMessageLogger _messageLogger;
    private readonly IEnvelopePersistence _persistence;
    private readonly IMessageSerializer _serializer;
    private readonly AdvancedSettings _settings;
    private readonly DurableReceiver _receiver;
    private readonly ILogger _logger;
    private Restarter? _restarter;
    private readonly WolverineRuntime _runtime;

    public DurableLocalQueue(Endpoint endpoint, WolverineRuntime runtime)
    {
        _settings = runtime.Advanced;
        _persistence = runtime.Persistence;
        _messageLogger = runtime.MessageLogger;
        _serializer = endpoint.DefaultSerializer ??
                      throw new ArgumentOutOfRangeException(nameof(endpoint),
                          "No default serializer for this Endpoint");
        Destination = endpoint.Uri;

        _runtime = runtime;

        Endpoint = endpoint;
        ReplyUri = TransportConstants.RepliesUri;

        _logger = runtime.Logger;

        if (endpoint.CircuitBreakerOptions != null)
        {
            CircuitBreaker = new CircuitBreaker(endpoint.CircuitBreakerOptions, this);
            Pipeline = new HandlerPipeline(runtime, new CircuitBreakerTrackedExecutorFactory(CircuitBreaker, runtime));
        }
        else
        {
            Pipeline = runtime.Pipeline;
        }

        _receiver = new DurableReceiver(endpoint, runtime, Pipeline);
    }

    public IHandlerPipeline Pipeline { get; }

    public CircuitBreaker? CircuitBreaker { get; }

    public Uri Destination { get; }

    public Endpoint Endpoint { get; }

    public Uri? ReplyUri { get; set; }

    public bool Latched { get; private set; }

    public bool IsDurable => true;

    public ValueTask EnqueueOutgoingAsync(Envelope envelope)
    {
        _messageLogger.Sent(envelope);

        _receiver.Enqueue(envelope);

        return ValueTask.CompletedTask;
    }

    public async ValueTask StoreAndForwardAsync(Envelope envelope)
    {
        _messageLogger.Sent(envelope);
        writeMessageData(envelope);

        // TODO -- have to watch this one
        envelope.Status = envelope.IsScheduledForLater(DateTimeOffset.UtcNow)
            ? EnvelopeStatus.Scheduled
            : EnvelopeStatus.Incoming;

        envelope.OwnerId = envelope.Status == EnvelopeStatus.Incoming
            ? _settings.UniqueNodeId
            : TransportConstants.AnyNode;

        try
        {
            await _persistence.StoreIncomingAsync(envelope);
        }
        catch (DuplicateIncomingEnvelopeException e)
        {
            _logger.LogError(e, "Duplicate incoming envelope detected");
            return; // Duplicate envelope, get out of here.
        }

        if (envelope.Status == EnvelopeStatus.Incoming)
        {
            _receiver.Enqueue(envelope);
        }
    }

    public bool SupportsNativeScheduledSend { get; } = true;


    private void writeMessageData(Envelope envelope)
    {
        if (envelope.Message is null)
        {
            throw new ArgumentOutOfRangeException(nameof(envelope), "Envelope.Message is null");
        }

        if (envelope.Data == null || envelope.Data.Length == 0)
        {
            _serializer.Write(envelope);
            envelope.ContentType = _serializer.ContentType;
        }
    }

    public void Dispose()
    {
        _receiver.Dispose();
        CircuitBreaker?.SafeDispose();
        _receiver?.SafeDispose();
    }

    public async ValueTask PauseAsync(TimeSpan pauseTime)
    {
        Latched = true;
        
        await _receiver.DrainAsync();

        CircuitBreaker?.Reset();

        _logger.LogInformation("Pausing message listening at {Uri}", _receiver.Uri);

        _restarter = new Restarter(this, pauseTime);

    }

    public ValueTask StartAsync()
    {
        Latched = false;
        _runtime.ListenerTracker.Publish(new ListenerState(_receiver.Uri, Endpoint.Name, ListeningStatus.Accepting));
        _restarter?.Dispose();
        _restarter = null;
        return ValueTask.CompletedTask;
    }

    ValueTask IReceiver.ReceivedAsync(IListener listener, Envelope[] messages)
    {
        return _receiver.ReceivedAsync(listener, messages);
    }

    ValueTask IReceiver.ReceivedAsync(IListener listener, Envelope envelope)
    {
        return _receiver.ReceivedAsync(listener, envelope);
    }

    ValueTask IReceiver.DrainAsync()
    {
        return _receiver.DrainAsync();
    }

    void ILocalQueue.Enqueue(Envelope envelope)
    {
        _receiver.Enqueue(envelope);
    }

    int ILocalQueue.QueueCount => _receiver.QueueCount;
}
