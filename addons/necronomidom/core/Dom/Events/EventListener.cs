using System;

namespace NecronomiDom.Dom
{
    /// <summary>The WebIDL <c>EventListener</c> callback interface (DOM § 2.7).</summary>
    public interface IEventListener
    {
        void HandleEvent(Event e);
    }

    /// <summary>An <see cref="IEventListener"/> wrapping a delegate. Identity (for de-duplication and removal) is the wrapper instance.</summary>
    public sealed class DelegateEventListener : IEventListener
    {
        private readonly Action<Event> _callback;

        public DelegateEventListener(Action<Event> callback) => _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        public void HandleEvent(Event e) => _callback(e);
    }

    /// <summary><c>AddEventListenerOptions</c> (DOM § 2.7); also used for removal, where only <see cref="Capture"/> matters.</summary>
    public sealed class EventListenerOptions
    {
        public bool Capture { get; init; }

        public bool Once { get; init; }

        /// <summary>Null means "use the default passive value" (DOM § 2.7).</summary>
        public bool? Passive { get; init; }

        public AbortSignal? Signal { get; init; }
    }

    /// <summary>An event listener record (DOM § 2.7 "event listener").</summary>
    internal sealed class EventListenerEntry
    {
        public EventListenerEntry(string type, IEventListener callback, bool capture, bool passive, bool once, AbortSignal? signal)
        {
            Type = type;
            Callback = callback;
            Capture = capture;
            Passive = passive;
            Once = once;
            Signal = signal;
        }

        public string Type { get; }

        public IEventListener Callback { get; }

        public bool Capture { get; }

        public bool Passive { get; }

        public bool Once { get; }

        public AbortSignal? Signal { get; }
        internal Action? AbortAlgorithm { get; set; }

        public bool Removed { get; set; }
    }
}
