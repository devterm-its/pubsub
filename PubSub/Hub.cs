﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PubSub
{
    public sealed class Hub
    {
        internal readonly List<Handler> _handlers = new();
        private readonly object _lock = new();
        private readonly ILogger<Hub> _logger;

        public Hub(ILogger<Hub> logger)
        {
            _logger = logger;
        }

        public async Task PublishAsync<T>(T data = default) {
            await Task.WhenAll(GetAliveHandlers<T>().Select(x => x.ExecuteAction(_logger, data)));
        }

        public void Subscribe<T>(object subscriber, Action<T> handler)
        {
            SubscribeDelegate<T>(subscriber, handler);
        }

        public void Subscribe<T>(object subscriber, Func<T, Task> handler)
        {
            SubscribeDelegate<T>(subscriber, handler);
        }

        public void Unsubscribe(object subscriber)
        {
            lock (_lock)
            {
                var query = _handlers.Where(a => a.Sender.Target != null &&
                                                 (!a.Sender.IsAlive || a.Sender.Target.Equals(subscriber)));

                foreach (var h in query.ToList())
                    _handlers.Remove(h);
            }
        }

        public void Unsubscribe<T>(object subscriber)
            => Unsubscribe<T>(subscriber, (Delegate) null);

        public void Unsubscribe<T>(object subscriber, Action<T> handler)
            => Unsubscribe<T>(subscriber, (Delegate) handler);

        public void Unsubscribe<T>(object subscriber, Func<T, Task> handler)
            => Unsubscribe<T>(subscriber, (Delegate) handler);

        private void Unsubscribe<T>(object subscriber, Delegate handler)
        {
            lock (_lock)
            {
                var query = _handlers.Where(a => a.Sender.Target != null &&
                                                 (!a.Sender.IsAlive || a.Sender.Target.Equals(subscriber) && a.Type == typeof(T)));

                if (handler != null)
                    query = query.Where(a => a.Action.Equals(handler));

                foreach (var h in query.ToList())
                    _handlers.Remove(h);
            }
        }

        private void SubscribeDelegate<T>(object subscriber, Delegate handler)
        {
            var item = new Handler
            {
                Action = handler,
                Sender = new WeakReference(subscriber),
                Type = typeof(T)
            };

            lock (_lock)
            {
                _handlers.Add(item);
            }
        }

        private IEnumerable<Handler> GetAliveHandlers<T>()
        {
            PruneHandlers();
            lock (_lock)
            {
                return _handlers
                    .Where(h => h.Type.GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo()))
                    .ToList();
            }
        }

        private void PruneHandlers()
        {
            lock (_lock)
            {
                for (int i = _handlers.Count - 1; i >= 0; --i)
                {
                    if (!_handlers[i].Sender.IsAlive)
                        _handlers.RemoveAt(i);
                }
            }
        }

        internal class Handler
        {
            public Delegate Action { get; init; }
            public WeakReference Sender { get; init; }
            public Type Type { get; init; }

            public async Task ExecuteAction<T>(ILogger logger, T data)  {
                try
                {
                    switch (Action)
                    {
                        case Action<T> action:
                            action(data);
                            break;
                        case Func<T, Task> func:
                            await func(data);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Could not deliver message of type {typeof(T)} to subscriber of type {Sender.Target?.GetType()}");
                }
            }
        }
    }
}
