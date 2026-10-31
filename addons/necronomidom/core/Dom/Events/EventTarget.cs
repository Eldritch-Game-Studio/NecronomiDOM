using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>
    /// DOM § 2.7 <c>EventTarget</c>: the event listener list with add/remove an event listener and
    /// <see cref="DispatchEvent"/> (DOM § 2.9 dispatch, implemented in <see cref="EventDispatcher"/>).
    /// </summary>
    public class EventTarget
    {
        private List<EventListenerEntry>? _listeners;

        internal List<EventListenerEntry>? Listeners => _listeners;

        /// <summary>addEventListener(type, callback, options) — DOM § 2.7 "add an event listener".</summary>
        public void AddEventListener(string type, IEventListener? listener, EventListenerOptions? options = null)
        {
            var signal = options?.Signal;
            if (signal != null && signal.Aborted) return;
            if (listener == null) return;
            var capture = options?.Capture ?? false;
            var passive = options?.Passive ?? DefaultPassiveValue(type);
            _listeners ??= new List<EventListenerEntry>(2);
            foreach (var existing in _listeners)
                if (existing.Type == type && ReferenceEquals(existing.Callback, listener) && existing.Capture == capture) return;
            var entry = new EventListenerEntry(type, listener, capture, passive, options?.Once ?? false, signal);
            _listeners.Add(entry);
            if (signal != null)
            {
                entry.AbortAlgorithm = () => RemoveListenerEntry(entry);
                signal.AddAlgorithm(entry.AbortAlgorithm);
            }
        }

        /// <summary>addEventListener with the legacy boolean <c>useCapture</c> argument.</summary>
        public void AddEventListener(string type, IEventListener? listener, bool capture) =>
            AddEventListener(type, listener, new EventListenerOptions { Capture = capture });

        /// <summary>removeEventListener(type, callback, options) — only capture participates in matching.</summary>
        public void RemoveEventListener(string type, IEventListener? listener, EventListenerOptions? options = null) =>
            RemoveEventListener(type, listener, options?.Capture ?? false);

        public void RemoveEventListener(string type, IEventListener? listener, bool capture)
        {
            if (_listeners == null || listener == null) return;
            foreach (var existing in _listeners)
            {
                if (existing.Type == type && ReferenceEquals(existing.Callback, listener) && existing.Capture == capture)
                {
                    RemoveListenerEntry(existing);
                    return;
                }
            }
        }

        /// <summary>dispatchEvent(event) — DOM § 2.7: throws InvalidStateError when dispatching or uninitialized.</summary>
        public bool DispatchEvent(Event e)
        {
            if (e.DispatchFlag || !e.InitializedFlag) throw DomException.InvalidState("The event is already being dispatched or was not initialized.");
            e.IsTrusted = false;
            return EventDispatcher.Dispatch(e, this);
        }

        /// <summary>UA dispatch for translated native input and HTML focus steps, not dispatchEvent().</summary>
        internal bool DispatchUserEvent(Event e)
        {
            if (e.DispatchFlag || !e.InitializedFlag) throw DomException.InvalidState("The event is already being dispatched or was not initialized.");
            e.IsTrusted = true;
            return EventDispatcher.Dispatch(e, this);
        }

        /// <summary>"remove an event listener" (DOM § 2.7): marks it removed so in-flight dispatch skips it.</summary>
        internal void RemoveListenerEntry(EventListenerEntry entry)
        {
            entry.Removed = true;
            _listeners?.Remove(entry);
            if (entry.AbortAlgorithm != null) entry.Signal?.RemoveAlgorithm(entry.AbortAlgorithm);
            entry.AbortAlgorithm = null;
        }

        /// <summary>"get the parent" (DOM § 2.7); null for plain event targets.</summary>
        internal virtual EventTarget? GetTheParent(Event e) => null;

        /// <summary>Activation behavior hooks used by HTML (e.g. click on checkboxes); none by default.</summary>
        protected internal virtual bool HasActivationBehavior => false;

        protected internal virtual void ActivationBehavior(Event e)
        {
        }

        protected internal virtual void LegacyPreActivationBehavior()
        {
        }

        protected internal virtual void LegacyCanceledActivationBehavior()
        {
        }

        /// <summary>DOM § 2.7 "default passive value": wheel/touch listeners on the document, root or body are passive.</summary>
        private bool DefaultPassiveValue(string type)
        {
            if (type is not ("touchstart" or "touchmove" or "wheel" or "mousewheel")) return false;
            if (this is not Node node) return false;
            var doc = node.NodeDocument;
            return ReferenceEquals(doc, this) || ReferenceEquals(doc.DocumentElement, this) || ReferenceEquals(doc.Body, this);
        }
    }
}
