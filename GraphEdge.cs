using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BlockCanvas {
    public sealed class Edge {
        public Port FromPort { get; }
        public Port ToPort { get; }
        public Node FromNode => FromPort.Owner;
        public Node ToNode => ToPort.Owner;
        public Edge(Port fromPort, Port toPort) { FromPort = fromPort; ToPort = toPort; }
    }

    public sealed class Graph {
        public readonly List<Node> Nodes = new();
        public readonly List<Edge> Edges = new();
        public Graph? Parent;
        public Node? Owner;

        // NEW: per-graph panning offset (world-to-screen translation)
        public PointF ViewOffset = PointF.Empty;
    }
}
