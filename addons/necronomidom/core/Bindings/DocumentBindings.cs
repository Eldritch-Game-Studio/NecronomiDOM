using System;
using System.Collections.Generic;
using System.ComponentModel;
using NecronomiDom.Dom;

namespace NecronomiDom.Bindings
{
    /// <summary>
    /// Explicit one-way bindings to a single document. Create, bind, notify and dispose on the owning UI thread.
    /// Selectors resolve once; replacing markup requires disposing this scope and binding the new document.
    /// </summary>
    public sealed class DocumentBindings : IDisposable
    {
        private Document? _document;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly List<IDisposable> _bindings = new();

        public DocumentBindings(Document document) => _document = document ?? throw new ArgumentNullException(nameof(document));

        public bool IsDisposed { get; private set; }
        public event Action? Disposed;

        public void Text<T>(string selector, T source, string sourceProperty, Func<T, string> read)
            where T : class, INotifyPropertyChanged
            => Add(selector, source, sourceProperty, read, "textContent", static (target, value) =>
            {
                if (value == null) throw new InvalidOperationException("The text getter returned null; return an empty string for empty text.");
                if (target.TextContent != value) target.TextContent = value;
            });

        public void Class<T>(string selector, string className, T source, string sourceProperty, Func<T, bool> read)
            where T : class, INotifyPropertyChanged
        {
            CheckAvailable();
            if (string.IsNullOrEmpty(className))
                throw Error(source, sourceProperty, selector, "class", new ArgumentException("A class token must not be empty.", nameof(className)));
            foreach (var character in className)
                if (DOMTokenList.IsSpace(character))
                    throw Error(source, sourceProperty, selector, "classList." + className, new ArgumentException("A class token must not contain ASCII whitespace.", nameof(className)));
            Add(selector, source, sourceProperty, read, "classList." + className, (target, value) => target.ClassList.Toggle(className, value));
        }

        /// <summary>Toggles the standard hidden attribute; the document's CSS must honor [hidden].</summary>
        public void Visible<T>(string selector, T source, string sourceProperty, Func<T, bool> read)
            where T : class, INotifyPropertyChanged
            => Add(selector, source, sourceProperty, read, "hidden", static (target, value) => target.ToggleAttribute("hidden", !value));

        /// <summary>Toggles disabled on HTML button, input or textarea elements only.</summary>
        public void Enabled<T>(string selector, T source, string sourceProperty, Func<T, bool> read)
            where T : class, INotifyPropertyChanged
            => Add(selector, source, sourceProperty, read, "disabled", static (target, value) => target.ToggleAttribute("disabled", !value), enabled: true);

        private void Add<T, TValue>(string selector, T source, string sourceProperty, Func<T, TValue> read,
            string targetProperty, Action<Element, TValue> apply, bool enabled = false)
            where T : class, INotifyPropertyChanged
        {
            CheckAvailable();
            Element target;
            try
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (string.IsNullOrWhiteSpace(sourceProperty)) throw new ArgumentException("Specify a source property name, normally using nameof.", nameof(sourceProperty));
                if (read == null) throw new ArgumentNullException(nameof(read));
                if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentException("Specify a target selector.", nameof(selector));
                target = _document!.QuerySelector(selector) ?? throw new InvalidOperationException("No element matches the target selector.");
                ValidateTarget(target);
                if (enabled && target is not (HTMLButtonElement or HTMLInputElement or HTMLTextAreaElement))
                    throw new InvalidOperationException("Enabled requires an HTML button, input or textarea target.");
            }
            catch (Exception error) { throw Error(source, sourceProperty, selector, targetProperty, error); }

            var binding = new Binding<T, TValue>(this, target, selector, source, sourceProperty, read, targetProperty, apply);
            _bindings.Add(binding);
            try { binding.Start(); }
            catch (Exception error)
            {
                _bindings.Remove(binding);
                try { binding.Dispose(); }
                catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
                if (error is BindingException) throw;
                throw binding.Error(error);
            }
        }

