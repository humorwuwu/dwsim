using System;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Client;

namespace DWSIM.H2sim.Io
{
    /// <summary>Subscribes to the PLC variables read into the model (FromPlc signals).</summary>
    internal sealed class OpcUaSubscriptionManager : IDisposable
    {
        private readonly Session _session;
        private readonly Action<Signal, DataValue> _onValue;
        private readonly Dictionary<string, Signal> _signals = new Dictionary<string, Signal>(StringComparer.Ordinal);
        private readonly Subscription _subscription;

        public OpcUaSubscriptionManager(Session session, IEnumerable<Signal> signals, Action<Signal, DataValue> onValue)
        {
            _session = session;
            _onValue = onValue;

            _subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };

            session.AddSubscription(_subscription);

            foreach (var signal in signals)
            {
                _signals[signal.Id] = signal;

                var item = new MonitoredItem(_subscription.DefaultItem)
                {
                    StartNodeId = OpcUaNodeResolver.Resolve(signal.OpcNodeId, signal.OpcNamespaceUri, session.NamespaceUris),
                    AttributeId = Attributes.Value,
                    DisplayName = signal.Id,
                    SamplingInterval = 100,
                    QueueSize = 1,
                    DiscardOldest = true
                };

                item.Notification += OnNotification;
                _subscription.AddItem(item);
            }
        }

        public void Start()
        {
            _subscription.Create();
            _subscription.ApplyChanges();
        }

        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            if (!(e.NotificationValue is MonitoredItemNotification notification)) return;
            if (_signals.TryGetValue(item.DisplayName, out var signal)) _onValue?.Invoke(signal, notification.Value);
        }

        public void Dispose()
        {
            try { _subscription.Delete(true); } catch { }
            try { _session.RemoveSubscription(_subscription); } catch { }
            try { _subscription.Dispose(); } catch { }
        }
    }
}