using System;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Cascade 5 § 6.4.3 ordered layer tree: a layer's implicit final sublayer follows all named children.</summary>
    internal sealed class CascadeLayers
    {
        internal sealed class Layer
        {
            internal readonly Dictionary<string, Layer> Named = new(StringComparer.Ordinal);
            internal readonly List<Layer> Children = new();
            internal int Order;
        }
        private readonly Layer _root = new();
        internal Layer Root => _root;
        internal Layer Register(string name, Layer? parent = null)
        {
            parent ??= _root;
            if (name.Length == 0)
            {
                var anonymous = new Layer();
                parent.Children.Add(anonymous);
                return anonymous;
            }
            foreach (ComponentValue token in CssParser.ParseComponentValueList(name))
            {
                if (token is not PreservedToken { Token.Kind: CssTokenKind.Ident } identifier) continue;
                string segment = identifier.Token.Value;
                if (!parent.Named.TryGetValue(segment, out var child))
                {
                    child = new Layer();
                    parent.Named.Add(segment, child);
                    parent.Children.Add(child);
                }
                parent = child;
            }
            return parent;
        }
        internal void AssignOrder()
        {
            int order = 0;
            Assign(_root, ref order);
            _root.Order = int.MaxValue;
        }
        private static void Assign(Layer parent, ref int order)
        {
            foreach (Layer child in parent.Children) Assign(child, ref order);
            parent.Order = order++;
        }
    }
}
