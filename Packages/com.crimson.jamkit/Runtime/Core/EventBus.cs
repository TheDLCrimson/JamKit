using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Base interface for all game events fired through <see cref="EventBus"/>.
    /// </summary>
    public interface IGameEvent { }

    /// <summary>
    /// Internal interface for event bindings.
    /// </summary>
    internal interface IEventBinding { }

    /// <summary>
    /// Internal class to store event callbacks.
    /// </summary>
    internal class EventBinding<T> : IEventBinding where T : IGameEvent
    {
        public Action<T> Callback { get; }

        public EventBinding(Action<T> callback)
        {
            Callback = callback;
        }
    }

    /// <summary>
    /// Static, type-keyed publish/subscribe event bus for decoupled communication between game systems.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<IEventBinding>> _bindings = new();

        /// <summary>
        /// Clears all bindings at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> so subscribers
        /// from a previous Play session cannot survive when Enter Play Mode has "Reload Domain" disabled
        /// (static state is not reset automatically in that mode).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bindings.Clear();
        }

        /// <summary>
        /// Subscribe to an event type with a callback.
        /// </summary>
        public static void Subscribe<T>(Action<T> callback) where T : IGameEvent
        {
            Type eventType = typeof(T);

            if (!_bindings.ContainsKey(eventType))
            {
                _bindings[eventType] = new List<IEventBinding>();
            }

            EventBinding<T> binding = new(callback);
            _bindings[eventType].Add(binding);
        }

        /// <summary>
        /// Unsubscribe from an event type.
        /// </summary>
        public static void Unsubscribe<T>(Action<T> callback) where T : IGameEvent
        {
            Type eventType = typeof(T);

            if (!_bindings.TryGetValue(eventType, out List<IEventBinding> bindingList)) return;

            IEventBinding bindingToRemove = bindingList.Find(binding =>
                binding is EventBinding<T> eventBinding &&
                eventBinding.Callback.Equals(callback));

            if (bindingToRemove != null)
            {
                bindingList.Remove(bindingToRemove);

                if (bindingList.Count == 0)
                {
                    _bindings.Remove(eventType);
                }
            }
        }

        /// <summary>
        /// Fire an event to all subscribers. Exceptions thrown by one subscriber are logged and do not
        /// prevent the remaining subscribers from receiving the event.
        /// </summary>
        public static void Fire<T>(T eventData) where T : IGameEvent
        {
            Type eventType = typeof(T);

            if (!_bindings.TryGetValue(eventType, out List<IEventBinding> bindingList)) return;

            // Copy so a subscriber unsubscribing mid-fire doesn't mutate the list we're iterating.
            List<IEventBinding> bindingsCopy = new(bindingList);

            foreach (IEventBinding binding in bindingsCopy)
            {
                if (binding is EventBinding<T> eventBinding)
                {
                    try
                    {
                        eventBinding.Callback?.Invoke(eventData);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error executing event callback for {eventType.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Clear all event bindings for every event type. Called by <see cref="SceneLoader"/> on every
        /// scene transition to prevent stale subscribers from a torn-down scene leaking into the next one.
        /// </summary>
        public static void Clear()
        {
            _bindings.Clear();
        }

        /// <summary>
        /// Clear all bindings for a specific event type.
        /// </summary>
        public static void Clear<T>() where T : IGameEvent
        {
            _bindings.Remove(typeof(T));
        }

        /// <summary>
        /// Get the number of subscribers for an event type.
        /// </summary>
        public static int GetSubscriberCount<T>() where T : IGameEvent
        {
            Type eventType = typeof(T);
            return _bindings.TryGetValue(eventType, out List<IEventBinding> binding) ? binding.Count : 0;
        }

        /// <summary>
        /// Check if there are any subscribers for an event type.
        /// </summary>
        public static bool HasSubscribers<T>() where T : IGameEvent
        {
            return GetSubscriberCount<T>() > 0;
        }

        /// <summary>
        /// Number of event types that currently have at least one subscriber. Read-only introspection for
        /// the debug overlay; not a hot-path API.
        /// </summary>
        public static int ActiveEventTypeCount => _bindings.Count;

        /// <summary>
        /// Total number of live subscriber bindings across every event type. Read-only introspection for the
        /// debug overlay; not a hot-path API.
        /// </summary>
        public static int TotalSubscriberCount()
        {
            int total = 0;
            foreach (List<IEventBinding> bindingList in _bindings.Values)
            {
                total += bindingList.Count;
            }
            return total;
        }
    }
}
