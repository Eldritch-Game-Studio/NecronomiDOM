using System.Collections.Generic;
using System.Linq;
using NecronomiDom.Css.Syntax;
using NUnit.Framework;

namespace NecronomiDom.Tests.CssSyntax
{
    [TestFixture]
    public sealed class CssSyntaxTests
    {
        [TestCase("a\r\nb\rc\fd\0", "a\nb\nc\nd\uFFFD")]
        [TestCase("\uD83D\uDE00", "\uD83D\uDE00")]
        public void PreprocessingUsesSyntaxCodePoints(string source, string expected) => Assert.That(CssTokenizer.Preprocess(source), Is.EqualTo(expected));

        [Test]
        public void PreprocessingReplacesUnpairedUtf16Surrogates()
        {
            // Custom-attribute SerString metadata is UTF-8, so lone UTF-16 surrogates cannot
            // survive TestCase argument decoding. Construct those code units at runtime.
            var source = new string(new[] { (char)0xD800, 'x', (char)0xDC00 });
            Assert.That(CssTokenizer.Preprocess(source), Is.EqualTo("\uFFFDx\uFFFD"));
        }

        [TestCase("\\31 23", CssTokenKind.Ident, "123")]
        [TestCase("#id", CssTokenKind.Hash, "id")]
        [TestCase("@m\\65 dia", CssTokenKind.AtKeyword, "media")]
        [TestCase("url(a\\ b)", CssTokenKind.Url, "a b")]
        [TestCase("'a\\\nb'", CssTokenKind.String, "ab")]
        [TestCase("\\0", CssTokenKind.Ident, "\uFFFD")]
        [TestCase("\\110000", CssTokenKind.Ident, "\uFFFD")]
        public void TokenValuesFollowEscapeAlgorithms(string source, CssTokenKind kind, string value)
        { var token = CssTokenizer.Tokenize(source).Single(); Assert.That(token.Kind, Is.EqualTo(kind)); Assert.That(token.Value, Is.EqualTo(value)); }

        [Test]
        public void NumbersRetainTypeRepresentationAndSign()
        {
            var tokens = CssTokenizer.Tokenize("+01 -1.5e2px 10% .25").Where(t => t.Kind != CssTokenKind.Whitespace).ToArray();
            Assert.That(tokens[0].Number, Is.EqualTo(1));
            Assert.That(tokens[0].Representation, Is.EqualTo("+01"));
            Assert.That(tokens[0].SignCharacter, Is.EqualTo('+'));
            Assert.That(tokens[0].NumberType, Is.EqualTo(CssNumberType.Integer));
            Assert.That(tokens[1].Number, Is.EqualTo(-150));
            Assert.That(tokens[1].Unit, Is.EqualTo("px"));
            Assert.That(tokens[1].NumberType, Is.EqualTo(CssNumberType.Number));
            Assert.That(tokens[2].Kind, Is.EqualTo(CssTokenKind.Percentage));
            Assert.That(tokens[3].Number, Is.EqualTo(.25));
        }
        [Test]
        public void CommentsPreserveTokenBoundariesAndPositions()
        {
            var tokens = CssTokenizer.Tokenize("a/**/b\r\n c");
            Assert.That(tokens[0].Value, Is.EqualTo("a"));
            Assert.That(tokens[1].Value, Is.EqualTo("b"));
            Assert.That(tokens[3].Line, Is.EqualTo(2));
            Assert.That(tokens[3].Column, Is.EqualTo(2));
            Assert.That(CssTokenSerializer.Serialize(CssParser.ParseComponentValueList("a/**/b")), Is.EqualTo("a/**/b"));
        }
        [TestCase("/* unterminated")]
        [TestCase("url(a b)")]
        [TestCase("'bad\nstring")]
        [TestCase("\\\n")]
        public void MalformedTokenizerInputProducesWarnings(string source)
        { var diagnostics = new List<WebDiagnostic>(); CssTokenizer.Tokenize(source, diagnostics, "bad.css"); Assert.That(diagnostics, Is.Not.Empty); Assert.That(diagnostics[0].File, Is.EqualTo("bad.css")); }

        [Test]
        public void ComponentParserRecoversUnclosedFunctionsAndBlocks()
        {
            var diagnostics = new List<WebDiagnostic>();
            var values = CssParser.ParseComponentValueList("f([x", diagnostics, "input.css");
            Assert.That(values.Single(), Is.TypeOf<FunctionValue>());
            Assert.That(CssTokenSerializer.Serialize(values), Is.EqualTo("f([x])"));
            Assert.That(diagnostics.Count, Is.EqualTo(2));
        }
        [Test]
        public void StylesheetsIgnoreHtmlCommentTokensOnlyAtTopLevel()
        {
            var rules = CssParser.ParseStylesheet("<!-- @media screen {a {color:red}} --> b {}");
            Assert.That(rules.Count, Is.EqualTo(2));
            Assert.That(rules[0], Is.TypeOf<RawAtRule>());
            Assert.That(CssParser.ParseRule("a {} b {}"), Is.Null);
            Assert.That(CssParser.ParseRule("@unknown x"), Is.TypeOf<RawAtRule>());
        }
        [Test]
        public void NestedRulesAndDeclarationsKeepSourceOrder()
        {
            var contents = CssParser.ParseBlockContents("color:red; &:hover {color:blue} color:green; @media (width > 1px) {color: black} --tokens:{x:y};");
            Assert.That(contents.Items.Select(i => i.GetType().Name), Is.EqualTo(new[] { "RawDeclaration", "RawQualifiedRule", "RawDeclaration", "RawAtRule", "RawDeclaration" }));
            Assert.That(((RawDeclaration)contents.Items[4]).Value.Single(), Is.TypeOf<SimpleBlock>());
        }
        [TestCase("color : red ! IMPORTANT", true)]
        [TestCase("--Case: !important", true)]
        [TestCase("color: f(!important)", false)]
        public void ImportanceOnlyRecognizesTopLevelTrailingTokens(string source, bool important) => Assert.That(CssParser.ParseDeclaration(source)!.Important, Is.EqualTo(important));
        [TestCase("color red")]
        [TestCase(":red")]
        [TestCase("color:{bad}")]
        [TestCase("color:red; width:1px")]
        public void InvalidSingleDeclarationsAreRejected(string source) => Assert.That(CssParser.ParseDeclaration(source), Is.Null);
        [Test]
        public void DeclarationRecoveryDoesNotLoseFollowingRules()
        {
            var contents = CssParser.ParseBlockContents("junk; color:red; [bad] { color:green } width: 2px");
            Assert.That(contents.Items.Count, Is.EqualTo(3));
            Assert.That(((RawDeclaration)contents.Items[0]).Name, Is.EqualTo("color"));
            Assert.That(((RawDeclaration)contents.Items[2]).Name, Is.EqualTo("width"));
        }
        [Test]
        public void CommaListsDoNotSplitNestedFunctions()
        { var values = CssParser.ParseCommaSeparatedComponentValues("a, f(b,c),"); Assert.That(values.Count, Is.EqualTo(3)); Assert.That(CssParser.Trim(values[2]), Is.Empty); }
    }
}
