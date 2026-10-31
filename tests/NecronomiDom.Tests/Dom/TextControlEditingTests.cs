using System;
using System.Collections.Generic;
using NecronomiDom.Css.Cascade;
using NecronomiDom.Dom;
using NecronomiDom.Html;
using NecronomiDom.Paint;
using NecronomiDom.Url;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom;

[TestFixture]
public sealed class TextControlEditingTests
{
    private static (Document Doc, HTMLElement Element, DocumentInput Input) Open(string markup)
    {
        var doc = HtmlImporter.Import("<!doctype html>" + markup, "editing", WebUrl.Parse("res:///editing.html")!).Document;
        var element = (HTMLElement)doc.GetElementById("field")!;
        var display = new DisplayList(); display.Boxes.Add(new LayoutBox(element, new Rect(0, 0, 300, 60), true));
        var input = new DocumentInput(doc, new StyleEngine(doc)); input.UpdateGeometry(display, 320, 100);
        element.Focus(); return (doc, element, input);
    }
    private static bool Key(DocumentInput input, string key, bool shift = false, bool ctrl = false, bool meta = false, string? text = null) =>
        input.Key(new KeyboardEvent("keydown", new KeyboardEventInit { Bubbles = true, Cancelable = true, Composed = true,
            Key = key, ShiftKey = shift, CtrlKey = ctrl, MetaKey = meta }), text);

