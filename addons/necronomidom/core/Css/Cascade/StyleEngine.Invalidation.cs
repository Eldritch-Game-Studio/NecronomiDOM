using System;
using System.Collections.Generic;
using NecronomiDom.Css.Om;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Dom;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>
    /// Subtree style invalidation. A computed style depends only on the element's own attributes and state, its
    /// ancestors and the root, so a DOM change restyles one subtree instead of the whole document: the changed element,
    /// widened to its parent for sibling-dependent selectors, to the owning select for option selectedness, to the nearest
    /// dir=auto/bdi ancestor for :dir(), and to the outermost ancestor whose :hover/:active/:focus-within compound it can
    /// match. Selector features without a sound subtree bound (:has(), form-owner validity/default, unknown
    /// pseudo-classes) and shadow trees restyle everything.
    /// Changes are recorded as they happen and resolved at the next style read, against the then-current selectors.
    /// </summary>
    public sealed partial class StyleEngine
    {
        private enum ChangeKind : byte { Element, InlineStyle, State, Children, Inserted, Removed }
        private readonly record struct PendingChange(Node Node, Node? OldParent, ChangeKind Kind);
        private const ElementPseudoState InTreeStates = ElementPseudoState.Hover | ElementPseudoState.Active | ElementPseudoState.Focus;
        private readonly List<PendingChange> _pendingChanges = new();
        private readonly HashSet<Element> _restyleRoots = new();
        private readonly List<SheetSource> _previousSources = new();
        private readonly List<CompoundSelector> _inTreeCompounds = new();
        private bool _hasShadowTrees, _restyleAllOnChange, _siblingSensitive, _inTreeUnbounded, _directionSensitive;

        /// <summary>Changes a reader has not drained yet; a larger backlog collapses into one full restyle.</summary>
        private const int MaxPendingChanges = 1024;

        private void RecordChange(Node node, ChangeKind kind, Node? oldParent = null)
        {
            // Detached trees hold no current styles that matter; their elements restyle when inserted.
            if (!(oldParent ?? node).IsConnected) return;
            // Repeated writes to one element (style animation, typed OM) record once and stay allocation-free.
            if (_pendingChanges.Count > 0 && _pendingChanges[^1] == new PendingChange(node, oldParent, kind)) return;
            if (_pendingChanges.Count >= MaxPendingChanges) { RestyleAll(); return; }
            _pendingChanges.Add(new PendingChange(node, oldParent, kind));
        }

        private void FlushChanges()
        {
            if (_hasShadowTrees || _restyleAllOnChange) { RestyleAll(); return; }
            bool reuseRules = IncrementalReconciliation && !_geometryStyleSensitive && ValueContext == null && ContainerEnvironment == null;
            foreach (var change in _pendingChanges)
            {
                switch (change.Kind)
                {
                    case ChangeKind.InlineStyle:
                        if (TryInvalidateInlineGeometry((Element)change.Node)) break;
                        if (reuseRules)
                        {
                            // No selector can observe this inline change; inheritance reaches descendants, not siblings.
                            MarkSubtreeStale((Element)change.Node, reuseRules: true);
                            break;
                        }
                        goto case ChangeKind.Element;
                    case ChangeKind.Element:
                        var element = (Element)change.Node;
                        _restyleRoots.Add(SiblingScope(FormScope(element)));
                        AddDirectionScope(element.ParentElement);
                        break;
                    case ChangeKind.State:
                        if (_inTreeUnbounded) { RestyleAll(); return; }
                        _restyleRoots.Add(SiblingScope(FormScope(InTreeScope((Element)change.Node))));
                        break;
                    case ChangeKind.Children:
                        if (change.Node is Element parent) { _restyleRoots.Add(SiblingScope(FormScope(parent))); AddDirectionScope(parent); }
                        // Document, shadow root or adopted-sheet owner; detached fragments restyle when inserted.
                        else if (change.Node.IsConnected) { RestyleAll(); return; }
                        break;
                    case ChangeKind.Inserted:
                        if (change.Node is not Element inserted) break;
                        _restyleRoots.Add(inserted);
                        if (HoldsInTreeState(inserted))
                        {
                            if (_inTreeUnbounded) { RestyleAll(); return; }
                            _restyleRoots.Add(SiblingScope(InTreeScope(inserted)));
                        }
                        break;
                    case ChangeKind.Removed:
                        if (change.Node is not Element removed) break;
                        _restyleRoots.Add(removed);
                        // Former ancestors lose :hover/:active/:focus-within held, or cleared before the removal, inside it.
                        if (change.OldParent is Element oldParent && (HoldsInTreeState(removed) || HasPendingState(removed)))
                        {
                            if (_inTreeUnbounded) { RestyleAll(); return; }
                            _restyleRoots.Add(SiblingScope(InTreeScope(oldParent)));
                        }
                        break;
                }
            }
            _pendingChanges.Clear();
            // Ordinary selector/DOM invalidation dominates overlapping inline scopes, independent of mutation order.
            foreach (var root in _restyleRoots) MarkSubtreeStale(root, reuseRules: false);
            _restyleRoots.Clear();
        }
        private void MarkSubtreeStale(Element root, bool reuseRules)
        {
            foreach (Node node in root.InclusiveDescendants())
                if (node is Element element && _entries.TryGetValue(element, out var entries))
                    entries.MarkStale(reuseRules, _generation);
        }
        private void RestyleAll()
        {
            _generation++;
            _pendingChanges.Clear();
            _restyleRoots.Clear();
        }
        /// <summary>Sibling combinators and :nth-child(of S) reach only the parent's other children and their subtrees.</summary>
        private Element SiblingScope(Element root) => _siblingSensitive && root.ParentElement is { } parent ? parent : root;
        /// <summary>Option selectedness, hence :checked, depends on the owning select's other options.</summary>
        private static Element FormScope(Element root)
        {
            if (root is HTMLOptionElement option) return option.SelectOwner ?? root;
            if (root.LocalName == "optgroup" && root.ParentElement is HTMLSelectElement select) return select;
            return root;
        }
        /// <summary>Outermost inclusive ancestor that can match a :hover/:active/:focus-within compound apart from that state.</summary>
        private Element InTreeScope(Element changed)
        {
            Element root = changed;
            if (_inTreeCompounds.Count == 0) return root;
            for (Element? ancestor = changed; ancestor != null; ancestor = ancestor.ParentElement)
                foreach (var compound in _inTreeCompounds)
                    if (MayMatch(ancestor, compound)) { root = ancestor; break; }
            return root;
        }
        /// <summary>:dir() of a dir=auto or bdi element follows its first strong descendant text, which the change may alter.</summary>
        private void AddDirectionScope(Element? changed)
        {
            if (!_directionSensitive) return;
            for (Element? ancestor = changed; ancestor != null; ancestor = ancestor.ParentElement)
            {
                string? dir = ancestor.GetAttribute("dir");
                if (dir != null ? !dir.Equals("auto", StringComparison.OrdinalIgnoreCase) : ancestor.LocalName != "bdi") continue;
                _restyleRoots.Add(SiblingScope(ancestor));
                return;
            }
        }
        private static bool HoldsInTreeState(Element root)
        {
            foreach (Node node in root.InclusiveDescendants())
                if (node is Element element && (element.PseudoState & InTreeStates) != 0) return true;
            return false;
        }
        private bool HasPendingState(Element removed)
        {
            foreach (var change in _pendingChanges)
                if (change.Kind == ChangeKind.State && removed.Contains(change.Node)) return true;
            return false;
        }
        /// <summary>Superset test over the compound's type, id and class selectors; every other simple selector may match.</summary>
        private static bool MayMatch(Element element, CompoundSelector compound)
        {
            foreach (var simple in compound.SimpleSelectors)
            {
                bool match = simple switch
                {
                    TypeSelector type => string.Equals(element.LocalName, type.Name, StringComparison.OrdinalIgnoreCase),
                    IdSelector id => string.Equals(element.Id, id.Id, StringComparison.OrdinalIgnoreCase),
                    ClassSelector cls => HasClassToken(element.ClassName, cls.ClassName),
                    _ => true,
                };
                if (!match) return false;
            }
            return true;
        }
        private static bool HasClassToken(string classes, string token)
        {
            int start = 0;
            for (int i = 0; i <= classes.Length; i++)
            {
                if (i < classes.Length && classes[i] is not (' ' or '\t' or '\n' or '\r' or '\f')) continue;
                if (classes.AsSpan(start, i - start).Equals(token, StringComparison.OrdinalIgnoreCase)) return true;
                start = i + 1;
            }
            return false;
        }

        /// <summary>Selector features that bound invalidation; rescanned only when the style sources change.</summary>
        private void ScanInvalidationFeatures()
        {
            _restyleAllOnChange = _siblingSensitive = _inTreeUnbounded = _directionSensitive = false;
            _geometryStyleSensitive = false;
            _inTreeCompounds.Clear();
            var visited = new HashSet<CSSStyleSheet>();
            foreach (var source in _sources) ScanSheet(source.Sheet, visited);
        }
        private void ScanSheet(CSSStyleSheet sheet, HashSet<CSSStyleSheet> visited)
        {
            if (visited.Add(sheet)) ScanRules(sheet.CssRules, visited);
        }
        private void ScanRules(CSSRuleList rules, HashSet<CSSStyleSheet> visited)
        {
            foreach (CSSRule rule in rules)
            {
                ScanGeometryRule(rule);
                if (rule is CSSStyleRule style) ScanSelectors(style.Selectors, nested: false);
                else if (rule is CSSScopeRule scope)
                {
                    if (scope.Start != null) ScanSelectors(scope.Start, nested: true);
                    if (scope.End != null) ScanSelectors(scope.End, nested: true);
                }
                else if (rule is CSSImportRule { StyleSheet: { } imported }) ScanSheet(imported, visited);
                if (rule is CSSGroupingRule group) ScanRules(group.CssRules, visited);
            }
        }
        private void ScanSelectors(SelectorList list, bool nested)
        {
            foreach (var complex in list.Selectors)
            {
                if (complex.LeadingCombinator is Combinator.AdjacentSibling or Combinator.GeneralSibling) _siblingSensitive = true;
                foreach (var combinator in complex.Combinators)
                {
                    if (combinator is Combinator.AdjacentSibling or Combinator.GeneralSibling) _siblingSensitive = true;
                    else if (combinator == Combinator.Column) _restyleAllOnChange = true;
                }
                foreach (var compound in complex.Compounds)
                {
                    bool inTree = false;
                    foreach (var simple in compound.SimpleSelectors)
                    {
                        if (simple is AttributeSelector attribute && attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
                            _geometryStyleSensitive = true;
                        if (simple is PseudoElementSelector pseudoElement)
                        {
                            if (pseudoElement.Arguments != null || pseudoElement.Argument != null) _restyleAllOnChange = true;
                            continue;
                        }
                        if (simple is not PseudoClassSelector pseudo) continue;
                        switch (pseudo.Name)
                        {
                            case "hover": case "active": case "focus-within":
                                inTree = true;
                                break;
                            case "is": case "where": case "not":
                                if (pseudo.Arguments != null) ScanSelectors(pseudo.Arguments, nested: true);
                                break;
                            case "dir":
                                _directionSensitive = true;
                                break;
                            case "nth-child": case "nth-last-child":
                                if (pseudo.Nth?.Of is { } of) { _siblingSensitive = true; ScanSelectors(of, nested: true); }
                                break;
                            // Own attributes/state, ancestors, or the parent's child list: all inside the restyled subtree.
                            case "root": case "scope": case "empty": case "first-child": case "last-child": case "only-child":
                            case "first-of-type": case "last-of-type": case "only-of-type": case "nth-of-type": case "nth-last-of-type":
                            case "lang": case "defined": case "state": case "focus": case "focus-visible": case "modal": case "popover-open":
                            case "enabled": case "disabled": case "checked": case "placeholder-shown": case "read-write": case "read-only":
                            case "in-range": case "out-of-range": case "link": case "any-link": case "visited":
                                break;
                            default:
                                _restyleAllOnChange = true;
                                break;
                        }
                    }
                    if (!inTree) continue;
                    if (nested) _inTreeUnbounded = true;
                    else _inTreeCompounds.Add(compound);
                }
            }
        }
    }
}