        private static BindingException Error<T>(T source, string property, string selector, string targetProperty, Exception error)
            => new(source == null ? typeof(T) : source.GetType(), property, selector, targetProperty, error);

        private void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("Document bindings must be accessed on their owning UI thread; marshal PropertyChanged notifications to that thread.");
        }

        private void CheckAvailable()
        {
            CheckThread();
            if (IsDisposed) throw new ObjectDisposedException(nameof(DocumentBindings));
        }

        private void ValidateTarget(Element target)
        {
            if (!ReferenceEquals(target.OwnerDocument, _document) || !target.IsConnected)
                throw new InvalidOperationException("The cached binding target is detached or belongs to a different document. Dispose this scope and rebind after replacing markup.");
        }

        public void Dispose()
        {
            CheckThread();
            if (IsDisposed) return;
            IsDisposed = true;
            _document = null;
            var disposed = Disposed;
            Disposed = null;
            List<Exception>? errors = null;
            foreach (var binding in _bindings)
            {
                try { binding.Dispose(); }
                catch (Exception error) { (errors ??= new()).Add(error); }
            }
            _bindings.Clear();
            if (disposed != null)
                foreach (Action callback in disposed.GetInvocationList())
                {
                    try { callback(); }
                    catch (Exception error) { (errors ??= new()).Add(error); }
                }
            if (errors != null) throw new AggregateException(errors);
        }

        private sealed class Binding<T, TValue> : IDisposable where T : class, INotifyPropertyChanged
        {
            private DocumentBindings? _owner;
            private Element? _target;
            private T? _source;
            private Func<T, TValue>? _read;
            private Action<Element, TValue>? _apply;
            private readonly Type _sourceType;
            private readonly string _sourceProperty;
            private readonly string _selector;
            private readonly string _targetProperty;
            private bool _subscribed;
            private bool _updating;
            private bool _pending;
            private bool _hasValue;
            private TValue? _value;

            internal Binding(DocumentBindings owner, Element target, string selector, T source, string sourceProperty,
                Func<T, TValue> read, string targetProperty, Action<Element, TValue> apply)
            {
                _owner = owner;
                _target = target;
                _selector = selector;
                _source = source;
                _sourceType = source.GetType();
                _sourceProperty = sourceProperty;
                _read = read;
                _targetProperty = targetProperty;
                _apply = apply;
            }

            internal BindingException Error(Exception error) => new(_sourceType, _sourceProperty, _selector, _targetProperty, error);

            internal void Start()
            {
                _subscribed = true;
                _source!.PropertyChanged += Changed;
                Update();
            }

            private void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (_owner == null) return;
                if (string.IsNullOrEmpty(args.PropertyName) || string.Equals(args.PropertyName, _sourceProperty, StringComparison.Ordinal)) Update();
            }

            private void Update()
            {
                if (_owner == null) return;
                try
                {
                    _owner.CheckThread();
                    if (_updating) { _pending = true; return; }
                    _updating = true;
                    try
                    {
                        do
                        {
                            _pending = false;
                            var owner = _owner;
                            if (owner == null) return;
                            var target = _target!;
                            owner.ValidateTarget(target);
                            var value = _read!(_source!);
                            // Getters may dispose the scope or synchronously notify a newer value.
                            if (_owner == null) return;
                            if (_pending) continue;
                            owner.ValidateTarget(target);
                            if (!_hasValue || !EqualityComparer<TValue>.Default.Equals(_value!, value))
                            {
                                _apply!(target, value);
                                if (_owner == null) return;
                                _value = value;
                                _hasValue = true;
                            }
                        } while (_pending);
                    }
                    finally { _updating = false; }
                }
                catch (Exception error) { throw Error(error); }
            }

            public void Dispose()
            {
                var source = _source;
                var subscribed = _subscribed;
                _owner = null;
                _target = null;
                _source = null;
                _read = null;
                _apply = null;
                _value = default;
                _hasValue = false;
                _subscribed = false;
                if (subscribed && source != null)
                {
                    try { source.PropertyChanged -= Changed; }
                    catch (Exception error) { throw Error(error); }
                }
            }
        }
    }
}
