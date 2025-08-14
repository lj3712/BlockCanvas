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
    public sealed class Port {
        public Node Owner { get; }
        public PortSide Side { get; }
        public string Name { get; set; }
        public string TypeName { get; set; } = "Integer";

        public RectangleF VisualRect;
        public float ArrowWidth = 12f;
        public float CornerRadius = 6f;
        public SizeF VisualSize = new SizeF(108, 26);
        public PointF ConnectionPoint;

        public Port(Node owner, PortSide side, string name, string typeName = "Integer") { Owner = owner; Side = side; Name = name; TypeName = TypeUtil.Normalize(typeName); }


        public RectangleF HitRect {
            get {
                var r = VisualRect;
                if (Side == PortSide.Input) r = RectangleF.FromLTRB(r.Left - ArrowWidth, r.Top, r.Right, r.Bottom);
                if (Side == PortSide.Output) r = RectangleF.FromLTRB(r.Left, r.Top, r.Right + ArrowWidth, r.Bottom);
                return r;
            }
        }
        public bool HitTest(PointF p) {
            if (HitRect.Contains(p)) return true;
            float dx = p.X - ConnectionPoint.X, dy = p.Y - ConnectionPoint.Y;
            return dx * dx + dy * dy <= 100;
        }
    }
}
