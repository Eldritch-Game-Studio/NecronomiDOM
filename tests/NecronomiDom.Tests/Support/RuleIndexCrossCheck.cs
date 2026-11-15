using System;
using NecronomiDom.Css.Cascade;
using NUnit.Framework;

namespace NecronomiDom.Tests;

/// <summary>NECRONOMIDOM_CROSSCHECK_RULE_INDEX=1 runs the whole suite with every rule collection compared against the
/// linear walk; any difference in candidates, source order, layers or registrations fails the test that caused it.</summary>
[SetUpFixture]
public sealed class RuleIndexCrossCheck
{
    [OneTimeSetUp]
    public void Enable() => StyleEngine.CrossCheckRuleIndex = Environment.GetEnvironmentVariable("NECRONOMIDOM_CROSSCHECK_RULE_INDEX") == "1";
}
