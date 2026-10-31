namespace AngleSharp.Dom
{
    using System;
    using AngleSharp.Text;

    /// <summary>
    /// Represents a processing instruction node.
    /// </summary>
    sealed class ProcessingInstruction : CharacterData, IProcessingInstruction
    {
        #region ctor

        internal ProcessingInstruction(Document owner, String name)
            : base(owner, name, NodeType.ProcessingInstruction)
        {
        }

        #endregion

        #region Properties

        public String Target => NodeName;

        #endregion

        #region Methods

        public override Node Clone(Document owner, Boolean deep)
        {
            var node = new ProcessingInstruction(owner, Target);
            CloneNode(node, owner, deep);
            return node;
        }

        /// <summary>
        /// Creates a processing instruction by splitting data into the name/target and data.
        /// </summary>
        internal static ProcessingInstruction Create(Document owner, String data)
        {
            var nameLength = 0;
            while (nameLength < data.Length && !data[nameLength].IsSpaceCharacter()) nameLength++;
            var dataStart = nameLength;
            while (dataStart < data.Length && data[dataStart].IsSpaceCharacter()) dataStart++;
            var pi = new ProcessingInstruction(owner, data.Substring(0, nameLength));

            if (dataStart < data.Length)
            {
                pi.Data = data.Substring(dataStart);
            }

            return pi;
        }

        #endregion
    }
}
