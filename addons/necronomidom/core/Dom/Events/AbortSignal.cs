using System;
using System.Collections.Generic;

namespace NecronomiDom.Dom
{
    /// <summary>DOM § 3.1 <c>AbortController</c>.</summary>
    public sealed class AbortController
    {
        public AbortSignal Signal { get; } = new();

        /// <summary>abort(reason) — "signal abort" on <see cref="Signal"/>; a null reason becomes an "AbortError" DomException.</summary>
        public void Abort(object? reason = null) => Signal.SignalAbort(reason);
    }

    /// <summary>
    /// DOM § 3.2 <c>AbortSignal</c> (abort algorithms, "abort" event). Dependent/composite signals
    /// (<c>AbortSignal.any</c>, <c>timeout</c>) are not implemented.
    /// </summary>
    public sealed class AbortSignal : EventTarget
    {
        private List<Action>? _algorithms;

        internal AbortSignal()
        {
        }

        public bool Aborted { get; private set; }

        public object? Reason { get; private set; }

        /// <summary>AbortSignal.abort(reason): a signal that is already aborted.</summary>
        public static AbortSignal Abort(object? reason = null)
        {
            var signal = new AbortSignal();
            signal.Aborted = true;
            signal.Reason = reason ?? new DomException("signal is aborted without reason", "AbortError");
            return signal;
        }

        /// <summary>throwIfAborted().</summary>
        public void ThrowIfAborted()
        {
            if (!Aborted) return;
            if (Reason is Exception ex) throw ex;
            throw new DomException("signal is aborted", "AbortError");
        }

        internal void AddAlgorithm(Action algorithm)
        {
            if (Aborted) return;
            (_algorithms ??= new List<Action>()).Add(algorithm);
        }
        internal void RemoveAlgorithm(Action algorithm) => _algorithms?.Remove(algorithm);

        /// <summary>"signal abort" (DOM § 3.2): run abort algorithms, then fire "abort".</summary>
        internal void SignalAbort(object? reason)
        {
            if (Aborted) return;
            Aborted = true;
            Reason = reason ?? new DomException("signal is aborted without reason", "AbortError");
            var algorithms = _algorithms;
            _algorithms = null;
            if (algorithms != null)
                foreach (var algorithm in algorithms) algorithm();
            var e = new Event("abort") { IsTrusted = true };
            EventDispatcher.Dispatch(e, this);
        }
    }
}
