using System;
using Opc.Ua;

namespace DWSIM.H2sim.Io
{
    /// <summary>Resolves configured NodeId strings against the session namespace table.</summary>
    public static class OpcUaNodeResolver
    {
        /// <summary>
        /// Accepts a full NodeId ("ns=2;s=..." / "ns=2;i=...") or a bare string id that is
        /// combined with the namespace index of <paramref name="namespaceUri"/>.
        /// </summary>
        public static NodeId Resolve(string nodeId, string namespaceUri, StringTable namespaceUris)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("NodeId is empty.", nameof(nodeId));

            if (nodeId.StartsWith("ns=", StringComparison.OrdinalIgnoreCase))
                return NodeId.Parse(nodeId);

            ushort index = 0;
            if (!string.IsNullOrEmpty(namespaceUri) && namespaceUris != null)
            {
                var found = namespaceUris.GetIndex(namespaceUri);
                if (found >= 0) index = (ushort)found;
            }

            return new NodeId(nodeId, index);
        }
    }
}