    [Test]
    public void InsertionReplacementAndBothDeleteDirectionsRespectMaxlength()
    {
        var (_, element, input) = Open("<input id=field maxlength=5 value=12345>"); using (input)
        {
            var field = (HTMLInputElement)element; field.SetSelectionRange(5, 5);
            Key(input, "6"); Assert.That(field.Value, Is.EqualTo("12345"));
            field.SetSelectionRange(1, 4); Key(input, "9"); Assert.That(field.Value, Is.EqualTo("195"));
            Assert.That(field.SelectionStart, Is.EqualTo(2));
            Key(input, "Backspace"); Assert.That(field.Value, Is.EqualTo("15"));
            Key(input, "Delete"); Assert.That(field.Value, Is.EqualTo("1"));
            Key(input, "Home"); Key(input, "Backspace"); Assert.That(field.Value, Is.EqualTo("1"));
            Key(input, "End"); Key(input, "Delete"); Assert.That(field.Value, Is.EqualTo("1"));
        }
    }
    [Test]
    public void ShiftSelectionTracksAnchorAndSelectAllWorksForControlAndMeta()
    {
        var (_, element, input) = Open("<input id=field value=12345>"); using (input)
        {
            var field = (HTMLInputElement)element; field.SetSelectionRange(3, 3);
            Key(input, "ArrowLeft", true); Key(input, "ArrowLeft", true);
            Assert.That((field.SelectionStart, field.SelectionEnd, field.SelectionDirection), Is.EqualTo((1, 3, "backward")));
            Key(input, "ArrowRight", true); Key(input, "ArrowRight", true); Key(input, "ArrowRight", true);
            Assert.That((field.SelectionStart, field.SelectionEnd, field.SelectionDirection), Is.EqualTo((3, 4, "forward")));
            Key(input, "ArrowLeft"); Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((3, 3)));
            Key(input, "a", ctrl: true); Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((0, 5)));
            Key(input, "9"); Key(input, "a", meta: true); Key(input, "Backspace"); Assert.That(field.Value, Is.Empty);
        }
    }
    [Test]
    public void SelectionIdlClampsAndUnsupportedInputTypesReturnNullAndThrowOnWrites()
    {
        var doc = new Document().Implementation.CreateHTMLDocument(); var field = (HTMLInputElement)doc.CreateElement("input"); field.Value = "123";
        field.SetSelectionRange(2, 99, "backward"); Assert.That((field.SelectionStart, field.SelectionEnd, field.SelectionDirection), Is.EqualTo((2, 3, "backward")));
        field.SetSelectionRange(3, 1, "invalid"); Assert.That((field.SelectionStart, field.SelectionEnd, field.SelectionDirection), Is.EqualTo((1, 1, "none")));
        field.SelectionStart = 3; Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((3, 3)));
        field.SelectionEnd = 1; Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((1, 1)));
        field.SetSelectionRange(-1, -1); Assert.That((field.SelectionStart, field.SelectionEnd), Is.EqualTo((3, 3)), "Web IDL unsigned offsets wrap before clamping.");
        field.Type = "number"; Assert.That(field.SelectionStart, Is.Null); Assert.That(field.SelectionEnd, Is.Null); Assert.That(field.SelectionDirection, Is.Null);
        Assert.That(Assert.Throws<DomException>(() => field.SetSelectionRange(0, 1))!.Name, Is.EqualTo("InvalidStateError"));
        Assert.Throws<DomException>(() => field.SelectionStart = 0);
        field.Type = "tel"; Assert.That(field.Type, Is.EqualTo("tel")); Assert.DoesNotThrow(() => field.SetSelectionRange(0, 0));
    }
    [Test]
    public void CanceledKeydownAndBeforeinputDoNotMutateOrEmitInput()
    {
        var (_, element, input) = Open("<input id=field>"); using (input)
        {
            var events = new List<string>();
            element.AddEventListener("keydown", new DelegateEventListener(e => { if (((KeyboardEvent)e).Key == "x") e.PreventDefault(); }));
            element.AddEventListener("beforeinput", new DelegateEventListener(e => { events.Add(e.Type); if (((InputEvent)e).Data == "y") e.PreventDefault(); }));
            element.AddEventListener("input", new DelegateEventListener(e => events.Add(e.Type)));
            Key(input, "x"); Key(input, "y"); Assert.That(((HTMLInputElement)element).Value, Is.Empty);
            Assert.That(events, Is.EqualTo(new[] { "beforeinput" }));
            Key(input, "z"); Assert.That(events, Is.EqualTo(new[] { "beforeinput", "beforeinput", "input" }));
        }
    }
    [Test]
    public void CommitAndBlurDispatchChangeOnceAndProgrammaticValueDoesNot()
    {
        var (doc, element, input) = Open("<input id=field>"); using (input)
        {
            var events = new List<string>();
            foreach (string type in new[] { "beforeinput", "input", "change", "blur" })
                element.AddEventListener(type, new DelegateEventListener(e => events.Add(e.Type)));
            Key(input, "1"); Key(input, "Enter"); Key(input, "Enter"); element.Blur();
            Assert.That(events, Is.EqualTo(new[] { "beforeinput", "input", "change", "blur" }));
            element.Focus(); ((HTMLInputElement)element).Value = "2"; element.Blur(); Assert.That(events.FindAll(e => e == "change"), Has.Count.EqualTo(1));
            element.Focus(); Key(input, "3"); doc.Focus(null); Assert.That(events.FindAll(e => e == "change"), Has.Count.EqualTo(2));
        }
    }
    [Test]
    public void ReadonlyDisabledInertAndFocusRedirectionPreventEditing()
    {
        var (doc, element, input) = Open("<input id=field readonly><button id=other>next</button>"); using (input)
        {
            var field = (HTMLInputElement)element; Key(input, "1"); Assert.That(field.Value, Is.Empty);
            field.ReadOnly = false; field.Disabled = true; Key(input, "2"); Assert.That(field.Value, Is.Empty);
            field.Disabled = false; field.Focus(); field.SetAttribute("inert", ""); Key(input, "3"); Assert.That(field.Value, Is.Empty);
            field.RemoveAttribute("inert"); field.Focus();
            field.AddEventListener("beforeinput", new DelegateEventListener(_ => doc.Focus(null)));
            Key(input, "4"); Assert.That(field.Value, Is.Empty);
        }
    }
    [Test]
    public void TextareaNewlineUnicodeGraphemesAndUtf16MaxlengthRemainCoherent()
    {
        var (_, element, input) = Open("<textarea id=field maxlength=6></textarea>"); using (input)
        {
            var area = (HTMLTextAreaElement)element;
            Key(input, "😀"); Assert.That(area.SelectionStart, Is.EqualTo(2));
            Key(input, "Enter"); Key(input, "e"); Key(input, "\u0301");
            Assert.That(area.Value, Is.EqualTo("😀\ne\u0301"));
            Key(input, "Backspace"); Assert.That(area.Value, Is.EqualTo("😀\n"));
            Key(input, "Home"); Assert.That(area.SelectionStart, Is.EqualTo(3));
            Key(input, "Backspace"); Key(input, "ArrowLeft"); Assert.That(area.SelectionStart, Is.Zero);
            Key(input, "Delete"); Assert.That(area.Value, Is.Empty);
            area.Value = "12345"; Key(input, "😀"); Assert.That(area.Value, Is.EqualTo("12345"));
            area.Value = "a\r\nb\rc"; Assert.That(area.Value, Is.EqualTo("a\nb\nc"));
        }
    }
    [Test]
    public void NativeUnicodeRoutingDoesNotInsertLogicalFallbackAndSelectEventsAreQueued()
    {
        var (doc, element, input) = Open("<input id=field value=12>"); using (input)
        {
            var field = (HTMLInputElement)element; int selects = 0;
            field.AddEventListener("select", new DelegateEventListener(_ => selects++));
            Key(input, "a", text: ""); Assert.That(field.Value, Is.EqualTo("12"));
            field.SetSelectionRange(0, 1); Assert.That(selects, Is.Zero); doc.DispatchTextSelectionEvents(); Assert.That(selects, Is.EqualTo(1));
            Key(input, "Unidentified", text: "é"); Assert.That(field.Value, Is.EqualTo("é2"));
        }
    }
    [Test]
    public void DigitsFilterUsesRealInputListenerAndSurvivesCommit()
    {
        var (_, element, input) = Open("<input id=field maxlength=5>"); using (input)
        {
            var field = (HTMLInputElement)element; string committed = "";
            field.AddEventListener("input", new DelegateEventListener(_ => field.Value = string.Concat(System.Linq.Enumerable.Where(field.Value, c => c is >= '0' and <= '9'))));
            field.AddEventListener("change", new DelegateEventListener(_ => committed = field.Value));
            foreach (char c in "12x3456") Key(input, c.ToString());
            Assert.That(field.Value, Is.EqualTo("12345")); Key(input, "Enter"); Assert.That(committed, Is.EqualTo("12345"));
        }
    }
    [Test]
    public void UserTypeSanitizationRemovesInvalidNumberAndUrlWhitespace()
    {
        var (_, element, input) = Open("<input id=field type=number>"); using (input)
        {
            var field = (HTMLInputElement)element; field.SetAttribute("maxlength", "0");
            Key(input, "1"); Assert.That(field.Value, Is.EqualTo("1"), "maxlength does not apply to number inputs.");
            Key(input, "x"); Assert.That(field.Value, Is.Empty);
            field.Type = "url"; field.Value = " \r\nhttps://example.test \t"; Assert.That(field.Value, Is.EqualTo("https://example.test"));
        }
    }
    [Test]
    public void BeforeinputCarriesIntentBeforeApplyingMaxlengthDefault()
    {
        var (_, element, input) = Open("<input id=field maxlength=5>"); using (input)
        {
            var field = (HTMLInputElement)element; field.Value = "12345"; int before = 0, inputs = 0;
            field.AddEventListener("beforeinput", new DelegateEventListener(e =>
            {
                Assert.That(((InputEvent)e).Data, Is.EqualTo(before++ == 0 ? "x" : "y"));
                field.SetAttribute("maxlength", "6");
            }));
            field.AddEventListener("input", new DelegateEventListener(_ => inputs++));
            Key(input, "x"); Key(input, "y");
            Assert.That(field.Value, Is.EqualTo("12345x")); Assert.That(before, Is.EqualTo(2)); Assert.That(inputs, Is.EqualTo(1));
        }
    }
    [Test]
    public void BeforeinputReentrancyRechecksValueRangeMaxlengthAndDisposal()
    {
        var (_, element, input) = Open("<input id=field maxlength=5>"); using (input)
        {
            var field = (HTMLInputElement)element; int inputs = 0; int phase = 0;
            field.AddEventListener("input", new DelegateEventListener(_ => inputs++));
            field.AddEventListener("beforeinput", new DelegateEventListener(_ =>
            {
                if (phase == 0) { field.Value = "12345"; field.SetSelectionRange(0, 1); }
                else if (phase == 1) field.SetAttribute("maxlength", "0");
                else input.Dispose();
            }));
            Key(input, "x"); Assert.That(field.Value, Is.EqualTo("x2345")); Assert.That(inputs, Is.EqualTo(1));
            phase = 1; field.Value = "1234"; Key(input, "y"); Assert.That(field.Value, Is.EqualTo("1234"));
            Assert.That(inputs, Is.EqualTo(1));
            phase = 2; field.RemoveAttribute("maxlength"); Key(input, "z"); Assert.That(field.Value, Is.EqualTo("1234"));
        }
    }
}
