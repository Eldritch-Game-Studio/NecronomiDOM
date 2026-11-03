using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NecronomiDom.Bindings;
using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Bindings;

[TestFixture]
public sealed class DocumentBindingsTests
{
    [Test]
    public void TextAppliesInitiallyAndOnlyReadsExactOrAllPropertyNotifications()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Initial" };
        var reads = 0;
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => { reads++; return model.Title; });
        Assert.That(target.TextContent, Is.EqualTo("Initial"));
        source.Notify(nameof(ViewModel.Active));
        source.Notify("title");
        Assert.That(reads, Is.EqualTo(1));
        source.Title = "Changed";
        Assert.That(target.TextContent, Is.EqualTo("Changed"));
        source.Notify(null);
        source.Notify("");
        Assert.That(reads, Is.EqualTo(4));
    }

    [Test]
    public void TextInsertsLiteralTextRatherThanMarkupAndPreservesUnchangedTextNode()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "<b>Not HTML</b>" };
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        var textNode = target.FirstChild;
        var observer = Observe(target);
        source.Notify(nameof(ViewModel.Title));
        Assert.Multiple(() =>
        {
            Assert.That(target.TextContent, Is.EqualTo("<b>Not HTML</b>"));
            Assert.That(target.ChildElementCount, Is.Zero);
            Assert.That(target.FirstChild, Is.SameAs(textNode));
            Assert.That(observer.TakeRecords(), Is.Empty);
        });
        source.Title = "Next";
        Assert.That(target.TextContent, Is.EqualTo("Next"));
        observer.Disconnect();
    }

    [Test]
    public void ClassPreservesOtherTokensAndVisibleUsesHiddenWithoutOverwritingStyles()
    {
        var (document, target) = Fixture();
        target.ClassName = "authored";
        target.SetAttribute("style", "display:flex");
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        bindings.Class("#target", "selected", source, nameof(ViewModel.Active), model => model.Active);
        bindings.Visible("#target", source, nameof(ViewModel.Active), model => model.Active);
        Assert.That(target.ClassName, Is.EqualTo("authored"));
        Assert.That(target.HasAttribute("hidden"), Is.True);
        var observer = Observe(target);
        source.Notify(nameof(ViewModel.Active));
        Assert.That(observer.TakeRecords(), Is.Empty);
        source.Active = true;
        Assert.Multiple(() =>
        {
            Assert.That(target.ClassList.Contains("authored"), Is.True);
            Assert.That(target.ClassList.Contains("selected"), Is.True);
            Assert.That(target.HasAttribute("hidden"), Is.False);
            Assert.That(target.GetAttribute("style"), Is.EqualTo("display:flex"));
        });
        observer.TakeRecords();
        source.Notify(null);
        Assert.That(observer.TakeRecords(), Is.Empty);
        source.Active = false;
        Assert.That(target.ClassName, Is.EqualTo("authored"));
        Assert.That(target.HasAttribute("hidden"), Is.True);
        observer.Disconnect();
    }

    [TestCase("button")]
    [TestCase("input")]
    [TestCase("textarea")]
    public void EnabledReflectsDisabledAndSkipsUnchangedWrites(string tag)
    {
        var (document, target) = Fixture(tag);
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        bindings.Enabled("#target", source, nameof(ViewModel.Active), model => model.Active);
        Assert.That(target.HasAttribute("disabled"), Is.True);
        var observer = Observe(target);
        source.Notify(nameof(ViewModel.Active));
        Assert.That(observer.TakeRecords(), Is.Empty);
        source.Active = true;
        Assert.That(target.HasAttribute("disabled"), Is.False);
        Assert.That(observer.TakeRecords().Count, Is.EqualTo(1));
        source.Active = false;
        Assert.That(target.HasAttribute("disabled"), Is.True);
        observer.Disconnect();
    }

    [TestCase("div")]
    [TestCase("select")]
    [TestCase("fieldset")]
    public void EnabledRejectsUnsupportedTargetsWithoutSubscribing(string tag)
    {
        var (document, target) = Fixture(tag);
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        var error = Assert.Throws<BindingException>(() => bindings.Enabled("#target", source, nameof(ViewModel.Active), model => model.Active))!;
        Assert.Multiple(() =>
        {
            Assert.That(error.TargetProperty, Is.EqualTo("disabled"));
            Assert.That(source.SubscriberCount, Is.Zero);
            Assert.That(target.HasAttribute("disabled"), Is.False);
        });
    }

    [Test]
    public void EditingInputCannotWriteToTheSource()
    {
        var (document, target) = Fixture("input");
        var input = (HTMLInputElement)target;
        var source = new ViewModel { Title = "Source text", Active = true };
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        bindings.Enabled("#target", source, nameof(ViewModel.Active), model => model.Active);
        var sourceNotifications = 0;
        source.PropertyChanged += (_, _) => sourceNotifications++;
        input.SetUserValue("Typed by player");
        input.DispatchEvent(new InputEvent("input"));
        input.DispatchEvent(new Event("change"));
        Assert.Multiple(() =>
        {
            Assert.That(input.Value, Is.EqualTo("Typed by player"));
            Assert.That(source.Title, Is.EqualTo("Source text"));
            Assert.That(source.Active, Is.True);
            Assert.That(sourceNotifications, Is.Zero);
        });
        source.Title = "New source text";
        Assert.That(input.TextContent, Is.EqualTo("New source text"));
        Assert.That(input.Value, Is.EqualTo("Typed by player"), "Text binds textContent, not a two-way value property");
    }

    [Test]
    public void SelectorIsResolvedOnceRatherThanFollowingANewMatchingElement()
    {
        var (document, original) = Fixture();
        var source = new ViewModel { Title = "First" };
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        original.Id = "original";
        var replacement = document.CreateElement("span");
        replacement.Id = "target";
        document.Body!.AppendChild(replacement);
        source.Title = "Second";
        Assert.That(original.TextContent, Is.EqualTo("Second"));
        Assert.That(replacement.TextContent, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DetachedOrAdoptedCachedTargetFailsEvenWhenValueIsUnchanged(bool adopt)
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Original" };
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        target.Remove();
        if (adopt)
        {
            var other = new Document().Implementation.CreateHTMLDocument();
            other.Body!.AppendChild(other.AdoptNode(target));
        }
        var replacement = document.CreateElement("span");
        replacement.Id = "target";
        replacement.TextContent = "Replacement";
        document.Body!.AppendChild(replacement);
        var error = Assert.Throws<BindingException>(() => source.Notify(nameof(ViewModel.Title)))!;
        Assert.Multiple(() =>
        {
            Assert.That(target.TextContent, Is.EqualTo("Original"));
            Assert.That(replacement.TextContent, Is.EqualTo("Replacement"));
        });
    }

    [Test]
    public void GetterDetachingTargetCannotWriteToIt()
    {
        var (document, target) = Fixture();
        target.TextContent = "Authored";
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        var error = Assert.Throws<BindingException>(() => bindings.Text("#target", source, nameof(ViewModel.Title), _ =>
        {
            target.Remove();
            return "Must not appear";
        }))!;
        Assert.That(target.TextContent, Is.EqualTo("Authored"));
        Assert.That(source.SubscriberCount, Is.Zero);
    }

    [TestCase("#missing")]
    [TestCase("[")]
    [TestCase("")]
    public void TargetFailuresIncludeSourceAndTargetContextAndDoNotSubscribe(string selector)
    {
        var (document, _) = Fixture();
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        var error = Assert.Throws<BindingException>(() => bindings.Text(selector, source, nameof(ViewModel.Title), model => model.Title))!;
        Assert.Multiple(() =>
        {
            Assert.That(error.SourceType, Is.EqualTo(typeof(ViewModel)));
            Assert.That(error.SourceProperty, Is.EqualTo(nameof(ViewModel.Title)));
            Assert.That(error.TargetSelector, Is.EqualTo(selector));
            Assert.That(error.TargetProperty, Is.EqualTo("textContent"));
            Assert.That(error.Message, Does.Contain(nameof(ViewModel)).And.Contain(nameof(ViewModel.Title)).And.Contain(selector).And.Contain("textContent"));
            Assert.That(error.InnerException, Is.Not.Null);
            Assert.That(source.SubscriberCount, Is.Zero);
        });
    }

    [TestCase("")]
    [TestCase("two tokens")]
    [TestCase("tab\ttoken")]
    [TestCase("line\ntoken")]
    [TestCase("return\rtoken")]
    [TestCase("form\ftoken")]
    public void InvalidClassTokensFailWithBindingContextBeforeSubscribing(string token)
    {
        var (document, target) = Fixture();
        var source = new ViewModel();
        using var bindings = new DocumentBindings(document);
        var error = Assert.Throws<BindingException>(() => bindings.Class("#target", token, source, nameof(ViewModel.Active), model => model.Active))!;
        Assert.Multiple(() =>
        {
            Assert.That(error.SourceProperty, Is.EqualTo(nameof(ViewModel.Active)));
            Assert.That(error.TargetSelector, Is.EqualTo("#target"));
            Assert.That(error.TargetProperty, Does.StartWith("class"));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(source.SubscriberCount, Is.Zero);
            Assert.That(target.HasAttribute("class"), Is.False);
        });
    }

    [Test]
    public void InitialGetterFailureKeepsOriginalFailureAndDoesNotLeakSubscription()
    {
        var (document, target) = Fixture();
        target.TextContent = "Authored";
        var source = new ViewModel { Title = "Recovery" };
        var failure = new InvalidOperationException("Inventory is unavailable");
        using var bindings = new DocumentBindings(document);
        var error = Assert.Throws<BindingException>(() => bindings.Text("#target", source, nameof(ViewModel.Title), _ => throw failure))!;
        Assert.Multiple(() =>
        {
            Assert.That(error.InnerException, Is.SameAs(failure));
            Assert.That(source.SubscriberCount, Is.Zero);
            Assert.That(target.TextContent, Is.EqualTo("Authored"));
        });
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        Assert.That(target.TextContent, Is.EqualTo("Recovery"));
        Assert.That(source.SubscriberCount, Is.EqualTo(1));
    }

    [Test]
    public void LaterGetterFailureIsReportedToNotifierWithoutReplacingTheLastGoodValue()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Good" };
        var fail = false;
        var failure = new InvalidOperationException("Read failed");
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => fail ? throw failure : model.Title);
        fail = true;
        var error = Assert.Throws<BindingException>(() => source.Title = "Pending")!;
        Assert.That(error.InnerException, Is.SameAs(failure));
        Assert.That(target.TextContent, Is.EqualTo("Good"));
        fail = false;
        source.Notify(nameof(ViewModel.Title));
        Assert.That(target.TextContent, Is.EqualTo("Pending"));
    }

    [Test]
    public void GetterDisposingScopePreventsInitialOrLaterWrites()
    {
        var (document, target) = Fixture();
        target.TextContent = "Authored";
        var source = new ViewModel { Title = "Must not appear" };
        var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model =>
        {
            bindings.Dispose();
            return model.Title;
        });
        Assert.That(bindings.IsDisposed, Is.True);
        Assert.That(source.SubscriberCount, Is.Zero);
        Assert.That(target.TextContent, Is.EqualTo("Authored"));

        var laterBindings = new DocumentBindings(document);
        var disposeOnRead = false;
        laterBindings.Text("#target", source, nameof(ViewModel.Title), model =>
        {
            if (disposeOnRead) laterBindings.Dispose();
            return model.Title;
        });
        disposeOnRead = true;
        source.Title = "Also must not appear";
        Assert.That(target.TextContent, Is.EqualTo("Must not appear"));
        Assert.That(laterBindings.IsDisposed, Is.True);
        Assert.That(source.SubscriberCount, Is.Zero);
    }

    [Test]
    public void ReentrantNotificationDuringGetterAppliesOnlyTheLatestValue()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Initial" };
        using var bindings = new DocumentBindings(document);
        var reenter = false;
        bindings.Text("#target", source, nameof(ViewModel.Title), model =>
        {
            var value = model.Title;
            if (reenter)
            {
                reenter = false;
                model.Title = "Newest";
            }
            return value;
        });
        var observer = Observe(target);
        reenter = true;
        source.Title = "Superseded";
        var records = observer.TakeRecords();
        Assert.That(target.TextContent, Is.EqualTo("Newest"));
        Assert.That(records.Count, Is.EqualTo(1), "The superseded getter result must not be written first");
        observer.Disconnect();
    }

    [Test]
    public void DisposeUnsubscribesAllBindingsBeforeNotificationAndOldDocumentCannotChange()
    {
        var (document, oldTarget) = Fixture();
        var source = new ViewModel { Title = "Old" };
        var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        bindings.Class("#target", "active", source, nameof(ViewModel.Active), model => model.Active);
        var disposedCalls = 0;
        bindings.Disposed += () =>
        {
            disposedCalls++;
            Assert.That(bindings.IsDisposed, Is.True);
            Assert.That(source.SubscriberCount, Is.Zero);
            source.Title = "While disposing";
        };
        bindings.Dispose();
        bindings.Dispose();
        var (newDocument, newTarget) = Fixture();
        using var freshBindings = new DocumentBindings(newDocument);
        freshBindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        source.Title = "New";
        source.Active = true;
        Assert.Multiple(() =>
        {
            Assert.That(disposedCalls, Is.EqualTo(1));
            Assert.That(oldTarget.TextContent, Is.EqualTo("Old"));
            Assert.That(oldTarget.ClassList.Contains("active"), Is.False);
            Assert.That(newTarget.TextContent, Is.EqualTo("New"));
            Assert.That(source.SubscriberCount, Is.EqualTo(1));
        });
        Assert.Throws<ObjectDisposedException>(() => bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title));
    }

    [Test]
    public void DisposedHandlerFailureDoesNotPreventOtherDetachHandlers()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Before" };
        var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => model.Title);
        var detached = false;
        var failure = new InvalidOperationException("Consumer cleanup failed");
        bindings.Disposed += () => throw failure;
        bindings.Disposed += () => detached = true;
        var error = Assert.Throws<AggregateException>(() => bindings.Dispose())!;
        source.Title = "After";
        Assert.That(error.InnerExceptions, Does.Contain(failure));
        Assert.That(detached, Is.True);
        Assert.That(source.SubscriberCount, Is.Zero);
        Assert.That(target.TextContent, Is.EqualTo("Before"));
    }

    [Test]
    public void DisposedScopeAndLongLivedSourceDoNotRetainDocumentOrCapturedGetter()
    {
        var source = new ViewModel();
        var (bindings, retiredDocument) = CreateRetiredScope(source);
        for (var attempt = 0; attempt < 3 && retiredDocument.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.That(retiredDocument.IsAlive, Is.False);
        Assert.That(source.SubscriberCount, Is.Zero);
        GC.KeepAlive(bindings);
        GC.KeepAlive(source);
    }

    [Test]
    public void AlreadyCapturedSourceNotificationCannotReadOrWriteAfterAnotherHandlerDisposesScope()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "Before" };
        var bindings = new DocumentBindings(document);
        var reads = 0;
        source.PropertyChanged += (_, _) => bindings.Dispose();
        bindings.Text("#target", source, nameof(ViewModel.Title), model => { reads++; return model.Title; });
        source.Title = "After";
        Assert.That(target.TextContent, Is.EqualTo("Before"));
        Assert.That(reads, Is.EqualTo(1));
        Assert.That(bindings.IsDisposed, Is.True);
        Assert.That(source.SubscriberCount, Is.EqualTo(1), "Only the independent consumer remains subscribed");
    }

    [Test]
    public void WorkerThreadNotificationFailsBeforeReadingOrChangingDom()
    {
        var (document, target) = Fixture();
        var source = new ViewModel { Title = "UI value" };
        var reads = 0;
        using var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), model => { reads++; return model.Title; });
        var failure = Task.Run(() =>
        {
            try { source.Title = "Worker value"; return null; }
            catch (BindingException error) { return error; }
        }).GetAwaiter().GetResult();
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(target.TextContent, Is.EqualTo("UI value"));
            Assert.That(reads, Is.EqualTo(1));
        });
        source.Notify(nameof(ViewModel.Title));
        Assert.That(target.TextContent, Is.EqualTo("Worker value"));
    }

    private static (Document Document, Element Target) Fixture(string tag = "span")
    {
        var document = new Document().Implementation.CreateHTMLDocument();
        var target = document.CreateElement(tag);
        target.Id = "target";
        document.Body!.AppendChild(target);
        return (document, target);
    }

    private static MutationObserver Observe(Element target)
    {
        var observer = new MutationObserver((_, _) => { });
        observer.Observe(target, new MutationObserverInit { ChildList = true, Attributes = true, CharacterData = true, Subtree = true });
        return observer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DocumentBindings Bindings, WeakReference Document) CreateRetiredScope(ViewModel source)
    {
        var (document, _) = Fixture();
        document.Title = "Retired";
        var bindings = new DocumentBindings(document);
        bindings.Text("#target", source, nameof(ViewModel.Title), _ => document.Title);
        bindings.Disposed += () => document.Title = "Disposed";
        bindings.Dispose();
        return (bindings, new WeakReference(document));
    }

    private sealed class ViewModel : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;
        private string _title = "";
        private bool _active;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        public int SubscriberCount => _propertyChanged?.GetInvocationList().Length ?? 0;
        public string Title { get => _title; set { _title = value; Notify(nameof(Title)); } }
        public bool Active { get => _active; set { _active = value; Notify(nameof(Active)); } }
        public void Notify(string? property) => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
