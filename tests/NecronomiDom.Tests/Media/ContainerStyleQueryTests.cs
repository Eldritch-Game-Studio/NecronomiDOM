using System.Collections.Generic;
using NecronomiDom.Css.Media;
using NUnit.Framework;

namespace NecronomiDom.Tests.Media
{
    public sealed class ContainerStyleQueryTests
    {
        [TestCase("style(--theme: dark)", true)]
        [TestCase("style(--theme: light)", false)]
        [TestCase("style(--theme)", true)]
        [TestCase("style(--missing)", false)]
        [TestCase("style((--theme) and (--density: compact))", true)]
        [TestCase("style((--theme: light) or (--density: compact))", true)]
        [TestCase("style(not (--theme: light))", true)]
        [TestCase("style((--missing) and (--theme: dark))", false)]
        [TestCase("style((--empty))", true)]
        [TestCase("style(--label: \"quoted\")", true)]
        [TestCase("style(--number: 1.0)", true)]
        [TestCase("style(--theme: DARK)", false)]
        public void ParsedStyleConditionsUseActualContainerComputedValues(string text, bool expected)
        {
            var values = new Dictionary<string, string> { ["--theme"] = "dark", ["--density"] = "compact", ["--empty"] = "", ["--label"] = "'quoted'", ["--number"] = "1" };
            var environment = new MediaEnvironment { ContainerStyle = name => values.GetValueOrDefault(name) };
            var query = ContainerQueryParser.Parse(text);
            Assert.That(query, Is.Not.Null);
            Assert.That(MediaEvaluator.Evaluate(query!.Condition, environment), Is.EqualTo(expected));
        }
    }
}
