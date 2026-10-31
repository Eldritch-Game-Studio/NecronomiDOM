using NecronomiDom.Dom;
using NUnit.Framework;

namespace NecronomiDom.Tests.Dom
{
    [TestFixture]
    public sealed class FormStateTests
    {
        private static Document Html() => new Document().Implementation.CreateHTMLDocument();
        [Test]
        public void InputDirtyValuesDoNotFollowDefaultAttributes()
        {
            var doc = Html(); var input = (HTMLInputElement)doc.CreateElement("input"); input.DefaultValue = "default";
            Assert.That(input.Value, Is.EqualTo("default")); input.Value = "dirty"; input.DefaultValue = "changed"; Assert.That(input.Value, Is.EqualTo("dirty"));
            input.Type = "checkbox"; Assert.That(input.Value, Is.EqualTo("changed")); input.Checked = true; input.DefaultChecked = false; Assert.That(input.Checked, Is.True);
        }
        [Test]
        public void RequiredRadioGroupAndSelectPlaceholderReflectLiveState()
        {
            var doc = Html(); var a = (HTMLInputElement)doc.CreateElement("input"); var b = (HTMLInputElement)doc.CreateElement("input");
            a.Type = b.Type = "radio"; a.Name = b.Name = "group"; a.Required = true; doc.Body!.Append(a, b);
            Assert.That(a.Validity.ValueMissing, Is.True); Assert.That(b.Validity.ValueMissing, Is.True);
            b.Checked = true; Assert.That(a.Validity.Valid, Is.True); a.Checked = true; Assert.That(b.Checked, Is.False);
            var select = (HTMLSelectElement)doc.CreateElement("select"); select.Required = true;
            var placeholder = (HTMLOptionElement)doc.CreateElement("option"); placeholder.Value = "";
            var option = (HTMLOptionElement)doc.CreateElement("option"); option.Value = "chosen"; select.Append(placeholder, option);
            Assert.That(select.Value, Is.EqualTo("")); Assert.That(select.Validity.ValueMissing, Is.True);
            select.Value = "chosen"; Assert.That(option.Selected, Is.True); Assert.That(select.Validity.Valid, Is.True);
        }
        [Test]
        public void NumericSanitizationBoundsAndStepUseActualValue()
        {
            var doc = Html(); var input = (HTMLInputElement)doc.CreateElement("input"); input.Type = "number";
            input.SetAttribute("min", "2"); input.SetAttribute("max", "10"); input.SetAttribute("step", "2");
            input.Value = "3"; Assert.That(input.Validity.StepMismatch, Is.True);
            input.Value = "1"; Assert.That(input.Validity.RangeUnderflow, Is.True);
            input.Value = "12"; Assert.That(input.Validity.RangeOverflow, Is.True);
            input.Value = "not-number"; Assert.That(input.Value, Is.Empty); Assert.That(input.Validity.BadInput, Is.False);
            input.SetUserValue("not-number"); Assert.That(input.Validity.BadInput, Is.True);
            input.Value = "4"; Assert.That(input.Validity.Valid, Is.True);
            input.Type = "range"; input.Value = "100"; Assert.That(input.Value, Is.EqualTo("10"));
        }
        [Test]
        public void TextLengthConstraintsDistinguishUserAndProgrammaticWrites()
        {
            var doc = Html(); var input = (HTMLInputElement)doc.CreateElement("input"); input.SetAttribute("minlength", "3"); input.SetAttribute("maxlength", "5"); input.SetAttribute("pattern", "[a-z]+");
            input.Value = "a"; Assert.That(input.Validity.TooShort, Is.False);
            input.SetUserValue("a"); Assert.That(input.Validity.TooShort, Is.True);
            input.SetUserValue("abcdef"); Assert.That(input.Validity.TooLong, Is.True);
            input.Value = "123"; Assert.That(input.Validity.PatternMismatch, Is.True);
            var area = (HTMLTextAreaElement)doc.CreateElement("textarea"); area.SetAttribute("minlength", "3"); area.SetUserValue("a"); Assert.That(area.Validity.TooShort, Is.True);
        }
        [Test]
        public void DisabledFieldsetLegendExceptionAndCustomErrorsAreRespected()
        {
            var doc = Html(); var fieldset = doc.CreateElement("fieldset"); fieldset.SetAttribute("disabled", "");
            var legend = doc.CreateElement("legend"); var exempt = (HTMLInputElement)doc.CreateElement("input"); legend.AppendChild(exempt);
            var input = (HTMLInputElement)doc.CreateElement("input"); fieldset.Append(legend, input); doc.Body!.AppendChild(fieldset);
            Assert.That(exempt.WillValidate, Is.True); Assert.That(input.WillValidate, Is.False);
            exempt.SetCustomValidity("message"); Assert.That(exempt.Validity.CustomError, Is.True); Assert.That(exempt.CheckValidity(), Is.False);
            exempt.SetCustomValidity(""); Assert.That(exempt.Validity.Valid, Is.True);
        }
        [Test]
        public void RangeRoundingStaysStepAlignedWithinBoundsAndUsesAttributeValueBasis()
        {
            var input = (HTMLInputElement)Html().CreateElement("input"); input.Type = "range";
            input.SetAttribute("max", "8"); input.SetAttribute("step", "3"); input.Value = "8";
            Assert.That(input.Value, Is.EqualTo("6")); Assert.That(input.Validity.StepMismatch, Is.False);
            input.RemoveAttribute("max"); input.SetAttribute("step", "2"); input.DefaultValue = "1"; input.Value = "2";
            Assert.That(input.Value, Is.EqualTo("3")); Assert.That(input.Validity.StepMismatch, Is.False);
        }
    }
}
