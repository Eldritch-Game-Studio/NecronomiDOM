using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Css.Om;

namespace NecronomiDom.Dom
{
    /// <summary>CSSOM computed styles and HTML animation-frame callbacks for the pure document context.</summary>
    public sealed partial class Window : EventTarget
    {
        private readonly Dictionary<int, Action<double>> _animationFrames = new();
        private int _nextFrameId;
        private readonly List<int> _frameBatch = new();
        public Window(Document document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            StyleEngine = new StyleEngine(document);
            document.AttachDefaultView(this);
        }
        public Document Document { get; }
        public StyleEngine StyleEngine { get; }
        /// <summary>Real host-rendered text; null means the element has no render tree. HTML innerText uses textContent only for unrendered elements.</summary>
        public Func<HTMLElement, string?>? RenderedText { get; set; }
        public CSSStyleDeclaration GetComputedStyle(Element element, string? pseudoElement = null)
            => StyleEngine.GetComputedStyle(element, pseudoElement);
        public int RequestAnimationFrame(Action<double> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            int id = checked(++_nextFrameId);
            _animationFrames.Add(id, callback);
            return id;
        }
        public void CancelAnimationFrame(int id) => _animationFrames.Remove(id);
        /// <summary>Called by the host or test harness at an animation-frame checkpoint; newly queued callbacks wait for the next frame.</summary>
        public void RunAnimationFrame(double timestampMilliseconds)
        {
            _frameBatch.Clear();
            foreach (int id in _animationFrames.Keys) _frameBatch.Add(id);
            foreach (int id in _frameBatch)
            {
                if (!_animationFrames.Remove(id, out var callback)) continue;
                callback(timestampMilliseconds);
            }
            MutationObserver.NotifyAll(Document);
        }
    }
}
