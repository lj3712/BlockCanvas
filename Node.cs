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
    public enum NodeType {
        Regular,
        Start,
        End,
        Const,
        Add,
        Decision,
        Marshaller,
        NullConsumer
    }
    public sealed class Node {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; }
        public PointF Position;
        public SizeF Size = new SizeF(190, 98);

        public readonly List<Port> Inputs = new();
        public readonly List<Port> Outputs = new();

        public Graph? Inner;
        public bool IsProxy = false;
        public bool ProxyIsInlet = false;
        public int ProxyIndex = 0;
        public float TitleH = 28f; // dynamic title bar height (min 28)
        public NodeType Type { get; set; } = NodeType.Regular;
        public bool IsPermanent = false; // Cannot be deleted
        public string ConstValue { get; set; } = "0"; // Value for CONST blocks
        public string? MarshallerOutputType { get; set; } = null; // User-defined type name for marshaller output



        public Node(string title, PointF pos, bool createDefaultPorts = true, NodeType nodeType = NodeType.Regular) {
            Title = title;
            Position = pos;
            Type = nodeType;
            if (createDefaultPorts) {
                // Configure ports based on node type
                if (Type == NodeType.Start) {
                    // START blocks have infinite output ports, start with one - outputs single bits
                    Outputs.Add(new Port(this, PortSide.Output, "Out1", 1));
                } else if (Type == NodeType.End) {
                    // END blocks have infinite input ports, start with one - can accept any length
                    Inputs.Add(new Port(this, PortSide.Input, "In1", TypeUtil.AnyLength));
                } else if (Type == NodeType.Const) {
                    // CONST blocks need a trigger and output configurable bit length (default 1)
                    Inputs.Add(new Port(this, PortSide.Input, "Trigger", TypeUtil.AnyLength));
                    Outputs.Add(new Port(this, PortSide.Output, "Value", 1));
                } else if (Type == NodeType.Add) {
                    // ADD blocks take two 32-bit numbers and output 32-bit sum plus carry and overflow bits
                    Inputs.Add(new Port(this, PortSide.Input, "A", 32));
                    Inputs.Add(new Port(this, PortSide.Input, "B", 32));
                    Outputs.Add(new Port(this, PortSide.Output, "Sum", 32));
                    Outputs.Add(new Port(this, PortSide.Output, "Carry", 1));
                    Outputs.Add(new Port(this, PortSide.Output, "Overflow", 1));
                } else if (Type == NodeType.Decision) {
                    // Decision blocks take multiple bits (number) and output single bits
                    Inputs.Add(new Port(this, PortSide.Input, "Input", 8)); // Default 8-bit input
                    Outputs.Add(new Port(this, PortSide.Output, "FALSE", 1));
                    Outputs.Add(new Port(this, PortSide.Output, "TRUE", 1));
                } else if (Type == NodeType.Marshaller) {
                    // Marshaller starts with two inputs and one output for construction
                    // Can be reconfigured for deconstruction (one input, multiple outputs)
                    Inputs.Add(new Port(this, PortSide.Input, "In1", 1));
                    Inputs.Add(new Port(this, PortSide.Input, "In2", 1));
                    Outputs.Add(new Port(this, PortSide.Output, "Out", 2)); // Combined bit length
                } else if (Type == NodeType.NullConsumer) {
                    // Null consumer accepts any length input but produces no outputs
                    Inputs.Add(new Port(this, PortSide.Input, "In", TypeUtil.AnyLength));
                    // No outputs - this block just consumes impetuses
                } else {
                    // Regular nodes get default single-bit ports
                    Inputs.Add(new Port(this, PortSide.Input, "In", 1));
                    Outputs.Add(new Port(this, PortSide.Output, "Out", 1));
                }
            }
            
            // Set special sizes for primitive blocks
            if (Type == NodeType.NullConsumer) {
                Size = new SizeF(120, 24); // Thin horizontal bar
            } else if (Type == NodeType.Marshaller) {
                Size = new SizeF(80, 160); // Vertical rectangular oval shape
            }
        }

        public RectangleF Bounds => new RectangleF(Position, Size);

        // Methods for managing infinite ports on START and END blocks
        public void AddOutputPortIfNeeded() {
            if (Type == NodeType.Start) {
                string newName = $"Out{Outputs.Count + 1}";
                Outputs.Add(new Port(this, PortSide.Output, newName, 1));
            }
        }

        public void AddInputPortIfNeeded() {
            if (Type == NodeType.End) {
                string newName = $"In{Inputs.Count + 1}";
                Inputs.Add(new Port(this, PortSide.Input, newName, TypeUtil.AnyLength));
            }
        }



        public void LayoutPorts() {
            if (IsProxy) return;

            float gap = 16f;
            float titleH = TitleH <= 0 ? 28f : TitleH; // use dynamic title height
            float topPad = 10f, botPad = 10f;
            float bodyTop = Position.Y + titleH + topPad;
            float bodyBottom = Position.Y + Size.Height - botPad;
            float bodyH = Math.Max(1f, bodyBottom - bodyTop);

            int inCount = Inputs.Count;
            int outCount = Outputs.Count;

            float YAtSlot(int i, int n) {
                if (n <= 1) return bodyTop + bodyH * 0.5f;
                float t = (i + 0.5f) / n;
                return bodyTop + t * bodyH;
            }

            for (int i = 0; i < inCount; i++) {
                var p = Inputs[i];
                float y = YAtSlot(i, inCount);
                p.VisualSize = new SizeF(p.CustomWidth, p.VisualSize.Height); // Use custom width
                var r = new RectangleF(Position.X - gap - p.VisualSize.Width, y - p.VisualSize.Height / 2f, p.VisualSize.Width, p.VisualSize.Height);
                p.VisualRect = r;
                p.ConnectionPoint = new PointF(r.Left - p.ArrowWidth, y);
            }
            for (int i = 0; i < outCount; i++) {
                var p = Outputs[i];
                float y = YAtSlot(i, outCount);
                p.VisualSize = new SizeF(p.CustomWidth, p.VisualSize.Height); // Use custom width
                var r = new RectangleF(Position.X + Size.Width + gap, y - p.VisualSize.Height / 2f, p.VisualSize.Width, p.VisualSize.Height);
                p.VisualRect = r;
                p.ConnectionPoint = new PointF(r.Right + p.ArrowWidth, y);
            }
        }


        public bool HitTitleBar(PointF p) => !IsProxy && new RectangleF(Position.X, Position.Y, Size.Width, 28).Contains(p);
        public bool HitBody(PointF p) => !IsProxy && Bounds.Contains(p);
        public Port? HitPort(PointF p) {
            foreach (var port in Inputs.Concat(Outputs))
                if (port.HitTest(p)) return port;
            return null;
        }
    }
}
