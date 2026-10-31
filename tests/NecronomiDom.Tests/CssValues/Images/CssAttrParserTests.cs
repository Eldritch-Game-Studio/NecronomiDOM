using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Images;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssValues.Images
{
    [TestFixture]
    public sealed class CssAttrParserTests
    {
        [TestCase("attr(data-label)")]
        [TestCase("attr(data-label raw-string)")]
        [TestCase("attr(data-width px, 1em)")]
        [TestCase("attr(data-alpha %, 50%)")]
        [TestCase("attr(data-count number, 1)")]
        [TestCase("attr(data-size type(<length> | auto), auto)")]
        [TestCase("attr(data-colors type(<color>#), red, blue)")]
        [TestCase("attr(data-value type(*),)")]
        [TestCase("attr(data-label type(<string>), \"missing\")")]
        public void AcceptsTypedAttributeGrammar(string input)
        {
            Assert.That(CssAttrParser.TryParse((FunctionValue)CssParser.ParseComponentValue(input)!,out CssValue? value),Is.True);
            Assert.That(CssAttrParser.TryParse((FunctionValue)CssParser.ParseComponentValue(value!.Serialize())!,out _),Is.True);
        }
        [TestCase("attr()")]
        [TestCase("attr(123)")]
        [TestCase("attr(name unknown)")]
        [TestCase("attr(name px px)")]
        [TestCase("attr(name type())")]
        [TestCase("attr(name type(<unknown>))")]
        [TestCase("attr(name type(<length> && <number>))")]
        [TestCase("attr(name type(<length> |))")]
        [TestCase("attr(name type(inherit))")]
        // CSS Values 5 attr security forbids <url>, including quoted syntax and alternatives.
        [TestCase("attr(name type(<url>))")]
        [TestCase("attr(name type(<url> | auto))")]
        [TestCase("attr(name type(\"<url>\"))")]
        [TestCase("attr(name type(<transform-list>+))")]
        [TestCase("attr(data-size type(\"<length> | auto\"), auto)")]
        // Chrome 155 does not ship the quoted syntax-string form.
        [TestCase("attr(data-label type(\"<string>\"), \"missing\")")]
        public void RejectsInvalidAttributeGrammar(string input) => Assert.That(CssAttrParser.TryParse((FunctionValue)CssParser.ParseComponentValue(input)!,out _),Is.False);

        [Test]
        public void RawAttributePreservesTextAndUsesEmptyMissingDefault()
        {
            CssAttrValue value=Parse("attr(data-label)");
            Assert.That(value.TryResolve(_=>"  raw text  ",out CssValue? resolved),Is.True);
            Assert.That(((CssStringValue)resolved!).Value,Is.EqualTo("  raw text  "));
            Assert.That(value.TryResolve(_=>null,out resolved),Is.True);
            Assert.That(((CssStringValue)resolved!).Value,Is.Empty);
        }
        [Test]
        public void UnitAttributesParseNumericTextAndFallbackWhenInvalid()
        {
            CssAttrValue value=Parse("attr(data-width px, 5em)");
            Assert.That(value.TryResolve(_=>"12.5",out CssValue? resolved),Is.True);
            Assert.That(resolved!.Serialize(),Is.EqualTo("12.5px"));
            Assert.That(value.TryResolve(_=>"12px",out resolved),Is.True);
            Assert.That(resolved!.Serialize(),Is.EqualTo("5em"));
            Assert.That(value.IsAttrTainted,Is.True);
        }
        [Test]
        public void RegistrationSyntaxValidatesActualAttributeValue()
        {
            CssAttrValue value=Parse("attr(data-size type(<length> | auto))");
            Assert.That(value.TryResolve(_=>"auto",out CssValue? resolved),Is.True);
            Assert.That(resolved!.Serialize(),Is.EqualTo("auto"));
            Assert.That(value.TryResolve(_=>"red",out _),Is.False);
        }
        [Test]
        public void MissingTypedAttributesWithoutFallbackAreInvalidAtComputedTime()
            => Assert.That(Parse("attr(data-width px)").TryResolve(_=>null,out _),Is.False);
        [TestCase("attr(data-label type(<string>), \"missing\")")]
        public void TypedStringsRequireCssStringTokensRatherThanRawText(string input)
        {
            CssAttrValue value=Parse(input);
            Assert.That(value.TryResolve(_=>"\"quoted text\"",out CssValue? resolved),Is.True);
            Assert.That(((CssStringValue)resolved!).Value,Is.EqualTo("quoted text"));
            Assert.That(value.TryResolve(_=>"unquoted text",out resolved),Is.True);
            Assert.That(resolved!.Serialize(),Is.EqualTo("\"missing\""));
            Assert.That(value.Serialize(),Is.EqualTo(input));
        }
        // Chrome 155 specified-value serialization.
        [Test]
        public void TypedStringFallbackRetainsAuthoredSpecifiedQuoteStyle()
        {
            const string text="attr(data-label type(<string>), 'fallback')";
            Assert.That(Parse(text).Serialize(),Is.EqualTo(text));
        }
        // Chrome 155 resolves the attribute's var() before matching <length>.
        [Test]
        public void AttributeTokenSubstitutionPrecedesTypedGrammarValidation()
        {
            bool called=false;
            var attribute=Parse("attr(data-length type(<length>), 10px)");
            Assert.That(attribute.TryResolve(_=>"var(--length)",out CssValue? value,(name,tokens)=>
            {
                called=true;
                Assert.That(name,Is.EqualTo("data-length"));
                Assert.That(CssTokenSerializer.Serialize(tokens),Is.EqualTo("var(--length)"));
                return CssParser.ParseComponentValueList("5px");
            }),Is.True);
            Assert.That(called,Is.True);
            Assert.That(value!.Serialize(),Is.EqualTo("5px"));
        }
        [Test]
        public void RawStringAttributesNeverInvokeTokenSubstitution()
        {
            bool called=false;
            Assert.That(Parse("attr(data-label)").TryResolve(_=>"var(--label)",out CssValue? value,(_,_)=>{ called=true; return null; }),Is.True);
            Assert.That(called,Is.False);
            Assert.That(((CssStringValue)value!).Value,Is.EqualTo("var(--label)"));
        }
        private static CssAttrValue Parse(string input)
        {
            Assert.That(CssAttrParser.TryParse((FunctionValue)CssParser.ParseComponentValue(input)!,out CssValue? value),Is.True);
            return (CssAttrValue)value!;
        }
    }
}
